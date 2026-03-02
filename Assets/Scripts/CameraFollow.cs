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

    private void OnDrawGizmos()
    {
        // Use cameraRotation field directly so the gizmo reflects inspector changes
        // even in Edit mode before Start() has applied the rotation to the transform.
        Quaternion rot = Quaternion.Euler(cameraRotation);

        Vector3 playerPos = _target != null ? _target.position : transform.position - offset;
        Vector3 camPos    = playerPos + offset;

        // --- Position ---
        // Green sphere = player/target
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerPos, 0.3f);

        // Cyan sphere + offset arm
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(camPos, 0.4f);
        Gizmos.DrawLine(playerPos, camPos);

        // --- Rotation: camera axes drawn from camPos ---
        // Forward (blue) — where the camera is pointing
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(camPos, rot * Vector3.forward * 3f);

        // Up (green)
        Gizmos.color = Color.green;
        Gizmos.DrawRay(camPos, rot * Vector3.up * 1.5f);

        // Right (red)
        Gizmos.color = Color.red;
        Gizmos.DrawRay(camPos, rot * Vector3.right * 1.5f);

        // --- Frustum — makes the field of view and tilt immediately readable ---
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(camPos, rot, Vector3.one);
        Gizmos.color  = new Color(1f, 1f, 1f, 0.15f);
        Gizmos.DrawFrustum(Vector3.zero, 60f, 12f, 0.3f, 16f / 9f);
        Gizmos.matrix = prev;
    }
}
