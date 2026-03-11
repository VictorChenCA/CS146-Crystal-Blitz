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
    [Header("Shield")]
    [SerializeField] private float shieldAmount   = 50f;
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float cooldown       = 12f;
    [SerializeField] private float manaCost       = 10f;

    [Header("Ring Visual")]
    [SerializeField] private float radius   = 1.1f;
    [SerializeField] private float height   = 0.5f;
    [SerializeField] private float spacing  = 0.25f;
    [SerializeField] private int   ringCount = 3;
    [SerializeField] private float width    = 0.18f;
    [SerializeField] private Color color    = new Color(0.3f, 0.7f, 1f, 0.9f);
    [SerializeField] private int   segments = 48;

    public float CooldownFraction  => Mathf.Clamp01((_nextShieldTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextShieldTime - Time.time);
    public float ManaCost          => manaCost;

    private PlayerController _pc;
    private PlayerHealth     _health;
    private PlayerMana       _mana;
    private float            _nextShieldTime;
    private LineRenderer[]   _rings;

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<PlayerHealth>();
        if (_health != null)
            _health.ShieldHP.OnValueChanged += OnShieldHPChanged;

        CreateRing();

        if (!IsOwner) { enabled = false; return; }
        _pc   = GetComponent<PlayerController>();
        _mana = GetComponent<PlayerMana>();
    }

    public override void OnNetworkDespawn()
    {
        if (_health != null)
            _health.ShieldHP.OnValueChanged -= OnShieldHPChanged;

        if (_rings != null)
            foreach (var r in _rings)
                if (r != null) Destroy(r.gameObject);
    }

    private void OnShieldHPChanged(float previous, float current)
    {
        bool active = current > 0f;
        if (_rings == null) return;
        foreach (var r in _rings)
            if (r != null) r.enabled = active;
    }

    private void CreateRing()
    {
        _rings = new LineRenderer[ringCount];
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;

        for (int ri = 0; ri < ringCount; ri++)
        {
            var go = new GameObject($"ShieldRing_{ri}");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.loop              = true;
            lr.positionCount     = segments;
            lr.startWidth        = width;
            lr.endWidth          = width;
            lr.useWorldSpace     = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.material          = mat;

            float ringHeight = height + ri * spacing;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    ringHeight,
                    Mathf.Sin(angle) * radius));
            }

            lr.enabled = false;
            _rings[ri]  = lr;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_pc != null && _pc.CharacterIndex.Value != 0) return;  // Tank only
        if (_health != null && _health.IsDead) return;
        if (Time.time < _nextShieldTime) return;

        bool pressed = GameSettings.UseWasd
            ? GameKeybinds.WasPressedThisFrame(GameKeybinds.Wasd_Ability2)
            : GameKeybinds.WasPressedThisFrame(GameKeybinds.PnC_Ability2);

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
