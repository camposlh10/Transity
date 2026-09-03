"""
Prepares the character models for upload to the Mixamo auto-rigger.

Mixamo wants a bare mesh: one object, no skeleton, T-pose, upright, front-facing, with
textures embedded so the preview is recognisable. It rigs what you give it, so anything
extra in the file -- an existing armature especially -- either confuses the rigger or is
silently baked into the result.

What this does to each model:
  * deletes any armature and the armature modifier bound to it
  * deletes stray empties (the exporters leave a "_materials" null behind)
  * joins and applies, so one mesh with clean transforms goes up
  * scales to 1.8 m with the feet on the origin, matching the rest of the project
  * embeds the packed textures in the FBX

Run:
  blender -b --factory-startup --python Tools/blender/export_mixamo.py -- <out dir> [blend ...]
"""

import bpy
import os
import sys
from mathutils import Vector

TARGET_HEIGHT = 1.8

DEFAULT_SOURCES = [
    r"C:\Users\campo\OneDrive\Documents\trinity\character\Adventurer_Girl_3D_Model.blend",
    r"C:\Users\campo\OneDrive\Documents\trinity\character\adventurer_character_3d_model.blend",
    r"C:\Users\campo\OneDrive\Documents\trinity\character\stylized_mercenary_3d_model.blend",
]

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if argv else r"C:\Users\campo\OneDrive\Documents\trinity\character\Mixamo_Upload"
SOURCES = argv[1:] if len(argv) > 1 else DEFAULT_SOURCES

os.makedirs(OUT_DIR, exist_ok=True)


def bounds(objs):
    mins = [1e18] * 3
    maxs = [-1e18] * 3
    for o in objs:
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            for i in range(3):
                mins[i] = min(mins[i], w[i])
                maxs[i] = max(maxs[i], w[i])
    return Vector(mins), Vector(maxs)


def report_facing(armature):
    """
    Which way the character looks, read off the foot bones.

    Mixamo needs the model facing the viewer. Rather than trust that the source happens to
    be oriented conventionally, the toes are the giveaway: they point the way the character
    faces. Only the rigged model can be measured, but all three came out of the same
    generator at the same size, so it settles the set.
    """
    if armature is None:
        return None

    for name in ("L_Foot", "R_Foot", "L_Toe", "R_Toe"):
        bone = armature.data.bones.get(name)
        if bone is None:
            continue

        direction = (bone.tail_local - bone.head_local)
        if abs(direction.y) < 1e-4:
            continue

        return "-Y (front-facing for Mixamo)" if direction.y < 0 else "+Y (BACKWARDS -- needs a 180 turn)"

    return None


def strip_to_bare_mesh():
    """Removes everything Mixamo should not see, and returns the single remaining mesh."""
    # Armature modifiers first: removing the object while a modifier still points at it
    # leaves the mesh in its bind pose evaluation with a dangling reference.
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for modifier in list(obj.modifiers):
            if modifier.type == "ARMATURE":
                obj.modifiers.remove(modifier)

    for obj in list(bpy.data.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        return None

    # Vertex groups are the old skeleton's weights. Mixamo makes its own, and leaving
    # groups named after a different rig is a reliable way to get a confusing result.
    for obj in meshes:
        obj.vertex_groups.clear()
        obj.parent = None

    if len(meshes) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
        meshes = [o for o in bpy.data.objects if o.type == "MESH"]

    return meshes[0]


results = []

for source in SOURCES:
    name = os.path.splitext(os.path.basename(source))[0]
    print("=" * 74)
    print("PREPARE:", name)

    record = {"name": name, "ok": False}

    try:
        bpy.ops.wm.open_mainfile(filepath=source)

        armature = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
        record["had_armature"] = armature is not None
        facing = report_facing(armature)
        if facing:
            record["facing"] = facing
            print("  facing:", facing)

        mesh = strip_to_bare_mesh()
        if mesh is None:
            raise RuntimeError("no mesh left after stripping")

        bpy.context.view_layer.objects.active = mesh
        bpy.ops.object.select_all(action="DESELECT")
        mesh.select_set(True)

        # ---- scale and place --------------------------------------------
        bpy.context.view_layer.update()
        mn, mx = bounds([mesh])
        height = mx.z - mn.z
        if height <= 0:
            raise RuntimeError("model has no height")

        scale = TARGET_HEIGHT / height
        mesh.scale = (mesh.scale.x * scale, mesh.scale.y * scale, mesh.scale.z * scale)
        bpy.context.view_layer.update()

        mn, mx = bounds([mesh])
        mesh.location = mesh.location + Vector((
            -(mn.x + mx.x) * 0.5,
            -(mn.y + mx.y) * 0.5,
            -mn.z))
        bpy.context.view_layer.update()

        # Baked in, so the FBX carries no leftover transform for Mixamo to interpret.
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

        mn, mx = bounds([mesh])
        size = mx - mn
        record["size"] = [round(v, 3) for v in size]
        record["feet_z"] = round(mn.z, 4)
        record["tris"] = sum(max(len(p.vertices) - 2, 0) for p in mesh.data.polygons)
        record["verts"] = len(mesh.data.vertices)

        # ---- export ------------------------------------------------------
        path = os.path.join(OUT_DIR, name + "_Mixamo.fbx")
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=True,
            global_scale=1.0,
            apply_scale_options="FBX_SCALE_NONE",
            axis_forward="-Z",
            axis_up="Y",
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            add_leaf_bones=False,
            bake_anim=False,
            # Mixamo shows the model while you place its markers; without the textures it
            # is a grey blob and the joints are much harder to place accurately.
            path_mode="COPY",
            embed_textures=True,
        )

        record["file"] = os.path.basename(path)
        record["bytes"] = os.path.getsize(path)
        record["ok"] = True

        print("  %d verts, %d tris, %.2f x %.2f x %.2f m, feet at %.3f"
              % (record["verts"], record["tris"], size.x, size.y, size.z, mn.z))
        print("  wrote %s (%.1f MB)" % (record["file"], record["bytes"] / 1048576))

    except Exception as exc:
        record["error"] = "%s: %s" % (type(exc).__name__, exc)
        print("  FAILED:", record["error"])

    results.append(record)

print("=" * 74)
ok = sum(1 for r in results if r["ok"])
print("DONE  %d/%d ready for Mixamo -> %s" % (ok, len(results), OUT_DIR))
for r in results:
    if not r["ok"]:
        print("  FAILED", r["name"], r.get("error"))
