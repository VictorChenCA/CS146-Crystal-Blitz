using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class ProjectileShooter : NetworkBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float projectileSpeed = 15f;

    [Header("Cooldown")]
    [SerializeField] private float fireCooldown = 2f;

    private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera _mainCamera;
    private float _nextFireTime;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (firePoint == null) firePoint = transform;
    }

    private void Update()
    {
        if (Mouse.current == null || Keyboard.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame ||
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TryFire();
        }
    }

    private void TryFire()
    {
        if (Time.time < _nextFireTime) return;
        if (_mainCamera == null || projectilePrefab == null) return;

        if (!GetGroundTarget(out Vector3 targetPos)) return;

        // Keep trajectory flat at the firePoint's Y so the projectile
        // flies parallel to the ground regardless of where the mouse lands.
        targetPos.y = firePoint.position.y;

        FireProjectileServerRpc(firePoint.position, targetPos);
        _nextFireTime = Time.time + fireCooldown;
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
