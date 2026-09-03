// Type-checks the project's assemblies with Unity's own Roslyn, without opening Unity.
//
// Unity writes the exact compile arguments for every assembly to a .rsp file under
// Library/Bee/artifacts. Reusing them gives a real compile against the real references,
// which is the only way to be sure while the editor has the project locked. Output goes
// to a scratch path so Unity's own DLLs are never touched.
//
// Usage: node Tools/typecheck.mjs [assembly ...]
import fs from "fs";
import path from "path";
import os from "os";
import { execFileSync } from "child_process";
import { fileURLToPath } from "url";

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const DAG = path.join(REPO, "Library/Bee/artifacts");
const CSC = "C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll";
const DOTNET = "C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/NetCoreRuntime/dotnet.exe";

const assemblies = process.argv.slice(2).length
  ? process.argv.slice(2)
  : ["Transity.Runtime", "Transity.Editor", "Transity.Tests.EditMode"];

// The .dag directory name changes between Unity sessions; find whichever holds the rsp files.
function findRsp(assembly) {
  for (const dir of fs.readdirSync(DAG)) {
    const candidate = path.join(DAG, dir, `${assembly}.rsp`);
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

/// Folder holding the .asmdef that defines this assembly.
function findAsmdefFolder(assembly) {
  const roots = [path.join(REPO, "Assets")];
  while (roots.length) {
    const dir = roots.pop();
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        roots.push(full);
      } else if (entry.name === `${assembly}.asmdef`) {
        return dir;
      }
    }
  }
  return null;
}

function* walkCs(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      // A nested .asmdef marks a different assembly; its files are not ours.
      if (fs.readdirSync(full).some(f => f.endsWith(".asmdef"))) continue;
      yield* walkCs(full);
    } else if (entry.name.endsWith(".cs")) {
      yield full;
    }
  }
}

const outDir = fs.mkdtempSync(path.join(os.tmpdir(), "transity-typecheck-"));
let failed = 0;

for (const assembly of assemblies) {
  const rsp = findRsp(assembly);
  if (!rsp) {
    console.error(`no .rsp for ${assembly} -- open Unity once so it writes one`);
    failed++;
    continue;
  }

  // Redirect the outputs; everything else (defines, references, sources) is Unity's.
  const original = fs.readFileSync(rsp, "utf8").split(/\r?\n/);

  // The .rsp lists the source files as of Unity's last compile, so anything added since
  // is missing. Re-scan the assembly's own folder and add what the list does not have --
  // without this, a brand new file silently fails to be type-checked at all.
  const listed = new Set(original
    .filter(l => l.trim().startsWith('"') && l.trim().toLowerCase().endsWith('.cs"'))
    .map(l => path.resolve(REPO, l.trim().slice(1, -1)).toLowerCase()));

  const asmdef = findAsmdefFolder(assembly);
  const extra = [];
  if (asmdef) {
    for (const file of walkCs(asmdef)) {
      if (!listed.has(file.toLowerCase())) {
        extra.push(`"${path.relative(REPO, file).replace(/\\/g, "/")}"`);
      }
    }
  }

  // Same staleness problem for references: an asmdef reference added since Unity's last
  // compile is not in the .rsp, and the assembly would fail to resolve types it now
  // legitimately has access to. Resolve those against Library/ScriptAssemblies.
  const referenced = new Set(original
    .filter(l => l.startsWith("-r:"))
    .map(l => path.basename(l.slice(3).replace(/"/g, "")).replace(/\.(ref\.)?dll$/, "").toLowerCase()));

  if (asmdef) {
    const definition = JSON.parse(fs.readFileSync(path.join(asmdef, `${assembly}.asmdef`), "utf8"));
    for (const reference of definition.references ?? []) {
      // GUID-form references are already resolved in the rsp; only names are checkable.
      if (reference.startsWith("GUID:") || referenced.has(reference.toLowerCase())) continue;

      const dll = path.join(REPO, "Library/ScriptAssemblies", `${reference}.dll`);
      if (fs.existsSync(dll)) {
        extra.push(`-r:"${dll.replace(/\\/g, "/")}"`);
      } else {
        console.log(`\n   (cannot resolve reference '${reference}' -- open Unity once)`);
      }
    }
  }

  if (extra.length) {
    console.log(`(+${extra.length} new file${extra.length === 1 ? "" : "s"}/reference${extra.length === 1 ? "" : "s"})`);
  }

  const args = [...original, ...extra].map(line => {
    if (line.startsWith("-out:")) return `-out:"${path.join(outDir, assembly + ".dll").replace(/\\/g, "/")}"`;
    if (line.startsWith("-refout:")) return `-refout:"${path.join(outDir, assembly + ".ref.dll").replace(/\\/g, "/")}"`;
    // Reference the freshly built assemblies rather than Unity's stale ones, so an error
    // in Runtime surfaces in Editor too instead of hiding behind an old DLL. Unity
    // references the lightweight .ref.dll, which only exists once we have built it.
    if (line.startsWith("-r:")) {
      for (const built of assemblies) {
        if (built === assembly) continue;
        for (const suffix of [".ref.dll", ".dll"]) {
          if (!line.includes(`/${built}${suffix}`)) continue;
          const fresh = path.join(outDir, built + suffix);
          const fallback = path.join(outDir, built + ".dll");
          const use = fs.existsSync(fresh) ? fresh : fallback;
          if (fs.existsSync(use)) return `-r:"${use.replace(/\\/g, "/")}"`;
        }
      }
    }

    return line;
  }).join("\n");

  const localRsp = path.join(outDir, `${assembly}.rsp`);
  fs.writeFileSync(localRsp, args);

  process.stdout.write(`${assembly} ... `);
  try {
    execFileSync(DOTNET, [CSC, "-nologo", `@${localRsp}`], { cwd: REPO, stdio: "pipe" });
    console.log("ok");
  } catch (e) {
    console.log("FAILED");
    const output = (e.stdout?.toString() ?? "") + (e.stderr?.toString() ?? "");
    const errors = output.split(/\r?\n/).filter(l => /error [A-Z]+\d+/.test(l));
    const seen = new Set();
    for (const line of errors) {
      const trimmed = line.replace(/^.*[\\/]Assets[\\/]/, "Assets/").trim();
      if (!seen.has(trimmed)) {
        seen.add(trimmed);
        console.log("   " + trimmed);
      }
    }
    if (errors.length === 0) console.log(output.split(/\r?\n/).slice(0, 20).join("\n"));
    failed++;
  }
}

fs.rmSync(outDir, { recursive: true, force: true });
process.exit(failed ? 1 : 0);
