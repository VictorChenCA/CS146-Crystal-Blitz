using UnityEngine;

// MonoBehaviour — camera is purely client-side, never networked.
// Target is set dynamically by PlayerController.OnNetworkSpawn (owner only).
public class CameraFollow : MonoBehaviour
{
    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 8f;

    [Header("Team Camera Positions")]
    // Index 0 = Team Blue (O key), Index 1 = Team Red (P key).
    // Offsets mirror the diagonal lane so each team's camera sits behind them.
    [SerializeField] private Vector3[] teamOffsets    = { new Vector3(-7f, 10f, -7f), new Vector3(7f, 10f, 7f) };
    [SerializeField] private Vector3[] teamRotations  = { new Vector3(45f, 45f, 0f),  new Vector3(45f, 225f, 0f) };

    // Active values — driven by SetTeam(); serialized so they show in inspector.
    [SerializeField] private Vector3 offset         = new Vector3(-7f, 10f, -7f);
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

    public void SetTeam(int team)
    {
        int i = Mathf.Clamp(team, 0, Mathf.Min(teamOffsets.Length, teamRotations.Length) - 1);
        offset         = teamOffsets[i];
        cameraRotation = teamRotations[i];
        transform.rotation = Quaternion.Euler(cameraRotation);
        if (_target != null)
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
        Quaternion rot      = Quaternion.Euler(cameraRotation);
        Vector3    playerPos = _target != null ? _target.position : transform.position - offset;
        Vector3    camPos    = playerPos + offset;

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
    }
}
