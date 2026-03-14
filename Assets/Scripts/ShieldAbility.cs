using System.Collections;
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
    [SerializeField] private float shieldPerLevel = 10f;
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float cooldown       = 12f;
    [SerializeField] private float manaCost       = 10f;

    [Header("Cast Timing")]
    [SerializeField] private float castDuration      = 0.2f;
    [SerializeField] private float animationDuration = 0f;

    [Header("Ring Visual")]
    [SerializeField] private float radius   = 1.1f;
    [SerializeField] private float height   = 0.5f;
    [SerializeField] private float spacing  = 0.25f;
    [SerializeField] private int   ringCount = 3;
    [SerializeField] private float width    = 0.18f;
    [SerializeField] private Color color    = new Color(0.3f, 0.7f, 1f, 0.9f);
    [SerializeField] private int   segments = 48;

    [Header("Aim Ring Visual")]
    [SerializeField] private float aimRingRadius = 1.5f;

    public float CooldownFraction  => Mathf.Clamp01((_nextShieldTime - Time.time) / cooldown);
    public float CooldownRemaining => Mathf.Max(0f, _nextShieldTime - Time.time);
    public float CastFraction      => _castFraction;
    public bool  IsAiming          => _aiming;
    public float ManaCost          => manaCost;

    private PlayerController _pc;
    private PlayerHealth     _health;
    private PlayerMana       _mana;
    private float            _nextShieldTime;
    private LineRenderer[]   _rings;
    private LineRenderer     _aimRing;

    private float     _castFraction;
    private bool      _aiming;
    private Coroutine _castCoroutine;

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<PlayerHealth>();
        if (_health != null)
            _health.ShieldHP.OnValueChanged += OnShieldHPChanged;

        CreateRing();
        CreateAimRing();

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
        if (_aimRing != null) Destroy(_aimRing.gameObject);
    }

    private void OnShieldHPChanged(float previous, float current)
    {
        bool active = current > 0f;
        if (_rings == null) return;
        foreach (var r in _rings)
            if (r != null) r.enabled = active;
    }

    private void UpdateAimRing()
    {
        if (_aimRing == null) return;
        Vector3 center = new Vector3(transform.position.x, 0.05f, transform.position.z);
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            _aimRing.SetPosition(i, center + new Vector3(
                Mathf.Cos(angle) * aimRingRadius, 0f, Mathf.Sin(angle) * aimRingRadius));
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_pc != null && _pc.CharacterIndex.Value != 0) return;  // Tank only
        if (_health != null && _health.IsDead) return;

        bool pressed  = GameSettings.UseWasd
            ? GameKeybinds.WasPressedThisFrame(GameKeybinds.Wasd_Ability2)
            : GameKeybinds.WasPressedThisFrame(GameKeybinds.PnC_Ability2);
        bool released = GameSettings.UseWasd
            ? GameKeybinds.WasReleasedThisFrame(GameKeybinds.Wasd_Ability2)
            : GameKeybinds.WasReleasedThisFrame(GameKeybinds.PnC_Ability2);

        // Start aim on press (only if off cooldown, enough mana, not already casting)
        if (pressed && _castCoroutine == null && Time.time >= _nextShieldTime)
        {
            if (_mana != null && !_mana.HasMana(manaCost)) return;
            _aiming = true;
            if (_aimRing != null) _aimRing.enabled = true;
        }

        if (_aiming) UpdateAimRing();

        // Right-click cancels aim or cast
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelCast();
            return;
        }

        // Release while aiming → start cast
        if (released && _aiming)
        {
            _aiming = false;
            if (_aimRing != null) _aimRing.enabled = false;
            _castCoroutine = StartCoroutine(CastCoroutine());
        }
    }

    private IEnumerator CastCoroutine()
    {
        // Cast phase: fill _castFraction 0→1 over castDuration
        float castEnd = Time.time + castDuration;
        while (Time.time < castEnd)
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                _castFraction  = 0f;
                _castCoroutine = null;
                yield break;
            }
            _castFraction = castDuration > 0f ? 1f - (castEnd - Time.time) / castDuration : 1f;
            yield return null;
        }
        _castFraction = 0f;

        // Apply effect
        _mana?.SpendManaServerRpc(manaCost);
        GrantShieldServerRpc();
        TutorialManager.OnWFired?.Invoke();
        _nextShieldTime = Time.time + cooldown;

        // Animation phase
        if (animationDuration > 0f)
            yield return new WaitForSeconds(animationDuration);

        _castCoroutine = null;
    }

    /// <summary>Cancels any ongoing aim or cast without applying the shield or cooldown.</summary>
    public void CancelCast()
    {
        _aiming = false;
        _castFraction = 0f;
        if (_aimRing != null) _aimRing.enabled = false;
        if (_castCoroutine != null)
        {
            StopCoroutine(_castCoroutine);
            _castCoroutine = null;
        }
    }

    [ServerRpc]
    private void GrantShieldServerRpc()
    {
        var xp = GetComponent<PlayerXP>();
        int level = xp != null ? xp.Level.Value : 1;
        float scaled = shieldAmount + shieldPerLevel * (level - 1);
        GetComponent<PlayerHealth>()?.GrantShield(scaled, shieldDuration);
    }

    private void CreateAimRing()
    {
        var go = new GameObject("ShieldAimRing");
        go.transform.SetParent(transform);

        _aimRing = go.AddComponent<LineRenderer>();
        _aimRing.loop              = true;
        _aimRing.positionCount     = segments;
        _aimRing.startWidth        = 0.12f;
        _aimRing.endWidth          = 0.12f;
        _aimRing.useWorldSpace     = true;
        _aimRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _aimRing.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(color.r, color.g, color.b, 0.5f);
        _aimRing.material = mat;

        UpdateAimRing();
        _aimRing.enabled = false;
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
}
