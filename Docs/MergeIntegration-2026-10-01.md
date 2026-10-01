# รวมงานในเครื่องกับ GitHub — 1 ตุลาคม 2569

## งานที่รวม

- งานในเครื่อง `0275048`: วงจรส่งลัง คะแนน ร้านค้า การ์ด ไอเทม ประตู Level02 เสียง และเอกสาร Week 6
- งาน GitHub `2975160`: Wall Jump, Player prefab และวงแหวน DeliveryZone
- เก็บประวัติทั้งสองฝั่งด้วย merge บน main

## จุดที่แก้ระหว่างรวม

- ใช้ Wallclimb และโครง Player prefab จาก GitHub แล้วเพิ่ม PlayerExpeditionState และ PlayerFootstepAudio ผ่าน PrefabUtility
- เก็บ Level01 ที่มีทางใหม่ ฉากหมู่บ้าน ป่า ปราสาท UI และระบบภารกิจ การวาง DeliveryZone ใช้ผิว Collider จริง ซึ่งอยู่ที่ Y=-11.24175 ในฉากที่รวมแล้ว
- วงแหวนเป็น Quad วางราบบนพื้น ปิดควันรอบโซนตามงาน GitHub และเก็บป้ายส่งของเดิม
- DeliveryPoint ตรวจลังที่กำหนดก่อนคิดคะแนน ใช้เกณฑ์ตำแหน่งเดียวกับ DeliveryZone และตรวจซ้ำใน FixedUpdate เมื่อยังอยู่ใน trigger
- สีเขียว/ข้อความส่งสำเร็จอิงสถานะ DeliveryPoint ที่ Host ยืนยัน ไม่ให้ตำแหน่งบน Client ยืนยันคะแนนเอง
- ย้ายการตรวจตำแหน่งของโซนไป FixedUpdate; ค้นหาลังอัตโนมัติเฉพาะโหมด fallback และไม่เกินวินาทีละครั้ง ลดการค้นหาซ้ำทุกเฟรมเมื่อไม่มีลัง
- ล้าง overlap เมื่อปิดโซน และลด predicate allocation ระหว่างตรวจ Collider
- เครื่องมือ Setup เก็บ Prefab/ภารกิจเดิม ไม่สร้างวงแหวนซ้ำ ไม่เพิ่ม CarryablePackage ที่เคยลบ และคืน Scene เดิมหลังทำงาน
- Inspector แสดงการส่งที่ยืนยันแล้วและเปิดให้ตั้งค่า reference/UnityEvent ที่เคยถูกซ่อน

## การตรวจหลังรวม

- Unity 6000.6.0f1 คอมไพล์ผ่าน ไม่มี compiler error
- SampleScene, Level01, Level02 และ Player prefab ไม่มี Missing Script
- Level01/02 มี mission cargo และ DeliveryPoint ครบ; Level01 เชื่อมประตูไป Level02; Level02 ยังไม่มีปลายทางด่านใหม่
- ทั้งสองด่านตั้ง target เป็น WoodenCrateItem และ SpecificObjectOnly; วงแหวนเป็น Quad; ไม่มี CarryablePackage ในฉาก
- Windows Development Build หลังรวมผ่าน: 0 errors / 515 warnings ใช้เวลา 29.8 วินาที; warnings ยังมี API เก่าและ mesh collision pre-bake เป็นต้น ผลเก็บที่ E:/Unity/Verification/ExpeditionMerged/BuildResult.json
- ผลเล่น Host/Client 2–4 คนในเอกสาร ExpeditionLoop เป็นผลก่อน merge รอบนี้ ยังไม่ได้เล่นซ้ำหลังรวม และยังไม่ได้ทดสอบ Steam ระหว่างเครื่องจริง
