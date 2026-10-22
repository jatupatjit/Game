# Hazard Prefabs (ด่าน/กับดัก)

วิธีวาง: ลาก prefab จากโฟลเดอร์นี้ลงใน Hierarchy (ใต้ Hazards ของเกาะ/สะพาน) แล้วปรับค่าใน Inspector ที่คอมโพเนนต์หลัก
ทุก prefab มี NetworkObject อยู่แล้ว (server เป็นผู้ตัดสินดาเมจ) ถ้า `_cargo` ว่าง สคริปต์จะหา FragileCargo ให้เองตอน spawn
บันทึกซีนหลังวาง เพื่อให้ NetworkObject ได้ GlobalObjectIdHash ไม่ซ้ำ
แปลงของเก่า: `Tools/Expedition/Convert Hazards To Prefabs` (รันซ้ำได้) และตรวจ `Tools/Expedition/Verify Hazard Prefabs`

## NetworkRouteHazard (ฟิลด์ร่วม)
| ฟิลด์ | หน่วย | ค่าเริ่มต้น |
|---|---|---|
| _safeSeconds | วินาที (หยุดปลอดภัย) | 6 |
| _warningSeconds | วินาที (เตือน) | 1.75 |
| _activeSeconds | วินาที (อันตราย) | 2.5 |
| _phaseOffset | วินาที (เลื่อนจังหวะ) | 0 |
| _damage | HP ต่อรอบ | 10 |
| _damageReason | ข้อความสาเหตุ (ว่าง = ค่าตามชนิด) | "" |
| _swingAngle | องศา (0-175) | 65 |
| _travel | เมตร (Crusher) | 2.3 |
| _dropDistance | เมตร (CollapsingDeck) | 0.8 |
| _secondarySwingScale | เท่า (DoublePendulum) | 1.15 |

Prefab: Hazard_SwingHammer, Hazard_SwingHammer_Large, Hazard_Crusher, Hazard_Crusher_Entry, Hazard_Crusher_Relay, Hazard_CollapsingDeck, Hazard_DoublePendulum
(ค่าจริงต่อตัวเก็บเป็น override ในซีน)

## TimedCargoTrap: Hazard_FlameTrap, Hazard_FlameTrap_Bridge, Hazard_FirePulse, Hazard_SpikeTrap
_safeSeconds 4 / _warningSeconds 1 / _activeSeconds 1.5 (วินาที), _phaseOffset 0 (วินาที), _damage 10 (HP ต่อการทำงาน), _damageReason "Timed flame trap"

## RotatingLogHazard: Hazard_RotatingLog, Hazard_RotatingLog_Sky
_rotationSpeedDegrees 90 (องศา/วินาที, โหมดหมุนต่อเนื่อง), _safeSeconds 0 / _warningSeconds 2 / _sweepSeconds 5 (วินาที, โหมดจับเวลา), _phaseOffset 0 (วินาที), _cargoImpactDamage 10 (HP), _damageCooldownSeconds 1 (วินาที), _damageReason "Rotating wooden beam"

## CollapsingSteppingStone: Hazard_CollapsingStone, Hazard_CollapsingStone_Sky
_warningSeconds 0.65, _collapsedSeconds 2.5, _dropSeconds 0.4 (วินาที), _shakeMagnitude 0.04 (เมตร), _shakeFrequency 35 (rad/s), _dropDistance 6 (เมตร)

Prefab ชุด Medieval (Assets/Prefabs/Hazards/Medieval) ใช้ MedievalMechanismHazard / NetworkCargoDamageZone ไม่ได้อยู่ใน Level01/02 จึงไม่ถูกแปลง
