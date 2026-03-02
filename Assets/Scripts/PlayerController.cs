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

    private static readonly Vector3 LaneForward = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 LaneRight   = new Vector3(1f, 0f, -1f).normalized;

    private static readonly Color[] TeamColors =
    {
        new Color(0.2f, 0.4f, 0.9f),  // 0 = Blue
        new Color(0.9f, 0.2f, 0.2f),  // 1 = Red
    };

    public NetworkVariable<Vector3> Position = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> TeamIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        Position.OnValueChanged  += OnPositionChanged;
        TeamIndex.OnValueChanged += OnTeamChanged;

        // Apply whatever team colour is already set (e.g. late-joining client).
        ApplyTeamColor(TeamIndex.Value);

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CameraFollow cam = Camera.main?.GetComponent<CameraFollow>();
        cam?.SetTarget(transform);
    }

    public override void OnNetworkDespawn()
    {
        Position.OnValueChanged  -= OnPositionChanged;
        TeamIndex.OnValueChanged -= OnTeamChanged;
    }

    private void OnPositionChanged(Vector3 previous, Vector3 current)
    {
        if (!IsOwner)
            transform.position = current;
    }

    private void OnTeamChanged(int previous, int current)
    {
        ApplyTeamColor(current);
    }

    private void ApplyTeamColor(int team)
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", TeamColors[Mathf.Clamp(team, 0, TeamColors.Length - 1)]);
        r.SetPropertyBlock(block);
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        HandleTeamSwitch();
        HandleMovement();
    }

    private void HandleTeamSwitch()
    {
        if (Keyboard.current.oKey.wasPressedThisFrame) ChangeTeamServerRpc(0);
        if (Keyboard.current.pKey.wasPressedThisFrame) ChangeTeamServerRpc(1);
    }

    [Rpc(SendTo.Server)]
    private void ChangeTeamServerRpc(int team, RpcParams rpcParams = default)
    {
        TeamIndex.Value = team;
    }

    private void HandleMovement()
    {
        float fwdInput   = 0f;
        float rightInput = 0f;

        if (Keyboard.current.wKey.isPressed) fwdInput   += 1f;
        if (Keyboard.current.sKey.isPressed) fwdInput   -= 1f;
        if (Keyboard.current.dKey.isPressed) rightInput += 1f;
        if (Keyboard.current.aKey.isPressed) rightInput -= 1f;

        if (fwdInput == 0f && rightInput == 0f) return;

        Vector3 move   = (LaneForward * fwdInput + LaneRight * rightInput).normalized;
        Vector3 newPos = ClampToLane(transform.position + move * moveSpeed * Time.deltaTime);

        transform.position = newPos;
        SubmitPositionServerRpc(newPos);
    }

    [Rpc(SendTo.Server)]
    private void SubmitPositionServerRpc(Vector3 newPosition, RpcParams rpcParams = default)
    {
        transform.position = newPosition;
        Position.Value     = newPosition;
    }

    private Vector3 ClampToLane(Vector3 pos)
    {
        float fwd   = Mathf.Clamp(Vector3.Dot(pos, LaneForward), minLane, maxLane);
        float right = Mathf.Clamp(Vector3.Dot(pos, LaneRight), -halfLaneWidth, halfLaneWidth);
        Vector3 clamped = LaneForward * fwd + LaneRight * right;
        clamped.y = groundY;
        return clamped;
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
