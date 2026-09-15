# Project Context Handoff: Co-op Multiplayer Physics Game (Unity 6 NGO)

> **Document Purpose**: This file serves as a complete, self-contained summary of the project architecture, implemented systems, resolved bugs, and active state to enable seamless context continuation across AI sessions (Antigravity IDE, Gemini Web, etc.).

---

## 1. Project Overview & Tech Stack

- **Engine**: Unity 6 (URP) `6000.6.0f1`
- **Multiplayer / Networking**: Netcode for GameObjects (NGO) + Steamworks.NET (Steam Matchmaking Lobby & Steam Relay)
- **Genre & Style**: Co-op 3D physics-based platformer inspired by *Human Fall Flat* and *Peak*.
- **Core Rules**:
  - All gameplay and network scripts inherit from `NetworkBehaviour`.
  - All physics forces and Rigidbody updates strictly execute in `FixedUpdate`.
  - Character movement and climbing drive via `CharacterController` with anti-phasing wall protection.
  - Procedural skeleton rigging uses pure rotation-based 2-bone Inverse Kinematics (IK) with direct forward orientation.

---

## 2. System Architecture & Key Files

### 🎮 Player Locomotion & Controls
- [`PlayerMovement.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Player/PlayerMovement.cs):
  - Handles 3D movement, sprinting, jumping with kinematic impulse formula `v = sqrt(2 * h * |g|)`, coyote time (`0.15s`), jump buffering (`0.15s`), and custom gravity (`-24 m/s²`).
  - Disables gravity during climbing (`IsClimbing`).
- [`PlayerInputReader.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Player/PlayerInputReader.cs):
  - Unity New Input System integration.
  - Separate hold states for Left Click (`GrabLeftHeld`), Right Click (`GrabRightHeld`), Interact (`InteractHeld`), and Throw (`ThrowHeld`).
  - Ignores UI raycasters when mouse cursor is locked in gameplay mode (`Cursor.lockState == CursorLockMode.Locked`).
- [`PlayerCameraController.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Player/PlayerCameraController.cs):
  - Smooth 3rd-person orbital camera, extended upward pitch (-65° up to +70° down), and horizontal heading vectors.

### 🦾 Procedural Skeleton & 2-Bone IK
- [`ProceduralPlayerArms.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Player/ProceduralPlayerArms.cs):
  - Pure rotation-based Analytic Law of Cosines 2-Bone IK on `Rigged_character_.fbx` skeleton (`UpperArm` -> `LowerArm` -> `Wrist`).
  - **Direct LookRotation Construction**: Uses forward-aligned `LookRotation(boneZ, boneY)` to mathematically eliminate the 180° gimbal flip singularity when raising arms overhead or turning around.
  - **Anti-Penetration Constraint**: Clamps IK targets in front of the shoulder plane (`localTarget.z >= -0.05f`) preventing arms from bending backwards through the torso.
  - **Natural Rest Pose**: Calibrated `28°` upper arm outward angle provides natural ~12cm clearance from hips at rest (`LeftHandRestLocal`, `RightHandRestLocal`).
  - **Unified Hand Binding**: Automatically binds with `Wallclimb` and `PlayerCarry` so character wrists and palms touch exact wall grip points and carry sockets without primitive duplicate meshes.

### 🧗 Climbing System (Restored from Commit `d8d65af`)
- [`Wallclimb.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Player/Wallclimb.cs):
  - **Exact `d8d65af` Mechanics**:
    - `_handReachDistance = 2.0f`
    - `_normalHangDistance = 0.95f`, `_minHangDistance = 0.35f`, `_maxHangDistance = 1.45f`
    - `_bodyPullSpeed = 10.0f`
    - `chestPos = transform.position + Vector3.up * 1.15f` (Correct chest height prevents raycasting into the ground and ensures body hoists properly).
  - **Unified Hand Tracking & Palm Alignment**:
    - Hand targets bind directly to `ProceduralPlayerArms` (`IKTarget_Left`, `IKTarget_Right`).
    - Hand rotation smoothly orients palm against wall normals (`Quaternion.LookRotation(-normal, Vector3.up)`).
  - **W / S Key Body Hoisting**:
    - Pressing **'W'** pulls the body UP towards hands to `_minHangDistance = 0.35m`.
    - Pressing **'S'** lowers the body DOWN away from hands to `_maxHangDistance = 1.45m`.
    - Neutral holds comfortably at `_normalHangDistance = 0.95m`.
  - **Exact Shoulder-Aligned Aim Scanning**:
    - Left and right rays shoot parallel from `leftShoulder` and `rightShoulder` in camera look direction.
    - Wall reticle markers appear directly in front of each hand with zero lateral skew.
  - **Wall Jump Boost (Spacebar)**: Launches upward + outward away from the wall.
  - **Top Ledge Pull-Up**: Automatically pulls and slides player onto the top surface when reaching the top lip.

### 📦 Physical Carrying & Outline System (Restored from Commit `d8d65af`)
- [`PlayerCarry.cs`](file:///e:/Unity/My%20project/Assets/Scripts/CarrySystem/PlayerCarry.cs) & [`CarryableObject.cs`](file:///e:/Unity/My%20project/Assets/Scripts/CarrySystem/CarryableObject.cs):
  - **Exact `d8d65af` Mechanics**:
    - `_grabContactDistance = 1.6f`
    - `_maxLiftHeight = 1.7f`, `_normalLiftHeight = 0.85f`, `_minLiftHeight = 0.25f`
    - `_bodyMoveSpeedRatio = 0.70f`
  - **Independent Hands & Easy Dual Grab**: Left/Right grab socket attachments with automatic dual grab.
  - **Outline Feedback**: [`CarryableOutline.cs`](file:///e:/Unity/My%20project/Assets/Scripts/CarrySystem/CarryableOutline.cs) + [`CarryableOutline.shader`](file:///e:/Unity/My%20project/Assets/Shaders/CarryableOutline.shader)
    - Dynamic smoothed-normal inverted hull outline shader in URP.
    - Green outline when aimed at within reach; Blue outline while carried/held.

### 🌐 Multiplayer, UI & Diagnostics
- [`SteamLobbyManager.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Network/SteamLobbyManager.cs): Steamworks matchmaking, 6-character room codes, auto-reconnect, and lobby member callbacks (`OnLobbyMemberJoined`, `OnLobbyMemberLeave`, `OnLobbyMembersChanged`) providing real-time player list names.
- [`ProceduralUIUtility.cs` / `UIBuilders.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Network/UIBuilders.cs): Procedural 9-sliced rounded sprite generator (`GetRoundedSprite`), modern glass dark slate theme, isolated sub-panels.
- [`LobbyUI.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Network/LobbyUI.cs): Fullscreen main menu with `▶ PLAY` button, clean sub-menus, and revamped `JoinSubPanel` (horizontal Input + Paste row, full-width `✓ JOIN ROOM` & `← BACK` buttons).
- [`RoomCodeHUD.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Network/RoomCodeHUD.cs): Top-left in-game overlay card (`250x190`) with `ROOM CODE • HOST/CLIENT` badge, big stylized room code, real-time `PLAYERS (count/4)` with `• Name (Host)` list, and one-click `📋 Copy Code` with dynamic `✓ Copied!` emerald feedback.
- [`AutomatedPlaytestVerifier.cs`](file:///e:/Unity/My%20project/Assets/Scripts/Editor/AutomatedPlaytestVerifier.cs): In-editor automated test runner validating all prefabs, scenes, and UI bindings.

---

## 3. Major Bugs Fixed & Key Solutions

| Issue / Bug | Root Cause | Solution Implemented |
| :--- | :--- | :--- |
| **Arm Twist / Torso Penetration on Overhead & Turn** | `FromToRotation` had a 180° singularity when rotating from down-facing rest pose to upward-facing overhead pose. | Replaced with direct `LookRotation(boneZ, boneY)` where local +Z is locked to forward projection and local +Y is the bone direction; added `localTarget.z >= -0.05f` clamp. |
| **Climbing Weak / Not Lifting Body Up** | `chestPos` was mistakenly offset to feet level (`-0.05f`), corrupting hang formulas. | Restored commit `d8d65af` parameters with `chestPos = transform.position + Vector3.up * 1.15f`, `_bodyPullSpeed = 10.0f`, `_minHangDistance = 0.35f`. |
| **Carrying & Outline Behavior** | Diverged from the responsive feel of commit `d8d65af`. | Restored `PlayerCarry.cs`, `CarryableObject.cs`, `CarryableOutline.cs`, and `CarryableOutline.shader` from commit `d8d65af`. |
| **Stamina System Disappeared / HUD Missing** | `PlayerStamina` & `PlayerStaminaUI` were missing on `Player.prefab`, `PlayerStaminaUI.Awake()` disabled itself due to premature `netObj.IsOwner` check before Netcode spawn. | Attached `PlayerStamina` and `PlayerStaminaUI` (`NetworkBehaviour`) via `PlayerModelSetupEditor`, fixed `OnNetworkSpawn()` ownership check, dedicated `PlayerHUD_Canvas`, and wired sprint/carry stamina drain. |
| **Climbing Pitch Interference & Hoisting Decoupling** | Looking up/down previously modulated climbing hang distance, fighting player intent. | Decoupled camera pitch entirely; body hoisting (up to `0.35m`) and lowering (down to `1.45m`) are now strictly mapped to **W** and **S** keys. |
| **Join Menu Stretched Buttons & Missing Player List** | `VerticalLayoutGroup` stretched buttons into tall monoliths without `flexibleHeight = 0`; room lacked connected member display. | Redesigned `JoinSubPanel` with a horizontal `[Room Code Input (270px) + Paste (110px)]` row and full-width `✓ JOIN ROOM` / `← BACK` buttons; subscribed `RoomCodeHUD` to `SteamLobbyManager.OnLobbyMembersChanged` to display real-time player names and host badges. |

---

## 4. Current Workspace State

- **Unity Editor Status**: Connected & Synchronized (Port 7800, Unity 6000.6.0f1).
- **Automated Verification Status**: **`ALL CHECKS PASSED ✅`** (0 Compilation Errors, 0 Runtime Exceptions, UI & Gameplay verified in Play Mode).
- **Session Persistence**: Closing hook active; updates automatically summarized to `.agents/context_handoff.md`.
- **Git Policy Notice**: Do not commit or push unless explicitly requested.

