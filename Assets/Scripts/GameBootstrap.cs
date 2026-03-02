using UnityEngine;
using Unity.Netcode;

public class GameBootstrap : MonoBehaviour
{
    private float _deathTimerEnd = -1f;

    private void OnEnable()  { PlayerHealth.OnLocalPlayerDeath += HandleLocalPlayerDeath; }
    private void OnDisable() { PlayerHealth.OnLocalPlayerDeath -= HandleLocalPlayerDeath; }

    private void HandleLocalPlayerDeath(float duration)
    {
        _deathTimerEnd = Time.time + duration;
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

        // --- Death countdown (normal scale, centred) ---
        if (_deathTimerEnd > Time.time)
        {
            int secs = Mathf.CeilToInt(_deathTimerEnd - Time.time);
            float w = 300f, h = 52f;
            float x = (Screen.width  - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            GUI.Box(new Rect(x, y, w, h), $"Respawning in  {secs}...");
        }
    }
}
