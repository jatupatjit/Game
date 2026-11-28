using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.Network
{
    /// <summary>
    /// The gear stall's glove displays (built from primitives by LobbyIslandBuilder) use the player's own glove
    /// model instead: on scene load the primitive palm/fingers/cuff are hidden and the right glove from the player
    /// mesh (Resources/Shop/PlayerGlove, cut out of PlayerCharacter.fbx) is shown in the display's colour.
    /// Runtime only, so Lobby.unity needs no re-save.
    /// </summary>
    public static class ShopGloveDisplay
    {
        private static readonly string[] Displays = { "Display_LeatherGlove", "Display_ClimbGlove", "RoofGlove" };
        private const float Scale = 1.05f;
        private static Mesh _mesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (string name in Displays)
            {
                GameObject display = GameObject.Find(name);
                if (display != null) Swap(display.transform);
            }
        }

        private static void Swap(Transform display)
        {
            if (display.Find("PlayerGlove") != null) return;
            if (_mesh == null) _mesh = Resources.Load<Mesh>("Shop/PlayerGlove");
            if (_mesh == null) return;

            Material source = null;
            foreach (Transform child in display)
            {
                if (!child.TryGetComponent(out Renderer r)) continue;
                if (child.name == "Palm") source = r.sharedMaterial;
                r.enabled = false;
            }

            var glove = new GameObject("PlayerGlove", typeof(MeshFilter), typeof(MeshRenderer));
            glove.transform.SetParent(display, false);
            glove.transform.localPosition = new Vector3(0f, -.015f, 0f); // wrist rests on the stand
            glove.transform.localRotation = Quaternion.identity;
            glove.transform.localScale = Vector3.one * Scale;
            glove.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = glove.GetComponent<MeshRenderer>();
            if (source != null) renderer.sharedMaterial = source;
        }
    }
}
