using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Streams the owner's drawn active-ragdoll pose (hips relative to CharacterVisual, every other body bone
    /// as a local rotation) to the other players at <see cref="SendRate"/> Hz over an unreliable RPC, and plays
    /// it back on their side through a short interpolation buffer on the shared server clock. Everyone therefore
    /// sees the owner's own wobble, stumble and knockdown instead of a local guess.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollPoseSync : NetworkBehaviour
    {
        public const float SendRate = 30f;
        [Tooltip("Seconds behind the server clock that remote poses are drawn, to hide jitter and loss")]
        [SerializeField] private float _interpolationDelay = .1f;
        [Tooltip("Hold the last pose this long when packets stop, then let the local animation show again")]
        [SerializeField] private float _staleSeconds = .5f;

        private struct PoseMessage : INetworkSerializable
        {
            public double Time;
            public Vector3 HipsLocal;
            public Quaternion HipsRotation;
            public Quaternion[] Local;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Time);
                serializer.SerializeValue(ref HipsLocal);
                SerializeQuaternion(serializer, ref HipsRotation);
                if (serializer.IsReader) Local = new Quaternion[ActiveRagdoll.BoneCount];
                for (int i = 1; i < ActiveRagdoll.BoneCount; i++) SerializeQuaternion(serializer, ref Local[i]);
            }

            // 4 x int16 per rotation (8 bytes): plenty for drawing.
            private static void SerializeQuaternion<T>(BufferSerializer<T> serializer, ref Quaternion q) where T : IReaderWriter
            {
                short x = 0, y = 0, z = 0, w = 0;
                if (serializer.IsWriter)
                {
                    if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    x = (short)Mathf.RoundToInt(q.x * 32767f); y = (short)Mathf.RoundToInt(q.y * 32767f);
                    z = (short)Mathf.RoundToInt(q.z * 32767f); w = (short)Mathf.RoundToInt(q.w * 32767f);
                }
                serializer.SerializeValue(ref x); serializer.SerializeValue(ref y);
                serializer.SerializeValue(ref z); serializer.SerializeValue(ref w);
                if (serializer.IsReader) q = Quaternion.Normalize(new Quaternion(x / 32767f, y / 32767f, z / 32767f, w / 32767f));
            }
        }

        private const int BufferSize = 16;
        private readonly PoseMessage[] _buffer = new PoseMessage[BufferSize];
        private int _count;
        private float _nextSendAt;
        private float _lastReceivedAt = -100f;

        public int ReceivedCount { get; private set; }
        public int SentCount { get; private set; }

        /// <summary>Owner: called by ActiveRagdoll after it has drawn this frame's pose.</summary>
        public void PublishDrawnPose(Transform[] bones, Transform visual)
        {
            if (!IsSpawned || !IsOwner || NetworkManager == null) return;
            if (Time.unscaledTime < _nextSendAt) return;
            _nextSendAt = Time.unscaledTime + 1f / SendRate;
            if (NetworkManager.IsServer && NetworkManager.ConnectedClientsIds.Count < 2) return; // nobody to see it

            var msg = new PoseMessage
            {
                Time = NetworkManager.ServerTime.Time,
                HipsLocal = visual.InverseTransformPoint(bones[0].position),
                HipsRotation = Quaternion.Inverse(visual.rotation) * bones[0].rotation,
                Local = new Quaternion[ActiveRagdoll.BoneCount],
            };
            for (int i = 1; i < ActiveRagdoll.BoneCount; i++) msg.Local[i] = bones[i].localRotation;
            PoseRpc(msg);
            SentCount++;
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        private void PoseRpc(PoseMessage msg)
        {
            if (IsOwner) return;
            // Keep the buffer ordered by time; drop late duplicates.
            if (_count > 0 && msg.Time <= _buffer[_count - 1].Time) return;
            if (_count == BufferSize) { System.Array.Copy(_buffer, 1, _buffer, 0, BufferSize - 1); _count--; }
            _buffer[_count++] = msg;
            _lastReceivedAt = Time.unscaledTime;
            ReceivedCount++;
        }

        /// <summary>Remote: the owner's pose at (server time - delay). False when there is nothing recent.</summary>
        public bool TrySampleRemotePose(Quaternion[] local, out Vector3 hipsLocal, out Quaternion hipsRotation)
        {
            hipsLocal = Vector3.zero;
            hipsRotation = Quaternion.identity;
            if (!IsSpawned || _count == 0 || Time.unscaledTime - _lastReceivedAt > _staleSeconds) return false;

            double renderTime = NetworkManager.ServerTime.Time - _interpolationDelay;
            int newer = 0;
            while (newer < _count && _buffer[newer].Time < renderTime) newer++;
            if (newer == 0) { Copy(_buffer[0], local, out hipsLocal, out hipsRotation); return true; }
            if (newer == _count) { Copy(_buffer[_count - 1], local, out hipsLocal, out hipsRotation); return true; }

            ref PoseMessage a = ref _buffer[newer - 1];
            ref PoseMessage b = ref _buffer[newer];
            float t = (float)((renderTime - a.Time) / System.Math.Max(1e-4, b.Time - a.Time));
            hipsLocal = Vector3.Lerp(a.HipsLocal, b.HipsLocal, t);
            hipsRotation = Quaternion.Slerp(a.HipsRotation, b.HipsRotation, t);
            for (int i = 1; i < ActiveRagdoll.BoneCount; i++) local[i] = Quaternion.Slerp(a.Local[i], b.Local[i], t);
            return true;
        }

        private static void Copy(in PoseMessage m, Quaternion[] local, out Vector3 hipsLocal, out Quaternion hipsRotation)
        {
            hipsLocal = m.HipsLocal;
            hipsRotation = m.HipsRotation;
            for (int i = 1; i < ActiveRagdoll.BoneCount; i++) local[i] = m.Local[i];
        }
    }
}
