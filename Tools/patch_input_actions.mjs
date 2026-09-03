// Adds the combat and equipment actions to the shared InputActionAsset.
//
// The asset is JSON, but Unity regenerates ids on import and rejects duplicates, so every
// new action and binding gets a fresh GUID. Existing actions are left alone except the two
// slot-cycle actions, which move from 1/2 to the mouse wheel to free the number row for
// direct slot selection.
import fs from "fs";
import path from "path";
import crypto from "crypto";
import { fileURLToPath } from "url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FILE = path.resolve(HERE, "../Assets/_Game/Input/InputSystem_Actions.inputactions");

const asset = JSON.parse(fs.readFileSync(FILE, "utf8"));
const map = asset.maps.find(m => m.name === "Player");
if (!map) throw new Error("No Player action map");

const uuid = () => crypto.randomUUID();

function ensureAction(name, type = "Button", control = "Button") {
  let action = map.actions.find(a => a.name === name);
  if (!action) {
    action = {
      name, type, id: uuid(), expectedControlType: control,
      processors: "", interactions: "", initialStateCheck: type !== "Button",
    };
    map.actions.push(action);
    console.log("action +", name);
  }
  return action;
}

function bind(actionName, controlPath, groups = "") {
  const exists = map.bindings.some(b => b.action === actionName && b.path === controlPath);
  if (exists) return;
  map.bindings.push({
    name: "", id: uuid(), path: controlPath, interactions: "", processors: "",
    groups, action: actionName, isComposite: false, isPartOfComposite: false,
  });
  console.log("bind  +", actionName, "<-", controlPath);
}

function unbind(actionName, controlPath) {
  const before = map.bindings.length;
  for (let i = map.bindings.length - 1; i >= 0; i--) {
    const b = map.bindings[i];
    if (b.action === actionName && b.path === controlPath) map.bindings.splice(i, 1);
  }
  if (map.bindings.length !== before) console.log("bind  -", actionName, "<-", controlPath);
}

// ---- slot selection -------------------------------------------------------
unbind("Previous", "<Keyboard>/1");
unbind("Next", "<Keyboard>/2");
bind("Previous", "<Mouse>/scroll/down", "Keyboard&Mouse");
bind("Next", "<Mouse>/scroll/up", "Keyboard&Mouse");

for (let i = 1; i <= 4; i++) {
  ensureAction(`Slot${i}`);
  bind(`Slot${i}`, `<Keyboard>/${i}`, "Keyboard&Mouse");
}

// ---- combat ---------------------------------------------------------------
ensureAction("Aim");
bind("Aim", "<Mouse>/rightButton", "Keyboard&Mouse");
bind("Aim", "<Gamepad>/leftTrigger", "Gamepad");

ensureAction("Reload");
bind("Reload", "<Keyboard>/r", "Keyboard&Mouse");
bind("Reload", "<Gamepad>/buttonWest", "Gamepad");

// Attack already exists on LMB / gamepad west; gamepad west now reloads, so move
// gamepad fire to the right trigger where it belongs.
unbind("Attack", "<Gamepad>/buttonWest");
bind("Attack", "<Gamepad>/rightTrigger", "Gamepad");

// ---- equipment ------------------------------------------------------------
ensureAction("Flashlight");
bind("Flashlight", "<Keyboard>/f", "Keyboard&Mouse");
bind("Flashlight", "<Gamepad>/dpad/up", "Gamepad");

ensureAction("Drop");
bind("Drop", "<Keyboard>/g", "Keyboard&Mouse");
bind("Drop", "<Gamepad>/dpad/down", "Gamepad");

ensureAction("Scoreboard");
bind("Scoreboard", "<Keyboard>/tab", "Keyboard&Mouse");
bind("Scoreboard", "<Gamepad>/select", "Gamepad");

fs.writeFileSync(FILE, JSON.stringify(asset, null, 4) + "\n");
console.log("actions now:", map.actions.map(a => a.name).join(", "));
