using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps legacy UI.Text as the data/input contract, while TextMeshPro renders it.
/// This fixes Thai vowel and tone-mark positioning without breaking serialized Text references.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class ThaiTextMeshProBridge : MonoBehaviour
{
    private const string MirrorName = "__TMP Thai Renderer";

    private Text _source;
    private TextMeshProUGUI _mirror;
    private string _text;
    private Font _font;
    private int _fontSize;
    private FontStyle _fontStyle;
    private Color _color;
    private TextAnchor _alignment;
    private float _lineSpacing;
    private bool _richText;
    private bool _bestFit;
    private int _bestFitMin;
    private int _bestFitMax;
    private HorizontalWrapMode _horizontalOverflow;
    private VerticalWrapMode _verticalOverflow;
    private bool _sourceEnabled;

    public static void Attach(Text text)
    {
        if (text == null || text.GetComponent<ThaiTextMeshProBridge>() != null) return;
        text.gameObject.AddComponent<ThaiTextMeshProBridge>();
    }

    private void Awake()
    {
        _source = GetComponent<Text>();
        EnsureMirror();
        Sync(true);
    }

    private void OnEnable()
    {
        EnsureMirror();
        Sync(true);
    }

    private void LateUpdate()
    {
        Sync(false);
    }

    private void EnsureMirror()
    {
        if (_source == null) _source = GetComponent<Text>();
        if (_mirror != null || _source == null) return;

        Transform existing = transform.Find(MirrorName);
        GameObject mirrorObject;
        if (existing != null)
        {
            mirrorObject = existing.gameObject;
            _mirror = mirrorObject.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            mirrorObject = new GameObject(MirrorName, typeof(RectTransform), typeof(TextMeshProUGUI));
            mirrorObject.transform.SetParent(transform, false);
            _mirror = mirrorObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform rect = _mirror.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = _source.rectTransform.pivot;
        _mirror.raycastTarget = false;
    }

    private void Sync(bool force)
    {
        if (_source == null || _mirror == null) return;

        // Keep the legacy component alive for existing script and InputField references.
        // Only its renderer is hidden; CanvasGroup fading still applies to the TMP child.
        _source.canvasRenderer.SetAlpha(0f);

        if (force || _text != _source.text)
        {
            _text = _source.text;
            _mirror.text = UITheme.ReplaceEmoji(_text);
        }
        if (force || _font != _source.font)
        {
            _font = _source.font;
            _mirror.font = UITheme.GetTmpFont(_font);
            _mirror.fontStyle = ToTmpStyle(_fontStyle, _font);
        }
        if (force || _fontSize != _source.fontSize)
        {
            _fontSize = _source.fontSize;
            _mirror.fontSize = _fontSize;
        }
        if (force || _fontStyle != _source.fontStyle)
        {
            _fontStyle = _source.fontStyle;
            _mirror.fontStyle = ToTmpStyle(_fontStyle, _font);
        }
        if (force || _color != _source.color)
        {
            _color = _source.color;
            _mirror.color = _color;
        }
        if (force || _alignment != _source.alignment)
        {
            _alignment = _source.alignment;
            _mirror.alignment = ToTmpAlignment(_alignment);
        }
        if (force || !Mathf.Approximately(_lineSpacing, _source.lineSpacing))
        {
            _lineSpacing = _source.lineSpacing;
            _mirror.lineSpacing = (_lineSpacing - 1f) * 100f;
        }
        if (force || _richText != _source.supportRichText)
        {
            _richText = _source.supportRichText;
            _mirror.richText = _richText;
        }
        if (force || _bestFit != _source.resizeTextForBestFit ||
            _bestFitMin != _source.resizeTextMinSize || _bestFitMax != _source.resizeTextMaxSize)
        {
            _bestFit = _source.resizeTextForBestFit;
            _bestFitMin = _source.resizeTextMinSize;
            _bestFitMax = _source.resizeTextMaxSize;
            _mirror.enableAutoSizing = _bestFit;
            _mirror.fontSizeMin = Mathf.Max(1, _bestFitMin);
            _mirror.fontSizeMax = Mathf.Max(_mirror.fontSizeMin, _bestFitMax);
        }
        if (force || _horizontalOverflow != _source.horizontalOverflow ||
            _verticalOverflow != _source.verticalOverflow)
        {
            _horizontalOverflow = _source.horizontalOverflow;
            _verticalOverflow = _source.verticalOverflow;
            _mirror.textWrappingMode = _horizontalOverflow == HorizontalWrapMode.Wrap
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            _mirror.overflowMode = _verticalOverflow == VerticalWrapMode.Truncate
                ? TextOverflowModes.Truncate
                : TextOverflowModes.Overflow;
        }
        if (force || _sourceEnabled != _source.enabled)
        {
            _sourceEnabled = _source.enabled;
            _mirror.enabled = _sourceEnabled;
        }
    }

    private static FontStyles ToTmpStyle(FontStyle style, Font sourceFont)
    {
        FontStyles result;
        switch (style)
        {
            case FontStyle.Bold: result = FontStyles.Bold; break;
            case FontStyle.Italic: result = FontStyles.Italic; break;
            case FontStyle.BoldAndItalic: result = FontStyles.Bold | FontStyles.Italic; break;
            default: result = FontStyles.Normal; break;
        }

        // The corrected Thai TMP asset uses Mitr Regular. Preserve heavy UI hierarchy through
        // SDF synthetic bold instead of switching to uncorrected SemiBold/Bold font assets.
        string fontName = sourceFont != null ? sourceFont.name : string.Empty;
        if (fontName.IndexOf("SemiBold", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            fontName.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0)
            result |= FontStyles.Bold;
        return result;
    }

    private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Left;
        }
    }
}

/// <summary>Adds the bridge to every scene UI.Text, including objects created after scene load.</summary>
internal sealed class ThaiTextMeshProBootstrap : MonoBehaviour
{
    private const float ScanInterval = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<ThaiTextMeshProBootstrap>() != null) return;
        var root = new GameObject("Thai Text Renderer");
        DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideInHierarchy;
        root.AddComponent<ThaiTextMeshProBootstrap>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Scan();
        StartCoroutine(ScanLoop());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopAllCoroutines();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Scan();
    }

    private IEnumerator ScanLoop()
    {
        var wait = new WaitForSecondsRealtime(ScanInterval);
        while (true)
        {
            yield return wait;
            Scan();
        }
    }

    private static void Scan()
    {
        Text[] texts = Resources.FindObjectsOfTypeAll<Text>();
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null || !text.gameObject.scene.IsValid()) continue;
            ThaiTextMeshProBridge.Attach(text);
        }
    }
}
