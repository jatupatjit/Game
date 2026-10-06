# Co-op acceptance checklist

Criteria, not claims that the current build passes. Use PASS / FAIL / UNVERIFIED / N/A. Record build/commit/date, scene, player count, actual serialized settings and evidence path. Host-only checks cannot establish Client or late-join correctness.

## Test setup

- Inspect current Lobby, Level01 and Level02 scene/build configuration and active player/cargo prefabs through Editor APIs.
- Use one Host plus a separate Client for network cases; repeat selected coordination cases with 2–4 players when available.
- Record original Editor/Play Mode/preferences and restore only changes made for the test. Read fresh relevant Console entries and compilation state.
- Use actual gameplay input, contacts and configured timings. Do not change production constants simply to make a test pass.

| ID | Scenario | Acceptance / evidence |
|---|---|---|
| C01 | Toggle each hand, E both, release and Q throw | One state transition per input; correct left/right pose and contact target on Host/Client. |
| C02 | Carry alone and with 2–4 players | Cargo remains attached to correct contacts; no sustained player penetration or sudden teleport; measure position differences over time using agreed project tolerances. |
| C03 | Grip/near/far reach and turn transitions | No NaN/invalid rotations, visible wrist break or abrupt shoulder flip. Measure transformed bone lengths, contact error and pose continuity; define tolerances before reporting PASS. |
| C04 | Carrier disconnect/despawn/stamina depleted | Server removes grip/support/collision exemptions; remaining player can release or re-grab; no suspended cargo. |
| H01 | Initial load and first pickup | HP starts at configured max; spawn settling and protected player contact do not cause unintended damage. |
| H02 | Impact, sustained scrape and distinct second hit | HP changes authoritatively; respects current cooldown/grace and configured normal-impact rules. Record timestamps, collider identities, damage and HP before/after. |
| H03 | HP display and floating damage numbers | Display above item, proximity visibility, actual damage amount shown once per accepted damage event; consistent replicated HP. |
| H04 | Cargo broken or dropped out of bounds | Configured failure/respawn behavior and message occur once; no stale grips/UI. Restart reconstructs cargo, HP and objectives. |
| D01 | Cargo enters DeliveryZone | Server accepts delivery once, centers cargo on delivery target, calculates score from remaining HP and informs clients. |
| D02 | Delivery unlocks Level01 exit | Portal visuals/trigger become active; HUD explains success and next destination. Repeated delivery cannot duplicate score. |
| D03 | Enter portal to Level02 | All connected clients follow authorized NGO scene transition; each camera follows its owned player; required HUD/stamina and cargo state are correct. |
| D04 | Fall/restart/late join | Correct spawn, no duplicate player/cargo/HUD, scene state and delivery/portal state reconstructed as designed. |
| U01 | Lobby face preview Save/Cancel | C/button works in Lobby; gameplay input blocked while modal open; Cancel restores saved state; server rejects changes outside Lobby. |
| U02 | Face and HUD persist across levels | Selected face reaches Client/late join; code-to-join UI absent in Level01/02; TAB panel and stamina work. |
| F01 | Walk on ground and carry across slopes | Visible soles grounded while grounded, correct squash compensation; no frame flicker. Measure visible mesh/ground gap separately from controller collider. |

## Recording results

Store a dated table under E:/Unity/Verification: case ID, setup, observed result, status, evidence file and limitation. Screenshots support visual evidence, not networking correctness alone. Logs and repeatable input/action sequence are required for failures.

Physics FixedUpdate and IK LateUpdate are project requirements; timing compliance does not prove deterministic networking or eliminate all jitter. Performance/GC claims require a captured measurement for the tested scenario. Do not claim host migration is implemented without a dedicated design and test.
