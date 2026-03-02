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

    private bool _initialized;
    private ulong _shooterClientId;

    [SerializeField] private float hitRadius = 0.6f;

    // Called by server after NetworkObject.Spawn()
    public void Initialize(Vector3 endPosition, float speed, ulong shooterClientId)
    {
        _netEndPos.Value  = endPosition;
        _netSpeed.Value   = speed;
        _shooterClientId  = shooterClientId;
        _initialized      = true;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            _initialized = true;
    }

    private void Update()
    {
        if (!_initialized) return;

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
                playerObj.GetComponent<PlayerHealth>()?.TakeDamage(25f);
                NetworkObject.Despawn(true);
                return;
            }
        }

        if (Vector3.Distance(transform.position, _netEndPos.Value) < 0.01f)
            NetworkObject.Despawn(true);
    }
}
