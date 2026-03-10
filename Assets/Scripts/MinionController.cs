using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// Server-authoritative minion AI. Non-server instances are disabled.
/// </summary>
public class MinionController : NetworkBehaviour
{
    public int TeamIndex { get; private set; }

    private MinionSettings _settings;
    private NavMeshAgent   _agent;
    private MinionHealth   _health;

    // Position sync
    private NetworkVariable<Vector3> _position = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Target tracking
    private IDamageable _target;
    private Transform   _targetTransform;
    private float       _nextTargetUpdate;
    private float       _nextAttackTime;

    // Enemy crystal cache (advance lane destination)
    private Transform _enemyCrystal;

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Initialize(int teamIndex, MinionSettings settings)
    {
        TeamIndex = teamIndex;
        _settings = settings;

        _agent = GetComponent<NavMeshAgent>();
        if (_agent != null)
        {
            _agent.speed            = settings.moveSpeed;
            _agent.angularSpeed     = 9999f;
            _agent.acceleration     = 9999f;
            _agent.autoBraking      = false;
            _agent.updateRotation   = false;
            _agent.stoppingDistance = settings.navStoppingDistance;
            Debug.Log($"[Minion T{teamIndex}] Agent found. isOnNavMesh={_agent.isOnNavMesh} speed={_agent.speed}");
        }
        else
        {
            Debug.LogWarning($"[Minion T{teamIndex}] No NavMeshAgent component found on prefab!");
        }

        _position.Value = transform.position;
        CacheEnemyCrystal();
        Debug.Log($"[Minion T{teamIndex}] Initialized at {transform.position}. Crystal={_enemyCrystal?.name ?? "NOT FOUND"}");
    }

    // ── Network spawn ─────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<MinionHealth>();

        if (!IsServer)
        {
            enabled = false;
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            _position.OnValueChanged += OnPositionChanged;
            return;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
            _position.OnValueChanged -= OnPositionChanged;
    }

    private void OnPositionChanged(Vector3 previous, Vector3 current)
    {
        transform.position = current;
    }

    // ── Server Update ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsServer) return;
        if (GamePhaseManager.Instance?.Phase.Value != GamePhaseManager.GamePhase.InGame) return;
        if (_health != null && _health.Health.Value <= 0f) return;

        if (Time.time >= _nextTargetUpdate)
        {
            _nextTargetUpdate = Time.time + _settings.targetUpdateInterval;
            FindBestTarget();
            Debug.Log($"[Minion T{TeamIndex}] target={_targetTransform?.name ?? "null"} crystal={_enemyCrystal?.name ?? "null"} onNavMesh={_agent?.isOnNavMesh} hasPath={_agent?.hasPath}");
        }

        if (_targetTransform != null)
        {
            float dist = Vector3.Distance(transform.position, _targetTransform.position);
            if (dist <= _settings.attackRange)
            {
                _agent.ResetPath();
                TryAttack();
            }
            else
            {
                Vector3 dest = NearestNavMeshPoint(_targetTransform.position);
                bool ok = _agent.SetDestination(dest);
                if (!ok) Debug.LogWarning($"[Minion T{TeamIndex}] SetDestination failed for target {_targetTransform.name}");
            }
        }
        else
        {
            AdvanceLane();
        }

        SyncPosition();
    }

    // ── Target selection ──────────────────────────────────────────────────────

    private void FindBestTarget()
    {
        _target          = null;
        _targetTransform = null;

        // 1. Nearest alive enemy minion within aggroRange
        float   bestDist   = _settings.aggroRange;
        MinionHealth bestMinion = null;

        foreach (var mh in FindObjectsByType<MinionHealth>(FindObjectsSortMode.None))
        {
            if (mh == _health) continue;
            if (mh.Health.Value <= 0f) continue;
            if (mh.TeamIndexNet.Value == TeamIndex) continue;

            float d = Vector3.Distance(transform.position, mh.transform.position);
            if (d < bestDist)
            {
                bestDist   = d;
                bestMinion = mh;
            }
        }

        if (bestMinion != null)
        {
            _target          = bestMinion;
            _targetTransform = bestMinion.transform;
            return;
        }

        // 2. Nearest alive non-crystal enemy structure
        StructureHealth nearestStructure = null;
        StructureHealth nearestCrystal  = null;
        float structDist  = float.MaxValue;
        float crystalDist = float.MaxValue;

        foreach (var s in FindObjectsByType<StructureHealth>(FindObjectsSortMode.None))
        {
            if (!s.IsAlive.Value) continue;
            if (s.TeamIndex == TeamIndex) continue;

            float d = Vector3.Distance(transform.position, s.transform.position);
            if (s.IsCrystal)
            {
                if (d < crystalDist) { crystalDist = d; nearestCrystal = s; }
            }
            else
            {
                if (d < structDist) { structDist = d; nearestStructure = s; }
            }
        }

        if (nearestStructure != null)
        {
            _target          = nearestStructure;
            _targetTransform = nearestStructure.transform;
            return;
        }

        if (nearestCrystal != null)
        {
            _target          = nearestCrystal;
            _targetTransform = nearestCrystal.transform;
        }
    }

    // ── Attack ────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (Time.time < _nextAttackTime) return;
        if (_target == null) return;

        // Team check for minion targets (IDamageable doesn't expose team directly)
        if (_target is MinionHealth enemyMh && enemyMh.TeamIndexNet.Value == TeamIndex) return;
        if (_target is StructureHealth s && s.TeamIndex == TeamIndex) return;

        _nextAttackTime = Time.time + _settings.attackCooldown;
        _target.TakeDamage(_settings.attackDamage, ulong.MaxValue);
    }

    // ── Lane advance ──────────────────────────────────────────────────────────

    private void AdvanceLane()
    {
        if (_enemyCrystal == null) CacheEnemyCrystal();
        if (_enemyCrystal == null)
        {
            Debug.LogWarning($"[Minion T{TeamIndex}] No enemy crystal found — cannot advance lane.");
            return;
        }
        Vector3 dest = NearestNavMeshPoint(_enemyCrystal.position);
        bool ok = _agent.SetDestination(dest);
        if (!ok) Debug.LogWarning($"[Minion T{TeamIndex}] SetDestination failed for crystal {_enemyCrystal.name}");
    }

    private static Vector3 NearestNavMeshPoint(Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            return hit.position;
        return pos;
    }

    private void CacheEnemyCrystal()
    {
        int enemyTeam = TeamIndex == 0 ? 1 : 0;
        foreach (var s in FindObjectsByType<StructureHealth>(FindObjectsSortMode.None))
        {
            if (s.IsCrystal && s.TeamIndex == enemyTeam)
            {
                _enemyCrystal = s.transform;
                return;
            }
        }
    }

    // ── Position sync ─────────────────────────────────────────────────────────

    private void SyncPosition()
    {
        if (Vector3.Distance(_position.Value, transform.position) > 0.05f)
            _position.Value = transform.position;
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    public void OnDeath()
    {
        if (!IsServer) return;
        if (_agent != null) _agent.enabled = false;
        NetworkObject.Despawn(true);
    }
}
