---
name: unity-cli-automation
description: Drive the running Unity Editor or run headless tests/builds via Unity CLI commands.
globs: "**/*.cs, **/*.unity, Assets/**"
---

# Unity CLI Automation Workflows

This skill provides direct command patterns to control the connected Unity Editor and run background operations using the official `unity` CLI tool.

> For project architecture, network setup, and component hierarchy details, refer to [.agents/context_handoff.md](../../context_handoff.md).

---

## 1. Quick Verification & Liveness Check

Always verify that an Editor is attached and ready before firing mutation commands:

```bash
# Check Editor connection status and active port
unity status --json

# Check if the Editor is compiling or in Play Mode
unity command editor_status --json
```

**Key Response Fields:**
- `data.instances[0].state == "ready"`: Editor is idle and accepting commands.
- `compiling: true` or `domainReloadInProgress: true`: Wait a few seconds for assembly reload to finish before issuing commands.

---

## 2. Inspecting the Scene & Components (Non-destructive)

You can inspect the hierarchy, search objects, and read serialized component properties without altering the scene:

```bash
# Get the full hierarchy tree of the currently active scene
unity command get_scene_hierarchy --json

# Read component properties (e.g. Rigidbody, NetworkObject) by instanceId or hierarchyPath
unity command get_component_properties --target <instanceId> --type Rigidbody --json

# Get the last 20 Unity Console logs (helpful after compiling or testing)
unity command get_console_logs --limit 20 --json

# Clear the Unity Editor console
unity command clear_console --json
```

---

## 3. Modifying GameObjects & Components

Modify scene objects with atomic Editor Undo support:

```bash
# Create a new primitive or empty GameObject
unity command create_gameobject --name "Obstacle" --primitive Cube --json

# Set Transform (position, rotation, scale)
unity command set_transform --target <instanceId> --position [0, 1.5, 0] --json

# Add a component (e.g. Rigidbody or custom NetworkBehaviour)
unity command add_component --target <instanceId> --type Rigidbody --json

# Reparent a GameObject
unity command set_parent --target <instanceId> --parent <parentInstanceId> --json

# Save the active scene
unity command save_scene --json
```

---

## 4. Play Mode & Visual Inspection (Screenshot)

Test runtime behaviors and capture visual snapshots:

```bash
# Enter Play Mode
unity command editor_play --json

# Pause or Stop Play Mode
unity command editor_pause --json
unity command editor_stop --json

# Capture current Game View or Scene View as PNG
unity command screenshot --view Game --output "C:/Users/gezof/game_view.png" --width 1920 --height 1080 --json
```

---

## 5. Live C# Execution (`eval`)

Run one-line C# expressions directly in the live Editor process (useful for inspecting private fields or testing NGO components):

```bash
# Inspect NetworkManager state
unity command eval 'Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.IsListening.ToString() : "Not initialized";'

# Find count of active NetworkObjects
unity command eval 'UnityEngine.Object.FindObjectsByType<Unity.Netcode.NetworkObject>(UnityEngine.FindObjectsSortMode.None).Length;'
```

---

## 6. Headless Testing & Building (Works with Editor Open or Closed)

```bash
# Run EditMode unit tests
unity test "E:\Unity\My project" --mode EditMode --output "test-results.xml"

# Run PlayMode integration tests
unity test "E:\Unity\My project" --mode PlayMode --output "playmode-results.xml"

# Build Standalone Windows player
unity build "E:\Unity\My project" --clean
```

---

## Guardrails & Best Practices
1. **Prefer Paged / Scoped Queries**: If the scene contains thousands of objects, query by specific parent or instance ID.
2. **FixedUpdate for Physics**: Any Rigidbody scripts generated or tested through CLI must keep physics updates in `FixedUpdate()`.
3. **Safe Mode Detection**: If `unity command` fails with connection timeout, check if Unity is stuck in Safe Mode due to compilation errors. Run `unity command get_console_logs` or inspect C# scripts to resolve syntax errors first.
