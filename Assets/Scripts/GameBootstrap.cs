using UnityEngine;
using Unity.Netcode;

public class GameBootstrap : MonoBehaviour
{
    private float  _deathTimerEnd    = -1f;
    private string _killMessage      = "";
    private float  _killMessageEnd   = -1f;

    private void OnEnable()
    {
        PlayerHealth.OnLocalPlayerDeath += HandleLocalPlayerDeath;
        PlayerHealth.OnKillAnnouncement += HandleKillAnnouncement;
    }

    private void OnDisable()
    {
        PlayerHealth.OnLocalPlayerDeath -= HandleLocalPlayerDeath;
        PlayerHealth.OnKillAnnouncement -= HandleKillAnnouncement;
    }

    private void HandleLocalPlayerDeath(float duration)  => _deathTimerEnd  = Time.time + duration;
    private void HandleKillAnnouncement(string message)
    {
        _killMessage    = message;
        _killMessageEnd = Time.time + 4f;
    }

    private void OnGUI()
    {
        // --- Connection buttons (2x scaled) ---
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2f, 2f, 1f));

        GUILayout.BeginArea(new Rect(10, 10, 200, 200));
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host"))   NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
            if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();
        }
        else
        {
            string mode = NetworkManager.Singleton.IsHost   ? "Host"
                        : NetworkManager.Singleton.IsServer ? "Server"
                        : "Client";
            GUILayout.Label($"Mode: {mode}");
        }
        GUILayout.EndArea();

        GUI.matrix = Matrix4x4.identity;   // reset before drawing the countdown

        // --- Kill announcement (top-centre, 4 s) ---
        if (_killMessageEnd > Time.time)
        {
            float w = 420f, h = 44f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, 60f, w, h), _killMessage);
        }

        // --- Death countdown (centre screen) ---
        if (_deathTimerEnd > Time.time)
        {
            int secs = Mathf.CeilToInt(_deathTimerEnd - Time.time);
            float w = 300f, h = 52f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h),
                    $"Respawning in {secs}...");
        }
    }
}
