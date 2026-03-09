using UnityEngine;
using UnityEngine.AI;
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

    // ── Movement lock (used by ProjectileShooter / AutoAttacker) ─────────────
    private float _movementLockUntil;

    public void LockMovement(float duration)  => _movementLockUntil = Time.time + duration;
    public void CancelMovementLock()          => _movementLockUntil = -1f;

    // ── NavMesh ───────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;

    // ── Right-click input (ground plane for P&C movement) ────────────────────
    private AutoAttacker           _autoAttacker;
    private readonly Plane         _groundPlane = new Plane(Vector3.up, Vector3.zero);

    // ── Click indicator (owner-only visual ring at nav destination) ───────────
    private LineRenderer _clickIndicator;
    private Vector3      _indicatorCenter;
    private float        _indicatorShowTime;
    private float        _indicatorFadeUntil;
    private float        _indicatorNextShow;
    private const float  IndicatorDuration       = 0.7f;
    private const float  IndicatorThrottle       = 0.25f;
    private const float  IndicatorExpandDuration = 0.2f;
    private const float  IndicatorFullRadius     = 0.45f;

    // ── Spawn helpers ─────────────────────────────────────────────────────────
    private static readonly Vector3[] TeamSpawnBase =
    {
        new Vector3(-14f, 1f, -14f),
        new Vector3( 14f, 1f,  14f),
    };

    public static Vector3 RandomSpawnForTeam(int team, float y)
    {
        Vector3 basePos = TeamSpawnBase[Mathf.Clamp(team, 0, TeamSpawnBase.Length - 1)];
        Vector2 offset  = Random.insideUnitCircle * 2f;
        return new Vector3(basePos.x + offset.x, y, basePos.z + offset.y);
    }

    // ── NetworkVariables ──────────────────────────────────────────────────────
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

    public NetworkVariable<Color> PlayerColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Position.OnValueChanged    += OnPositionChanged;
        PlayerColor.OnValueChanged += OnPlayerColorChanged;

        _agent = GetComponent<NavMeshAgent>();

        if (IsServer)
        {
            PlayerColor.Value  = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f);
            Vector3 spawnPos   = RandomSpawnForTeam(TeamIndex.Value, groundY);
            transform.position = spawnPos;
            Position.Value     = spawnPos;
        }

        ApplyPlayerColor(PlayerColor.Value);

        if (!IsOwner)
        {
            if (_agent != null) _agent.enabled = false;
            enabled = false;
            return;
        }

        if (_agent != null)
        {
            _agent.speed            = moveSpeed;
            _agent.angularSpeed     = 9999f;
            _agent.acceleration     = 9999f;   // instant acceleration
            _agent.autoBraking      = false;   // no deceleration near destination
            _agent.stoppingDistance = 0.15f;
            _agent.updateRotation   = false;
        }

        _autoAttacker = GetComponent<AutoAttacker>();
        CreateClickIndicator();

        CameraFollow cam = Camera.main?.GetComponent<CameraFollow>();
        cam?.SetTarget(transform);
    }

    public override void OnNetworkDespawn()
    {
        Position.OnValueChanged    -= OnPositionChanged;
        PlayerColor.OnValueChanged -= OnPlayerColorChanged;

        if (_clickIndicator != null)
            Destroy(_clickIndicator.gameObject);
    }

    // ── NetworkVariable callbacks ─────────────────────────────────────────────

    private void OnPositionChanged(Vector3 previous, Vector3 current)
    {
        if (!IsOwner) transform.position = current;
    }

    private void OnPlayerColorChanged(Color previous, Color current) => ApplyPlayerColor(current);

    private void ApplyPlayerColor(Color color)
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Keyboard.current == null) return;
        HandleTeamSwitch();

        if (GameSettings.UseWasd)
            HandleWasdMovement();
        else
            SyncNavMovement();

        HandleRightClickInput();
        UpdateClickIndicator();
    }

    // ── Team switch ───────────────────────────────────────────────────────────

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

    // ── WASD movement ─────────────────────────────────────────────────────────

    private void HandleWasdMovement()
    {
        if (Time.time < _movementLockUntil) return;

        float fwdInput   = 0f;
        float rightInput = 0f;

        if (Keyboard.current.wKey.isPressed) fwdInput   += 1f;
        if (Keyboard.current.sKey.isPressed) fwdInput   -= 1f;
        if (Keyboard.current.dKey.isPressed) rightInput += 1f;
        if (Keyboard.current.aKey.isPressed) rightInput -= 1f;

        if (fwdInput == 0f && rightInput == 0f) return;

        _agent?.ResetPath();

        Vector3 move   = (LaneForward * fwdInput + LaneRight * rightInput).normalized;
        Vector3 newPos = ClampToLane(transform.position + move * moveSpeed * Time.deltaTime);

        // Preserve the agent's natural Y so it doesn't fight the NavMesh surface
        if (_agent != null && _agent.enabled)
            newPos.y = _agent.nextPosition.y;

        transform.position = newPos;
        if (_agent != null && _agent.enabled)
            _agent.nextPosition = newPos;
        SubmitPositionServerRpc(newPos);
    }

    // ── NavMesh sync (P&C mode) ───────────────────────────────────────────────

    /// <summary>Syncs the NavMeshAgent's movement to the server each frame.</summary>
    private void SyncNavMovement()
    {
        // S = full stop in P&C mode
        if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame)
        {
            StopNavMovement();
            _autoAttacker?.CancelAutoAttack();
        }

        if (_agent == null || !_agent.enabled) return;
        if (_agent.hasPath && _agent.velocity.sqrMagnitude > 0.01f)
            SubmitPositionServerRpc(transform.position);
    }

    // ── Right-click input (both modes) ────────────────────────────────────────

    /// <summary>
    /// Reads right-click every frame regardless of movement mode.
    /// AutoAttacker gets first refusal (enemy hover → AA).
    /// Ground clicks navigate only in P&C mode.
    /// </summary>
    private void HandleRightClickInput()
    {
        if (Mouse.current == null) return;

        bool pressed = Mouse.current.rightButton.wasPressedThisFrame;
        bool held    = Mouse.current.rightButton.isPressed && !pressed;
        if (!pressed && !held) return;

        // Let AutoAttacker intercept for enemy targeting (works in both modes)
        if (_autoAttacker != null && _autoAttacker.TryHandleRightClick(pressed)) return;

        // Ground movement — P&C mode only
        if (GameSettings.UseWasd) return;
        if (pressed) _autoAttacker?.CancelAutoAttack();
        if (Time.time < _movementLockUntil) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
        if (_groundPlane.Raycast(ray, out float dist))
            SetNavDestination(ray.GetPoint(dist));
    }

    // ── Public NavMesh API (used by AutoAttacker) ─────────────────────────────

    /// <summary>Player-commanded move: clamps to lane and shows click indicator.</summary>
    public void SetNavDestination(Vector3 worldPos)
    {
        if (_agent == null || !_agent.enabled) return;
        if (Time.time < _movementLockUntil) return;

        Vector3 dest = ClampToLane(worldPos);
        _agent.SetDestination(dest);
        ShowClickIndicator(dest);
    }

    /// <summary>AA-commanded chase: no lane clamp, no indicator.</summary>
    public void SetChaseDestination(Vector3 worldPos)
    {
        if (_agent == null || !_agent.enabled) return;
        _agent.SetDestination(worldPos);
    }

    /// <summary>Cancels any active NavMesh path.</summary>
    public void StopNavMovement() => _agent?.ResetPath();

    // ── Server RPC ────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void SubmitPositionServerRpc(Vector3 newPosition, RpcParams rpcParams = default)
    {
        transform.position = newPosition;
        Position.Value     = newPosition;
    }

    // ── Lane clamping ─────────────────────────────────────────────────────────

    private Vector3 ClampToLane(Vector3 pos)
    {
        float fwd   = Mathf.Clamp(Vector3.Dot(pos, LaneForward), minLane, maxLane);
        float right = Mathf.Clamp(Vector3.Dot(pos, LaneRight), -halfLaneWidth, halfLaneWidth);
        Vector3 clamped = LaneForward * fwd + LaneRight * right;
        clamped.y = groundY;
        return clamped;
    }

    // ── Click indicator ───────────────────────────────────────────────────────

    private void CreateClickIndicator()
    {
        var go = new GameObject("ClickIndicator");
        _clickIndicator = go.AddComponent<LineRenderer>();

        _clickIndicator.positionCount     = 17;
        _clickIndicator.loop              = false;
        _clickIndicator.startWidth        = 0.08f;
        _clickIndicator.endWidth          = 0.08f;
        _clickIndicator.useWorldSpace     = true;
        _clickIndicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _clickIndicator.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white;
        _clickIndicator.material = mat;

        _clickIndicator.enabled = false;
    }

    private void ShowClickIndicator(Vector3 center)
    {
        if (_clickIndicator == null) return;
        if (Time.time < _indicatorNextShow) return;

        _indicatorCenter    = new Vector3(center.x, 0.05f, center.z);
        _indicatorShowTime  = Time.time;
        _indicatorFadeUntil = Time.time + IndicatorDuration;
        _indicatorNextShow  = Time.time + IndicatorThrottle;
        _clickIndicator.enabled = true;
    }

    private void UpdateClickIndicator()
    {
        if (_clickIndicator == null || !_clickIndicator.enabled) return;

        float remaining = _indicatorFadeUntil - Time.time;
        if (remaining <= 0f)
        {
            _clickIndicator.enabled = false;
            return;
        }

        float expandT = Mathf.Clamp01((Time.time - _indicatorShowTime) / IndicatorExpandDuration);
        float r       = Mathf.Lerp(0f, IndicatorFullRadius, expandT);

        for (int i = 0; i <= 16; i++)
        {
            float angle = i / 16f * Mathf.PI * 2f;
            _clickIndicator.SetPosition(i, _indicatorCenter + new Vector3(
                Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
        }

        float alpha = Mathf.Clamp01(remaining / IndicatorDuration);
        _clickIndicator.material.color = new Color(1f, 1f, 1f, alpha);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);

        Vector3 center = LaneForward * ((minLane + maxLane) * 0.5f);
        center.y = groundY;
        Quaternion rot = Quaternion.LookRotation(LaneForward, Vector3.up);

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfLaneWidth * 2f, 0.1f, maxLane - minLane));
        Gizmos.matrix = prev;
    }
}
