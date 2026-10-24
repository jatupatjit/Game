using UnityEngine;
using UnityEngine.UI;
using CoopGame.Network;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────────────────
// Spinner Animator (Runtime Component)
// Place outside #if UNITY_EDITOR so standalone builds will compile cleanly!
// ─────────────────────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────────────────────
// UI Button Hover & Press Animator (Runtime Component)
// Smooth scale transitions on hover and click for modern tactile feel
// ─────────────────────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────────────────────
// Procedural UI Utility (Shared Runtime & Editor)
// Provides 9-sliced rounded anti-aliased sprites, color palettes, and UI helpers
// ─────────────────────────────────────────────────────────────────────────────
// Colours, font and shapes come only from UITheme (the user's pink UI kit). Old per-screen palette
// constants were removed so no screen can drift from the theme.
public static class ProceduralUIUtility
{
    private static Sprite _sharedSpinnerRingSprite;

    private static readonly System.Collections.Generic.Dictionary<int, Sprite> _roundedSprites =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// 9-sliced rounded rectangle (corner radius <paramref name="cut"/> px) with an optional
    /// <paramref name="outline"/> px rim baked at <paramref name="rimShade"/> grey, so tinting
    /// the Image gives a flat fill with a darker rim of the same hue. Cached per parameter set.
    /// </summary>
    public static Sprite GetRoundedSprite(int cut = 14, int outline = 2, float rimShade = 0.62f)
    {
        cut = Mathf.Clamp(cut, 1, 60);
        outline = Mathf.Clamp(outline, 0, 8);
        int key = (cut << 16) | (outline << 8) | Mathf.Clamp(Mathf.RoundToInt(rimShade * 255f), 0, 255);
        if (_roundedSprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int border = cut + outline + 2;
        int size = border * 2 + 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"T_Procedural_Rounded_{cut}_{outline}"
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Min(x + 0.5f, size - (x + 0.5f));
                float dy = Mathf.Min(y + 0.5f, size - (y + 0.5f));
                // Signed distance to the outer boundary (straight edges or the circular corner).
                float cx = Mathf.Max(0f, cut - dx), cy = Mathf.Max(0f, cut - dy);
                float edge = (cx > 0f && cy > 0f) ? cut - Mathf.Sqrt(cx * cx + cy * cy) : Mathf.Min(dx, dy);
                float alpha = Mathf.Clamp01(edge + 0.5f);
                float shade = outline > 0 ? Mathf.Lerp(rimShade, 1f, Mathf.Clamp01(edge - outline + 0.5f)) : 1f;
                pixels[y * size + x] = new Color(shade, shade, shade, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = tex.name;
        _roundedSprites[key] = sprite;
        return sprite;
    }

    public static Sprite GetSpinnerRingSprite(int size = 128, int thickness = 14)
    {
        if (_sharedSpinnerRingSprite != null) return _sharedSpinnerRingSprite;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "T_Procedural_SpinnerRing"
        };

        float center = size * 0.5f;
        float outerRadius = center - 2f;
        float innerRadius = outerRadius - thickness;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - center;
                float dy = (y + 0.5f) - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Anti-aliased ring mask
                float ringAlpha = Mathf.Clamp01((dist - (innerRadius - 1.5f)) / 1.5f) *
                                  Mathf.Clamp01(((outerRadius + 1.5f) - dist) / 1.5f);

                if (ringAlpha <= 0f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                // Angle from 0 to 360 degrees (0 = right, 90 = top)
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;

                // Comet arc: 0 to 280 degrees, gap at 280 to 360
                float arcLength = 280f;
                float arcAlpha = 0f;

                if (angle <= arcLength)
                {
                    float fraction = angle / arcLength;
                    float tail = Mathf.Pow(1f - fraction, 1.2f);
                    float head = Mathf.Clamp01(angle / 18f);
                    arcAlpha = head * tail;
                }

                float finalAlpha = ringAlpha * arcAlpha;
                pixels[y * size + x] = new Color(1f, 1f, 1f, finalAlpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        _sharedSpinnerRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _sharedSpinnerRingSprite;
    }

    public static Font GetDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    public static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    public static GameObject MakeEmpty(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.zero;
        return go;
    }

    public static Image MakeImage(GameObject parent, string name, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var img = go.AddComponent<Image>();
        img.color = color;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Simple;
        }
        return img;
    }

    /// <summary>Soil-dark chamfered panel (UITheme.StylePanel).</summary>
    public static GameObject MakePanel(GameObject parent, string name, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        UITheme.StylePanel(go.AddComponent<Image>());
        return go;
    }

    public static Text MakeText(GameObject parent, string name, string text, int size, FontStyle style, UITextRole role, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 30);
        var t = go.AddComponent<Text>();
        UITheme.StyleText(t, role);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    /// <summary>Layout-friendly themed button (LayoutElement height) styled by UITheme.StyleButton.</summary>
    public static (GameObject, Button) MakeButton(GameObject parent, string name, string label, UIButtonStyle style, float height, int fontSize = 15)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        go.AddComponent<UIButtonHover>();

        var lbl = MakeText(go, "Label", label, fontSize, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        StretchFull(lbl.GetComponent<RectTransform>());
        UITheme.StyleButton(btn, style);

        return (go, btn);
    }

    public static (GameObject, InputField) MakeInputField(GameObject parent, string name, string placeholder, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
#endif
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);

        var bgImg = go.AddComponent<Image>();
        var inputField = go.AddComponent<InputField>();
        inputField.caretWidth = 2;
        inputField.characterLimit = 8;

        var textObj = MakeText(go, "Text", "", 20, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        StretchFull(textObj.GetComponent<RectTransform>());
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.offsetMin = new Vector2(12, 0);
        textRT.offsetMax = new Vector2(-12, 0);

        var phObj = MakeText(go, "Placeholder", placeholder, 14, FontStyle.Italic, UITextRole.Secondary, TextAnchor.MiddleCenter);
        StretchFull(phObj.GetComponent<RectTransform>());
        var phRT = phObj.GetComponent<RectTransform>();
        phRT.offsetMin = new Vector2(12, 0);
        phRT.offsetMax = new Vector2(-12, 0);

        inputField.textComponent = textObj;
        inputField.placeholder = phObj;
        inputField.targetGraphic = bgImg;
        UITheme.StyleInputField(inputField);

        return (go, inputField);
    }
}

#if UNITY_EDITOR
// ─────────────────────────────────────────────────────────────────────────────
// UIBuilders.cs  |  Editor Only
// Procedural builders for:
// 1. LobbyUI (Peak-style lobby with Host Room, Settings, Quit, and Sub-menus)
// 2. PauseMenu (In-game ESC menu with Continue, Settings, Quit)
// 3. RoomCodeHUD (Top-left corner room code overlay)
// Uses 100% standard UnityEngine.UI (Text, InputField, Button). Zero TMPro dependencies.
// ─────────────────────────────────────────────────────────────────────────────

public static class LobbyUIBuilder
{
    public static void Build(LobbyUI target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Lobby UI");

        // 1. Fullscreen Canvas
        Canvas canvas = target.GetComponent<Canvas>();
        if (canvas == null) canvas = target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup lobbyRoot = target.GetComponent<CanvasGroup>();
        if (lobbyRoot == null) lobbyRoot = target.gameObject.AddComponent<CanvasGroup>();

        // Clear existing generated children
        for (int i = target.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(target.transform.GetChild(i).gameObject, true);
        }

        // 2. Background
        var bg = ProceduralUIUtility.MakeImage(target.gameObject, "Background", UITheme.Backdrop);
        ProceduralUIUtility.StretchFull(bg.GetComponent<RectTransform>());

        // 3. Center Glass Card (460 x 520)
        var cardGO = ProceduralUIUtility.MakePanel(target.gameObject, "LobbyCard", new Vector2(460, 520), Vector2.zero);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;

        var cardLayout = cardGO.AddComponent<VerticalLayoutGroup>();
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.padding = new RectOffset(36, 36, 32, 32);
        cardLayout.spacing = 14;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        // 4. Header: Game Title & Subtitle
        var titleText = ProceduralUIUtility.MakeText(cardGO, "TitleLabel", "DON'T DROP IT", 32, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        var titleLE = titleText.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 40;

        var subTitleText = ProceduralUIUtility.MakeText(cardGO, "SubTitleLabel", "", 11, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleCenter);
        var subLE = subTitleText.gameObject.AddComponent<LayoutElement>();
        subLE.preferredHeight = 0;

        // Status Message Label (errors / warnings only, hidden when empty)
        var statusMsgText = ProceduralUIUtility.MakeText(cardGO, "StatusMessageLabel", "", 12, FontStyle.Italic, UITextRole.Danger, TextAnchor.MiddleCenter);
        var statusLE = statusMsgText.gameObject.AddComponent<LayoutElement>();
        statusLE.preferredHeight = 18;

        // ── 5. MAIN MENU PANEL (PLAY / SETTINGS / QUIT) ───────────────────────
        var mainPanel = ProceduralUIUtility.MakeEmpty(cardGO, "MainMenuPanel");
        var mainLE = mainPanel.AddComponent<LayoutElement>();
        mainLE.preferredHeight = 360;
        mainLE.flexibleHeight = 1f;

        var mainVL = mainPanel.AddComponent<VerticalLayoutGroup>();
        mainVL.padding = new RectOffset(0, 0, 0, 0);
        mainVL.spacing = 22;
        mainVL.childControlWidth = true;
        mainVL.childControlHeight = true;
        mainVL.childForceExpandWidth = true;
        mainVL.childForceExpandHeight = false;
        mainVL.childAlignment = TextAnchor.MiddleCenter;

        var (_, hostRoomBtn) = ProceduralUIUtility.MakeButton(mainPanel, "HostRoomMenuButton", "เล่นเกม", UIButtonStyle.Primary, 58, 16);
        var (_, settingsBtn) = ProceduralUIUtility.MakeButton(mainPanel, "SettingsMenuButton", "ตั้งค่า", UIButtonStyle.Secondary, 52, 15);

        // Controlled breathing spacer before quit button
        var mainSpacer = ProceduralUIUtility.MakeEmpty(mainPanel, "MainSpacer");
        var mainSpacerLE = mainSpacer.AddComponent<LayoutElement>();
        mainSpacerLE.preferredHeight = 32;
        mainSpacerLE.flexibleHeight = 0;

        var (_, quitBtn)     = ProceduralUIUtility.MakeButton(mainPanel, "QuitGameButton", "ออกจากเกม", UIButtonStyle.Secondary, 48, 14);
        var quitLE = quitBtn.GetComponent<LayoutElement>();
        if (quitLE != null) { quitLE.preferredHeight = 48; quitLE.flexibleHeight = 0; }

        // ── 6. PLAY / HOST ROOM SUB-PANEL (Host / Join / Back) ────────────────
        var hostRoomPanel = ProceduralUIUtility.MakeEmpty(cardGO, "HostRoomSubPanel");
        var hostRoomLE = hostRoomPanel.AddComponent<LayoutElement>();
        hostRoomLE.preferredHeight = 360;
        hostRoomLE.flexibleHeight = 1f;

        var hostRoomVL = hostRoomPanel.AddComponent<VerticalLayoutGroup>();
        hostRoomVL.padding = new RectOffset(0, 0, 0, 0);
        hostRoomVL.spacing = 18;
        hostRoomVL.childControlWidth = true;
        hostRoomVL.childControlHeight = true;
        hostRoomVL.childForceExpandWidth = true;
        hostRoomVL.childForceExpandHeight = false;
        hostRoomVL.childAlignment = TextAnchor.MiddleCenter;

        var subTitle1 = ProceduralUIUtility.MakeText(hostRoomPanel, "SubTitle", "เลือกโหมดการเล่น", 13, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleCenter);
        var subTitle1LE = subTitle1.gameObject.AddComponent<LayoutElement>();
        subTitle1LE.preferredHeight = 26;
        subTitle1LE.flexibleHeight = 0;

        var (_, hostGameBtn) = ProceduralUIUtility.MakeButton(hostRoomPanel, "HostButton", "สร้างห้อง", UIButtonStyle.Primary, 58, 16);
        var (_, joinGameBtn) = ProceduralUIUtility.MakeButton(hostRoomPanel, "JoinButton", "เข้าห้องด้วยรหัส", UIButtonStyle.Secondary, 54, 15);

        // Controlled breathing spacer before Back button
        var hostSpacer = ProceduralUIUtility.MakeEmpty(hostRoomPanel, "HostSpacer");
        var hostSpacerLE = hostSpacer.AddComponent<LayoutElement>();
        hostSpacerLE.preferredHeight = 28;
        hostSpacerLE.flexibleHeight = 0;

        var (_, backFromHostBtn) = ProceduralUIUtility.MakeButton(hostRoomPanel, "BackFromHostRoomButton", "ย้อนกลับ", UIButtonStyle.Secondary, 48, 14);
        var backHostLE = backFromHostBtn.GetComponent<LayoutElement>();
        if (backHostLE != null) { backHostLE.preferredHeight = 48; backHostLE.flexibleHeight = 0; }
        hostRoomPanel.SetActive(false);

        // ── 7. JOIN ROOM SUB-PANEL (Input + Paste Row / Join Button / Back) ────
        var joinPanel = ProceduralUIUtility.MakeEmpty(cardGO, "JoinSubPanel");
        var joinPanelLE = joinPanel.AddComponent<LayoutElement>();
        joinPanelLE.preferredHeight = 360;
        joinPanelLE.flexibleHeight = 1f;

        var joinVL = joinPanel.AddComponent<VerticalLayoutGroup>();
        joinVL.padding = new RectOffset(0, 0, 0, 0);
        joinVL.spacing = 18;
        joinVL.childControlWidth = true;
        joinVL.childControlHeight = true;
        joinVL.childForceExpandWidth = true;
        joinVL.childForceExpandHeight = false;
        joinVL.childAlignment = TextAnchor.MiddleCenter;

        var subTitle2 = ProceduralUIUtility.MakeText(joinPanel, "JoinTitle", "กรอกรหัสห้อง 6 หลัก", 13, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleCenter);
        var subTitle2LE = subTitle2.gameObject.AddComponent<LayoutElement>();
        subTitle2LE.preferredHeight = 26;
        subTitle2LE.flexibleHeight = 0;

        // Input + Paste Row (Horizontal)
        var inputRow = ProceduralUIUtility.MakeEmpty(joinPanel, "InputRow");
        var inputRowLE = inputRow.AddComponent<LayoutElement>();
        inputRowLE.preferredHeight = 54;
        inputRowLE.flexibleHeight = 0;
        var inputRowHL = inputRow.AddComponent<HorizontalLayoutGroup>();
        inputRowHL.spacing = 8;
        inputRowHL.childControlWidth = false;
        inputRowHL.childControlHeight = true;
        inputRowHL.childForceExpandWidth = false;
        inputRowHL.childForceExpandHeight = true;

        var (inputGO, codeInput) = ProceduralUIUtility.MakeInputField(inputRow, "RoomCodeInput", "รหัสห้อง", 54);
        var inputRT = inputGO.GetComponent<RectTransform>();
        inputRT.sizeDelta = new Vector2(270, 54);
        var inputLE = inputGO.AddComponent<LayoutElement>();
        inputLE.preferredWidth = 270;
        inputLE.preferredHeight = 54;

        var (pasteGO, pasteBtn) = ProceduralUIUtility.MakeButton(inputRow, "PasteCodeButton", "วาง", UIButtonStyle.Secondary, 54, 13);
        var pasteRT = pasteGO.GetComponent<RectTransform>();
        pasteRT.sizeDelta = new Vector2(110, 54);
        var pasteLE = pasteGO.GetComponent<LayoutElement>();
        if (pasteLE != null) { pasteLE.preferredWidth = 110; pasteLE.preferredHeight = 54; }

        // Primary Join Button (Full width)
        var (_, confirmJoinBtn) = ProceduralUIUtility.MakeButton(joinPanel, "ConfirmJoinButton", "เข้าห้อง", UIButtonStyle.Primary, 54, 15);
        var confirmLE = confirmJoinBtn.GetComponent<LayoutElement>();
        if (confirmLE != null) { confirmLE.preferredHeight = 54; confirmLE.flexibleHeight = 0; }

        // Controlled breathing spacer before Back button
        var joinSpacer = ProceduralUIUtility.MakeEmpty(joinPanel, "JoinSpacer");
        var joinSpacerLE = joinSpacer.AddComponent<LayoutElement>();
        joinSpacerLE.preferredHeight = 28;
        joinSpacerLE.flexibleHeight = 0;

        // Back Button
        var (_, backFromJoinBtn)= ProceduralUIUtility.MakeButton(joinPanel, "BackFromJoinButton", "ย้อนกลับ", UIButtonStyle.Secondary, 48, 14);
        var backJoinLE = backFromJoinBtn.GetComponent<LayoutElement>();
        if (backJoinLE != null) { backJoinLE.preferredHeight = 48; backJoinLE.flexibleHeight = 0; }
        joinPanel.SetActive(false);

        // ── 8. SETTINGS SUB-PANEL ─────────────────────────────────────────────
        var settingsPanel = ProceduralUIUtility.MakeEmpty(cardGO, "SettingsSubPanel");
        var settingsLE = settingsPanel.AddComponent<LayoutElement>();
        settingsLE.preferredHeight = 360;
        settingsLE.flexibleHeight = 1f;

        var settingsVL = settingsPanel.AddComponent<VerticalLayoutGroup>();
        settingsVL.padding = new RectOffset(0, 0, 0, 0);
        settingsVL.spacing = 18;
        settingsVL.childControlWidth = true;
        settingsVL.childControlHeight = true;
        settingsVL.childForceExpandWidth = true;
        settingsVL.childForceExpandHeight = false;
        settingsVL.childAlignment = TextAnchor.MiddleCenter;

        var sTitle = ProceduralUIUtility.MakeText(settingsPanel, "SettingsTitle", "ตั้งค่า", 18, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleCenter);
        var sTitleLE = sTitle.gameObject.AddComponent<LayoutElement>();
        sTitleLE.preferredHeight = 28;
        sTitleLE.flexibleHeight = 0;

        var blankArea = ProceduralUIUtility.MakeEmpty(settingsPanel, "BlankSettingsArea");
        var blankLE = blankArea.AddComponent<LayoutElement>();
        blankLE.preferredHeight = 110;
        blankLE.flexibleHeight = 0;
        var blankText = ProceduralUIUtility.MakeText(blankArea, "BlankNote", "การตั้งค่าเสียงและการควบคุมกำลังพัฒนา", 13, FontStyle.Italic, UITextRole.Secondary, TextAnchor.MiddleCenter);
        ProceduralUIUtility.StretchFull(blankText.GetComponent<RectTransform>());

        // Controlled breathing spacer before Back button
        var settingsSpacer = ProceduralUIUtility.MakeEmpty(settingsPanel, "SettingsSpacer");
        var settingsSpacerLE = settingsSpacer.AddComponent<LayoutElement>();
        settingsSpacerLE.preferredHeight = 28;
        settingsSpacerLE.flexibleHeight = 0;

        var (_, backFromSettingsBtn) = ProceduralUIUtility.MakeButton(settingsPanel, "BackFromSettingsButton", "ย้อนกลับ", UIButtonStyle.Secondary, 48, 14);
        var backSettingsLE = backFromSettingsBtn.GetComponent<LayoutElement>();
        if (backSettingsLE != null) { backSettingsLE.preferredHeight = 48; backSettingsLE.flexibleHeight = 0; }
        settingsPanel.SetActive(false);

        // ── 9. CONNECTING PANEL ───────────────────────────────────────────────
        var connectingPanel = ProceduralUIUtility.MakeEmpty(cardGO, "ConnectingPanel");
        var connPanelLE = connectingPanel.AddComponent<LayoutElement>();
        connPanelLE.preferredHeight = 360;
        connPanelLE.flexibleHeight = 1f;

        var connVL = connectingPanel.AddComponent<VerticalLayoutGroup>();
        connVL.padding = new RectOffset(0, 0, 0, 0);
        connVL.spacing = 16;
        connVL.childControlWidth = true;
        connVL.childControlHeight = true;
        connVL.childForceExpandWidth = true;
        connVL.childForceExpandHeight = false;
        connVL.childAlignment = TextAnchor.MiddleCenter;

        // Centered Spinner container to prevent stretching
        var spinnerContainer = ProceduralUIUtility.MakeEmpty(connectingPanel, "SpinnerContainer");
        var scLE = spinnerContainer.AddComponent<LayoutElement>();
        scLE.preferredHeight = 76;
        scLE.minHeight = 76;
        scLE.flexibleHeight = 0;

        var spinnerGO = ProceduralUIUtility.MakeImage(spinnerContainer, "SpinnerRing", UITheme.Primary, ProceduralUIUtility.GetSpinnerRingSprite());
        var spinnerRT = spinnerGO.GetComponent<RectTransform>();
        spinnerRT.anchorMin = new Vector2(0.5f, 0.5f);
        spinnerRT.anchorMax = new Vector2(0.5f, 0.5f);
        spinnerRT.pivot = new Vector2(0.5f, 0.5f);
        spinnerRT.sizeDelta = new Vector2(58, 58);
        spinnerRT.anchoredPosition = Vector2.zero;
        spinnerGO.gameObject.AddComponent<SpinnerAnimator>();

        // Title
        var connTitle = ProceduralUIUtility.MakeText(connectingPanel, "ConnectingTitle", "กำลังเข้าเกม", 17, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        var connTitleLE = connTitle.gameObject.AddComponent<LayoutElement>();
        connTitleLE.preferredHeight = 28;
        connTitleLE.flexibleHeight = 0;

        // Dynamic status text
        var connectingText = ProceduralUIUtility.MakeText(connectingPanel, "ConnectingLabel", "กำลังสร้างห้อง", 15, FontStyle.Normal, UITextRole.Accent, TextAnchor.MiddleCenter);
        var connTextLE = connectingText.gameObject.AddComponent<LayoutElement>();
        connTextLE.preferredHeight = 24;
        connTextLE.flexibleHeight = 0;

        // Subtitle / hint
        var connSub = ProceduralUIUtility.MakeText(connectingPanel, "ConnectingSub", "กำลังเตรียมระบบเครือข่ายและเปิดห้อง", 12, FontStyle.Italic, UITextRole.Secondary, TextAnchor.MiddleCenter);
        var connSubLE = connSub.gameObject.AddComponent<LayoutElement>();
        connSubLE.preferredHeight = 22;
        connSubLE.flexibleHeight = 0;

        // Controlled breathing spacer before cancel button
        var bottomSpacer = ProceduralUIUtility.MakeEmpty(connectingPanel, "BottomSpacer");
        var btmSpacerLE = bottomSpacer.AddComponent<LayoutElement>();
        btmSpacerLE.preferredHeight = 24;
        btmSpacerLE.flexibleHeight = 0;

        // Cancel button
        var (_, cancelConnBtn) = ProceduralUIUtility.MakeButton(connectingPanel, "CancelConnectingButton", "ยกเลิก", UIButtonStyle.Secondary, 48, 14);
        var cancelLE = cancelConnBtn.GetComponent<LayoutElement>();
        if (cancelLE != null) { cancelLE.preferredHeight = 48; cancelLE.flexibleHeight = 0; }

        connectingPanel.SetActive(false);

        // ── 10. Wire references to LobbyUI via SerializedObject ───────────────
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_canvas").objectReferenceValue = canvas;
        so.FindProperty("_lobbyRoot").objectReferenceValue = lobbyRoot;

        // Main panel
        so.FindProperty("_mainPanel").objectReferenceValue = mainPanel;
        so.FindProperty("_hostRoomMenuButton").objectReferenceValue = hostRoomBtn;
        so.FindProperty("_settingsMenuButton").objectReferenceValue = settingsBtn;
        so.FindProperty("_quitGameButton").objectReferenceValue = quitBtn;
        so.FindProperty("_steamStatusLabel").objectReferenceValue = null; // Completely hidden as requested
        so.FindProperty("_statusMessageLabel").objectReferenceValue = statusMsgText;

        // Host room panel
        so.FindProperty("_hostRoomPanel").objectReferenceValue = hostRoomPanel;
        so.FindProperty("_hostButton").objectReferenceValue = hostGameBtn;
        so.FindProperty("_joinButton").objectReferenceValue = joinGameBtn;
        so.FindProperty("_backFromHostRoomButton").objectReferenceValue = backFromHostBtn;

        // Join panel
        so.FindProperty("_joinPanel").objectReferenceValue = joinPanel;
        so.FindProperty("_roomCodeInput").objectReferenceValue = codeInput;
        so.FindProperty("_confirmJoinButton").objectReferenceValue = confirmJoinBtn;
        so.FindProperty("_pasteCodeButton").objectReferenceValue = pasteBtn;
        so.FindProperty("_backFromJoinButton").objectReferenceValue = backFromJoinBtn;

        // Settings panel
        so.FindProperty("_settingsPanel").objectReferenceValue = settingsPanel;
        so.FindProperty("_backFromSettingsButton").objectReferenceValue = backFromSettingsBtn;

        // Connecting panel
        so.FindProperty("_connectingPanel").objectReferenceValue = connectingPanel;
        so.FindProperty("_connectingLabel").objectReferenceValue = connectingText;
        so.FindProperty("_cancelConnectingButton").objectReferenceValue = cancelConnBtn;

        so.ApplyModifiedProperties();
        target.ApplyTheme(); // same styling the runtime applies in Awake
        EditorUtility.SetDirty(target);
        Debug.Log("[LobbyUIBuilder] Premium modern Lobby UI built and wired successfully! ✅");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PauseMenuBuilder
// ─────────────────────────────────────────────────────────────────────────────
public static class PauseMenuBuilder
{
    public static void Build(PauseMenu target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Pause Menu");

        Canvas canvas = target.GetComponent<Canvas>();
        if (canvas == null) canvas = target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup rootGroup = target.GetComponent<CanvasGroup>();
        if (rootGroup == null) rootGroup = target.gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;

        // Clear existing generated children
        for (int i = target.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(target.transform.GetChild(i).gameObject, true);
        }

        // Dark overlay
        var overlay = ProceduralUIUtility.MakeImage(target.gameObject, "BackgroundOverlay", UITheme.Overlay);
        ProceduralUIUtility.StretchFull(overlay.GetComponent<RectTransform>());

        // Center Pause Card
        var panelGO = ProceduralUIUtility.MakePanel(target.gameObject, "PausePanel", new Vector2(400, 420), Vector2.zero);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;

        var panelVL = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVL.padding = new RectOffset(36, 36, 36, 36);
        panelVL.spacing = 16;
        panelVL.childControlWidth = true;
        panelVL.childControlHeight = true;
        panelVL.childForceExpandWidth = true;
        panelVL.childForceExpandHeight = false;
        panelVL.childAlignment = TextAnchor.UpperCenter;

        // Title
        var title = ProceduralUIUtility.MakeText(panelGO, "PauseTitle", "PAUSED", 30, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 40;

        // Divider
        var div = ProceduralUIUtility.MakeImage(panelGO, "Divider", UITheme.Primary);
        var divLE = div.gameObject.AddComponent<LayoutElement>();
        divLE.preferredHeight = 2;

        // Buttons: Continue, Setting, Quit
        var (_, continueBtn) = ProceduralUIUtility.MakeButton(panelGO, "ContinueButton", "▶  CONTINUE", UIButtonStyle.Primary, 50);
        var (_, settingsBtn) = ProceduralUIUtility.MakeButton(panelGO, "SettingsButton", "⚙  SETTINGS", UIButtonStyle.Secondary, 50);
        var (_, quitBtn)     = ProceduralUIUtility.MakeButton(panelGO, "QuitToMenuButton", "✕  QUIT TO MENU", UIButtonStyle.Secondary, 50);

        // ── Settings Sub-Panel ─────
        var settingsPanelGO = ProceduralUIUtility.MakePanel(target.gameObject, "SettingsPanel", new Vector2(440, 380), Vector2.zero);
        var settingsRT = settingsPanelGO.GetComponent<RectTransform>();
        settingsRT.anchorMin = settingsRT.anchorMax = new Vector2(0.5f, 0.5f);
        settingsRT.pivot = new Vector2(0.5f, 0.5f);
        settingsRT.anchoredPosition = Vector2.zero;

        var settingsVL = settingsPanelGO.AddComponent<VerticalLayoutGroup>();
        settingsVL.padding = new RectOffset(36, 36, 36, 36);
        settingsVL.spacing = 16;
        settingsVL.childControlWidth = true;
        settingsVL.childControlHeight = true;
        settingsVL.childForceExpandWidth = true;
        settingsVL.childForceExpandHeight = false;
        settingsVL.childAlignment = TextAnchor.UpperCenter;

        var sTitle = ProceduralUIUtility.MakeText(settingsPanelGO, "SettingsTitle", "SETTINGS", 22, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleCenter);
        var sTitleLE = sTitle.gameObject.AddComponent<LayoutElement>();
        sTitleLE.preferredHeight = 32;

        // Master Volume Slider
        var volRow = ProceduralUIUtility.MakeEmpty(settingsPanelGO, "MasterVolumeRow");
        var volLE = volRow.AddComponent<LayoutElement>();
        volLE.preferredHeight = 44;
        var volVL = volRow.AddComponent<VerticalLayoutGroup>();
        volVL.spacing = 4; volVL.childControlWidth = true; volVL.childControlHeight = true; volVL.childForceExpandWidth = true; volVL.childForceExpandHeight = false;

        var volLabel = ProceduralUIUtility.MakeText(volRow, "Label", "MASTER VOLUME", 11, FontStyle.Bold, UITextRole.Secondary, TextAnchor.MiddleLeft);
        var vlblLE = volLabel.gameObject.AddComponent<LayoutElement>(); vlblLE.preferredHeight = 16;

        var sliderGO = ProceduralUIUtility.MakeEmpty(volRow, "Slider");
        var slLE = sliderGO.AddComponent<LayoutElement>(); slLE.preferredHeight = 20;
        var sliderBg = ProceduralUIUtility.MakeImage(sliderGO, "Background", UITheme.Track);
        var slBgRT = sliderBg.GetComponent<RectTransform>();
        slBgRT.anchorMin = new Vector2(0, 0.25f); slBgRT.anchorMax = new Vector2(1, 0.75f);
        slBgRT.offsetMin = slBgRT.offsetMax = Vector2.zero;

        var fillArea = ProceduralUIUtility.MakeEmpty(sliderGO, "FillArea");
        var faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0, 0.25f); faRT.anchorMax = new Vector2(1, 0.75f);
        faRT.offsetMin = new Vector2(5, 0); faRT.offsetMax = new Vector2(-5, 0);

        var fillImg = ProceduralUIUtility.MakeImage(fillArea, "Fill", UITheme.Primary);
        ProceduralUIUtility.StretchFull(fillImg.GetComponent<RectTransform>());

        var handleGO = ProceduralUIUtility.MakeImage(sliderGO, "Handle", UITheme.TextPrimary);
        var hRT = handleGO.GetComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(20, 0);

        var slider = sliderGO.AddComponent<Slider>();
        slider.fillRect = fillImg.GetComponent<RectTransform>();
        slider.handleRect = hRT;
        slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;
        slider.targetGraphic = handleGO;

        // Fullscreen Toggle
        var togRow = ProceduralUIUtility.MakeEmpty(settingsPanelGO, "FullscreenToggleRow");
        var togLE = togRow.AddComponent<LayoutElement>(); togLE.preferredHeight = 32;
        var togHL = togRow.AddComponent<HorizontalLayoutGroup>();
        togHL.spacing = 10; togHL.childControlWidth = false; togHL.childControlHeight = true; togHL.childAlignment = TextAnchor.MiddleLeft;

        var togBg = ProceduralUIUtility.MakeImage(togRow, "Background", UITheme.Track);
        var togBgRT = togBg.GetComponent<RectTransform>(); togBgRT.sizeDelta = new Vector2(22, 22);

        var checkImg = ProceduralUIUtility.MakeImage(togBg.gameObject, "Checkmark", UITheme.Primary);
        var ckRT = checkImg.GetComponent<RectTransform>();
        ckRT.anchorMin = new Vector2(0.15f, 0.15f); ckRT.anchorMax = new Vector2(0.85f, 0.85f);
        ckRT.offsetMin = ckRT.offsetMax = Vector2.zero;

        var togLabel = ProceduralUIUtility.MakeText(togRow, "Label", "FULLSCREEN", 13, FontStyle.Bold, UITextRole.Secondary, TextAnchor.MiddleLeft);
        var tlRT = togLabel.GetComponent<RectTransform>(); tlRT.sizeDelta = new Vector2(180, 22);

        var toggle = togRow.AddComponent<Toggle>();
        toggle.targetGraphic = togBg;
        toggle.graphic = checkImg;
        toggle.isOn = true;

        var (_, backBtn) = ProceduralUIUtility.MakeButton(settingsPanelGO, "BackFromSettingsButton", "←  BACK", UIButtonStyle.Secondary, 46);
        settingsPanelGO.SetActive(false);

        // ── Confirm Quit Sub-Panel ─────
        var confirmGO = ProceduralUIUtility.MakePanel(target.gameObject, "ConfirmQuitDialog", new Vector2(400, 200), Vector2.zero);
        var confirmRT = confirmGO.GetComponent<RectTransform>();
        confirmRT.anchorMin = confirmRT.anchorMax = new Vector2(0.5f, 0.5f);
        confirmRT.pivot = new Vector2(0.5f, 0.5f);
        confirmRT.anchoredPosition = Vector2.zero;

        var confirmVL = confirmGO.AddComponent<VerticalLayoutGroup>();
        confirmVL.padding = new RectOffset(30, 30, 24, 24);
        confirmVL.spacing = 14;
        confirmVL.childControlWidth = true;
        confirmVL.childControlHeight = true;
        confirmVL.childForceExpandWidth = true;
        confirmVL.childForceExpandHeight = false;
        confirmVL.childAlignment = TextAnchor.UpperCenter;

        var cTitle = ProceduralUIUtility.MakeText(confirmGO, "ConfirmTitle", "QUIT TO MENU?", 22, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleCenter);
        var cTitleLE = cTitle.gameObject.AddComponent<LayoutElement>();
        cTitleLE.preferredHeight = 30;

        var cDesc = ProceduralUIUtility.MakeText(confirmGO, "ConfirmDesc", "Are you sure you want to quit?", 13, FontStyle.Normal, UITextRole.Secondary, TextAnchor.MiddleCenter);
        var cDescLE = cDesc.gameObject.AddComponent<LayoutElement>();
        cDescLE.preferredHeight = 22;

        var cBtnRow = ProceduralUIUtility.MakeEmpty(confirmGO, "ButtonRow");
        var cBtnRowLE = cBtnRow.AddComponent<LayoutElement>();
        cBtnRowLE.preferredHeight = 44;
        var cBtnHL = cBtnRow.AddComponent<HorizontalLayoutGroup>();
        cBtnHL.spacing = 12;
        cBtnHL.childControlWidth = true;
        cBtnHL.childControlHeight = true;
        cBtnHL.childForceExpandWidth = true;

        var (_, confirmQuitBtn) = ProceduralUIUtility.MakeButton(cBtnRow, "ConfirmQuitButton", "YES, QUIT", UIButtonStyle.Danger, 44);
        var (_, cancelQuitBtn)  = ProceduralUIUtility.MakeButton(cBtnRow, "CancelQuitButton", "CANCEL", UIButtonStyle.Secondary, 44);
        confirmGO.SetActive(false);

        // Wire references via SerializedObject
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_canvas").objectReferenceValue = canvas;
        so.FindProperty("_pauseRoot").objectReferenceValue = rootGroup;
        so.FindProperty("_pausePanel").objectReferenceValue = panelGO;
        so.FindProperty("_continueButton").objectReferenceValue = continueBtn;
        so.FindProperty("_settingsButton").objectReferenceValue = settingsBtn;
        so.FindProperty("_quitToMenuButton").objectReferenceValue = quitBtn;
        so.FindProperty("_settingsPanel").objectReferenceValue = settingsPanelGO;
        so.FindProperty("_backFromSettingsButton").objectReferenceValue = backBtn;
        so.FindProperty("_backgroundOverlay").objectReferenceValue = overlay;
        so.FindProperty("_confirmDialog").objectReferenceValue = confirmGO;
        so.FindProperty("_confirmQuitButton").objectReferenceValue = confirmQuitBtn;
        so.FindProperty("_cancelQuitButton").objectReferenceValue = cancelQuitBtn;
        so.FindProperty("_masterVolumeSlider").objectReferenceValue = slider;
        so.FindProperty("_fullscreenToggle").objectReferenceValue = toggle;
        so.ApplyModifiedProperties();
        target.ApplyTheme(); // same styling the runtime applies in Awake

        EditorUtility.SetDirty(target);
        Debug.Log("[PauseMenuBuilder] Pause Menu built and wired successfully! ✅");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RoomCodeHUDBuilder
// ─────────────────────────────────────────────────────────────────────────────
public static class RoomCodeHUDBuilder
{
    public static void Build(RoomCodeHUD target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Room Code HUD");

        Canvas canvas = target.GetComponent<Canvas>();
        if (canvas == null) canvas = target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup hudGroup = target.GetComponent<CanvasGroup>();
        if (hudGroup == null) hudGroup = target.gameObject.AddComponent<CanvasGroup>();
        hudGroup.alpha = 0f;

        // Clear existing generated children
        for (int i = target.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(target.transform.GetChild(i).gameObject, true);
        }

        // Top-left HUD Card (250 x 190)
        var panelGO = ProceduralUIUtility.MakePanel(target.gameObject, "RoomCodePanel", new Vector2(250, 190), Vector2.zero);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 1f);
        panelRT.pivot = new Vector2(0f, 1f);
        panelRT.anchoredPosition = new Vector2(20f, -20f);

        var panelVL = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVL.padding = new RectOffset(16, 16, 12, 12);
        panelVL.spacing = 6;
        panelVL.childControlWidth = true;
        panelVL.childControlHeight = true;
        panelVL.childForceExpandWidth = true;
        panelVL.childForceExpandHeight = false;
        panelVL.childAlignment = TextAnchor.UpperLeft;

        // 1. Header Row (Room code tag + Host/Client tag)
        var headerRow = ProceduralUIUtility.MakeEmpty(panelGO, "HeaderRow");
        var headerRowLE = headerRow.AddComponent<LayoutElement>();
        headerRowLE.preferredHeight = 18;
        var headerHL = headerRow.AddComponent<HorizontalLayoutGroup>();
        headerHL.childControlWidth = false;
        headerHL.childControlHeight = true;
        headerHL.childForceExpandWidth = false;
        headerHL.spacing = 8;

        var headerTitle = ProceduralUIUtility.MakeText(headerRow, "HeaderTitle", "ROOM CODE", 10, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleLeft);
        var htRT = headerTitle.GetComponent<RectTransform>();
        htRT.sizeDelta = new Vector2(90, 18);
        var htLE = headerTitle.gameObject.AddComponent<LayoutElement>();
        htLE.preferredWidth = 90;

        var modeText = ProceduralUIUtility.MakeText(headerRow, "ModeLabel", "● HOST", 10, FontStyle.Bold, UITextRole.Positive, TextAnchor.MiddleRight);
        var mtRT = modeText.GetComponent<RectTransform>();
        mtRT.sizeDelta = new Vector2(110, 18);
        var mtLE = modeText.gameObject.AddComponent<LayoutElement>();
        mtLE.preferredWidth = 110;

        // 2. Room code text (Big, bold, stylized)
        var codeText = ProceduralUIUtility.MakeText(panelGO, "RoomCodeText", "------", 24, FontStyle.Bold, UITextRole.Primary, TextAnchor.MiddleLeft);
        var codeLE = codeText.gameObject.AddComponent<LayoutElement>();
        codeLE.preferredHeight = 30;

        // 3. Divider line
        var div = ProceduralUIUtility.MakeImage(panelGO, "Divider", UITheme.Track);
        var divLE = div.gameObject.AddComponent<LayoutElement>();
        divLE.preferredHeight = 1;

        // 4. Players section title
        var playerText = ProceduralUIUtility.MakeText(panelGO, "PlayerCountText", "PLAYERS (1/4)", 10, FontStyle.Bold, UITextRole.Accent, TextAnchor.MiddleLeft);
        var countLE = playerText.gameObject.AddComponent<LayoutElement>();
        countLE.preferredHeight = 16;

        // 5. Player names list
        var namesText = ProceduralUIUtility.MakeText(panelGO, "PlayerNamesText", "• Player (Host)", 11, FontStyle.Normal, UITextRole.Primary, TextAnchor.UpperLeft);
        var namesLE = namesText.gameObject.AddComponent<LayoutElement>();
        namesLE.preferredHeight = 44;

        // 6. Copy button row
        var copyRow = ProceduralUIUtility.MakeEmpty(panelGO, "CopyRow");
        var copyRowLE = copyRow.AddComponent<LayoutElement>();
        copyRowLE.preferredHeight = 28;
        var copyHL = copyRow.AddComponent<HorizontalLayoutGroup>();
        copyHL.spacing = 8;
        copyHL.childControlWidth = false;
        copyHL.childControlHeight = true;

        var (copyBtnGO, copyBtn) = ProceduralUIUtility.MakeButton(copyRow, "CopyButton", "Copy Code", UIButtonStyle.Secondary, 28, 11);
        var copyBtnRT = copyBtnGO.GetComponent<RectTransform>();
        copyBtnRT.sizeDelta = new Vector2(105, 28);
        var copyBtnLE = copyBtnGO.GetComponent<LayoutElement>();
        if (copyBtnLE != null) copyBtnLE.preferredWidth = 105;

        var copyFeedback = ProceduralUIUtility.MakeText(copyRow, "CopyFeedbackText", "✓ Copied!", 11, FontStyle.Bold, UITextRole.Positive, TextAnchor.MiddleLeft);
        var fbRT = copyFeedback.GetComponent<RectTransform>();
        fbRT.sizeDelta = new Vector2(80, 28);
        var fbLE = copyFeedback.gameObject.AddComponent<LayoutElement>();
        fbLE.preferredWidth = 80;
        copyFeedback.gameObject.SetActive(false);

        // Wire references
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_canvas").objectReferenceValue = canvas;
        so.FindProperty("_hudGroup").objectReferenceValue = hudGroup;
        so.FindProperty("_roomCodeText").objectReferenceValue = codeText;
        so.FindProperty("_modeLabel").objectReferenceValue = modeText;
        so.FindProperty("_playerCountText").objectReferenceValue = playerText;
        so.FindProperty("_playerNamesText").objectReferenceValue = namesText;
        so.FindProperty("_copyButton").objectReferenceValue = copyBtn;
        so.FindProperty("_copyFeedbackText").objectReferenceValue = copyFeedback;
        so.ApplyModifiedProperties();

        target.ApplyTheme(); // same styling the runtime applies in Awake
        EditorUtility.SetDirty(target);
        Debug.Log("[RoomCodeHUDBuilder] Premium modern Room Code HUD built and wired successfully! ✅");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AutoSceneUIInstaller
// Automatically sets up EventSystem, LobbyUI, PauseMenu, and RoomCodeHUD in the scene
// Or use the Unity top menu: CoopGame → Build All UI in Scene
// ─────────────────────────────────────────────────────────────────────────────
[InitializeOnLoad]
public static class AutoSceneUIInstaller
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";

    private static bool IsLobbyScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.isLoaded && scene.path == LobbyScenePath;
    }

    private static T FindInLobbyScene<T>() where T : Component
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    static AutoSceneUIInstaller()
    {
        // Runtime UI self-builds cleanly in Awake(), so we do not auto-bake UI children into Edit Mode scenes.
    }

    public static bool NeedsBuild()
    {
        if (!IsLobbyScene()) return false;

        var lobby = FindInLobbyScene<LobbyUI>();
        if (lobby == null) return true;

        var pause = FindInLobbyScene<PauseMenu>();
        if (pause == null) return true;

        var hud = FindInLobbyScene<RoomCodeHUD>();
        if (hud == null) return true;

        var es = FindInLobbyScene<UnityEngine.EventSystems.EventSystem>();
        if (es == null) return true;

        return false;
    }

    [MenuItem("CoopGame/Build All UI in Scene")]
    public static void BuildAll()
    {
        if (!IsLobbyScene())
        {
            Debug.LogWarning("[AutoSceneUIInstaller] Lobby UI can only be built in Lobby.");
            return;
        }

        EnsureEventSystem();
        EnsureLobbyUI();
        EnsurePauseMenu();
        EnsureRoomCodeHUD();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isLoaded)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        Debug.Log("[AutoSceneUIInstaller] All UI (LobbyUI, PauseMenu, RoomCodeHUD, EventSystem) built, wired, and saved to scene! ✅");
    }

    private static void EnsureEventSystem()
    {
        var es = FindInLobbyScene<UnityEngine.EventSystems.EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }
        else
        {
            if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null &&
                es.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
            {
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }
    }

    private static void EnsureLobbyUI()
    {
        var lobby = FindInLobbyScene<LobbyUI>();
        if (lobby == null)
        {
            var go = new GameObject("LobbyUI");
            lobby = go.AddComponent<LobbyUI>();
            Undo.RegisterCreatedObjectUndo(go, "Create LobbyUI");
        }
        LobbyUIBuilder.Build(lobby);
    }

    private static void EnsurePauseMenu()
    {
        var pause = FindInLobbyScene<PauseMenu>();
        if (pause == null)
        {
            var go = new GameObject("PauseMenu");
            pause = go.AddComponent<PauseMenu>();
            Undo.RegisterCreatedObjectUndo(go, "Create PauseMenu");
        }
        PauseMenuBuilder.Build(pause);
    }

    private static void EnsureRoomCodeHUD()
    {
        var hud = FindInLobbyScene<RoomCodeHUD>();
        if (hud == null)
        {
            var go = new GameObject("RoomCodeHUD");
            hud = go.AddComponent<RoomCodeHUD>();
            Undo.RegisterCreatedObjectUndo(go, "Create RoomCodeHUD");
        }
        RoomCodeHUDBuilder.Build(hud);
    }
}
#endif
