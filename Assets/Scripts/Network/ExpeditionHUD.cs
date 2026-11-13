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
        // Last level (no exit portal): the host takes everyone back to the Lobby, still connected.
        private Button _toLobby;
        // Delivery result card (MissionCompleteUI) replaces the old summary panel; _cardPanel is the card-select card.
        private MissionCompleteUI _complete;
        private GameObject _cardPanel;
        private Text _toLobbyLabel;
        private bool _lobbyRequested;
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
        private Text _timerText, _sideText, _padText, _playText, _navText;
        private RectTransform _navArrow;
        private GameObject _navRow;
        private int _shownPlaySecond = -1, _shownNavDistance = -1;
        private string _playString = "", _navString = "", _navLabel;
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
            if (_complete != null) _complete.SetVisible(false);
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
            _shop.SetActive(false); // the old shop panel stays built (references) but MissionCompleteUI shows the result
            if (_cardPanel.activeSelf == summary) _cardPanel.SetActive(!summary);
            _complete.SetVisible(summary && !PauseMenu.IsPaused);
            if (summary)
            {
                bool host = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                string text = host ? (_lobbyRequested ? "กำลังกลับ Lobby..." : "กลับ Lobby พร้อมกัน") : "รอ Host พากลับ Lobby";
                if (_toLobby != null)
                {
                    _toLobby.interactable = host && !_lobbyRequested;
                    if (_toLobbyLabel != null && _toLobbyLabel.text != text) _toLobbyLabel.text = text;
                }
                _complete.SetLobbyButton(_mission.Portal == null, host && !_lobbyRequested, text);
            }
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
                    if (selecting) { _pendingSlot = slot; _refreshAt = 0f; }
                    else if (!summary) _player.UseItemRpc(slot);
                }
                if (selecting && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
                {
                    if (_pendingSlot < 0) _pendingSlot = 0;
                    ConfirmCard();
                }
                else if (summary && keyboard.enterKey.wasPressedThisFrame) Dismiss();
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
            if (block && !PauseMenu.IsPaused && !MissionFailUI.IsVisible && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
                PlayerCameraController.LocalInstance?.SetCursorLock(false);
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
                // A new draw: show the three full card images and preselect the first slot so confirm works immediately.
                _shownOffer = offer;
                _pendingSlot = 0;
                for (int i = 0; i < _cardArt.Length; i++) _cardArt[i].sprite = CardCatalog.Art(CardCatalog.OfferAt(offer, i));
            }
            if (!selecting) _pendingSlot = -1;
            else if (_pendingSlot < 0) _pendingSlot = 0;
            for (int i = 0; i < _cardFrames.Length; i++)
            {
                bool selected = selecting && _pendingSlot == i;
                if (_cardFrames[i] != null) _cardFrames[i].enabled = selected;
                _cardHover[i]?.SetBaseScale(selected ? 1.05f : 1f);
            }
            if (_confirmCard != null) _confirmCard.interactable = selecting && _pendingSlot >= 0;
            if (selecting)
            {
                _title.text = "เลือกการ์ด 1 ใบ";
                _message.text = "สุ่มมา 3 ใบ คลิกการ์ดหรือกด 1–3 เพื่อเลือก แล้วกด เลือกใบนี้ (Enter) เพื่อยืนยัน";
            }
            if (summary)
            {
                var result = _mission.Delivery.LastDeliveryResult;
                _title.text = "ส่งลังสำเร็จ!";
                _message.text = $"HP เหลือ {result.RemainingHP} × ตัวคูณ {result.Multiplier} = {result.FinalScore} คะแนน\nได้รับ {result.CoinsEarned} เหรียญต่อคน • ไอเทมติดไปด่านถัดไป";
                _shopWallet.text = $"เงิน {_player.Coins.Value}   |   ช่องไอเทม 3 ช่อง   |   ซื้อได้ก่อนเข้าประตู";
                bool full = _player.Slot1.Value != 0 && _player.Slot2.Value != 0 && _player.Slot3.Value != 0;
                for (int i = 0; i < 2; i++) _buy[i].interactable = !full && _player.Coins.Value >= PlayerExpeditionState.ItemPrice((byte)(i + 1));
                float seconds = _run != null && _run.IsSpawned && _run.ElapsedSeconds > 0f ? _run.ElapsedSeconds
                    : LevelResults.LevelSeconds() > 0f ? LevelResults.LevelSeconds() : -1f;
                int maxHp = _mission.Cargo != null ? _mission.Cargo.MaxHP : 100;
                _complete.SetResult(result.RemainingHP, maxHp, seconds, result.CoinsEarned, LevelResults.Stars(result.RemainingHP, maxHp));
                RefreshResultRows();
                // Items only matter when another level follows (they carry through the exit portal).
                _complete.SetShop(_mission.Portal != null ? _player.Coins.Value : (int?)null, _buy[0].interactable, _buy[1].interactable);
            }
        }

        // Per-player numbers and awards under the result rows (MissionCompleteUI.ExtraContent), from LevelResults.
        private readonly System.Collections.Generic.List<Text> _resultRows = new();

        private void RefreshResultRows()
        {
            if (_complete == null || _complete.ExtraContent == null || !LevelResults.IsReady) return;
            var players = LevelResults.Players();
            var awards = LevelResults.Awards();
            int rows = players.Count + awards.Count;
            if (_resultRows.Count != rows)
            {
                foreach (Transform child in _complete.ExtraContent) Destroy(child.gameObject);
                _resultRows.Clear();
                for (int i = 0; i < rows; i++) _resultRows.Add(ResultLine(i < players.Count));
                _complete.SetVisible(true); // shows the slot now that it has rows
            }
            for (int i = 0; i < players.Count; i++)
                SetChanged(_resultRows[i], LevelResults.NameOf(players[i]) + "  " + LevelResults.Line(players[i].Stats.Value));
            for (int i = 0; i < awards.Count; i++)
                SetChanged(_resultRows[players.Count + i], "★ " + awards[i].Title + ": " + awards[i].Player + " (" + awards[i].Detail + ")");
        }

        /// <summary>One compact full-width line in the result card (stats rows are secondary, awards accent).</summary>
        private Text ResultLine(bool stats)
        {
            var text = UITheme.MakeText(_complete.ExtraContent, stats ? "PlayerStats" : "Award", "", 17,
                stats ? UITextRole.Secondary : UITextRole.Accent, TextAnchor.MiddleLeft);
            UITheme.StyleCuteText(text);
            if (!stats) UITheme.SetWeight(text, UITheme.FontSemiBold);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            UITheme.SetPreferredHeight(text.gameObject, 24f);
            return text;
        }

        private static void SetChanged(Text text, string value) { if (text.text != value) text.text = value; }

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
            if (_timerChip == null) return;
            bool show = (_run == null || _run.IsSpawned) && _mission.Phase.Value < 3;
            if (_timerChip.activeSelf != show) _timerChip.SetActive(show);
            RunInfoBottom = show && _rRun != null ? -_rRun.anchoredPosition.y + _rRun.rect.height : 0f;
            if (!show) return;
            RefreshPlayTime();
            RefreshNavigator();
            // nearest checkpoint line: the active pad number and the distance from the local player
            var pad = _checkpoints != null ? _checkpoints.Current : null;
            var local = _player != null ? _player.transform : null;
            int padNumber = pad != null && pad.IsActive ? pad.Number : 0;
            int distance = pad != null && local != null ? Mathf.RoundToInt(Vector3.Distance(local.position, pad.transform.position)) : -1;
            if (_checkpoints != null && (padNumber != _shownPad || distance != _shownPadDistance))
            {
                _shownPad = padNumber; _shownPadDistance = distance;
                _padString = padNumber == 0 ? "เช็คพอยต์: ยังไม่ได้เซฟ (วางลังบนแท่นทอง)" : "เช็คพอยต์ล่าสุด " + padNumber + "  ห่าง " + distance + " ม.";
                _padText.text = _padString;
            }
            if (_padText.gameObject.activeSelf != (_checkpoints != null)) _padText.gameObject.SetActive(_checkpoints != null);
            if (_run == null) return;
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
            // notices: shown in the context chip for a few seconds
            if (_run.NoticeSequence != _noticeSeq)
            {
                _noticeSeq = _run.NoticeSequence;
                _notice = _run.Notice;
                _noticeUntil = Time.unscaledTime + 4.5f;
            }
            if (_notice != LevelNotice.None && Time.unscaledTime < _noticeUntil) detail = NoticeText(_notice, _run);
        }

        /// <summary>Play time since the session started, from the network clock, so every player shows the same.</summary>
        private void RefreshPlayTime()
        {
            var manager = NetworkManager.Singleton;
            int seconds = manager != null && manager.IsListening ? Mathf.FloorToInt((float)manager.ServerTime.Time) : 0;
            if (seconds == _shownPlaySecond) return;
            SetMmSs(ref _playString, ref _shownPlaySecond, seconds, "เล่นมาแล้ว ");
            _playText.text = _playString;
            PlayLabel = _playString;
        }

        private void BuildNavigator(Transform parent)
        {
            _navRow = new GameObject("Navigator", typeof(RectTransform));
            _navRow.transform.SetParent(parent, false);
            var row = _navRow.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f; row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            UITheme.SetPreferredHeight(_navRow, 34f);
            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(_navRow.transform, false);
            var image = arrow.GetComponent<Image>();
            image.sprite = ArrowSprite();
            image.color = UITheme.TextColor(UITextRole.Accent);
            image.raycastTarget = false;
            var size = arrow.AddComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = 30f;
            _navArrow = (RectTransform)arrow.transform;
            _navText = UITheme.MakeText(_navRow.transform, "Target", "", 19, UITextRole.Accent, TextAnchor.MiddleLeft);
            UITheme.SetWeight(_navText, UITheme.FontBold);
            _navText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private static Sprite s_arrow;

        /// <summary>White arrowhead pointing up (screen forward), drawn once.</summary>
        private static Sprite ArrowSprite()
        {
            if (s_arrow != null) return s_arrow;
            const int n = 64;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n - .5f, v = (y + .5f) / n;
                    // Arrowhead: a triangle narrowing to the tip at the top, with a notch cut into its base.
                    bool inside = v < .96f && Mathf.Abs(u) < (1f - v) * .48f && v > .08f + Mathf.Abs(u) * .6f;
                    pixels[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            s_arrow = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
            return s_arrow;
        }

        /// <summary>Next goal: the first checkpoint after the last saved one, then the delivery zone, then the exit
        /// portal once delivered.</summary>
        private bool TryGetNavTarget(out Vector3 position, out string label)
        {
            position = default; label = null;
            if (_mission.Phase.Value >= 2)
            {
                if (_mission.Portal == null || !_mission.Portal.IsOpen) return false;
                position = _mission.Portal.transform.position; label = "ไปประตูด่านถัดไป";
                return true;
            }
            if (_checkpoints != null)
            {
                var next = _checkpoints.PadAt(Mathf.Max(-1, _checkpoints.CurrentIndex) + 1);
                if (next != null) { position = next.transform.position; label = "ไปเช็คพอยต์ " + next.Number; return true; }
            }
            if (_mission.Delivery == null) return false;
            position = _mission.Delivery.transform.position; label = "ไปจุดส่งของ";
            return true;
        }

        /// <summary>The arrow turns with the camera (up = straight ahead); the label names the goal and its distance.</summary>
        private void RefreshNavigator()
        {
            var local = _player != null ? _player.transform : null;
            var view = Camera.main;
            Vector3 target = default;
            string label = null;
            bool show = local != null && view != null && TryGetNavTarget(out target, out label);
            if (_navRow.activeSelf != show) _navRow.SetActive(show);
            if (!show) return;
            Vector3 to = target - local.position; to.y = 0f;
            Vector3 forward = view.transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = view.transform.up;
            float angle = to.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(forward, to, Vector3.up) : 0f;
            _navArrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
            NavAngle = angle;
            int distance = Mathf.RoundToInt(to.magnitude);
            if (distance == _shownNavDistance && label == _navLabel) return;
            _shownNavDistance = distance; _navLabel = label;
            _navString = label + "  " + distance + " ม.";
            _navText.text = _navString;
            NavLabel = _navString;
        }

        /// <summary>Last arrow angle (degrees, + = target to the right) and label, for tests.</summary>
        public static float NavAngle { get; private set; }
        public static string NavLabel { get; private set; }
        /// <summary>How far down from the top of the screen (1920x1080 reference units) the run info chip reaches,
        /// 0 while hidden. Other top-centre panels (RelayBridgeHUD) sit below it.</summary>
        public static float RunInfoBottom { get; private set; }
        public static string PlayLabel { get; private set; }

        private static void SetChanged(Text text, string value, ref string cached)
        {
            if (cached == value) return;
            cached = value; text.text = value;
        }

        private void Dismiss() { _summaryDismissed = true; ShowModal(false); }

        /// <summary>Host, after delivering on the last level (no exit portal): loads the Lobby for everyone through NGO
        /// scene management, so the session stays connected and the Lobby portal can start Level01 again.</summary>
        public void ReturnToLobbyTogether()
        {
            var manager = NetworkManager.Singleton;
            if (_lobbyRequested || manager == null || !manager.IsServer || manager.SceneManager == null ||
                _mission == null || _mission.Phase.Value != 2 || _mission.Portal != null) return;
            _lobbyRequested = true;
            var status = manager.SceneManager.LoadScene("Lobby", UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started) return;
            _lobbyRequested = false;
            Debug.LogWarning($"[ExpeditionHUD] Return to Lobby could not start: {status}.");
        }

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

            // Run info (top-centre): level timer + side mission (levels with a LevelRunState), the last saved
            // checkpoint, play time and an arrow to the next target.
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
                _playText = UITheme.MakeText(_timerChip.transform, "PlayTime", "", 17, UITextRole.Secondary, TextAnchor.MiddleCenter);
                UITheme.SetPreferredHeight(_playText.gameObject, 24f);
                BuildNavigator(_timerChip.transform);
                _timerText.gameObject.SetActive(_run != null);
                _sideText.gameObject.SetActive(_run != null);
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
            // Keys follow the settings menu bindings (read when the HUD is built).
            string Key(BindRole role) => GameSettings.KeyLabel(GameSettings.Binding(role));
            Hint(Key(BindRole.Carry), "ยก / วาง (ค้าง = รับลัง)");
            Hint(Key(BindRole.Throw), "ค้างเล็ง ปล่อยโยน");
            KeyCap(_hintBar.transform, Key(BindRole.GrabLeft), 56f, 35f); // both mouse buttons climb
            Hint(Key(BindRole.GrabRight), "ปีน");

            // Own card icon (mockup 3, bottom-right) with the ability tooltip.
            var own = Box("OwnCardIcon", transform, new Vector2(1f, 0f), new Vector2(-72, 72), new Vector2(84, 84), Color.white);
            _ownCardIcon = own.GetComponent<Image>();
            UITheme.StyleIcon(_ownCardIcon, UITheme.CardIcon(0));
            _ownCardIcon.raycastTarget = true;
            _ownCardTip = own.AddComponent<UITooltip>();
            _ownCardTip.Side = new Vector2(-1f, 0f);
            // "V มุมมอง" and "H ดูการ์ด" key hints to the left of the own-card icon (child of the icon, so it hides with it).
            var viewHint = Box("ViewCardHint", own.transform, new Vector2(0f, .5f), new Vector2(-12f, 0f), new Vector2(10f, 10f), Color.clear);
            ((RectTransform)viewHint.transform).pivot = new Vector2(1f, .5f);
            viewHint.GetComponent<Image>().raycastTarget = false;
            var viewLayout = viewHint.AddComponent<HorizontalLayoutGroup>();
            viewLayout.spacing = 8f; viewLayout.childAlignment = TextAnchor.MiddleRight;
            viewLayout.childControlWidth = viewLayout.childControlHeight = true;
            viewLayout.childForceExpandWidth = viewLayout.childForceExpandHeight = false;
            var viewFit = viewHint.AddComponent<ContentSizeFitter>();
            viewFit.horizontalFit = viewFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // "V มุมมอง" sits here too (George 2026-11-05: on the left it ran into the stamina bar).
            KeyCap(viewHint.transform, "V", 32f, 35f);
            var cameraLabel = UITheme.MakeText(viewHint.transform, "Hint V", "มุมมอง", 20, UITextRole.Primary);
            cameraLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddOutline(cameraLabel);
            var cameraElement = cameraLabel.gameObject.AddComponent<LayoutElement>();
            cameraElement.preferredHeight = 35f;
            cameraElement.minWidth = cameraLabel.preferredWidth + 18f;
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
            // Cute kit (Canva page 71): wide beige card, plank title, navy message, pink confirm on the bottom edge.
            var panel = Box("Panel", _modal.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1320, 730), Color.white);
            _cardPanel = panel;
            _complete = MissionCompleteUI.EnsureInstance();
            _complete.Bind(Dismiss, ReturnToLobbyTogether, id => _player?.BuyItemRpc(id));
            UITheme.StyleCuteCard(panel.GetComponent<Image>(), true);
            panel.GetComponent<Image>().raycastTarget = false;
            _title = UITheme.MakeCutePlank(panel.transform, "", 460f, 100f, 6f, 36);
            UITheme.AddCuteTape(panel.transform, 5, new Vector2(1f, 1f), new Vector2(-130, -70), -28f, 100f);
            UITheme.AddCuteTape(panel.transform, 2, new Vector2(0f, 0f), new Vector2(110, 90), 30f, 90f);
            _message = Label(panel.transform, "Message", "", 26, new Vector2(0, 268), new Vector2(1000, 52), TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_message);
            UITheme.SetWeight(_message, UITheme.FontMedium);

            _cards = new GameObject("Cards", typeof(RectTransform)); _cards.transform.SetParent(panel.transform, false);
            for (int slot = 0; slot < 3; slot++)
                CardButton(_cards.transform, slot, new Vector2((slot - 1) * CardSpacing, _cardsY));
            _confirmCard = Button(_cards.transform, "เลือกใบนี้", new Vector2(0, -420), new Vector2(380, 96), ConfirmCard, UIButtonStyle.Pink);
            foreach (Text label in _confirmCard.GetComponentsInChildren<Text>(true)) label.fontSize = 32;
            // Disabled until a card is picked, but stay fully opaque (the default 60% fade read as broken).
            var confirmColors = _confirmCard.colors;
            confirmColors.disabledColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            _confirmCard.colors = confirmColors;
            // Cards (and the confirm under them) at 86% so they sit inside the card frame like the mockup.
            _cards.transform.localScale = Vector3.one * 0.86f;
            ((RectTransform)_cards.transform).anchoredPosition = new Vector2(0, 4);

            _shop = new GameObject("Shop", typeof(RectTransform)); _shop.transform.SetParent(panel.transform, false);
            ((RectTransform)_shop.transform).anchoredPosition = new Vector2(0, 95); // shop sits under the raised banner
            var shopPanel = Box("ShopPanel", _shop.transform, new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(1060, 420), UITheme.Panel, UITheme.PanelSprite);
            shopPanel.GetComponent<Image>().raycastTarget = false;
            _shopWallet = Label(_shop.transform, "Wallet", "", 22, new Vector2(0, 110), new Vector2(1020, 50), TextAnchor.MiddleCenter, UITextRole.Secondary);
            _buy[0] = Button(_shop.transform, "Stamina Elixir — 40 เหรียญ\nฟื้น Stamina เต็ม 1 ครั้ง", new Vector2(-250, -10), new Vector2(460, 120), () => _player?.BuyItemRpc(1));
            _buy[1] = Button(_shop.transform, "Cargo Padding — 60 เหรียญ\nลดความเสียหายครั้งถัดไป 50%\nใช้ขณะจับลัง", new Vector2(250, -10), new Vector2(460, 120), () => _player?.BuyItemRpc(2));
            bool lastLevel = _mission != null && _mission.Portal == null;
            Button(_shop.transform, "เดินต่อ / ENTER", new Vector2(lastLevel ? -200 : 0, -170), new Vector2(360, 80), Dismiss, UIButtonStyle.Pink);
            if (lastLevel)
            {
                _toLobby = Button(_shop.transform, "กลับ Lobby พร้อมกัน", new Vector2(200, -170), new Vector2(360, 80), ReturnToLobbyTogether);
                _toLobbyLabel = _toLobby.GetComponentInChildren<Text>(true);
            }
            _modal.SetActive(false);
            BuildView();
        }

        private static void StretchFull(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>H card view: same dim + backdrop, own card full-size, teammates as small thumbnails with names.</summary>
        private void BuildView()
        {
            _view = Box("Card View", transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, UITheme.Overlay);
            StretchFull(_view);
            var root = Box("Panel", _view.transform, new Vector2(.5f, .5f), Vector2.zero, _viewPanelSize, Color.clear);
            root.GetComponent<Image>().raycastTarget = false;
            // Cute kit (Canva page 72): own card in a beige taped frame on the left, navy info panel on the right.
            var frame = Box("OwnCardFrame", root.transform, new Vector2(.5f, .5f), new Vector2(-440, 0), new Vector2(560, 780), Color.white);
            UITheme.StyleCuteCard(frame.GetComponent<Image>());
            frame.GetComponent<Image>().raycastTarget = false;
            UITheme.AddCuteTape(frame.transform, 5, new Vector2(0f, 1f), new Vector2(20, -40), 30f, 110f);
            UITheme.AddCuteTape(frame.transform, 3, new Vector2(0f, 0f), new Vector2(30, 40), -30f, 100f);
            var own = Box("OwnCard", frame.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(500, 712), Color.white);
            _viewOwnArt = own.GetComponent<Image>();
            _viewOwnArt.preserveAspect = true; _viewOwnArt.raycastTarget = false;
            ((RectTransform)own.transform).sizeDelta = new Vector2(500, 706); // card art is 640:904
            UITheme.RoundCorners(_viewOwnArt, 40);
            var info = Box("Info", root.transform, new Vector2(.5f, .5f), new Vector2(300, 0), new Vector2(920, 720), UITheme.CuteNavyPanel, UITheme.SmallSprite);
            info.GetComponent<Image>().raycastTarget = false;
            Label(info.transform, "Title", "การ์ดของคุณ", 26, new Vector2(0, 312), new Vector2(820, 40), TextAnchor.MiddleLeft, UITextRole.Secondary);
            _viewName = Label(info.transform, "CardName", "", 58, new Vector2(0, 245), new Vector2(820, 80), TextAnchor.MiddleLeft);
            UITheme.SetWeight(_viewName, UITheme.FontSemiBold);
            _viewEffect = Label(info.transform, "CardEffect", "", 36, new Vector2(0, 140), new Vector2(820, 110), TextAnchor.UpperLeft);
            UITheme.SetWeight(_viewEffect, UITheme.FontMedium);
            _viewTip = Label(info.transform, "CardTip", "", 26, new Vector2(0, 10), new Vector2(820, 150), TextAnchor.UpperLeft, UITextRole.Secondary);
            _viewMatesTitle = Label(info.transform, "MatesTitle", "การ์ดของเพื่อน", 24, new Vector2(0, -105), new Vector2(820, 36), TextAnchor.MiddleLeft, UITextRole.Secondary);
            for (int i = 0; i < 3; i++)
            {
                var mate = Box($"Mate{i + 1}", info.transform, new Vector2(.5f, .5f), new Vector2(-300 + i * 200, -225), new Vector2(190, 190), Color.clear);
                mate.GetComponent<Image>().raycastTarget = false;
                var art = Box("Art", mate.transform, new Vector2(.5f, .5f), new Vector2(0, 18), new Vector2(105, 148), Color.white);
                _viewMateArt[i] = art.GetComponent<Image>();
                _viewMateArt[i].preserveAspect = true; _viewMateArt[i].raycastTarget = false;
                UITheme.RoundCorners(_viewMateArt[i], 14);
                _viewMateName[i] = Label(mate.transform, "Name", "", 20, new Vector2(0, -78), new Vector2(190, 30), TextAnchor.MiddleCenter, UITextRole.Secondary);
            }
            Label(info.transform, "Close", "H / ESC  ปิด", 22, new Vector2(0, -322), new Vector2(500, 34), TextAnchor.MiddleCenter, UITextRole.Secondary);
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
            KeyCap(_hintBar.transform, key, wide || key.Length > 1 ? 56f : 32f, 35f);
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
        /// selection frame and a small lift when selected. Clicking a card or keys 1-3 only select it; the confirm button, Enter or Space applies it.</summary>
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
            UITheme.RoundCorners(_cardArt[slot], 30);

            var frame = Box("SelectedFrame", root.transform, new Vector2(.5f, .5f), new Vector2(0, -6f), new Vector2(CardWidth * 1.2f, CardHeight * 1.22f), Color.white);
            _cardFrames[slot] = frame.GetComponent<Image>();
            UITheme.StyleIcon(_cardFrames[slot], UITheme.CardFrameSprite);
            _cardFrames[slot].preserveAspect = false;
            _cardFrames[slot].raycastTarget = false;
            _cardFrames[slot].enabled = false;
        }

        private void ConfirmCard()
        {
            if (_pendingSlot < 0 || _player == null) return;
            _player.SelectCardRpc(CardCatalog.OfferAt(_player.Offer.Value, _pendingSlot));
            ShowModal(false);
        }
    }
}
