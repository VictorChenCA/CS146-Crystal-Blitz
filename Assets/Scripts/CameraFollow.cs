using UnityEngine;

// MonoBehaviour — camera is purely client-side, never networked.
// Target is set dynamically by PlayerController.OnNetworkSpawn (owner only).
public class CameraFollow : MonoBehaviour
{
    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed     = 8f;
    [SerializeField] private float rotationSpeed   = 4f;   // slerp speed for rotation transitions

    [Header("In-Game Camera")]
    [SerializeField] private Vector3 offset         = new Vector3(-7f, 10f, -7f);
    [SerializeField] private Vector3 cameraRotation = new Vector3(45f, 45f, 0f);
    [SerializeField] private Vector3 originOffset   = Vector3.zero;
    [SerializeField] private float   originPullX    = 0.3f;
    [SerializeField] private float   originPullY    = 0.3f;
    [SerializeField] private float   originRotation = 0f;

    [Header("Lobby Camera")]
    [SerializeField] private Vector3 lobbyOffset         = new Vector3(-7f, 10f, -7f);
    [SerializeField] private Vector3 lobbyCameraRotation = new Vector3(45f, 45f, 0f);
    [SerializeField] private Vector3 lobbyOriginOffset   = Vector3.zero;
    [SerializeField] private float   lobbyOriginPullX    = 0.3f;
    [SerializeField] private float   lobbyOriginPullY    = 0.3f;
    [SerializeField] private float   lobbyOriginRotation = 0f;

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
        transform.rotation = Quaternion.Euler(ActiveRotation());
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        transform.position = DesiredPosition(_target.position);
    }

    // ── Active parameter selection ────────────────────────────────────────────

    private bool IsLobby()
    {
        var phase = GamePhaseManager.Instance?.Phase.Value;
        return phase == null
            || phase == GamePhaseManager.GamePhase.Lobby
            || phase == GamePhaseManager.GamePhase.Countdown;
    }

    private Vector3 ActiveOffset()         => IsLobby() ? lobbyOffset         : offset;
    private Vector3 ActiveRotation()       => IsLobby() ? lobbyCameraRotation  : cameraRotation;
    private Vector3 ActiveOriginOffset()   => IsLobby() ? lobbyOriginOffset    : originOffset;
    private float   ActiveOriginPullX()    => IsLobby() ? lobbyOriginPullX     : originPullX;
    private float   ActiveOriginPullY()    => IsLobby() ? lobbyOriginPullY     : originPullY;
    private float   ActiveOriginRotation() => IsLobby() ? lobbyOriginRotation  : originRotation;

    // ── Position calculation ──────────────────────────────────────────────────

    private Vector3 DesiredPosition(Vector3 playerPos)
    {
        Vector3 ao  = ActiveOffset();
        Vector3 oo  = ActiveOriginOffset();
        float   px  = ActiveOriginPullX();
        float   pz  = ActiveOriginPullY();
        float   rot = ActiveOriginRotation();

        Quaternion rotQ    = Quaternion.Euler(0f, rot, 0f);
        Vector3    delta   = oo - playerPos;
        Vector3    local   = Quaternion.Inverse(rotQ) * delta;
        Vector3    pull    = new Vector3(local.x * px, 0f, local.z * pz);
        return playerPos + ao + rotQ * pull;
    }

    // ── LateUpdate ────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        // Smoothly rotate camera when switching between lobby and game params
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.Euler(ActiveRotation()),
            rotationSpeed * Time.deltaTime
        );

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

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Vector3    activeRot = ActiveRotation();
        Quaternion rot       = Quaternion.Euler(activeRot);
        Vector3    playerPos = _target != null ? _target.position : transform.position - ActiveOffset();
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
        Vector3 oo = ActiveOriginOffset();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(oo, 0.5f);
        Gizmos.DrawLine(oo - Vector3.right   * 1.5f, oo + Vector3.right   * 1.5f);
        Gizmos.DrawLine(oo - Vector3.up      * 1.5f, oo + Vector3.up      * 1.5f);
        Gizmos.DrawLine(oo - Vector3.forward * 1.5f, oo + Vector3.forward * 1.5f);

        // Pull ellipse
        const float referenceShift = 3f;
        const int   segments       = 64;
        float px = ActiveOriginPullX();
        float pz = ActiveOriginPullY();
        float rx = px > 0.0001f ? referenceShift / px : 0f;
        float rz = pz > 0.0001f ? referenceShift / pz : 0f;
        Quaternion ellipseRot = Quaternion.Euler(0f, ActiveOriginRotation(), 0f);
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Vector3 prevPt = oo + ellipseRot * new Vector3(rx, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float   t     = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 local = new Vector3(Mathf.Cos(t) * rx, 0f, Mathf.Sin(t) * rz);
            Vector3 pt    = oo + ellipseRot * local;
            Gizmos.DrawLine(prevPt, pt);
            prevPt = pt;
        }
    }
}
