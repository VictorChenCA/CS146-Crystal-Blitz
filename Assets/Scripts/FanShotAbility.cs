using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Tank-only (CharacterIndex 0) fan-shot ability.
/// E key fires 5 projectiles in a −40°/−20°/0°/+20°/+40° fan.
/// </summary>
public class FanShotAbility : NetworkBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform  firePoint;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private float maxRange        = 20f;
    [SerializeField] private float damage          = 15f;
    [SerializeField] private float cooldown        = 10f;
    [SerializeField] private float launchOffset    = 0.5f;
    [SerializeField] private float manaCost        = 30f;
    [Tooltip("Half-angle of the fan spread in degrees. Bullets span from -spreadRadius to +spreadRadius.")]
    [SerializeField] private float spreadRadius    = 40f;

    [Header("Cast Timing")]
    [SerializeField] private float castDuration      = 0.5f;
    [SerializeField] private float animationDuration = 0.2f;

    [Header("Aim Arrow Visual")]
    [SerializeField] private float arrowShaftWidth  = 0.12f;
    [SerializeField] private float arrowHeadLength  = 1.2f;
    [SerializeField] private float arrowHeadWidth   = 0.5f;

    [Header("Orbit Visual")]
    [SerializeField] private float orbitRadius   = 1.5f;
    [SerializeField] private float orbitSpeed    = 180f;
    [SerializeField] private float orbitBallSize = 0.25f;

    private const int ProjectileCount = 5;
    private const int RingSegments    = 64;

    public float CooldownFraction  => Mathf.Clamp01((_nextFireTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextFireTime - Time.time);
    public bool  IsCharging        => _charging;   // true during aim phase only
    public float CastFraction      => _castFraction;
    public float ManaCost          => manaCost;

    private readonly Plane   _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera           _mainCamera;
    private float            _nextFireTime;
    private bool             _charging;    // aim phase flag
    private bool             _casting;     // cast phase flag
    private float            _castFraction;
    private float            _orbitAngle;
    private Coroutine        _castCoroutine;
    private PlayerHealth     _health;
    private PlayerMana       _mana;
    private PlayerXP         _xp;
    private PlayerController _pc;

    private GameObject[] _orbitBalls;
    private LineRenderer  _rangeRing;
    private LineRenderer[] _aimArrows;

    public override void OnNetworkSpawn()
    {
        _pc = GetComponent<PlayerController>();
        CreateOrbitBalls();
        CreateRangeRing();
        CreateAimArrows();

        if (!IsOwner) { enabled = false; return; }
        _mana = GetComponent<PlayerMana>();
        _xp   = GetComponent<PlayerXP>();
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (firePoint == null) firePoint = transform;
    }

    public override void OnNetworkDespawn()
    {
        if (_orbitBalls == null) return;
        foreach (var ball in _orbitBalls)
            if (ball != null) Destroy(ball);
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;
        if (_pc != null && _pc.CharacterIndex.Value != 0) { CancelCharge(); return; }  // Tank only
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null && _health.IsDead) return;

        bool pressed  = GameKeybinds.WasPressedThisFrame(GameSettings.UseWasd ? GameKeybinds.Wasd_Ability3 : GameKeybinds.PnC_Ability3);
        bool released = GameKeybinds.WasReleasedThisFrame(GameSettings.UseWasd ? GameKeybinds.Wasd_Ability3 : GameKeybinds.PnC_Ability3);

        if (pressed && Time.time >= _nextFireTime && _castCoroutine == null)
        {
            if (_mana != null && !_mana.HasMana(manaCost)) return;
            StartCharge();
        }

        if (_charging)
        {
            UpdateRingPosition();
            UpdateAimArrows();

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelCharge();
                return;
            }
        }

        if (released && _charging)
        {
            if (GetFireTarget(out Vector3 targetPos))
            {
                _charging = false;
                if (_rangeRing != null) _rangeRing.enabled = false;
                SetAimArrowsVisible(false);
                _castCoroutine = StartCoroutine(CastSequence(targetPos));
            }
            else
            {
                CancelCharge();
            }
        }
    }

    private void StartCharge()
    {
        _charging          = true;
        _rangeRing.enabled = true;
        SetAimArrowsVisible(true);
    }

    public void CancelCharge()
    {
        _charging = false;
        if (_castCoroutine != null)
        {
            StopCoroutine(_castCoroutine);
            _castCoroutine = null;
        }
        _casting      = false;
        _castFraction = 0f;
        SetOrbitBallsVisible(false);
        if (_rangeRing != null) _rangeRing.enabled = false;
        SetAimArrowsVisible(false);
        BroadcastFanChargeEndRpc();
    }

    // ── Non-owner orbit animations ────────────────────────────────────────────

    [Rpc(SendTo.NotOwner)]
    private void BroadcastFanChargeStartRpc(Vector3 pos)
    {
        StartCoroutine(NonOwnerOrbitSpin());
    }

    [Rpc(SendTo.NotOwner)]
    private void BroadcastFanChargeEndRpc()
    {
        SetOrbitBallsVisible(false);
    }

    private IEnumerator NonOwnerOrbitSpin()
    {
        float spinEnd = Time.time + castDuration;
        SetOrbitBallsVisible(true);
        while (Time.time < spinEnd)
        {
            _orbitAngle += orbitSpeed * Time.deltaTime;
            UpdateOrbitBalls();
            yield return null;
        }
        SetOrbitBallsVisible(false);
    }

    // ── Cast sequence ─────────────────────────────────────────────────────────

    private IEnumerator CastSequence(Vector3 targetPos)
    {
        GetComponent<AutoAttacker>()?.CancelAutoAttack();

        Vector3 dir = targetPos - (firePoint != null ? firePoint.position : transform.position);
        dir.y = 0f;
        dir   = dir.magnitude > 0.001f ? dir.normalized : Vector3.forward;

        _orbitAngle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;

        _casting = true;
        SetOrbitBallsVisible(true);
        BroadcastFanChargeStartRpc(transform.position);

        // Cast phase: orbit spin fills castDuration, _castFraction 0→1
        float castEnd = Time.time + castDuration;
        while (Time.time < castEnd)
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                _castFraction = 0f;
                _casting      = false;
                SetOrbitBallsVisible(false);
                BroadcastFanChargeEndRpc();
                _castCoroutine = null;
                yield break;
            }
            _castFraction = castDuration > 0f ? 1f - (castEnd - Time.time) / castDuration : 1f;
            _orbitAngle  += orbitSpeed * Time.deltaTime;
            UpdateOrbitBalls();
            yield return null;
        }

        _castFraction = 0f;
        _casting      = false;
        SetOrbitBallsVisible(false);
        BroadcastFanChargeEndRpc();

        // Apply effect
        _nextFireTime = Time.time + cooldown;
        _mana?.SpendManaServerRpc(manaCost);
        TutorialManager.OnEFired?.Invoke();
        float scaledDamage = damage * (1f + 0.1f * ((_xp?.Level.Value ?? 1) - 1));

        Vector3 fp = firePoint != null ? firePoint.position : transform.position;
        for (int i = 0; i < ProjectileCount; i++)
        {
            float fanAngle    = ProjectileCount > 1
                ? -spreadRadius + i * (2f * spreadRadius / (ProjectileCount - 1))
                : 0f;
            Quaternion rot    = Quaternion.AngleAxis(fanAngle, Vector3.up);
            Vector3    fanDir = rot * dir;
            Vector3 startPos  = new Vector3(
                fp.x + fanDir.x * launchOffset, fp.y, fp.z + fanDir.z * launchOffset);
            Vector3 endPos    = new Vector3(
                fp.x + fanDir.x * maxRange, fp.y, fp.z + fanDir.z * maxRange);
            FireProjectileServerRpc(startPos, endPos, scaledDamage);
        }

        // Animation phase
        if (animationDuration > 0f)
            yield return new WaitForSeconds(animationDuration);

        _castCoroutine = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool GetFireTarget(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (_mainCamera == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
        if (_groundPlane.Raycast(ray, out float dist))
        {
            worldPoint = ray.GetPoint(dist);
            return true;
        }
        return false;
    }

    private void SetOrbitBallsVisible(bool visible)
    {
        if (_orbitBalls == null) return;
        foreach (var ball in _orbitBalls)
            if (ball != null) ball.SetActive(visible);
    }

    private void UpdateOrbitBalls()
    {
        if (_orbitBalls == null) return;
        float ballY = firePoint != null ? firePoint.position.y : transform.position.y;
        for (int i = 0; i < _orbitBalls.Length; i++)
        {
            if (_orbitBalls[i] == null) continue;
            float angle = (_orbitAngle + i * 72f) * Mathf.Deg2Rad;
            _orbitBalls[i].transform.position = new Vector3(
                transform.position.x + Mathf.Cos(angle) * orbitRadius,
                ballY,
                transform.position.z + Mathf.Sin(angle) * orbitRadius);
        }
    }

    private void UpdateRingPosition()
    {
        Vector3 center = new Vector3(transform.position.x, 0.05f, transform.position.z);
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (float)i / RingSegments * Mathf.PI * 2f;
            _rangeRing.SetPosition(i, center + new Vector3(
                Mathf.Cos(angle) * maxRange, 0f, Mathf.Sin(angle) * maxRange));
        }
    }

    private void UpdateAimArrows()
    {
        if (_aimArrows == null || _mainCamera == null) return;

        Vector3 fp     = firePoint != null ? firePoint.position : transform.position;
        Vector3 origin = new Vector3(fp.x, 0.05f, fp.z);

        Vector3 baseDir = Vector3.forward;
        if (GetFireTarget(out Vector3 targetPos))
        {
            Vector3 flat = new Vector3(targetPos.x - fp.x, 0f, targetPos.z - fp.z);
            if (flat.magnitude > 0.001f) baseDir = flat.normalized;
        }

        for (int i = 0; i < ProjectileCount; i++)
        {
            float   fanAngle = ProjectileCount > 1
                ? -spreadRadius + i * (2f * spreadRadius / (ProjectileCount - 1))
                : 0f;
            Vector3 dir      = Quaternion.AngleAxis(fanAngle, Vector3.up) * baseDir;
            float   length   = maxRange;

            Vector3 tip      = origin + dir * length;
            Vector3 shaftEnd = origin + dir * (length - arrowHeadLength);
            Vector3 perp     = new Vector3(-dir.z, 0f, dir.x) * (arrowHeadWidth * 0.5f);

            var lr = _aimArrows[i];
            lr.SetPosition(0, origin);
            lr.SetPosition(1, shaftEnd);
            lr.SetPosition(2, shaftEnd + perp);
            lr.SetPosition(3, tip);
            lr.SetPosition(4, shaftEnd - perp);
            lr.SetPosition(5, shaftEnd);
        }
    }

    private void SetAimArrowsVisible(bool visible)
    {
        if (_aimArrows == null) return;
        foreach (var lr in _aimArrows)
            if (lr != null) lr.enabled = visible;
    }

    // ── Visual creation ───────────────────────────────────────────────────────

    private void CreateOrbitBalls()
    {
        Material projMat = null;
        if (projectilePrefab != null)
        {
            var r = projectilePrefab.GetComponent<Renderer>();
            if (r != null) projMat = r.sharedMaterial;
        }

        _orbitBalls = new GameObject[5];
        for (int i = 0; i < 5; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * orbitBallSize;
            if (projMat != null) go.GetComponent<Renderer>().material = projMat;
            go.SetActive(false);
            _orbitBalls[i] = go;
        }
    }

    private void CreateRangeRing()
    {
        var ringObj = new GameObject("FanShotRangeRing");
        ringObj.transform.SetParent(transform);

        _rangeRing = ringObj.AddComponent<LineRenderer>();
        _rangeRing.loop              = true;
        _rangeRing.positionCount     = RingSegments;
        _rangeRing.startWidth        = 0.12f;
        _rangeRing.endWidth          = 0.12f;
        _rangeRing.useWorldSpace     = true;
        _rangeRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rangeRing.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.8f, 0.4f, 1f, 0.5f);
        _rangeRing.material = mat;

        _rangeRing.enabled = false;
    }

    private void CreateAimArrows()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.8f, 0.4f, 1f, 0.7f);

        _aimArrows = new LineRenderer[ProjectileCount];
        for (int i = 0; i < ProjectileCount; i++)
        {
            var go = new GameObject($"FanShotAimArrow_{i}");
            go.transform.SetParent(transform);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount     = 6;
            lr.loop              = false;
            lr.useWorldSpace     = true;
            lr.startWidth        = arrowShaftWidth;
            lr.endWidth          = arrowShaftWidth;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.material          = mat;
            lr.enabled           = false;

            _aimArrows[i] = lr;
        }
    }

    [ServerRpc]
    private void FireProjectileServerRpc(Vector3 startPos, Vector3 endPos, float damageAmount)
    {
        if (projectilePrefab == null) return;
        var proj   = Instantiate(projectilePrefab, startPos, Quaternion.identity);
        var netObj = proj.GetComponent<NetworkObject>();
        netObj.Spawn(true);
        proj.GetComponent<ProjectileController>()
            .Initialize(endPos, projectileSpeed, OwnerClientId, damageAmount);
    }
}
