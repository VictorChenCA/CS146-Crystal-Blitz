using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// A target dummy in the lobby center. Resets health 5s after last hit.
/// Server-authoritative; implements IDamageable so projectiles can hit it.
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

        Health.Value = Mathf.Max(0f, Health.Value - amount);
        _resetCoroutine = StartCoroutine(ResetAfterDelay());
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Instance       = this;
            Health.Value   = maxHealth;
        }
        else
        {
            // Clients still need the reference for proximity checks
            if (Instance == null) Instance = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Reset coroutine ──────────────────────────────────────────────────────

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);
        Health.Value    = maxHealth;
        _resetCoroutine = null;
    }

    // ── Health bar (screen-space OnGUI) ──────────────────────────────────────

    private Camera _cam;

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
    }

    private const float BarW          = 120f;
    private const float BarH          = 14f;
    private const float WorldHeadOffset = 1.8f;

    private void OnGUI()
    {
        if (_cam == null) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(transform.position + Vector3.up * WorldHeadOffset);
        if (screenPos.z <= 0f) return;

        float screenY = Screen.height - screenPos.y;
        float x = screenPos.x - BarW * 0.5f;
        float y = screenY - BarH;

        // Background
        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, BarW, BarH), Texture2D.whiteTexture);

        // Green fill
        float t = Mathf.Clamp01(Health.Value / maxHealth);
        GUI.color = new Color(0.2f, 0.78f, 0.2f, 1f);
        GUI.DrawTexture(new Rect(x, y, BarW * t, BarH), Texture2D.whiteTexture);

        // Label
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 18f, BarW, 18f), "Training Dummy");
    }
}
