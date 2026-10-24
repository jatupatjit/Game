using System;
using CoopGame.CarrySystem;
using CoopGame.Player;
using TMPro;
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
        /// <summary>True while the card select / shop / card view overlay is open; other HUD pieces hide themselves.</summary>
        public static bool OverlayOpen { get; private set; }
        public static bool CardViewOpen { get; private set; }
        /// <summary>Frame in which Esc closed the card view (PauseMenu must not also open).</summary>
        public static int EscConsumedFrame { get; private set; } = -1;
        private GameObject _view;
        private bool _viewOpen;
        private Image _viewOwnArt;
        private Text _viewName, _viewEffect, _viewTip, _viewMatesTitle;
        private readonly Image[] _viewMateArt = new Image[3];
        private readonly Text[] _viewMateName = new Text[3];
        private readonly CanvasGroup[] _hudGroups = new CanvasGroup[5];
        [SerializeField] private Font _font;

        /// <summary>Position/size of one HUD element, editable in the Inspector (applied on Play, live while playing).</summary>
        [Serializable]
        public struct Slot
        {
            [Tooltip("Anchored position from the element's anchor.")] public Vector2 Position;
            [Tooltip("Width/height. Height is ignored for elements that size themselves from content.")] public Vector2 Size;
            public Slot(float x, float y, float w, float h) { Position = new Vector2(x, y); Size = new Vector2(w, h); }
        }

        [Header("Layout (reference 1920x1080)")]
        [SerializeField] private Slot _missionPanel = new Slot(-300, -78, 560, 116);
        [SerializeField] private Slot _contextChip = new Slot(-300, -146, 560, 60);
        [SerializeField] private Slot _runInfo = new Slot(0, -22, 480, 10);
        [SerializeField] private Slot _inventory = new Slot(-150, -244, 260, 10);
        [SerializeField] private Slot _controlHints = new Slot(24, 24, 10, 46);
        [SerializeField] private Slot _ownCard = new Slot(-72, 72, 84, 84);
        [Header("Card select")]
        [SerializeField] private float _cardWidth = 340f;
        [SerializeField] private float _cardHeight = 480f;
        [SerializeField] private float _cardSpacing = 390f;
        [SerializeField] private float _cardsY = -36f;
        [Header("Card view (H)")]
        [SerializeField] private Vector2 _viewPanelSize = new Vector2(1500, 940);
        private RectTransform _rMission, _rChip, _rRun, _rInv, _rHints, _rOwn;

        private static void Place(RectTransform r, Slot s, bool setHeight = true)
        {
            if (r == null) return;
            r.anchoredPosition = s.Position;
            r.sizeDelta = setHeight ? s.Size : new Vector2(s.Size.x, r.sizeDelta.y);
        }

        private void ApplyLayout()
        {
            Place(_rMission, _missionPanel);
            Place(_rChip, _contextChip, false);
            Place(_rRun, _runInfo, false);
            Place(_rInv, _inventory, false);
            Place(_rHints, _controlHints, false);
            Place(_rOwn, _ownCard);
        }

        private void OnValidate()
        {
            if (Application.isPlaying && _rMission != null) ApplyLayout();
        }
        private LevelMission _mission;
        private PlayerExpeditionState _player;
        private TeamLeverGate _lever;
        private ClimbSwitchGate[] _climbGates;
        private Text _objective, _detail, _title, _message;
        private GameObject _modal, _cards, _shop;
        private Text _shopWallet;
        private readonly Button[] _buy = new Button[2];
        private float _refreshAt;
        private bool _summaryDismissed;
        private bool _modalShown;
        private string _lastObjective, _lastDetail;
        // UI kit layout (mockups 2/3): cargo HP row in the mission panel, own card icon bottom-right,
        // card select with a selection frame + confirm button.
        private Text _hpText;
        private Image _ownCardIcon;
        private UITooltip _ownCardTip;
        private readonly Image[] _cardFrames = new Image[3];
        private readonly UIButtonHover[] _cardHover = new UIButtonHover[3];
        private Button _confirmCard;
        private int _pendingSlot = -1;
        private readonly Image[] _cardArt = new Image[3];
        private uint _shownOffer;
        private Text _carriersText, _coinText;
        private readonly Text[] _slotNames = new Text[3];
        private GameObject _detailChip, _inventoryRoot, _hintBar;
        // Run timer (counts up), side mission line, nearest checkpoint line. Only present in levels with a LevelRunState.
        private LevelRunState _run;
        private CheckpointManager _checkpoints;
        private GameObject _timerChip;
        private Text _timerText, _sideText, _padText;
        private int _shownSecond = -1, _shownSide = -1, _shownPadDistance = -1, _shownPad = -2, _noticeSeq;
        private float _noticeUntil;
        private LevelNotice _notice;

        private void Awake()
        {
            _mission = FindAnyObjectByType<LevelMission>();
            _run = FindAnyObjectByType<LevelRunState>();
            _checkpoints = FindAnyObjectByType<CheckpointManager>();
            _lever = FindAnyObjectByType<TeamLeverGate>();
            _climbGates = FindObjectsByType<ClimbSwitchGate>();
            _font = UITheme.Font; // theme font (Mitr); ignores any scene-serialized font
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
            if (_modalShown || _viewOpen) PlayerCameraController.LocalInstance?.SetCursorLock(true);
            BlocksGameplayInput = false;
            OverlayOpen = false;
            CardViewOpen = false;
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            if (_player == null && manager != null && manager.IsListening && manager.LocalClient?.PlayerObject != null)
                _player = manager.LocalClient.PlayerObject.GetComponent<PlayerExpeditionState>();
            if (_mission == null || _player == null || !_player.IsSpawned) return;
            if (_mission.Phase.Value == 3)
            {
                _viewOpen = false; CardViewOpen = false; OverlayOpen = false;
                ShowModal(false);
                MissionFailUI.EnsureInstance().Show();
                return;
            }
            uint offer = _player.Offer.Value;
            bool selecting = _player.Card.Value == 0 && _mission.Phase.Value < 2 && CardCatalog.HasOffer(offer);
            bool summary = _mission.Phase.Value == 2 && !_summaryDismissed;
            bool modal = (selecting || summary) && !PauseMenu.IsPaused;
            // Card view (H): only after a card is chosen, never during select/shop, pause or mission fail.
            bool canView = !modal && !summary && !selecting && _player.Card.Value != 0 && !PauseMenu.IsPaused && !MissionFailUI.IsVisible;
            if (Keyboard.current != null)
            {
                if (canView && Keyboard.current.hKey.wasPressedThisFrame) _viewOpen = !_viewOpen;
                else if (_viewOpen && Keyboard.current.escapeKey.wasPressedThisFrame) { _viewOpen = false; EscConsumedFrame = Time.frameCount; }
            }
            if (_viewOpen && !canView) _viewOpen = false;
            if (_view.activeSelf != _viewOpen)
            {
                _view.SetActive(_viewOpen);
                if (_viewOpen) RefreshView();
            }
            CardViewOpen = _viewOpen;
            ShowModal(modal);
            _cards.SetActive(selecting);
            _shop.SetActive(summary);
            // HUD pieces that would compete with the card-select / shop / card-view overlay stay hidden while it is open.
            bool overlay = modal || _viewOpen;
            OverlayOpen = overlay;
            if (_inventoryRoot.activeSelf == overlay) _inventoryRoot.SetActive(!overlay);
            float hudAlpha = overlay ? 0f : 1f;
            for (int i = 0; i < _hudGroups.Length; i++)
                if (_hudGroups[i] != null && _hudGroups[i].alpha != hudAlpha) { _hudGroups[i].alpha = hudAlpha; _hudGroups[i].blocksRaycasts = !overlay; }
            bool hints = !overlay && _mission.Phase.Value < 2;
            if (_hintBar.activeSelf != hints) _hintBar.SetActive(hints);
            if (Keyboard.current != null && !PauseMenu.IsPaused && !MissionFailUI.IsVisible)
            {
                var keyboard = Keyboard.current;
                int slot = keyboard.digit1Key.wasPressedThisFrame ? 0 :
                    keyboard.digit2Key.wasPressedThisFrame ? 1 : keyboard.digit3Key.wasPressedThisFrame ? 2 : -1;
                if (slot >= 0)
                {
                    if (selecting) _player.SelectCardRpc(CardCatalog.OfferAt(offer, slot));
                    else if (!summary) _player.UseItemRpc(slot);
                }
                if (summary && keyboard.enterKey.wasPressedThisFrame) Dismiss();
                else if (_mission.Phase.Value == 2 && keyboard.bKey.wasPressedThisFrame)
                    _summaryDismissed = !_summaryDismissed;
            }
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + .2f;
            Refresh(selecting, summary);
            if (_viewOpen) RefreshView();
        }

        private bool _lastBlock;

        private void ShowModal(bool show)
        {
            if (_modalShown != show) { _modalShown = show; _modal.SetActive(show); }
            bool block = _modalShown || _viewOpen;
            if (block == _lastBlock) return;
            _lastBlock = block;
            BlocksGameplayInput = block;
            if (block && EventSystem.current == null)
                new GameObject("Expedition EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (!PauseMenu.IsPaused && !MissionFailUI.IsVisible)
                PlayerCameraController.LocalInstance?.SetCursorLock(!block);
        }

        /// <summary>Own card full-art plus small teammate thumbnails with names (replicated Card + DisplayName).</summary>
        private void RefreshView()
        {
            byte own = _player.Card.Value;
            _viewOwnArt.sprite = CardCatalog.Art(own);
            _viewName.text = own == 0 ? "ยังไม่ได้เลือกการ์ด" : CardCatalog.Name(own);
            _viewEffect.text = own == 0 ? "" : CardCatalog.Description(own);
            _viewTip.text = CardTip(own);
            var mates = new System.Collections.Generic.List<PlayerExpeditionState>(FindObjectsByType<PlayerExpeditionState>());
            mates.RemoveAll(p => p == null || !p.IsSpawned || p == _player);
            mates.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            _viewMatesTitle.gameObject.SetActive(mates.Count > 0);
            for (int i = 0; i < _viewMateArt.Length; i++)
            {
                bool has = i < mates.Count;
                _viewMateArt[i].transform.parent.gameObject.SetActive(has);
                if (!has) continue;
                byte card = mates[i].Card.Value;
                _viewMateArt[i].sprite = card != 0 ? CardCatalog.Art(card) : UITheme.CardBack(1);
                string name = mates[i].DisplayName.Value.ToString();
                _viewMateName[i].text = string.IsNullOrEmpty(name) ? $"Player {mates[i].OwnerClientId + 1}" : name;
            }
        }

        private void Refresh(bool selecting, bool summary)
        {
            string objective = _mission.Phase.Value == 2 ?
                (_mission.Portal != null ? "ส่งของสำเร็จ — เดินเข้าประตูไป Level02" : "ส่งของสำเร็จ — จบด่านแล้ว") :
                _mission.Objective;

            // Mission panel (compact, mockup 3): title + cube + HP, small carrier count.
            string hp = _mission.Cargo != null ? $"HP {_mission.Cargo.CurrentHP.Value}/{_mission.Cargo.MaxHP}" : "HP --";
            if (_hpText.text != hp) _hpText.text = hp;
            string carriers = _mission.Cargo != null ? $"ผู้ยก {_mission.Cargo.CurrentCarrierCount}/4" : "";
            if (_carriersText.text != carriers) _carriersText.text = carriers;

            // Context chip under the panel: only situational messages (the standing controls moved to
            // the key-cap hint bar), hidden when there is nothing to say.
            string detail = "";
            if (_mission.Phase.Value == 2)
            {
                var result = _mission.Delivery.LastDeliveryResult;
                detail = $"HP {result.RemainingHP} × {result.Multiplier} = {result.FinalScore} คะแนน   |   B เปิดร้านค้า";
                if (_mission.Portal != null)
                    detail += _mission.Portal.IsOpen ? "\nประตูด่านถัดไปเปิดแล้ว!" : "\nกำลังเตรียมประตู...";
            }
            else if (_mission.Cargo == null) detail = "กำลังเตรียมลัง...";
            else if (_mission.Cargo.CurrentCarrierCount >= 2) detail = "ช่วยกันยกแล้วไม่เสียแรง • คลิกอีกครั้งเพื่อปล่อย";
            var localCarry = _player.GetComponent<PlayerCarry>();
            if (localCarry != null && localCarry.PlacementFeedback != null)
                detail = localCarry.PlacementFeedback;
            if (_lever != null && _lever.IsLocalNear && _mission.Phase.Value < 2)
                detail = $"กด E ค้าง ประตูจะเปิด {_lever.OpenDurationSeconds:0.#} วินาที\nให้เพื่อนขนลังผ่าน แล้วค่อยตามไป";
            if (_mission.Phase.Value < 2)
                for (int i = 0; i < _climbGates.Length; i++)
                    if (_climbGates[i] != null && !_climbGates[i].IsOpen && _climbGates[i].IsLocalNear)
                    {
                        detail = "กด E เปิดประตูให้เพื่อน\nเปิดแล้ว ลงไปช่วยขนลังได้เลย";
                        break;
                    }
            if (string.IsNullOrEmpty(detail) && _mission.Cargo != null && _mission.Cargo.SoloAssistActive && _mission.Phase.Value < 2)
                detail = "เล่นคนเดียว: ลังเบาลง 50%";
            RefreshRunInfo(ref detail);
            SetChanged(_objective, objective, ref _lastObjective);
            SetChanged(_detail, detail, ref _lastDetail);
            bool showDetail = !string.IsNullOrEmpty(detail);
            if (_detailChip.activeSelf != showDetail) _detailChip.SetActive(showDetail);

            // Inventory (kit rows): coins + three key-cap item slots.
            string coins = $"เงิน {_player.Coins.Value}";
            if (_coinText.text != coins) _coinText.text = coins;
            for (int i = 0; i < 3; i++)
            {
                byte item = _player.GetSlot(i);
                string name = PlayerExpeditionState.ItemName(item);
                if (_slotNames[i].text != name) _slotNames[i].text = name;
                _slotNames[i].color = UITheme.TextColor(item == 0 ? UITextRole.Secondary : UITextRole.Primary);
            }

            byte ownCard = _player.Card.Value;
            Sprite icon = UITheme.CardIcon(ownCard);
            if (_ownCardIcon.sprite != icon) _ownCardIcon.sprite = icon;
            _ownCardTip.Text = CardCatalog.Tooltip(ownCard);

            uint offer = _player.Offer.Value;
            if (offer != _shownOffer)
            {
                // A new draw: show the three full card images and clear any previous preview.
                _shownOffer = offer;
                _pendingSlot = -1;
                for (int i = 0; i < _cardArt.Length; i++) _cardArt[i].sprite = CardCatalog.Art(CardCatalog.OfferAt(offer, i));
            }
            if (!selecting) _pendingSlot = -1;
            for (int i = 0; i < _cardFrames.Length; i++)
            {
                bool selected = selecting && _pendingSlot == i;
                if (_cardFrames[i] != null) _cardFrames[i].enabled = selected;
                _cardHover[i]?.SetBaseScale(selected ? 1.05f : 1f);
            }
            if (_confirmCard != null) _confirmCard.interactable = selecting && _pendingSlot >= 0;
            if (selecting)
            {
                _title.text = "เลือกการ์ดสำหรับด่านนี้";
                _message.text = "สุ่มมา 3 ใบ เลือก 1 ใบด้วยปุ่ม 1–3 หรือคลิกแล้วกดยืนยัน";
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

        private static string NoticeText(LevelNotice notice, LevelRunState run) => notice switch
        {
            LevelNotice.CheckpointSaved => "เซฟเช็คพอยต์แล้ว",
            LevelNotice.CargoFell => "ลังตกเหว! กลับมาที่เช็คพอยต์ (เลือดไม่ฟื้น)",
            LevelNotice.CargoRestart => "ลังพัง! เริ่มใหม่จากเช็คพอยต์",
            LevelNotice.RepairKit => "ใช้ชุดซ่อมแล้ว +20 เลือดลัง",
            LevelNotice.KitFull => "เลือดลังเต็มอยู่แล้ว เก็บชุดซ่อมไว้ก่อน",
            LevelNotice.SideSuccess => "ภารกิจเสริมสำเร็จ! +" + (run != null ? run.BonusCoins : 0) + " เหรียญ และดาว",
            LevelNotice.SideMissed => "ภารกิจเสริมหมดเวลาแล้ว (ไม่มีผลต่อคะแนน)",
            _ => ""
        };

        private static void SetMmSs(ref string cache, ref int shown, int seconds, string prefix)
        {
            if (shown == seconds) return;
            shown = seconds;
            cache = prefix + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        private string _timerString = "", _sideString = "", _padString = "";

        /// <summary>Timer, side mission and checkpoint lines plus one-shot notices. Strings are rebuilt only when the shown value changes.</summary>
        private void RefreshRunInfo(ref string detail)
        {
            if (_run == null || _timerChip == null) return;
            bool show = _run.IsSpawned && _mission.Phase.Value < 3;
            if (_timerChip.activeSelf != show) _timerChip.SetActive(show);
            if (!show) return;
            int seconds = Mathf.FloorToInt(_run.ElapsedSeconds);
            if (seconds != _shownSecond)
            {
                SetMmSs(ref _timerString, ref _shownSecond, seconds, "เวลา ");
                _timerText.text = _timerString;
            }
            // side mission line: state -> text
            byte side = _run.SideState;
            int sideKey = side == 0 ? 100000 + Mathf.Max(0, Mathf.FloorToInt(_run.SideLimitSeconds - _run.ElapsedSeconds)) : side;
            if (sideKey != _shownSide)
            {
                _shownSide = sideKey;
                if (side == 1) { _sideString = "ภารกิจเสริมสำเร็จ  ดาว 1"; _sideText.color = UITheme.TextColor(UITextRole.Positive); }
                else if (side == 2) { _sideString = "ภารกิจเสริมหมดเวลา"; _sideText.color = UITheme.TextColor(UITextRole.Secondary); }
                else
                {
                    int left = sideKey - 100000;
                    _sideString = "ภารกิจเสริม: ถึงเกาะชุดซ่อมก่อน " + (left / 60).ToString("00") + ":" + (left % 60).ToString("00");
                    _sideText.color = UITheme.TextColor(UITextRole.Accent);
                }
                _sideText.text = _sideString;
            }
            // nearest checkpoint line: the active pad number and the distance from the local player
            var pad = _checkpoints != null ? _checkpoints.Current : null;
            var local = _player != null ? _player.transform : null;
            int padNumber = pad != null && pad.IsActive ? pad.Number : 0;
            int distance = pad != null && local != null ? Mathf.RoundToInt(Vector3.Distance(local.position, pad.transform.position)) : -1;
            if (padNumber != _shownPad || distance != _shownPadDistance)
            {
                _shownPad = padNumber; _shownPadDistance = distance;
                _padString = padNumber == 0 ? "เช็คพอยต์: ยังไม่ได้เซฟ (วางลังบนแท่นทอง)" : "เช็คพอยต์ล่าสุด " + padNumber + "  ห่าง " + distance + " ม.";
                _padText.text = _padString;
            }
            // notices: shown in the context chip for a few seconds
            if (_run.NoticeSequence != _noticeSeq)
            {
                _noticeSeq = _run.NoticeSequence;
                _notice = _run.Notice;
                _noticeUntil = Time.unscaledTime + 4.5f;
            }
            if (_notice != LevelNotice.None && Time.unscaledTime < _noticeUntil) detail = NoticeText(_notice, _run);
        }

        private static void SetChanged(Text text, string value, ref string cached)
        {
            if (cached == value) return;
            cached = value; text.text = value;
        }

        private void Dismiss() { _summaryDismissed = true; ShowModal(false); }

        private GameObject Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, Sprite shape = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            if (shape != null) UITheme.StyleShape(image, shape, color);
            else image.color = color;
            return go;
        }

        private Text Label(Transform parent, string name, string value, int fontSize, Vector2 pos, Vector2 size,
            TextAnchor alignment = TextAnchor.MiddleLeft, UITextRole role = UITextRole.Primary)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = pos; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = _font; text.fontSize = fontSize;
            text.text = value; text.color = UITheme.TextColor(role); text.alignment = alignment; text.raycastTarget = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button Button(Transform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action,
            UIButtonStyle style = UIButtonStyle.Secondary)
        {
            var go = Box(label, parent, new Vector2(.5f, .5f), position, size, Color.white);
            var button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            go.AddComponent<UIButtonHover>();
            button.onClick.AddListener(action);
            Label(go.transform, "Label", label, 23, Vector2.zero, size - new Vector2(20, 10), TextAnchor.MiddleCenter);
            UITheme.StyleButton(button, style);
            return button;
        }

        private void Build()
        {
            // Mission panel (mockup 3, top-right): dark panel matching the other HUD panels, mission title, cube + large HP,
            // small carrier count on the right.
            var objective = Box("Objective", transform, new Vector2(1f, 1f), new Vector2(-300, -78), new Vector2(560, 116), UITheme.SolidPanel, UITheme.SmallSprite);
            objective.GetComponent<Image>().raycastTarget = false;
            _objective = Label(objective.transform, "Goal", "นำลังไม้ไปส่งที่ปราสาท", 26, new Vector2(0, 26), new Vector2(516, 40));
            UITheme.SetWeight(_objective, UITheme.FontSemiBold);
            var cube = Box("CargoIcon", objective.transform, new Vector2(.5f, .5f), new Vector2(-230, -22), new Vector2(54, 54), Color.white);
            UITheme.StyleIcon(cube.GetComponent<Image>(), UITheme.IconCargoHud);
            _hpText = Label(objective.transform, "CargoHP", "HP --", 30, new Vector2(-78, -22), new Vector2(250, 44));
            UITheme.SetWeight(_hpText, UITheme.FontBold);
            _carriersText = Label(objective.transform, "Carriers", "", 17, new Vector2(122, -22), new Vector2(262, 40), TextAnchor.MiddleRight);
            _carriersText.color = UITheme.TextSecondary;

            // Situational message chip under the mission panel (hidden when empty).
            _detailChip = Box("ContextChip", transform, new Vector2(1f, 1f), new Vector2(-300, -146), new Vector2(560, 60), UITheme.SolidPanel, UITheme.SmallSprite);
            var chipRect = (RectTransform)_detailChip.transform;
            chipRect.pivot = new Vector2(.5f, 1f);
            _detailChip.GetComponent<Image>().raycastTarget = false;
            var chipLayout = _detailChip.AddComponent<VerticalLayoutGroup>();
            chipLayout.padding = new RectOffset(16, 16, 8, 8);
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = true; chipLayout.childForceExpandHeight = false;
            var chipFit = _detailChip.AddComponent<ContentSizeFitter>();
            chipFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _detail = Label(_detailChip.transform, "Status", "", 19, Vector2.zero, new Vector2(528, 40));
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailChip.SetActive(false);

            // Run timer + side mission + checkpoint (top-centre, only built for levels with a LevelRunState).
            if (_run != null)
            {
                _timerChip = Box("RunInfo", transform, new Vector2(.5f, 1f), new Vector2(0, -22), new Vector2(480, 10), UITheme.SolidPanel, UITheme.SmallSprite);
                ((RectTransform)_timerChip.transform).pivot = new Vector2(.5f, 1f);
                _timerChip.GetComponent<Image>().raycastTarget = false;
                var runLayout = _timerChip.AddComponent<VerticalLayoutGroup>();
                runLayout.padding = new RectOffset(16, 16, 8, 10);
                runLayout.spacing = 2f; runLayout.childAlignment = TextAnchor.UpperCenter;
                runLayout.childControlWidth = runLayout.childControlHeight = true;
                runLayout.childForceExpandWidth = true; runLayout.childForceExpandHeight = false;
                _timerChip.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                _timerText = UITheme.MakeText(_timerChip.transform, "Timer", "เวลา 00:00", 30, UITextRole.Primary, TextAnchor.MiddleCenter);
                UITheme.SetWeight(_timerText, UITheme.FontBold);
                UITheme.SetPreferredHeight(_timerText.gameObject, 38f);
                _sideText = UITheme.MakeText(_timerChip.transform, "SideMission", "", 19, UITextRole.Accent, TextAnchor.MiddleCenter);
                UITheme.SetPreferredHeight(_sideText.gameObject, 26f);
                _padText = UITheme.MakeText(_timerChip.transform, "Checkpoint", "", 17, UITextRole.Secondary, TextAnchor.MiddleCenter);
                UITheme.SetPreferredHeight(_padText.gameObject, 24f);
                _timerChip.SetActive(false);
            }

            // Inventory (kit): dark panel, coins row, three key-cap item slots. Hidden under overlays.
            _inventoryRoot = Box("Inventory Panel", transform, new Vector2(1f, 1f), new Vector2(-150, -244), new Vector2(260, 10), UITheme.SolidPanel, UITheme.SmallSprite);
            var invRect = (RectTransform)_inventoryRoot.transform;
            invRect.pivot = new Vector2(.5f, 1f);
            _inventoryRoot.GetComponent<Image>().raycastTarget = false;
            var invLayout = _inventoryRoot.AddComponent<VerticalLayoutGroup>();
            invLayout.padding = new RectOffset(18, 18, 14, 16);
            invLayout.spacing = 6f;
            invLayout.childControlWidth = invLayout.childControlHeight = true;
            invLayout.childForceExpandWidth = true; invLayout.childForceExpandHeight = false;
            _inventoryRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _coinText = UITheme.MakeText(_inventoryRoot.transform, "Coins", "เงิน 0", 20, UITextRole.Accent);
            UITheme.SetPreferredHeight(_coinText.gameObject, 30f);
            for (int i = 0; i < 3; i++)
            {
                var row = new GameObject($"Slot{i + 1}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(_inventoryRoot.transform, false);
                var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 10f; rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
                UITheme.SetPreferredHeight(row, 36f);
                KeyCap(row.transform, (i + 1).ToString(), 32f, 35f);
                _slotNames[i] = UITheme.MakeText(row.transform, "Item", "Empty", 18, UITextRole.Primary);
                _slotNames[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            // Control hints (bottom-left): key caps + short labels, subtle.
            _hintBar = Box("ControlHints", transform, new Vector2(0f, 0f), new Vector2(24, 24), new Vector2(10, 46), Color.clear);
            var hintRect = (RectTransform)_hintBar.transform;
            hintRect.pivot = new Vector2(0f, 0f);
            _hintBar.GetComponent<Image>().raycastTarget = false;
            var hintLayout = _hintBar.AddComponent<HorizontalLayoutGroup>();
            hintLayout.spacing = 8f; hintLayout.childAlignment = TextAnchor.MiddleLeft;
            hintLayout.childControlWidth = hintLayout.childControlHeight = true;
            hintLayout.childForceExpandWidth = hintLayout.childForceExpandHeight = false;
            var hintFit = _hintBar.AddComponent<ContentSizeFitter>();
            hintFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _hintBar.AddComponent<CanvasGroup>().alpha = 1f;
            Hint("คลิกซ้าย", "จับ", true);
            Hint("คลิกขวา", "วาง");
            Hint("Q", "โยน");

            // Own card icon (mockup 3, bottom-right) with the ability tooltip.
            var own = Box("OwnCardIcon", transform, new Vector2(1f, 0f), new Vector2(-72, 72), new Vector2(84, 84), Color.white);
            _ownCardIcon = own.GetComponent<Image>();
            UITheme.StyleIcon(_ownCardIcon, UITheme.CardIcon(0));
            _ownCardIcon.raycastTarget = true;
            _ownCardTip = own.AddComponent<UITooltip>();
            _ownCardTip.Side = new Vector2(-1f, 0f);
            // "H ดูการ์ด" key hint to the left of the own-card icon (child of the icon, so it hides with it).
            var viewHint = Box("ViewCardHint", own.transform, new Vector2(0f, .5f), new Vector2(-12f, 0f), new Vector2(10f, 10f), Color.clear);
            ((RectTransform)viewHint.transform).pivot = new Vector2(1f, .5f);
            viewHint.GetComponent<Image>().raycastTarget = false;
            var viewLayout = viewHint.AddComponent<HorizontalLayoutGroup>();
            viewLayout.spacing = 8f; viewLayout.childAlignment = TextAnchor.MiddleRight;
            viewLayout.childControlWidth = viewLayout.childControlHeight = true;
            viewLayout.childForceExpandWidth = viewLayout.childForceExpandHeight = false;
            var viewFit = viewHint.AddComponent<ContentSizeFitter>();
            viewFit.horizontalFit = viewFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            KeyCap(viewHint.transform, "H", 32f, 35f);
            var viewLabel = UITheme.MakeText(viewHint.transform, "Hint H", "ดูการ์ด", 20, UITextRole.Primary);
            viewLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddOutline(viewLabel);
            var viewElement = viewLabel.gameObject.AddComponent<LayoutElement>();
            viewElement.preferredHeight = 35f;
            viewElement.minWidth = viewLabel.preferredWidth + 10f;

            // Card select / summary (mockup 2): banner header flanked by card backs, message on a dark
            // strip, three tall cards with a selection frame, pink confirm button.
            _rMission = (RectTransform)objective.transform; _rChip = chipRect;
            _rRun = _timerChip != null ? (RectTransform)_timerChip.transform : null;
            _rInv = invRect; _rHints = hintRect; _rOwn = (RectTransform)own.transform;
            ApplyLayout();
            _hudGroups[0] = objective.AddComponent<CanvasGroup>();
            _hudGroups[1] = _detailChip.AddComponent<CanvasGroup>();
            _hudGroups[2] = _timerChip != null ? _timerChip.AddComponent<CanvasGroup>() : null;
            _hudGroups[3] = own.AddComponent<CanvasGroup>();

            // Full-screen dim + large soft dark panel behind the banner and cards (uGUI has no real blur).
            _modal = Box("Mission Menu", transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, UITheme.Overlay);
            StretchFull(_modal);
            var panel = Box("Panel", _modal.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 700), Color.clear);
            panel.GetComponent<Image>().raycastTarget = false;
            Backdrop(panel.transform);
            var banner = Box("Banner", panel.transform, new Vector2(.5f, .5f), new Vector2(0, 375), new Vector2(620, 92), UITheme.MissionRed, UITheme.SmallSprite);
            var leftBack = Box("BannerCardLeft", panel.transform, new Vector2(.5f, .5f), new Vector2(-340, 375), new Vector2(58, 88), Color.white);
            UITheme.StyleIcon(leftBack.GetComponent<Image>(), UITheme.CardBack(1));
            leftBack.transform.localRotation = Quaternion.Euler(0, 0, 10f);
            var rightBack = Box("BannerCardRight", panel.transform, new Vector2(.5f, .5f), new Vector2(340, 375), new Vector2(58, 88), Color.white);
            UITheme.StyleIcon(rightBack.GetComponent<Image>(), UITheme.CardBack(2));
            rightBack.transform.localRotation = Quaternion.Euler(0, 0, -10f);
            _title = Label(banner.transform, "Title", "", 40, Vector2.zero, new Vector2(600, 80), TextAnchor.MiddleCenter);
            UITheme.SetWeight(_title, UITheme.FontBold);
            var strip = Box("MessageStrip", panel.transform, new Vector2(.5f, .5f), new Vector2(0, 300), new Vector2(760, 56), UITheme.SolidPanel, UITheme.SmallSprite);
            strip.GetComponent<Image>().raycastTarget = false;
            _message = Label(strip.transform, "Message", "", 22, Vector2.zero, new Vector2(740, 52), TextAnchor.MiddleCenter);

            _cards = new GameObject("Cards", typeof(RectTransform)); _cards.transform.SetParent(panel.transform, false);
            for (int slot = 0; slot < 3; slot++)
                CardButton(_cards.transform, slot, new Vector2((slot - 1) * CardSpacing, _cardsY));
            _confirmCard = Button(_cards.transform, "เลือกใบนี้", new Vector2(0, -385), new Vector2(320, 70), ConfirmCard, UIButtonStyle.Primary);

            _shop = new GameObject("Shop", typeof(RectTransform)); _shop.transform.SetParent(panel.transform, false);
            ((RectTransform)_shop.transform).anchoredPosition = new Vector2(0, 95); // shop sits under the raised banner
            var shopPanel = Box("ShopPanel", _shop.transform, new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(1060, 420), UITheme.Panel, UITheme.PanelSprite);
            shopPanel.GetComponent<Image>().raycastTarget = false;
            _shopWallet = Label(_shop.transform, "Wallet", "", 22, new Vector2(0, 110), new Vector2(1020, 50), TextAnchor.MiddleCenter, UITextRole.Secondary);
            _buy[0] = Button(_shop.transform, "Stamina Elixir — 40 เหรียญ\nฟื้น Stamina เต็ม 1 ครั้ง", new Vector2(-250, -10), new Vector2(460, 120), () => _player?.BuyItemRpc(1));
            _buy[1] = Button(_shop.transform, "Cargo Padding — 60 เหรียญ\nลดความเสียหายครั้งถัดไป 50%\nใช้ขณะจับลัง", new Vector2(250, -10), new Vector2(460, 120), () => _player?.BuyItemRpc(2));
            Button(_shop.transform, "เดินต่อ / ENTER", new Vector2(0, -170), new Vector2(340, 66), Dismiss, UIButtonStyle.Primary);
            _modal.SetActive(false);
            BuildView();
        }

        private static void StretchFull(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Large dark rounded panel (themed panel sprite) behind the card UI, sibling index 0.</summary>
        private void Backdrop(Transform parent, Vector2? size = null, Vector2? offset = null)
        {
            var go = Box("Backdrop", parent, new Vector2(.5f, .5f), offset ?? Vector2.zero, size ?? new Vector2(1280, 930), UITheme.Panel, UITheme.PanelSprite);
            go.GetComponent<Image>().raycastTarget = false;
            go.transform.SetAsFirstSibling();
        }

        /// <summary>H card view: same dim + backdrop, own card full-size, teammates as small thumbnails with names.</summary>
        private void BuildView()
        {
            _view = Box("Card View", transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, UITheme.Overlay);
            StretchFull(_view);
            var root = Box("Panel", _view.transform, new Vector2(.5f, .5f), Vector2.zero, _viewPanelSize, Color.clear);
            root.GetComponent<Image>().raycastTarget = false;
            Backdrop(root.transform, _viewPanelSize, Vector2.zero);
            var title = Label(root.transform, "Title", "การ์ดของคุณ", 44, new Vector2(0, 410), new Vector2(900, 64), TextAnchor.MiddleCenter);
            UITheme.SetWeight(title, UITheme.FontBold);
            // Own card, large, on the left; its name, effect and a plain-language tip on the right.
            var own = Box("OwnCard", root.transform, new Vector2(.5f, .5f), new Vector2(-400, -15), new Vector2(520, 735), Color.white);
            _viewOwnArt = own.GetComponent<Image>();
            _viewOwnArt.preserveAspect = true; _viewOwnArt.raycastTarget = false;
            _viewName = Label(root.transform, "CardName", "", 54, new Vector2(330, 250), new Vector2(720, 80), TextAnchor.MiddleLeft);
            UITheme.SetWeight(_viewName, UITheme.FontBold);
            _viewEffect = Label(root.transform, "CardEffect", "", 36, new Vector2(330, 150), new Vector2(720, 110), TextAnchor.UpperLeft);
            UITheme.SetWeight(_viewEffect, UITheme.FontSemiBold);
            _viewTip = Label(root.transform, "CardTip", "", 30, new Vector2(330, 10), new Vector2(720, 170), TextAnchor.UpperLeft, UITextRole.Secondary);
            _viewMatesTitle = Label(root.transform, "MatesTitle", "การ์ดของเพื่อน", 28, new Vector2(330, -120), new Vector2(720, 40), TextAnchor.MiddleLeft, UITextRole.Secondary);
            for (int i = 0; i < 3; i++)
            {
                var mate = Box($"Mate{i + 1}", root.transform, new Vector2(.5f, .5f), new Vector2(150 + i * 230, -300), new Vector2(210, 250), Color.clear);
                mate.GetComponent<Image>().raycastTarget = false;
                var art = Box("Art", mate.transform, new Vector2(.5f, .5f), new Vector2(0, 28), new Vector2(125, 176), Color.white);
                _viewMateArt[i] = art.GetComponent<Image>();
                _viewMateArt[i].preserveAspect = true; _viewMateArt[i].raycastTarget = false;
                _viewMateName[i] = Label(mate.transform, "Name", "", 22, new Vector2(0, -92), new Vector2(210, 34), TextAnchor.MiddleCenter, UITextRole.Secondary);
            }
            Label(root.transform, "Close", "H / Esc  ปิด", 24, new Vector2(0, -435), new Vector2(500, 36), TextAnchor.MiddleCenter, UITextRole.Secondary);
            _view.SetActive(false);
        }

        /// <summary>Plain-language explanation shown under the card effect in the H view.</summary>
        private static string CardTip(byte id)
        {
            switch (CardCatalog.Get(id).Key)
            {
                case "strong_arms": return "ลังที่ถือจะเบาลงสำหรับคุณ และเหนื่อยช้าลง ยกคนเดียวได้สบายกว่าเดิม";
                case "long_throw": return "กด Q ค้างเพื่อชาร์จแล้วปล่อย ลังจะไปได้ไกลขึ้น 40% เหมาะกับข้ามช่องว่างหรือส่งต่อให้เพื่อน";
                case "high_jumper": return "กระโดดสูงขึ้น ช่วยขึ้นแท่นสูงและข้ามหินให้ถึงง่ายขึ้น";
                case "endurance": return "ตอนแบกลังคนเดียว stamina จะลดช้าลง แบกได้ไกลขึ้นก่อนต้องพัก";
                case "teamwork": return "เมื่อมีคนช่วยยกตั้งแต่ 2 คนขึ้นไป ลังจะเบาลง 20% ยิ่งช่วยกัน ยิ่งเบา";
                case "steady_hands": return "ลังที่ถือจะสั่นและแกว่งน้อยลงครึ่งหนึ่ง เวลาเลี้ยวหรือกระโดดลังจะไม่เหวี่ยงง่าย";
                case "light_step": return "การวิ่งและการกระโดดเกาะผนังจะเสีย stamina น้อยลง 20%";
                case "double_jump": return "กดกระโดดซ้ำกลางอากาศได้อีก 1 ครั้ง ใช้ช่วยตอนพลาดจังหวะหรือข้ามช่องที่ไกล";
                case "quick_recovery": return "ตอนไม่ได้ถือของ stamina จะฟื้นเร็วขึ้น 30% สลับให้เพื่อนแบกแล้วพักเพื่อกลับมาแบกต่อได้ไว";
                case "climber": return "ปีนกำแพงและหอได้เร็วขึ้น 25% ช่วยให้ไปกดสวิตช์ที่สูงได้ทันใจ";
                case "last_mile": return "เมื่อลังเข้าใกล้จุดส่งของ (ประมาณ 12 เมตร) ลังจะเบาลง 30% ช่วยดันช่วงสุดท้ายให้จบ";
                case "close_support": return "เมื่อคุณยืนใกล้เพื่อนที่กำลังแบกของ (ภายใน 6 เมตร) stamina ของเพื่อนคนนั้นจะลดช้าลง 20% ไม่ต้องถือของเองก็ช่วยได้";
                case "fast_carrier": return "เดินเร็วขึ้น 15% ตอนแบกลัง ช่วยให้ส่งของเสร็จไวขึ้น";
                default: return "เลือกการ์ดก่อน แล้วกด H เพื่อดูคำอธิบายของการ์ดที่คุณได้";
            }
        }

        /// <summary>Key-cap sprite with a centred label (inventory slots, control hints).</summary>
        private void KeyCap(Transform parent, string key, float width, float height)
        {
            var cap = new GameObject("Key " + key, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cap.transform.SetParent(parent, false);
            var element = cap.GetComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = width;
            element.preferredHeight = element.minHeight = height;
            var image = cap.GetComponent<Image>();
            UITheme.StyleShape(image, width > height * 1.3f ? UITheme.Kit("KeyCapLong") : UITheme.KeyCapSprite, Color.white);
            image.raycastTarget = false;
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(cap.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = UITheme.TmpFont;
            label.fontSize = 17f;
            label.text = key;
            label.color = UITheme.TextColor(UITextRole.OnPrimary);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            ProceduralUIUtility.StretchFull(label.rectTransform);
            label.ForceMeshUpdate();
            float fittedWidth = Mathf.Ceil(label.preferredWidth) + 20f;
            element.preferredWidth = element.minWidth = Mathf.Max(width, fittedWidth);
        }

        private void Hint(string key, string action, bool wide = false)
        {
            KeyCap(_hintBar.transform, key, wide ? 56f : 32f, 35f);
            var text = UITheme.MakeText(_hintBar.transform, "Hint " + key, action, 20, UITextRole.Primary);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddOutline(text);
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 35f;
            element.minWidth = text.preferredWidth + 10f;
        }

        // Card art is 640:904. 340x480 reference px is 480 px tall at 1080p; the canvas scaler shrinks it to ~320 px at 720p.
        private float CardWidth => _cardWidth;
        private float CardHeight => _cardHeight;
        private float CardSpacing => _cardSpacing;

        /// <summary>One offered card slot: the full card image (name and effect are baked into the art) with a neon
        /// selection frame and a small lift when selected. Click previews; confirm (or keys 1-3) applies it.</summary>
        private void CardButton(Transform parent, int slot, Vector2 position)
        {
            var root = Box($"Card{slot + 1}", parent, new Vector2(.5f, .5f), position, new Vector2(CardWidth, CardHeight), Color.clear);
            var button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.transition = Selectable.Transition.None;
            _cardHover[slot] = root.AddComponent<UIButtonHover>();
            button.onClick.AddListener(() => { _pendingSlot = slot; _refreshAt = 0f; });

            var art = Box("Illustration", root.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(CardWidth, CardHeight), Color.white);
            _cardArt[slot] = art.GetComponent<Image>();
            _cardArt[slot].preserveAspect = true;
            _cardArt[slot].raycastTarget = false;

            var frame = Box("SelectedFrame", root.transform, new Vector2(.5f, .5f), new Vector2(0, -6f), new Vector2(CardWidth * 1.2f, CardHeight * 1.22f), Color.white);
            _cardFrames[slot] = frame.GetComponent<Image>();
            UITheme.StyleIcon(_cardFrames[slot], UITheme.CardFrameSprite);
            _cardFrames[slot].preserveAspect = false;
            _cardFrames[slot].enabled = false;
        }

        private void ConfirmCard()
        {
            if (_pendingSlot >= 0) _player?.SelectCardRpc(CardCatalog.OfferAt(_player.Offer.Value, _pendingSlot));
        }
    }
}
