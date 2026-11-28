using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// Invisible posts in the Lobby (George 2026-11-28): the 17 pine trees ("ต้นสน" under Nature) keep their capsule
    /// collider on the tree's root, but the tree meshes are children moved 32-54 m away, so each collider stood alone
    /// in the plaza. On every scene load, a solid collider on an object with no renderer of its own, whose child
    /// models it does not touch at all and lie more than <see cref="MinOffset"/> away, is moved (same height, same
    /// size) under the middle of those models. Runtime only: the scene files are not changed.
    /// </summary>
    public static class StrayColliderFix
    {
        private const float MinOffset = 2f;

        /// <summary>How many colliders the last scene load moved. For tests.</summary>
        public static int LastMoved { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int moved = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    if (TryRecenter(c)) moved++;
            LastMoved = moved;
            if (moved > 0) Debug.Log($"[StrayColliderFix] {scene.name}: moved {moved} collider(s) back onto their models");
        }

        private static bool TryRecenter(Collider c)
        {
            if (c == null || c.isTrigger || c.GetComponent<Renderer>() != null) return false;
            if (!(c is CapsuleCollider || c is BoxCollider || c is SphereCollider)) return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false; // moving props: leave alone
            Bounds visual = default;
            bool any = false;
            foreach (var r in c.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { visual = r.bounds; any = true; }
                else visual.Encapsulate(r.bounds);
            }
            if (!any) return false;
            Bounds solid = c.bounds;
            if (solid.Intersects(visual)) return false;
            Vector2 flatOffset = new Vector2(visual.center.x - solid.center.x, visual.center.z - solid.center.z);
            if (flatOffset.magnitude < MinOffset) return false;

            Vector3 target = new Vector3(visual.center.x, solid.center.y, visual.center.z);
            Vector3 local = c.transform.InverseTransformPoint(target);
            switch (c)
            {
                case CapsuleCollider capsule: capsule.center = local; break;
                case BoxCollider box: box.center = local; break;
                case SphereCollider sphere: sphere.center = local; break;
            }
            return true;
        }
    }
}
