using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame.Player
{
    /// <summary>
    /// Help a team-mate up (George 2026-11-19, idea 11): a player lying after a knockdown or a fall, or getting up,
    /// can be helped by a team-mate standing next to them who taps F. A knockdown ends at once (short stand-up),
    /// a fall-respawn lie starts the get-up at once and plays it faster, a get-up in progress finishes right away.
    /// The owner publishes its down state; the helper asks the server, which checks the distance, tells the downed
    /// player's owner, plays the effect on every screen and posts a feed line.
    /// </summary>
    public sealed partial class RagdollCharacter
    {
        [Header("Help up (team-mate taps F)")]
        [Tooltip("How close (m, hips to hips) a team-mate must be to help this player up.")]
        [SerializeField, Min(.5f)] private float _helpReach = 1.8f;
        [Tooltip("A helped fall-respawn get-up plays its poses this much faster (step seconds x this).")]
        [SerializeField, Range(.2f, 1f)] private float _helpedGetUpStepScale = .5f;
        [Tooltip("Seconds of the physics stand-up after a helped knockdown.")]
        [SerializeField, Min(.1f)] private float _helpedStandUpSeconds = .45f;

        // 0 = standing, 1 = lying (knockdown or after a fall), 2 = getting up. Owner writes, everyone reads.
        private readonly NetworkVariable<byte> _downNet = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private bool _helpHoldsF;
        private float _nextHelpAt, _serverNextHelpAt, _savedGetUpStep = -1f;

        /// <summary>0 standing, 1 lying, 2 getting up (every screen).</summary>
        public byte DownState => IsSpawned ? _downNet.Value : LocalDownState;
        private byte LocalDownState => _gettingUp ? (byte)2 : IsKnockedDown || _getUpPending ? (byte)1 : (byte)0;
        /// <summary>Owner screen: the downed team-mate a tap of F would help now (null when none).</summary>
        public static RagdollCharacter LocalHelpTarget { get; private set; }
        /// <summary>Players in the session (for the "a team-mate can help you" hint).</summary>
        public static int SpawnedCount => s_spawned.Count;

        /// <summary>Owner, Update: publishes the down state, finds a downed team-mate in reach and helps on F.
        /// Returns true from the F press that helped until F is let go, so that press does not also lift a crate.</summary>
        private bool UpdateHelpUp(bool canAct)
        {
            byte down = LocalDownState;
            if (IsSpawned && _downNet.Value != down) _downNet.Value = down;
            if (_savedGetUpStep > 0f && !_gettingUp && !_getUpPending)
            {
                _getUpStepSeconds = _savedGetUpStep;
                _savedGetUpStep = -1f;
            }
            if (_helpHoldsF && !_input.CarryHeld && !_input.CarryReleased) _helpHoldsF = false;

            RagdollCharacter target = canAct && down == 0 && _carry == null && (_wallclimb == null || !_wallclimb.IsClimbing)
                ? FindDownedTeammate() : null;
            LocalHelpTarget = target;
            if (target != null && _input.CarryPressed && Time.time >= _nextHelpAt)
            {
                _nextHelpAt = Time.time + .6f;
                _helpHoldsF = true;
                if (target.IsSpawned) target.HelpUpRequestRpc();
                else target.ApplyHelpUp(); // offline test
            }
            return _helpHoldsF;
        }

        private RagdollCharacter FindDownedTeammate()
        {
            Vector3 me = HipsPosition;
            RagdollCharacter best = null;
            float bestSq = _helpReach * _helpReach;
            foreach (var other in s_spawned)
            {
                if (other == null || other == this || other.DownState == 0) continue;
                Vector3 d = other.HipsPosition - me;
                if (Mathf.Abs(d.y) > 1.4f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > bestSq) continue;
                bestSq = d.sqrMagnitude;
                best = other;
            }
            return best;
        }

        /// <summary>Server: a team-mate asks to help this player up (sent on the downed player's object).</summary>
        [Rpc(SendTo.Server)]
        private void HelpUpRequestRpc(RpcParams rpc = default)
        {
            ulong helper = rpc.Receive.SenderClientId;
            if (helper == OwnerClientId || _downNet.Value == 0 || Time.time < _serverNextHelpAt) return;
            var manager = NetworkManager;
            if (manager == null || !manager.ConnectedClients.TryGetValue(helper, out var client) || client.PlayerObject == null) return;
            var helperBody = client.PlayerObject.GetComponent<RagdollCharacter>();
            Vector3 from = helperBody != null ? helperBody.HipsPosition : client.PlayerObject.transform.position;
            float reach = _helpReach + 1f; // lag allowance: the helper saw this player a little earlier
            if ((from - HipsPosition).sqrMagnitude > reach * reach) return;
            _serverNextHelpAt = Time.time + .8f;
            HelpedUpRpc();
            HelpUpFxRpc(HipsPosition, from);
            GameRoom.ServerFeed($"{GameRoom.NameOfClient(helper)} ช่วยพยุง {GameRoom.NameOfClient(OwnerClientId)} ลุกขึ้น", UITheme.Positive);
        }

        [Rpc(SendTo.Owner)]
        private void HelpedUpRpc() => ApplyHelpUp();

        /// <summary>Owner: get up now.</summary>
        private void ApplyHelpUp()
        {
            if (_physicsRoot == null) return;
            if (_gettingUp)
            {
                // Same soft hand-over as skipping the get-up by moving; FinishGetUp also ends the dizzy stars everywhere.
                FinishGetUp();
                _activeStandUpSeconds = _helpedStandUpSeconds;
                _standUpUntil = Time.time + _activeStandUpSeconds;
                return;
            }
            if (!IsKnockedDown && !_getUpPending) return;
            if (_getUpPending)
            {
                // Lying after a fall: start the get-up now and play it faster (step time restored once it ends).
                if (_savedGetUpStep < 0f) _savedGetUpStep = _getUpStepSeconds;
                _getUpStepSeconds = _savedGetUpStep * _helpedGetUpStepScale;
                _knockdownUntil = Time.time;
                return;
            }
            _knockdownUntil = Time.time;
            _activeStandUpSeconds = _helpedStandUpSeconds;
            _standUpUntil = Time.time + _activeStandUpSeconds;
        }

        /// <summary>Every screen: a green sparkle on the helped player and a lift sound.</summary>
        [Rpc(SendTo.Everyone)]
        private void HelpUpFxRpc(Vector3 at, Vector3 from)
        {
            GameplayFeedback.Play(GameplayFeedback.Cue.Lift, at, .8f);
            GameplayFeedback.Burst(at + Vector3.up * .5f, new Color(.55f, 1f, .62f), 22, 2.4f, .12f);
            GameplayFeedback.Burst(Vector3.Lerp(from, at, .5f) + Vector3.up * .7f, new Color(1f, .95f, .6f), 10, 1.4f, .09f);
        }
    }

    /// <summary>"[F] ช่วยพยุง" over a downed team-mate the local player can help, and a hint for a local player who is down.</summary>
    public sealed class HelpUpHUD : MonoBehaviour
    {
        private RectTransform _canvasRect, _prompt;
        private Text _hint;
        private float _pulse;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("HelpUpHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<HelpUpHUD>().Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 41;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _canvasRect = (RectTransform)transform;

            var prompt = new GameObject("Prompt", typeof(RectTransform), typeof(Image));
            _prompt = (RectTransform)prompt.transform;
            _prompt.SetParent(transform, false);
            _prompt.anchorMin = _prompt.anchorMax = Vector2.zero;
            _prompt.pivot = new Vector2(.5f, 0f);
            _prompt.sizeDelta = new Vector2(250f, 52f);
            var bg = prompt.GetComponent<Image>();
            bg.color = new Color(.08f, .07f, .1f, .78f);
            bg.raycastTarget = false;
            var key = new GameObject("Key", typeof(RectTransform), typeof(Image));
            var keyRect = (RectTransform)key.transform;
            keyRect.SetParent(_prompt, false);
            keyRect.anchorMin = keyRect.anchorMax = new Vector2(0f, .5f);
            keyRect.pivot = new Vector2(0f, .5f);
            keyRect.anchoredPosition = new Vector2(10f, 0f);
            keyRect.sizeDelta = new Vector2(38f, 38f);
            var keyImage = key.GetComponent<Image>();
            keyImage.sprite = UITheme.KeyCapSprite;
            keyImage.color = keyImage.sprite != null ? Color.white : UITheme.Positive;
            keyImage.raycastTarget = false;
            var keyText = UITheme.MakeText(keyRect, "F", "F", 24, UITextRole.Primary, TextAnchor.MiddleCenter);
            keyText.color = UITheme.OnPrimary;
            keyText.raycastTarget = false;
            Stretch((RectTransform)keyText.transform, 0f, 0f);
            var label = UITheme.MakeText(_prompt, "Label", "ช่วยพยุง", 26, UITextRole.Primary, TextAnchor.MiddleLeft);
            label.color = new Color(.75f, 1f, .78f);
            label.raycastTarget = false;
            UITheme.AddOutline(label);
            Stretch((RectTransform)label.transform, 58f, 10f);
            prompt.SetActive(false);

            _hint = UITheme.MakeText(transform, "DownHint", "เพื่อนเดินมากด F ข้าง ๆ เพื่อช่วยพยุงให้ลุกเร็วขึ้น", 26, UITextRole.Primary, TextAnchor.MiddleCenter);
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;
            UITheme.AddOutline(_hint);
            var hintRect = (RectTransform)_hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(.5f, 0f);
            hintRect.pivot = new Vector2(.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 190f);
            hintRect.sizeDelta = new Vector2(900f, 40f);
            _hint.gameObject.SetActive(false);
        }

        private static void Stretch(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f); rect.offsetMax = new Vector2(-right, 0f);
        }

        private static bool Hidden() => PauseMenu.IsPaused || PhotoMode.IsOn || TitleScreen.IsVisible || MissionFailUI.IsVisible ||
            LevelMap.IsOpen || UIPanels.ShopOpen || SpectateMode.IsOn || ExpeditionHUD.BlocksGameplayInput;

        private void LateUpdate()
        {
            bool hidden = Hidden();
            var target = hidden ? null : RagdollCharacter.LocalHelpTarget;
            var view = Camera.main;
            bool show = target != null && target.DownState != 0 && view != null;
            Vector3 screen = show ? view.WorldToScreenPoint(target.HeadPosition + Vector3.up * .55f) : default;
            if (show && screen.z <= .05f) show = false;
            if (_prompt.gameObject.activeSelf != show) { _prompt.gameObject.SetActive(show); _pulse = 1f; }
            if (show)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
                _prompt.anchoredPosition = local + _canvasRect.rect.size * .5f;
                _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime * 5f);
                _prompt.localScale = Vector3.one * (1f + _pulse * .15f + Mathf.Sin(Time.unscaledTime * 6f) * .03f);
            }

            var manager = NetworkManager.Singleton;
            var me = manager != null && manager.IsListening && manager.LocalClient != null && manager.LocalClient.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<RagdollCharacter>() : null;
            bool hint = !hidden && me != null && me.DownState != 0 && RagdollCharacter.SpawnedCount > 1;
            if (_hint.gameObject.activeSelf != hint) _hint.gameObject.SetActive(hint);
        }
    }
}
