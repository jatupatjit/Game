using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    /// <summary>
    /// Player level / XP across runs (saved in <see cref="SaveSystem"/>). On delivery every machine adds XP for its
    /// own player: 100 + 50 per star + 25 per award won, times the difficulty reward. A small panel at the top of the
    /// screen shows the gain and a level-up. The level is shown to others through
    /// <see cref="PlayerExpeditionState.PlayerLevel"/> (scoreboard).
    /// </summary>
    public sealed class PlayerProgress : MonoBehaviour
    {
        public const int MaxLevel = 99;
        private static PlayerProgress _instance;

        private GameObject _panel;
        private Text _title, _detail;
        private Image _fill;
        private CanvasGroup _group;
        private float _showAt = -1f, _hideAt = -1f;
        private float _fromRatio, _toRatio;
        private bool _levelUp;

        /// <summary>XP needed to go from <paramref name="level"/> to the next.</summary>
        public static int XpForNext(int level) => 200 + 100 * (Mathf.Max(1, level) - 1);
        public static int Level => Mathf.Clamp(SaveSystem.Current.level, 1, MaxLevel);
        public static float Ratio => Level >= MaxLevel ? 1f : SaveSystem.Current.xp / (float)XpForNext(Level);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("PlayerProgress");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlayerProgress>();
            _instance.Build();
        }

        private void OnEnable() => LevelResults.Ready += OnLevelDone;
        private void OnDisable() => LevelResults.Ready -= OnLevelDone;

        private static PlayerExpeditionState Local()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        /// <summary>Adds XP, handles level-ups and saves. Returns the number of levels gained.</summary>
        public static int AddXp(int amount)
        {
            var data = SaveSystem.Current;
            int gained = 0;
            data.xp += Mathf.Max(0, amount);
            while (data.level < MaxLevel && data.xp >= XpForNext(data.level))
            {
                data.xp -= XpForNext(data.level);
                data.level++;
                gained++;
            }
            if (data.level >= MaxLevel) data.xp = 0;
            SaveSystem.Save();
            var local = Local();
            if (local != null && local.IsSpawned && local.IsOwner) local.PlayerLevel.Value = (byte)data.level;
            return gained;
        }

        private void OnLevelDone()
        {
            var local = Local();
            var mission = FindAnyObjectByType<LevelMission>();
            if (local == null || mission == null || mission.Delivery == null) return;
            int maxHp = mission.Cargo != null ? mission.Cargo.MaxHP : 100;
            int stars = LevelResults.Stars(mission.Delivery.LastDeliveryResult.RemainingHP, maxHp);
            int awards = 0;
            string me = LevelResults.NameOf(local);
            foreach (var a in LevelResults.Awards()) if (a.Player == me) awards++;
            float mult = GameRoom.RewardMultiplier;
            int xp = Mathf.RoundToInt((100 + stars * 50 + awards * 25) * mult);
            float from = Ratio;
            SaveSystem.Current.deliveries++;
            int ups = AddXp(xp);
            Show(xp, stars, awards, mult, from, ups);
        }

        private void Show(int xp, int stars, int awards, float mult, float from, int ups)
        {
            _levelUp = ups > 0;
            _title.text = _levelUp ? $"เลเวลอัป!  Lv.{Level}" : $"+{xp} XP";
            _title.color = _levelUp ? new Color(1f, .85f, .3f) : Color.white;
            _detail.text = $"+{xp} XP  (ส่งของ 100 + ดาว {stars}x50 + รางวัล {awards}x25) x{mult:0.0} ความยาก{GameRoom.Current.Name}  •  Lv.{Level}  {SaveSystem.Current.xp}/{XpForNext(Level)}";
            _fromRatio = _levelUp ? 0f : from;
            _toRatio = Ratio;
            _showAt = Time.unscaledTime + 1.2f;
            _hideAt = _showAt + 6f;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _panel = new GameObject("XpPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rect = (RectTransform)_panel.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(760f, 120f);
            _panel.GetComponent<Image>().color = new Color(.1f, .09f, .14f, .85f);
            _panel.GetComponent<Image>().raycastTarget = false;
            _group = _panel.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _title = UITheme.MakeText(rect, "Title", "", 38, UITextRole.Primary, TextAnchor.MiddleCenter);
            UITheme.AddOutline(_title);
            ((RectTransform)_title.transform).anchoredPosition = new Vector2(0f, 30f);
            ((RectTransform)_title.transform).sizeDelta = new Vector2(740f, 50f);
            _detail = UITheme.MakeText(rect, "Detail", "", 18, UITextRole.Secondary, TextAnchor.MiddleCenter);
            ((RectTransform)_detail.transform).anchoredPosition = new Vector2(0f, -8f);
            ((RectTransform)_detail.transform).sizeDelta = new Vector2(740f, 28f);
            var bar = UITheme.MakeBar(rect, "XpBar", new Vector2(680f, 18f), new Color(.45f, .8f, 1f), out _fill);
            ((RectTransform)bar.transform).anchoredPosition = new Vector2(0f, -38f);
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (_showAt < 0f) return;
            float now = Time.unscaledTime;
            if (now < _showAt) return;
            if (now > _hideAt) { _panel.SetActive(false); _showAt = -1f; return; }
            if (!_panel.activeSelf) _panel.SetActive(true);
            float age = now - _showAt;
            _group.alpha = Mathf.Clamp01(age / .3f) * Mathf.Clamp01((_hideAt - now) / .5f);
            UITheme.SetBarFill(_fill, Mathf.Lerp(_fromRatio, _toRatio, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .3f) / 1.2f))));
            if (_levelUp) _title.transform.localScale = Vector3.one * (1f + .06f * Mathf.Sin(age * 6f));
        }
    }
}
