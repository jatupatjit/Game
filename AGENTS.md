# AGENTS.md

## 1. Project Overview & Rules
- **Stack**: Unity 6 (URP) `6000.6.0f1`, Netcode for GameObjects (NGO), Steamworks.NET.
- **Network Entities**: All networked gameplay scripts inherit from `NetworkBehaviour`.
- **Physics**: All physics calculations and Rigidbody modifications must run strictly in `FixedUpdate()`.
- **Direct Editor Control**: Drive live Editor state via Unity MCP / CLI rather than manual `.unity` / `.prefab` YAML file manipulation.

## 2. Progressive Disclosure
- Refer to [`.agents/context_handoff.md`](.agents/context_handoff.md) for full system architecture, player mechanics (movement, climbing, lifting, procedural 2-bone IK), and resolved bug references.

## 3. Autonomous Verification & Error Resolution
- You have full permission to autonomously use Unity MCP tools (`get_console_logs`, `recompile`, `eval`, `editor_play`, etc.) to diagnose console errors, apply fixes, and re-test until compilation passes 100% cleanly without pausing to ask for approval at each step.
- **Git Policy**: Do NOT run `git commit` or `git push` unless explicitly requested.

## 4. Session Persistence Rule
- At the conclusion of each milestone or major bug fix, automatically summarize the latest changes, resolved issues, and next tasks directly into [`.agents/context_handoff.md`](.agents/context_handoff.md). Keep it concise under 300 words using bullet points.

