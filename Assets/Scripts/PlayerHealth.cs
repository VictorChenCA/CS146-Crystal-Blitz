using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] private float maxHealth    = 100f;
    [SerializeField] private float respawnDelay = 3f;

    // Bar dimensions in screen pixels.
    private const float BarW = 140f;
    private const float BarH = 18f;
    // World-space height above the player's origin to anchor the bar.
    private const float WorldHeadOffset = 2.3f;


    // Fired on the owner's client so GameBootstrap can show the countdown UI.
    public static event System.Action<float>  OnLocalPlayerDeath;
    // Fired on ALL clients to show the kill feed.
    public static event System.Action<string> OnKillAnnouncement;

    public float HealthFraction => Mathf.Clamp01(Health.Value / maxHealth);
    public bool  IsDead         => _isDead;

    public void IncreaseMaxHealth(float delta)  // server-only
    {
        if (!IsServer) return;
        maxHealth    += delta;
        Health.Value  = Mathf.Min(Health.Value + delta, maxHealth);
    }

    // ── IDamageable ──────────────────────────────────────────────────────────
    public float MaxHealth     => maxHealth;
    public float CurrentHealth => Health.Value;

    public bool IsImmuneTo(ulong attackerClientId)
    {
        return GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Lobby ||
               GamePhaseManager.Instance?.Phase.Value == GamePhaseManager.GamePhase.Countdown;
    }

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> ShieldHP = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private RectTransform   _greenFillRect;  // width driven via anchorMax.x
    private RectTransform   _shieldFillRect; // grey shield overlay on top of green fill
    private TextMeshProUGUI _healthText;
    private RectTransform   _barRect;       // root rect for this player's screen-space bar
    private Camera          _cam;
    private bool            _isDead;

    // Shared screen-space canvas for all health bars (created once).
    private static Canvas _hudCanvas;

    [SerializeField] private Color shieldBarColor = new Color(0.62f, 0.62f, 0.62f, 1f);

    // ── Regen (server-only) ───────────────────────────────────────────────────
    [SerializeField] private float passiveRegenPerSecond = 2f;
    [SerializeField] private float spawnRegenPerTick     = 10f;
    [SerializeField] private float spawnRegenTickRate    = 4f;   // ticks per second

    private float     _passiveRegenAccum;
    private float     _spawnRegenTimer;
    private Transform _cachedSpawnTransform;
    private int       _cachedSpawnTeam = -99;

    // ------------------------------------------------------------------ lifecycle

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged   += OnHealthChanged;
        ShieldHP.OnValueChanged += OnShieldHPChanged;
        _cam = Camera.main;
        CreateHealthBar();
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged   -= OnHealthChanged;
        ShieldHP.OnValueChanged -= OnShieldHPChanged;
        DestroyBar();
    }

    public override void OnDestroy()
    {
        DestroyBar();
    }

    // ------------------------------------------------------------------ damage / death

    public void TakeDamage(float amount, ulong killerClientId = ulong.MaxValue)
    {
        if (!IsServer || _isDead) return;
        if (IsImmuneTo(killerClientId)) return;

        // Drain shield first
        if (ShieldHP.Value > 0f)
        {
            float absorbed = Mathf.Min(ShieldHP.Value, amount);
            ShieldHP.Value -= absorbed;
            amount         -= absorbed;
            if (amount <= 0f) return;
        }

        Health.Value = Mathf.Max(0f, Health.Value - amount);

        if (Health.Value <= 0f)
        {
            _isDead = true;
            // Both RPCs must be sent BEFORE despawn while NetworkObject is still valid.
            NotifyDeathRpc(respawnDelay);
            AnnounceKillRpc(killerClientId, OwnerClientId);
            StartCoroutine(DespawnAndRespawn(OwnerClientId));
        }
    }

    // ── Shield API (server-only) ─────────────────────────────────────────────

    public void SetBaseHealth(int charIndex)
    {
        if (!IsServer) return;
        maxHealth    = charIndex == 0 ? 150f : 100f;
        Health.Value = maxHealth;
    }

    public void ResetToBase(int charIdx)
    {
        if (!IsServer) return;
        maxHealth      = charIdx == 0 ? 150f : 100f;
        Health.Value   = maxHealth;
        ShieldHP.Value = 0f;
    }

    public void GrantShield(float amount, float duration)
    {
        if (!IsServer) return;
        ShieldHP.Value = amount;
        StartCoroutine(ExpireShield(duration));
    }

    private IEnumerator ExpireShield(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (ShieldHP.Value > 0f) ShieldHP.Value = 0f;
    }

    // ── Regen update (server-only) ────────────────────────────────────────────

    private void Update()
    {
        if (!IsServer || _isDead || Health.Value >= maxHealth) return;

        // Passive: 2 HP/s — accumulated locally, written to NetworkVariable in
        // whole-HP chunks to avoid sending an update every single frame.
        _passiveRegenAccum += passiveRegenPerSecond * Time.deltaTime;
        if (_passiveRegenAccum >= 1f)
        {
            float toAdd = Mathf.Floor(_passiveRegenAccum);
            _passiveRegenAccum -= toAdd;
            Health.Value = Mathf.Min(maxHealth, Health.Value + toAdd);
        }

        // Spawn regen: 10 HP, 4 times per second, only when inside team spawn
        if (IsNearTeamSpawn())
        {
            _spawnRegenTimer += Time.deltaTime;
            float tickInterval = 1f / spawnRegenTickRate;
            while (_spawnRegenTimer >= tickInterval)
            {
                _spawnRegenTimer -= tickInterval;
                Health.Value      = Mathf.Min(maxHealth, Health.Value + spawnRegenPerTick);
            }
        }
        else
        {
            _spawnRegenTimer = 0f;
        }
    }

    private bool IsNearTeamSpawn()
    {
        var pc = GetComponent<PlayerController>();
        if (pc == null) return false;
        int team = pc.TeamIndex.Value;
        if (team < 0) return false;

        if (_cachedSpawnTeam != team || _cachedSpawnTransform == null)
        {
            _cachedSpawnTeam = team;
            string spawnName = team == 0 ? "Blue Spawn" : "Red Spawn";
            var go = GameObject.Find(spawnName);
            _cachedSpawnTransform = go != null ? go.transform : null;
        }

        if (_cachedSpawnTransform == null) return false;

        Vector3 spawnXZ  = new Vector3(_cachedSpawnTransform.position.x, 0f, _cachedSpawnTransform.position.z);
        Vector3 playerXZ = new Vector3(transform.position.x, 0f, transform.position.z);
        float   radius   = _cachedSpawnTransform.lossyScale.x * 0.5f;
        return Vector3.Distance(playerXZ, spawnXZ) <= radius;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceKillRpc(ulong killer, ulong victim)
    {
        string killerName = killer == ulong.MaxValue ? "Unknown" : $"Player {killer + 1}";
        string victimName = $"Player {victim + 1}";
        OnKillAnnouncement?.Invoke($"{killerName} has defeated {victimName}");
    }

    [Rpc(SendTo.Owner)]
    private void NotifyDeathRpc(float duration)
    {
        OnLocalPlayerDeath?.Invoke(duration);
    }

    private IEnumerator DespawnAndRespawn(ulong clientId)
    {
        // Save player state before the NetworkObject is removed.
        var pc      = GetComponent<PlayerController>();
        int team    = pc?.TeamIndex.Value ?? 0;
        Color col   = pc?.PlayerColor.Value ?? Color.white;
        int charIdx = pc?.CharacterIndex.Value ?? 0;

        // Despawn(false): removes from all clients' views but keeps this server-side
        // GO alive so the coroutine can finish.
        NetworkObject.Despawn(false);

        // Disable the renderer (not SetActive — that kills the coroutine).
        var rend = GetComponent<Renderer>();
        if (rend != null) rend.enabled = false;
        DestroyBar();

        yield return new WaitForSeconds(respawnDelay);

        var spawnPos = GamePhaseManager.Instance != null
            ? GamePhaseManager.Instance.GetTeamSpawnPosition(team)
            : PlayerController.RandomSpawnForTeam(team, 1f);
        var prefab   = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var go       = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        // Restore team, color, and character via NetworkVariables.
        var newPc = go.GetComponent<PlayerController>();
        newPc.TeamIndex.Value      = team;
        newPc.PlayerColor.Value    = col;
        newPc.CharacterIndex.Value = charIdx;

        // OnNetworkSpawn runs before TeamIndex is set so it picks the wrong position.
        // Re-teleport now that the team is assigned.
        newPc.TeleportTo(spawnPos);

        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ bar positioning

    private void LateUpdate()
    {
        if (_barRect == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(transform.position + Vector3.up * WorldHeadOffset);

        // Hide bar if player is behind the camera.
        bool visible = screenPos.z > 0f;
        _barRect.gameObject.SetActive(visible);
        if (visible)
            _barRect.position = new Vector3(screenPos.x, screenPos.y, 0f);
    }

    // ------------------------------------------------------------------ bar state

    private void OnHealthChanged(float previous, float current)
    {
        UpdateBar(current);
    }

    private void OnShieldHPChanged(float previous, float current)
    {
        UpdateBar(Health.Value);  // redraws green + shield position + text
    }

    private void UpdateShieldBar(float shield)
    {
        if (_shieldFillRect == null) return;
        bool  atFull     = Health.Value >= maxHealth;
        float effMax     = atFull ? (maxHealth + shield) : maxHealth;
        if (effMax <= 0f) return;
        float greenFrac  = Mathf.Clamp01(Health.Value / effMax);
        float shieldFrac = Mathf.Clamp01(shield / effMax);
        _shieldFillRect.anchorMin = new Vector2(greenFrac, 0f);
        _shieldFillRect.anchorMax = new Vector2(Mathf.Min(1f, greenFrac + shieldFrac), 1f);
    }

    private void UpdateBar(float current)
    {
        float shield = ShieldHP.Value;
        bool  atFull = current >= maxHealth;
        float effMax = (atFull && shield > 0f) ? (maxHealth + shield) : maxHealth;
        float gFrac  = effMax > 0f ? Mathf.Clamp01(current / effMax) : 0f;

        if (_greenFillRect != null)
            _greenFillRect.anchorMax = new Vector2(gFrac, 1f);

        UpdateShieldBar(shield);

        if (_healthText != null)
            _healthText.text = $"{Mathf.RoundToInt(current + shield)}/{Mathf.RoundToInt(effMax)}";
    }

    private void DestroyBar()
    {
        if (_barRect != null)
        {
            Destroy(_barRect.gameObject);
            _barRect = null;
        }
    }

    // ------------------------------------------------------------------ bar creation

    private void CreateHealthBar()
    {
        var hud = GetOrCreateHudCanvas();

        // Root container for this player's bar.
        var barGO = new GameObject($"HealthBar_{GetInstanceID()}");
        barGO.transform.SetParent(hud.transform, false);
        _barRect           = barGO.AddComponent<RectTransform>();
        _barRect.sizeDelta = new Vector2(BarW, BarH);
        _barRect.pivot     = new Vector2(0.5f, 0f);   // anchor at bottom-centre

        // Layer 1 — dark background
        MakeFullRect(barGO, "Background")
            .AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        // Layer 2 — red full-width (missing health)
        MakeFullRect(barGO, "RedBar")
            .AddComponent<Image>().color = new Color(0.82f, 0.18f, 0.18f);

        // Layer 3 — grey shield (right-anchored; green draws on top where they overlap)
        var shieldGO    = new GameObject("ShieldFill");
        shieldGO.transform.SetParent(barGO.transform, false);
        _shieldFillRect = shieldGO.AddComponent<RectTransform>();
        _shieldFillRect.anchorMin = new Vector2(1f, 0f);  // starts collapsed at right edge
        _shieldFillRect.anchorMax = new Vector2(1f, 1f);
        _shieldFillRect.offsetMin = _shieldFillRect.offsetMax = Vector2.zero;
        shieldGO.AddComponent<Image>().color = shieldBarColor;

        // Layer 4 — green fill (current health); drawn on top of grey so overlap looks correct
        var greenGO      = new GameObject("GreenFill");
        greenGO.transform.SetParent(barGO.transform, false);
        _greenFillRect   = greenGO.AddComponent<RectTransform>();
        _greenFillRect.anchorMin  = Vector2.zero;
        _greenFillRect.anchorMax  = Vector2.one;   // starts full; UpdateBar shrinks it
        _greenFillRect.offsetMin  = _greenFillRect.offsetMax = Vector2.zero;
        greenGO.AddComponent<Image>().color = new Color(0.18f, 0.78f, 0.18f);

        // Layer 5 — text label sized to 100% of bar height
        var textGO  = MakeFullRect(barGO, "HealthText");
        _healthText = textGO.AddComponent<TextMeshProUGUI>();
        _healthText.alignment          = TextAlignmentOptions.Center;
        _healthText.color              = Color.white;
        _healthText.fontStyle          = FontStyles.Bold;
        _healthText.enableAutoSizing   = true;
        _healthText.fontSizeMin        = 4f;
        _healthText.fontSizeMax        = BarH;   // fill 100% of bar height
        _healthText.overflowMode       = TextOverflowModes.Overflow;

        UpdateBar(Health.Value);
        UpdateShieldBar(ShieldHP.Value);
    }

    private static Canvas GetOrCreateHudCanvas()
    {
        if (_hudCanvas != null) return _hudCanvas;

        var go = new GameObject("HUDCanvas");
        _hudCanvas = go.AddComponent<Canvas>();
        _hudCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _hudCanvas.sortingOrder = 10;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        return _hudCanvas;
    }

    private static GameObject MakeFullRect(GameObject parent, string name)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return go;
    }
}
