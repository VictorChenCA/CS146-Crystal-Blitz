using UnityEngine;
using Unity.Netcode;

public class PlayerMana : NetworkBehaviour
{
    [SerializeField] private float baseMana              = 100f;
    [SerializeField] private float manaRegenPerSecond   = 3f;
    [SerializeField] private float spawnRegenPerTick     = 5f;
    [SerializeField] private float spawnRegenTickRate    = 4f;

    public NetworkVariable<float> Mana = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> MaxMana = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public float ManaFraction => MaxMana.Value > 0f ? Mathf.Clamp01(Mana.Value / MaxMana.Value) : 0f;

    private float     _regenAccum;
    private float     _spawnRegenTimer;
    private Transform _cachedSpawnTransform;
    private int       _cachedSpawnTeam = -99;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        Mana.Value    = baseMana;
        MaxMana.Value = baseMana;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (Mana.Value < MaxMana.Value)
        {
            _regenAccum += manaRegenPerSecond * Time.deltaTime;
            if (_regenAccum >= 1f)
            {
                float toAdd  = Mathf.Floor(_regenAccum);
                _regenAccum -= toAdd;
                Mana.Value   = Mathf.Min(MaxMana.Value, Mana.Value + toAdd);
            }
        }

        if (IsNearTeamSpawn())
        {
            _spawnRegenTimer += Time.deltaTime;
            float tickInterval = 1f / spawnRegenTickRate;
            while (_spawnRegenTimer >= tickInterval)
            {
                _spawnRegenTimer -= tickInterval;
                Mana.Value        = Mathf.Min(MaxMana.Value, Mana.Value + spawnRegenPerTick);
            }
        }
        else
        {
            _spawnRegenTimer = 0f;
        }
    }

    private bool IsNearTeamSpawn()
    {
        var pc = GetComponent<PlayerController>();
        if (pc == null) return false;
        int team = pc.TeamIndex.Value;
        if (team < 0) return false;

        if (_cachedSpawnTeam != team || _cachedSpawnTransform == null)
        {
            _cachedSpawnTeam = team;
            string spawnName = team == 0 ? "Blue Spawn" : "Red Spawn";
            var go = GameObject.Find(spawnName);
            _cachedSpawnTransform = go != null ? go.transform : null;
        }

        if (_cachedSpawnTransform == null) return false;

        Vector3 spawnXZ  = new Vector3(_cachedSpawnTransform.position.x, 0f, _cachedSpawnTransform.position.z);
        Vector3 playerXZ = new Vector3(transform.position.x, 0f, transform.position.z);
        float   radius   = _cachedSpawnTransform.lossyScale.x * 0.5f;
        return Vector3.Distance(playerXZ, spawnXZ) <= radius;
    }

    public bool HasMana(float cost)
    {
        return Mana.Value >= cost;
    }

    [ServerRpc(RequireOwnership = true)]
    public void SpendManaServerRpc(float cost)
    {
        Mana.Value = Mathf.Max(0f, Mana.Value - cost);
    }

    public void IncreaseMaxMana(float delta)
    {
        if (!IsServer) return;
        MaxMana.Value += delta;
        Mana.Value     = Mathf.Min(MaxMana.Value, Mana.Value + delta);
    }
}
