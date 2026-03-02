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

    [Header("Cooldown")]
    [SerializeField] private float fireCooldown = 2f;

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera _mainCamera;
    private float _nextFireTime;
    private bool _charging;

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

    private void Update()
    {
        if (Mouse.current == null) return;

        bool firePressed  = Mouse.current.leftButton.wasPressedThisFrame
                         || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
        bool fireReleased = Mouse.current.leftButton.wasReleasedThisFrame
                         || (Keyboard.current != null && Keyboard.current.spaceKey.wasReleasedThisFrame);

        if (firePressed && Time.time >= _nextFireTime)
            _charging = true;

        if (_charging)
        {
            UpdateRingPosition();
            UpdateTrajectory();
            _rangeRing.enabled      = true;
            _trajectoryLine.enabled = true;
        }

        if (fireReleased && _charging)
        {
            _charging = false;
            _rangeRing.enabled      = false;
            _trajectoryLine.enabled = false;
            TryFire();
        }
    }

    private void TryFire()
    {
        if (_mainCamera == null || projectilePrefab == null) return;
        if (!GetGroundTarget(out Vector3 targetPos)) return;

        Vector3 fp   = firePoint.position;
        Vector3 flat = new Vector3(targetPos.x - fp.x, 0f, targetPos.z - fp.z);
        if (flat.magnitude > maxRange)
            flat = flat.normalized * maxRange;

        targetPos = new Vector3(fp.x + flat.x, fp.y, fp.z + flat.z);

        FireProjectileServerRpc(fp, targetPos);
        _nextFireTime = Time.time + fireCooldown;
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
            if (flat.magnitude > maxRange)
                flat = flat.normalized * maxRange;
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
    private void FireProjectileServerRpc(Vector3 startPos, Vector3 endPos)
    {
        GameObject proj = Instantiate(projectilePrefab, startPos, Quaternion.identity);
        NetworkObject netObj = proj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        ProjectileController controller = proj.GetComponent<ProjectileController>();
        controller.Initialize(endPos, projectileSpeed);
    }
}
