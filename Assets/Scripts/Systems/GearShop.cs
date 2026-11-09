using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Gear with small stat bonuses (stats only for now, George 2026-11-09): one pair of gloves and one pair of
    /// glasses can be worn. Bought with coins in the Lobby shop (K, shown on the left beside the hat shop); owned
    /// and worn gear is saved in <see cref="SaveSystem"/>. Bonuses act on the owner's stamina, which the owner
    /// simulates (<see cref="PlayerExpeditionState"/> FixedUpdate multiplies them in).
    /// </summary>
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
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, .5f);
            rootRect.anchoredPosition = new Vector2(330f, 0f);
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(560f, 760f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            UITheme.MakeCutePlank(card.transform, "ร้านอุปกรณ์", 360f, 90f, 6f, 36);
            _wallet = UITheme.MakeText(card.transform, "Wallet", "", 24, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_wallet);
            ((RectTransform)_wallet.transform).sizeDelta = new Vector2(500f, 40f);
            ((RectTransform)_wallet.transform).anchoredPosition = new Vector2(0f, 290f);
            for (int i = 0; i < Items.Length; i++)
            {
                int index = i;
                var button = UITheme.MakeButton(card.transform, "Gear" + i, Items[i].Name, UIButtonStyle.White, new Vector2(480f, 110f), 22);
                ((RectTransform)button.transform).anchoredPosition = new Vector2(0f, 190f - i * 124f);
                var text = button.GetComponentInChildren<Text>();
                if (text != null) text.supportRichText = true;
                button.onClick.AddListener(() => OnClick(index));
                _buttons.Add(button);
            }
            var hint = UITheme.MakeText(card.transform, "Hint", "ใส่ได้ถุงมือ 1 คู่ + แว่น 1 อัน • กดอันที่ใส่อยู่เพื่อถอด", 19, UITextRole.Secondary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(hint);
            ((RectTransform)hint.transform).sizeDelta = new Vector2(520f, 34f);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -310f);
            _root.SetActive(false);
        }

        private static void OnClick(int index)
        {
            var state = Local();
            if (state == null) return;
            if (SaveSystem.OwnsGear(index)) Wear(index);
            else state.BuyGearRpc((byte)index);
        }

        private void Update()
        {
            bool open = Cosmetics.IsOpen;
            if (_root.activeSelf != open) _root.SetActive(open);
            if (!open || Time.frameCount % 10 != 0) return;
            var state = Local();
            int coins = state != null ? state.Coins.Value : 0;
            _wallet.text = "เงิน " + coins + "   •   Lv." + PlayerProgress.Level;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var def = Items[i];
                bool owned = SaveSystem.OwnsGear(i), worn = Worn(def.Slot) == i;
                string status = worn ? "✓ ใส่อยู่" : owned ? "กดเพื่อใส่" : def.Price + " เหรียญ";
                string label = $"{(def.Slot == Slot.Gloves ? "[ถุงมือ]" : "[แว่น]")} {def.Name}  •  {status}\n<size=18>{def.Detail}</size>";
                var text = _buttons[i].GetComponentInChildren<Text>();
                if (text != null && text.text != label) text.text = label;
                _buttons[i].interactable = owned || coins >= def.Price;
            }
        }
    }
}
