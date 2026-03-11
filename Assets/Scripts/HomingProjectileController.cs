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

    private IDamageable _target;
    private Transform   _targetTransform;
    private float       _speed;
    private ulong       _shooterClientId;
    private float       _damage;
    private bool        _initialized;

    private NetworkVariable<Vector3> _netPos = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Called by the server immediately after Spawn().</summary>
    public void Initialize(IDamageable target, Transform targetTransform, float speed, ulong shooterClientId, float damage)
    {
        _target          = target;
        _targetTransform = targetTransform;
        _speed           = speed;
        _shooterClientId = shooterClientId;
        _damage          = damage;
        _initialized     = true;
    }

    private void Update()
    {
        if (!_initialized) return;

        if (IsServer)
        {
            // Target gone → despawn
            if (_target == null || _targetTransform == null || !_targetTransform.gameObject.activeInHierarchy)
            {
                NetworkObject.Despawn(true);
                return;
            }

            Vector3 targetPos = _targetTransform.position;
            float   step      = _speed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, step);
            _netPos.Value      = transform.position;

            // Hit
            if (Vector3.Distance(transform.position, targetPos) < hitRadius)
            {
                if (!_target.IsImmuneTo(_shooterClientId))
                    _target.TakeDamage(_damage, _shooterClientId);

                // Dismiss tutorial hint only for the shooter's client
                if (_target is TrainingDummy dummy)
                    dummy.NotifyAutoAttackHit(_shooterClientId);

                NetworkObject.Despawn(true);
            }
        }
        else
        {
            // Clients: follow the synced position
            transform.position = _netPos.Value;
        }
    }
}
