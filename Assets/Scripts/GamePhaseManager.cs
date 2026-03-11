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

    // ── Server-only state ─────────────────────────────────────────────────────

    private readonly HashSet<ulong> _playersInStartZone = new HashSet<ulong>();

    // ── Inspector references ──────────────────────────────────────────────────

    [SerializeField] public StructureHealth[] _allStructures;

    // ── Lobby spawn ───────────────────────────────────────────────────────────

    private static readonly Vector3 LobbySpawnCenter = new Vector3(0f, 1f, 92f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
        if (IsServer)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
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
        Phase.Value            = GamePhase.Countdown;
        CountdownEndTime.Value = Time.time + 3f;

        yield return new WaitForSeconds(3f);

        Phase.Value = GamePhase.InGame;
        AssignTeamsToUnassignedPlayers();
        TeleportAllPlayersToTeamSpawns();
    }

    // ── Win / game over ───────────────────────────────────────────────────────

    public void DeclareWinner(int winningTeam)
    {
        if (!IsServer || Phase.Value != GamePhase.InGame) return;

        Phase.Value       = GamePhase.GameOver;
        WinningTeam.Value = winningTeam;

        Vector3 crystalPos = Vector3.zero;
        int     losingTeam = winningTeam == 0 ? 1 : 0;
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

        foreach (var s in _allStructures)
            s.ResetStructure();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var pc = client.PlayerObject?.GetComponent<PlayerController>();
            if (pc == null) continue;
            pc.TeleportTo(LobbySpawnCenter);
            pc.SetTeamServerSide(-1);

            var xp = client.PlayerObject?.GetComponent<PlayerXP>();
            xp?.ResetForLobby();

            int charIdx = pc.CharacterIndex.Value;
            var health  = client.PlayerObject?.GetComponent<PlayerHealth>();
            health?.ResetToBase(charIdx);
        }

        _playersInStartZone.Clear();
        WinningTeam.Value       = -1;
        PlayersReadyCount.Value = 0;
        Phase.Value             = GamePhase.Lobby;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Assigns a random team (0 or 1) to any player still showing white (unassigned).</summary>
    private void AssignTeamsToUnassignedPlayers()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var pc = client.PlayerObject?.GetComponent<PlayerController>();
            if (pc == null) continue;
            if (pc.PlayerColor.Value == Color.white)
                pc.SetTeamServerSide(Random.Range(0, 2));
        }
    }

    /// <summary>Returns a random spawn point on top of the team's cylinder (Blue Spawn / Red Spawn).</summary>
    public Vector3 GetTeamSpawnPosition(int team)
    {
        string name = team == 0 ? "Blue Spawn" : "Red Spawn";
        return RandomPointOnSpawnCylinder(name);
    }

    /// <summary>Returns a random point on top of the named spawn cylinder.</summary>
    private Vector3 RandomPointOnSpawnCylinder(string cylinderName)
    {
        var go = GameObject.Find(cylinderName);
        if (go == null)
        {
            Debug.LogWarning($"[GamePhaseManager] Could not find spawn cylinder '{cylinderName}'");
            return Vector3.zero;
        }

        Transform t      = go.transform;
        // Unity cylinder mesh: height = 2, radius = 0.5 at scale (1,1,1)
        float topY = t.position.y + t.lossyScale.y;   // top surface Y
        return new Vector3(t.position.x, topY, t.position.z);
    }

    private void TeleportAllPlayersToTeamSpawns()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var pc = client.PlayerObject?.GetComponent<PlayerController>();
            if (pc == null) continue;

            string cylinderName = pc.TeamIndex.Value == 0 ? "Blue Spawn" : "Red Spawn";
            Vector3 spawnPos    = RandomPointOnSpawnCylinder(cylinderName);
            if (spawnPos == Vector3.zero) continue;
            pc.TeleportTo(spawnPos);
        }
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    private void StartWinSequenceRpc(Vector3 crystalPos, int winTeam)
    {
        CameraFollow.Instance?.SetTemporaryTarget(crystalPos, 10f);
    }
}
