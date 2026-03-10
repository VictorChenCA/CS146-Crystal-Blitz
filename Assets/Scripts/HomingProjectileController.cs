using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative homing projectile for auto-attacks.
/// Tracks the target's current position each frame and applies
/// guaranteed damage on arrival. Despawns if target disappears.
/// </summary>
public class HomingProjectileController : NetworkBehaviour
{
    [SerializeField] private float hitRadius = 0.7f;

    private PlayerHealth _target;
    private float        _speed;
    private ulong        _shooterClientId;
    private float        _damage;
    private bool         _initialized;

    /// <summary>Called by the server immediately after Spawn().</summary>
    public void Initialize(PlayerHealth target, float speed, ulong shooterClientId, float damage)
    {
        _target          = target;
        _speed           = speed;
        _shooterClientId = shooterClientId;
        _damage          = damage;
        _initialized     = true;
    }

    private void Update()
    {
        if (!IsServer || !_initialized) return;

        // Target gone → despawn
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Vector3 targetPos = _target.transform.position;
        float   step      = _speed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, step);

        // Hit
        if (Vector3.Distance(transform.position, targetPos) < hitRadius)
        {
            if (!_target.IsImmuneTo(_shooterClientId))
                _target.TakeDamage(_damage, _shooterClientId);
            NetworkObject.Despawn(true);
        }
    }
}
