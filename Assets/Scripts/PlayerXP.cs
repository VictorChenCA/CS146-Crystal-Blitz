using UnityEngine;
using Unity.Netcode;

public class PlayerXP : NetworkBehaviour
{
    [SerializeField] private float xpPerLevel           = 20f;
    [SerializeField] private float passiveXpPerSecond   = 0.1f;
    [SerializeField] private float healthLevelUpRatio   = 0.1f;
    [SerializeField] private float manaLevelUpRatio     = 0.1f;

    public NetworkVariable<float> XP = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> Level = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public float XPFraction => xpPerLevel > 0f ? Mathf.Clamp01(XP.Value / xpPerLevel) : 0f;

    private float _passiveAccum;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        Level.Value = 1;
        XP.Value    = 0f;
    }

    private static bool IsInGame =>
        GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.InGame;

    private void Update()
    {
        if (!IsServer || !IsInGame) return;

        _passiveAccum += passiveXpPerSecond * Time.deltaTime;
        if (_passiveAccum >= 0.01f)
        {
            float toAdd    = _passiveAccum;
            _passiveAccum  = 0f;
            GainXP(toAdd);
        }
    }

    public void ResetForLobby()
    {
        if (!IsServer) return;
        XP.Value    = 0f;
        Level.Value = 1;
    }

    public void GainXP(float amount)
    {
        if (!IsServer || !IsInGame) return;
        XP.Value += amount;
        TryLevelUp();
    }

    private const int MaxLevel = 10;

    private void TryLevelUp()
    {
        while (XP.Value >= xpPerLevel && Level.Value < MaxLevel)
        {
            XP.Value -= xpPerLevel;
            Level.Value++;
            ApplyLevelUpBonuses();
        }
        if (Level.Value >= MaxLevel)
            XP.Value = 0f;
    }

    /// <summary>Re-applies level-up stat bonuses from scratch up to the given level. Call after respawn.</summary>
    public void ReapplyBonuses(int toLevel)
    {
        if (!IsServer) return;
        for (int i = 1; i < toLevel; i++)
            ApplyLevelUpBonuses();
    }

    private void ApplyLevelUpBonuses()
    {
        var health = GetComponent<PlayerHealth>();
        if (health != null)
            health.IncreaseMaxHealth(health.MaxHealth * healthLevelUpRatio);

        var mana = GetComponent<PlayerMana>();
        if (mana != null)
            mana.IncreaseMaxMana(mana.MaxMana.Value * manaLevelUpRatio);
    }
}
