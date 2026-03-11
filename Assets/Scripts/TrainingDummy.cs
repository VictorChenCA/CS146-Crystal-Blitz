using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TMPro;

/// <summary>
/// A target dummy in the lobby center. Resets health 5s after last hit.
/// Server-authoritative; implements IDamageable so projectiles can hit it.
/// Shows a pulsing "Right-click to attack!" hint per-client that dismisses
/// only for the player who lands their first auto-attack.
/// </summary>
public class TrainingDummy : NetworkBehaviour, IDamageable
{
    public static TrainingDummy Instance { get; private set; }

    [SerializeField] private float maxHealth  = 1000f;
    [SerializeField] private float resetDelay = 5f;

    public NetworkVariable<float> Health = new NetworkVariable<float>(
        1000f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine _resetCoroutine;

    // ── Tutorial hint (purely client-side, per-player) ───────────────────────

    private TextMeshPro _hintText;
    private Coroutine   _pulseCoroutine;
    private bool        _hintHidden;

    private void Awake()
    {
        CreateHintText();
    }

    private void CreateHintText()
    {
        var go = new GameObject("AutoAttackHint");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 2.5f, 0f);

        _hintText           = go.AddComponent<TextMeshPro>();
        _hintText.text      = "Right-click to attack!";
        _hintText.fontSize  = 1.5f;   // world-space units — keep small
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color     = Color.white;

        // Wide enough for the text, tall enough for one line
        _hintText.rectTransform.sizeDelta = new Vector2(6f, 1f);
    }

    // ── IDamageable ──────────────────────────────────────────────────────────

    public float MaxHealth     => maxHealth;
    public float CurrentHealth => Health.Value;
    public bool  IsImmuneTo(ulong attackerClientId) => false;

    public void TakeDamage(float amount, ulong attackerClientId)
    {
        if (!IsServer) return;

        if (_resetCoroutine != null)
        {
            StopCoroutine(_resetCoroutine);
            _resetCoroutine = null;
        }

        Health.Value    = Mathf.Max(0f, Health.Value - amount);
        _resetCoroutine = StartCoroutine(ResetAfterDelay());
    }

    /// <summary>
    /// Called by HomingProjectileController (server-side) after an auto-attack
    /// lands. Sends a targeted RPC so only that shooter's client hides the hint.
    /// </summary>
    public void NotifyAutoAttackHit(ulong shooterClientId)
    {
        if (!IsServer) return;
        HideHintClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { shooterClientId } }
        });
    }

    [ClientRpc]
    private void HideHintClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!_hintHidden)
            StartCoroutine(FadeOutHint());
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Instance     = this;
            Health.Value = maxHealth;
        }
        else
        {
            if (Instance == null) Instance = this;
        }

        _pulseCoroutine = StartCoroutine(PulseHint());
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Hint coroutines ──────────────────────────────────────────────────────

    private IEnumerator PulseHint()
    {
        while (true)
        {
            float alpha = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * 1.5f));
            Color c = _hintText.color;
            c.a = alpha;
            _hintText.color = c;
            yield return null;
        }
    }

    private IEnumerator FadeOutHint()
    {
        _hintHidden = true;

        if (_pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
        }

        float elapsed    = 0f;
        float duration   = 0.5f;
        float startAlpha = _hintText.color.a;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Color c = _hintText.color;
            c.a = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
            _hintText.color = c;
            yield return null;
        }

        _hintText.gameObject.SetActive(false);
    }

    // ── Reset coroutine ──────────────────────────────────────────────────────

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);
        Health.Value    = maxHealth;
        _resetCoroutine = null;
    }

    // ── Health bar + hint billboard ──────────────────────────────────────────

    private Camera _cam;

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;

        // Billboard hint to always face the camera
        if (_hintText != null && !_hintHidden && _cam != null)
        {
            _hintText.transform.rotation = Quaternion.LookRotation(
                _hintText.transform.position - _cam.transform.position
            );
        }
    }

    private const float BarW            = 120f;
    private const float BarH            = 14f;
    private const float WorldHeadOffset = 1.8f;

    private void OnGUI()
    {
        if (_cam == null) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(transform.position + Vector3.up * WorldHeadOffset);
        if (screenPos.z <= 0f) return;

        float screenY = Screen.height - screenPos.y;
        float x = screenPos.x - BarW * 0.5f;
        float y = screenY - BarH;

        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, BarW, BarH), Texture2D.whiteTexture);

        float t = Mathf.Clamp01(Health.Value / maxHealth);
        GUI.color = new Color(0.2f, 0.78f, 0.2f, 1f);
        GUI.DrawTexture(new Rect(x, y, BarW * t, BarH), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 18f, BarW, 18f), "Training Dummy");
    }
}
