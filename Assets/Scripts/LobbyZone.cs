using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Trigger zones in the lobby area. Only server processes trigger callbacks.
/// </summary>
public class LobbyZone : MonoBehaviour
{
    public enum ZoneType { BlueTeam, RedTeam, StartGame, CharSelect1, CharSelect2 }

    [SerializeField] public ZoneType zoneType;

    private static readonly Color BlueColor = new Color(0.2f, 0.5f, 1f);
    private static readonly Color RedColor  = new Color(1f, 0.25f, 0.25f);

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        var pc = other.GetComponent<PlayerController>();
        if (pc == null) return;

        switch (zoneType)
        {
            case ZoneType.BlueTeam:
                pc.SetTeamServerSide(0);
                break;

            case ZoneType.RedTeam:
                pc.SetTeamServerSide(1);
                break;

            case ZoneType.StartGame:
                if (GamePhaseManager.Instance != null &&
                    GamePhaseManager.Instance.Phase.Value == GamePhaseManager.GamePhase.Lobby)
                {
                    GamePhaseManager.Instance.PlayerEnteredStartZone(pc.OwnerClientId);
                }
                break;

            case ZoneType.CharSelect1:
                pc.SetCharacterIndexServerSide(0);
                break;

            case ZoneType.CharSelect2:
                pc.SetCharacterIndexServerSide(1);
                break;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (zoneType != ZoneType.StartGame) return;

        var pc = other.GetComponent<PlayerController>();
        if (pc == null) return;

        GamePhaseManager.Instance?.PlayerLeftStartZone(pc.OwnerClientId);
    }
}
