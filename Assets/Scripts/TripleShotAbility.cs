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

    public float CooldownFraction  => Mathf.Clamp01((_nextFireTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextFireTime - Time.time);

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera    _mainCamera;
    private float     _nextFireTime;
    private bool      _charging;
    private Coroutine _fireCoroutine;
    private PlayerHealth _health;

    private LineRenderer _rangeRing;
    private LineRenderer _trajectoryLine;
    private const int RingSegments = 64;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        CreateRangeRing();
        CreateTrajectoryLine();
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
            CancelCharge();
            return;
        }

        if (released && _charging)
        {
            CancelCharge();
            if (GetFireTarget(out Vector3 targetPos))
            {
                _nextFireTime  = Time.time + cooldown;
                _fireCoroutine = StartCoroutine(FireSequence(targetPos));
            }
        }
    }

    private void CancelCharge()
    {
        _charging               = false;
        _rangeRing.enabled      = false;
        _trajectoryLine.enabled = false;
    }

    private IEnumerator FireSequence(Vector3 targetPos)
    {
        var pc = GetComponent<PlayerController>();
        pc?.LockMovement(delayBetweenShots * 2f + 0.1f);
        GetComponent<AutoAttacker>()?.CancelAutoAttack();

        Vector3 dir = targetPos - firePoint.position;
        dir.y = 0f;
        dir   = dir.magnitude > 0.001f ? dir.normalized : firePoint.forward;

        for (int i = 0; i < 3; i++)
        {
            Vector3 startPos = firePoint.position + dir * launchOffset;
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
        mat.color       = new Color(1f, 0.6f, 0f, 0.5f);
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
