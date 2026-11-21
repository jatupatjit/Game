using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// A shop stall in the Lobby (George 2026-11-20, replaces the K key): walk up to the counter and press
    /// Interact (E) to open its window. Gear stall opens <see cref="GearShop"/>, outfit stall opens the hat shop
    /// (<see cref="Cosmetics"/>). Local only: each player opens their own window; buying still goes through the host RPCs.
    /// Walking away closes the window. Display items listed in <see cref="_spin"/> turn slowly and bob.
    /// </summary>
    public sealed class ShopStall : MonoBehaviour
    {
        public enum Kind { Gear, Outfit }

        [SerializeField] private Kind _kind = Kind.Gear;
        [SerializeField] private string _title = "ร้านอุปกรณ์";
        [Tooltip("Where the player stands to shop (defaults to this transform).")]
        [SerializeField] private Transform _counter;
        [SerializeField] private float _radius = 3.2f;
        [Tooltip("Sign text anchor above the stall.")]
        [SerializeField] private Transform _signAnchor;
        [SerializeField] private Color _signColor = new Color(1f, .97f, .9f);
        [Tooltip("Display items that turn and bob.")]
        [SerializeField] private Transform[] _spin;

        private TextMeshPro _prompt;
        private string _shownPrompt;
        private Vector3[] _spinBase;
        private bool _near;

        public Kind StallKind => _kind;
        private Transform Counter => _counter != null ? _counter : transform;

        private bool PanelOpen => _kind == Kind.Gear ? GearShop.IsOpen : Cosmetics.IsOpen;

        private void Start()
        {
            if (_signAnchor != null)
            {
                var sign = MakeText("SignText", _signAnchor, _title, 6f, _signColor);
                sign.outlineWidth = .22f;
                sign.outlineColor = new Color32(59, 38, 24, 255);
            }
            // George 2026-11-21: text only, no plate behind it; a dark outline + soft drop shadow keep it readable on the stall.
            _prompt = MakeText("Prompt", transform, "", 3f, Color.white);
            var mat = _prompt.fontMaterial; // own instance, the shared font asset is untouched
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .75f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .4f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, .4f);
            _prompt.outlineWidth = .28f;
            _prompt.outlineColor = new Color32(26, 18, 20, 255);
            _prompt.gameObject.SetActive(false);
            if (_spin != null)
            {
                _spinBase = new Vector3[_spin.Length];
                for (int i = 0; i < _spin.Length; i++) if (_spin[i] != null) _spinBase[i] = _spin[i].localPosition;
            }
        }

        private static TextMeshPro MakeText(string name, Transform parent, string text, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            var font = UITheme.TmpFont;
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            return tmp;
        }

        private static Transform LocalPlayer()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.transform : null;
        }

        private static bool Blocked() => PauseMenu.IsPaused || TitleScreen.IsVisible || PhotoMode.IsOn || DevCheatMenu.IsOpen ||
                                         SpectateMode.IsOn || LobbyCharacterCustomizationUI.IsVisible || LevelMap.IsOpen;

        private void Update()
        {
            AnimateDisplay();
            var player = LocalPlayer();
            Vector3 counter = Counter.position;
            Vector3 flat = player != null ? player.position - counter : Vector3.positiveInfinity;
            float vertical = player != null ? Mathf.Abs(flat.y) : 999f;
            flat.y = 0f;
            float distance = player != null ? flat.magnitude : 999f;
            _near = distance <= _radius && vertical < 3f;

            bool open = PanelOpen;
            if (open && distance > _radius + 2.5f) SetPanel(false);

            bool showPrompt = _near && !open && !Blocked() && !UIPanels.AnyOpen;
            if (_prompt != null)
            {
                if (_prompt.gameObject.activeSelf != showPrompt) _prompt.gameObject.SetActive(showPrompt);
                if (showPrompt)
                {
                    string text = "กด " + GameSettings.KeyName(BindRole.Interact) + "  เปิด" + _title;
                    if (text != _shownPrompt)
                    {
                        _shownPrompt = text;
                        _prompt.text = text;
                    }
                    _prompt.transform.position = counter + Vector3.up * 2.2f;
                    var cam = Camera.main;
                    if (cam != null) _prompt.transform.rotation = Quaternion.LookRotation(_prompt.transform.position - cam.transform.position);
                }
            }

            if (!_near || Blocked()) return;
            if (!GameSettings.Pressed(BindRole.Interact)) return;
            if (open) SetPanel(false);
            else if (!IsTyping()) SetPanel(true);
        }

        private static bool IsTyping()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            return selected != null && (selected.GetComponent<UnityEngine.UI.InputField>() != null || selected.GetComponent<TMP_InputField>() != null);
        }

        private void SetPanel(bool open)
        {
            if (_kind == Kind.Gear) GearShop.SetOpen(open); else Cosmetics.SetOpen(open);
            if (open) GameplayFeedback.Play(GameplayFeedback.Cue.Click, Counter.position, .6f);
        }

        /// <summary>Test probe: open/close this stall's window as if E was pressed at the counter.</summary>
        public void ProbeOpen(bool open) => SetPanel(open);

        private void AnimateDisplay()
        {
            if (_spin == null || _spinBase == null) return;
            float t = Time.time;
            for (int i = 0; i < _spin.Length; i++)
            {
                var item = _spin[i];
                if (item == null) continue;
                item.localPosition = _spinBase[i] + Vector3.up * (Mathf.Sin(t * 1.6f + i * 1.3f) * .05f);
                item.Rotate(0f, 35f * Time.deltaTime, 0f, Space.World);
            }
        }
    }
}
