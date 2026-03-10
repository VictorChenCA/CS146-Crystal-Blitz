using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Single source of truth for game phase. Must be an in-scene NetworkObject.
/// Server writes all NetworkVariables; clients read only.
/// </summary>
public class GamePhaseManager : NetworkBehaviour
{
    public static GamePhaseManager Instance { get; private set; }

    // ── Phase enum ────────────────────────────────────────────────────────────

    public enum GamePhase { Lobby, Countdown, InGame, GameOver }

    // ── NetworkVariables (server-write) ───────────────────────────────────────

    public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(
        GamePhase.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> PlayersReadyCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> CountdownEndTime = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> WinningTeam = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> BarriersActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Server-only state ─────────────────────────────────────────────────────

    private readonly HashSet<ulong> _playersInStartZone = new HashSet<ulong>();

    // ── Inspector references ──────────────────────────────────────────────────

    [SerializeField] private SpawnBarrierController[] _spawnBarriers;
    [SerializeField] public  StructureHealth[]        _allStructures;

    // ── Lobby spawn rect ──────────────────────────────────────────────────────

    private static readonly Vector3 LobbySpawnCenter = new Vector3(0f, 1f, 100f);
    private const float LobbySpawnRadius = 3f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ── Start zone tracking (called by LobbyZone on server) ──────────────────

    public void PlayerEnteredStartZone(ulong clientId)
    {
        if (!IsServer || Phase.Value != GamePhase.Lobby) return;
        _playersInStartZone.Add(clientId);
        PlayersReadyCount.Value = _playersInStartZone.Count;
        CheckGameStart();
    }

    public void PlayerLeftStartZone(ulong clientId)
    {
        if (!IsServer) return;
        _playersInStartZone.Remove(clientId);
        PlayersReadyCount.Value = _playersInStartZone.Count;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _playersInStartZone.Remove(clientId);
        PlayersReadyCount.Value = _playersInStartZone.Count;
        CheckGameStart();
    }

    // ── Game start check ──────────────────────────────────────────────────────

    private void CheckGameStart()
    {
        if (!IsServer || Phase.Value != GamePhase.Lobby) return;

        int totalClients = NetworkManager.Singleton.ConnectedClientsList.Count;
        if (totalClients < 1) return;
        if (_playersInStartZone.Count >= totalClients)
            StartCoroutine(StartGameSequence());
    }

    // ── Game start sequence ───────────────────────────────────────────────────

    private IEnumerator StartGameSequence()
    {
        Phase.Value           = GamePhase.Countdown;
        CountdownEndTime.Value = Time.time + 3f;

        yield return new WaitForSeconds(3f);

        Phase.Value = GamePhase.InGame;
        TeleportAllPlayersToTeamSpawns();
        BarriersActive.Value = true;
        SetBarriersRpc(true);

        yield return new WaitForSeconds(10f);

        BarriersActive.Value = false;
        SetBarriersRpc(false);
    }

    // ── Win / game over ───────────────────────────────────────────────────────

    public void DeclareWinner(int winningTeam)
    {
        if (!IsServer || Phase.Value != GamePhase.InGame) return;

        Phase.Value       = GamePhase.GameOver;
        WinningTeam.Value = winningTeam;

        // Find the destroyed crystal position for the camera pan
        Vector3 crystalPos = Vector3.zero;
        int     losingTeam  = winningTeam == 0 ? 1 : 0;
        foreach (var s in _allStructures)
        {
            if (s.IsCrystal && s.TeamIndex == losingTeam)
            {
                crystalPos = s.transform.position;
                break;
            }
        }

        StartWinSequenceRpc(crystalPos, winningTeam);
        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        yield return new WaitForSeconds(10f);
        ResetForLobby();
    }

    private void ResetForLobby()
    {
        if (!IsServer) return;

        // Reset all structures
        foreach (var s in _allStructures)
            s.ResetStructure();

        // Teleport all players to lobby and reset their team
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            var pc = playerObj.GetComponent<PlayerController>();
            if (pc == null) continue;

            Vector2 rand    = Random.insideUnitCircle * LobbySpawnRadius;
            Vector3 lobbyPos = LobbySpawnCenter + new Vector3(rand.x, 0f, rand.y);
            pc.TeleportTo(lobbyPos);
            pc.SetTeamServerSide(-1);
        }

        _playersInStartZone.Clear();
        BarriersActive.Value    = false;
        WinningTeam.Value       = -1;
        PlayersReadyCount.Value = 0;
        Phase.Value             = GamePhase.Lobby;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the world-space centre of the spawn barrier for the given team, or Vector3.zero if not found.</summary>
    public Vector3 GetSpawnCenterForTeam(int team)
    {
        if (_spawnBarriers == null) return Vector3.zero;
        foreach (var b in _spawnBarriers)
            if (b != null && b.TeamIndex == team)
                return b.transform.position;
        return Vector3.zero;
    }

    private void TeleportAllPlayersToTeamSpawns()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            var pc = playerObj.GetComponent<PlayerController>();
            if (pc == null) continue;

            int     team   = pc.TeamIndex.Value;
            Vector3 center = GetSpawnCenterForTeam(team);
            Vector2 rand   = Random.insideUnitCircle * 2f;
            Vector3 spawnPos = new Vector3(center.x + rand.x, 1f, center.z + rand.y);
            pc.TeleportTo(spawnPos);
        }
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    private void SetBarriersRpc(bool active)
    {
        if (_spawnBarriers == null) return;
        foreach (var b in _spawnBarriers)
            b?.SetActive(active);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartWinSequenceRpc(Vector3 crystalPos, int winTeam)
    {
        CameraFollow.Instance?.SetTemporaryTarget(crystalPos, 10f);
    }
}
