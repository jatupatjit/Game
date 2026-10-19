using CoopGame.CarrySystem;
using CoopGame.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>Server-owned wallet, three inventory slots and one carry card per stage.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerExpeditionState : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Coins = new(0);
        public readonly NetworkVariable<byte> Card = new(0);
        /// <summary>The three card ids the server offered this player (CardCatalog packing). Only the owner (and the
        /// server) can read it; SelectCardRpc accepts nothing else.</summary>
        public readonly NetworkVariable<uint> Offer = new(0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<byte> Slot1 = new(0);
        public readonly NetworkVariable<byte> Slot2 = new(0);
        public readonly NetworkVariable<byte> Slot3 = new(0);
        /// <summary>Display name for HUD player lists (owner-written: the Steam name, or "Player N").
        /// Presentation only; no gameplay reads it.</summary>
        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> DisplayName =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private int _stageStartCoins;
        private byte _start1, _start2, _start3;
        private string _stageScene;
        private bool _rewarded;
        private PlayerStamina _stamina;
        private PlayerCarry _carry;
        private bool _restoreStamina;
        private float _nextRequestAt;
        private System.Random _drawRng;
        private uint _stageOffer;
        [Tooltip("CLOSE SUPPORT: a holder within this distance (m) of a carrying player slows that player's stamina drain.")]
        [SerializeField, Min(0f)] private float _closeSupportRadius = CardCatalog.CloseSupportRadius;

        private static readonly System.Collections.Generic.List<PlayerExpeditionState> All = new();

        /// <summary>Effect magnitude of this player's card for <paramref name="effect"/>, or 1 (neutral) without it.</summary>
        public float CardMagnitude(CardEffect effect)
        {
            CardDef def = CardCatalog.Get(Card.Value);
            return def.Effect == effect ? def.Magnitude : 1f;
        }
        public bool HasCard(CardEffect effect) => CardCatalog.Get(Card.Value).Effect == effect;

        /// <summary>True when another player holding CLOSE SUPPORT is within the radius of this player.</summary>
        public bool IsCloseSupportNear()
        {
            for (int i = 0; i < All.Count; i++)
            {
                var other = All[i];
                if (other == null || other == this || !other.IsSpawned || !other.HasCard(CardEffect.CloseSupport)) continue;
                if ((other.transform.position - transform.position).sqrMagnitude <= _closeSupportRadius * _closeSupportRadius) return true;
            }
            return false;
        }
        public static string ItemName(byte id) => id switch
        {
            1 => "Stamina Elixir", 2 => "Cargo Padding", _ => "Empty"
        };
        public static int ItemPrice(byte id) => id == 1 ? 40 : id == 2 ? 60 : int.MaxValue;
        public byte GetSlot(int index) => index == 0 ? Slot1.Value : index == 1 ? Slot2.Value : Slot3.Value;
        private NetworkVariable<byte> Slot(int index) => index == 0 ? Slot1 : index == 1 ? Slot2 : Slot3;

        public override void OnNetworkSpawn()
        {
            _stamina = GetComponent<PlayerStamina>();
            _carry = GetComponent<PlayerCarry>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (!All.Contains(this)) All.Add(this);
            if (IsOwner)
            {
                var lobby = SteamLobbyManager.Instance;
                string name = lobby != null && !lobby.IsLocalSession && lobby.IsSteamMultiplayerAvailable
                    ? lobby.SteamPlayerName : $"Player {OwnerClientId + 1}";
                while (name.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(name) > FixedString64Bytes.UTF8MaxLengthInBytes)
                    name = name.Substring(0, name.Length - 1); // UTF-8 byte-safe (Thai is 3 bytes/char)
                var fixedName = new FixedString64Bytes(name);
                DisplayName.Value = fixedName;
            }
            if (IsServer)
            {
                DeliveryPoint.OnDeliverySuccessGlobal += AwardDelivery;
                BeginStage(SceneManager.GetActiveScene().name);
            }
        }

        public override void OnNetworkDespawn()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            All.Remove(this);
            DeliveryPoint.OnDeliverySuccessGlobal -= AwardDelivery;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsServer && mode == LoadSceneMode.Single) BeginStage(scene.name);
        }

        private void BeginStage(string sceneName)
        {
            if (_stageScene == sceneName)
            {
                // A retry restores the stage-entry wallet and inventory; rewards cannot be farmed.
                Coins.Value = _stageStartCoins;
                Slot1.Value = _start1; Slot2.Value = _start2; Slot3.Value = _start3;
                Offer.Value = _stageOffer; // retry of the same stage keeps the same three cards
            }
            else
            {
                _stageScene = sceneName;
                _stageStartCoins = Coins.Value;
                _start1 = Slot1.Value; _start2 = Slot2.Value; _start3 = Slot3.Value;
                RerollOffer(); // only a new stage draws new cards
                _stageOffer = Offer.Value;
            }
            Card.Value = 0;
            _rewarded = false;
        }

        /// <summary>Server only: draws a fresh set of three distinct cards for this player. Called for a new stage only (a retry reuses the stored offer).</summary>
        public void RerollOffer()
        {
            if (!IsServer) return;
            _drawRng ??= new System.Random(unchecked(System.Environment.TickCount * 31 + (int)OwnerClientId * 7919));
            Offer.Value = CardCatalog.Draw(_drawRng);
        }

        private void AwardDelivery(DeliveryPoint.DeliveryResult result)
        {
            if (!IsServer || _rewarded || !result.IsSuccess) return;
            _rewarded = true;
            Coins.Value = Mathf.Min(1000000, Coins.Value + result.CoinsEarned);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SelectCardRpc(byte id)
        {
            if (Card.Value != 0 || !CardCatalog.IsOffered(Offer.Value, id) || _rewarded || MissionFailUI.IsVisible) return;
            Card.Value = id;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void BuyItemRpc(byte id)
        {
            if (!_rewarded || id < 1 || id > 2 || Time.unscaledTime < _nextRequestAt) return;
            _nextRequestAt = Time.unscaledTime + .2f;
            int price = ItemPrice(id);
            if (Coins.Value < price) return;
            for (int i = 0; i < 3; i++)
            {
                if (GetSlot(i) != 0) continue;
                Coins.Value -= price;
                Slot(i).Value = id;
                return;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void UseItemRpc(int index)
        {
            if (index < 0 || index > 2 || Time.unscaledTime < _nextRequestAt || MissionFailUI.IsVisible) return;
            byte item = GetSlot(index);
            if (item == 0) return;
            if (item == 2)
            {
                // Padding targets only the owner's currently carried cargo, checked by the host.
                if (_carry == null || !(_carry.CurrentCarryable is FragileCargo cargo) ||
                    cargo.IsDestroyed || cargo.IsSecured || !cargo.HasCarrier(OwnerClientId) ||
                    cargo.HasPadding || (cargo.transform.position - transform.position).sqrMagnitude > 16f) return;
                cargo.AddPadding();
            }
            else RestoreStaminaRpc();
            Slot(index).Value = 0;
            _nextRequestAt = Time.unscaledTime + .3f;
        }

        [Rpc(SendTo.Owner)]
        private void RestoreStaminaRpc() => _restoreStamina = true;

        private void FixedUpdate()
        {
            if (!IsOwner) return;
            if (_restoreStamina)
            {
                _restoreStamina = false;
                _stamina?.ResetStamina();
            }
            if (_stamina != null)
            {
                // Card effects on stamina (owner-simulated, like the stamina itself); PlayerStamina clamps the drain to 0.1..1.
                float drain = CardMagnitude(CardEffect.StrongArms) * CardMagnitude(CardEffect.Endurance);
                if (_carry != null && _carry.IsCarrying && IsCloseSupportNear()) drain *= CardCatalog.CloseSupportDrain;
                _stamina.CarryDrainMultiplier = drain;
                _stamina.RestRegenMultiplier = CardMagnitude(CardEffect.QuickRecovery);
                _stamina.ActionCostMultiplier = CardMagnitude(CardEffect.LightStep);
            }
        }
    }
}

