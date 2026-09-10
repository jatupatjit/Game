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
[DisallowMultipleComponent]
public class SpinnerAnimator : MonoBehaviour
{
    [SerializeField] private float _speed = 180f;

    private void Update()
    {
        transform.Rotate(0, 0, -_speed * Time.unscaledDeltaTime);
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
    private static readonly Color BgDeep        = HexColor("#0A0E1A");
    private static readonly Color PanelColor    = HexColor("#111827EE");
    private static readonly Color AccentCyan    = HexColor("#00D9FF");
    private static readonly Color ButtonHost    = HexColor("#1D4ED8");
    private static readonly Color ButtonJoin    = HexColor("#059669");
    private static readonly Color ButtonSettings= HexColor("#4B5563");
    private static readonly Color ButtonQuit    = HexColor("#991B1B");
    private static readonly Color ButtonBack    = HexColor("#374151");
    private static readonly Color ButtonCopy    = HexColor("#1F2937");
    private static readonly Color TextPrimary   = HexColor("#F9FAFB");
    private static readonly Color TextSecondary = HexColor("#9CA3AF");
    private static readonly Color InputBg       = HexColor("#1F2937");

    public static void Build(LobbyUI target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Lobby UI");

        // 1. Fullscreen Canvas
        Canvas canvas = target.GetComponent<Canvas>() ?? target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>() ?? target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup lobbyRoot = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();

        // Clear existing generated children
        while (target.transform.childCount > 0)
        {
            Undo.DestroyObjectImmediate(target.transform.GetChild(0).gameObject);
        }

        // 2. Background
        var bg = MakeImage(target.gameObject, "Background", BgDeep);
        StretchFull(bg.GetComponent<RectTransform>());

        // 3. Center Card
        var cardGO = MakePanel(target.gameObject, "LobbyCard", new Vector2(500, 560), Vector2.zero, PanelColor);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;

        var cardLayout = cardGO.AddComponent<VerticalLayoutGroup>();
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.padding = new RectOffset(40, 40, 36, 36);
        cardLayout.spacing = 14;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = false;
        cardLayout.childForceExpandWidth = true;

        // 4. Header: Game Title
        var titleText = MakeText(cardGO, "TitleLabel", "DONT DROP IT", 34, FontStyle.Bold, TextPrimary, TextAnchor.MiddleCenter);
        var titleLE = titleText.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 46;

        // Accent Line
        var accentLine = MakeImage(cardGO, "AccentLine", AccentCyan);
        var accentLE = accentLine.gameObject.AddComponent<LayoutElement>();
        accentLE.preferredHeight = 2;

        // Steam Status Label
        var steamStatusText = MakeText(cardGO, "SteamStatusLabel", "● Checking Steam status...", 13, FontStyle.Normal, TextSecondary, TextAnchor.MiddleCenter);
        var steamLE = steamStatusText.gameObject.AddComponent<LayoutElement>();
        steamLE.preferredHeight = 24;

        // Status Message Label (errors, connection updates)
        var statusMsgText = MakeText(cardGO, "StatusMessageLabel", "", 12, FontStyle.Italic, TextSecondary, TextAnchor.MiddleCenter);
        var statusLE = statusMsgText.gameObject.AddComponent<LayoutElement>();
        statusLE.preferredHeight = 22;

        // ── 5. MAIN MENU PANEL (Host Room / Setting / Quit) ───────────────────
        var mainPanel = MakeEmpty(cardGO, "MainMenuPanel");
        var mainVL = mainPanel.AddComponent<VerticalLayoutGroup>();
        mainVL.spacing = 14;
        mainVL.childControlWidth = true;
        mainVL.childControlHeight = false;
        mainVL.childForceExpandWidth = true;
        mainVL.childAlignment = TextAnchor.UpperCenter;

        var (_, hostRoomBtn) = MakeButton(mainPanel, "HostRoomMenuButton", "🎮  HOST ROOM", ButtonHost, 50);
        var (_, settingsBtn) = MakeButton(mainPanel, "SettingsMenuButton", "⚙  SETTINGS", ButtonSettings, 50);
        var (_, quitBtn)     = MakeButton(mainPanel, "QuitGameButton", "✕  QUIT GAME", ButtonQuit, 50);

        // ── 6. HOST ROOM SUB-PANEL (Host / Join / Back) ───────────────────────
        var hostRoomPanel = MakeEmpty(cardGO, "HostRoomSubPanel");
        var hostRoomVL = hostRoomPanel.AddComponent<VerticalLayoutGroup>();
        hostRoomVL.spacing = 14;
        hostRoomVL.childControlWidth = true;
        hostRoomVL.childControlHeight = false;
        hostRoomVL.childForceExpandWidth = true;
        hostRoomVL.childAlignment = TextAnchor.UpperCenter;

        var subTitle1 = MakeText(hostRoomPanel, "SubTitle", "CREATE OR JOIN", 14, FontStyle.Bold, AccentCyan, TextAnchor.MiddleCenter);
        var subTitle1LE = subTitle1.gameObject.AddComponent<LayoutElement>();
        subTitle1LE.preferredHeight = 26;

        var (_, hostGameBtn) = MakeButton(hostRoomPanel, "HostButton", "▶  HOST", ButtonHost, 50);
        var (_, joinGameBtn) = MakeButton(hostRoomPanel, "JoinButton", "🔑  JOIN", ButtonJoin, 50);
        var (_, backFromHostBtn) = MakeButton(hostRoomPanel, "BackFromHostRoomButton", "←  BACK", ButtonBack, 46);
        hostRoomPanel.SetActive(false);

        // ── 7. JOIN ROOM SUB-PANEL (Input / Enter / Paste / Back) ──────────────
        var joinPanel = MakeEmpty(cardGO, "JoinSubPanel");
        var joinVL = joinPanel.AddComponent<VerticalLayoutGroup>();
        joinVL.spacing = 12;
        joinVL.childControlWidth = true;
        joinVL.childControlHeight = false;
        joinVL.childForceExpandWidth = true;
        joinVL.childAlignment = TextAnchor.UpperCenter;

        var subTitle2 = MakeText(joinPanel, "JoinTitle", "ENTER ROOM CODE", 14, FontStyle.Bold, AccentCyan, TextAnchor.MiddleCenter);
        var subTitle2LE = subTitle2.gameObject.AddComponent<LayoutElement>();
        subTitle2LE.preferredHeight = 24;

        var (inputGO, codeInput) = MakeInputField(joinPanel, "RoomCodeInput", "ROOM CODE (e.g. K7M2X9)", 48);
        var inputLE = inputGO.AddComponent<LayoutElement>();
        inputLE.preferredHeight = 48;

        // Button row
        var btnRow = MakeEmpty(joinPanel, "JoinBtnRow");
        var btnRowLE = btnRow.AddComponent<LayoutElement>();
        btnRowLE.preferredHeight = 44;
        var btnRowHL = btnRow.AddComponent<HorizontalLayoutGroup>();
        btnRowHL.spacing = 10;
        btnRowHL.childControlWidth = true;
        btnRowHL.childControlHeight = true;
        btnRowHL.childForceExpandWidth = true;

        var (_, pasteBtn)       = MakeButton(btnRow, "PasteCodeButton", "📋 Paste", ButtonCopy, 44);
        var (_, confirmJoinBtn) = MakeButton(btnRow, "ConfirmJoinButton", "✓ Enter", ButtonJoin, 44);
        var (_, backFromJoinBtn)= MakeButton(btnRow, "BackFromJoinButton", "← Back", ButtonBack, 44);
        joinPanel.SetActive(false);

        // ── 8. SETTINGS SUB-PANEL (Blank screen with Back) ─────────────────────
        var settingsPanel = MakeEmpty(cardGO, "SettingsSubPanel");
        var settingsVL = settingsPanel.AddComponent<VerticalLayoutGroup>();
        settingsVL.spacing = 18;
        settingsVL.childControlWidth = true;
        settingsVL.childControlHeight = false;
        settingsVL.childForceExpandWidth = true;
        settingsVL.childAlignment = TextAnchor.UpperCenter;

        var sTitle = MakeText(settingsPanel, "SettingsTitle", "SETTINGS", 18, FontStyle.Bold, AccentCyan, TextAnchor.MiddleCenter);
        var sTitleLE = sTitle.gameObject.AddComponent<LayoutElement>();
        sTitleLE.preferredHeight = 32;

        // Blank content space
        var blankArea = MakeEmpty(settingsPanel, "BlankSettingsArea");
        var blankLE = blankArea.AddComponent<LayoutElement>();
        blankLE.preferredHeight = 120;
        var blankText = MakeText(blankArea, "BlankNote", "(Settings will be configured here)", 13, FontStyle.Italic, TextSecondary, TextAnchor.MiddleCenter);
        StretchFull(blankText.GetComponent<RectTransform>());

        var (_, backFromSettingsBtn) = MakeButton(settingsPanel, "BackFromSettingsButton", "←  BACK", ButtonBack, 46);
        settingsPanel.SetActive(false);

        // ── 9. CONNECTING PANEL ───────────────────────────────────────────────
        var connectingPanel = MakeEmpty(cardGO, "ConnectingPanel");
        var connVL = connectingPanel.AddComponent<VerticalLayoutGroup>();
        connVL.spacing = 16;
        connVL.childControlWidth = true;
        connVL.childControlHeight = false;
        connVL.childForceExpandWidth = true;
        connVL.childAlignment = TextAnchor.UpperCenter;

        var spinnerGO = MakeImage(connectingPanel, "SpinnerRing", AccentCyan);
        var spinnerRT = spinnerGO.GetComponent<RectTransform>();
        spinnerRT.sizeDelta = new Vector2(40, 40);
        var spinnerLE = spinnerGO.gameObject.AddComponent<LayoutElement>();
        spinnerLE.preferredWidth = 40;
        spinnerLE.preferredHeight = 40;
        spinnerGO.gameObject.AddComponent<SpinnerAnimator>();

        var connectingText = MakeText(connectingPanel, "ConnectingLabel", "Connecting to room...", 16, FontStyle.Bold, AccentCyan, TextAnchor.MiddleCenter);
        var connTextLE = connectingText.gameObject.AddComponent<LayoutElement>();
        connTextLE.preferredHeight = 30;
        connectingPanel.SetActive(false);

        // ── 10. Wire references to LobbyUI via SerializedObject ───────────────
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_lobbyRoot").objectReferenceValue = lobbyRoot;

        // Main panel
        so.FindProperty("_mainPanel").objectReferenceValue = mainPanel;
        so.FindProperty("_hostRoomMenuButton").objectReferenceValue = hostRoomBtn;
        so.FindProperty("_settingsMenuButton").objectReferenceValue = settingsBtn;
        so.FindProperty("_quitGameButton").objectReferenceValue = quitBtn;
        so.FindProperty("_steamStatusLabel").objectReferenceValue = steamStatusText;
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

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        Debug.Log("[LobbyUIBuilder] Lobby UI built and wired successfully! ✅");
    }

    // ─────────────────────────────────────────── UI Helpers ───────────────────

    public static Font GetDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    public static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    public static Image MakeImage(GameObject parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static GameObject MakeEmpty(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.AddComponent<RectTransform>();
        return go;
    }

    public static GameObject MakePanel(GameObject parent, string name, Vector2 size, Vector2 pos, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    public static Text MakeText(GameObject parent, string name, string text, int size, FontStyle style, Color color, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400, 30);
        var t = go.AddComponent<Text>();
        t.font = GetDefaultFont();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static (GameObject, Button) MakeButton(GameObject parent, string name, string label, Color bgColor, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = bgColor * 1.3f;
        colors.pressedColor = bgColor * 0.75f;
        colors.selectedColor = bgColor;
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.targetGraphic = img;

        var lbl = MakeText(go, "Label", label, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        StretchFull(lbl.GetComponent<RectTransform>());

        return (go, btn);
    }

    public static (GameObject, InputField) MakeInputField(GameObject parent, string name, string placeholder, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);

        var bgImg = go.AddComponent<Image>();
        bgImg.color = HexColor("#1F2937");

        var inputField = go.AddComponent<InputField>();
        inputField.caretWidth = 2;
        inputField.characterLimit = 8;

        // Text object
        var textObj = MakeText(go, "Text", "", 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        StretchFull(textObj.GetComponent<RectTransform>());
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.offsetMin = new Vector2(10, 0);
        textRT.offsetMax = new Vector2(-10, 0);

        // Placeholder object
        var phObj = MakeText(go, "Placeholder", placeholder, 14, FontStyle.Italic, HexColor("#9CA3AF"), TextAnchor.MiddleCenter);
        StretchFull(phObj.GetComponent<RectTransform>());
        var phRT = phObj.GetComponent<RectTransform>();
        phRT.offsetMin = new Vector2(10, 0);
        phRT.offsetMax = new Vector2(-10, 0);

        inputField.textComponent = textObj;
        inputField.placeholder = phObj;
        inputField.targetGraphic = bgImg;

        return (go, inputField);
    }

    public static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PauseMenuBuilder
// ─────────────────────────────────────────────────────────────────────────────
public static class PauseMenuBuilder
{
    private static readonly Color OverlayColor   = LobbyUIBuilder.HexColor("#0A0E1ACC");
    private static readonly Color PanelColor     = LobbyUIBuilder.HexColor("#111827EE");
    private static readonly Color AccentCyan     = LobbyUIBuilder.HexColor("#00D9FF");
    private static readonly Color BtnContinue    = LobbyUIBuilder.HexColor("#059669");
    private static readonly Color BtnSettings    = LobbyUIBuilder.HexColor("#1D4ED8");
    private static readonly Color BtnQuit        = LobbyUIBuilder.HexColor("#991B1B");
    private static readonly Color BtnBack        = LobbyUIBuilder.HexColor("#374151");
    private static readonly Color TextPrimary    = LobbyUIBuilder.HexColor("#F9FAFB");
    private static readonly Color TextSecondary  = LobbyUIBuilder.HexColor("#9CA3AF");

    public static void Build(PauseMenu target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Pause Menu");

        Canvas canvas = target.GetComponent<Canvas>() ?? target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>() ?? target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup rootGroup = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;

        // Clear existing generated children
        while (target.transform.childCount > 0)
        {
            Undo.DestroyObjectImmediate(target.transform.GetChild(0).gameObject);
        }

        // Dark overlay
        var overlay = LobbyUIBuilder.MakeImage(target.gameObject, "BackgroundOverlay", OverlayColor);
        LobbyUIBuilder.StretchFull(overlay.GetComponent<RectTransform>());

        // Center Pause Card
        var panelGO = LobbyUIBuilder.MakePanel(target.gameObject, "PausePanel", new Vector2(400, 420), Vector2.zero, PanelColor);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;

        var panelVL = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVL.padding = new RectOffset(36, 36, 36, 36);
        panelVL.spacing = 16;
        panelVL.childControlWidth = true;
        panelVL.childControlHeight = false;
        panelVL.childForceExpandWidth = true;
        panelVL.childAlignment = TextAnchor.UpperCenter;

        // Title
        var title = LobbyUIBuilder.MakeText(panelGO, "PauseTitle", "PAUSED", 32, FontStyle.Bold, TextPrimary, TextAnchor.MiddleCenter);
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 44;

        // Accent line
        var div = LobbyUIBuilder.MakeImage(panelGO, "Divider", AccentCyan);
        var divLE = div.gameObject.AddComponent<LayoutElement>();
        divLE.preferredHeight = 2;

        // Buttons: Continue, Setting, Quit
        var (_, continueBtn) = LobbyUIBuilder.MakeButton(panelGO, "ContinueButton", "▶  CONTINUE", BtnContinue, 50);
        var (_, settingsBtn) = LobbyUIBuilder.MakeButton(panelGO, "SettingsButton", "⚙  SETTINGS", BtnSettings, 50);
        var (_, quitBtn)     = LobbyUIBuilder.MakeButton(panelGO, "QuitToMenuButton", "✕  QUIT TO MENU", BtnQuit, 50);

        // ── Settings Sub-Panel (Blank screen placeholder with Back button) ─────
        var settingsPanelGO = LobbyUIBuilder.MakePanel(target.gameObject, "SettingsPanel", new Vector2(420, 360), Vector2.zero, PanelColor);
        var settingsRT = settingsPanelGO.GetComponent<RectTransform>();
        settingsRT.anchorMin = settingsRT.anchorMax = new Vector2(0.5f, 0.5f);
        settingsRT.pivot = new Vector2(0.5f, 0.5f);
        settingsRT.anchoredPosition = Vector2.zero;

        var settingsVL = settingsPanelGO.AddComponent<VerticalLayoutGroup>();
        settingsVL.padding = new RectOffset(36, 36, 36, 36);
        settingsVL.spacing = 20;
        settingsVL.childControlWidth = true;
        settingsVL.childControlHeight = false;
        settingsVL.childForceExpandWidth = true;
        settingsVL.childAlignment = TextAnchor.UpperCenter;

        var sTitle = LobbyUIBuilder.MakeText(settingsPanelGO, "SettingsTitle", "SETTINGS", 24, FontStyle.Bold, AccentCyan, TextAnchor.MiddleCenter);
        var sTitleLE = sTitle.gameObject.AddComponent<LayoutElement>();
        sTitleLE.preferredHeight = 36;

        var blankSettingsArea = LobbyUIBuilder.MakeEmpty(settingsPanelGO, "BlankSettingsContent");
        var blankLE = blankSettingsArea.AddComponent<LayoutElement>();
        blankLE.preferredHeight = 120;
        var blankNote = LobbyUIBuilder.MakeText(blankSettingsArea, "BlankNote", "(Settings will appear here)", 13, FontStyle.Italic, TextSecondary, TextAnchor.MiddleCenter);
        LobbyUIBuilder.StretchFull(blankNote.GetComponent<RectTransform>());

        var (_, backBtn) = LobbyUIBuilder.MakeButton(settingsPanelGO, "BackFromSettingsButton", "←  BACK", BtnBack, 46);
        settingsPanelGO.SetActive(false);

        // Wire references via SerializedObject
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_pauseRoot").objectReferenceValue = rootGroup;
        so.FindProperty("_continueButton").objectReferenceValue = continueBtn;
        so.FindProperty("_settingsButton").objectReferenceValue = settingsBtn;
        so.FindProperty("_quitToMenuButton").objectReferenceValue = quitBtn;
        so.FindProperty("_settingsPanel").objectReferenceValue = settingsPanelGO;
        so.FindProperty("_backFromSettingsButton").objectReferenceValue = backBtn;
        so.FindProperty("_backgroundOverlay").objectReferenceValue = overlay;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(target);
        Debug.Log("[PauseMenuBuilder] Pause Menu built and wired successfully! ✅");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RoomCodeHUDBuilder
// ─────────────────────────────────────────────────────────────────────────────
public static class RoomCodeHUDBuilder
{
    private static readonly Color BgDark       = LobbyUIBuilder.HexColor("#111827DD");
    private static readonly Color AccentCyan   = LobbyUIBuilder.HexColor("#00D9FF");
    private static readonly Color TextPrimary  = LobbyUIBuilder.HexColor("#F9FAFB");
    private static readonly Color TextSecondary= LobbyUIBuilder.HexColor("#9CA3AF");
    private static readonly Color ButtonCopy   = LobbyUIBuilder.HexColor("#1D4ED8");

    public static void Build(RoomCodeHUD target)
    {
        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Build Room Code HUD");

        Canvas canvas = target.GetComponent<Canvas>() ?? target.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>() ?? target.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (target.GetComponent<GraphicRaycaster>() == null)
            target.gameObject.AddComponent<GraphicRaycaster>();

        CanvasGroup hudGroup = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
        hudGroup.alpha = 0f;

        // Clear existing generated children
        while (target.transform.childCount > 0)
        {
            Undo.DestroyObjectImmediate(target.transform.GetChild(0).gameObject);
        }

        // Top-left HUD Card
        var panelGO = new GameObject("RoomCodePanel");
        panelGO.transform.SetParent(target.gameObject.transform, false);
        Undo.RegisterCreatedObjectUndo(panelGO, "Create RoomCodePanel");

        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 1f);
        panelRT.pivot = new Vector2(0f, 1f);
        panelRT.anchoredPosition = new Vector2(16f, -16f);
        panelRT.sizeDelta = new Vector2(230, 105);

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = BgDark;

        var panelVL = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVL.padding = new RectOffset(12, 12, 8, 8);
        panelVL.spacing = 3;
        panelVL.childControlWidth = true;
        panelVL.childControlHeight = false;
        panelVL.childForceExpandWidth = true;

        // Mode label (HOST / CLIENT)
        var modeText = LobbyUIBuilder.MakeText(panelGO, "ModeLabel", "HOST", 11, FontStyle.Bold, TextSecondary, TextAnchor.MiddleLeft);
        var modeLE = modeText.gameObject.AddComponent<LayoutElement>();
        modeLE.preferredHeight = 16;

        // Room code text
        var codeText = LobbyUIBuilder.MakeText(panelGO, "RoomCodeText", "------", 24, FontStyle.Bold, AccentCyan, TextAnchor.MiddleLeft);
        var codeLE = codeText.gameObject.AddComponent<LayoutElement>();
        codeLE.preferredHeight = 30;

        // Player count text
        var playerText = LobbyUIBuilder.MakeText(panelGO, "PlayerCountText", "Players: 1", 11, FontStyle.Normal, TextSecondary, TextAnchor.MiddleLeft);
        var playerLE = playerText.gameObject.AddComponent<LayoutElement>();
        playerLE.preferredHeight = 15;

        // Copy button row
        var copyRow = LobbyUIBuilder.MakeEmpty(panelGO, "CopyRow");
        var copyRowLE = copyRow.AddComponent<LayoutElement>();
        copyRowLE.preferredHeight = 24;
        var copyHL = copyRow.AddComponent<HorizontalLayoutGroup>();
        copyHL.spacing = 8;
        copyHL.childControlWidth = false;
        copyHL.childControlHeight = true;

        var (copyBtnGO, copyBtn) = LobbyUIBuilder.MakeButton(copyRow, "CopyButton", "📋 Copy Code", ButtonCopy, 24);
        var copyBtnRT = copyBtnGO.GetComponent<RectTransform>();
        copyBtnRT.sizeDelta = new Vector2(100, 24);
        var copyBtnLE = copyBtnGO.GetComponent<LayoutElement>();
        if (copyBtnLE != null) copyBtnLE.preferredWidth = 100;

        var copyFeedback = LobbyUIBuilder.MakeText(copyRow, "CopyFeedbackText", "Copied!", 11, FontStyle.Bold, AccentCyan, TextAnchor.MiddleLeft);
        var fbRT = copyFeedback.GetComponent<RectTransform>();
        fbRT.sizeDelta = new Vector2(60, 24);
        copyFeedback.gameObject.SetActive(false);

        // Wire references
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("_hudGroup").objectReferenceValue = hudGroup;
        so.FindProperty("_roomCodeText").objectReferenceValue = codeText;
        so.FindProperty("_modeLabel").objectReferenceValue = modeText;
        so.FindProperty("_playerCountText").objectReferenceValue = playerText;
        so.FindProperty("_copyButton").objectReferenceValue = copyBtn;
        so.FindProperty("_copyFeedbackText").objectReferenceValue = copyFeedback;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(target);
        Debug.Log("[RoomCodeHUDBuilder] Room Code HUD built and wired successfully! ✅");
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
    static AutoSceneUIInstaller()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().isLoaded)
            {
                var lobby = Object.FindFirstObjectByType<LobbyUI>();
                if (lobby == null)
                {
                    BuildAll();
                }
            }
        };
    }

    [MenuItem("CoopGame/Build All UI in Scene")]
    public static void BuildAll()
    {
        EnsureEventSystem();
        EnsureLobbyUI();
        EnsurePauseMenu();
        EnsureRoomCodeHUD();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isLoaded)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log("[AutoSceneUIInstaller] All UI (LobbyUI, PauseMenu, RoomCodeHUD, EventSystem) built and wired in scene! ✅");
    }

    private static void EnsureEventSystem()
    {
        var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
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
        var lobby = Object.FindFirstObjectByType<LobbyUI>();
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
        var pause = Object.FindFirstObjectByType<PauseMenu>();
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
        var hud = Object.FindFirstObjectByType<RoomCodeHUD>();
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
