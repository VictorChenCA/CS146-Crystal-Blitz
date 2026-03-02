using UnityEngine;

// MonoBehaviour — camera is purely client-side, never networked.
// Target is set dynamically by PlayerController.OnNetworkSpawn (owner only).
public class CameraFollow : MonoBehaviour
{
    [Header("2.5D Offset")]
    [SerializeField] private Vector3 offset = new Vector3(-7f, 10f, -7f);

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 8f;

    [Header("Rotation")]
    // 45° tilt from vertical, 45° yaw to face the diagonal lane (bottom-left → top-right)
    [SerializeField] private Vector3 cameraRotation = new Vector3(45f, 45f, 0f);

    private Transform _target;

    private void Start()
    {
        transform.rotation = Quaternion.Euler(cameraRotation);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        transform.position = _target.position + offset;
    }

    private void LateUpdate()
    {
        if (_target == null) return;
        transform.position = Vector3.Lerp(
            transform.position,
            _target.position + offset,
            smoothSpeed * Time.deltaTime
        );
    }
}
