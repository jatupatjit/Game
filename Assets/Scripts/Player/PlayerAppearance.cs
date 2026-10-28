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
        private const string PreferenceKey = "Player.Face.v1";
        private const string ColorPreferenceKey = "Player.Color.v1";

        public static readonly Color[] PastelColors =
        {
            new Color(1.00f, 0.53f, 0.49f, 1f), // 0: แดงพาสเทล (Pastel Coral Red)
            new Color(1.00f, 0.58f, 0.83f, 1f), // 1: ชมพูพาสเทล (Pastel Pink)
            new Color(0.55f, 0.91f, 0.38f, 1f), // 2: เขียวไลม์พาสเทล (Pastel Lime Green)
            new Color(0.45f, 0.48f, 1.00f, 1f), // 3: น้ำเงินพาสเทล (Pastel Royal Blue)
            new Color(0.38f, 0.84f, 1.00f, 1f), // 4: ฟ้าพาสเทล (Pastel Sky Cyan)
            new Color(0.32f, 0.77f, 0.59f, 1f), // 5: เขียวมิ้นต์พาสเทล (Pastel Emerald/Teal)
            new Color(0.69f, 0.42f, 0.98f, 1f), // 6: ม่วงพาสเทล (Pastel Purple)
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
        private MaterialPropertyBlock _properties;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");

        public static PlayerAppearance LocalInstance { get; private set; }
        public int CurrentFace => IsSpawned ? Sanitize(_face.Value) : SavedFace;
        public int CurrentColor => IsSpawned ? SanitizeColor(_color.Value) : SavedColor;
        public Color CurrentBodyColor => GetPastelColor(CurrentColor);
        public static bool IsLobby => SceneManager.GetActiveScene().name == LobbySceneName;
        public static int SavedFace => Sanitize(PlayerPrefs.GetInt(PreferenceKey, 0));
        public static int SavedColor => SanitizeColor(PlayerPrefs.GetInt(ColorPreferenceKey, 0));

        private static int Sanitize(int value) => value >= 0 && value < FaceCount ? value : 0;
        public static int SanitizeColor(int value) => value >= 0 && value < ColorCount ? value : 0;
        public static Color GetPastelColor(int index) => PastelColors[SanitizeColor(index)];
        public static string GetPastelColorName(int index) => PastelColorNames[SanitizeColor(index)];

        public static bool SaveLocal(int value)
        {
            if (!IsLobby || value != Sanitize(value)) return false;
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
            if (!IsLobby || faceValue != Sanitize(faceValue) || colorValue != SanitizeColor(colorValue)) return false;
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
            if (LocalInstance == this) LocalInstance = null;
        }

        [Rpc(SendTo.Server)]
        private void SetFaceRpc(int value, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsLobby ||
                value != Sanitize(value)) return;
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

        public void ApplyFace(int value)
        {
            if (_faceRenderer == null && TryGetComponent(out CharacterRig rig)) _faceRenderer = rig.FaceRenderer;
            if (_faceRenderer == null || _faceTextures == null || _faceTextures.Length < FaceCount) return;
            _properties ??= new MaterialPropertyBlock();
            _faceRenderer.GetPropertyBlock(_properties);
            _properties.SetTexture(BaseMap, _faceTextures[Sanitize(value)]);
            _properties.SetColor(BaseColor, Color.white);
            _faceRenderer.SetPropertyBlock(_properties);
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

                r.GetPropertyBlock(_properties);
                _properties.SetColor(BaseColor, chosenColor);
                _properties.SetColor(LegacyColor, chosenColor);
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
