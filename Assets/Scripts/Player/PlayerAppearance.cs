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
        private const string PreferenceKey = "Player.Face.v1";

        [SerializeField] private Renderer _faceRenderer;
        [SerializeField] private Texture2D[] _faceTextures;
        private readonly NetworkVariable<int> _face = new(0);
        private MaterialPropertyBlock _properties;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static PlayerAppearance LocalInstance { get; private set; }
        public int CurrentFace => IsSpawned ? _face.Value : SavedFace;
        public static bool IsLobby => SceneManager.GetActiveScene().name == LobbySceneName;
        public static int SavedFace => Sanitize(PlayerPrefs.GetInt(PreferenceKey, 0));
        private static int Sanitize(int value) => value >= 0 && value < FaceCount ? value : 0;

        public static bool SaveLocal(int value)
        {
            if (!IsLobby || value != Sanitize(value)) return false;
            PlayerPrefs.SetInt(PreferenceKey, value);
            PlayerPrefs.Save();
            if (LocalInstance != null && LocalInstance.IsSpawned)
                LocalInstance.SetFaceRpc(value);
            return true;
        }

        public override void OnNetworkSpawn()
        {
            _face.OnValueChanged += OnFaceChanged;
            ApplyFace(_face.Value);
            if (IsOwner)
            {
                LocalInstance = this;
                if (IsLobby) SetFaceRpc(SavedFace);
            }
        }

        public override void OnNetworkDespawn()
        {
            _face.OnValueChanged -= OnFaceChanged;
            if (LocalInstance == this) LocalInstance = null;
        }

        [Rpc(SendTo.Server)]
        private void SetFaceRpc(int value, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsLobby ||
                value != Sanitize(value)) return;
            _face.Value = value;
        }

        private void OnFaceChanged(int previous, int current) => ApplyFace(current);

        public void ApplyFace(int value)
        {
            if (_faceRenderer == null || _faceTextures == null || _faceTextures.Length < FaceCount) return;
            _properties ??= new MaterialPropertyBlock();
            _faceRenderer.GetPropertyBlock(_properties);
            _properties.SetTexture(BaseMap, _faceTextures[Sanitize(value)]);
            _properties.SetColor(BaseColor, Color.white);
            _faceRenderer.SetPropertyBlock(_properties);
        }
    }
}
