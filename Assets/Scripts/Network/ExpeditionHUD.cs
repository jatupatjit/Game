using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>Objective, replicated score, card choice and two-item shop. All purchases are validated by the host.</summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    public sealed class ExpeditionHUD : MonoBehaviour
    {
        public static bool BlocksGameplayInput { get; private set; }
        [SerializeField] private Font _font;
        private LevelMission _mission;
        private PlayerExpeditionState _player;
        private TeamLeverGate _lever;
        private Text _objective, _detail, _inventory, _title, _message;
        private GameObject _modal, _cards, _shop;
        private Text _shopWallet;
        private readonly Button[] _buy = new Button[2];
        private float _refreshAt;
        private bool _summaryDismissed;
        private bool _modalShown;
        private string _lastObjective, _lastDetail, _lastInventory;

        private void Awake()
        {
            _mission = FindAnyObjectByType<LevelMission>();
            _lever = FindAnyObjectByType<TeamLeverGate>();
            if (_font == null) _font = Resources.Load<Font>("Fonts/GameThai");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GetComponent<Canvas>().sortingOrder = 210;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            Build();
        }

        private void OnDestroy()
        {
            if (_modalShown) PlayerCameraController.LocalInstance?.SetCursorLock(true);
            BlocksGameplayInput = false;
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            if (_player == null && manager != null && manager.IsListening && manager.LocalClient?.PlayerObject != null)
                _player = manager.LocalClient.PlayerObject.GetComponent<PlayerExpeditionState>();
            if (_mission == null || _player == null || !_player.IsSpawned) return;
            if (_mission.Phase.Value == 3)
            {
                ShowModal(false);
                MissionFailUI.EnsureInstance().Show();
                return;
            }
            bool selecting = _player.Card.Value == 0 && _mission.Phase.Value < 2;
            bool summary = _mission.Phase.Value == 2 && !_summaryDismissed;
            bool modal = (selecting || summary) && !PauseMenu.IsPaused;
            ShowModal(modal);
            _cards.SetActive(selecting);
            _shop.SetActive(summary);
            if (Keyboard.current != null && !PauseMenu.IsPaused && !MissionFailUI.IsVisible)
            {
                var keyboard = Keyboard.current;
                int slot = keyboard.digit1Key.wasPressedThisFrame ? 0 :
                    keyboard.digit2Key.wasPressedThisFrame ? 1 : keyboard.digit3Key.wasPressedThisFrame ? 2 : -1;
                if (slot >= 0)
                {
                    if (selecting) _player.SelectCardRpc((byte)(slot + 1));
                    else if (!summary) _player.UseItemRpc(slot);
                }
                if (summary && keyboard.enterKey.wasPressedThisFrame) Dismiss();
                else if (_mission.Phase.Value == 2 && keyboard.bKey.wasPressedThisFrame)
                    _summaryDismissed = !_summaryDismissed;
            }
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + .2f;
            Refresh(selecting, summary);
        }

        private void ShowModal(bool show)
        {
            if (_modalShown == show) return;
            _modalShown = show;
            _modal.SetActive(show);
            BlocksGameplayInput = show;
            if (show && EventSystem.current == null)
                new GameObject("Expedition EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (!PauseMenu.IsPaused && !MissionFailUI.IsVisible)
                PlayerCameraController.LocalInstance?.SetCursorLock(!show);
        }

        private void Refresh(bool selecting, bool summary)
        {
            string objective = _mission.Phase.Value == 2 ?
                (_mission.Portal != null ? "ส่งของสำเร็จ — เดินเข้าประตูไป Level02" : "ส่งของสำเร็จ — จบด่านแล้ว") :
                _mission.Objective;
            string detail;
            if (_mission.Phase.Value == 2)
            {
                var result = _mission.Delivery.LastDeliveryResult;
                detail = $"HP {result.RemainingHP} × {result.Multiplier} = {result.FinalScore} คะแนน   |   B เปิดร้านค้า";
                if (_mission.Portal != null)
                    detail += _mission.Portal.IsOpen ? "\nประตูด่านถัดไปเปิดแล้ว!" : "\nกำลังเตรียมประตู...";
            }
            else if (_mission.Cargo != null)
            {
                int count = _mission.Cargo.CurrentCarrierCount;
                detail = $"ลัง HP {_mission.Cargo.CurrentHP.Value}/{_mission.Cargo.MaxHP}   |   ผู้ยก {count}/4\n" +
                    (count >= 2 ? "ยกด้วยกัน: Stamina ไม่ลด • คลิกซ้ำเพื่อปล่อย" : "เมาส์ซ้าย/ขวา: จับหรือปล่อย • E สองมือ • Q โยน");
            }
            else detail = "กำลังเตรียมลัง...";
            if (_lever != null && _lever.IsLocalNear && _mission.Phase.Value < 2)
                detail = "กด E ค้างเพื่อเปิดประตู 8 วินาที\nให้เพื่อนช่วยขนลังผ่านประตู";
            string inventory = $"{PlayerExpeditionState.CardName(_player.Card.Value)}   |   เงิน {_player.Coins.Value}\n" +
                $"[1] {PlayerExpeditionState.ItemName(_player.Slot1.Value)}\n[2] {PlayerExpeditionState.ItemName(_player.Slot2.Value)}\n[3] {PlayerExpeditionState.ItemName(_player.Slot3.Value)}";
            SetChanged(_objective, objective, ref _lastObjective);
            SetChanged(_detail, detail, ref _lastDetail);
            SetChanged(_inventory, inventory, ref _lastInventory);
            if (selecting)
            {
                _title.text = "เลือกการ์ดสำหรับด่านนี้";
                _message.text = "เลือก 1 ใบด้วยปุ่ม 1–3 หรือคลิก • มีผลเฉพาะการขนลัง";
            }
            if (summary)
            {
                var result = _mission.Delivery.LastDeliveryResult;
                _title.text = "ส่งลังสำเร็จ!";
                _message.text = $"HP เหลือ {result.RemainingHP} × ตัวคูณ {result.Multiplier} = {result.FinalScore} คะแนน\nได้รับ {result.CoinsEarned} เหรียญต่อคน • ไอเทมติดไปด่านถัดไป";
                _shopWallet.text = $"เงิน {_player.Coins.Value}   |   ช่องไอเทม 3 ช่อง   |   ซื้อได้ก่อนเข้าประตู";
                bool full = _player.Slot1.Value != 0 && _player.Slot2.Value != 0 && _player.Slot3.Value != 0;
                for (int i = 0; i < 2; i++) _buy[i].interactable = !full && _player.Coins.Value >= PlayerExpeditionState.ItemPrice((byte)(i + 1));
            }
        }

        private static void SetChanged(Text text, string value, ref string cached)
        {
            if (cached == value) return;
            cached = value; text.text = value;
        }

        private void Dismiss() { _summaryDismissed = true; ShowModal(false); }

        private GameObject Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private Text Label(Transform parent, string name, string value, int fontSize, Vector2 pos, Vector2 size, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = pos; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = _font; text.fontSize = fontSize;
            text.text = value; text.color = Color.white; text.alignment = alignment; text.raycastTarget = false;
            return text;
        }

        private Button Button(Transform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var go = Box(label, parent, new Vector2(.5f, .5f), position, size, new Color(.2f, .32f, .28f));
            var button = go.AddComponent<Button>();
            go.AddComponent<UIButtonHover>();
            button.onClick.AddListener(action);
            Label(go.transform, "Label", label, 23, Vector2.zero, size - new Vector2(20, 10), TextAnchor.MiddleCenter);
            return button;
        }

        private void Build()
        {
            var objective = Box("Objective", transform, new Vector2(1f, 1f), new Vector2(-365, -95), new Vector2(680, 145), new Color(.035f, .075f, .065f, 1f));
            objective.GetComponent<Image>().raycastTarget = false;
            _objective = Label(objective.transform, "Goal", "นำลังไม้ไปส่งที่ปราสาท", 28, new Vector2(0, 38), new Vector2(635, 44));
            _objective.color = new Color(1f, .85f, .38f);
            _detail = Label(objective.transform, "Status", "กำลังเตรียมภารกิจ...", 21, new Vector2(0, -24), new Vector2(635, 78));
            var inventory = Box("Inventory Panel", transform, new Vector2(1f, 1f), new Vector2(-290, -275), new Vector2(540, 145), new Color(.035f, .075f, .065f, .94f));
            inventory.GetComponent<Image>().raycastTarget = false;
            _inventory = Label(inventory.transform, "Inventory", "", 21, Vector2.zero, new Vector2(500, 125));
            _modal = Box("Mission Menu", transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1080), new Color(.015f, .035f, .03f, .8f));
            var panel = Box("Panel", _modal.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 560), new Color(.07f, .14f, .12f));
            _title = Label(panel.transform, "Title", "", 40, new Vector2(0, 208), new Vector2(1020, 66), TextAnchor.MiddleCenter);
            _title.color = new Color(1, .85f, .38f);
            _message = Label(panel.transform, "Message", "", 24, new Vector2(0, 124), new Vector2(1020, 100), TextAnchor.MiddleCenter);
            _cards = new GameObject("Cards", typeof(RectTransform)); _cards.transform.SetParent(panel.transform, false);
            Button(_cards.transform, "[1] ENDURANCE\nยกคนเดียวใช้ Stamina\nลดลง 40%", new Vector2(-340, -42), new Vector2(310, 190), () => _player?.SelectCardRpc(1));
            Button(_cards.transform, "[2] CARGO GUARD\nลังที่กำลังช่วยยก\nรับความเสียหายลด 50%", new Vector2(0, -42), new Vector2(310, 190), () => _player?.SelectCardRpc(2));
            Button(_cards.transform, "[3] TEAMWORK\nเมื่อยกด้วยกัน 2 คนขึ้นไป\nเดินขณะยกเร็วขึ้น 15%", new Vector2(340, -42), new Vector2(310, 190), () => _player?.SelectCardRpc(3));
            _shop = new GameObject("Shop", typeof(RectTransform)); _shop.transform.SetParent(panel.transform, false);
            _shopWallet = Label(_shop.transform, "Wallet", "", 22, new Vector2(0, 35), new Vector2(1020, 50), TextAnchor.MiddleCenter);
            _buy[0] = Button(_shop.transform, "Stamina Elixir — 40 เหรียญ\nฟื้น Stamina เต็ม 1 ครั้ง", new Vector2(-250, -58), new Vector2(460, 120), () => _player?.BuyItemRpc(1));
            _buy[1] = Button(_shop.transform, "Cargo Padding — 60 เหรียญ\nลดความเสียหายครั้งถัดไป 50%\nใช้ขณะจับลัง", new Vector2(250, -58), new Vector2(460, 120), () => _player?.BuyItemRpc(2));
            Button(_shop.transform, "เดินต่อ / ENTER", new Vector2(0, -204), new Vector2(340, 62), Dismiss);
            _modal.SetActive(false);
        }
    }
}
