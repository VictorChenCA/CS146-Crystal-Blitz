using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Purely client-side guided tutorial. Auto-spawns via RuntimeInitializeOnLoadMethod.
/// Skipped entirely on subsequent runs (PlayerPrefs gate "nrtm_tutorial_done").
/// Drives DummyHints.ShowTutorialMessage / HideTutorialMessage for world-space display.
/// Other scripts call the static Action hooks; TutorialManager subscribes only while active.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    // ── Static hooks (null-safe from call sites) ──────────────────────────────
    public static Action OnAutoAttackHit;
    public static Action OnQFired;
    public static Action OnWFired;
    public static Action OnEFired;

    // ── State machine ─────────────────────────────────────────────────────────
    private enum TutorialStep
    {
        Idle,
        WaitForAA,
        ShowAA,
        WaitForQ,
        ShowQ,
        ShowQHint,
        WaitForW,
        ShowW,
        ShowWHint,
        WaitForE,
        ShowE,
        ShowEHint,
        Done
    }

    private TutorialStep     _step          = TutorialStep.Idle;
    private float            _showUntil;
    private string           _activeMessage = "";
    private bool             _visible;
    private PlayerController _localPc;
    private DummyHints       _dummyHints;

    private const float DoneShowTime = 4f;
    private const float StepShowTime = 3f;
    private const float HintShowTime = 2.5f;
    private const string HoverHint   = "Hover over the ability icon for more details.";

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (PlayerPrefs.GetInt("nrtm_tutorial_done", 0) == 1) return;

        var go = new GameObject("[TutorialManager]");
        go.AddComponent<TutorialManager>();
        DontDestroyOnLoad(go);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        OnAutoAttackHit += HandleAutoAttackHit;
        OnQFired        += HandleQFired;
        OnWFired        += HandleWFired;
        OnEFired        += HandleEFired;
    }

    private void OnDisable()
    {
        OnAutoAttackHit -= HandleAutoAttackHit;
        OnQFired        -= HandleQFired;
        OnWFired        -= HandleWFired;
        OnEFired        -= HandleEFired;
    }

    private void OnDestroy()
    {
        _dummyHints?.HideTutorialMessage();
    }

    private void Start()
    {
        _step    = TutorialStep.WaitForAA;
        _visible = false;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Resolve local player lazily
        if (_localPc == null)
        {
            var netMgr = NetworkManager.Singleton;
            if (netMgr != null && netMgr.IsClient)
            {
                var playerObj = netMgr.LocalClient?.PlayerObject;
                if (playerObj != null)
                    _localPc = playerObj.GetComponent<PlayerController>();
            }
        }

        // Resolve DummyHints lazily (handles scene reload)
        if (_dummyHints == null)
            _dummyHints = FindObjectOfType<DummyHints>();

        // Auto-advance timed Show states
        if (_visible && Time.time >= _showUntil)
            AdvanceFromShowState();

        // Drive world-space display on DummyHints
        if (_dummyHints != null)
        {
            string msg = GetDisplayMessage();
            if (!string.IsNullOrEmpty(msg))
                _dummyHints.ShowTutorialMessage(msg);
            else
                _dummyHints.HideTutorialMessage();
        }
    }

    // ── Hook handlers ─────────────────────────────────────────────────────────

    private void HandleAutoAttackHit()
    {
        if (_step != TutorialStep.WaitForAA) return;
        EnterShowState(TutorialStep.ShowAA,
            "That's an auto attack! Auto attacks deal damage and scale with your level.",
            StepShowTime);
    }

    private void HandleQFired()
    {
        if (_step != TutorialStep.WaitForQ) return;
        int charIdx = _localPc?.CharacterIndex.Value ?? 0;
        string msg = charIdx == 0
            ? "Heavy Round: Fires a large, slow projectile toward a target point."
            : "Swift Shot: Fires a small, fast projectile toward a target point.";
        EnterShowState(TutorialStep.ShowQ, msg, StepShowTime);
    }

    private void HandleWFired()
    {
        if (_step != TutorialStep.WaitForW) return;
        int charIdx = _localPc?.CharacterIndex.Value ?? 0;
        string msg = charIdx == 0
            ? "Bulwark: Grants a shield that absorbs damage before health. Scales in strength with level!"
            : "Blink Step: Dash in the aimed direction. Range scales with level!";
        EnterShowState(TutorialStep.ShowW, msg, StepShowTime);
    }

    private void HandleEFired()
    {
        if (_step != TutorialStep.WaitForE) return;
        int charIdx = _localPc?.CharacterIndex.Value ?? 0;
        string msg = charIdx == 0
            ? "Buckshot: Fires 5 projectiles in a wide fan arc — great for area control!"
            : "Burst Fire: Fires 3 projectiles in rapid sequence along the same direction!";
        EnterShowState(TutorialStep.ShowE, msg, StepShowTime);
    }

    // ── State helpers ─────────────────────────────────────────────────────────

    private void EnterShowState(TutorialStep showStep, string message, float duration)
    {
        _step          = showStep;
        _activeMessage = message;
        _showUntil     = Time.time + duration;
        _visible       = true;
    }

    private void AdvanceFromShowState()
    {
        switch (_step)
        {
            case TutorialStep.ShowAA:
                _step    = TutorialStep.WaitForQ;
                _visible = false;
                break;

            case TutorialStep.ShowQ:
                EnterShowState(TutorialStep.ShowQHint, HoverHint, HintShowTime);
                break;

            case TutorialStep.ShowQHint:
                _step    = TutorialStep.WaitForW;
                _visible = false;
                break;

            case TutorialStep.ShowW:
                EnterShowState(TutorialStep.ShowWHint, HoverHint, HintShowTime);
                break;

            case TutorialStep.ShowWHint:
                _step    = TutorialStep.WaitForE;
                _visible = false;
                break;

            case TutorialStep.ShowE:
                EnterShowState(TutorialStep.ShowEHint, HoverHint, HintShowTime);
                break;

            case TutorialStep.ShowEHint:
                EnterShowState(TutorialStep.Done,
                    "You're ready! Walk to the start zone when all players are set.",
                    DoneShowTime);
                break;

            case TutorialStep.Done:
                _visible = false;
                PlayerPrefs.SetInt("nrtm_tutorial_done", 1);
                PlayerPrefs.Save();
                Destroy(gameObject);
                break;
        }
    }

    // ── Message builder ───────────────────────────────────────────────────────

    private string GetDisplayMessage()
    {
        switch (_step)
        {
            case TutorialStep.WaitForQ:
            {
                int charIdx = _localPc?.CharacterIndex.Value ?? 0;
                string name = charIdx == 0 ? "Heavy Round" : "Swift Shot";
                return $"Press [{QKeyName()}] to use {name}!";
            }

            case TutorialStep.WaitForW:
            {
                int charIdx = _localPc?.CharacterIndex.Value ?? 0;
                string name = charIdx == 0 ? "Bulwark" : "Blink Step";
                return $"Press [{WKeyName()}] to use {name}!";
            }

            case TutorialStep.WaitForE:
            {
                int charIdx = _localPc?.CharacterIndex.Value ?? 0;
                string name = charIdx == 0 ? "Buckshot" : "Burst Fire";
                return $"Press [{EKeyName()}] to use {name}!";
            }

            case TutorialStep.ShowAA:
            case TutorialStep.ShowQ:
            case TutorialStep.ShowQHint:
            case TutorialStep.ShowW:
            case TutorialStep.ShowWHint:
            case TutorialStep.ShowE:
            case TutorialStep.ShowEHint:
            case TutorialStep.Done:
                return _visible ? _activeMessage : null;

            default:
                return null;
        }
    }

    // ── Key name helpers ──────────────────────────────────────────────────────

    private static string QKeyName() => GameSettings.UseWasd
        ? GameKeybinds.KeyName(GameKeybinds.Wasd_Ability1)
        : GameKeybinds.KeyName(GameKeybinds.PnC_Ability1);

    private static string WKeyName() => GameSettings.UseWasd
        ? GameKeybinds.KeyName(GameKeybinds.Wasd_Ability2)
        : GameKeybinds.KeyName(GameKeybinds.PnC_Ability2);

    private static string EKeyName() => GameSettings.UseWasd
        ? GameKeybinds.KeyName(GameKeybinds.Wasd_Ability3)
        : GameKeybinds.KeyName(GameKeybinds.PnC_Ability3);
}
