using System.Collections.Generic;
using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Hats bought with coins at the Lobby outfit stall (E at <see cref="ShopStall"/>). The host checks and takes the coins
    /// (<see cref="PlayerExpeditionState.BuyHatRpc"/>); owned hats are remembered on the buyer's machine
    /// (PlayerPrefs). The equipped hat and colour are owner-written network values on the player, and every screen
    /// builds the hat from simple shapes on that player's head.
    /// </summary>
    public sealed class Cosmetics : MonoBehaviour
    {
        public static readonly string[] HatNames = { "ไม่ใส่", "หมวกปาร์ตี้", "หมวกทรงสูง", "มงกุฎ", "หมวกแก๊ป" };
        public static readonly int[] HatPrices = { 0, 100, 200, 400, 150 };
        public static readonly Color[] HatColors =
        {
            new Color(.95f, .35f, .45f), new Color(.35f, .65f, 1f), new Color(.45f, .85f, .4f),
            new Color(1f, .8f, .2f), new Color(.6f, .4f, .9f), new Color(.15f, .15f, .18f)
        };
        private const string Lobby = "Lobby";

        /// <summary>George 2026-11-21: only the party hat (the first one) takes a chosen colour; the others keep their own.</summary>
        public static bool HatHasColor(int hat) => hat == 1;

        public static bool IsOpen { get; private set; }
        private static Material _template;
        private readonly Dictionary<PlayerExpeditionState, (Transform hat, int key)> _shown = new();
        private GameObject _root;
        private Text _wallet;
        private readonly List<Button> _hatButtons = new();
        private readonly List<ShopRow> _hatRows = new();
        private readonly List<GameObject> _swatches = new();
        private readonly List<Image> _swatchRings = new();
        private GameObject _colorTitle;
        private Button _action;
        private Text _actionLabel;
        private int _selHat = -1, _selColor;
        private bool _restored;

        public static bool Owns(int hat) => SaveSystem.OwnsHat(hat); // local save file (was PlayerPrefs; migrated on first run)

        /// <summary>Owner: the host confirmed the purchase.</summary>
        public static void MarkOwned(int hat)
        {
            SaveSystem.MarkHatOwned(hat);
            Equip(hat, SaveSystem.Current.hatColor);
        }

        /// <summary>Owner: wear a hat (0 = none) in a colour; remembered for next time.</summary>
        public static void Equip(int hat, int color)
        {
            var state = Local();
            if (state == null || !Owns(hat)) return;
            state.Hat.Value = (byte)hat;
            state.HatColor.Value = (byte)Mathf.Clamp(color, 0, HatColors.Length - 1);
            SaveSystem.SetHat(hat, state.HatColor.Value);
        }

        private static PlayerExpeditionState Local()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            IsOpen = false;
            var go = new GameObject("Cosmetics");
            DontDestroyOnLoad(go);
            go.AddComponent<Cosmetics>().BuildShop();
        }

        // ───────────────────────── Hats on heads (every screen) ─────────────────────────

        private void LateUpdate()
        {
            var local = Local();
            if (local != null && local.IsSpawned && !_restored)
            {
                _restored = true;
                int hat = SaveSystem.Current.hatEquipped;
                if (Owns(hat)) Equip(hat, SaveSystem.Current.hatColor);
            }
            if (local == null) _restored = false;
            foreach (var player in PlayerExpeditionState.Everyone)
            {
                if (player == null || !player.IsSpawned) continue;
                int key = player.Hat.Value * 16 + player.HatColor.Value;
                _shown.TryGetValue(player, out var shown);
                if (shown.hat != null && shown.key == key) continue;
                if (shown.hat != null) Destroy(shown.hat.gameObject);
                _shown[player] = (player.Hat.Value == 0 ? null : BuildHat(player, player.Hat.Value, HatColors[Mathf.Min(player.HatColor.Value, HatColors.Length - 1)]), key);
            }
            if (Time.frameCount % 120 == 0)
            {
                var dead = new List<PlayerExpeditionState>();
                foreach (var pair in _shown) if (pair.Key == null) dead.Add(pair.Key);
                foreach (var key in dead) _shown.Remove(key);
            }
            UpdateShopInput();
        }

        private static Material MaterialFor(Color color)
        {
            if (_template == null) _template = Resources.Load<Material>("CosmeticLit");
            if (_template == null) return null;
            var material = new Material(_template);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>Builds the hat on top of the player's head, sized in world units whatever the bone scale.</summary>
        private static Transform BuildHat(PlayerExpeditionState player, int hat, Color color)
        {
            var rig = player.GetComponent<CharacterRig>();
            Transform head = rig != null ? rig.Head : null;
            if (head == null) return null;
            // Top of the head above the bone, from the drawn body (measured once when the hat is put on).
            float top = .45f;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.name.StartsWith("body")) top = Mathf.Clamp(renderer.bounds.max.y - head.position.y, .2f, .9f);
            var root = new GameObject("CosmeticHat", typeof(CosmeticPart)).transform;
            root.SetParent(head, false);
            Vector3 lossy = head.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(1e-4f, lossy.x), 1f / Mathf.Max(1e-4f, lossy.y), 1f / Mathf.Max(1e-4f, lossy.z));
            root.position = head.position + Vector3.up * (top - .04f);
            root.rotation = Quaternion.LookRotation(player.transform.forward, Vector3.up);
            // Only the party hat takes the chosen colour; the others keep their own look (and the body colour never dyes them).
            if (!HatHasColor(hat)) color = hat == 2 ? new Color(.15f, .14f, .17f) : new Color(.24f, .49f, .85f);
            Material main = MaterialFor(color), dark = MaterialFor(hat == 2 ? new Color(.85f, .28f, .23f) : color * .6f), gold = MaterialFor(new Color(1f, .82f, .2f));
            switch (hat)
            {
                case 1: // party cone with a pompom
                    Shape(root, PrimitiveType.Cylinder, new Vector3(0f, .12f, 0f), new Vector3(.22f, .13f, .22f), main).GetComponent<MeshFilter>().sharedMesh = Cone();
                    Shape(root, PrimitiveType.Sphere, new Vector3(0f, .27f, 0f), Vector3.one * .07f, gold);
                    break;
                case 2: // top hat
                    Shape(root, PrimitiveType.Cylinder, new Vector3(0f, .01f, 0f), new Vector3(.42f, .012f, .42f), dark);
                    Shape(root, PrimitiveType.Cylinder, new Vector3(0f, .13f, 0f), new Vector3(.26f, .12f, .26f), main);
                    Shape(root, PrimitiveType.Cylinder, new Vector3(0f, .05f, 0f), new Vector3(.265f, .02f, .265f), dark);
                    break;
                case 3: // crown
                    Shape(root, PrimitiveType.Cylinder, new Vector3(0f, .04f, 0f), new Vector3(.3f, .04f, .3f), gold);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        var spike = Shape(root, PrimitiveType.Cube, new Vector3(Mathf.Sin(a) * .13f, .11f, Mathf.Cos(a) * .13f), new Vector3(.05f, .09f, .05f), gold);
                        spike.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 45f);
                        Shape(root, PrimitiveType.Sphere, new Vector3(Mathf.Sin(a) * .15f, .04f, Mathf.Cos(a) * .15f), Vector3.one * .035f, main);
                    }
                    break;
                default: // cap with a visor
                    Shape(root, PrimitiveType.Sphere, new Vector3(0f, .0f, 0f), new Vector3(.36f, .2f, .36f), main);
                    Shape(root, PrimitiveType.Cube, new Vector3(0f, -.01f, .2f), new Vector3(.26f, .015f, .16f), dark);
                    break;
            }
            return root;
        }

        private static Transform Shape(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private static Mesh _cone;

        /// <summary>A unit cone (radius .5, height 2, centred) to replace the cylinder mesh of the party hat.</summary>
        private static Mesh Cone()
        {
            if (_cone != null) return _cone;
            const int sides = 20;
            var vertices = new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f) };
            var triangles = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(Mathf.Sin(a) * .5f, -1f, Mathf.Cos(a) * .5f));
            }
            for (int i = 0; i < sides; i++)
            {
                triangles.AddRange(new[] { 0, 2 + i, 3 + i });
                triangles.AddRange(new[] { 1, 3 + i, 2 + i });
            }
            _cone = new Mesh { name = "Cone" };
            _cone.SetVertices(vertices);
            _cone.SetTriangles(triangles, 0);
            _cone.RecalculateNormals();
            return _cone;
        }

        // ───────────────────────── Shop panel (Lobby outfit stall, E) ─────────────────────────

        public static void SetOpen(bool open)
        {
            var shop = FindAnyObjectByType<Cosmetics>();
            if (shop == null) return;
            if (open) UIPanels.CloseOthers(UIPanels.Panel.Outfit);
            else if (IsOpen) UIPanels.MarkClosed();
            IsOpen = open && SceneManager.GetActiveScene().name == Lobby && Local() != null;
            shop._root.SetActive(IsOpen);
            if (IsOpen)
            {
                var state = Local();
                shop._selHat = -1;
                shop._selColor = state != null ? state.HatColor.Value : 0;
                shop.RefreshShop();
            }
            Cursor.lockState = IsOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = IsOpen;
        }

        private void UpdateShopInput()
        {
            bool inLobby = SceneManager.GetActiveScene().name == Lobby;
            if (IsOpen && (!inLobby || Local() == null)) SetOpen(false);
            if (IsOpen && Time.frameCount % 15 == 0) RefreshShop();
            // George 2026-11-20: no K key any more; the outfit stall in the Lobby opens this (ShopStall).
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !inLobby) return;
            if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
        }

        private void BuildShop()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 251;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = new GameObject("Shop", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var rootRect = (RectTransform)_root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(.5f, .5f);
            rootRect.anchoredPosition = Vector2.zero;
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_root.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(560f, 760f);
            UITheme.StyleCuteCard(card.GetComponent<Image>());
            UITheme.MakeCutePlank(card.transform, "ร้านแต่งตัว", 360f, 90f, 6f, 36);
            _wallet = UITheme.MakeText(card.transform, "Wallet", "", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(_wallet);
            ((RectTransform)_wallet.transform).sizeDelta = new Vector2(500f, 40f);
            ((RectTransform)_wallet.transform).anchoredPosition = new Vector2(0f, 290f);
            // George 2026-11-21: click a hat to pick it, the bottom button buys / wears it, ออก closes.
            // Colour swatches only for the party hat (the other hats keep their own colours).
            for (int hat = 0; hat < HatNames.Length; hat++)
            {
                int id = hat;
                Vector2 at = new Vector2(0f, 212f - hat * 78f);
                var button = UITheme.MakeButton(card.transform, "Hat" + hat, HatNames[hat], UIButtonStyle.White, new Vector2(460f, 66f), 24);
                ((RectTransform)button.transform).anchoredPosition = at;
                button.onClick.AddListener(() => { _selHat = id; RefreshShop(); });
                _hatButtons.Add(button);
                _hatRows.Add(new ShopRow(button));
            }
            var colorTitle = UITheme.MakeText(card.transform, "ColorTitle", "สีหมวกปาร์ตี้", 20, UITextRole.Secondary, TextAnchor.MiddleCenter);
            UITheme.StyleCuteText(colorTitle);
            ((RectTransform)colorTitle.transform).sizeDelta = new Vector2(500f, 30f);
            ((RectTransform)colorTitle.transform).anchoredPosition = new Vector2(0f, -178f);
            _colorTitle = colorTitle.gameObject;
            Sprite round = ProceduralUIUtility.GetRoundedSprite(30, 0);
            for (int c = 0; c < HatColors.Length; c++)
            {
                int color = c;
                Vector2 at = new Vector2((c - (HatColors.Length - 1) * .5f) * 72f, -226f);
                var ring = new GameObject("ColorRing" + c, typeof(RectTransform), typeof(Image));
                ring.transform.SetParent(card.transform, false);
                ((RectTransform)ring.transform).sizeDelta = new Vector2(68f, 68f);
                ((RectTransform)ring.transform).anchoredPosition = at;
                var ringImage = ring.GetComponent<Image>();
                UITheme.StyleShape(ringImage, round, UITheme.Primary);
                ringImage.raycastTarget = false;
                _swatchRings.Add(ringImage);
                var swatch = new GameObject("Color" + c, typeof(RectTransform), typeof(Image), typeof(Button));
                swatch.transform.SetParent(card.transform, false);
                var rect = (RectTransform)swatch.transform;
                rect.sizeDelta = new Vector2(56f, 56f);
                rect.anchoredPosition = at;
                UITheme.StyleShape(swatch.GetComponent<Image>(), round, HatColors[c]);
                swatch.GetComponent<Button>().onClick.AddListener(() => { _selColor = color; RefreshShop(); });
                _swatches.Add(swatch);
                _swatches.Add(ring);
            }
            var exit = UITheme.MakeButton(card.transform, "Exit", "ออก", UIButtonStyle.White, new Vector2(220f, 64f), 24);
            ((RectTransform)exit.transform).anchoredPosition = new Vector2(-125f, -310f);
            exit.onClick.AddListener(() => SetOpen(false));
            _action = UITheme.MakeButton(card.transform, "Action", "เลือก", UIButtonStyle.Pink, new Vector2(240f, 64f), 24);
            ((RectTransform)_action.transform).anchoredPosition = new Vector2(125f, -310f);
            _action.onClick.AddListener(OnAction);
            _actionLabel = _action.GetComponentInChildren<Text>();
            _root.SetActive(false);
        }

        private void RefreshShop()
        {
            var state = Local();
            if (state == null) return;
            _wallet.text = "เงิน " + state.Coins.Value;
            for (int hat = 0; hat < _hatButtons.Count; hat++)
            {
                bool owned = Owns(hat), worn = state.Hat.Value == hat;
                string label = HatNames[hat] + (worn ? "  ✓ ใส่อยู่" : owned ? (hat == 0 ? "" : "  • มีแล้ว") : "  • " + HatPrices[hat] + " เหรียญ");
                var text = _hatButtons[hat].GetComponentInChildren<Text>();
                if (text != null && text.text != label) text.text = label;
                _hatRows[hat].SetPicked(hat == _selHat);
            }
            bool colorable = HatHasColor(_selHat);
            _colorTitle.SetActive(_selHat > 0);
            var titleText = _colorTitle.GetComponent<Text>();
            string title = colorable ? "สีหมวกปาร์ตี้" : "หมวกนี้มีสีของตัวเอง เปลี่ยนสีไม่ได้";
            if (titleText != null && titleText.text != title) titleText.text = title;
            foreach (var go in _swatches) go.SetActive(colorable);
            for (int c = 0; c < _swatchRings.Count; c++) _swatchRings[c].enabled = colorable && c == _selColor;
            string action; bool can;
            int coins = state.Coins.Value;
            if (_selHat < 0) { action = "เลือกหมวกก่อน"; can = false; }
            else if (!Owns(_selHat)) { can = coins >= HatPrices[_selHat]; action = can ? "ซื้อ " + HatPrices[_selHat] + " เหรียญ" : "เงินไม่พอ"; }
            else if (state.Hat.Value == _selHat && (!colorable || state.HatColor.Value == _selColor)) { action = _selHat == 0 ? "ไม่ได้ใส่อยู่" : "ใส่อยู่แล้ว"; can = false; }
            else { action = _selHat == 0 ? "ถอดหมวก" : colorable && state.Hat.Value == _selHat ? "บันทึกสี" : "ใส่"; can = true; }
            if (_actionLabel.text != action) _actionLabel.text = action;
            _action.interactable = can;
        }

        /// <summary>Bottom button: buy the picked hat (in the picked colour) or wear it.</summary>
        private void OnAction()
        {
            var state = Local();
            if (state == null || _selHat < 0) return;
            int color = HatHasColor(_selHat) ? _selColor : state.HatColor.Value;
            if (Owns(_selHat)) Equip(_selHat, color);
            else { SaveSystem.Current.hatColor = color; state.BuyHatRpc((byte)_selHat); }
            RefreshShop();
        }

        /// <summary>Test probe: buy or wear a hat.</summary>
        public static void ProbeHat(int hat, int color)
        {
            var state = Local();
            if (state == null) return;
            if (Owns(hat)) Equip(hat, color);
            else { SaveSystem.Current.hatColor = color; state.BuyHatRpc((byte)hat); }
        }
    }
}
