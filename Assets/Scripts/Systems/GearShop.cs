using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Gear with small stat bonuses (stats only for now, George 2026-11-09): one pair of gloves and one pair of
    /// glasses can be worn. Bought with coins at the Lobby gear stall (E at <see cref="ShopStall"/>); owned
    /// and worn gear is saved in <see cref="SaveSystem"/>. Bonuses act on the owner's stamina, which the owner
    /// simulates (<see cref="PlayerExpeditionState"/> FixedUpdate multiplies them in).
    /// </summary>
    /// <summary>
    /// An item row in a shop window (George 2026-11-21): no frame and no pink on mouse-over; the row turns pink
    /// only while it is the picked one.
    /// </summary>
    internal sealed class ShopRow
    {
        private readonly Button _button;
        private readonly Image _image;
        private readonly Sprite _normal, _pink;
        private readonly Color _normalText;
        private int _shown = -1;

        public ShopRow(Button button)
        {
            _button = button;
            _image = button.targetGraphic as Image;
            _normal = _image != null ? _image.sprite : null;
            _pink = UITheme.ButtonSprite(UIButtonStyle.Pink);
            var label = button.GetComponentInChildren<Text>(true);
            _normalText = label != null ? label.color : Color.black;
            var hover = button.GetComponent<UIHoverPink>();
            if (hover != null) hover.enabled = false;
            var colors = button.colors;
            colors.highlightedColor = colors.normalColor;
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        public void SetPicked(bool picked)
        {
            int state = picked ? 1 : 0;
            if (_shown == state) return;
            _shown = state;
            if (_image != null) _image.sprite = picked ? _pink : _normal;
            foreach (var text in _button.GetComponentsInChildren<Text>(true)) text.color = picked ? UITheme.TextPrimary : _normalText;
        }
    }

    public sealed class GearShop : MonoBehaviour
    {
        public enum Slot : byte { Gloves, Glasses }

        public readonly struct GearDef
        {
            public readonly string Name, Detail;
            public readonly Slot Slot;
            public readonly int Price;
            public readonly float CarryDrain, ActionCost, RestRegen;
            public GearDef(string name, string detail, Slot slot, int price, float carryDrain = 1f, float actionCost = 1f, float restRegen = 1f)
            { Name = name; Detail = detail; Slot = slot; Price = price; CarryDrain = carryDrain; ActionCost = actionCost; RestRegen = restRegen; }
        }

        /// <summary>Index = save bit. Id 0 in each slot means "none".</summary>
        public static readonly GearDef[] Items =
        {
            new("ถุงมือหนัง", "ยกลังแล้วเหนื่อยช้าลง 10%", Slot.Gloves, 250, carryDrain: .9f),
            new("ถุงมือนักปีน", "กระโดด/วิ่งใช้สตามิน่าน้อยลง 12%", Slot.Gloves, 300, actionCost: .88f),
            new("แว่นกันแดด", "สตามิน่าฟื้นเร็วขึ้น 12%", Slot.Glasses, 200, restRegen: 1.12f),
            new("แว่นนักบิน", "ยกเหนื่อยช้าลง 5% และฟื้นเร็วขึ้น 6%", Slot.Glasses, 350, carryDrain: .95f, restRegen: 1.06f),
        };

        public static bool IsOpen => _root != null && _root.activeSelf;

        private static GameObject _root;
        private readonly List<Button> _buttons = new();
        private Text _wallet;
        private readonly List<ShopRow> _rows = new();
        private Button _action;
        private Text _actionLabel;
        private static int _selected = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("GearShop");
            DontDestroyOnLoad(go);
            go.AddComponent<GearShop>().Build();
        }

        /// <summary>Worn item index for a slot, or -1.</summary>
        public static int Worn(Slot slot)
        {
            int id = slot == Slot.Gloves ? SaveSystem.Current.gloves : SaveSystem.Current.glasses;
            int index = id - 1;
            return index >= 0 && index < Items.Length && Items[index].Slot == slot && SaveSystem.OwnsGear(index) ? index : -1;
        }

        /// <summary>Combined multipliers of the worn gear (this machine's player).</summary>
        public static void Bonuses(out float carryDrain, out float actionCost, out float restRegen)
        {
            carryDrain = actionCost = restRegen = 1f;
            foreach (Slot slot in new[] { Slot.Gloves, Slot.Glasses })
            {
                int i = Worn(slot);
                if (i < 0) continue;
                carryDrain *= Items[i].CarryDrain; actionCost *= Items[i].ActionCost; restRegen *= Items[i].RestRegen;
            }
        }

        public static void Wear(int index)
        {
            if (index < 0 || index >= Items.Length || !SaveSystem.OwnsGear(index)) return;
            bool worn = Worn(Items[index].Slot) == index;
            int value = worn ? 0 : index + 1; // clicking the worn item takes it off
            if (Items[index].Slot == Slot.Gloves) SaveSystem.Current.gloves = value; else SaveSystem.Current.glasses = value;
            SaveSystem.Save();
        }

        /// <summary>Owner, after the host took the coins.</summary>
        public static void OnBought(int index)
        {
            SaveSystem.MarkGearOwned(index);
            Wear(index);
        }

        private static PlayerExpeditionState Local()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 251;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = new GameObject("Gear", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var rootRect = (RectTransform)_root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(.5f, .5f);
            rootRect.anchoredPosition = Vector2.zero;
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(560f, 760f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            UITheme.MakeCutePlank(card.transform, "ร้านอุปกรณ์", 360f, 90f, 6f, 36);
            _wallet = UITheme.MakeText(card.transform, "Wallet", "", 24, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_wallet);
            ((RectTransform)_wallet.transform).sizeDelta = new Vector2(500f, 40f);
            ((RectTransform)_wallet.transform).anchoredPosition = new Vector2(0f, 290f);
            // George 2026-11-21: click a row to pick it, then the bottom button buys / wears / takes it off; ออก closes.
            for (int i = 0; i < Items.Length; i++)
            {
                int index = i;
                Vector2 at = new Vector2(0f, 196f - i * 118f);
                var button = UITheme.MakeButton(card.transform, "Gear" + i, Items[i].Name, UIButtonStyle.White, new Vector2(480f, 104f), 22);
                ((RectTransform)button.transform).anchoredPosition = at;
                var text = button.GetComponentInChildren<Text>();
                if (text != null) text.supportRichText = true;
                button.onClick.AddListener(() => { _selected = index; _nextRefresh = 0; });
                _buttons.Add(button);
                _rows.Add(new ShopRow(button));
            }
            var hint = UITheme.MakeText(card.transform, "Hint", "ใส่ได้ถุงมือ 1 คู่ + แว่น 1 อัน", 19, UITextRole.Secondary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(hint);
            ((RectTransform)hint.transform).sizeDelta = new Vector2(520f, 34f);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -258f);
            var exit = UITheme.MakeButton(card.transform, "Exit", "ออก", UIButtonStyle.White, new Vector2(220f, 64f), 24);
            ((RectTransform)exit.transform).anchoredPosition = new Vector2(-125f, -318f);
            exit.onClick.AddListener(() => SetOpen(false));
            _action = UITheme.MakeButton(card.transform, "Action", "เลือก", UIButtonStyle.Pink, new Vector2(240f, 64f), 24);
            ((RectTransform)_action.transform).anchoredPosition = new Vector2(125f, -318f);
            _action.onClick.AddListener(OnAction);
            _actionLabel = _action.GetComponentInChildren<Text>();
            _root.SetActive(false);
        }


        /// <summary>Bottom button: buy the picked item, wear it, or take it off.</summary>
        private static void OnAction()
        {
            var state = Local();
            if (state == null || _selected < 0) return;
            if (SaveSystem.OwnsGear(_selected)) Wear(_selected); // wearing the worn one takes it off
            else state.BuyGearRpc((byte)_selected);
            _nextRefresh = 0;
        }

        /// <summary>Opened by the gear stall in the Lobby (George 2026-11-20); its own window, no longer beside the hat shop.</summary>
        public static void SetOpen(bool open)
        {
            if (_root == null) return;
            if (open) UIPanels.CloseOthers(UIPanels.Panel.Gear);
            open = open && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == CoopGame.Player.PlayerAppearance.LobbySceneName && Local() != null;
            if (_root.activeSelf == open) return;
            if (!open) UIPanels.MarkClosed();
            _root.SetActive(open);
            if (open) _selected = -1;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open) _nextRefresh = 0;
        }

        private static int _nextRefresh;

        private void Update()
        {
            bool open = _root.activeSelf;
            if (open && (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != CoopGame.Player.PlayerAppearance.LobbySceneName || Local() == null)) { SetOpen(false); return; }
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { SetOpen(false); return; }
            if (!open || Time.frameCount < _nextRefresh) return;
            _nextRefresh = Time.frameCount + 10;
            var state = Local();
            int coins = state != null ? state.Coins.Value : 0;
            _wallet.text = "เงิน " + coins + "   •   Lv." + PlayerProgress.Level;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var def = Items[i];
                bool owned = SaveSystem.OwnsGear(i), worn = Worn(def.Slot) == i;
                string status = worn ? "✓ ใส่อยู่" : owned ? "มีแล้ว" : def.Price + " เหรียญ";
                string label = $"{(def.Slot == Slot.Gloves ? "[ถุงมือ]" : "[แว่น]")} {def.Name}  •  {status}\n<size=18>{def.Detail}</size>";
                var text = _buttons[i].GetComponentInChildren<Text>();
                if (text != null && text.text != label) text.text = label;
                _rows[i].SetPicked(i == _selected);
            }
            string action; bool can;
            if (_selected < 0) { action = "เลือกของก่อน"; can = false; }
            else if (Worn(Items[_selected].Slot) == _selected) { action = "ถอดออก"; can = true; }
            else if (SaveSystem.OwnsGear(_selected)) { action = "ใส่"; can = true; }
            else { can = coins >= Items[_selected].Price; action = can ? "ซื้อ " + Items[_selected].Price + " เหรียญ" : "เงินไม่พอ"; }
            if (_actionLabel.text != action) _actionLabel.text = action;
            _action.interactable = can;
        }
    }
}
