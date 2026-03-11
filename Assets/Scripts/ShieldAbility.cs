using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Tank-only (CharacterIndex 0) shield ability.
/// WASD mode: Left Shift | P&C mode: W key.
/// Grants a temporary absorption shield from PlayerHealth.ShieldHP.
/// </summary>
public class ShieldAbility : NetworkBehaviour
{
    [SerializeField] private float shieldAmount   = 50f;
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float cooldown       = 12f;
    [SerializeField] private float manaCost       = 10f;

    public float CooldownFraction  => Mathf.Clamp01((_nextShieldTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextShieldTime - Time.time);

    private PlayerController _pc;
    private PlayerHealth     _health;
    private PlayerMana       _mana;
    private float            _nextShieldTime;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        _pc     = GetComponent<PlayerController>();
        _health = GetComponent<PlayerHealth>();
        _mana   = GetComponent<PlayerMana>();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_pc != null && _pc.CharacterIndex.Value != 0) return;  // Tank only
        if (_health != null && _health.IsDead) return;
        if (Time.time < _nextShieldTime) return;

        bool pressed = GameSettings.UseWasd
            ? Keyboard.current.leftShiftKey.wasPressedThisFrame
            : Keyboard.current.wKey.wasPressedThisFrame;

        if (!pressed) return;
        if (_mana != null && !_mana.HasMana(manaCost)) return;

        _mana?.SpendManaServerRpc(manaCost);
        GrantShieldServerRpc();
        _nextShieldTime = Time.time + cooldown;
    }

    [ServerRpc]
    private void GrantShieldServerRpc()
    {
        GetComponent<PlayerHealth>()?.GrantShield(shieldAmount, shieldDuration);
    }
}
