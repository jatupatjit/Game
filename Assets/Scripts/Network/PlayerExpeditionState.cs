using CoopGame.CarrySystem;
using CoopGame.Player;
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
        public readonly NetworkVariable<byte> Slot1 = new(0);
        public readonly NetworkVariable<byte> Slot2 = new(0);
        public readonly NetworkVariable<byte> Slot3 = new(0);
        private int _stageStartCoins;
        private byte _start1, _start2, _start3;
        private string _stageScene;
        private bool _rewarded;
        private PlayerStamina _stamina;
        private PlayerCarry _carry;
        private bool _restoreStamina;
        private float _nextRequestAt;

        public static string CardName(byte id) => id switch
        {
            1 => "ENDURANCE", 2 => "CARGO GUARD", 3 => "TEAMWORK", _ => "CHOOSE A CARD"
        };
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
            if (IsServer)
            {
                DeliveryPoint.OnDeliverySuccessGlobal += AwardDelivery;
                BeginStage(SceneManager.GetActiveScene().name);
            }
        }

        public override void OnNetworkDespawn()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
            }
            else
            {
                _stageScene = sceneName;
                _stageStartCoins = Coins.Value;
                _start1 = Slot1.Value; _start2 = Slot2.Value; _start3 = Slot3.Value;
            }
            Card.Value = 0;
            _rewarded = false;
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
            if (Card.Value != 0 || id < 1 || id > 3 || _rewarded || MissionFailUI.IsVisible) return;
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
            if (_stamina != null) _stamina.CarryDrainMultiplier = Card.Value == 1 ? .6f : 1f;
        }
    }
}

