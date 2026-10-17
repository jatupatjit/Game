using UnityEngine;
using UnityEngine.UI;

/// <summary>Button roles of the low-poly block theme.</summary>
public enum UIButtonStyle { Primary, Secondary, Danger }

/// <summary>Text roles of the low-poly block theme.</summary>
public enum UITextRole { Primary, Secondary, Accent, OnPrimary, Positive, Danger }

/// <summary>
/// Single source of the "Low-poly block" UI theme (approved concept 3): soil-dark chamfered panels,
/// orange route-marker accent, green stamina, warm muted text, Sarabun (GameThai) font. Flat colours,
/// no gradients or glow. Screens read colours, sprites and the font only from here.
/// </summary>
public static class UITheme
{
    // ── Palette ──────────────────────────────────────────────────────────────
    // Soil dark at 0.97: the project blends UI in Linear colour space, where 0.92 let the bright sky
    // wash the panels to blue-grey; 0.97 keeps the soil hue while staying faintly see-through.
    public static readonly Color Panel         = Hex("#2A211BF7");
    public static readonly Color PanelSolid    = Hex("#2A211B");
    public static readonly Color Track         = Hex("#4A3B30");     // secondary / bar track
    public static readonly Color Primary       = Hex("#F27A4A");     // primary action + cargo HP (route-marker orange)
    public static readonly Color OnPrimary     = Hex("#2A1408");     // text on orange
    public static readonly Color Stamina       = Hex("#7FD27F");     // stamina / positive state
    public static readonly Color Danger        = Hex("#E2553A");     // low cargo HP, exhaustion, destructive confirm
    public static readonly Color TextPrimary   = Hex("#FFFFFF");
    public static readonly Color TextSecondary = Hex("#CDBFB2");
    public static readonly Color Outline       = Hex("#1A1410E6");   // text/shape outline
    public static readonly Color Overlay       = Hex("#1A1410B8");   // full-screen dim behind modals
    public static readonly Color Ghost         = Hex("#CDBFB273");   // trailing "recent loss" bar
    public static readonly Color Backdrop      = Hex("#1A1410F2");   // full-screen menu backdrop (lobby)
    public static readonly Color InputWell     = Hex("#1A1410");     // text-entry well
    public static readonly Color Placeholder   = Hex("#CDBFB280");   // input placeholder text
    /// <summary>Not a UI accent: matches the player's blue body so face previews read as on the character.</summary>
    public static readonly Color FacePreview   = Hex("#3399FF");

    /// <summary>Meters at or below this ratio blend toward <see cref="Danger"/>.</summary>
    public const float LowRatio = 0.3f;

    // ── Derived interaction colours ──────────────────────────────────────────
    public static Color Hover(Color c) => Color.Lerp(c, Color.white, 0.16f);
    public static Color Pressed(Color c) => Color.Lerp(c, Color.black, 0.22f);
    public static Color Disabled(Color c) { Color d = Color.Lerp(c, Track, 0.65f); d.a = 0.75f; return d; }

    // ── Shapes ───────────────────────────────────────────────────────────────
    /// <summary>Large panel: 14 px chamfer, 2 px darker rim.</summary>
    public static Sprite PanelSprite => ProceduralUIUtility.GetChamferSprite(14, 2);
    /// <summary>Buttons, tracks, small cards: 7 px chamfer, 2 px darker rim.</summary>
    public static Sprite SmallSprite => ProceduralUIUtility.GetChamferSprite(7, 2);
    /// <summary>Bar fills: 4 px chamfer, no rim.</summary>
    public static Sprite FillSprite => ProceduralUIUtility.GetChamferSprite(4, 0);

    // ── Font ─────────────────────────────────────────────────────────────────
    private static Font _font;
    /// <summary>GameThai (Sarabun) so Thai renders; builtin LegacyRuntime/Arial if missing.</summary>
    public static Font Font
    {
        get
        {
            if (_font != null) return _font;
            _font = Resources.Load<Font>("Fonts/GameThai");
            if (_font == null) _font = ProceduralUIUtility.GetDefaultFont();
            return _font;
        }
    }

    // ── Colour lookups ───────────────────────────────────────────────────────
    public static Color ButtonColor(UIButtonStyle style) =>
        style == UIButtonStyle.Primary ? Primary : style == UIButtonStyle.Danger ? Danger : Track;

    public static Color ButtonTextColor(UIButtonStyle style) =>
        style == UIButtonStyle.Secondary ? TextPrimary : OnPrimary;

    public static Color TextColor(UITextRole role)
    {
        switch (role)
        {
            case UITextRole.Secondary: return TextSecondary;
            case UITextRole.Accent:    return Primary;
            case UITextRole.OnPrimary: return OnPrimary;
            case UITextRole.Positive:  return Stamina;
            case UITextRole.Danger:    return Danger;
            default:                   return TextPrimary;
        }
    }

    /// <summary>Meter colour: <paramref name="normal"/> at or above <see cref="LowRatio"/>, Danger below.
    /// A hard step (no blend) so green never muddies through brown on the way to red-orange.</summary>
    public static Color Meter(float ratio, Color normal) => ratio >= LowRatio ? normal : Danger;

    // ── Stylers (restyle existing Graphics; layout untouched) ────────────────
    public static void StylePanel(Image image, bool small = false) => StyleShape(image, small ? SmallSprite : PanelSprite, Panel);

    public static void StyleShape(Image image, Sprite sprite, Color color)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = color;
    }

    public static void StyleTrack(Image image) => StyleShape(image, SmallSprite, Track);

    public static void StyleFill(Image image, Color color) => StyleShape(image, FillSprite, color);

    public static void StyleText(Text text, UITextRole role)
    {
        if (text == null) return;
        text.font = Font;
        text.color = TextColor(role);
    }

    /// <summary>Chamfered shape + palette ColorBlock (hover/press/disabled derived from the role colour).
    /// The Image is white so the ColorBlock tint is the exact theme colour.</summary>
    public static void StyleButton(Button button, UIButtonStyle style)
    {
        if (button == null) return;
        Color baseColor = ButtonColor(style);
        if (button.targetGraphic is Image image) StyleShape(image, SmallSprite, Color.white);
        else if (button.targetGraphic != null) button.targetGraphic.color = Color.white;

        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = Hover(baseColor);
        colors.selectedColor = baseColor;
        colors.pressedColor = Pressed(baseColor);
        colors.disabledColor = Disabled(baseColor);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Color label = ButtonTextColor(style);
        foreach (Text text in button.GetComponentsInChildren<Text>(true))
        {
            text.font = Font;
            text.color = label;
        }
    }

    public static void StyleSlider(Slider slider)
    {
        if (slider == null) return;
        Transform background = slider.transform.Find("Background");
        if (background != null) StyleTrack(background.GetComponent<Image>());
        if (slider.fillRect != null) StyleFill(slider.fillRect.GetComponent<Image>(), Primary);
        if (slider.handleRect != null) StyleShape(slider.handleRect.GetComponent<Image>(), SmallSprite, TextPrimary);
        ColorBlock colors = slider.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = TextSecondary;
        colors.selectedColor = Color.white;
        colors.pressedColor = Pressed(Color.white);
        slider.colors = colors;
    }

    public static void StyleToggle(Toggle toggle)
    {
        if (toggle == null) return;
        if (toggle.targetGraphic is Image box) StyleTrack(box);
        if (toggle.graphic is Image check) StyleFill(check, Primary);
        ColorBlock colors = toggle.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Hover(Color.white);
        colors.selectedColor = Color.white;
        colors.pressedColor = Pressed(Color.white);
        toggle.colors = colors;
    }

    // ── Builders (for new code) ──────────────────────────────────────────────
    public static Image MakePanel(Transform parent, string name, Vector2 size, bool small = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;
        Image image = go.GetComponent<Image>();
        StylePanel(image, small);
        image.raycastTarget = false;
        return image;
    }

    public static Text MakeText(Transform parent, string name, string value, int fontSize, UITextRole role,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        StyleText(text, role);
        text.fontSize = fontSize;
        text.text = value;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    public static Button MakeButton(Transform parent, string name, string label, UIButtonStyle style,
        Vector2 size, int fontSize = 22)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        go.AddComponent<UIButtonHover>();
        Text text = MakeText(go.transform, "Label", label, fontSize, UITextRole.Primary, TextAnchor.MiddleCenter);
        ProceduralUIUtility.StretchFull(text.rectTransform);
        StyleButton(button, style);
        return button;
    }

    /// <summary>Track (secondary, chamfered) with a left-anchored fill child; drive the fill with
    /// <c>fill.rectTransform.anchorMax = new Vector2(ratio, 1)</c>.</summary>
    public static Image MakeBar(Transform parent, string name, Vector2 size, Color fillColor, out Image fill, float inset = 3f)
    {
        var track = new GameObject(name, typeof(RectTransform), typeof(Image));
        track.transform.SetParent(parent, false);
        track.GetComponent<RectTransform>().sizeDelta = size;
        Image trackImage = track.GetComponent<Image>();
        StyleTrack(trackImage);
        trackImage.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(track.transform, false);
        RectTransform rect = fillGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        fill = fillGo.GetComponent<Image>();
        StyleFill(fill, fillColor);
        fill.raycastTarget = false;
        return trackImage;
    }

    /// <summary>Sets a sliced bar fill's width ratio (left anchored); hides it when too thin to draw.</summary>
    public static void SetBarFill(Image fill, float ratio)
    {
        if (fill == null) return;
        ratio = Mathf.Clamp01(ratio);
        RectTransform rect = fill.rectTransform;
        rect.anchorMax = new Vector2(ratio, rect.anchorMax.y);
        bool visible = ratio > 0.001f && rect.rect.width > 0.5f;
        if (fill.enabled != visible) fill.enabled = visible;
    }

    /// <summary>Themed input well: chamfered dark field, white text, muted placeholder.</summary>
    public static void StyleInputField(InputField field)
    {
        if (field == null) return;
        if (field.targetGraphic is Image image) StyleShape(image, SmallSprite, InputWell);
        if (field.textComponent != null) StyleText(field.textComponent, UITextRole.Primary);
        if (field.placeholder is Text placeholder) { placeholder.font = Font; placeholder.color = Placeholder; }
        field.selectionColor = new Color(Primary.r, Primary.g, Primary.b, 0.45f);
        field.caretColor = Primary;
        field.customCaretColor = true;
        ColorBlock colors = field.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Hover(Color.white);
        colors.selectedColor = Color.white;
        colors.pressedColor = Color.white;
        field.colors = colors;
    }

    // ── Glyphs ───────────────────────────────────────────────────────────────
    // Colour emoji (outside the BMP) do not render in GameThai/Sarabun or its fallbacks; these BMP
    // symbols do (verified in Play Mode alongside ▶ ⚙ ✕ ✓ ←).
    private static readonly string[,] _emojiSwaps =
    {
        { "📋 ", "" }, { "📋", "" },      // clipboard: label text already says Copy/Paste
        { "👑", "★" },                     // host
        { "🔑", "→" },                     // join
        { "🎮", "▶" },                     // play / host room
    };

    /// <summary>Replaces emoji the theme font cannot draw with renderable symbols (or drops them).</summary>
    public static string ReplaceEmoji(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        for (int i = 0; i < _emojiSwaps.GetLength(0); i++)
            value = value.Replace(_emojiSwaps[i, 0], _emojiSwaps[i, 1]);
        return value;
    }

    /// <summary>Applies <see cref="ReplaceEmoji"/> to every Text under <paramref name="root"/> (scene-serialized labels).</summary>
    public static void ReplaceEmojiIn(Transform root)
    {
        if (root == null) return;
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
            text.text = ReplaceEmoji(text.text);
    }

    // ── Layout helpers ───────────────────────────────────────────────────────
    /// <summary>LayoutElement preferred height (-1 = let the content decide); never flexible.</summary>
    public static void SetPreferredHeight(GameObject go, float height)
    {
        if (go == null) return;
        var element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    /// <summary>
    /// Info card (session / room code): top-left pivot, fixed <paramref name="width"/>, standard padding,
    /// height fitted to its VerticalLayoutGroup content.
    /// </summary>
    public static void CompactCard(RectTransform card, float width)
    {
        if (card == null) return;
        Vector2 topLeft = card.anchoredPosition;
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = topLeft;
        card.sizeDelta = new Vector2(width, card.sizeDelta.y);
        var layout = card.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(CardPadX, CardPadX, CardPadTop, CardPadBottom);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        FitHeightToContent(card.gameObject);
    }

    /// <summary>Card height follows its layout's preferred height (no empty bands).</summary>
    public static void FitHeightToContent(GameObject card)
    {
        var fitter = card.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = card.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    /// <summary>Header row of an info card: children share the width (title left, status right).</summary>
    public static void SpreadRow(Transform row, float height)
    {
        if (row == null) return;
        SetPreferredHeight(row.gameObject, height);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
        foreach (Transform child in row)
            if (child.TryGetComponent(out LayoutElement element)) { element.preferredWidth = -1f; element.flexibleWidth = 1f; }
    }

    public const int CardPadX = 20, CardPadTop = 14, CardPadBottom = 16;
    /// <summary>Smallest label size on info cards (≈11 px at 720p).</summary>
    public const int CardLabelFontSize = 16;

    /// <summary>"#RRGGBB" for IMGUI rich-text colour tags.</summary>
    public static string RichHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    // ── IMGUI (legacy OnGUI screens) ─────────────────────────────────────────
    // Styles are created lazily inside OnGUI (GUI.skin is only valid there) and cached.
    private static readonly System.Collections.Generic.Dictionary<long, Texture2D> _imguiTextures =
        new System.Collections.Generic.Dictionary<long, Texture2D>();
    private static GUIStyle _imguiBox, _imguiLabel, _imguiField;
    private static readonly GUIStyle[] _imguiButtons = new GUIStyle[3];

    /// <summary>The chamfer sprite baked to a solid colour (IMGUI cannot tint per style).</summary>
    public static Texture2D ImguiTexture(Color color, bool small)
    {
        long key = ((long)ColorUtility.ToHtmlStringRGBA(color).GetHashCode() << 1) | (small ? 1L : 0L);
        if (_imguiTextures.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
        Texture2D source = (small ? SmallSprite : PanelSprite).texture;
        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++) pixels[i] *= color;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(pixels);
        texture.Apply();
        _imguiTextures[key] = texture;
        return texture;
    }

    private static RectOffset ImguiBorder(bool small)
    {
        int b = Mathf.RoundToInt((small ? SmallSprite : PanelSprite).border.x);
        return new RectOffset(b, b, b, b);
    }

    public static GUIStyle ImguiBox
    {
        get
        {
            if (_imguiBox != null) return _imguiBox;
            _imguiBox = new GUIStyle(GUI.skin.box) { border = ImguiBorder(false), padding = new RectOffset(18, 18, 14, 14), font = Font, richText = true };
            _imguiBox.normal.background = ImguiTexture(Panel, false);
            _imguiBox.normal.textColor = TextPrimary;
            return _imguiBox;
        }
    }

    public static GUIStyle ImguiLabel
    {
        get
        {
            if (_imguiLabel != null) return _imguiLabel;
            _imguiLabel = new GUIStyle(GUI.skin.label) { font = Font, fontSize = 15, richText = true, wordWrap = true };
            _imguiLabel.normal.textColor = TextPrimary;
            return _imguiLabel;
        }
    }

    public static GUIStyle ImguiTextField
    {
        get
        {
            if (_imguiField != null) return _imguiField;
            _imguiField = new GUIStyle(GUI.skin.textField) { border = ImguiBorder(true), font = Font, fontSize = 18, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 4, 4) };
            _imguiField.normal.background = _imguiField.hover.background = _imguiField.focused.background = ImguiTexture(InputWell, true);
            _imguiField.normal.textColor = _imguiField.hover.textColor = _imguiField.focused.textColor = TextPrimary;
            return _imguiField;
        }
    }

    public static GUIStyle ImguiButton(UIButtonStyle style)
    {
        int i = (int)style;
        if (_imguiButtons[i] != null) return _imguiButtons[i];
        Color baseColor = ButtonColor(style);
        var s = new GUIStyle(GUI.skin.button) { border = ImguiBorder(true), font = Font, fontSize = 16, richText = true, alignment = TextAnchor.MiddleCenter };
        s.normal.background = s.focused.background = ImguiTexture(baseColor, true);
        s.hover.background = ImguiTexture(Hover(baseColor), true);
        s.active.background = ImguiTexture(Pressed(baseColor), true);
        s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = ButtonTextColor(style);
        _imguiButtons[i] = s;
        return s;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
