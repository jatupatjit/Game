using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIButtonHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
{
    [SerializeField] private float _hoverScale = 1.025f;
    [SerializeField] private float _downScale = 0.975f;
    [SerializeField] private float _lerpSpeed = 18f;

    private Vector3 _originalScale = Vector3.one;
    private Vector3 _targetScale = Vector3.one;
    private Button _button;
    private bool _hovered;
    private bool _pressed;

    private void Awake()
    {
        _originalScale = transform.localScale;
        _targetScale = _originalScale;
        _button = GetComponent<Button>();
        if (_button != null) _button.onClick.AddListener(PlayClick);
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick() => CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.Click, Vector3.zero);

    private void OnEnable()
    {
        _hovered = false;
        _pressed = false;
        transform.localScale = _originalScale;
        RefreshTarget();
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.unscaledDeltaTime * _lerpSpeed);
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _hovered = true;
        RefreshTarget();
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _hovered = false;
        _pressed = false;
        RefreshTarget();
    }

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _pressed = true;
        RefreshTarget();
    }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _pressed = false;
        _hovered = true;
        RefreshTarget();
    }

    public void SetBaseScale(float uniformScale)
    {
        Vector3 next = Vector3.one * uniformScale;
        if ((_originalScale - next).sqrMagnitude < .000001f) return;
        _originalScale = next;
        RefreshTarget();
    }

    private void RefreshTarget()
    {
        float multiplier = _pressed ? _downScale : _hovered ? _hoverScale : 1f;
        _targetScale = _originalScale * multiplier;
    }
}
