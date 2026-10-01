using UnityEngine;

[DisallowMultipleComponent]
public class SpinnerAnimator : MonoBehaviour
{
    [SerializeField] private float _speed = 260f;

    private void Update()
    {
        transform.Rotate(0, 0, -_speed * Time.unscaledDeltaTime);
    }
}
