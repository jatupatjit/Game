using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover tooltip in the UI kit style (dark rounded chip, white text). Put on a raycast-target
/// Graphic; hovering shows <see cref="Text"/> next to it. One tooltip chip per root canvas.
/// </summary>
[DisallowMultipleComponent]
public sealed class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea] public string Text;
    /// <summary>Which side of the trigger the chip appears on (anchor-relative direction).</summary>
    public Vector2 Side = new Vector2(1f, 0f);

    private static UITooltip _shownBy;

    public void OnPointerEnter(PointerEventData eventData) => Show(this);
    public void OnPointerExit(PointerEventData eventData) { if (_shownBy == this) Hide(transform); }
    private void OnDisable() { if (_shownBy == this) Hide(transform); }

    /// <summary>Shows the tooltip for <paramref name="trigger"/> (also usable from code/tests).</summary>
    public static void Show(UITooltip trigger)
    {
        if (trigger == null || string.IsNullOrEmpty(trigger.Text)) return;
        Canvas canvas = trigger.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        RectTransform chip = GetChip(canvas, out Text label);
        label.text = trigger.Text;
        chip.gameObject.SetActive(true);
        chip.SetAsLastSibling();

        // Size to text, then place beside the trigger and keep it on screen.
        LayoutRebuilder.ForceRebuildLayoutImmediate(chip);
        var target = (RectTransform)trigger.transform;
        var canvasRect = (RectTransform)canvas.transform;
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 anchorWorld = trigger.Side.x > 0f ? (corners[2] + corners[3]) * 0.5f :
                              trigger.Side.x < 0f ? (corners[0] + corners[1]) * 0.5f :
                              trigger.Side.y > 0f ? (corners[1] + corners[2]) * 0.5f : (corners[0] + corners[3]) * 0.5f;
        Vector2 local = canvasRect.InverseTransformPoint(anchorWorld);
        Vector2 size = chip.rect.size;
        chip.pivot = new Vector2(trigger.Side.x > 0f ? 0f : trigger.Side.x < 0f ? 1f : 0.5f,
                                 trigger.Side.y > 0f ? 0f : trigger.Side.y < 0f ? 1f : 0.5f);
        Vector2 offset = trigger.Side * 18f;
        Vector2 half = canvasRect.rect.size * 0.5f;
        Vector2 pos = local + offset;
        pos.x = Mathf.Clamp(pos.x, -half.x + size.x * chip.pivot.x, half.x - size.x * (1f - chip.pivot.x));
        pos.y = Mathf.Clamp(pos.y, -half.y + size.y * chip.pivot.y, half.y - size.y * (1f - chip.pivot.y));
        chip.anchoredPosition = pos;
        _shownBy = trigger;
    }

    public static void Hide(Transform anyUnderCanvas)
    {
        _shownBy = null;
        Canvas canvas = anyUnderCanvas != null ? anyUnderCanvas.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return;
        Transform chip = canvas.rootCanvas.transform.Find(ChipName);
        if (chip != null) chip.gameObject.SetActive(false);
    }

    private const string ChipName = "UITooltipChip";

    private static RectTransform GetChip(Canvas canvas, out Text label)
    {
        Transform existing = canvas.transform.Find(ChipName);
        if (existing != null)
        {
            label = existing.GetComponentInChildren<Text>(true);
            return (RectTransform)existing;
        }
        var go = new GameObject(ChipName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        var image = go.GetComponent<Image>();
        UITheme.StyleShape(image, UITheme.SmallSprite, UITheme.InputWell);
        image.raycastTarget = false;
        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        label = UITheme.MakeText(go.transform, "Text", "", 20, UITextRole.Primary);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var element = label.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = -1f;
        go.SetActive(false);
        return rect;
    }
}
