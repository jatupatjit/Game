using CoopGame.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.Network
{
    [DisallowMultipleComponent]
    public sealed class LobbyCharacterCustomizationUI : MonoBehaviour
    {
        [SerializeField] private Texture2D[] _faceTextures;
        public static bool IsVisible { get; private set; }
        private static int _closedFrame = -1;
        public static bool BlocksPause => IsVisible || _closedFrame == Time.frameCount;
        private GameObject _panel;
        private Button _launchButton;
        private LobbyUI _lobbyUI;
        private Image _previewBoxImage;
        private RawImage _preview;
        private Text _styleLabel;
        private Text _status;
        private Image[] _faceBgImages;
        private Outline[] _faceOutlines;
        private Image[] _colorRings;
        private int _draft;
        private int _draftColor;
        private InputField _nicknameInput;
        // Hats bought in the shop (K): owned ones can be worn from here too.
        private int _draftHat, _savedHat;
        private Button[] _hatButtons;
        private Image[] _hatRings;
        private Text[] _hatLabels;
        private int _refreezeFrames;
        private bool _restoreCursorLock;
        private Font _font;
        private static Sprite _hangerSprite;

        // Live 3D portrait of the local player (left box): a camera in front of the face renders into a
        // RenderTexture with a bokeh depth of field on its own volume layer, so the face stays sharp and
        // the lobby behind it is blurred. The main camera's volume mask never includes that layer.
        private const int PreviewVolumeLayer = 31;
        private const int PreviewWidth = 544, PreviewHeight = 832;
        private const float PreviewDistance = 3.3f, PreviewFov = 30f;
        private Camera _previewCamera;
        private Light _previewLight;
        private RenderTexture _previewTexture;
        private VolumeProfile _previewProfile;
        // Frozen copy of the player's visible meshes, posed once when the panel opens. Only the preview camera
        // draws it (and skips the real body), so the ragdoll's small arm jitter never shows in the portrait.
        private readonly System.Collections.Generic.List<Renderer> _frozenSources = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<MeshRenderer> _frozenCopies = new System.Collections.Generic.List<MeshRenderer>();
        private readonly System.Collections.Generic.List<Mesh> _frozenMeshes = new System.Collections.Generic.List<Mesh>();
        private GameObject _frozenRoot;
        private MaterialPropertyBlock _copyBlock;
        private bool _previewRendering;
        private bool _freezePending;
        private float _stillTime, _pendingTime;
        private Vector3? _lastFaceCenter;
        private const float SettleSpeed = 0.15f, SettleTime = 0.4f, SettleTimeout = 2.5f;

        private bool CanCustomize
        {
            get
            {
                var manager = NetworkManager.Singleton;
                var player = PlayerAppearance.LocalInstance;
                return PlayerAppearance.IsLobby && manager != null && manager.IsConnectedClient &&
                    !manager.ShutdownInProgress && player != null && player.IsSpawned && player.IsOwner &&
                    (_lobbyUI == null || !_lobbyUI.IsMenuVisible);
            }
        }

        private void Awake()
        {
            if (!PlayerAppearance.IsLobby) { Destroy(gameObject); return; }
            _lobbyUI = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
            Build();
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (IsVisible) Close(false);
            ClearFrozenPose();
            if (_previewCamera != null) Destroy(_previewCamera.gameObject);
            if (_previewTexture != null) { _previewTexture.Release(); Destroy(_previewTexture); }
            if (_previewProfile != null) Destroy(_previewProfile);
        }

        private void SetPreviewActive(bool active)
        {
            if (active && _previewCamera == null) BuildPreviewCamera();
            // Opening while walking: wait until the body has settled into its idle pose before freezing it,
            // so the portrait is upright instead of mid-stride (input is already blocked while the panel is open).
            ClearFrozenPose();
            _freezePending = active;
            _stillTime = _pendingTime = 0f;
            _lastFaceCenter = null;
            if (_previewCamera != null) _previewCamera.gameObject.SetActive(active);
            if (_preview != null && _previewTexture != null)
            {
                _preview.texture = _previewTexture;
                _preview.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            if (active) AimPreview();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        /// <summary>Copies every visible mesh of the local player as a static mesh at its current world pose.</summary>
        private void FreezePose()
        {
            ClearFrozenPose();
            var player = PlayerAppearance.LocalInstance;
            if (player == null) return;
            _frozenRoot = new GameObject("CustomizationFrozenPose");
            foreach (Renderer source in player.GetComponentsInChildren<Renderer>())
            {
                if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) continue;
                Mesh mesh;
                Vector3 scale;
                if (source is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) continue;
                    mesh = new Mesh { name = source.name + " (frozen)" };
                    // Unscaled bake + unscaled transform matches the skinned result (the rig's bones carry the
                    // FBX scale; useScale would apply the renderer's 46x scale a second time).
                    skinned.BakeMesh(mesh, false);
                    _frozenMeshes.Add(mesh);
                    scale = Vector3.one;
                }
                else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    mesh = filter.sharedMesh;
                    scale = source.transform.lossyScale;
                }
                else continue;
                var copy = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(_frozenRoot.transform, false);
                copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                copy.transform.localScale = scale;
                copy.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = copy.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.forceRenderingOff = true; // drawn only for the preview camera
                _frozenSources.Add(source);
                _frozenCopies.Add(renderer);
            }
            CopyFrozenLooks();
        }

        /// <summary>Face texture and body colour live in property blocks: mirror them onto the frozen copy.</summary>
        private void CopyFrozenLooks()
        {
            _copyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < _frozenCopies.Count; i++)
            {
                if (_frozenSources[i] == null || _frozenCopies[i] == null) continue;
                _frozenSources[i].GetPropertyBlock(_copyBlock);
                _frozenCopies[i].SetPropertyBlock(_copyBlock);
                _frozenCopies[i].sharedMaterials = _frozenSources[i].sharedMaterials;
            }
        }

        private void ClearFrozenPose()
        {
            RestoreRealBody();
            if (_frozenRoot != null) Destroy(_frozenRoot);
            foreach (Mesh mesh in _frozenMeshes) if (mesh != null) Destroy(mesh);
            _frozenRoot = null;
            _frozenSources.Clear();
            _frozenCopies.Clear();
            _frozenMeshes.Clear();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // The portrait's own front light shines only while the preview camera renders.
            bool preview = camera == _previewCamera;
            if (_previewLight != null && _previewLight.enabled != preview) _previewLight.enabled = preview;
            if (!preview || _frozenCopies.Count == 0) return;
            _previewRendering = true;
            for (int i = 0; i < _frozenCopies.Count; i++)
            {
                if (_frozenSources[i] != null) _frozenSources[i].forceRenderingOff = true;
                if (_frozenCopies[i] != null) _frozenCopies[i].forceRenderingOff = false;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _previewCamera) return;
            RestoreRealBody();
            if (_previewLight != null) _previewLight.enabled = false;
        }

        private void RestoreRealBody()
        {
            if (!_previewRendering) return;
            _previewRendering = false;
            for (int i = 0; i < _frozenCopies.Count; i++)
            {
                if (_frozenSources[i] != null) _frozenSources[i].forceRenderingOff = false;
                if (_frozenCopies[i] != null) _frozenCopies[i].forceRenderingOff = true;
            }
        }

        private void BuildPreviewCamera()
        {
            _previewTexture = new RenderTexture(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "CustomizationPreview", antiAliasing = 4
            };
            var go = new GameObject("CustomizationPreviewCamera");
            DontDestroyOnLoad(go);
            go.layer = PreviewVolumeLayer;
            _previewCamera = go.AddComponent<Camera>();
            _previewCamera.targetTexture = _previewTexture;
            _previewCamera.fieldOfView = PreviewFov;
            _previewCamera.nearClipPlane = 0.05f;
            _previewCamera.farClipPlane = 200f;
            _previewCamera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << PreviewVolumeLayer;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            _previewProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var dof = _previewProfile.Add<DepthOfField>(true);
            // Bokeh focused on the face: Gaussian DoF was too weak against the near lobby walls.
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(PreviewDistance);
            dof.aperture.Override(2f);
            dof.focalLength.Override(90f);
            // The lobby is lit from above/behind, so the face read dark (George 2026-11-07): lift exposure a little
            // and add a soft front light from just above the camera.
            var color = _previewProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.45f);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = _previewProfile;

            var lightGo = new GameObject("PortraitLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0.35f, 0.45f, 0.2f);
            _previewLight = lightGo.AddComponent<Light>();
            _previewLight.type = LightType.Point;
            _previewLight.range = PreviewDistance + 2f;
            _previewLight.intensity = 2.2f;
            _previewLight.color = new Color(1f, 0.97f, 0.92f);
            _previewLight.shadows = LightShadows.None;
            _previewLight.enabled = false;
        }

        /// <summary>Puts the preview camera in front of the face renderer (once, at the frozen pose).</summary>
        private void AimPreview()
        {
            if (_previewCamera == null) return;
            var player = PlayerAppearance.LocalInstance;
            Renderer face = player != null && player.TryGetComponent(out CharacterRig rig) ? rig.FaceRenderer : null;
            if (face == null) return;
            Vector3 center = face.bounds.center;
            Vector3 forward = Vector3.ProjectOnPlane(face.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = player.transform.forward;
            forward.Normalize();
            // Head and upper body with some room above for a hat (George 2026-11-08: the old shot was too close).
            Vector3 target = center - Vector3.up * 0.28f;
            _previewCamera.transform.position = center + forward * PreviewDistance + Vector3.up * 0.05f;
            _previewCamera.transform.rotation = Quaternion.LookRotation(target - _previewCamera.transform.position, Vector3.up);
        }

        private void LateUpdate()
        {
            // A new hat is built on the real head a frame later: copy the pose again so the portrait shows it.
            if (IsVisible && _refreezeFrames > 0 && !_freezePending && --_refreezeFrames == 0) FreezePose();
            if (!IsVisible || !_freezePending) return;
            var player = PlayerAppearance.LocalInstance;
            Renderer face = player != null && player.TryGetComponent(out CharacterRig rig) ? rig.FaceRenderer : null;
            if (face == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            Vector3 center = face.bounds.center;
            float speed = _lastFaceCenter.HasValue ? (center - _lastFaceCenter.Value).magnitude / dt : float.MaxValue;
            _lastFaceCenter = center;
            _stillTime = speed < SettleSpeed ? _stillTime + dt : 0f;
            _pendingTime += dt;
            AimPreview(); // live view until the pose is frozen
            if (_stillTime < SettleTime && _pendingTime < SettleTimeout) return;
            _freezePending = false;
            FreezePose();
            AimPreview();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            Close(false);
            if (_launchButton != null) _launchButton.gameObject.SetActive(false);
        }

        private void OnSceneChanged(Scene previous, Scene next)
        {
            if (next.name != PlayerAppearance.LobbySceneName)
            {
                Close(true);
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            bool available = CanCustomize && !PauseMenu.IsPaused;
            if (_launchButton != null && _launchButton.gameObject.activeSelf != available)
                _launchButton.gameObject.SetActive(available);
            if (!available)
            {
                Close(false);
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            // Do not steal typing from room-code input fields.
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() != null) return;
            if (keyboard.cKey.wasPressedThisFrame) { if (IsVisible) Cancel(); else Open(); }
            else if (IsVisible && keyboard.escapeKey.wasPressedThisFrame) Cancel();
        }

        public void Open()
        {
            if (!CanCustomize || PauseMenu.IsPaused || IsVisible) return;
            _draft = PlayerAppearance.LocalInstance != null ? PlayerAppearance.LocalInstance.CurrentFace : PlayerAppearance.SavedFace;
            _draftColor = PlayerAppearance.LocalInstance != null ? PlayerAppearance.LocalInstance.CurrentColor : PlayerAppearance.SavedColor;
            if (_nicknameInput != null) _nicknameInput.SetTextWithoutNotify(PlayerExpeditionState.SavedNickname);
            var localState = LocalState();
            _savedHat = _draftHat = localState != null ? localState.Hat.Value : 0;
            _restoreCursorLock = Cursor.lockState == CursorLockMode.Locked;
            IsVisible = true;
            _panel.SetActive(true);
            SetPreviewActive(true);
            PlayerCameraController.LocalInstance?.SetCursorLock(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        private void Cancel()
        {
            if (PlayerAppearance.LocalInstance != null)
            {
                PlayerAppearance.LocalInstance.ApplyFace(PlayerAppearance.LocalInstance.CurrentFace);
                PlayerAppearance.LocalInstance.ApplyColor(PlayerAppearance.LocalInstance.CurrentColor);
            }
            var state = LocalState();
            if (state != null && state.Hat.Value != _savedHat) Cosmetics.Equip(_savedHat, state.HatColor.Value);
            Close(true);
        }

        public void Close(bool restoreCursor)
        {
            if (!IsVisible) return;
            IsVisible = false;
            _closedFrame = Time.frameCount;
            if (_panel != null) _panel.SetActive(false);
            SetPreviewActive(false);
            if (restoreCursor && _restoreCursorLock && !PauseMenu.IsPaused)
            {
                PlayerCameraController.LocalInstance?.SetCursorLock(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static PlayerExpeditionState LocalState()
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.IsListening && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            return player != null ? player.GetComponent<PlayerExpeditionState>() : null;
        }

        private void PickHat(int hat)
        {
            var state = LocalState();
            if (state == null || !Cosmetics.Owns(hat)) return;
            _draftHat = hat;
            // Worn right away so the portrait shows it; Cancel puts the old hat back.
            if (state.Hat.Value != hat) { Cosmetics.Equip(hat, state.HatColor.Value); _refreezeFrames = 3; }
            Refresh();
        }

        private void ChangeStyle(int direction)
        {
            int count = PlayerAppearance.PickableFaces.Length;
            int slot = Mathf.Max(0, System.Array.IndexOf(PlayerAppearance.PickableFaces, _draft));
            _draft = PlayerAppearance.PickableFaces[(slot + direction + count) % count];
            Refresh();
        }

        private void Refresh()
        {
            int style = _draft;
            Color bodyColor = PlayerAppearance.GetPastelColor(_draftColor);
            int styleSlot = System.Array.IndexOf(PlayerAppearance.PickableFaces, style);
            _styleLabel.text = $"ใบหน้า {styleSlot + 1}/{PlayerAppearance.PickableFaces.Length}";
            // Live 3D portrait when the preview camera runs; the flat face texture is the fallback.
            if (_previewTexture == null)
                _preview.texture = _faceTextures != null && style < _faceTextures.Length ? _faceTextures[style] : null;
            _preview.color = Color.white;
            for (int i = 0; i < _faceOutlines.Length; i++)
            {
                _faceOutlines[i].enabled = PlayerAppearance.PickableFaces[i] == style;
                if (_faceBgImages != null && i < _faceBgImages.Length && _faceBgImages[i] != null)
                    _faceBgImages[i].color = bodyColor;
            }
            if (_colorRings != null)
            {
                for (int i = 0; i < _colorRings.Length; i++)
                    _colorRings[i].enabled = i == _draftColor;
            }
            if (_hatButtons != null)
            {
                for (int i = 0; i < _hatButtons.Length; i++)
                {
                    bool owned = Cosmetics.Owns(i);
                    _hatRings[i].enabled = i == _draftHat;
                    _hatButtons[i].interactable = owned;
                    _hatLabels[i].text = owned ? Cosmetics.HatNames[i] : $"{Cosmetics.HatNames[i]}\n{Cosmetics.HatPrices[i]} เหรียญ";
                }
            }
            if (PlayerAppearance.LocalInstance != null)
            {
                PlayerAppearance.LocalInstance.ApplyFace(_draft);
                PlayerAppearance.LocalInstance.ApplyColor(_draftColor);
                CopyFrozenLooks();
            }
            string colorName = PlayerAppearance.GetPastelColorName(_draftColor);
            _status.text = $"สีพาสเทล: {colorName} ({_draftColor + 1:00}/{PlayerAppearance.ColorCount:00})   •   เปลี่ยนได้เฉพาะในล็อบบี้";
        }

        private void Save()
        {
            if (!CanCustomize || PauseMenu.IsPaused) { Close(false); return; }
            if (PlayerAppearance.SaveLocal(_draft, _draftColor))
            {
                if (_nicknameInput != null) PlayerExpeditionState.SaveNickname(_nicknameInput.text);
                Close(true);
            }
        }

        private void Build()
        {
            _font = UITheme.Font;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _launchButton = BuildBottomLeftCustomizeButton(transform, Open);
            _launchButton.gameObject.SetActive(false);

            _panel = Box(transform, "FaceCustomizationOverlay", Vector2.zero, Vector2.zero, UITheme.Overlay);
            var overlayRect = (RectTransform)_panel.transform;
            overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;

            // Cute kit (Canva page 70): wide beige card, plank title, preview left, options right.
            // Order top to bottom (George 2026-11-08): nickname > character colour > face > hat.
            var content = Box(_panel.transform, "FaceCustomization", new Vector2(1300, 800), Vector2.zero, Color.white);
            UITheme.StyleCuteCard(content.GetComponent<Image>(), true);
            UITheme.MakeCutePlank(content.transform, "แต่งตัวละคร", 400f, 96f, 40f, 32);
            UITheme.AddCuteTape(content.transform, 6, new Vector2(.5f, .5f), new Vector2(-560, 318), 35f, 96f);
            UITheme.AddCuteTape(content.transform, 5, new Vector2(.5f, .5f), new Vector2(560, 318), -30f, 96f);

            var previewBox = Box(content.transform, "Preview", new Vector2(360, 551), new Vector2(-400, -8), Color.white);
            _previewBoxImage = previewBox.GetComponent<Image>();
            UITheme.StyleShape(_previewBoxImage, UITheme.SmallSprite, Color.white);
            var previewGO = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(previewBox.transform, false);
            var rect = (RectTransform)previewGO.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8, 8); rect.offsetMax = new Vector2(-8, -8);
            _preview = previewGO.GetComponent<RawImage>(); _preview.raycastTarget = false;

            const float column = 170f;
            const float left = column - 290f; // left edge of the option column (section titles line up here)

            // 1) Nickname: shown in the top-left player list and to every player (PlayerExpeditionState.DisplayName).
            var nickLabel = Label(content.transform, "ชื่อเล่น", 24, new Vector2(120, 50), new Vector2(left + 60, 262));
            nickLabel.alignment = TextAnchor.MiddleLeft;
            UITheme.StyleCuteText(nickLabel);
            UITheme.SetWeight(nickLabel, UITheme.FontMedium);
            var (nickGO, nickInput) = ProceduralUIUtility.MakeInputField(content, "NicknameInput", "พิมพ์ชื่อเล่น (ไม่เกิน 16 ตัว)", 54);
            var nickRect = (RectTransform)nickGO.transform;
            nickRect.sizeDelta = new Vector2(440, 54);
            nickRect.anchoredPosition = new Vector2(column + 70, 262);
            nickInput.characterLimit = PlayerExpeditionState.NicknameMaxChars;
            nickInput.lineType = InputField.LineType.SingleLine;
            // Match the cute card: white rounded box, dark ink text.
            UITheme.StyleShape(nickGO.GetComponent<Image>(), UITheme.SmallSprite, Color.white);
            nickInput.textComponent.color = UITheme.CuteInk;
            nickInput.textComponent.fontSize = 24;
            if (nickInput.placeholder is Text ph) { ph.color = UITheme.CuteInkSoft; ph.fontSize = 20; ph.fontStyle = FontStyle.Normal; }
            var nickTint = nickInput.colors; nickTint.normalColor = nickTint.selectedColor = Color.white; nickTint.highlightedColor = UITheme.Hover(Color.white); nickTint.pressedColor = UITheme.Pressed(Color.white); nickInput.colors = nickTint;
            nickInput.selectionColor = new Color(UITheme.Primary.r, UITheme.Primary.g, UITheme.Primary.b, .35f);
            _nicknameInput = nickInput;

            // 2) Character colour.
            var colorTitle = Label(content.transform, "สีตัวละคร", 22, new Vector2(580, 34), new Vector2(column, 200));
            colorTitle.alignment = TextAnchor.MiddleLeft;
            UITheme.StyleCuteText(colorTitle);
            UITheme.SetWeight(colorTitle, UITheme.FontMedium);
            _colorRings = new Image[PlayerAppearance.ColorCount];
            Sprite round = ProceduralUIUtility.GetRoundedSprite(30, 0);
            float colorCenterIndex = (PlayerAppearance.ColorCount - 1) * 0.5f;
            for (int i = 0; i < PlayerAppearance.ColorCount; i++)
            {
                int colorIndex = i;
                Vector2 at = new Vector2(column + (i - colorCenterIndex) * 84f, 146);
                var ring = Box(content.transform, "SelectedRing", new Vector2(76, 76), at, UITheme.Primary);
                UITheme.StyleShape(ring.GetComponent<Image>(), round, UITheme.Primary);
                ring.GetComponent<Image>().raycastTarget = false;
                _colorRings[i] = ring.GetComponent<Image>();
                var colorBtn = Button(content.transform, "", new Vector2(62, 62), at, () =>
                {
                    _draftColor = colorIndex;
                    Refresh();
                });
                Color pastel = PlayerAppearance.GetPastelColor(i);
                UITheme.StyleShape(colorBtn.GetComponent<Image>(), round, pastel);
                var tint = colorBtn.colors;
                tint.normalColor = tint.selectedColor = Color.white;
                tint.highlightedColor = UITheme.Hover(Color.white);
                tint.pressedColor = UITheme.Pressed(Color.white);
                colorBtn.colors = tint;
            }

            // 3) Face: header "ใบหน้า n/10" with arrows, then 10 faces in two rows of 5 (knockdown faces left out).
            _styleLabel = Label(content.transform, "", 24, new Vector2(340, 44), new Vector2(column, 80));
            UITheme.StyleCuteText(_styleLabel);
            UITheme.SetWeight(_styleLabel, UITheme.FontMedium);
            UITheme.StyleButton(Button(content.transform, "<", new Vector2(70, 54), new Vector2(column - 230, 80), () => ChangeStyle(-1)), UIButtonStyle.WhiteShort);
            UITheme.StyleButton(Button(content.transform, ">", new Vector2(70, 54), new Vector2(column + 230, 80), () => ChangeStyle(1)), UIButtonStyle.WhiteShort);
            _faceOutlines = new Outline[PlayerAppearance.PickableFaces.Length];
            _faceBgImages = new Image[PlayerAppearance.PickableFaces.Length];
            for (int i = 0; i < _faceOutlines.Length; i++)
            {
                int index = PlayerAppearance.PickableFaces[i];
                var button = Button(content.transform, "", new Vector2(92, 78), new Vector2(column + (i % 5 - 2f) * 112, 4 - i / 5 * 88), () =>
                {
                    _draft = index;
                    Refresh();
                });
                var bgImage = button.GetComponent<Image>();
                UITheme.StyleShape(bgImage, UITheme.SmallSprite, UITheme.FacePreview);
                _faceBgImages[i] = bgImage;
                var tint = button.colors; tint.normalColor = tint.selectedColor = Color.white; tint.highlightedColor = UITheme.TextSecondary; tint.pressedColor = UITheme.Pressed(Color.white); button.colors = tint;
                var thumbnail = new GameObject("FaceThumbnail", typeof(RectTransform), typeof(RawImage));
                thumbnail.transform.SetParent(button.transform, false);
                var thumbnailRect = (RectTransform)thumbnail.transform;
                thumbnailRect.anchorMin = Vector2.zero; thumbnailRect.anchorMax = Vector2.one;
                thumbnailRect.offsetMin = thumbnailRect.offsetMax = Vector2.zero;
                var thumbnailImage = thumbnail.GetComponent<RawImage>();
                thumbnailImage.texture = _faceTextures != null && index < _faceTextures.Length ? _faceTextures[index] : null;
                thumbnailImage.raycastTarget = false;
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = UITheme.Primary; outline.effectDistance = new Vector2(4, -4);
                _faceOutlines[i] = outline;
            }

            // 4) Hat: hats bought in the shop (K) can be worn here; ones not bought yet show their price.
            var hatTitle = Label(content.transform, "หมวก (ซื้อเพิ่มที่ร้าน K)", 22, new Vector2(580, 34), new Vector2(column, -146));
            hatTitle.alignment = TextAnchor.MiddleLeft;
            UITheme.StyleCuteText(hatTitle);
            UITheme.SetWeight(hatTitle, UITheme.FontMedium);
            int hatCount = Cosmetics.HatNames.Length;
            _hatButtons = new Button[hatCount];
            _hatRings = new Image[hatCount];
            _hatLabels = new Text[hatCount];
            for (int i = 0; i < hatCount; i++)
            {
                int hat = i;
                Vector2 at = new Vector2(column + (i - (hatCount - 1) * .5f) * 112, -204);
                var ring = Box(content.transform, "HatRing", new Vector2(104, 66), at, UITheme.Primary);
                UITheme.StyleShape(ring.GetComponent<Image>(), UITheme.SmallSprite, UITheme.Primary);
                ring.GetComponent<Image>().raycastTarget = false;
                _hatRings[i] = ring.GetComponent<Image>();
                var hatBtn = Button(content.transform, "", new Vector2(94, 56), at, () => PickHat(hat));
                UITheme.StyleShape(hatBtn.GetComponent<Image>(), UITheme.SmallSprite, Color.white);
                var tint = hatBtn.colors; tint.normalColor = tint.selectedColor = Color.white; tint.highlightedColor = UITheme.Hover(Color.white); tint.pressedColor = UITheme.Pressed(Color.white); tint.disabledColor = new Color(1f, 1f, 1f, .45f); hatBtn.colors = tint;
                var hatLabel = Label(hatBtn.transform, Cosmetics.HatNames[i], 16, new Vector2(94, 56), Vector2.zero);
                UITheme.StyleCuteText(hatLabel);
                hatLabel.lineSpacing = .85f;
                _hatButtons[i] = hatBtn;
                _hatLabels[i] = hatLabel;
            }

            // Colour name line from the old panel; the kit layout has no room for it.
            _status = Label(content.transform, "", 15, new Vector2(610, 32), new Vector2(column, -240));
            _status.gameObject.SetActive(false);
            UITheme.StyleButton(Button(content.transform, "ยกเลิก", new Vector2(270, 70), new Vector2(column - 145, -312), Cancel), UIButtonStyle.White);
            UITheme.StyleButton(Button(content.transform, "บันทึก", new Vector2(270, 70), new Vector2(column + 145, -312), Save), UIButtonStyle.Pink);
            _panel.SetActive(false);
        }

        private Button BuildBottomLeftCustomizeButton(Transform parent, UnityEngine.Events.UnityAction action)
        {
            Vector2 size = new Vector2(290, 80);
            var go = Box(parent, "CustomizeOutfitButton", size, Vector2.zero, Color.white);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(24, 24);

            var button = go.AddComponent<Button>();
            var bgImage = go.GetComponent<Image>();
            button.targetGraphic = bgImage;
            button.onClick.AddListener(action);
            go.AddComponent<UIButtonHover>();

            UITheme.StyleShape(bgImage, UITheme.ButtonSprite(UIButtonStyle.Pink), Color.white);

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = UITheme.Hover(Color.white);
            colors.selectedColor = Color.white;
            colors.pressedColor = UITheme.Pressed(Color.white);
            colors.disabledColor = UITheme.Disabled(Color.white);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var iconGo = new GameObject("HangerIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.sizeDelta = new Vector2(48, 40);
            iconRect.anchoredPosition = new Vector2(-82, 5);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = GetHangerSprite();
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;

            var label = Label(go.transform, "เปลี่ยนลุค [C]", 26, new Vector2(196, 48), new Vector2(26, 5));
            label.font = UITheme.FontMedium;
            label.color = Color.white;
            ThaiTextMeshProBridge.Attach(label);

            return button;
        }

        private static Sprite GetHangerSprite()
        {
            if (_hangerSprite != null) return _hangerSprite;
            const int w = 64, h = 56;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var clear = new Color32[w * h];
            tex.SetPixels32(clear);

            void PlotDisk(float cx, float cy, float r)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r + 1));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1));
                int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r + 1));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(r + 0.5f - d);
                    if (a <= 0f) continue;
                    Color c = tex.GetPixel(x, y);
                    if (a > c.a) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            void DrawLine(Vector2 a, Vector2 b, float radius)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2.5f);
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                    PlotDisk(p.x, p.y, radius);
                }
            }

            // Hanger hook arc at top center
            Vector2 hookCenter = new Vector2(32f, 43f);
            float hookR = 6.5f;
            for (int deg = -45; deg <= 215; deg += 4)
            {
                float rad = deg * Mathf.Deg2Rad;
                PlotDisk(hookCenter.x + Mathf.Cos(rad) * hookR, hookCenter.y + Mathf.Sin(rad) * hookR, 2.2f);
            }
            // Neck down to shoulder junction
            DrawLine(new Vector2(32f, 36.5f), new Vector2(32f, 31f), 2.2f);
            // Left and right shoulders
            DrawLine(new Vector2(32f, 31f), new Vector2(8f, 14f), 2.3f);
            DrawLine(new Vector2(32f, 31f), new Vector2(56f, 14f), 2.3f);
            // Rounded corners & bottom bar
            DrawLine(new Vector2(8f, 14f), new Vector2(10f, 10f), 2.3f);
            DrawLine(new Vector2(56f, 14f), new Vector2(54f, 10f), 2.3f);
            DrawLine(new Vector2(10f, 10f), new Vector2(54f, 10f), 2.3f);

            tex.Apply();
            _hangerSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _hangerSprite;
        }

        private GameObject Box(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private Text Label(Transform parent, string text, int fontSize, Vector2 size, Vector2 position)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            var label = go.GetComponent<Text>(); label.font = _font; label.fontSize = fontSize;
            label.text = text; label.color = UITheme.TextPrimary; label.verticalOverflow = VerticalWrapMode.Overflow; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            ThaiTextMeshProBridge.Attach(label);
            return label;
        }

        private Button Button(Transform parent, string text, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = Box(parent, text, size, position, Color.white);
            var button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            Label(go.transform, text, text.Length == 1 ? 30 : 26, size, Vector2.zero);
            UITheme.StyleButton(button, UIButtonStyle.Secondary);
            return button;
        }
    }
}
