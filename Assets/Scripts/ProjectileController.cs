using UnityEngine;
using Unity.Netcode;

public class ProjectileController : NetworkBehaviour
{
    private NetworkVariable<Vector3> _netEndPos =
        new NetworkVariable<Vector3>(default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private NetworkVariable<float> _netSpeed =
        new NetworkVariable<float>(15f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> _netActive =
        new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private ulong _shooterClientId;
    private float _damage;

    [SerializeField] private float hitRadius = 0.6f;

    // Called by server after NetworkObject.Spawn()
    public void Initialize(Vector3 endPosition, float speed, ulong shooterClientId, float damage,
                           float sizeMultiplier = 1f)
    {
        endPosition.y    = transform.position.y;  // lock to spawn height — XZ travel only
        _netEndPos.Value = endPosition;
        _netSpeed.Value  = speed;
        _netActive.Value = true;
        _shooterClientId = shooterClientId;
        _damage          = damage;
        if (!Mathf.Approximately(sizeMultiplier, 1f))
            transform.localScale *= sizeMultiplier;
    }

    public override void OnNetworkSpawn() { }

    private void Update()
    {
        if (!_netActive.Value) return;

        float step = _netSpeed.Value * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, _netEndPos.Value, step);

        if (!IsServer) return;

        // Check for player hits (server-authoritative, no collider needed on projectile).
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == _shooterClientId) continue;   // no self-damage

            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            if (Vector3.Distance(transform.position, playerObj.transform.position) < hitRadius)
            {
                var dmg = playerObj.GetComponent<IDamageable>();
                if (dmg != null && !dmg.IsImmuneTo(_shooterClientId))
                {
                    dmg.TakeDamage(_damage, _shooterClientId);
                    NetworkObject.Despawn(true);
                    return;
                }
            }
        }

        // Check for training dummy
        var dummy = TrainingDummy.Instance;
        if (dummy != null && Vector3.Distance(transform.position, dummy.transform.position) < hitRadius)
        {
            dummy.TakeDamage(_damage, _shooterClientId);
            NetworkObject.Despawn(true);
            return;
        }

        // Check for structures (towers + crystals)
        foreach (var s in FindObjectsByType<StructureHealth>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(transform.position, s.transform.position) < hitRadius + 1f)
            {
                s.TakeDamage(_damage, _shooterClientId);
                NetworkObject.Despawn(true);
                return;
            }
        }

        // Check for minions
        foreach (var mh in FindObjectsByType<MinionHealth>(FindObjectsSortMode.None))
        {
            if (mh.Health.Value <= 0f) continue;
            if (Vector3.Distance(transform.position, mh.transform.position) < hitRadius)
            {
                if (!mh.IsImmuneTo(_shooterClientId))
                {
                    mh.TakeDamage(_damage, _shooterClientId);
                    NetworkObject.Despawn(true);
                    return;
                }
            }
        }

        if (Vector3.Distance(transform.position, _netEndPos.Value) < 0.01f)
            NetworkObject.Despawn(true);
    }
}
