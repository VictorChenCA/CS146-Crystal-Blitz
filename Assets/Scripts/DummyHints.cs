using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TMPro;

/// <summary>
/// Attach alongside TrainingDummy. Manages the per-client pulsing
/// "Right-click to attack!" tutorial hint above the dummy.
/// Dismissed only for the player who lands their first auto-attack.
/// </summary>
public class DummyHints : NetworkBehaviour
{
    [SerializeField] private float hintHeight  = 2.5f;
    [SerializeField] private float fontSize    = 1.5f;
    [SerializeField] private float rectWidth   = 6f;
    [SerializeField] private float flashSpeed  = 1.5f;

    private TextMeshPro _hintText;
    private Coroutine   _pulseCoroutine;
    private bool        _hintHidden;
    private Camera      _cam;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        var go = new GameObject("AutoAttackHint");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, hintHeight, 0f);

        _hintText           = go.AddComponent<TextMeshPro>();
        _hintText.text      = "Right-click to attack!";
        _hintText.fontSize  = fontSize;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color     = Color.white;
        _hintText.rectTransform.sizeDelta = new Vector2(rectWidth, 1f);
    }

    public override void OnNetworkSpawn()
    {
        _pulseCoroutine = StartCoroutine(PulseHint());
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Called by TrainingDummy (server-side) when an auto-attack hits.</summary>
    public void NotifyAutoAttackHit(ulong shooterClientId)
    {
        if (!IsServer) return;
        HideHintClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { shooterClientId } }
        });
    }

    // ── RPC ──────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void HideHintClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!_hintHidden)
            StartCoroutine(FadeOutHint());
    }

    // ── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator PulseHint()
    {
        while (true)
        {
            float alpha = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * flashSpeed));
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

    // ── Billboard ────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;

        if (_hintText != null && !_hintHidden && _cam != null)
        {
            _hintText.transform.rotation = Quaternion.LookRotation(
                _hintText.transform.position - _cam.transform.position
            );
        }
    }
}
