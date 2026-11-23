using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// White kit button that turns pink while the mouse is over it or it is pressed ("white boxes,
/// pink only when the mouse points at them, like the Settings page"). Added by <see cref="UITheme.StyleButton"/> to
/// White / WhiteShort buttons and switched off for every other style, so a button styled Pink on purpose
/// (a selected Settings tab) stays pink. Purely local UI.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIHoverPink : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Button _button;
    private Image _image;
    private Sprite _normalSprite, _hoverSprite;
    private Color _normalText, _hoverText;
    private bool _over, _down;

    public void Setup(Sprite normal, Sprite hover, Color normalText, Color hoverText)
    {
        _button = GetComponent<Button>();
        _image = _button != null ? _button.targetGraphic as Image : null;
        _normalSprite = normal;
        _hoverSprite = hover;
        _normalText = normalText;
        _hoverText = hoverText;
        _over = _down = false;
        enabled = true;
        Apply();
    }

    private void OnDisable()
    {
        _over = _down = false;
        if (_image != null && _normalSprite != null) Apply(force: false);
    }

    public void OnPointerEnter(PointerEventData eventData) { _over = true; Apply(); }
    public void OnPointerExit(PointerEventData eventData) { _over = false; _down = false; Apply(); }
    public void OnPointerDown(PointerEventData eventData) { _down = true; Apply(); }
    public void OnPointerUp(PointerEventData eventData) { _down = false; Apply(); }

    private void Apply(bool force = true)
    {
        if (_image == null) return;
        bool pink = force && enabled && (_over || _down) && (_button == null || _button.interactable);
        Sprite sprite = pink ? _hoverSprite : _normalSprite;
        if (sprite != null && _image.sprite != sprite) _image.sprite = sprite;
        Color text = pink ? _hoverText : _normalText;
        foreach (Text label in GetComponentsInChildren<Text>(true)) label.color = text;
    }
}
