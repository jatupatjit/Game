---
name: unity-netcode-guidelines
description: Unity 6 NGO and physics synchronization rules. Use when writing or modifying multiplayer gameplay, RPCs, NetworkVariables, or FixedUpdate physics.
globs: "**/*.cs, Assets/**"
always_apply: true
---

# Unity 6 Netcode & Co-op Physics Guidelines

> For specific player mechanics (movement, climbing, carrying, 2-bone IK) and active network scripts, refer to [.agents/context_handoff.md](../../context_handoff.md).

## Core Principles
1. **Target Stack**: Unity 6 (6000.x) with Universal Render Pipeline (URP) and Netcode for GameObjects (NGO 2.x).
2. **Network Classes**:
   - All classes that synchronize online gameplay state, send RPCs, or manage network variables must inherit from `Unity.Netcode.NetworkBehaviour`.
   - Never use plain `MonoBehaviour` for synchronized game entities.
3. **Physics Synchronization (FixedUpdate)**:
   - All `Rigidbody` modifications (e.g. `AddForce`, `AddTorque`, setting `linearVelocity` or `velocity`) and physics calculations MUST strictly execute within `FixedUpdate()`.
   - Input polling occurs in `Update()`, but physics application must be deferred to `FixedUpdate()` to maintain deterministic synchronization across networked clients.
4. **NGO Best Practices**:
   - Server/Host authoritative physics or client network transform sync: Ensure ownership is clearly defined (`IsOwner`, `IsServer`).
   - Use `NetworkVariable<T>` with proper write permissions (`NetworkVariableWritePermission.Server` or `Owner`).
   - Use `[Rpc(SendTo.Server)]` / `[Rpc(SendTo.ClientsAndHost)]` with correct permissions.
5. **Unity MCP Automation**:
   - Automatically leverage connected Unity MCP tools to inspect scenes, query components, create GameObjects, and manage project assets without hesitation.
