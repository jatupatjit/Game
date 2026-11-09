# Trap Prefabs (กับดักทำงานตลอด)

กับดักชุดนี้ **ไม่มีตัวจับเวลา ไม่มีสีเตือน ไม่มีช่วงพัก** ทำงานตลอดและเร็ว
ทุกตัวเป็น Prefab ลากไปวางในด่านได้เลย ไม่มี NetworkObject (ยกเว้น `Coop_ClimbSwitchLift` และ `Hazard_CollapsingStone*`)
ทุกเครื่อง (Host/Client) เห็นจังหวะเดียวกัน เพราะท่าทางคำนวณจากเวลาเซิร์ฟเวอร์

ดูของจริงทั้งหมด: เปิด `Assets/Scenes/Test/TrapGym.unity` แล้วกด Play (กับดักขยับเองโดยไม่ต้องมีผู้เล่น)

## โดนแล้วเกิดอะไร
- **ผู้เล่น**: ล้มแบบ Ragdoll + กระเด็นตามทิศที่โดนตี (ถ้าแบกลังอยู่ ลังหลุดมือตามระบบล้มเดิม)
- **ลัง**: HP ลด (นับเป็นดาเมจ "ความเสี่ยงจริง" ลังพังได้) + กระเด็น
- พัดลม/สายพาน: ผลักต่อเนื่อง ไม่ทำให้ล้ม

## โฟลเดอร์ (กับดักทั้งหมดอยู่ที่นี่ที่เดียว)
| โฟลเดอร์ | มีอะไร |
|---|---|
| `Moving/` | กับดักใหม่ 12 แบบ: Sweeper, TwinSweeper, Pendulum, PistonWall, SpinPost, Turntable, SpikeRoller, WallSaw, WindFan, Conveyor, Bumper, SlamDoor |
| `Classic/` | กับดักชุดเดิมที่ใช้ใน Level01/Level02 (Hazard_* และ Medieval_* ต่างกันแค่หน้าตา) ทำให้ทำงานตลอดแล้ว ไม่มีสีเตือน |
| `Obstacles/` | อุปสรรค Ragdoll: BoulderChute, SteepSlope 25/35/45, ScreeSlope, CliffLedge, ChasmGap 2/3/4m, RollingLogBridge, WobblyBridge, FallingRocks, TiltPlate, SlipperyPatch, TripStone |
| `Coop/` | `Coop_ClimbSwitchLift`: คนหนึ่งปีนกำแพงขึ้นไปกด E ที่ปุ่ม แท่นที่เพื่อนถือลังยืนรออยู่จะลอยขึ้นไปทางต่อไป |
| `Materials/` | วัสดุของกับดัก (`Materials/Medieval/` = วัสดุไม้/หิน/เหล็กของชุด Medieval) |

## ความไว
ทุกกับดักที่ขยับมี **Trap Speed** ที่ตัวบนสุด: 1 = ปกติ, 2 = เร็ว 2 เท่า, 0.5 = ช้าครึ่งหนึ่ง (0.1 ถึง 5)
ยิ่งเร็วยิ่งตีแรง ถ้าจะปรับแค่ชิ้นเดียว ใช้ช่อง Speed ใน TrapMotor ของชิ้นนั้น

## ทิศทาง
ทางเดินของผู้เล่นคือแกน **Z (ลูกศรสีน้ำเงิน)** ของ Prefab: พัดลมเป่าไป +Z, สายพานดึงไป -Z, ลูกสูบพุ่งไป +X

## ค่าที่ปรับได้ (Inspector)
**TrapMotor** (ส่วนที่ขยับ)
| ค่า | ความหมาย |
|---|---|
| Mode | Spin หมุน / Swing แกว่ง / Piston พุ่งออกแล้วดึงกลับ / PingPong เลื่อนไปกลับ / Loop เลื่อนทางเดียวแล้ววนใหม่ / Blink เปิดปิด |
| Degrees Per Second | ความเร็วหมุน (องศา/วินาที) ติดลบ = หมุนกลับทิศ |
| Swing Angle / Swing Center | มุมแกว่งแต่ละข้าง / จุดกลางการแกว่ง (องศา) |
| Period | วินาทีต่อ 1 รอบ (น้อย = เร็ว) |
| Travel | ระยะและทิศที่เลื่อน (เมตร) |
| Out Portion / Hold Portion | ลูกสูบ: สัดส่วนเวลาที่พุ่งออก / ค้างไว้ |
| Phase Offset | เลื่อนจังหวะ (วินาที) |
| Random Phase From Position | เปิด = แต่ละตัวที่วางจังหวะไม่ตรงกัน |

**TrapHitbox** (ส่วนที่ทำร้าย)
| ค่า | ความหมาย |
|---|---|
| Direction | Motion ตามทิศที่ส่วนนั้นเคลื่อน / AwayFromCenter ออกจากจุดกลาง / Forward / Up |
| Launch Speed / Upward Speed | ความแรงกระเด็น (m/s) / แรงยกขึ้น (m/s) |
| Min Part Speed | ถ้าส่วนนั้นเคลื่อนช้ากว่านี้ แตะแล้วไม่เป็นไร (เช่นเสากลาง) |
| Knock Down | เปิด = ล้ม |
| Cargo Damage | HP ที่ลังเสียต่อการโดน 1 ครั้ง |
| Cooldown | วินาทีก่อนโดนซ้ำได้ |

**TrapPushZone** (ลม/สายพาน): Direction, Player Acceleration, Cargo Acceleration
**TrapRockStream** (หินกลิ้ง/หินร่วง): Interval (วินาทีต่อก้อน), Roll Seconds, Size Range, Drop Area

## ข้อห้าม
- อย่าใส่ MeshCollider แบบไม่ convex ให้ส่วนที่ขยับ
- อย่าเพิ่ม NetworkObject ให้กับดัก (ไม่จำเป็น)
- เหว/ทางแคบใช้ Kill-Z และ checkpoint ของด่านเดิม ต้องมี checkpoint ก่อนถึงอุปสรรค

## สร้างใหม่
เมนู `Tools/Traps/Build All Trap Prefabs` และ `Tools/Traps/Build TrapGym Scene` (เขียนทับเฉพาะไฟล์ในโฟลเดอร์นี้และ TrapGym)
ถ้าปรับค่าใน Prefab เองแล้ว อย่ากดสร้างใหม่ เพราะจะเขียนทับ
