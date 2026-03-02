using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;

    [Header("Lane Boundaries")]
    [SerializeField] private float groundY = 1f;
    [SerializeField] private float minLane = -30f;
    [SerializeField] private float maxLane = 30f;
    [SerializeField] private float halfLaneWidth = 3f;

    // Lane is diagonal: bottom-left (-X,-Z) to top-right (+X,+Z)
    private static readonly Vector3 LaneForward = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 LaneRight   = new Vector3(1f, 0f, -1f).normalized;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CameraFollow cam = Camera.main?.GetComponent<CameraFollow>();
        cam?.SetTarget(transform);

        EnforceLane();
    }

    private void Update()
    {
        HandleMovement();
        EnforceLane();
    }

    private void HandleMovement()
    {
        if (Keyboard.current == null) return;

        float fwdInput   = 0f;
        float rightInput = 0f;

        if (Keyboard.current.wKey.isPressed) fwdInput   += 1f;
        if (Keyboard.current.sKey.isPressed) fwdInput   -= 1f;
        if (Keyboard.current.dKey.isPressed) rightInput += 1f;
        if (Keyboard.current.aKey.isPressed) rightInput -= 1f;

        if (fwdInput == 0f && rightInput == 0f) return;

        Vector3 move = (LaneForward * fwdInput + LaneRight * rightInput).normalized;
        transform.Translate(move * moveSpeed * Time.deltaTime, Space.World);
    }

    private void EnforceLane()
    {
        Vector3 pos = transform.position;

        float fwd   = Vector3.Dot(pos, LaneForward);
        float right = Vector3.Dot(pos, LaneRight);

        fwd   = Mathf.Clamp(fwd,   minLane,        maxLane);
        right = Mathf.Clamp(right, -halfLaneWidth,  halfLaneWidth);

        pos   = LaneForward * fwd + LaneRight * right;
        pos.y = groundY;
        transform.position = pos;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);

        Vector3 center = LaneForward * ((minLane + maxLane) * 0.5f);
        center.y = groundY;
        Quaternion rot = Quaternion.LookRotation(LaneForward, Vector3.up);

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rot, Vector3.one);

        Gizmos.DrawWireCube(Vector3.zero,
            new Vector3(halfLaneWidth * 2f, 0.1f, maxLane - minLane));

        Gizmos.matrix = prev;
    }
}
