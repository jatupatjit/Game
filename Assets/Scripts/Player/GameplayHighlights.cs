using System.Collections.Generic;
using CoopGame.CarrySystem;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// Local-screen highlights for the full-ragdoll player (GAME_RULES: outline through obstacles for the crate and
    /// team-mates). Draws only, changes no gameplay or network state:
    /// - the crate a left click would lift (<see cref="RagdollCharacter.AimedCargo"/>) gets the existing
    ///   <see cref="CarryableOutline"/>;
    /// - team-mates and the mission crate get a see-through copy of their meshes (Custom/HighlightXRay, depth test
    ///   Greater) that shows only where a wall or island hides them;
    /// - each hand's wall grab point (<see cref="Wallclimb.TryGetGrabPreview"/>) gets a small marker.
    /// </summary>
    public sealed class GameplayHighlights : MonoBehaviour
    {
        private const string CloneName = "XRay";
        private const float ScanInterval = .5f;
        private const float MarkerSize = .12f;
        private const float NearHidden = 4f, BodyHalfWidth = .7f;
        private static readonly Color AimColor = new Color(.2f, 1f, .4f, .95f);
        private static readonly Color MateColor = new Color(.35f, .75f, 1f, .6f);
        private static readonly Color CrateColor = new Color(1f, .78f, .25f, .6f);
        private static readonly Color MarkerColor = new Color(1f, 1f, 1f, .9f);

        private Material _mateMaterial, _crateMaterial, _markerMaterial;
        private CarryableObject _aimed;
        private float _nextScan;
        private readonly Transform[] _markers = new Transform[2];
        private readonly List<GameObject> _clones = new();

        /// <summary>For tests: what is highlighted on this screen right now.</summary>
        public static string AimedName { get; private set; }
        public static int XRayCount { get; private set; }
        public static int MarkerCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("GameplayHighlights") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<GameplayHighlights>();
        }

        private void Awake()
        {
            // A Resources material keeps the shader in standalone builds (Shader.Find alone may be stripped).
            var template = Resources.Load<Material>("HighlightXRay");
            if (template == null) { Debug.LogWarning("[GameplayHighlights] Resources/HighlightXRay material missing."); enabled = false; return; }
            _mateMaterial = Make(template, MateColor, UnityEngine.Rendering.CompareFunction.Greater, .9f);
            // A crate is deep (up to 2 m) and has props inside: a larger pull keeps its own planks from counting.
            _crateMaterial = Make(template, CrateColor, UnityEngine.Rendering.CompareFunction.Greater, 1.6f);
            // Grab spots are right under the crosshair, behind the own body from the camera: always drawn on top
            // (they only exist within arm's reach, so they never show through far walls).
            _markerMaterial = Make(template, MarkerColor, UnityEngine.Rendering.CompareFunction.Always, 0f);
        }

        private static Material Make(Material template, Color color, UnityEngine.Rendering.CompareFunction zTest, float depthPull)
        {
            var material = new Material(template) { hideFlags = HideFlags.DontSave };
            material.SetColor("_Color", color);
            material.SetFloat("_ZTest", (float)zTest);
            material.SetFloat("_DepthPull", depthPull);
            return material;
        }

        private void LateUpdate()
        {
            var manager = NetworkManager.Singleton;
            var localObject = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            var local = localObject != null ? localObject.GetComponent<RagdollCharacter>() : null;
            bool active = local != null && local.FullRagdoll;
            UpdateAimed(active ? local.AimedCargo : null);
            UpdateMarkers(active ? localObject.GetComponent<Wallclimb>() : null);
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                ScanXRay(manager, active ? localObject : null);
            }
            if (active) UpdateXRayVisibility(local);
        }

        /// <summary>
        /// The x-ray shows what walls hide, but the own body (the camera sits behind it) would trigger it too. A copy
        /// is hidden when it is close by, or when the line from the camera to it passes behind the own body.
        /// </summary>
        private void UpdateXRayVisibility(RagdollCharacter local)
        {
            var view = Camera.main;
            if (view == null) return;
            Vector3 eye = view.transform.position;
            Vector3 me = local.HipsPosition + Vector3.up * .3f;
            Vector3 toMe = me - eye, toHead = local.HeadPosition - eye;
            float meDistance = toMe.magnitude;
            foreach (var clone in _clones)
            {
                if (clone == null) continue;
                Vector3 target = clone.transform.position;
                Vector3 toTarget = target - eye;
                bool near = (target - me).sqrMagnitude < NearHidden * NearHidden;
                float cover = Mathf.Atan2(BodyHalfWidth, Mathf.Max(.1f, meDistance)) * Mathf.Rad2Deg;
                bool behindMe = toTarget.magnitude > meDistance &&
                    (Vector3.Angle(toTarget, toMe) < cover || Vector3.Angle(toTarget, toHead) < cover); // body or big head
                bool visible = !near && !behindMe;
                if (clone.activeSelf != visible) clone.SetActive(visible);
            }
        }

        private void UpdateAimed(CarryableObject aimed)
        {
            if (aimed == _aimed) return;
            if (_aimed != null && _aimed.TryGetComponent(out CarryableOutline previous)) previous.SetHighlighted(false);
            _aimed = aimed;
            if (_aimed != null && _aimed.TryGetComponent(out CarryableOutline outline)) outline.SetHighlighted(true, AimColor);
            AimedName = _aimed != null ? _aimed.name : null;
        }

        private void UpdateMarkers(Wallclimb climb)
        {
            int shown = 0;
            for (int h = 0; h < 2; h++)
            {
                Vector3 point = default, normal = default;
                bool show = climb != null && climb.TryGetGrabPreview(h == 0, out point, out normal);
                if (show)
                {
                    if (_markers[h] == null) _markers[h] = CreateMarker(h);
                    _markers[h].position = point + normal * .03f;
                    shown++;
                }
                if (_markers[h] != null && _markers[h].gameObject.activeSelf != show) _markers[h].gameObject.SetActive(show);
            }
            MarkerCount = shown;
        }

        private Transform CreateMarker(int hand)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = hand == 0 ? "GrabMarker L" : "GrabMarker R";
            Destroy(go.GetComponent<Collider>());
            DontDestroyOnLoad(go);
            go.transform.localScale = Vector3.one * MarkerSize;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _markerMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Gives team-mates and the mission crate an x-ray copy once (it lives and dies with them).</summary>
        private void ScanXRay(NetworkManager manager, NetworkObject localPlayer)
        {
            _clones.RemoveAll(c => c == null);
            if (localPlayer == null) { SetClonesVisible(false); XRayCount = 0; return; }
            foreach (var player in manager.SpawnManager.SpawnedObjectsList)
                if (player != null && player.IsPlayerObject && player != localPlayer)
                    AddXRay(player.transform, _mateMaterial);
            foreach (var cargo in FindObjectsByType<FragileCargo>())
                AddXRay(cargo.transform, _crateMaterial);
            XRayCount = _clones.Count;
        }

        private void SetClonesVisible(bool visible)
        {
            foreach (var clone in _clones)
                if (clone != null && clone.activeSelf != visible) clone.SetActive(visible);
        }

        private void AddXRay(Transform root, Material material)
        {
            foreach (var source in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!source.enabled || source.name.StartsWith("Outline") || source.name == CloneName) continue;
                if (source.transform.Find(CloneName) != null) continue;
                if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    var go = NewClone(source.transform);
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skinned.sharedMesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.updateWhenOffscreen = true;
                    Finish(copy, material, skinned.sharedMesh.subMeshCount);
                }
                else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    var go = NewClone(source.transform);
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    Finish(go.AddComponent<MeshRenderer>(), material, filter.sharedMesh.subMeshCount);
                }
            }
        }

        private GameObject NewClone(Transform parent)
        {
            var go = new GameObject(CloneName);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            _clones.Add(go);
            return go;
        }

        private static void Finish(Renderer renderer, Material material, int subMeshes)
        {
            var materials = new Material[Mathf.Max(1, subMeshes)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
