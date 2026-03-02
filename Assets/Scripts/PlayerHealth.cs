using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Image _healthFill;
    private Transform _barCanvas;
    private Camera _cam;

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

    // Called directly by the server (e.g. ProjectileController hit detection).
    public void TakeDamage(float amount)
    {
        if (!IsServer) return;
        Health.Value = Mathf.Max(0f, Health.Value - amount);
    }

    private void OnHealthChanged(float previous, float current)
    {
        UpdateFill(current);
    }

    private void LateUpdate()
    {
        if (_barCanvas == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // Billboard — face the camera so the bar is always readable.
        _barCanvas.LookAt(_cam.transform);
        _barCanvas.Rotate(0f, 180f, 0f);
    }

    private void UpdateFill(float current)
    {
        if (_healthFill == null) return;
        float t = Mathf.Clamp01(current / maxHealth);
        _healthFill.fillAmount = t;
        _healthFill.color = Color.Lerp(Color.red, Color.green, t);
    }

    private void CreateHealthBar()
    {
        // World-space canvas parented to the player, positioned above the head.
        var canvasGO = new GameObject("HealthBarCanvas");
        canvasGO.transform.SetParent(transform);
        canvasGO.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        // Scale down so a 100×14 RectTransform = 1.0 × 0.14 world units.
        canvasGO.transform.localScale = Vector3.one * 0.01f;
        _barCanvas = canvasGO.transform;

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(100f, 14f);

        // Dark background
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        // Coloured fill (Filled mode — shrinks from right as health drops)
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(canvasGO.transform, false);
        var fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        _healthFill = fillGO.AddComponent<Image>();
        _healthFill.type        = Image.Type.Filled;
        _healthFill.fillMethod  = Image.FillMethod.Horizontal;
        _healthFill.fillOrigin  = 0;

        UpdateFill(Health.Value);
    }
}
