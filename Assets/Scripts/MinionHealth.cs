using UnityEngine;
using Unity.Netcode;

public class MinionHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] private float xpGrantAmount = 2f;
    [SerializeField] private float xpGrantRange  = 20f;
    public NetworkVariable<float> Health = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> TeamIndexNet = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> MaxHealthNet = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float MaxHealth     => MaxHealthNet.Value;
    public float CurrentHealth => Health.Value;

    public bool IsImmuneTo(ulong attackerClientId)
    {
        if (GamePhaseManager.Instance?.Phase.Value != GamePhaseManager.GamePhase.InGame) return true;

        var nm = NetworkManager.Singleton;
        if (nm == null) return true;
        if (!nm.ConnectedClients.TryGetValue(attackerClientId, out var client)) return false;
        var pc = client.PlayerObject?.GetComponent<PlayerController>();
        return pc != null && pc.TeamIndex.Value == TeamIndexNet.Value;
    }

    public void TakeDamage(float amount, ulong attackerClientId)
    {
        if (!IsServer) return;
        if (Health.Value <= 0f) return;

        Health.Value = Mathf.Max(0f, Health.Value - amount);
        if (Health.Value <= 0f)
        {
            GetComponent<MinionController>()?.OnDeath();
            GrantXpToNearbyPlayers();
        }
    }

    private void GrantXpToNearbyPlayers()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;
            var pc = playerObj.GetComponent<PlayerController>();
            if (pc == null || pc.TeamIndex.Value == TeamIndexNet.Value) continue;
            if (Vector3.Distance(transform.position, playerObj.transform.position) > xpGrantRange) continue;
            playerObj.GetComponent<PlayerXP>()?.GainXP(xpGrantAmount);
        }
    }

    // ── Initialize (called server-side after Spawn) ───────────────────────────

    public void Initialize(float maxHealth, int teamIndex)
    {
        MaxHealthNet.Value  = maxHealth;
        Health.Value        = maxHealth;
        TeamIndexNet.Value  = teamIndex;
    }

    // ── Network spawn ─────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        TeamIndexNet.OnValueChanged += (_, current) => ApplyTeamColor(current);
        ApplyTeamColor(TeamIndexNet.Value);
    }

    public override void OnNetworkDespawn()
    {
        TeamIndexNet.OnValueChanged -= (_, current) => ApplyTeamColor(current);
    }

    private void ApplyTeamColor(int teamIndex)
    {
        var rend = GetComponent<Renderer>();
        if (rend == null) return;
        var block = new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetColor("_BaseColor", teamIndex == 0
            ? new Color(0.2f, 0.4f, 1f)
            : new Color(1f, 0.2f, 0.2f));
        rend.SetPropertyBlock(block);
    }

    // ── Health bar ────────────────────────────────────────────────────────────

    private Camera _cam;

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
    }

    private const float BarW           = 80f;
    private const float BarH           = 10f;
    private const float WorldHeadOffset = 1.5f;

    private void OnGUI()
    {
        if (_cam == null || Health.Value <= 0f) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(transform.position + Vector3.up * WorldHeadOffset);
        if (screenPos.z <= 0f) return;

        float screenY = Screen.height - screenPos.y;
        float x = screenPos.x - BarW * 0.5f;
        float y = screenY - BarH;

        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, BarW, BarH), Texture2D.whiteTexture);

        float t = MaxHealthNet.Value > 0f ? Mathf.Clamp01(Health.Value / MaxHealthNet.Value) : 0f;
        GUI.color = TeamIndexNet.Value == 0
            ? new Color(0.2f, 0.5f, 1f, 1f)
            : new Color(1f, 0.25f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(x, y, BarW * t, BarH), Texture2D.whiteTexture);

        GUI.color = Color.white;
        string label = TeamIndexNet.Value == 0 ? "Blue Minion" : "Red Minion";
        GUI.Label(new Rect(x, y - 16f, BarW, 16f), label);
    }
}
