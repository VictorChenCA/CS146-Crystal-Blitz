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
    // How strongly the camera shifts toward the origin as the player moves away.
    // 0 = pure follow; 0.5 = halfway between player and origin; 1 = locked to origin.
    [SerializeField] private float originPullFactor = 0.3f;

    private Transform _target;

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
        // Pull the camera toward origin proportionally to the player's distance from it.
        Vector3 originPull = -playerPos * originPullFactor;
        return playerPos + offset + originPull;
    }

    private void LateUpdate()
    {
        if (_target == null) return;
        transform.position = Vector3.Lerp(
            transform.position,
            DesiredPosition(_target.position),
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
    }
}
