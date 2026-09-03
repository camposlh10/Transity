"""
Runs every Hunter Depot equipment generator headlessly and exports Unity-ready FBX.

The packs ship as Blender generator scripts rather than meshes, so the mesh only exists
after the script runs. Each pack is built in its own fresh scene to keep one pack's
materials and objects from leaking into the next, then exported with the settings the
pack READMEs specify.

The HD_COL_* object each generator makes is a wire-display cube carrying the collider
spec; it is excluded from the export and the spec is written into the report instead, so
Unity can build a real collider component rather than importing a redundant mesh.
"""

import bpy
import json
import os
import sys
import hashlib
import traceback
from pathlib import Path
from mathutils import Vector

PACKS_DIR = Path(sys.argv[sys.argv.index("--") + 1])
OUT_DIR = Path(sys.argv[sys.argv.index("--") + 2])
TEX_DIR = OUT_DIR / "Textures"
REPORT = Path(sys.argv[sys.argv.index("--") + 3])

OUT_DIR.mkdir(parents=True, exist_ok=True)
TEX_DIR.mkdir(parents=True, exist_ok=True)

collection_manifest = json.loads(
    (PACKS_DIR.parent / "HD_EquipmentCollection_Manifest.json").read_text(encoding="utf-8"))


def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_stats(objs):
    """
    Counts triangles after modifiers, because every part carries a bevel modifier and the
    raw mesh data would under-report by roughly the factor the bevel adds.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    tris = 0
    mins = [1e18] * 3
    maxs = [-1e18] * 3
    counted = False
    for o in objs:
        if o.type != "MESH":
            continue
        evaluated = o.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        tris += sum(max(len(p.vertices) - 2, 0) for p in mesh.polygons)
        evaluated.to_mesh_clear()
        counted = True
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            for i in range(3):
                mins[i] = min(mins[i], w[i])
                maxs[i] = max(maxs[i], w[i])
    if not counted:
        return 0, [0, 0, 0], [0, 0, 0]
    return tris, [maxs[i] - mins[i] for i in range(3)], mins


def digest(path):
    return hashlib.md5(Path(path).read_bytes()).hexdigest()


results = []
atlas_by_hash = {}

for entry in collection_manifest["assets"]:
    pascal = entry["pascal_name"]
    pack = PACKS_DIR / ("HD_%s_BlenderPack" % pascal)
    generator = pack / ("HD_%s_Generator.py" % pascal)

    print("=" * 72)
    print("BUILD:", pascal)

    record = {
        "pascal_name": pascal,
        "asset_key": entry["asset_key"],
        "display_name": entry["display_name"],
        "slot": entry["slot"],
        "tradeoff": entry["tradeoff"],
        "ok": False,
    }

    try:
        wipe()

        pack_manifest = json.loads(
            (pack / ("HD_%s_Manifest.json" % pascal)).read_text(encoding="utf-8"))
        spec = pack_manifest["spec"]
        record["dimensions_m"] = spec["dimensions_m"]
        record["collider"] = spec["collider"]
        record["pivot"] = spec.get("pivot", "")
        record["emissive_required"] = bool(spec.get("emissive_required", False))
        record["texture_resolution"] = spec["texture_resolution"]

        # Run the generator as if it were the main script, with __file__ pointing at the
        # pack so its own texture lookup (which resolves "textures/" beside __file__)
        # finds the atlas without needing TEXTURE_DIRECTORY_OVERRIDE.
        # 20 of the 27 generators carry JSON booleans in their SPEC dict ("emissive": true)
        # rather than Python ones, which is a NameError the moment the module is executed.
        # Binding the two names is enough to run the packs unmodified -- preferable to
        # rewriting vendor source, which would have to be redone on every asset drop.
        namespace = {
            "__name__": "__main__",
            "__file__": str(generator),
            "true": True,
            "false": False,
        }
        exec(compile(generator.read_text(encoding="utf-8"), str(generator), "exec"), namespace)

        root_name = pack_manifest["asset"]["root_name"]
        root = bpy.data.objects.get(root_name)
        if root is None:
            raise RuntimeError("generator produced no root named " + root_name)

        exportable = [root]
        colliders = []
        for o in bpy.data.objects:
            if o is root:
                continue
            if o.name.startswith("HD_COL_"):
                colliders.append(o)
                continue
            if o.type in {"MESH", "EMPTY"}:
                exportable.append(o)

        meshes = [o for o in exportable if o.type == "MESH"]
        if not meshes:
            raise RuntimeError("generator produced no mesh objects")

        tris, dims, mins = mesh_stats(meshes)
        record["triangles"] = tris
        record["measured_size"] = [round(v, 4) for v in dims]
        record["measured_min"] = [round(v, 4) for v in mins]
        record["parts"] = len(meshes)
        record["materials"] = sorted({
            slot.material.name for o in meshes for slot in o.material_slots if slot.material})

        # Collider dimensions come from the wire cube the generator built, falling back to
        # the manifest spec if it is absent.
        if colliders:
            ctris, cdims, cmins = mesh_stats(colliders)
            record["collider_size"] = [round(v, 4) for v in cdims]
            record["collider_center"] = [
                round(cmins[i] + cdims[i] * 0.5, 4) for i in range(3)]
        else:
            record["collider_size"] = [round(v, 4) for v in spec["dimensions_m"]]
            record["collider_center"] = [0.0, 0.0, 0.0]

        # ---- export ------------------------------------------------------
        for o in bpy.data.objects:
            o.select_set(False)
        for o in exportable:
            o.select_set(True)
        bpy.context.view_layer.objects.active = root

        fbx = OUT_DIR / ("HD_%s.fbx" % pascal)
        bpy.ops.export_scene.fbx(
            filepath=str(fbx),
            use_selection=True,
            apply_unit_scale=True,
            global_scale=1.0,
            apply_scale_options="FBX_SCALE_NONE",
            bake_space_transform=True,          # README: "Apply Transform"
            axis_forward="-Z",
            axis_up="Y",
            object_types={"MESH", "EMPTY"},
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            bake_anim=False,
            mesh_smooth_type="FACE",
            path_mode="STRIP",
        )
        record["fbx"] = fbx.name
        record["fbx_bytes"] = fbx.stat().st_size

        # ---- texture -----------------------------------------------------
        atlas = pack / "textures" / ("HD_%s_Atlas_Albedo.png" % pascal)
        if atlas.is_file():
            h = digest(atlas)
            if h in atlas_by_hash:
                # Several packs ship byte-identical atlases; one copy serves them all.
                record["atlas"] = atlas_by_hash[h]
                record["atlas_shared"] = True
            else:
                dest = TEX_DIR / atlas.name
                dest.write_bytes(atlas.read_bytes())
                atlas_by_hash[h] = dest.name
                record["atlas"] = dest.name
                record["atlas_shared"] = False
        else:
            record["atlas"] = None

        record["ok"] = True
        print("  tris=%d parts=%d size=%s atlas=%s"
              % (tris, len(meshes), record["measured_size"], record["atlas"]))

    except Exception as exc:
        record["error"] = "%s: %s" % (type(exc).__name__, exc)
        print("  FAILED:", record["error"])
        traceback.print_exc()

    results.append(record)

REPORT.write_text(json.dumps(results, indent=2), encoding="utf-8")

ok = sum(1 for r in results if r["ok"])
print("=" * 72)
print("DONE  %d/%d exported" % (ok, len(results)))
for r in results:
    if not r["ok"]:
        print("  FAILED", r["pascal_name"], r.get("error"))
