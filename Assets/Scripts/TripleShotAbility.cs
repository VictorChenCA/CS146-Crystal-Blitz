using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class TripleShotAbility : NetworkBehaviour
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform  firePoint;
    [SerializeField] private float projectileSpeed   = 15f;
    [SerializeField] private float maxRange          = 20f;
    [SerializeField] private float damage            = 15f;
    [SerializeField] private float cooldown          = 10f;
    [SerializeField] private float delayBetweenShots = 0.15f;
    [SerializeField] private float launchOffset      = 0.5f;

    [Header("Orbit Visual")]
    [SerializeField] private float orbitRadius   = 1.5f;
    [SerializeField] private float orbitSpeed    = 180f;  // degrees per second
    [SerializeField] private float orbitBallSize = 0.25f;

    public float CooldownFraction  => Mathf.Clamp01((_nextFireTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextFireTime - Time.time);

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera    _mainCamera;
    private float     _nextFireTime;
    private bool      _charging;
    private float     _orbitAngle;
    private Coroutine _fireCoroutine;
    private PlayerHealth _health;

    private GameObject[] _orbitBalls;
    private LineRenderer  _rangeRing;
    private LineRenderer  _trajectoryLine;
    private const int RingSegments = 64;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        CreateRangeRing();
        CreateTrajectoryLine();
        CreateOrbitBalls();
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (firePoint == null) firePoint = transform;
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_health != null && _health.IsDead) return;

        bool pressed  = Keyboard.current.eKey.wasPressedThisFrame;
        bool released = Keyboard.current.eKey.wasReleasedThisFrame;

        if (pressed && Time.time >= _nextFireTime && _fireCoroutine == null)
            StartCharge();

        if (_charging)
        {
            UpdateRingPosition();
            UpdateTrajectory();
        }

        if (_charging && Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelCharge();
            return;
        }

        if (released && _charging)
        {
            if (GetFireTarget(out Vector3 targetPos))
            {
                CancelCharge();
                _nextFireTime  = Time.time + cooldown;
                _fireCoroutine = StartCoroutine(FireSequence(targetPos));
            }
            else
            {
                CancelCharge();
            }
        }
    }

    private void StartCharge()
    {
        _charging               = true;
        _rangeRing.enabled      = true;
        _trajectoryLine.enabled = true;
    }

    /// <summary>Called externally by ProjectileShooter / AutoAttacker to cancel the cast.</summary>
    public void CancelCharge()
    {
        _charging               = false;
        SetOrbitBallsVisible(false);
        _rangeRing.enabled      = false;
        _trajectoryLine.enabled = false;
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
        float ballY = (firePoint != null ? firePoint.position.y : transform.position.y);
        for (int i = 0; i < _orbitBalls.Length; i++)
        {
            if (_orbitBalls[i] == null) continue;
            float angle = (_orbitAngle + i * 120f) * Mathf.Deg2Rad;
            _orbitBalls[i].transform.position = new Vector3(
                transform.position.x + Mathf.Cos(angle) * orbitRadius,
                ballY,
                transform.position.z + Mathf.Sin(angle) * orbitRadius);
        }
    }

    private IEnumerator FireSequence(Vector3 targetPos)
    {
        // E can be cast while moving — no movement lock
        GetComponent<AutoAttacker>()?.CancelAutoAttack();

        Vector3 dir = targetPos - firePoint.position;
        dir.y = 0f;
        dir   = dir.magnitude > 0.001f ? dir.normalized : firePoint.forward;

        // Seed orbit angle toward the fire direction so balls start "in front"
        _orbitAngle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;

        // Show balls and spin 360°
        SetOrbitBallsVisible(true);
        float spinDuration = 360f / orbitSpeed;
        float spinEnd      = Time.time + spinDuration;
        while (Time.time < spinEnd)
        {
            _orbitAngle += orbitSpeed * Time.deltaTime;
            UpdateOrbitBalls();
            yield return null;
        }
        SetOrbitBallsVisible(false);

        // Fire 3 projectiles in sequence
        for (int i = 0; i < 3; i++)
        {
            Vector3 startPos = new Vector3(
                firePoint.position.x + dir.x * launchOffset,
                firePoint.position.y,
                firePoint.position.z + dir.z * launchOffset);
            Vector3 endPos   = new Vector3(
                firePoint.position.x + dir.x * maxRange,
                firePoint.position.y,
                firePoint.position.z + dir.z * maxRange);
            FireProjectileServerRpc(startPos, endPos, damage);
            if (i < 2) yield return new WaitForSeconds(delayBetweenShots);
        }

        _fireCoroutine = null;
    }

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

    private void UpdateTrajectory()
    {
        if (_mainCamera == null) return;
        Vector3 fp    = firePoint != null ? firePoint.position : transform.position;
        Vector3 start = new Vector3(fp.x, 0.05f, fp.z);
        Vector3 end   = start;

        if (GetFireTarget(out Vector3 targetPos))
        {
            Vector3 flat = new Vector3(targetPos.x - fp.x, 0f, targetPos.z - fp.z);
            flat = flat.magnitude > 0.001f ? flat.normalized * maxRange : Vector3.forward * maxRange;
            end  = new Vector3(fp.x + flat.x, 0.05f, fp.z + flat.z);
        }

        _trajectoryLine.SetPosition(0, start);
        _trajectoryLine.SetPosition(1, end);
    }

    private void CreateOrbitBalls()
    {
        Material projMat = null;
        if (projectilePrefab != null)
        {
            var r = projectilePrefab.GetComponent<Renderer>();
            if (r != null) projMat = r.sharedMaterial;
        }

        _orbitBalls = new GameObject[3];
        for (int i = 0; i < 3; i++)
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
        var ringObj = new GameObject("TripleShotRangeRing");
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
        var lineObj = new GameObject("TripleShotTrajectoryLine");
        lineObj.transform.SetParent(transform);

        _trajectoryLine = lineObj.AddComponent<LineRenderer>();
        _trajectoryLine.positionCount     = 2;
        _trajectoryLine.startWidth        = 0.2f;
        _trajectoryLine.endWidth          = 0.08f;
        _trajectoryLine.useWorldSpace     = true;
        _trajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _trajectoryLine.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.4f, 0.85f, 1f, 0.6f);
        _trajectoryLine.material = mat;

        _trajectoryLine.enabled = false;
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
