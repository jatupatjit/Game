# อัปเดตงาน Week 6 วันที่ 1 ตุลาคม 2569

ภาพทั้งหมดแคปจาก Unity จริง มี Inspector แยกสำหรับสคริปต์ใหม่ 12 ไฟล์และเสียงที่นำเข้า 9 ไฟล์ แอสเซ็ตฉากอื่นใช้ภาพรวมการใช้งานร่วมกัน ไม่ใช่ภาพ Inspector แยกทุก material หรือ mesh

เปิด `index.html` เพื่อดูภาพ ค้นชื่อไฟล์ และกรองหมวดได้ รายงาน Word อยู่ที่ `E:/Unity/Reports/2026-10-01`

## ภาพฟีเจอร์

### UI ห้องและปุ่มเดิมหลังแก้สคริปต์ที่ทำให้ standalone โหลดไม่ได้

![UI ห้องและปุ่มเดิมหลังแก้สคริปต์ที่ทำให้ standalone โหลดไม่ได้](01_SampleScene_UI.png)

### เลือกการ์ดก่อนเริ่มด่าน

![เลือกการ์ดก่อนเริ่มด่าน](02_ExpeditionHUD_Cards.png)

### เป้าหมาย HP ลัง Stamina และช่องไอเทมขวาล่าง

![เป้าหมาย HP ลัง Stamina และช่องไอเทมขวาล่าง](03_ExpeditionHUD_Gameplay.png)

### คะแนนส่งลังและร้านค้า

![คะแนนส่งลังและร้านค้า](04_Delivery_Score_Shop.png)

### หน้าล้มเหลวหลังแก้ Pause ซ้อนและปุ่มเริ่มใหม่

![หน้าล้มเหลวหลังแก้ Pause ซ้อนและปุ่มเริ่มใหม่](06_MissionFail_Retry.png)

### ทางเดิมและทางร่วมมือใหม่ มุม Editor ปิดหมอกเฉพาะตอนแคปเพื่อเห็นเส้นทาง

![ทางเดิมและทางร่วมมือใหม่ มุม Editor ปิดหมอกเฉพาะตอนแคปเพื่อเห็นเส้นทาง](07_Level01_Overview.png)

### DeliveryZone และโครงสร้างปราสาทกับประตู

![DeliveryZone และโครงสร้างปราสาทกับประตู](08_DeliveryZone_Castle.png)

### คันโยกเปิดทางและจุดส่งของใน Level02

![คันโยกเปิดทางและจุดส่งของใน Level02](09_Level02_TeamLeverGate.png)

## ไฟล์ที่เพิ่ม

| ไฟล์ | งาน | ภาพ Inspector | ภาพในเกมหรือฉาก |
| --- | --- | --- | --- |
| `Assets/Materials/Level01BackRoute/DirectionCyan.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/GoalBeacon.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/LandingGold.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/NarrowBridge.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/ObstacleOrange.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/PortalGlow.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Level01BackRoute/RouteSlate.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Cloud.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Flag.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Grass.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Leaves.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Roof.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Sky.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Stone.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Materials/Medieval/Wood.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/AscentRamp.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/Ascent_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/Ascent_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/Ascent_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/CargoRamp_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/CargoRamp_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/CargoRamp_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/CargoRamp_3.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_3.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_4.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_5.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_6.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/DeliveryRamp_7.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/ExitBridge.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/HighTeamBridge.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/RelayRamp_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/RelayRamp_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/RelayRamp_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/RelayRamp_3.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_3.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_4.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SafeDescent_5.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/SharedCrossing.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/TeamBridge_0.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/TeamBridge_1.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/TeamBridge_2.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/TeamBridge_3.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Meshes/Level01Route/WideBridge.asset` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/Broken.wav` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/Delivered.wav` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/Impact.wav` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/Lift.wav` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/Portal.wav` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Audio/SFX/Click.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Click.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Click2.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Click2.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Grab.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Grab.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Grab2.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Grab2.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/HeavyDrop.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_HeavyDrop.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Throw.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Throw.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Throw2.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Throw2.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Walk.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Walk.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/Audio/SFX/Walk2.wav` | เสียงจากโฟลเดอร์ Sound ที่ผู้ใช้ส่ง | [ดูภาพ](File_Walk2.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Resources/CarryableOutline.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/FeedbackDust.mat` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Resources/Fonts/GameThai.ttf` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](02_ExpeditionHUD_Cards.png) |
| `Assets/Resources/Fonts/OFL.txt` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](02_ExpeditionHUD_Cards.png) |
| `Assets/Scenes/Level02.unity` | แอสเซ็ตประกอบฉาก ฟอนต์ หรือเสียงต้นแบบ | ดูภาพรวมฉาก | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Scripts/Editor/ExpeditionVerificationBuild.cs` | เมนู build โปรแกรมทดสอบแยกจาก Editor | [ดูภาพ](File_ExpeditionVerificationBuild.png) | [ดูภาพ](01_SampleScene_UI.png) |
| `Assets/Scripts/Network/CoopPressureGate.cs` | สองผู้เล่นยืนคนละแผ่นเพื่อเปิดทาง | [ดูภาพ](File_CoopPressureGate.png) | [ดูภาพ](07_Level01_Overview.png) |
| `Assets/Scripts/Network/DeliveryExitPortal.cs` | เปิดหลังส่งลัง และโหลด Level02 ผ่าน NGO | [ดูภาพ](File_DeliveryExitPortal.png) | [ดูภาพ](08_DeliveryZone_Castle.png) |
| `Assets/Scripts/Network/ExpeditionHUD.cs` | เป้าหมาย การ์ด คะแนน ร้านค้า และช่องไอเทม | [ดูภาพ](File_ExpeditionHUD.png) | [ดูภาพ](04_Delivery_Score_Shop.png) |
| `Assets/Scripts/Network/ExpeditionNetworkProbe.cs` | ทดสอบ localhost เฉพาะ Editor หรือ development build | [ดูภาพ](File_ExpeditionNetworkProbe.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Scripts/Network/GameplayFeedback.cs` | เสียง 9 ไฟล์จากผู้ใช้ AudioSource ใช้ซ้ำ และฝุ่นกระแทก | [ดูภาพ](File_GameplayFeedback.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Scripts/Network/LevelMission.cs` | สถานะภารกิจและผลส่งของ | [ดูภาพ](File_LevelMission.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |
| `Assets/Scripts/Network/PlayerExpeditionState.cs` | เงิน การ์ด ไอเทม และ server validation | [ดูภาพ](File_PlayerExpeditionState.png) | [ดูภาพ](02_ExpeditionHUD_Cards.png) |
| `Assets/Scripts/Network/SpinnerAnimator.cs` | แยก MonoBehaviour ให้โหลดใน build ได้ | [ดูภาพ](File_SpinnerAnimator.png) | [ดูภาพ](01_SampleScene_UI.png) |
| `Assets/Scripts/Network/TeamLeverGate.cs` | กด E เปิด 8 วินาทีและไม่ปิดทับคนหรือของ | [ดูภาพ](File_TeamLeverGate.png) | [ดูภาพ](09_Level02_TeamLeverGate.png) |
| `Assets/Scripts/Network/UIButtonHover.cs` | hover scale และเสียงคลิกปุ่ม | [ดูภาพ](File_UIButtonHover.png) | [ดูภาพ](01_SampleScene_UI.png) |
| `Assets/Scripts/Player/PlayerFootstepAudio.cs` | เสียงก้าวจากการเคลื่อนบนพื้น ไม่แก้อนิเมชัน | [ดูภาพ](File_PlayerFootstepAudio.png) | [ดูภาพ](03_ExpeditionHUD_Gameplay.png) |

## ผลตรวจ

- Host Editor + standalone Clients สูงสุด 4 โปรแกรมบนเครื่องเดียว ยก ส่งลัง วาป และ restart ผ่านตาม snapshots
- Build Windows ล่าสุด 0 errors, 515 warnings; console การทดสอบเสียงและ Pause 0 errors, 2 warnings
- เดิน 60 FixedUpdate ticks ระยะ 2.4 เมตร เรียก Walk และ Walk2 รวม 2 ก้าว
- Client จับของแล้ว Host เล่น Grab; Host ปาผ่าน owner RPC แล้วเล่น Throw; กระแทกเล่น HeavyDrop; ปุ่มเล่น Click
- ลังแตกขณะ Pause: failure=true, paused=false; restart แล้วทั้งสองฝั่ง HP 100 และ cameraOwned=true
- ยังต้องลองเสียงด้วยคนเล่นจริงและ Steam คนละเครื่อง; ไม่อ้างว่าผ่านการเดินครบเส้นทางด้วยมนุษย์
- ภาพ `05_Level02_LeverPrompt.png` เป็นภาพก่อนแก้ที่ Pause เปิดอยู่ เก็บไว้เป็นหลักฐานปัญหา ไม่ใช่ภาพผลแก้สุดท้าย
- ไม่มี commit หรือ push รอบนี้

## ไฟล์เดิมที่แก้

- `Assets/Materials/DeliveryZone_Mat.mat`
- `Assets/Prefabs/DeliveryZone.prefab`
- `Assets/Prefabs/Player/Player.prefab`
- `Assets/Scenes/Level01.unity`
- `Assets/Scenes/SampleScene.unity`
- `Assets/Scripts/CarrySystem/CarryableObject.cs`
- `Assets/Scripts/CarrySystem/CarryableOutline.cs`
- `Assets/Scripts/CarrySystem/DeliveryPoint.cs`
- `Assets/Scripts/CarrySystem/DeliveryZone.cs`
- `Assets/Scripts/CarrySystem/FragileCargo.cs`
- `Assets/Scripts/CarrySystem/MissionFailUI.cs`
- `Assets/Scripts/CarrySystem/PlayerCarry.cs`
- `Assets/Scripts/Network/LobbyPortalGate.cs`
- `Assets/Scripts/Network/PauseMenu.cs`
- `Assets/Scripts/Network/UIBuilders.cs`
- `Assets/Scripts/Player/NetworkPlayer.cs`
- `Assets/Scripts/Player/PlayerCameraController.cs`
- `Assets/Scripts/Player/PlayerInputReader.cs`
- `Assets/Scripts/Player/PlayerStamina.cs`
- `Assets/Scripts/Player/PlayerStaminaUI.cs`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/ProjectSettings.asset`