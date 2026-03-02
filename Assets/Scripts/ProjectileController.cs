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

    // Called by server after NetworkObject.Spawn()
    public void Initialize(Vector3 endPosition, float speed)
    {
        _netEndPos.Value = endPosition;
        _netSpeed.Value = speed;
        _initialized = true;
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
        transform.position = Vector3.MoveTowards(
            transform.position,
            _netEndPos.Value,
            step
        );

        if (IsServer && Vector3.Distance(transform.position, _netEndPos.Value) < 0.01f)
        {
            NetworkObject.Despawn(true);
        }
    }
}
