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
    private PlayerHealth     _health;
    private PlayerXP         _xp;
    private Renderer         _renderer;
    private Camera           _mainCamera;
    private readonly Plane   _groundPlane = new Plane(Vector3.up, Vector3.zero);

    private Transform      _target;
    private NetworkObject  _targetNetObj;
    private float          _nextAttackTime;
    private Coroutine      _attackCoroutine;

    // ── Charge tint (driven by Update, independent of coroutine) ─────────────
    private float _tintValue;   // 0..1, current glow intensity
    private float _tintTarget;  // destination (0 = dark, 1 = bright)
    private float _tintRate;    // units per second

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
        _health     = GetComponent<PlayerHealth>();
        _xp         = GetComponent<PlayerXP>();
        _renderer   = GetComponent<Renderer>();
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

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (IsOwner) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        UpdateChargeTint();   // always runs so the fade completes even after cancel/death
        if (_mainCamera == null) return;
        if (Mouse.current == null) return;
        if (_health != null && _health.IsDead) return;
        UpdateHoverDetection();
        UpdateAttackMoveIndicator();
    }

    // ── Hover detection ───────────────────────────────────────────────────────

    private void UpdateHoverDetection()
    {
        bool isPreGame = GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby ||
                         GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Countdown;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray     ray      = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));

        _hoveredEnemy = null;
        int myTeam = _pc != null ? _pc.TeamIndex.Value : -1;

        RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
        foreach (var hit in hits)
        {
            // Training dummy — always targetable (lobby practice)
            var dummy = hit.collider.GetComponentInParent<TrainingDummy>();
            if (dummy != null)
            {
                _hoveredEnemy = dummy.transform;
                break;
            }

            if (isPreGame) continue;

            // Enemy player (alive) — in-game only
            var pc = hit.collider.GetComponentInParent<PlayerController>();
            if (pc != null && pc != _pc && pc.TeamIndex.Value != myTeam)
            {
                var ph = pc.GetComponent<PlayerHealth>();
                if (ph == null || ph.IsDead) continue;
                _hoveredEnemy = pc.transform;
                break;
            }

            // Enemy minion — in-game only
            var mh = hit.collider.GetComponentInParent<MinionHealth>();
            if (mh != null && mh.TeamIndexNet.Value != myTeam && mh.Health.Value > 0f)
            {
                _hoveredEnemy = mh.transform;
                break;
            }

            // Enemy structure (tower / crystal) — in-game only
            var sh = hit.collider.GetComponentInParent<StructureHealth>();
            if (sh != null && sh.TeamIndex != myTeam && sh.IsAlive.Value)
            {
                _hoveredEnemy = sh.transform;
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
        bool isPreGame = GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby ||
                         GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Countdown;

        // ── Attack move: Shift + right-click in P&C mode ──────────────────────
        if (isShift && isNewPress && !GameSettings.UseWasd)
        {
            if (GetCursorWorldPos(out Vector3 worldPos))
            {
                ShowAttackMoveIndicator(worldPos);
                Transform enemy = FindNearestEnemyAt(worldPos, autoAttackRange);
                if (enemy != null)
                {
                    var netObj = enemy.GetComponent<NetworkObject>()
                              ?? enemy.GetComponentInParent<NetworkObject>();
                    if (netObj != null)
                    {
                        _target       = enemy;
                        _targetNetObj = netObj;
                        GetComponent<TripleShotAbility>()?.CancelCharge();
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

        // During lobby/countdown only the training dummy is targetable
        if (isPreGame && _hoveredEnemy.GetComponent<TrainingDummy>() == null)
        {
            _hoveredEnemy = null;
            return false;
        }

        // While hovering an enemy, consume the input even on hold to prevent
        // movement orders from firing simultaneously.
        if (!isNewPress) return true;

        var hoverNetObj = _hoveredEnemy.GetComponent<NetworkObject>()
                       ?? _hoveredEnemy.GetComponentInParent<NetworkObject>();
        if (hoverNetObj == null) return false;

        _target       = _hoveredEnemy;
        _targetNetObj = hoverNetObj;
        GetComponent<TripleShotAbility>()?.CancelCharge();
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
        StartTintFadeOut();
        _pc?.StopNavMovement();
        _pc?.CancelMovementLock();
    }

    // ── Auto-attack loop ──────────────────────────────────────────────────────

    private IEnumerator AutoAttackLoop()
    {
        while (_target != null && _targetNetObj != null && _targetNetObj.IsSpawned)
        {
            // Cancel if owner died
            if (_health != null && _health.IsDead)
            {
                _attackCoroutine = null;
                yield break;
            }

            // WASD input cancels the chase
            if (GameSettings.UseWasd && HasWasdInput())
            {
                CancelAutoAttack();
                yield break;
            }

            float dist = Vector3.Distance(transform.position, _target.position);

            if (dist > autoAttackRange)
            {
                _pc.SetChaseDestination(_target.position);
                yield return null;
                continue;
            }

            // In range ────────────────────────────────────────────────────────

            _pc?.StopNavMovement();

            // Pre-wind-up cooldown wait (harmless no-op on the first attack)
            while (Time.time < _nextAttackTime)
            {
                if (_target == null || !_targetNetObj.IsSpawned) yield break;
                if (GameSettings.UseWasd && HasWasdInput()) { CancelAutoAttack(); yield break; }
                if (Vector3.Distance(transform.position, _target.position) > autoAttackRange) break;
                yield return null;
            }

            // If target fled during cooldown wait, resume chase
            if (_target != null && Vector3.Distance(transform.position, _target.position) > autoAttackRange)
            {
                _pc?.SetChaseDestination(_target.position);
                continue;
            }

            // Wind-up: tint builds 0→1 over windUpDuration
            _tintTarget = 1f;
            _tintRate   = 1f / windUpDuration;
            _pc?.LockMovement(windUpDuration);

            float windEnd = Time.time + windUpDuration;
            while (Time.time < windEnd)
            {
                if (_target == null || !_targetNetObj.IsSpawned)
                {
                    _pc?.CancelMovementLock();
                    StartTintFadeOut();
                    _attackCoroutine = null;
                    yield break;
                }

                if (GameSettings.UseWasd && HasWasdInput())
                {
                    _pc?.CancelMovementLock();
                    CancelAutoAttack();   // includes StartTintFadeOut
                    yield break;
                }

                if (Vector3.Distance(transform.position, _target.position) > autoAttackRange)
                {
                    _pc?.CancelMovementLock();
                    StartTintFadeOut();
                    break;
                }

                yield return null;
            }

            // If target fled during wind-up, resume chase (skip fire)
            if (_target != null && Vector3.Distance(transform.position, _target.position) > autoAttackRange)
            {
                _pc?.SetChaseDestination(_target.position);
                continue;
            }

            if (_target == null || !_targetNetObj.IsSpawned)
            {
                _attackCoroutine = null;
                yield break;
            }

            // Fire — fade out over (cooldown - windUpDuration), then charge for windUpDuration
            // so the charge phase flows seamlessly into the wind-up at the same rate.
            float scaledDamage = damage * (1f + 0.1f * ((_xp?.Level.Value ?? 1) - 1));
            FireAutoAttackServerRpc(_targetNetObj.NetworkObjectId, scaledDamage);
            _nextAttackTime = Time.time + autoAttackCooldown;

            float fadeDuration    = autoAttackCooldown - windUpDuration;
            _tintTarget           = 0f;
            _tintRate             = fadeDuration > 0f ? 1f / fadeDuration : float.MaxValue;
            float chargeStartTime = Time.time + fadeDuration;
            bool  chargeStarted   = false;

            while (Time.time < _nextAttackTime)
            {
                if (_target == null || !_targetNetObj.IsSpawned) yield break;
                if (GameSettings.UseWasd && HasWasdInput()) { CancelAutoAttack(); yield break; }

                // Switch from fade-out to charge windUpDuration before cooldown ends
                if (!chargeStarted && Time.time >= chargeStartTime)
                {
                    chargeStarted = true;
                    _tintTarget   = 1f;
                    _tintRate     = 1f / windUpDuration;
                }

                yield return null;
            }
        }

        _attackCoroutine = null;
    }

    // ── Charge tint helpers ───────────────────────────────────────────────────

    /// <summary>Called from Update every frame. Smoothly moves _tintValue and applies to renderer.</summary>
    private void UpdateChargeTint()
    {
        if (_tintRate <= 0f) return;
        _tintValue = Mathf.MoveTowards(_tintValue, _tintTarget, _tintRate * Time.deltaTime);
        if (_renderer == null || _pc == null) return;
        Color baseColor = _pc.PlayerColor.Value;
        var block = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", Color.Lerp(baseColor, Color.white, _tintValue * 0.5f));
        _renderer.SetPropertyBlock(block);
    }

    /// <summary>Begins fading the glow out over half the attack cooldown.</summary>
    private void StartTintFadeOut()
    {
        _tintTarget = 0f;
        _tintRate   = 1f / (autoAttackCooldown * 0.5f);
    }

    private bool HasWasdInput()
    {
        if (Keyboard.current == null) return false;
        return Keyboard.current.wKey.isPressed ||
               Keyboard.current.sKey.isPressed ||
               Keyboard.current.aKey.isPressed ||
               Keyboard.current.dKey.isPressed;
    }


    // ── Server RPC ────────────────────────────────────────────────────────────

    [ServerRpc]
    private void FireAutoAttackServerRpc(ulong targetNetObjId, float dmg)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(targetNetObjId, out var targetNetObj)) return;

        var damageable = targetNetObj.GetComponent<IDamageable>();
        if (damageable == null || damageable.IsImmuneTo(OwnerClientId)) return;

        if (autoAttackProjectilePrefab != null)
        {
            // Homing projectile toward any IDamageable target
            GameObject proj   = Instantiate(autoAttackProjectilePrefab, transform.position, Quaternion.identity);
            var        netObj = proj.GetComponent<NetworkObject>();
            netObj.Spawn(true);
            var controller = proj.GetComponent<HomingProjectileController>();
            controller.Initialize(damageable, targetNetObj.transform, projectileSpeed, OwnerClientId, dmg);
            return;
        }

        // No prefab assigned — fall back to instant damage
        damageable.TakeDamage(dmg, OwnerClientId);
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
            var ph = pc.GetComponent<PlayerHealth>();
            if (ph != null && ph.IsDead) continue;
            float dist = Vector3.Distance(worldPos, pc.transform.position);
            if (dist <= radius && dist < nearestDist)
            {
                nearest = pc.transform;
                nearestDist = dist;
            }
        }

        // Training dummy
        var dummy = TrainingDummy.Instance;
        if (dummy != null)
        {
            float dist = Vector3.Distance(worldPos, dummy.transform.position);
            if (dist <= radius && dist < nearestDist)
            {
                nearest = dummy.transform;
                nearestDist = dist;
            }
        }

        // Enemy minions
        foreach (var mh in FindObjectsByType<MinionHealth>(FindObjectsSortMode.None))
        {
            if (mh.TeamIndexNet.Value == myTeam || mh.Health.Value <= 0f) continue;
            float dist = Vector3.Distance(worldPos, mh.transform.position);
            if (dist <= radius && dist < nearestDist)
            {
                nearest = mh.transform;
                nearestDist = dist;
            }
        }

        // Enemy structures (towers / crystals)
        foreach (var sh in FindObjectsByType<StructureHealth>(FindObjectsSortMode.None))
        {
            if (sh.TeamIndex == myTeam || !sh.IsAlive.Value) continue;
            float dist = Vector3.Distance(worldPos, sh.transform.position);
            if (dist <= radius && dist < nearestDist)
            {
                nearest = sh.transform;
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
