using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Button roles of the UI kit theme.</summary>
public enum UIButtonStyle { Primary, Secondary, Danger, Pink, Blue, White, WhiteShort, Green }

/// <summary>Text roles of the UI kit theme. OnLight is dark text for beige panels.</summary>
public enum UITextRole { Primary, Secondary, Accent, OnPrimary, Positive, Danger, OnLight }

/// <summary>
/// Single source of the UI look: the user's own design kit (Assets/Art/UI, sprites built by
/// Tools/UI Kit/Rebuild Sprites into Assets/Art/UI/Resources/UIKit). Pink primary / light-pink
/// secondary buttons with dark outline, dark translucent rounded panels, beige panels with a darker
/// top band, red mission/danger, yellow stamina, round player faces, card backs and icons.
/// Screens read colours, sprites and the font only from here.
/// </summary>
public static class UITheme
{
    // ── Palette (sampled from the kit art) ───────────────────────────────────
    public static readonly Color Panel         = Color.white;           // kit panels carry their own colour/alpha
    public static readonly Color PanelSolid    = Color.white;
    public static readonly Color Track         = Hex("#313336");        // stamina-bar frame grey: dividers, tracks
    public static readonly Color Primary       = Hex("#FC6788");        // pink primary (Selected Button)
    public static readonly Color PrimaryLight  = Hex("#F2D5D9");        // light pink secondary (Button Long)
    public static readonly Color OnPrimary     = Hex("#1A0E12");        // dark text on pink buttons
    public static readonly Color Stamina       = Hex("#F3B600");        // yellow stamina fill (card yellow)
    public static readonly Color Positive      = Hex("#7EDC95");        // online / copied status
    public static readonly Color Danger        = Hex("#FF4854");        // red: mission panel, low HP, exhaustion
    public static readonly Color MissionRed    = Hex("#E8434E");        // mission panel fill
    public static readonly Color Beige         = Hex("#E5C8A3");        // Settings panel body
    public static readonly Color OnLight       = Hex("#3B2618");        // dark brown text on beige
    public static readonly Color Neon          = Hex("#FF00E4");        // selected-card glow
    public static readonly Color TextPrimary   = Hex("#FFFFFF");
    public static readonly Color TextSecondary = Hex("#E9DCDF");
    public static readonly Color Outline       = Hex("#1A1214E6");      // text outline (dark)
    public static readonly Color Overlay       = Hex("#00000073");      // dim behind modals (game shows through)
    public static readonly Color Ghost         = Hex("#FFFFFF59");      // trailing "recent loss" bar
    public static readonly Color Backdrop      = Color.white;           // lobby backdrop uses the BackdropBlur art
    public static readonly Color InputWell     = Hex("#1E1A1CE0");
    public static readonly Color Placeholder   = Hex("#E9DCDF80");
    /// <summary>Not a UI accent: matches the player's blue body so face previews read as on the character.</summary>
    public static readonly Color FacePreview   = Hex("#2D7DF5");

    // Cute kit (Canva pages 64-74): beige cards, navy text, wooden plank titles.
    public static readonly Color CuteInk       = Hex("#2E3B73");        // navy text on beige / white buttons
    public static readonly Color CuteInkSoft   = Hex("#6F75A8");        // secondary text on beige
    public static readonly Color CutePlankText = Hex("#FFF7EE");        // cream title on the wooden plank
    public static readonly Color CuteAccent    = Hex("#3F8CFF");        // spinner / highlights
    public static readonly Color CuteNavyPanel = Hex("#1F2C47EB");      // card view info panel
    public static readonly Color CuteWell      = Hex("#2C3E73");        // room-code input well

    /// <summary>Meters below this ratio switch to <see cref="Danger"/>.</summary>
    public const float LowRatio = 0.3f;

    // ── Derived interaction colours (kit sprites are full colour, so tints only darken) ──
    public static Color Hover(Color c) => new Color(c.r * 0.93f, c.g * 0.93f, c.b * 0.93f, c.a);
    public static Color Pressed(Color c) => new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, c.a);
    public static Color Disabled(Color c) => new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, c.a * 0.6f);

    // ── Kit sprites ──────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Sprite> _kit = new Dictionary<string, Sprite>();

    /// <summary>Loads a kit sprite from Resources/UIKit (cached). Null if missing.</summary>
    public static Sprite Kit(string name)
    {
        if (_kit.TryGetValue(name, out Sprite cached) && cached != null) return cached;
        Sprite sprite = Resources.Load<Sprite>("UIKit/" + name);
        if (sprite == null) Debug.LogWarning("[UITheme] Missing kit sprite UIKit/" + name + " (run Tools/UI Kit/Rebuild Sprites)");
        _kit[name] = sprite;
        return sprite;
    }

    /// <summary>Dark translucent rounded panel (UI.png).</summary>
    public static Sprite PanelSprite => Kit("PanelDark");
    /// <summary>Beige panel with darker top band (Settings.png).</summary>
    public static Sprite BeigeSprite => Kit("PanelBeige");
    public static Sprite ButtonPrimarySprite => Kit("ButtonPrimary");
    public static Sprite ButtonSecondarySprite => Kit("ButtonSecondary");
    public static Sprite ButtonCustomizePinkSprite => Kit("ButtonCustomizePink");
    public static Sprite ButtonCustomizePurpleSprite => Kit("ButtonCustomizePurple");
    public static Sprite CardFrameSprite => Kit("CardSelectedFrame");
    public static Sprite StaminaFrameSprite => Kit("StaminaFrame");
    public static Sprite StaminaInnerSprite => Kit("StaminaInner");
    public static Sprite KeyCapSprite => Kit("KeyCap");
    public static Sprite BackdropSprite => Kit("BackdropBlur");
    public static Sprite IconCarrier => Kit("IconCarrier");
    public static Sprite IconStrength => Kit("IconStrength");
    public static Sprite IconCube => Kit("IconCube");
    public static Sprite IconCargoHud => Kit("IconCargoHud");
    // Cute kit sprites (Tools/UI Kit/Rebuild Cute Sprites, sources in Assets/ThirdParty/UI เพิ่ม).
    public static Sprite CuteCardSprite => Kit("CuteCard");
    public static Sprite CuteCardWideSprite => Kit("CuteCardWide");
    public static Sprite CutePlankSprite => Kit("CutePlank");
    public static Sprite CuteRoomCodeSprite => Kit("CuteRoomCodeBlock");
    public static Sprite CutePlayerListSprite => Kit("CutePlayerList");
    public static Sprite CutePlayerRowSprite(bool you) => Kit(you ? "CutePlayerRowYou" : "CutePlayerRow");
    public static Sprite CutePlayerIcon(bool joined) => Kit(joined ? "CuteIconJoined" : "CuteIconWaiting");
    /// <summary>Tape strips 1-7 as drawn in the kit.</summary>
    public static Sprite CuteTape(int index) => Kit("CuteTape" + Mathf.Clamp(index, 1, 7));

    /// <summary>Tintable rounded shape with a darker rim (chips, tracks, banners, tooltips).</summary>
    public static Sprite SmallSprite => ProceduralUIUtility.GetRoundedSprite(10, 2, 0.55f);
    /// <summary>Tintable rounded fill without rim (bar fills).</summary>
    public static Sprite FillSprite => ProceduralUIUtility.GetRoundedSprite(6, 0);

    /// <summary>Stamina frame window in sprite pixels: fill area inside the frame (left cap holds the icon).</summary>
    public static readonly RectOffset StaminaWindow = new RectOffset(52, 13, 9, 9);

    // Face per player colour. NetworkPlayer tints players by OwnerClientId % 4:
    // 0 sky blue, 1 coral red, 2 emerald green, 3 amber -> matching kit faces (no yellow face in the kit;
    // brown is the closest to amber).
    private static readonly string[] PlayerFaces = { "FaceBlue", "FaceCrimson", "FaceGreen", "FaceBrown" };
    /// <summary>All kit faces (grey, purple, blue, teal, green, lime, brown, crimson).</summary>
    public static readonly string[] AllFaces = { "FaceGrey", "FacePurple", "FaceBlue", "FaceTeal", "FaceGreen", "FaceLime", "FaceBrown", "FaceCrimson" };

    /// <summary>Round face icon matching the player's body colour (same rule as NetworkPlayer, so all peers agree).</summary>
    public static Sprite FaceForClient(ulong ownerClientId) => Kit(PlayerFaces[(int)(ownerClientId % (ulong)PlayerFaces.Length)]);

    /// <summary>Card icon: the illustration area of the card's art; 0 (no card yet) = a plain card back.</summary>
    public static Sprite CardIcon(byte card) => CoopGame.Network.CardCatalog.Icon(card) ?? Kit("CardBackYellow");

    /// <summary>Decorative card backs for the card-select banner (1 red, 2 blue, otherwise yellow).</summary>
    public static Sprite CardBack(byte card) =>
        card == 1 ? Kit("CardBackRed") : card == 2 ? Kit("CardBackBlue") : Kit("CardBackYellow");

    // ── Font ─────────────────────────────────────────────────────────────────
    // Mitr (rounded Thai, SIL OFL, Assets/Art/Fonts/Mitr) in real weights via Resources/UIFontSet;
    // GameThai (Sarabun) then the builtin font are fallbacks. Never use FontStyle.Bold (fake bold).
    private static UIFontSet _fontSet;
    private static bool _fontSetLoaded;
    private static Font _fallback;
    private static readonly Dictionary<Font, TMP_FontAsset> _tmpFonts = new Dictionary<Font, TMP_FontAsset>();
    private static TMP_FontAsset _thaiTmpFont;
    private static bool _thaiTmpFontLoaded;

    private static Font Weight(System.Func<UIFontSet, Font> pick)
    {
        if (!_fontSetLoaded)
        {
            _fontSetLoaded = true;
            _fontSet = Resources.Load<UIFontSet>("UIFontSet");
            if (_fontSet == null) Debug.LogWarning("[UITheme] Resources/UIFontSet missing; falling back to GameThai.");
        }
        Font font = _fontSet != null ? pick(_fontSet) : null;
        if (font == null && _fontSet != null) font = _fontSet.Regular;
        if (font != null) return font;
        if (_fallback == null) _fallback = Resources.Load<Font>("Fonts/GameThai");
        if (_fallback == null) _fallback = ProceduralUIUtility.GetDefaultFont();
        return _fallback;
    }

    /// <summary>Body text / labels (Mitr Regular).</summary>
    public static Font Font => Weight(s => s.Regular);
    /// <summary>Buttons (Mitr Medium).</summary>
    public static Font FontMedium => Weight(s => s.Medium);
    /// <summary>Panel titles, names (Mitr SemiBold).</summary>
    public static Font FontSemiBold => Weight(s => s.SemiBold);
    /// <summary>Big headers: PAUSE, banners, HP number (Mitr Bold).</summary>
    public static Font FontBold => Weight(s => s.Bold);

    /// <summary>Runtime SDF font for small Thai labels where legacy UI.Text cannot apply Thai mark positioning reliably.</summary>
    public static TMP_FontAsset TmpFont
    {
        get { return GetTmpFont(Font); }
    }

    /// <summary>Returns one cached dynamic TMP font per legacy font weight.</summary>
    public static TMP_FontAsset GetTmpFont(Font source)
    {
        if (!_thaiTmpFontLoaded)
        {
            _thaiTmpFontLoaded = true;
            _thaiTmpFont = Resources.Load<TMP_FontAsset>("Fonts/MitrThai SDF");
            if (_thaiTmpFont == null)
                Debug.LogWarning("[UITheme] Fonts/MitrThai SDF missing; using an uncorrected runtime TMP font.");
        }
        if (_thaiTmpFont != null) return _thaiTmpFont;

        if (source == null) source = Font;
        if (_tmpFonts.TryGetValue(source, out TMP_FontAsset cached) && cached != null) return cached;

        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source);
        if (asset != null)
        {
            asset.name = source.name + " Runtime SDF";
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            _tmpFonts[source] = asset;
        }
        return asset;
    }

    /// <summary>Applies a real font weight (no synthesized bold).</summary>
    public static void SetWeight(Text text, Font weight)
    {
        if (text == null) return;
        text.font = weight;
        text.fontStyle = FontStyle.Normal;
    }

    // ── Colour lookups ───────────────────────────────────────────────────────
    /// <summary>Tint applied to the button sprite (kit sprites are already coloured, so mostly white).</summary>
    public static Color ButtonColor(UIButtonStyle style) => style == UIButtonStyle.Danger ? Danger : Color.white;

    public static Color ButtonTextColor(UIButtonStyle style)
    {
        switch (style)
        {
            case UIButtonStyle.Danger:
            case UIButtonStyle.Pink:
            case UIButtonStyle.Blue:
            case UIButtonStyle.Green: return TextPrimary;
            case UIButtonStyle.White:
            case UIButtonStyle.WhiteShort: return CuteInk;
            default: return OnPrimary;
        }
    }

    /// <summary>True for the glossy cute-kit buttons (drop shadow under the art).</summary>
    public static bool IsCute(UIButtonStyle style) => style >= UIButtonStyle.Pink;

    public static Sprite ButtonSprite(UIButtonStyle style)
    {
        switch (style)
        {
            case UIButtonStyle.Primary: return ButtonPrimarySprite;
            case UIButtonStyle.Pink: return Kit("CuteButtonPink");
            case UIButtonStyle.Blue: return Kit("CuteButtonBlue");
            case UIButtonStyle.White: return Kit("CuteButtonWhite");
            case UIButtonStyle.WhiteShort: return Kit("CuteButtonWhiteShort");
            case UIButtonStyle.Green: return Kit("CuteButtonGreen");
            default: return ButtonSecondarySprite;
        }
    }

    public static Color TextColor(UITextRole role)
    {
        switch (role)
        {
            case UITextRole.Secondary: return TextSecondary;
            case UITextRole.Accent:    return Primary;
            case UITextRole.OnPrimary: return OnPrimary;
            case UITextRole.Positive:  return Positive;
            case UITextRole.Danger:    return Danger;
            case UITextRole.OnLight:   return OnLight;
            default:                   return TextPrimary;
        }
    }

    /// <summary>Meter colour: <paramref name="normal"/> at or above <see cref="LowRatio"/>, Danger below.</summary>
    public static Color Meter(float ratio, Color normal) => ratio >= LowRatio ? normal : Danger;

    // ── Stylers (restyle existing Graphics) ──────────────────────────────────
    /// <summary>Dark translucent kit panel; <paramref name="small"/> = compact rounded dark chip.</summary>
    public static void StylePanel(Image image, bool small = false)
    {
        if (small) StyleShape(image, SmallSprite, Hex("#1E1A1CD9"));
        else StyleShape(image, PanelSprite, Color.white);
    }

    /// <summary>Near-opaque dark rounded panel for content-dense dialogs (the kit panel is ~65% see-through).</summary>
    public static void StyleSolidPanel(Image image) => StyleShape(image, SmallSprite, SolidPanel);
    public static readonly Color SolidPanel = Hex("#262124F2");

    /// <summary>Beige kit panel (darker top band); use <see cref="UITextRole.OnLight"/> text on it.</summary>
    public static void StyleBeigePanel(Image image) => StyleShape(image, BeigeSprite, Color.white);

    /// <summary>Full-screen blurred level art used behind the lobby menu.</summary>
    public static void StyleBackdrop(Image image)
    {
        if (image == null) return;
        image.sprite = BackdropSprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = Color.white;
    }

    public static void StyleShape(Image image, Sprite sprite, Color color)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = color;
    }

    /// <summary>Icon art (faces, card icons): simple, aspect preserved.</summary>
    public static void StyleIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    public static void StyleTrack(Image image) => StyleShape(image, FillSprite, Hex("#313336D9"));

    public static void StyleFill(Image image, Color color) => StyleShape(image, FillSprite, color);

    public static void StyleText(Text text, UITextRole role)
    {
        if (text == null) return;
        text.font = Font;
        text.fontStyle = FontStyle.Normal; // regular weight only (no Bold file in the project)
        text.color = TextColor(role);
        ThaiTextMeshProBridge.Attach(text);
    }

    /// <summary>Dark outline around text (kit titles: white text with dark outline).</summary>
    public static void AddOutline(Text text, float distance = 1f)
    {
        if (text == null) return;
        var outline = text.GetComponent<Outline>();
        if (outline == null) outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Outline;
        distance = Mathf.Min(distance, 1f); // at most 1 px: thicker outlines blur Thai glyphs
        outline.effectDistance = new Vector2(distance, -distance);
    }

    /// <summary>Kit button sprite (pink / light pink, dark outline) + ColorBlock that darkens on hover/press.</summary>
    public static void StyleButton(Button button, UIButtonStyle style)
    {
        if (button == null) return;
        Color baseColor = ButtonColor(style);
        Sprite sprite = ButtonSprite(style);
        if (button.targetGraphic is Image image) StyleShape(image, sprite, Color.white);
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
            text.font = FontMedium;
            text.color = label;
            text.fontStyle = FontStyle.Normal; // only regular Sarabun ships; synthesized bold smears
            // Cute buttons carry a ~6 px drop shadow at the bottom: centre the label on the face, not the art.
            RectTransform rect = text.rectTransform;
            if (rect.parent == button.transform && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one)
                rect.offsetMin = new Vector2(rect.offsetMin.x, IsCute(style) ? 6f : 0f);
        }
    }

    // ── Cute kit builders (Canva pages 64-74) ────────────────────────────────
    /// <summary>Beige card; <paramref name="wide"/> = the wide arched card (card select, customization).</summary>
    public static void StyleCuteCard(Image image, bool wide = false)
    {
        if (image == null) return;
        if (wide)
        {
            image.sprite = CuteCardWideSprite;
            image.type = Image.Type.Simple; // the arched top would flatten if sliced
            image.preserveAspect = false;
            image.color = Color.white;
        }
        else StyleShape(image, CuteCardSprite, Color.white);
    }

    /// <summary>
    /// Wooden plank title centred on the card's top edge (ignored by layouts). Returns its label.
    /// <paramref name="drop"/> is how far the plank centre sits below the card's top edge.
    /// </summary>
    public static Text MakeCutePlank(Transform card, string title, float width = 300f, float height = 84f, float drop = 4f, int fontSize = 30)
    {
        var go = new GameObject("CutePlank", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(card, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -drop);
        rect.sizeDelta = new Vector2(width, height);
        Image image = go.GetComponent<Image>();
        StyleShape(image, CutePlankSprite, Color.white);
        image.raycastTarget = false;
        Text text = MakeText(go.transform, "Title", title, fontSize, UITextRole.Primary, TextAnchor.MiddleCenter);
        ProceduralUIUtility.StretchFull(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(16f, 8f); // the plank art has a shadow under it
        text.rectTransform.offsetMax = new Vector2(-16f, -2f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        SetWeight(text, FontSemiBold);
        text.color = CutePlankText;
        return text;
    }

    /// <summary>Decorative tape strip (ignored by layouts), anchored on <paramref name="anchor"/> of the parent.</summary>
    public static Image AddCuteTape(Transform parent, int tape, Vector2 anchor, Vector2 position, float angle, float size = 72f)
    {
        var go = new GameObject("Tape" + tape, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, size * 48f / 51f);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = go.GetComponent<Image>();
        StyleIcon(image, CuteTape(tape));
        return image;
    }

    /// <summary>
    /// Clips <paramref name="art"/> to a rounded rectangle (card illustrations have square corners): inserts a
    /// mask parent at the art's rect and stretches the art inside it. <paramref name="radius"/> in sprite pixels.
    /// </summary>
    public static void RoundCorners(Image art, int radius = 26)
    {
        if (art == null || art.transform.parent == null || art.transform.parent.name == "RoundMask") return;
        RectTransform rect = art.rectTransform;
        var go = new GameObject("RoundMask", typeof(RectTransform), typeof(Image), typeof(Mask));
        go.transform.SetParent(rect.parent, false);
        go.transform.SetSiblingIndex(rect.GetSiblingIndex());
        var mask = (RectTransform)go.transform;
        mask.anchorMin = rect.anchorMin; mask.anchorMax = rect.anchorMax; mask.pivot = rect.pivot;
        mask.anchoredPosition = rect.anchoredPosition; mask.sizeDelta = rect.sizeDelta;
        Image shape = go.GetComponent<Image>();
        StyleShape(shape, ProceduralUIUtility.GetRoundedSprite(radius, 0), Color.white);
        shape.raycastTarget = false;
        go.GetComponent<Mask>().showMaskGraphic = false;
        rect.SetParent(go.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        art.preserveAspect = false; // the mask rect already has the card's 640:904 shape
    }

    /// <summary>Text on a beige card: navy (<paramref name="soft"/> = muted secondary line).</summary>
    public static void StyleCuteText(Text text, bool soft = false)
    {
        if (text == null) return;
        StyleText(text, UITextRole.OnLight);
        text.color = soft ? CuteInkSoft : CuteInk;
        var outline = text.GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
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
        ThaiTextMeshProBridge.Attach(text);
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
        { "👑", "" }, { "🔑", "" }, { "🎮", "" },
        { "▶", "" }, { "⚙", "" }, { "✕", "" }, { "✓", "" },
        { "←", "" }, { "→", "" }, { "★", "" },
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
    private static readonly GUIStyle[] _imguiButtons = new GUIStyle[System.Enum.GetValues(typeof(UIButtonStyle)).Length];

    /// <summary>The tintable rounded shape baked to a solid colour (IMGUI cannot tint per style).</summary>
    public static Texture2D ImguiTexture(Color color, bool small)
    {
        long key = ((long)ColorUtility.ToHtmlStringRGBA(color).GetHashCode() << 1) | (small ? 1L : 0L);
        if (_imguiTextures.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
        Texture2D source = (small ? SmallSprite : FillSprite).texture; // procedural, always readable
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

    private static RectOffset BorderOf(Sprite sprite)
    {
        if (sprite == null) return new RectOffset(8, 8, 8, 8);
        Vector4 b = sprite.border; // L, B, R, T
        return new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
    }

    /// <summary>Kit dark panel texture for IMGUI boxes/chips.</summary>
    public static Texture2D ImguiPanelTexture => PanelSprite != null ? PanelSprite.texture : ImguiTexture(InputWell, true);
    public static RectOffset ImguiPanelBorder => BorderOf(PanelSprite);

    public static GUIStyle ImguiBox
    {
        get
        {
            if (_imguiBox != null) return _imguiBox;
            _imguiBox = new GUIStyle(GUI.skin.box) { border = ImguiPanelBorder, padding = new RectOffset(22, 22, 18, 18), font = Font, richText = true };
            _imguiBox.normal.background = ImguiPanelTexture;
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
            _imguiField = new GUIStyle(GUI.skin.textField) { border = BorderOf(SmallSprite), font = Font, fontSize = 18, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 4, 4) };
            _imguiField.normal.background = _imguiField.hover.background = _imguiField.focused.background = ImguiTexture(InputWell, true);
            _imguiField.normal.textColor = _imguiField.hover.textColor = _imguiField.focused.textColor = TextPrimary;
            return _imguiField;
        }
    }

    /// <summary>Kit button textures (pink / light pink). Danger uses the light-pink art with red text.</summary>
    public static GUIStyle ImguiButton(UIButtonStyle style)
    {
        int i = (int)style;
        if (_imguiButtons[i] != null) return _imguiButtons[i];
        Sprite sprite = ButtonSprite(style);
        var s = new GUIStyle(GUI.skin.button) { border = BorderOf(sprite), font = FontMedium, fontSize = 16, richText = true, alignment = TextAnchor.MiddleCenter };
        Texture2D texture = sprite != null ? sprite.texture : ImguiTexture(Primary, true);
        s.normal.background = s.focused.background = s.hover.background = s.active.background = texture;
        Color text = style == UIButtonStyle.Danger ? Danger : OnPrimary;
        s.normal.textColor = s.focused.textColor = text;
        s.hover.textColor = s.active.textColor = style == UIButtonStyle.Danger ? Pressed(Danger) : Hex("#5A2633");
        _imguiButtons[i] = s;
        return s;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
