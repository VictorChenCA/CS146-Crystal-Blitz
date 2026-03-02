using UnityEngine;
using Unity.Netcode;

public class GameBootstrap : MonoBehaviour
{
    private void OnGUI()
    {
        // Scale the entire GUI 2x — avoids custom GUIStyle font assignment errors.
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
    }
}
