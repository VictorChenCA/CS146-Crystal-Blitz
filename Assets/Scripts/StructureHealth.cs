using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Health component shared by towers and crystals.
/// Never despawns — IsAlive drives renderer + collider state.
/// </summary>
public class StructureHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] public  int    TeamIndex     = 0;
    [SerializeField] private float  maxHealth     = 1000f;
    [SerializeField] public  bool   IsCrystal     = false;
    [SerializeField] public  string StructureName = "Tower";

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── IDamageable ──────────────────────────────────────────────────────────

    public float MaxHealth     => maxHealth;
    public float CurrentHealth => Health.Value;

    public bool IsImmuneTo(ulong attackerClientId)
    {
        // Immune when not in InGame phase
        if (GamePhaseManager.Instance?.Phase.Value != GamePhaseManager.GamePhase.InGame) return true;

        // Immune to attackers on the same team
        var nm = NetworkManager.Singleton;
        if (nm == null) return true;
        if (!nm.ConnectedClients.TryGetValue(attackerClientId, out var client)) return false;
        var pc = client.PlayerObject?.GetComponent<PlayerController>();
        return pc != null && pc.TeamIndex.Value == TeamIndex;
    }

    public void TakeDamage(float amount, ulong attackerClientId)
    {
        if (!IsServer || !IsAlive.Value) return;
        if (IsImmuneTo(attackerClientId)) return;

        Health.Value = Mathf.Max(0f, Health.Value - amount);
        if (Health.Value <= 0f) OnStructureDestroyed();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            Health.Value = maxHealth;

        IsAlive.OnValueChanged += OnIsAliveChanged;
        ApplyAliveState(IsAlive.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsAlive.OnValueChanged -= OnIsAliveChanged;
    }

    // ── Reset (called by GamePhaseManager on server) ─────────────────────────

    public void ResetStructure()
    {
        if (!IsServer) return;
        Health.Value  = maxHealth;
        IsAlive.Value = true;
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void OnStructureDestroyed()
    {
        IsAlive.Value = false;
        if (IsCrystal)
        {
            int winningTeam = TeamIndex == 0 ? 1 : 0;
            GamePhaseManager.Instance?.DeclareWinner(winningTeam);
        }
    }

    private void OnIsAliveChanged(bool previous, bool current) => ApplyAliveState(current);

    private void ApplyAliveState(bool alive)
    {
        var rend = GetComponent<Renderer>();
        if (rend != null) rend.enabled = alive;

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = alive;

        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(alive);
    }

    // ── Health bar (screen-space OnGUI) ──────────────────────────────────────

    private Camera _cam;

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
    }

    private const float BarW           = 130f;
    private const float BarH           = 14f;
    private const float WorldHeadOffset = 2.5f;

    private void OnGUI()
    {
        if (_cam == null || !IsAlive.Value) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(transform.position + Vector3.up * WorldHeadOffset);
        if (screenPos.z <= 0f) return;

        float screenY = Screen.height - screenPos.y;
        float x = screenPos.x - BarW * 0.5f;
        float y = screenY - BarH;

        // Background
        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, BarW, BarH), Texture2D.whiteTexture);

        // Colored fill based on team
        float t = Mathf.Clamp01(Health.Value / maxHealth);
        GUI.color = TeamIndex == 0
            ? new Color(0.2f, 0.5f, 1f, 1f)
            : new Color(1f, 0.25f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(x, y, BarW * t, BarH), Texture2D.whiteTexture);

        // Label
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 18f, BarW, 18f), StructureName);
    }
}
