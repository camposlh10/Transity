// Cross-checks every field name passed to GrayboxKit.Wire against the fields that
// actually exist on the target type.
//
// Wire logs an error and carries on when a name is wrong, so a typo produces a component
// that looks built and is silently unwired -- invisible until something is null at
// runtime, in generated content nobody reads. The compiler cannot catch it because the
// names are strings.
//
// Usage: node Tools/check-wires.mjs
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const SRC = [path.join(REPO, "Assets/_Game/Scripts"), path.join(REPO, "Assets/_Game/Editor")];

function* walk(dir) {
  if (!fs.existsSync(dir)) return;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(full);
    else if (entry.name.endsWith(".cs")) yield full;
  }
}

const files = [...SRC.flatMap(root => [...walk(root)])];

// --- every type's own fields, plus who it inherits from ----------------------
const own = new Map();      // type -> Set(field)
const baseOf = new Map();   // type -> base type name

for (const file of files) {
  const text = fs.readFileSync(file, "utf8");

  // Type declarations, with an optional base list.
  const types = [];
  const typeRe = /\b(?:class|struct)\s+([A-Za-z_]\w*)(?:\s*:\s*([^{]+))?/g;
  let m;
  while ((m = typeRe.exec(text))) {
    const name = m[1];
    types.push({ name, at: m.index });
    if (!own.has(name)) own.set(name, new Set());
    if (m[2]) {
      // First entry in a base list is the class (interfaces follow, and start with I).
      const first = m[2].split(",")[0].trim().split("<")[0].trim();
      if (first && !/^I[A-Z]/.test(first)) baseOf.set(name, first);
    }
  }

  // [SerializeField] in any attribute combination, or a plain public field.
  const fieldRe =
    /(?:\[SerializeField[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:private\s+|internal\s+)?|\bpublic\s+(?!class|struct|enum|interface|static|override|virtual|abstract|sealed|readonly\s+static))(?:readonly\s+)?[A-Za-z_][\w.]*(?:<[^>=;(]*>)?(?:\[\])?\s+([a-z_]\w*)\s*(?:=[^;]*)?;/g;

  while ((m = fieldRe.exec(text))) {
    let owner = null;
    for (const t of types) if (t.at < m.index) owner = t.name;
    if (owner) own.get(owner).add(m[1]);
  }
}

/// A type's fields including everything it inherits.
function fieldsOf(type, seen = new Set()) {
  if (!type || seen.has(type)) return new Set();
  seen.add(type);
  const result = new Set(own.get(type) ?? []);
  for (const inherited of fieldsOf(baseOf.get(type), seen)) result.add(inherited);
  return result;
}

// --- check every Wire call ---------------------------------------------------
let problems = 0;
let checked = 0;
let skipped = 0;

for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  if (!text.includes("Wire(")) continue;

  // Local variable -> type, from the ways generated content gets its components.
  const varType = new Map();
  const patterns = [
    /\bvar\s+(\w+)\s*=\s*[\w.]*(?:AddComponent|GetComponent|GetComponentInChildren)<([\w.]+)>/g,
    /\bvar\s+(\w+)\s*=\s*[\w.]*LoadAssetAtPath<([\w.]+)>/g,
    /\bvar\s+(\w+)\s*=\s*ScriptableObject\.CreateInstance<([\w.]+)>/g,
    /\b([\w.]+)\s+(\w+)\s*=\s*\w+\.AddComponent<[\w.]+>/g,
  ];

  for (const [i, re] of patterns.entries()) {
    let m;
    while ((m = re.exec(text))) {
      const [name, type] = i === 3 ? [m[2], m[1]] : [m[1], m[2]];
      varType.set(name, type.split(".").pop());
    }
  }

  // TryGetComponent<T>(out var x)
  const tryRe = /TryGetComponent<([\w.]+)>\s*\(\s*out\s+var\s+(\w+)\s*\)/g;
  let t;
  while ((t = tryRe.exec(text))) varType.set(t[2], t[1].split(".").pop());

  const wireRe = /GrayboxKit\.Wire\(\s*(\w+)\s*,([\s\S]*?)\);/g;
  let m;
  while ((m = wireRe.exec(text))) {
    let type = varType.get(m[1]);
    const names = [...m[2].matchAll(/\(\s*"(\w+)"/g)].map(x => x[1]);

    // When the target's type cannot be read from the declaration (it came out of a helper
    // method, say), fall back to identifying it by its fields: if exactly one type in the
    // codebase has all of these names, that is unambiguously the one being wired.
    if (!type || !own.has(type)) {
      const candidates = [...own.keys()].filter(candidate => {
        const fields = fieldsOf(candidate);
        return names.length > 0 && names.every(n => fields.has(n));
      });

      if (candidates.length !== 1) {
        skipped += names.length;
        continue;
      }

      type = candidates[0];
    }

    const known = fieldsOf(type);
    for (const name of names) {
      checked++;
      if (!known.has(name)) {
        const line = text.slice(0, m.index).split("\n").length;
        console.log(`${path.relative(REPO, file).replace(/\\/g, "/")}:${line}  ` +
                    `${type} has no serialized field '${name}'`);
        problems++;
      }
    }
  }
}

console.log(`\nchecked ${checked} wired field name(s)` +
            (skipped ? `, skipped ${skipped} with an untracked target type` : "") +
            `, ${problems} problem(s)`);
process.exit(problems ? 1 : 0);
