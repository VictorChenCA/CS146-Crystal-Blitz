using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class ProjectileShooter : NetworkBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private float maxRange = 20f;
    [SerializeField] private float damage = 25f;

    [Header("Cooldown")]
    [SerializeField] private float fireCooldown = 2f;

    [Header("Attack Timing")]
    [SerializeField] private float castDelay      = 0.4f;
    [SerializeField] private float animationDelay = 0.4f;

    [Header("Launch")]
    [SerializeField] private float launchOffset = 0.5f;

    public float CooldownFraction  => Mathf.Clamp01((_nextFireTime - Time.time) / fireCooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextFireTime - Time.time);
    public float CastFraction      => _castFraction;

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera _mainCamera;
    private float _nextFireTime;
    private float _castFraction;
    private bool _charging;
    private Coroutine _attackCoroutine;

    private LineRenderer _rangeRing;
    private LineRenderer _trajectoryLine;
    private const int RingSegments = 64;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }
        CreateRangeRing();
        CreateTrajectoryLine();
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (firePoint == null) firePoint = transform;
    }

    private PlayerHealth _health;

    private void Update()
    {
        if (Mouse.current == null) return;
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null && _health.IsDead) return;

        bool firePressed  = Mouse.current.leftButton.wasPressedThisFrame
                         || (Keyboard.current != null && (GameSettings.UseWasd
                             ? Keyboard.current.spaceKey.wasPressedThisFrame
                             : Keyboard.current.qKey.wasPressedThisFrame));
        bool fireReleased = Mouse.current.leftButton.wasReleasedThisFrame
                         || (Keyboard.current != null && (GameSettings.UseWasd
                             ? Keyboard.current.spaceKey.wasReleasedThisFrame
                             : Keyboard.current.qKey.wasReleasedThisFrame));

        if (firePressed && Time.time >= _nextFireTime && _attackCoroutine == null)
            _charging = true;

        if (_charging)
        {
            UpdateRingPosition();
            UpdateTrajectory();
            _rangeRing.enabled      = true;
            _trajectoryLine.enabled = true;
        }

        // Right-click cancels the charge without firing
        if (_charging && Mouse.current.rightButton.wasPressedThisFrame)
        {
            _charging               = false;
            _rangeRing.enabled      = false;
            _trajectoryLine.enabled = false;
            return;
        }

        if (fireReleased && _charging)
        {
            _charging = false;
            _rangeRing.enabled      = false;
            _trajectoryLine.enabled = false;

            if (SnapFireTarget(out Vector3 targetPos))
            {
                if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
                _attackCoroutine = StartCoroutine(AttackSequence(targetPos));
            }
        }
    }

    private bool SnapFireTarget(out Vector3 targetPos)
    {
        targetPos = Vector3.zero;
        if (_mainCamera == null || projectilePrefab == null) return false;
        if (!GetGroundTarget(out Vector3 hit)) return false;

        Vector3 fp   = firePoint.position;
        Vector3 flat = new Vector3(hit.x - fp.x, 0f, hit.z - fp.z);
        flat = flat.magnitude > 0.001f ? flat.normalized * maxRange : Vector3.forward * maxRange;

        targetPos = new Vector3(fp.x + flat.x, fp.y, fp.z + flat.z);
        return true;
    }

    private IEnumerator AttackSequence(Vector3 targetPos)
    {
        var pc = GetComponent<PlayerController>();
        pc?.LockMovement(castDelay + animationDelay);

        // Compute offset start position toward target
        Vector3 castDir   = targetPos - firePoint.position;
        castDir.y = 0f;
        castDir   = castDir.magnitude > 0.001f ? castDir.normalized : firePoint.forward;
        Vector3 startPos  = firePoint.position + castDir * launchOffset;

        // Phase 1: Cast (0.25 s) — local preview sphere grows from 0 to full size
        Vector3 fullScale = projectilePrefab != null
            ? projectilePrefab.transform.localScale
            : Vector3.one * 0.3f;

        GameObject preview = CreatePreviewSphere();
        preview.transform.position = startPos;
        var previewRenderer = preview.GetComponent<Renderer>();

        float castEnd = Time.time + castDelay;
        while (Time.time < castEnd)
        {
            if (HasMovementInput())
            {
                Destroy(preview);
                pc?.CancelMovementLock();
                _castFraction    = 0f;
                _attackCoroutine = null;
                yield break;
            }
            float t = 1f - (castEnd - Time.time) / castDelay;
            _castFraction = t;
            preview.transform.localScale = Vector3.Lerp(Vector3.zero, fullScale, t);
            previewRenderer.material.color = Color.Lerp(Color.white, Color.yellow, Mathf.Pow(t, 3f));
            yield return null;
        }

        _castFraction = 0f;
        Destroy(preview);

        // Fire at end of cast phase — cooldown starts here
        FireProjectileServerRpc(startPos, targetPos, damage);
        _nextFireTime = Time.time + fireCooldown;

        // Phase 2: Animation delay — projectile in flight, can cancel lock early
        float animEnd = Time.time + animationDelay;
        while (Time.time < animEnd)
        {
            if (HasMovementInput())
            {
                pc?.CancelMovementLock();
                _attackCoroutine = null;
                yield break;
            }
            yield return null;
        }

        _attackCoroutine = null;
    }

    private GameObject CreatePreviewSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.zero;

        var projRenderer = projectilePrefab != null ? projectilePrefab.GetComponent<Renderer>() : null;
        if (projRenderer != null)
            go.GetComponent<Renderer>().material = projRenderer.sharedMaterial;

        return go;
    }

    private bool HasMovementInput()
    {
        if (Keyboard.current == null) return false;
        if (GameSettings.UseWasd)
            return Keyboard.current.wKey.wasPressedThisFrame ||
                   Keyboard.current.sKey.wasPressedThisFrame ||
                   Keyboard.current.aKey.wasPressedThisFrame ||
                   Keyboard.current.dKey.wasPressedThisFrame;
        // P&C mode: S (stop) or right-click (new move order) cancels the cast
        return Keyboard.current.sKey.wasPressedThisFrame ||
               (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
    }

    private void UpdateRingPosition()
    {
        Vector3 center = new Vector3(transform.position.x, 0.05f, transform.position.z);
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (float)i / RingSegments * Mathf.PI * 2f;
            _rangeRing.SetPosition(i, center + new Vector3(
                Mathf.Cos(angle) * maxRange,
                0f,
                Mathf.Sin(angle) * maxRange
            ));
        }
    }

    private void UpdateTrajectory()
    {
        if (_mainCamera == null) return;

        Vector3 fp    = firePoint != null ? firePoint.position : transform.position;
        Vector3 start = new Vector3(fp.x, 0.05f, fp.z);
        Vector3 end   = start;

        if (GetGroundTarget(out Vector3 targetPos))
        {
            Vector3 flat = new Vector3(targetPos.x - fp.x, 0f, targetPos.z - fp.z);
            flat = flat.magnitude > 0.001f ? flat.normalized * maxRange : Vector3.forward * maxRange;
            end = new Vector3(fp.x + flat.x, 0.05f, fp.z + flat.z);
        }

        _trajectoryLine.SetPosition(0, start);
        _trajectoryLine.SetPosition(1, end);
    }

    private void CreateRangeRing()
    {
        var ringObj = new GameObject("RangeRing");
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
        mat.color = new Color(1f, 0.6f, 0f, 0.5f);
        _rangeRing.material = mat;

        UpdateRingPosition();
        _rangeRing.enabled = false;
    }

    private void CreateTrajectoryLine()
    {
        var lineObj = new GameObject("TrajectoryLine");
        lineObj.transform.SetParent(transform);

        _trajectoryLine = lineObj.AddComponent<LineRenderer>();
        _trajectoryLine.positionCount     = 2;
        _trajectoryLine.startWidth        = 0.2f;
        _trajectoryLine.endWidth          = 0.08f;  // taper toward target
        _trajectoryLine.useWorldSpace     = true;
        _trajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _trajectoryLine.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.4f, 0.85f, 1f, 0.6f);  // light blue
        _trajectoryLine.material = mat;

        _trajectoryLine.enabled = false;
    }

    private bool GetGroundTarget(out Vector3 worldPoint)
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));

        if (_groundPlane.Raycast(ray, out float dist))
        {
            worldPoint = ray.GetPoint(dist);
            return true;
        }

        worldPoint = Vector3.zero;
        return false;
    }

    [ServerRpc]
    private void FireProjectileServerRpc(Vector3 startPos, Vector3 endPos, float damageAmount)
    {
        GameObject proj = Instantiate(projectilePrefab, startPos, Quaternion.identity);
        NetworkObject netObj = proj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        ProjectileController controller = proj.GetComponent<ProjectileController>();
        controller.Initialize(endPos, projectileSpeed, OwnerClientId, damageAmount);
    }
}
