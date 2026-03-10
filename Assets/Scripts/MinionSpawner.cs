using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// Spawns waves of minions for one team. Server-only; disabled on clients.
/// Place as an in-scene NetworkObject near the team's base.
/// </summary>
public class MinionSpawner : NetworkBehaviour
{
    [SerializeField] private int           spawnerTeam = 0;
    [SerializeField] private MinionSettings settings   = new MinionSettings();

    private readonly List<NetworkObject> _activeMinions = new List<NetworkObject>();
    private Coroutine _spawnCoroutine;

    // ── Network spawn ─────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        if (GamePhaseManager.Instance != null)
        {
            GamePhaseManager.Instance.Phase.OnValueChanged += OnPhaseChanged;
            if (GamePhaseManager.Instance.Phase.Value == GamePhaseManager.GamePhase.InGame)
                StartSpawning();
        }
        else
        {
            // GamePhaseManager spawned after us — poll until it's ready
            StartCoroutine(WaitForPhaseManager());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        if (GamePhaseManager.Instance != null)
            GamePhaseManager.Instance.Phase.OnValueChanged -= OnPhaseChanged;
    }

    private IEnumerator WaitForPhaseManager()
    {
        while (GamePhaseManager.Instance == null)
            yield return null;

        GamePhaseManager.Instance.Phase.OnValueChanged += OnPhaseChanged;
        if (GamePhaseManager.Instance.Phase.Value == GamePhaseManager.GamePhase.InGame)
            StartSpawning();
    }

    // ── Phase handling ────────────────────────────────────────────────────────

    private void OnPhaseChanged(GamePhaseManager.GamePhase previous, GamePhaseManager.GamePhase current)
    {
        if (current == GamePhaseManager.GamePhase.InGame)
            StartSpawning();
        else
            StopSpawning();
    }

    private void StartSpawning()
    {
        if (_spawnCoroutine != null) return;
        _spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    private void StopSpawning()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
        DespawnAllMinions();
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(3f);

        while (true)
        {
            SpawnWave();
            yield return new WaitForSeconds(settings.spawnInterval);
        }
    }

    private void SpawnWave()
    {
        // Clean up dead/despawned entries
        _activeMinions.RemoveAll(no => no == null || !no.IsSpawned);

        if (settings.minionPrefab == null)
        {
            Debug.LogWarning("[MinionSpawner] minionPrefab is not assigned.");
            return;
        }

        for (int i = 0; i < settings.spawnCount; i++)
        {
            // Random XZ offset within 1.5u radius
            Vector2 rand     = Random.insideUnitCircle * 1.5f;
            Vector3 spawnPos = transform.position + new Vector3(rand.x, 0f, rand.y);

            // Snap to NavMesh
            if (!NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;

            spawnPos = hit.position;

            var go  = Instantiate(settings.minionPrefab, spawnPos, Quaternion.identity);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Destroy(go);
                continue;
            }

            netObj.Spawn(true);

            var mc = go.GetComponent<MinionController>();
            var mh = go.GetComponent<MinionHealth>();

            mc?.Initialize(spawnerTeam, settings);
            mh?.Initialize(settings.maxHealth, spawnerTeam);

            _activeMinions.Add(netObj);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void DespawnAllMinions()
    {
        foreach (var no in _activeMinions)
        {
            if (no != null && no.IsSpawned)
                no.Despawn(true);
        }
        _activeMinions.Clear();
    }
}
