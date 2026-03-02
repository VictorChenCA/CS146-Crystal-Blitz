using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float respawnDelay = 3f;

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Image          _greenFill;
    private TextMeshProUGUI _healthText;
    private Transform      _barCanvas;
    private Camera         _cam;
    private bool           _isDead;

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged += OnHealthChanged;
        CreateHealthBar();
        _cam = Camera.main;
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged -= OnHealthChanged;
    }

    public void TakeDamage(float amount)
    {
        if (!IsServer || _isDead) return;

        Health.Value = Mathf.Max(0f, Health.Value - amount);

        if (Health.Value <= 0f)
        {
            _isDead = true;
            StartCoroutine(DespawnAndRespawn(OwnerClientId));
        }
    }

    private IEnumerator DespawnAndRespawn(ulong clientId)
    {
        NetworkObject.Despawn(false);

        yield return new WaitForSeconds(respawnDelay);

        var prefab   = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var spawnPos = new Vector3(0f, 1f, 0f);
        var go       = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        Destroy(gameObject);
    }

    private void OnHealthChanged(float previous, float current)
    {
        UpdateBar(current);
    }

    private void LateUpdate()
    {
        if (_barCanvas == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        _barCanvas.LookAt(_cam.transform);
        _barCanvas.Rotate(0f, 180f, 0f);
    }

    private void UpdateBar(float current)
    {
        if (_greenFill != null)
            _greenFill.fillAmount = Mathf.Clamp01(current / maxHealth);

        if (_healthText != null)
            _healthText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(maxHealth)}";
    }

    private void CreateHealthBar()
    {
        // World-space canvas: 100×14 rect units × 0.01 scale = 1.0 × 0.14 world units.
        var canvasGO = new GameObject("HealthBarCanvas");
        canvasGO.transform.SetParent(transform);
        canvasGO.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        canvasGO.transform.localScale    = Vector3.one * 0.01f;
        _barCanvas = canvasGO.transform;

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 14f);

        // --- Layer 1: dark background ---
        MakeFullRect(canvasGO, "Background")
            .AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        // --- Layer 2: red bar (full width — always visible as "missing health") ---
        MakeFullRect(canvasGO, "RedBar")
            .AddComponent<Image>().color = new Color(0.82f, 0.18f, 0.18f);

        // --- Layer 3: green fill (shrinks from right as health drops) ---
        var greenGO   = MakeFullRect(canvasGO, "GreenFill");
        _greenFill    = greenGO.AddComponent<Image>();
        _greenFill.color      = new Color(0.18f, 0.78f, 0.18f);
        _greenFill.type       = Image.Type.Filled;
        _greenFill.fillMethod = Image.FillMethod.Horizontal;
        _greenFill.fillOrigin = 0;   // grow from left

        // --- Layer 4: text label ("90/100") ---
        var textGO   = MakeFullRect(canvasGO, "HealthText");
        _healthText  = textGO.AddComponent<TextMeshProUGUI>();
        _healthText.alignment  = TextAlignmentOptions.Center;
        _healthText.color      = Color.white;
        _healthText.fontSize   = 8f;
        _healthText.fontStyle  = FontStyles.Bold;

        UpdateBar(Health.Value);
    }

    // Helper: create a child whose RectTransform stretches to fill the parent.
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
