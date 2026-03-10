using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Owner-only component responsible solely for auto-attack behaviour:
///   - Cursor hover detection (turns red over enemies)
///   - TryHandleRightClick: called by PlayerController; returns true if a right-click
///     was consumed for auto-attack so PlayerController can skip movement handling
///   - Auto-attack state machine: chase (via PlayerController.SetChaseDestination),
///     wind-up animation, fire homing projectile
///
/// All movement execution goes through PlayerController.
/// </summary>
[DefaultExecutionOrder(-1)]
public class AutoAttacker : NetworkBehaviour
{
    [Header("Auto Attack")]
    [SerializeField] private float autoAttackRange    = 12f;
    [SerializeField] private float autoAttackCooldown = 1.5f;
    [SerializeField] private float windUpDuration     = 0.3f;
    [SerializeField] private float projectileSpeed    = 18f;
    [SerializeField] private float damage             = 30f;
    [SerializeField] private GameObject autoAttackProjectilePrefab;

    private PlayerController _pc;
    private Camera           _mainCamera;
    private readonly Plane   _groundPlane = new Plane(Vector3.up, Vector3.zero);

    private Transform      _target;
    private NetworkObject  _targetNetObj;
    private float          _nextAttackTime;
    private Coroutine      _attackCoroutine;

    private Transform _hoveredEnemy;
    private bool      _attackCursorActive;

    private Texture2D _cursorDefault;
    private Texture2D _cursorAttack;

    // ── Attack-move indicator ─────────────────────────────────────────────────
    private LineRenderer _attackMoveIndicator;
    private Vector3      _attackMoveCenter;
    private float        _attackMoveFadeUntil;
    private const float  AttackMoveIndicatorDuration = 0.7f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _pc         = GetComponent<PlayerController>();
        _mainCamera = Camera.main;

        BuildCursorTextures();
        CreateAttackMoveIndicator();
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.SetCursor(_cursorDefault, new Vector2(8f, 8f), CursorMode.Auto);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (_cursorDefault) Destroy(_cursorDefault);
        if (_cursorAttack)  Destroy(_cursorAttack);
        if (_attackMoveIndicator) Destroy(_attackMoveIndicator.gameObject);
    }

    private void OnDestroy()
    {
        if (IsOwner) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_mainCamera == null) return;
        if (Mouse.current == null) return;
        UpdateHoverDetection();
        UpdateAttackMoveIndicator();
    }

    // ── Hover detection ───────────────────────────────────────────────────────

    private void UpdateHoverDetection()
    {
        // No targeting during lobby or countdown
        if (GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby ||
            GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Countdown)
        {
            if (_hoveredEnemy != null)
            {
                _hoveredEnemy       = null;
                _attackCursorActive = false;
                Cursor.SetCursor(_cursorDefault, new Vector2(8f, 8f), CursorMode.Auto);
            }
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray     ray      = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));

        _hoveredEnemy = null;
        int myTeam = _pc != null ? _pc.TeamIndex.Value : -1;

        RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
        foreach (var hit in hits)
        {
            var pc = hit.collider.GetComponent<PlayerController>();
            if (pc != null && pc != _pc && pc.TeamIndex.Value != myTeam)
            {
                _hoveredEnemy = hit.collider.transform;
                break;
            }
        }

        bool wantAttackCursor = _hoveredEnemy != null;
        if (wantAttackCursor != _attackCursorActive)
        {
            _attackCursorActive = wantAttackCursor;
            Cursor.SetCursor(
                _attackCursorActive ? _cursorAttack : _cursorDefault,
                new Vector2(8f, 8f),
                CursorMode.Auto);
        }
    }

    // ── Right-click interception (called by PlayerController) ─────────────────

    /// <summary>
    /// Returns true if the right-click was consumed for auto-attack or attack-move.
    /// PlayerController calls this first; if false, it handles movement itself.
    /// </summary>
    public bool TryHandleRightClick(bool isNewPress, bool isShift = false)
    {
        // No attack targeting during lobby or countdown
        if (GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby ||
            GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Countdown)
        {
            _hoveredEnemy = null;
            return false;
        }

        // ── Attack move: Shift + right-click in P&C mode ──────────────────────
        if (isShift && isNewPress && !GameSettings.UseWasd)
        {
            if (GetCursorWorldPos(out Vector3 worldPos))
            {
                ShowAttackMoveIndicator(worldPos);
                Transform enemy = FindNearestEnemyAt(worldPos, 1f);
                if (enemy != null)
                {
                    var netObj = enemy.GetComponent<NetworkObject>()
                              ?? enemy.GetComponentInParent<NetworkObject>();
                    if (netObj != null)
                    {
                        _target       = enemy;
                        _targetNetObj = netObj;
                        if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
                        _attackCoroutine = StartCoroutine(AutoAttackLoop());
                        return true;
                    }
                }
                // No enemy nearby — move to the position
                _pc?.SetNavDestination(worldPos);
            }
            return true;
        }

        // ── Normal: hovering an enemy ─────────────────────────────────────────
        if (_hoveredEnemy == null) return false;

        // While hovering an enemy, consume the input even on hold to prevent
        // movement orders from firing simultaneously.
        if (!isNewPress) return true;

        var hoverNetObj = _hoveredEnemy.GetComponent<NetworkObject>()
                       ?? _hoveredEnemy.GetComponentInParent<NetworkObject>();
        if (hoverNetObj == null) return false;

        _target       = _hoveredEnemy;
        _targetNetObj = hoverNetObj;
        if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
        _attackCoroutine = StartCoroutine(AutoAttackLoop());
        return true;
    }

    // ── Auto-attack cancellation ──────────────────────────────────────────────

    public void CancelAutoAttack()
    {
        if (_attackCoroutine != null)
        {
            StopCoroutine(_attackCoroutine);
            _attackCoroutine = null;
        }
        _target       = null;
        _targetNetObj = null;
        _pc?.StopNavMovement();
        _pc?.CancelMovementLock();
    }

    // ── Auto-attack loop ──────────────────────────────────────────────────────

    private IEnumerator AutoAttackLoop()
    {
        while (_target != null && _targetNetObj != null && _targetNetObj.IsSpawned)
        {
            // WASD input cancels the chase
            if (GameSettings.UseWasd && HasWasdInput())
            {
                CancelAutoAttack();
                yield break;
            }

            float dist = Vector3.Distance(transform.position, _target.position);

            if (dist > autoAttackRange)
            {
                // Chase via PlayerController — P&C mode only
                if (!GameSettings.UseWasd)
                    _pc.SetChaseDestination(_target.position);

                yield return null;
                continue;
            }

            // In range ────────────────────────────────────────────────────────

            _pc?.StopNavMovement();

            while (Time.time < _nextAttackTime)
            {
                if (_target == null || !_targetNetObj.IsSpawned) yield break;
                yield return null;
            }

            // Wind-up animation
            _pc?.LockMovement(windUpDuration);
            GameObject preview = CreateWindUpSphere();

            float windEnd = Time.time + windUpDuration;
            while (Time.time < windEnd)
            {
                if (_target == null || !_targetNetObj.IsSpawned)
                {
                    Destroy(preview);
                    _pc?.CancelMovementLock();
                    _attackCoroutine = null;
                    yield break;
                }

                float t = 1f - (windEnd - Time.time) / windUpDuration;
                preview.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 0.25f, t);
                preview.GetComponent<Renderer>().material.color =
                    Color.Lerp(Color.white, new Color(1f, 0.5f, 0f), Mathf.Pow(t, 2f));
                yield return null;
            }

            Destroy(preview);

            if (_target == null || !_targetNetObj.IsSpawned)
            {
                _attackCoroutine = null;
                yield break;
            }

            FireAutoAttackServerRpc(_targetNetObj.NetworkObjectId, damage);
            _nextAttackTime = Time.time + autoAttackCooldown;

            yield return new WaitForSeconds(0.15f);
        }

        _attackCoroutine = null;
    }

    private bool HasWasdInput()
    {
        if (Keyboard.current == null) return false;
        return Keyboard.current.wKey.isPressed ||
               Keyboard.current.sKey.isPressed ||
               Keyboard.current.aKey.isPressed ||
               Keyboard.current.dKey.isPressed;
    }

    // ── Wind-up visual ────────────────────────────────────────────────────────

    private GameObject CreateWindUpSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.position   = transform.position + Vector3.up * 0.5f;
        go.transform.localScale = Vector3.zero;
        return go;
    }

    // ── Server RPC ────────────────────────────────────────────────────────────

    [ServerRpc]
    private void FireAutoAttackServerRpc(ulong targetNetObjId, float dmg)
    {
        if (autoAttackProjectilePrefab == null) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(targetNetObjId, out var targetNetObj)) return;

        var targetHealth = targetNetObj.GetComponent<PlayerHealth>();
        if (targetHealth == null) return;

        GameObject proj   = Instantiate(autoAttackProjectilePrefab, transform.position, Quaternion.identity);
        var        netObj = proj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        var controller = proj.GetComponent<HomingProjectileController>();
        controller.Initialize(targetHealth, projectileSpeed, OwnerClientId, dmg);
    }

    // ── Attack-move helpers ───────────────────────────────────────────────────

    private bool GetCursorWorldPos(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        if (_mainCamera == null || Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
        if (_groundPlane.Raycast(ray, out float dist))
        {
            worldPos = ray.GetPoint(dist);
            return true;
        }
        return false;
    }

    private Transform FindNearestEnemyAt(Vector3 worldPos, float radius)
    {
        int myTeam = _pc != null ? _pc.TeamIndex.Value : -1;
        Transform nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (pc == _pc) continue;
            if (pc.TeamIndex.Value == myTeam) continue;
            float dist = Vector3.Distance(worldPos, pc.transform.position);
            if (dist <= radius && dist < nearestDist)
            {
                nearest = pc.transform;
                nearestDist = dist;
            }
        }
        return nearest;
    }

    // ── Attack-move indicator ─────────────────────────────────────────────────

    private void CreateAttackMoveIndicator()
    {
        var go = new GameObject("AttackMoveIndicator");
        _attackMoveIndicator = go.AddComponent<LineRenderer>();
        _attackMoveIndicator.positionCount     = 17;
        _attackMoveIndicator.loop              = false;
        _attackMoveIndicator.startWidth        = 0.08f;
        _attackMoveIndicator.endWidth          = 0.08f;
        _attackMoveIndicator.useWorldSpace     = true;
        _attackMoveIndicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _attackMoveIndicator.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.red;
        _attackMoveIndicator.material = mat;
        _attackMoveIndicator.enabled  = false;
    }

    private void ShowAttackMoveIndicator(Vector3 center)
    {
        if (_attackMoveIndicator == null) return;
        _attackMoveCenter    = new Vector3(center.x, 0.05f, center.z);
        _attackMoveFadeUntil = Time.time + AttackMoveIndicatorDuration;
        _attackMoveIndicator.enabled = true;
    }

    private void UpdateAttackMoveIndicator()
    {
        if (_attackMoveIndicator == null || !_attackMoveIndicator.enabled) return;

        float remaining = _attackMoveFadeUntil - Time.time;
        if (remaining <= 0f)
        {
            _attackMoveIndicator.enabled = false;
            return;
        }

        float r = 0.45f;
        for (int i = 0; i <= 16; i++)
        {
            float angle = i / 16f * Mathf.PI * 2f;
            _attackMoveIndicator.SetPosition(i, _attackMoveCenter + new Vector3(
                Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
        }

        float alpha = Mathf.Clamp01(remaining / AttackMoveIndicatorDuration);
        _attackMoveIndicator.material.color = new Color(1f, 0f, 0f, alpha);
    }

    // ── Cursor textures (procedural) ──────────────────────────────────────────

    private void BuildCursorTextures()
    {
        _cursorDefault = MakeCursorTex(16, Color.white, filled: false);
        _cursorAttack  = MakeCursorTex(16, Color.red,   filled: true);
    }

    private static Texture2D MakeCursorTex(int size, Color color, bool filled)
    {
        var   tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var   pixels = new Color[size * size];
        float cx     = size * 0.5f - 0.5f;
        float cy     = size * 0.5f - 0.5f;
        float r      = size * 0.5f - 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx   = x - cx;
                float dy   = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                bool onCircle = dist >= r - 1f && dist <= r + 0.5f;
                bool onCross  = filled && dist < r && (Mathf.Abs(dx) <= 1f || Mathf.Abs(dy) <= 1f);

                pixels[y * size + x] = (onCircle || onCross) ? color : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
