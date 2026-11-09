using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAppearance : NetworkBehaviour
    {
        public const string LobbySceneName = "Lobby";
        public const int FaceCount = 12;
        public const int ColorCount = 7;
        /// <summary>Spiral-eyes face: shown while the player lies knocked down (ragdoll), not pickable.</summary>
        public const int DizzyFace = 6;
        /// <summary>Googly-eyed face: shown for a moment while the player gets back up, not pickable.</summary>
        public const int DazedFace = 10;
        /// <summary>Faces offered in the customize screen (DizzyFace and DazedFace are kept for knockdowns).</summary>
        public static readonly int[] PickableFaces = { 0, 1, 2, 3, 4, 5, 7, 8, 9, 11 };
        private const float DazedSeconds = 1.2f;
        /// <summary>Bloodshot tired face: shown while stamina is nearly empty (still pickable as a normal face).</summary>
        public const int TiredFace = 11;
        private const float TiredEnter = 0.2f, TiredExit = 0.3f;
        private const string PreferenceKey = "Player.Face.v1";
        private const string ColorPreferenceKey = "Player.Color.v1";

        public static readonly Color[] PastelColors =
        {
            new Color(1.00f, 0.38f, 0.33f, 1f), // 0: แดงพาสเทล (Coral Red, สดขึ้น)
            new Color(1.00f, 0.40f, 0.74f, 1f), // 1: ชมพูพาสเทล (Pink, สดขึ้น)
            new Color(0.42f, 0.96f, 0.20f, 1f), // 2: เขียวไลม์พาสเทล (Lime Green, สดขึ้น)
            new Color(0.30f, 0.46f, 1.00f, 1f), // 3: น้ำเงินพาสเทล (Royal Blue, สดขึ้น)
            new Color(0.12f, 0.80f, 1.00f, 1f), // 4: ฟ้าพาสเทล (Sky Cyan, สดขึ้น)
            new Color(0.08f, 0.86f, 0.58f, 1f), // 5: เขียวมิ้นต์พาสเทล (Emerald/Teal, สดขึ้น)
            new Color(0.64f, 0.32f, 1.00f, 1f), // 6: ม่วงพาสเทล (Purple, สดขึ้น)
        };

        public static readonly string[] PastelColorNames =
        {
            "แดงพาสเทล",
            "ชมพูพาสเทล",
            "เขียวไลม์พาสเทล",
            "น้ำเงินพาสเทล",
            "ฟ้าพาสเทล",
            "เขียวมิ้นต์พาสเทล",
            "ม่วงพาสเทล",
        };

        [SerializeField] private Renderer _faceRenderer;
        [SerializeField] private Texture2D[] _faceTextures;
        private readonly NetworkVariable<int> _face = new(0);
        private readonly NetworkVariable<int> _color = new(0);
        // 0 = normal, 1 = knocked down (DizzyFace), 2 = getting up (DazedFace), 3 = low stamina (TiredFace). Written by the owner.
        private readonly NetworkVariable<byte> _dizzy = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private byte _localDizzy;
        private int _shownFace;
        private float _dazedUntil = -1f;
        private bool _wasDown;
        private ActiveRagdoll _activeRagdoll;
        private RagdollCharacter _fullRagdoll;
        private PlayerStamina _stamina;
        private bool _tired;
        private MaterialPropertyBlock _properties;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// <summary>Soft self-glow in the body colour so the shadow side doesn't go dull (body material has _EMISSION on).</summary>
        public const float BodyGlow = 0.20f;

        public static PlayerAppearance LocalInstance { get; private set; }
        public int CurrentFace => IsSpawned ? SanitizePickable(_face.Value) : SavedFace;
        public int CurrentColor => IsSpawned ? SanitizeColor(_color.Value) : SavedColor;
        public Color CurrentBodyColor => GetPastelColor(CurrentColor);
        public static bool IsLobby => SceneManager.GetActiveScene().name == LobbySceneName;
        public static int SavedFace => SanitizePickable(PlayerPrefs.GetInt(PreferenceKey, 0));
        public static bool IsPickable(int value) => System.Array.IndexOf(PickableFaces, value) >= 0;
        public static int SanitizePickable(int value) => IsPickable(value) ? value : 0;
        /// <summary>True while this player shows the knockdown face.</summary>
        public bool IsDizzy => DizzyState == 1 || DizzyState == 2;
        public bool IsTiredFace => DizzyState == 3;
        private byte DizzyState => IsSpawned ? _dizzy.Value : _localDizzy;
        public static int SavedColor => SanitizeColor(PlayerPrefs.GetInt(ColorPreferenceKey, 0));

        private static int Sanitize(int value) => value >= 0 && value < FaceCount ? value : 0;
        public static int SanitizeColor(int value) => value >= 0 && value < ColorCount ? value : 0;
        public static Color GetPastelColor(int index) => PastelColors[SanitizeColor(index)];
        public static string GetPastelColorName(int index) => PastelColorNames[SanitizeColor(index)];

        public static bool SaveLocal(int value)
        {
            if (!IsLobby || !IsPickable(value)) return false;
            PlayerPrefs.SetInt(PreferenceKey, value);
            PlayerPrefs.Save();
            if (LocalInstance != null && LocalInstance.IsSpawned)
            {
                LocalInstance.ApplyFace(value);
                LocalInstance.SetFaceRpc(value);
            }
            return true;
        }

        public static bool SaveLocalColor(int colorValue)
        {
            if (!IsLobby || colorValue != SanitizeColor(colorValue)) return false;
            PlayerPrefs.SetInt(ColorPreferenceKey, colorValue);
            PlayerPrefs.Save();
            if (LocalInstance != null && LocalInstance.IsSpawned)
            {
                LocalInstance.ApplyColor(colorValue);
                LocalInstance.SetColorRpc(colorValue);
            }
            return true;
        }

        public static bool SaveLocal(int faceValue, int colorValue)
        {
            if (!IsLobby || !IsPickable(faceValue) || colorValue != SanitizeColor(colorValue)) return false;
            PlayerPrefs.SetInt(PreferenceKey, faceValue);
            PlayerPrefs.SetInt(ColorPreferenceKey, colorValue);
            PlayerPrefs.Save();
            if (LocalInstance != null && LocalInstance.IsSpawned)
            {
                LocalInstance.ApplyFace(faceValue);
                LocalInstance.ApplyColor(colorValue);
                LocalInstance.SetFaceRpc(faceValue);
                LocalInstance.SetColorRpc(colorValue);
            }
            return true;
        }

        public override void OnNetworkSpawn()
        {
            _face.OnValueChanged += OnFaceChanged;
            _color.OnValueChanged += OnColorChanged;
            _dizzy.OnValueChanged += OnDizzyChanged;
            ApplyFace(_face.Value);
            ApplyColor(_color.Value);
            if (IsOwner)
            {
                LocalInstance = this;
                if (IsLobby)
                {
                    SetFaceRpc(SavedFace);
                    SetColorRpc(SavedColor);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _face.OnValueChanged -= OnFaceChanged;
            _color.OnValueChanged -= OnColorChanged;
            _dizzy.OnValueChanged -= OnDizzyChanged;
            if (LocalInstance == this) LocalInstance = null;
        }

        [Rpc(SendTo.Server)]
        private void SetFaceRpc(int value, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsLobby ||
                !IsPickable(value)) return;
            _face.Value = value;
        }

        [Rpc(SendTo.Server)]
        private void SetColorRpc(int value, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsLobby ||
                value != SanitizeColor(value)) return;
            _color.Value = value;
        }

        private void OnFaceChanged(int previous, int current) => ApplyFace(current);
        private void OnColorChanged(int previous, int current) => ApplyColor(current);
        private void OnDizzyChanged(byte previous, byte current) => RenderFace();

        // Owner: watch the ragdoll and swap to the dizzy face while knocked down, then the dazed face as it stands.
        private void Update()
        {
            if (IsSpawned && !IsOwner) return;
            if (_activeRagdoll == null) _activeRagdoll = GetComponent<ActiveRagdoll>();
            if (_fullRagdoll == null) _fullRagdoll = GetComponent<RagdollCharacter>();
            bool full = _fullRagdoll != null && _fullRagdoll.isActiveAndEnabled && _fullRagdoll.FullRagdoll;
            bool down = full ? _fullRagdoll.IsKnockedDown
                : _activeRagdoll != null && _activeRagdoll.isActiveAndEnabled && _activeRagdoll.IsKnockedDown;
            if (_wasDown && !down) _dazedUntil = Time.time + DazedSeconds;
            _wasDown = down;
            if (_stamina == null) _stamina = GetComponent<PlayerStamina>();
            if (_stamina != null)
            {
                float st = _stamina.NormalizedStamina;
                _tired = _stamina.IsExhausted || (_tired ? st < TiredExit : st < TiredEnter);
            }
            byte state = down ? (byte)1 : Time.time < _dazedUntil ? (byte)2 : _tired ? (byte)3 : (byte)0;
            if (state == DizzyState) return;
            if (IsSpawned) _dizzy.Value = state;
            else _localDizzy = state;
            RenderFace();
        }

        public void ApplyFace(int value)
        {
            _shownFace = Sanitize(value);
            RenderFace();
        }

        private void RenderFace()
        {
            byte dizzy = DizzyState;
            int value = dizzy == 1 ? DizzyFace : dizzy == 2 ? DazedFace : dizzy == 3 ? TiredFace : _shownFace;
            if (_faceRenderer == null && TryGetComponent(out CharacterRig rig)) _faceRenderer = rig.FaceRenderer;
            if (_faceRenderer == null || _faceTextures == null || _faceTextures.Length < FaceCount) return;
            _properties ??= new MaterialPropertyBlock();
            _faceRenderer.GetPropertyBlock(_properties);
            _properties.SetTexture(BaseMap, _faceTextures[Sanitize(value)]);
            _properties.SetColor(BaseColor, Color.white);
            _faceRenderer.SetPropertyBlock(_properties);
        }

        // The body/glove materials keep _EMISSION on so BodyGlow shows; re-enable it in case the asset was re-validated.
        private static void EnsureGlowKeyword(Renderer r)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || !m.HasProperty(EmissionColor) || m.IsKeywordEnabled("_EMISSION")) continue;
                if (m.name.StartsWith("Mat_chBODY") || m.name.StartsWith("Mat_Glove")) m.EnableKeyword("_EMISSION");
            }
        }

        public void ApplyColor(int value)
        {
            Color chosenColor = GetPastelColor(value);
            if (_faceRenderer == null && TryGetComponent(out CharacterRig rig)) _faceRenderer = rig.FaceRenderer;
            Transform visual = transform.Find(CharacterRig.VisualName);
            Renderer[] renderers = visual != null
                ? visual.GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<Renderer>(true);

            _properties ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r == _faceRenderer || r.gameObject == gameObject) continue;
                if (r.name.StartsWith("face_", System.StringComparison.OrdinalIgnoreCase)) continue;

                EnsureGlowKeyword(r);
                r.GetPropertyBlock(_properties);
                _properties.SetColor(BaseColor, chosenColor);
                _properties.SetColor(LegacyColor, chosenColor);
                _properties.SetColor(EmissionColor, chosenColor * BodyGlow);
                r.SetPropertyBlock(_properties);
            }

            ProceduralPlayerArms arms = GetComponent<ProceduralPlayerArms>();
            if (arms != null)
            {
                arms.SetArmColor(chosenColor);
            }
        }
    }
}
