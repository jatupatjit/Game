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
        transform.localScale = _originalScale;
        _targetScale = _originalScale;
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.unscaledDeltaTime * _lerpSpeed);
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _targetScale = _originalScale * _hoverScale;
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _targetScale = _originalScale;
    }

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _targetScale = _originalScale * _downScale;
    }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        _targetScale = _originalScale * _hoverScale;
    }
}
