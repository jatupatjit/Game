# Glossary — Dont Drop It

Co-op 3D delivery game (Unity 6, NGO). Terms as used in code and `.agents/design/GAME_RULES.md`. Items marked *(planned)* are agreed design (GAME_RULES §7) not yet in code.

- **Cargo / FragileCargo**: the crate the team delivers; has HP (max 100) that drops on impacts. HP loss, not time, is what scores.
- **Carrier**: a player currently gripping a cargo. A cargo holds up to 4 carriers (Front/Back/Left/Right sockets).
- **Grip**: a hand's physical attachment to a point on the cargo surface; each hand can be released independently.
- **Solo carry / Co-op carry**: one carrier vs two or more. Co-op carry is the core mechanic; coordination is the intended fun.
- **Solo assist**: Level01-only help that lets one player lift cargo (teaching/demo build). Not intended for later levels.
- **Hand load**: share of cargo weight borne by the hands; the rest is relieved so cargo stays floaty but not weightless.
- **Weight preset**: switchable hand-load scale (F6, 0 current / 1 / 2) for playtest tuning.
- **RequiresTwoCarriers**: per-cargo flag *(new)*; with one carrier the far end drags on the ground.
- **Stamina**: per-player resource spent on carry (solo), sprint, jump, climb. Exhausted forces dropping the cargo.
- **DeliveryZone / DeliveryPoint**: the target the cargo must rest in to complete a mission.
- **Stage / Level**: a mission scene (Level01 teaching, Level02 eleven-island sky kingdom). Score multiplier depends on stage.
- **Island**: a platform section of a level.
- **Checkpoint pad** *(planned)*: pad that activates when cargo is placed on it; respawn point and fast stamina refill. Dense early, sparse late.
- **Repair kit** *(planned)*: one-use pickup on optional branch islands, +20 cargo HP (capped at 100).
- **Side mission** *(planned)*: optional objective; first kind is reaching a branch island within a time limit for a coin/star bonus. Time otherwise never affects score.
- **Lobby**: hub scene; planned to host playable demo stations, one per mechanic.
