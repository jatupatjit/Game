using System.Collections.Generic;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// The "ready to catch" outline of a player holding F (<see cref="RagdollCharacter"/>): a pulsing yellow inverted-hull
    /// copy of every body renderer (Custom/CarryableOutline, the crate outline shader, with _UseMeshNormal: the body
    /// has no baked smooth normals, so it extrudes along its own normals). The flat face decal is skipped. Copies follow their
    /// source's visibility, so a first-person body stays hidden. Drawing only.
    /// </summary>
    public sealed class CatchGlow : MonoBehaviour
    {
        private const string CloneName = "CatchGlow";
        private const float Width = 6f;
        private readonly List<(Renderer source, Renderer copy)> _copies = new();
        private Material _material;
        private Color _color;
        private bool _on;
        private float _fade;

        public static CatchGlow Create(Transform visual, Color color)
        {
            var glow = visual.gameObject.AddComponent<CatchGlow>();
            glow._color = color;
            glow.Build(visual);
            return glow;
        }

        private void Build(Transform visual)
        {
            // A Resources material keeps the shader in standalone builds (Shader.Find alone may be stripped).
            var template = Resources.Load<Material>("CarryableOutline");
            if (template == null) return;
            _material = new Material(template) { hideFlags = HideFlags.DontSave };
            _material.SetColor("_OutlineColor", _color);
            _material.SetFloat("_OutlineWidth", Width);
            _material.SetFloat("_UseMeshNormal", 1f);
            _material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);

            foreach (var source in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (source.name.StartsWith(CloneName) || source.name == "XRay" || source.name.StartsWith("Outline") ||
                    source.name.StartsWith("face")) continue;
                Renderer copy = null;
                if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    var go = NewClone(source.transform);
                    var s = go.AddComponent<SkinnedMeshRenderer>();
                    s.sharedMesh = skinned.sharedMesh;
                    s.bones = skinned.bones;
                    s.rootBone = skinned.rootBone;
                    s.updateWhenOffscreen = true;
                    copy = Finish(s, skinned.sharedMesh.subMeshCount);
                }
                else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    var go = NewClone(source.transform);
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    copy = Finish(go.AddComponent<MeshRenderer>(), filter.sharedMesh.subMeshCount);
                }
                if (copy != null) _copies.Add((source, copy));
            }
            SetVisible(false);
        }

        private static GameObject NewClone(Transform parent)
        {
            var go = new GameObject(CloneName);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return go;
        }

        private Renderer Finish(Renderer renderer, int subMeshes)
        {
            var materials = new Material[Mathf.Max(1, subMeshes)];
            for (int i = 0; i < materials.Length; i++) materials[i] = _material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        public void SetOn(bool on) => _on = on;

        private void LateUpdate()
        {
            if (_material == null) return;
            _fade = Mathf.MoveTowards(_fade, _on ? 1f : 0f, Time.deltaTime / .15f);
            bool visible = _fade > 0f;
            SetVisible(visible);
            if (!visible) return;
            float pulse = .8f + .2f * Mathf.Sin(Time.time * 7f);
            Color c = _color;
            c.a *= _fade;
            _material.SetColor("_OutlineColor", c);
            _material.SetFloat("_OutlineWidth", Width * _fade * pulse);
        }

        private void SetVisible(bool visible)
        {
            foreach (var (source, copy) in _copies)
            {
                if (copy == null) continue;
                bool show = visible && source != null && source.enabled && source.gameObject.activeInHierarchy &&
                            source.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                if (copy.enabled != show) copy.enabled = show;
            }
        }

        private void OnDestroy()
        {
            foreach (var (_, copy) in _copies)
                if (copy != null) Destroy(copy.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}
