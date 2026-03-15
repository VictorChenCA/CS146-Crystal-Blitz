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

    // Tutorial display — driven by TutorialManager
    private Transform   _tutorialRoot;
    private TextMeshPro _tutorialText;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        var go = new GameObject("AutoAttackHint");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, hintHeight, 0f);

        _hintText           = go.AddComponent<TextMeshPro>();
        _hintText.text      = "Tutorial: Right-click to auto attack!";
        _hintText.fontSize  = fontSize;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color     = Color.white;
        _hintText.rectTransform.sizeDelta = new Vector2(rectWidth, 1f);

        // ── Tutorial display group (hidden until TutorialManager activates it) ──
        var tutRoot = new GameObject("TutorialHintRoot");
        tutRoot.transform.SetParent(transform);
        tutRoot.transform.localPosition = new Vector3(0f, hintHeight + 1.4f, 0f);
        _tutorialRoot = tutRoot.transform;

        // Dark background quad (rendered at local Z=0, faces camera via billboard)
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(bgGo.GetComponent<Collider>());
        bgGo.transform.SetParent(_tutorialRoot);
        bgGo.transform.localPosition = Vector3.zero;
        bgGo.transform.localRotation = Quaternion.identity;
        bgGo.transform.localScale    = new Vector3(rectWidth + 1.2f, 3.2f, 1f);
        var bgMat = new Material(Shader.Find("Sprites/Default"));
        bgMat.color = new Color(0f, 0f, 0f, 0.72f);
        bgGo.GetComponent<Renderer>().material = bgMat;

        // Tutorial text (local Z = -0.02 → slightly in front of the bg quad)
        var tutTextGo = new GameObject("TutorialHintText");
        tutTextGo.transform.SetParent(_tutorialRoot);
        tutTextGo.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        tutTextGo.transform.localRotation = Quaternion.identity;

        _tutorialText           = tutTextGo.AddComponent<TextMeshPro>();
        _tutorialText.fontSize  = fontSize;
        _tutorialText.alignment = TextAlignmentOptions.Center;
        _tutorialText.color     = Color.white;
        _tutorialText.rectTransform.sizeDelta = new Vector2(rectWidth, 2.8f);

        tutRoot.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        _pulseCoroutine = StartCoroutine(PulseHint());
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Called by TutorialManager to show a message above the dummy.</summary>
    public void ShowTutorialMessage(string msg)
    {
        if (_tutorialText == null) return;
        _tutorialText.text = msg;
        _tutorialRoot.gameObject.SetActive(true);
    }

    /// <summary>Called by TutorialManager to hide the tutorial display.</summary>
    public void HideTutorialMessage()
    {
        if (_tutorialRoot != null)
            _tutorialRoot.gameObject.SetActive(false);
    }

    /// <summary>Resets the AA hint for a new tutorial session.</summary>
    public void ResetHint()
    {
        _hintHidden = false;
        _hintText.gameObject.SetActive(true);
        _hintText.color = new Color(_hintText.color.r, _hintText.color.g, _hintText.color.b, 1f);
        if (_pulseCoroutine != null) StopCoroutine(_pulseCoroutine);
        _pulseCoroutine = StartCoroutine(PulseHint());
        HideTutorialMessage();
    }

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
        {
            TutorialManager.OnAutoAttackHit?.Invoke();
            StartCoroutine(FadeOutHint());
        }
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
        if (_cam == null) return;

        if (_hintText != null && !_hintHidden)
        {
            _hintText.transform.rotation = Quaternion.LookRotation(
                _hintText.transform.position - _cam.transform.position
            );
        }

        if (_tutorialRoot != null && _tutorialRoot.gameObject.activeSelf)
        {
            _tutorialRoot.rotation = Quaternion.LookRotation(
                _tutorialRoot.position - _cam.transform.position
            );
        }
    }
}
