using UnityEngine;

// MonoBehaviour — camera is purely client-side, never networked.
// Target is set dynamically by PlayerController.OnNetworkSpawn (owner only).
public class CameraFollow : MonoBehaviour
{
    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 8f;

    [Header("Camera")]
    [SerializeField] private Vector3 offset         = new Vector3(-7f, 10f, -7f);
    [SerializeField] private Vector3 cameraRotation = new Vector3(45f, 45f, 0f);
    // The point the camera pulls toward (world space).
    [SerializeField] private Vector3 originOffset = Vector3.zero;
    // How strongly the camera shifts toward originOffset per axis.
    // 0 = pure follow; 0.5 = halfway between player and origin; 1 = locked to origin.
    [SerializeField] private float originPullX = 0.3f;
    [SerializeField] private float originPullY = 0.3f;
    // Rotation of the pull ellipse around the Y axis (degrees).
    [SerializeField] private float originRotation = 0f;

    public static CameraFollow Instance { get; private set; }

    private Transform _target;

    // ── Temporary target override (for win-sequence crystal cam pan) ──────────
    private Vector3? _tempTarget;
    private float    _tempTargetExpiry;

    public void SetTemporaryTarget(Vector3 worldPos, float duration)
    {
        _tempTarget       = worldPos;
        _tempTargetExpiry = Time.time + duration;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        transform.rotation = Quaternion.Euler(cameraRotation);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        transform.position = DesiredPosition(_target.position);
    }

    private Vector3 DesiredPosition(Vector3 playerPos)
    {
        // Pull the camera toward originOffset along rotated ground-plane axes.
        Quaternion rot      = Quaternion.Euler(0f, originRotation, 0f);
        Quaternion invRot   = Quaternion.Inverse(rot);
        Vector3    delta     = originOffset - playerPos;
        Vector3    local     = invRot * delta;
        Vector3    localPull = new Vector3(local.x * originPullX, 0f, local.z * originPullY);
        return playerPos + offset + rot * localPull;
    }

    private void LateUpdate()
    {
        Vector3 followPos;
        if (_tempTarget.HasValue && Time.time < _tempTargetExpiry)
        {
            followPos = _tempTarget.Value;
        }
        else
        {
            _tempTarget = null;
            if (_target == null) return;
            followPos = _target.position;
        }

        transform.position = Vector3.Lerp(
            transform.position,
            DesiredPosition(followPos),
            smoothSpeed * Time.deltaTime
        );
    }

    private void OnDrawGizmos()
    {
        Quaternion rot      = Quaternion.Euler(cameraRotation);
        Vector3    playerPos = _target != null ? _target.position : transform.position - offset;
        Vector3    camPos    = DesiredPosition(playerPos);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerPos, 0.3f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(camPos, 0.4f);
        Gizmos.DrawLine(playerPos, camPos);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(camPos, rot * Vector3.forward * 3f);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(camPos, rot * Vector3.up * 1.5f);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(camPos, rot * Vector3.right * 1.5f);

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(camPos, rot, Vector3.one);
        Gizmos.color  = new Color(1f, 1f, 1f, 0.15f);
        Gizmos.DrawFrustum(Vector3.zero, 60f, 12f, 0.3f, 16f / 9f);
        Gizmos.matrix = prev;

        // Origin marker
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(originOffset, 0.5f);
        Gizmos.DrawLine(originOffset - Vector3.right  * 1.5f, originOffset + Vector3.right  * 1.5f);
        Gizmos.DrawLine(originOffset - Vector3.up     * 1.5f, originOffset + Vector3.up     * 1.5f);
        Gizmos.DrawLine(originOffset - Vector3.forward * 1.5f, originOffset + Vector3.forward * 1.5f);

        // Ellipse showing the camera shift at a reference player distance from the origin.
        // X axis maps to world X (originPullX), Z axis maps to world Z (originPullY).
        // A player sitting on this ellipse produces exactly `referenceShift` units of camera offset.
        const float referenceShift = 3f;
        const int   segments       = 64;
        float rx = originPullX > 0.0001f ? referenceShift / originPullX : 0f;
        float rz = originPullY > 0.0001f ? referenceShift / originPullY : 0f;
        Quaternion ellipseRot = Quaternion.Euler(0f, originRotation, 0f);
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Vector3 prevPt = originOffset + ellipseRot * new Vector3(rx, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float   t      = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 local  = new Vector3(Mathf.Cos(t) * rx, 0f, Mathf.Sin(t) * rz);
            Vector3 pt     = originOffset + ellipseRot * local;
            Gizmos.DrawLine(prevPt, pt);
            prevPt = pt;
        }
    }
}
