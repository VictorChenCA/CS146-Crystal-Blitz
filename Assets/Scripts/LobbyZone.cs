using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Lobby trigger zone. Uses Physics.OverlapSphere polling (server-only) so
/// it works without a Rigidbody on either the zone or the player.
/// </summary>
public class LobbyZone : MonoBehaviour
{
    public enum ZoneType { BlueTeam, RedTeam, StartGame, CharSelect1, CharSelect2 }

    [SerializeField] public ZoneType zoneType;

    private SphereCollider        _col;
    private readonly HashSet<ulong> _occupants = new HashSet<ulong>();

    private void Awake()
    {
        _col = GetComponent<SphereCollider>();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (_col == null) return;

        // World-space centre and radius (accounts for non-uniform scale on the collider's transform)
        Vector3 worldCenter = transform.TransformPoint(_col.center);
        float   worldRadius = _col.radius * Mathf.Max(
            transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);

        Collider[] hits = Physics.OverlapSphere(worldCenter, worldRadius);

        // Detect who is currently inside
        var current = new HashSet<ulong>();
        foreach (var hit in hits)
        {
            var pc = hit.GetComponent<PlayerController>();
            if (pc == null) continue;
            current.Add(pc.OwnerClientId);

            if (!_occupants.Contains(pc.OwnerClientId))
                OnPlayerEntered(pc);
        }

        // Detect who just left
        foreach (ulong id in _occupants)
        {
            if (!current.Contains(id))
                OnPlayerExited(id);
        }

        _occupants.Clear();
        foreach (ulong id in current) _occupants.Add(id);
    }

    private void OnPlayerEntered(PlayerController pc)
    {
        switch (zoneType)
        {
            case ZoneType.BlueTeam:
                pc.SetTeamServerSide(0);
                break;

            case ZoneType.RedTeam:
                pc.SetTeamServerSide(1);
                break;

            case ZoneType.StartGame:
                if (GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby)
                    GamePhaseManager.Instance.PlayerEnteredStartZone(pc.OwnerClientId);
                break;

            case ZoneType.CharSelect1:
                pc.SetCharacterIndexServerSide(0);
                break;

            case ZoneType.CharSelect2:
                pc.SetCharacterIndexServerSide(1);
                break;
        }
    }

    private void OnPlayerExited(ulong clientId)
    {
        if (zoneType != ZoneType.StartGame) return;
        GamePhaseManager.Instance?.PlayerLeftStartZone(clientId);
    }
}
