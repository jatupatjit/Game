---
trigger: always_on
---
# Unity Development Core Rules

## 1. Core Stack & Invariants
- **Engine**: Unity 6 (URP) `6000.6.0f1` with Netcode for GameObjects (NGO).
- **Network Entities**: All networked gameplay scripts must inherit from `NetworkBehaviour`.
- **Physics**: All `Rigidbody` modifications, physics forces, and velocity updates must strictly execute in `FixedUpdate()`.
- **Direct Editor Control**: Drive the live Editor via Unity MCP / CLI instead of hand-editing `.unity` or `.prefab` YAML files while the Editor is running.

## 2. Progressive Disclosure & Context
- For comprehensive project architecture, active component details, rigging structure, and bug history, refer to [`.agents/context_handoff.md`](../../.agents/context_handoff.md).

## 3. Autonomous Execution & Verification Boundaries (GPT-6 Astra Persistence)
- **Autonomous MCP & Error Resolution**: You have full permission to use Unity MCP / CLI tools (`get_console_logs`, `recompile`, `eval`, `editor_play`, `get_scene_hierarchy`, etc.) to inspect live state, diagnose compilation or runtime errors, and iterate on fixes until code compiles and passes 100% cleanly without pausing to ask for approval at each step.
- **Git Safety Boundary**: Do NOT execute `git commit` or `git push` unless explicitly instructed by the user.
