using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private float maxHealth    = 100f;
    [SerializeField] private float respawnDelay = 3f;

    // Bar dimensions in screen pixels.
    private const float BarW = 140f;
    private const float BarH = 18f;
    // World-space height above the player's origin to anchor the bar.
    private const float WorldHeadOffset = 2.3f;

    // Spawn positions per team (along the diagonal lane).
    private static readonly Vector3[] TeamSpawnPos =
    {
        new Vector3(-14f, 1f, -14f),  // Team 0 Blue — bottom-left end
        new Vector3( 14f, 1f,  14f),  // Team 1 Red  — top-right end
    };

    // Fired on the owner's client so GameBootstrap can show the countdown UI.
    public static event System.Action<float> OnLocalPlayerDeath;

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private RectTransform   _greenFillRect; // width driven via anchorMax.x
    private TextMeshProUGUI _healthText;
    private RectTransform   _barRect;      // root rect for this player's screen-space bar
    private Camera          _cam;
    private bool            _isDead;

    // Shared screen-space canvas for all health bars (created once).
    private static Canvas _hudCanvas;

    // ------------------------------------------------------------------ lifecycle

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged += OnHealthChanged;
        _cam = Camera.main;
        CreateHealthBar();
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged -= OnHealthChanged;
        DestroyBar();
    }

    private void OnDestroy()
    {
        DestroyBar();
    }

    // ------------------------------------------------------------------ damage / death

    public void TakeDamage(float amount)
    {
        if (!IsServer || _isDead) return;

        Health.Value = Mathf.Max(0f, Health.Value - amount);

        if (Health.Value <= 0f)
        {
            _isDead = true;
            // Notify owner BEFORE despawn so the RPC can still be sent.
            NotifyDeathRpc(respawnDelay);
            StartCoroutine(DespawnAndRespawn(OwnerClientId));
        }
    }

    [Rpc(SendTo.Owner)]
    private void NotifyDeathRpc(float duration)
    {
        OnLocalPlayerDeath?.Invoke(duration);
    }

    private IEnumerator DespawnAndRespawn(ulong clientId)
    {
        // Save team before the NetworkObject is removed.
        int team = GetComponent<PlayerController>()?.TeamIndex.Value ?? 0;

        // Despawn(false): removes from all clients' views but keeps this server-side
        // GO alive so the coroutine can finish.
        NetworkObject.Despawn(false);

        // Hide the capsule on the server/host so it doesn't appear on the host's screen.
        GetComponent<Renderer>()?.gameObject.SetActive(false);
        DestroyBar();

        yield return new WaitForSeconds(respawnDelay);

        var spawnPos = TeamSpawnPos[Mathf.Clamp(team, 0, TeamSpawnPos.Length - 1)];
        var prefab   = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var go       = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        // Restore team — fires OnTeamChanged on all clients via NetworkVariable.
        go.GetComponent<PlayerController>().TeamIndex.Value = team;

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

    private void UpdateBar(float current)
    {
        if (_greenFillRect != null)
        {
            float t = Mathf.Clamp01(current / maxHealth);
            // Shrink the right anchor to reduce width — no sprite required.
            _greenFillRect.anchorMax = new Vector2(t, 1f);
        }

        if (_healthText != null)
            _healthText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(maxHealth)}";
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

        // Layer 3 — green fill (current health).
        // Width is controlled by anchorMax.x rather than Image.Type.Filled,
        // which requires a sprite and can silently fail when created in code.
        var greenGO      = new GameObject("GreenFill");
        greenGO.transform.SetParent(barGO.transform, false);
        _greenFillRect   = greenGO.AddComponent<RectTransform>();
        _greenFillRect.anchorMin  = Vector2.zero;
        _greenFillRect.anchorMax  = Vector2.one;   // starts full; UpdateBar shrinks it
        _greenFillRect.offsetMin  = _greenFillRect.offsetMax = Vector2.zero;
        greenGO.AddComponent<Image>().color = new Color(0.18f, 0.78f, 0.18f);

        // Layer 4 — text label sized to 100% of bar height
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
