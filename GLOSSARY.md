# Glossary — Dont Drop It

Co-op 3D delivery game (Unity 6, NGO). Terms as used in code and `E:/Unity/.agents/design/GAME_RULES.md`. Items marked *(planned)* are agreed design (GAME_RULES §7) not yet in code. Items marked *(Level01)* are in code but so far used only in Level01 (GAME_RULES §7b, not yet hand-tested with 2 players).

- **Cargo / FragileCargo**: the crate the team delivers; has HP (max 100) that drops on impacts. HP loss, not time, is what scores.
- **Carrier**: a player currently gripping a cargo. A cargo holds up to 4 carriers (Front/Back/Left/Right sockets).
- **Grip**: a hand's physical attachment to a point on the cargo surface; each hand can be released independently.
- **Solo carry / Co-op carry**: one carrier vs two or more. Co-op carry is the core mechanic; coordination is the intended fun.
- **Solo assist**: Level01-only help that lets one player lift cargo (teaching/demo build). Not intended for later levels.
- **Hand load**: share of cargo weight borne by the hands; the rest is relieved so cargo stays floaty but not weightless.
- **Weight preset**: switchable hand-load scale (F6 cycles 0 current / 1 / 2) for playtest tuning (`CarryWeightPreset`).
- **RequiresTwoCarriers**: per-cargo flag; with one carrier the far end drags on the ground.
- **Stamina**: per-player resource spent on carry (solo), sprint, jump, climb. Exhausted forces dropping the cargo.
- **DeliveryZone / DeliveryPoint**: the target the cargo must rest in to complete a mission.
- **Stage / Level**: a mission scene (Level01 teaching, Level02 eleven-island sky kingdom). Score multiplier depends on stage.
- **Island**: a platform section of a level.
- **Checkpoint pad** *(Level01)*: `CheckpointPad`/`CheckpointManager`; activates when cargo rests on it; respawn point for cargo and players, stamina regen x3 nearby. Dense early, sparse late.
- **Repair kit** *(Level01)*: `RepairKit`; one-use pickup on an optional branch island, +20 cargo HP (capped at 100), cargo must be within 7 m.
- **Side mission** *(Level01)*: `LevelRunState`; reach the repair-kit island within 270 s for +50 coins and a star. Time otherwise never affects score.
- **Card**: one of 13 perks in `CardCatalog`; the server offers each player 3 at stage start and they keep 1 (GAME_RULES §2b).
- **Lobby**: hub scene with character customization (C); planned to host playable demo stations, one per mechanic *(planned)*.
