using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;

    [SerializeField] private float groundY = 1f;

    // Isometric movement axes (match camera rotation 45,45,0)
    private static readonly Vector3 MoveForward = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 MoveRight   = new Vector3(1f, 0f, -1f).normalized;

    // ── Last move direction (used by DashAbility fallback) ───────────────────
    public Vector3 LastMoveDirection { get; private set; } = new Vector3(1f, 0f, 1f).normalized;

    // ── Movement lock (used by ProjectileShooter / AutoAttacker) ─────────────
    private float _movementLockUntil;

    public void LockMovement(float duration)
    {
        _movementLockUntil = Time.time + duration;
        _agent?.ResetPath();
    }
    public void CancelMovementLock()          => _movementLockUntil = -1f;

    // ── NavMesh ───────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;

    // ── Right-click input (ground plane for P&C movement) ────────────────────
    private AutoAttacker           _autoAttacker;
    private readonly Plane         _groundPlane = new Plane(Vector3.up, Vector3.zero);

    // ── Position sync throttle ────────────────────────────────────────────────
    private Vector3 _lastSentPosition;
    private float   _lastPositionSendTime;
    private const float PositionSendInterval  = 0.05f;  // 20 Hz max
    private const float PositionSendThreshold = 0.05f;  // ignore sub-5cm jitter

    // ── Destination set throttle ──────────────────────────────────────────────
    private Vector3 _lastNavDest        = Vector3.positiveInfinity;
    private float   _lastDestSetTime;
    private const float DestSetInterval = 0.1f;         // 10 Hz max — prevents NavMesh thrash
    private const float NavDestMoveSqr  = 0.25f;        // 0.5u threshold — skip trivial re-paths

    // ── Chase destination throttle ────────────────────────────────────────────
    private Vector3 _lastChaseDest      = Vector3.positiveInfinity;
    private float   _nextChaseDestTime;
    private const float ChaseDestInterval = 0.05f;  // 20 Hz
    private const float ChaseMoveSqr      = 0.09f;  // 0.3u threshold squared

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

    public NetworkVariable<int> CharacterIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        GameObjectRegistry.Players.Add(this);
        Position.OnValueChanged    += OnPositionChanged;
        PlayerColor.OnValueChanged += OnPlayerColorChanged;

        _agent = GetComponent<NavMeshAgent>();

        if (IsServer)
        {
            PlayerColor.Value = Color.white;  // Unassigned color; LobbyZone sets it later
            bool isLobby = GamePhaseManager.Instance == null ||
                           GamePhaseManager.Instance.Phase.Value == GamePhaseManager.GamePhase.Lobby;
            Vector3 spawnPos;
            if (isLobby)
            {
                Vector2 rand = Random.insideUnitCircle * 3f;
                spawnPos = new Vector3(rand.x, groundY, 100f + rand.y);
            }
            else
            {
                spawnPos = RandomSpawnForTeam(TeamIndex.Value, groundY);
            }
            transform.position = spawnPos;
            Position.Value     = spawnPos;
            _agent?.Warp(spawnPos);
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
            _agent.speed                  = moveSpeed;
            _agent.angularSpeed           = 9999f;
            _agent.acceleration           = 9999f;   // instant acceleration
            _agent.autoBraking            = false;   // no deceleration near destination
            _agent.stoppingDistance       = 0.15f;
            _agent.updateRotation         = false;
            _agent.obstacleAvoidanceType  = UnityEngine.AI.ObstacleAvoidanceType.NoObstacleAvoidance;
        }

        // Apply server-assigned spawn position. OnPositionChanged skips the owner,
        // so a joining client would otherwise stay at the prefab's default origin.
        if (!IsServer)
        {
            transform.position = Position.Value;
            _agent?.Warp(Position.Value);
        }

        _autoAttacker = GetComponent<AutoAttacker>();
        CreateClickIndicator();

        CameraFollow cam = Camera.main?.GetComponent<CameraFollow>();
        cam?.SetTarget(transform);
    }

    public override void OnNetworkDespawn()
    {
        GameObjectRegistry.Players.Remove(this);
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
        {
            HandleWasdMovement();
            SyncNavPosition();   // sync AA-chase movement in WASD mode
        }
        else
        {
            SyncNavMovement();
        }

        HandleRightClickInput();
        UpdateClickIndicator();
    }

    // ── Server-side team / character / teleport API ───────────────────────────

    /// <summary>Sets team and adjusts color. team=-1 resets to white (unassigned).</summary>
    public void SetTeamServerSide(int team)
    {
        if (!IsServer) return;
        TeamIndex.Value = Mathf.Max(-1, team);
        PlayerColor.Value = team == 0 ? new Color(0.2f, 0.5f, 1f)
                          : team == 1 ? new Color(1f, 0.25f, 0.25f)
                          : Color.white;
    }

    public void SetCharacterIndexServerSide(int index)
    {
        if (!IsServer) return;
        CharacterIndex.Value = index;
    }

    /// <summary>Server: teleport this player's transform and warp their NavMeshAgent.</summary>
    public void TeleportTo(Vector3 pos)
    {
        if (!IsServer) return;
        transform.position = pos;
        Position.Value     = pos;
        _agent?.Warp(pos);
        TeleportOwnerRpc(pos);
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 pos)
    {
        transform.position = pos;
        _agent?.Warp(pos);
    }

    // ── Team switch (editor only) ─────────────────────────────────────────────

    private void HandleTeamSwitch()
    {
#if UNITY_EDITOR
        if (Keyboard.current.oKey.wasPressedThisFrame) ChangeTeamServerRpc(0);
        if (Keyboard.current.pKey.wasPressedThisFrame) ChangeTeamServerRpc(1);
#endif
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

        Vector3 move   = (MoveForward * fwdInput + MoveRight * rightInput).normalized;
        LastMoveDirection = move;
        Vector3 newPos = transform.position + move * moveSpeed * Time.deltaTime;

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

        SyncNavPosition();
    }

    /// <summary>
    /// Syncs NavMeshAgent position to the server when the agent is actively moving.
    /// Called in both WASD and P&C modes so AA-chase works in WASD mode.
    /// </summary>
    private void SyncNavPosition()
    {
        if (_agent == null || !_agent.enabled) return;

        _agent.speed = moveSpeed;

        if (!_agent.hasPath || _agent.velocity.sqrMagnitude <= 0.01f) return;

        float now = Time.time;
        Vector3 pos = transform.position;
        if (now - _lastPositionSendTime < PositionSendInterval) return;
        if ((pos - _lastSentPosition).sqrMagnitude < PositionSendThreshold * PositionSendThreshold) return;

        _lastPositionSendTime = now;
        _lastSentPosition     = pos;
        SubmitPositionServerRpc(pos);
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

        bool isShift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        // Let AutoAttacker intercept for enemy targeting (works in both modes)
        if (_autoAttacker != null && _autoAttacker.TryHandleRightClick(pressed, isShift)) return;

        // Shift+right-click is fully owned by AutoAttacker — never fall through to movement
        if (isShift) return;

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

    /// <summary>Player-commanded move: throttled to prevent rapid-click thrash.</summary>
    public void SetNavDestination(Vector3 worldPos)
    {
        if (_agent == null || !_agent.enabled) return;
        if (Time.time < _movementLockUntil) return;
        if (Time.time - _lastDestSetTime < DestSetInterval) return;
        if ((worldPos - _lastNavDest).sqrMagnitude < NavDestMoveSqr) return;

        _lastDestSetTime = Time.time;
        _lastNavDest     = worldPos;
        _agent.SetDestination(worldPos);
        ShowClickIndicator(worldPos);
    }

    /// <summary>AA-commanded chase: throttled to 20 Hz to avoid NavMesh pathfinder thrash.</summary>
    public void SetChaseDestination(Vector3 pos)
    {
        if (_agent == null || !_agent.enabled || Time.time < _movementLockUntil) return;
        if (_agent.pathPending) return;
        if (Time.time < _nextChaseDestTime) return;
        if ((pos - _lastChaseDest).sqrMagnitude < ChaseMoveSqr) return;
        _agent.SetDestination(pos);
        _lastChaseDest     = pos;
        _nextChaseDestTime = Time.time + ChaseDestInterval;
    }

    /// <summary>Cancels any active NavMesh path.</summary>
    public void StopNavMovement()
    {
        _agent?.ResetPath();
        _lastChaseDest     = Vector3.positiveInfinity;
        _nextChaseDestTime = 0f;
    }

    /// <summary>Immediately submits a position to the server (used by DashAbility).</summary>
    public void ForceSubmitPosition(Vector3 pos) => SubmitPositionServerRpc(pos);

    // ── Server RPC ────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void SubmitPositionServerRpc(Vector3 newPosition, RpcParams rpcParams = default)
    {
        transform.position = newPosition;
        Position.Value     = newPosition;
    }

    // ── Spawn barrier clamping ────────────────────────────────────────────────

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

}
