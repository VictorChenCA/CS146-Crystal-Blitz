using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    // ── State ────────────────────────────────────────────────────────────────
    private enum UIState { MainMenu, ConnectionScreen, InGame, Settings }
    private UIState _state = UIState.MainMenu;
    private UIState _settingsPreviousState = UIState.MainMenu;

    // ── Networking ───────────────────────────────────────────────────────────
    private string _playerName = "Player";
    private string _hostIp = "127.0.0.1";
    private ushort _port = 7777;
    [SerializeField] private bool _useRelay = false;
    private string _joinCode = "";
    private string _relayError = "";
    private bool  _relayBusy   = false;
    private float _copiedUntil = -1f;

    // ── Kill / Death HUD ─────────────────────────────────────────────────────
    private float  _deathTimerEnd    = -1f;
    private string _killMessage      = "";
    private float  _killMessageEnd   = -1f;
    private string _lobbyMessage     = "";
    private float  _lobbyMessageEnd  = -1f;

    // ── FPS counter ──────────────────────────────────────────────────────────
    private float _fpsAccum = 0f;
    private int _fpsFrames = 0;
    private float _fpsNextUpdate = 0f;
    private float _fpsDisplay = 0f;

    // ── Settings toggles ─────────────────────────────────────────────────────
    private bool _showFps = false;
    private bool _showConnectionStatus = false;
    private bool _showLatency = false;
    private bool _useWasd = true;

    // ── Graphics / performance settings ──────────────────────────────────────
    private int   _qualityIndex    = 2;       // 0=Low 1=Med 2=High 3=Ultra
    private float _targetFps       = 60f;     // 241 = uncapped
    private float _guiScale        = 1.5f;
    private float _bottomBarScale  = 1f;
    private float _cursorScale     = 1f;

    // ── Settings panel state ──────────────────────────────────────────────────
    private int     _settingsTab      = 0;   // 0=General 1=Graphics 2=Keybinds
    private int     _keybindTab       = 0;   // 0=WASD 1=P&C
    private int     _prevSettingsTab  = -1;
    private string  _rebindTarget     = null;
    private Vector2 _settingsScrollPos;

    // ── Textures (created in Awake, destroyed in OnDestroy) ──────────────────
    private Texture2D _dimOverlayTex;
    private Texture2D _whitePanelTex;
    private Texture2D _segActiveTex;
    private Texture2D _segInactiveTex;
    private Texture2D _circleTex;

    // ── Ability Icons (assign in Inspector) ──────────────────────────────────
    [Header("Ability Icons")]
    [SerializeField] private Texture2D _iconTankQ;
    [SerializeField] private Texture2D _iconTankW;
    [SerializeField] private Texture2D _iconTankE;
    [SerializeField] private Texture2D _iconRangerQ;
    [SerializeField] private Texture2D _iconRangerW;
    [SerializeField] private Texture2D _iconRangerE;

    // ── Ability Tooltip Data ──────────────────────────────────────────────────
    private struct AbilityTooltipData
    {
        public string Name;
        public string Stat;        // damage / effect line, "" to hide
        public string Description;
    }

    private static readonly AbilityTooltipData[] TankTooltips = {
        new() { Name="Heavy Round", Stat="25 dmg",         Description="Fires a large, slow straight projectile toward a target point."  },
        new() { Name="Bulwark",     Stat="50 shield · 5s", Description="Grants a shield that absorbs damage before health."              },
        new() { Name="Buckshot",    Stat="15 dmg × 5",     Description="Fires 5 projectiles in a wide fan arc."                          },
    };
    private static readonly AbilityTooltipData[] RangerTooltips = {
        new() { Name="Swift Shot",  Stat="25 dmg",         Description="Fires a small, fast straight projectile toward a target point."  },
        new() { Name="Blink Step",  Stat="",               Description="Dashes in the aimed direction."                                  },
        new() { Name="Burst Fire",  Stat="15 dmg × 3",     Description="Fires 3 projectiles in rapid sequence along the same direction." },
    };

    // ── GUIStyles (lazy-initialized once) ────────────────────────────────────
    private GUIStyle _titleStyle;
    private GUIStyle _subtitleStyle;
    private GUIStyle _panelTitleStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _hudLabelStyle;
    private GUIStyle _toggleStyle;
    private GUIStyle _smallToggleStyle;
    private GUIStyle _joinCodeStyle;
    private GUIStyle _smallButtonStyle;
    private GUIStyle _announcementStyle;
    private GUIStyle _levelStyle;
    private GUIStyle _segActiveStyle;
    private GUIStyle _segInactiveStyle;
    private GUIStyle _panelLabelStyle;
    private GUIStyle _textFieldStyle;
    private GUIStyle _tooltipStyle;
    private bool _stylesInitialized;

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _dimOverlayTex  = MakeTex(new Color(0f,    0f,    0f,   0.6f));
        _whitePanelTex  = MakeTex(new Color(1f,    1f,    1f,   0.95f));
        _segActiveTex   = MakeTex(new Color(0.18f, 0.38f, 0.72f, 1f));
        _segInactiveTex = MakeTex(new Color(0.82f, 0.82f, 0.82f, 1f));
        _circleTex      = MakeCircleTex(128);

        ApplyRenderScale(_qualityIndex);
        Application.targetFrameRate = (int)_targetFps;
        GameKeybinds.Load();
        _playerName = PlayerPrefs.GetString("playerName", "Player");
    }

    private void OnDestroy()
    {
        Destroy(_dimOverlayTex);
        Destroy(_whitePanelTex);
        Destroy(_segActiveTex);
        Destroy(_segInactiveTex);
        Destroy(_circleTex);
    }

    private void OnEnable()
    {
        PlayerHealth.OnLocalPlayerDeath += HandleLocalPlayerDeath;
        PlayerHealth.OnKillAnnouncement += HandleKillAnnouncement;
    }

    private void OnDisable()
    {
        PlayerHealth.OnLocalPlayerDeath -= HandleLocalPlayerDeath;
        PlayerHealth.OnKillAnnouncement -= HandleKillAnnouncement;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleLocalPlayerDeath(float duration) => _deathTimerEnd = Time.time + duration;
    private void HandleKillAnnouncement(string message)
    {
        _killMessage = message;
        _killMessageEnd = Time.time + 4f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        // FPS accumulation
        _fpsAccum += Time.unscaledDeltaTime;
        _fpsFrames += 1;
        if (Time.unscaledTime >= _fpsNextUpdate)
        {
            _fpsDisplay = _fpsFrames / _fpsAccum;
            _fpsAccum = 0f;
            _fpsFrames = 0;
            _fpsNextUpdate = Time.unscaledTime + 1f;
        }

        var nm = NetworkManager.Singleton;

        // Cursor always visible (used for projectile aiming and P&C movement)
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;

        // NetworkManager gone → back to main menu
        if ((_state == UIState.InGame || _state == UIState.Settings) && nm == null)
            _state = UIState.MainMenu;

        // Connection established → enter InGame
        if (_state == UIState.ConnectionScreen && nm != null && (nm.IsClient || nm.IsServer))
        {
            _relayBusy = false;
            _state = UIState.InGame;
        }

        bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        if (escPressed)
        {
            if (_state == UIState.InGame)
            {
                _settingsPreviousState = UIState.InGame;
                _state = UIState.Settings;
            }
            else if (_state == UIState.Settings)
            {
                _state = _settingsPreviousState;
            }
            else if (_state == UIState.ConnectionScreen)
            {
                _state = UIState.MainMenu;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OnGUI dispatcher
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EnsureStyles();

        // ── Rebind listening mode ─────────────────────────────────────────────
        if (_rebindTarget != null)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    _rebindTarget = null;
                else
                    TryCompleteRebind();
            }
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dimOverlayTex);
            var promptStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };
            float pw = 400f, ph = 80f;
            GUI.Box(new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph),
                    "Press a key...\n(ESC to cancel)", promptStyle);
            return;
        }

        switch (_state)
        {
            case UIState.MainMenu: DrawMainMenu(); break;
            case UIState.ConnectionScreen: DrawConnectionScreen(); break;
            case UIState.InGame:
                DrawInGameHUD();
                break;
            case UIState.Settings:
                DrawInGameHUD();
                DrawSettingsPanel();
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Phase-specific HUD overlays
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the persistent top-of-screen phase banner and returns the Y
    /// position just below it so callers can stack further boxes underneath.
    /// </summary>
    private float DrawPhaseTopBanner(float startY)
    {
        var gpm = GamePhaseManager.Instance;
        if (gpm == null) return startY;

        if (gpm.Phase.Value == GamePhaseManager.GamePhase.Lobby)
            return DrawLobbyBanner(gpm, startY);

        return startY;
    }

    private float DrawLobbyBanner(GamePhaseManager gpm, float startY)
    {
        float sw = Screen.width;
        float w  = 700f, h = 50f;
        float x  = (sw - w) * 0.5f;

        int total = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsList.Count : 1;

        string msg = $"{gpm.PlayersReadyCount.Value}/{total} players ready in start zone";

        GUI.Box(new Rect(x, startY, w, h), msg, _announcementStyle);
        return startY + h + 6f;
    }

    private void DrawCountdownHUD(GamePhaseManager gpm)
    {
        float sw = Screen.width, sh = Screen.height;
        float remaining = Mathf.Max(0f, gpm.CountdownEndTime.Value - Time.time);
        int secs = Mathf.CeilToInt(remaining);
        float w = 200f, h = 120f;
        GUI.Box(new Rect((sw - w) * 0.5f, (sh - h) * 0.5f, w, h),
                secs > 0 ? secs.ToString() : "GO!", _announcementStyle);
    }

    private void DrawGameOverHUD(GamePhaseManager gpm)
    {
        float sw = Screen.width, sh = Screen.height;
        int winner = gpm.WinningTeam.Value;
        string teamName = winner == 0 ? "Blue Team" : winner == 1 ? "Red Team" : "Unknown";
        float w = 500f, h = 100f;
        GUI.Box(new Rect((sw - w) * 0.5f, sh * 0.3f, w, h),
                $"{teamName} Wins!\nReturning to lobby...", _announcementStyle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Main Menu
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawMainMenu()
    {
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_guiScale, _guiScale, 1f));
        float sw = Screen.width  / _guiScale;
        float sh = Screen.height / _guiScale;

        // Title
        float titleW = 600f, titleH = 80f;
        float titleX = (sw - titleW) * 0.5f;
        float titleY = sh * 0.22f;
        GUI.Label(new Rect(titleX, titleY, titleW, titleH), "NRAM", _titleStyle);

        // Subtitle
        float subW = 600f, subH = 36f;
        float subX = (sw - subW) * 0.5f;
        float subY = titleY + titleH + 6f;
        GUI.Label(new Rect(subX, subY, subW, subH), "(Not Random, All Mid)", _subtitleStyle);

        // Buttons
        float btnW = 300f, btnH = 60f, btnGap = 20f;
        float btnX = (sw - btnW) * 0.5f;
        float btn1Y = sh * 0.52f;

        // Username
        float fieldW = 300f, fieldH = 44f;
        float fieldX = (sw - fieldW) * 0.5f;
        float fieldY = btn1Y - fieldH - 24f;
        GUI.Label(new Rect(fieldX, fieldY - 28f, fieldW, 28f), "Username", _labelStyle);
        string newName = GUI.TextField(new Rect(fieldX, fieldY, fieldW, fieldH), _playerName, 24, _textFieldStyle);
        if (newName != _playerName)
        {
            _playerName = newName;
            PlayerPrefs.SetString("playerName", _playerName);
        }

        if (GUI.Button(new Rect(btnX, btn1Y, btnW, btnH), "Play", _buttonStyle))
            _state = UIState.ConnectionScreen;

        if (GUI.Button(new Rect(btnX, btn1Y + (btnH + btnGap), btnW, btnH), "Settings", _buttonStyle))
        {
            _settingsPreviousState = UIState.MainMenu;
            _state = UIState.Settings;
        }

        if (GUI.Button(new Rect(btnX, btn1Y + 2f * (btnH + btnGap), btnW, btnH), "Quit", _buttonStyle))
            Application.Quit();

        GUI.matrix = Matrix4x4.identity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Connection Screen
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawConnectionScreen()
    {
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2f, 2f, 1f));

        float logW = Screen.width / 2f;
        float logH = Screen.height / 2f;
        float panelW = 220f;
        float panelH = _useRelay ? 370f : 260f;
        float panelX = (logW - panelW) * 0.5f;
        float panelY = (logH - panelH) * 0.5f + logH * 0.15f;

        GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));

        if (!_useRelay)
        {
            // ── LAN mode (unchanged) ─────────────────────────────────────
            GUILayout.Label("Host IP:", _labelStyle);
            _hostIp = GUILayout.TextField(_hostIp, 32, _textFieldStyle);

            if (GUILayout.Button("Host", _buttonStyle))
            {
                SetTransport("0.0.0.0", _port);
                NetworkManager.Singleton.StartHost();
            }
            if (GUILayout.Button("Client", _buttonStyle))
            {
                SetTransport(_hostIp, _port);
                NetworkManager.Singleton.StartClient();
            }
            if (GUILayout.Button("Server", _buttonStyle))
            {
                SetTransport("0.0.0.0", _port);
                NetworkManager.Singleton.StartServer();
            }
        }
        else
        {
            // ── Relay mode ───────────────────────────────────────────────
            GUILayout.Label("Join Code:", _labelStyle);
            string raw = GUILayout.TextField(_joinCode, 10, _textFieldStyle);
            _joinCode = raw.ToUpperInvariant();

            if (_relayBusy)
            {
                GUILayout.Label("Contacting Relay...", _labelStyle);
            }
            else
            {
                if (GUILayout.Button("Start Host", _buttonStyle))
                    _ = StartHostWithRelayAsync();

                if (GUILayout.Button("Join Game", _buttonStyle))
                    _ = StartClientWithRelayAsync(_joinCode);

                if (!string.IsNullOrEmpty(_joinCode) && NetworkManager.Singleton != null
                    && NetworkManager.Singleton.IsHost)
                {
                    GUILayout.Label($"Code: {_joinCode}", _labelStyle);
                }

                if (!string.IsNullOrEmpty(_relayError))
                {
                    Color prev = GUI.color;
                    GUI.color = Color.red;
                    GUILayout.Label(_relayError, _labelStyle);
                    GUI.color = prev;
                }
            }
        }

        GUILayout.Space(10f);

        if (!_relayBusy && GUILayout.Button("Back", _buttonStyle))
            _state = UIState.MainMenu;

        GUILayout.EndArea();

        // Small "Start Server" button — bottom-right corner (relay only)
        if (_useRelay && !_relayBusy)
        {
            float sbW = 160f, sbH = 36f, sbPad = 16f;
            if (GUI.Button(new Rect(logW - sbW - sbPad, logH - sbH - sbPad, sbW, sbH),
                           "Start Server", _smallButtonStyle))
                _ = StartServerWithRelayAsync();
        }

        GUI.matrix = Matrix4x4.identity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: In-Game HUD
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawInGameHUD()
    {
        // Dedicated server overlay (persistent, no local player)
        var nmHud = NetworkManager.Singleton;
        if (nmHud != null && nmHud.IsServer && !nmHud.IsClient)
        {
            float w = 700f, h = 88f;
            float cx = (Screen.width - w) * 0.5f;
            string serverLabel = string.IsNullOrEmpty(_joinCode)
                ? "Server Running"
                : $"Server Running  •  Join code: {_joinCode}";
            GUI.Box(new Rect(cx, 10f, w, h), serverLabel, _announcementStyle);
            return; // server has no local player — skip rest of HUD
        }

        // ── Top-of-screen announcements — stacked in order ────────────────────
        float nextY = 10f;
        const float boxGap = 6f;

        // 1. Phase-specific banner (lobby zone info — always visible in lobby)
        nextY = DrawPhaseTopBanner(nextY);

        // 2. Relay / lobby-created message (temporary, 8 s)
        if (_lobbyMessageEnd > Time.time)
        {
            float w = 700f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, nextY, w, h), _lobbyMessage, _announcementStyle);
            nextY += h + boxGap;
        }

        // 3. Kill feed (temporary, 4 s)
        if (_killMessageEnd > Time.time)
        {
            float w = 700f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, nextY, w, h), _killMessage, _announcementStyle);
        }

        // ── Centre-screen overlays (not stacked — they own the middle) ─────────

        // Countdown (large number, centre screen)
        var gpm = GamePhaseManager.Instance;
        if (gpm != null && gpm.Phase.Value == GamePhaseManager.GamePhase.Countdown)
            DrawCountdownHUD(gpm);

        // Game-over banner (centre screen)
        if (gpm != null && gpm.Phase.Value == GamePhaseManager.GamePhase.GameOver)
            DrawGameOverHUD(gpm);

        // Death countdown (centre screen)
        if (_deathTimerEnd > Time.time)
        {
            int secs = Mathf.CeilToInt(_deathTimerEnd - Time.time);
            float w = 500f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h),
                    $"Respawning in {secs}...", _announcementStyle);
        }

        DrawPlayerHUD();
        DrawFloatingNames();

        // Optional HUD: top-right corner
        if (!_showFps && !_showConnectionStatus && !_showLatency) return;

        _hudLabelStyle.fontSize = Sz(18);
        float hudX  = Screen.width - Sz(220f);
        float hudY  = 10f;
        float lineH = Sz(28f);

        float hudW = Sz(210f);
        if (_showFps)
        {
            GUI.Label(new Rect(hudX, hudY, hudW, lineH),
                      $"FPS: {Mathf.RoundToInt(_fpsDisplay)}", _hudLabelStyle);
            hudY += lineH;
        }
        if (_showConnectionStatus)
        {
            GUI.Label(new Rect(hudX, hudY, hudW, lineH), GetConnectionString(), _hudLabelStyle);
            hudY += lineH;
        }
        if (_showLatency)
        {
            GUI.Label(new Rect(hudX, hudY, hudW, lineH),
                      $"RTT: {GetLatencyString()}", _hudLabelStyle);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Settings Panel
    // ─────────────────────────────────────────────────────────────────────────

    // ── Sz helper — scales a pixel value by _guiScale ─────────────────────────
    private int Sz(float px) => Mathf.RoundToInt(px * _guiScale);

    // ── Binding action descriptors ────────────────────────────────────────────
    private static readonly (string label, string field)[] WasdBindings =
    {
        ("Move Forward",            "Wasd_MoveForward"),
        ("Move Backward",           "Wasd_MoveBack"),
        ("Move Left",               "Wasd_MoveLeft"),
        ("Move Right",              "Wasd_MoveRight"),
        ("Ability 1  (Shoot)",      "Wasd_Ability1"),
        ("Ability 2  (Dash/Shield)","Wasd_Ability2"),
        ("Ability 3  (Triple/Fan)", "Wasd_Ability3"),
    };

    private static readonly (string label, string field)[] PnCBindings =
    {
        ("Stop / Cancel",           "PnC_Stop"),
        ("Ability 1  (Shoot)",      "PnC_Ability1"),
        ("Ability 2  (Dash/Shield)","PnC_Ability2"),
        ("Ability 3  (Triple/Fan)", "PnC_Ability3"),
        ("Attack Move",             "PnC_ForceAA"),
    };

    private static Key GetBoundKey(string field) => field switch
    {
        "Wasd_MoveForward" => GameKeybinds.Wasd_MoveForward,
        "Wasd_MoveBack"    => GameKeybinds.Wasd_MoveBack,
        "Wasd_MoveLeft"    => GameKeybinds.Wasd_MoveLeft,
        "Wasd_MoveRight"   => GameKeybinds.Wasd_MoveRight,
        "Wasd_Ability1"    => GameKeybinds.Wasd_Ability1,
        "Wasd_Ability2"    => GameKeybinds.Wasd_Ability2,
        "Wasd_Ability3"    => GameKeybinds.Wasd_Ability3,
        "PnC_Stop"         => GameKeybinds.PnC_Stop,
        "PnC_Ability1"     => GameKeybinds.PnC_Ability1,
        "PnC_Ability2"     => GameKeybinds.PnC_Ability2,
        "PnC_Ability3"     => GameKeybinds.PnC_Ability3,
        "PnC_ForceAA"      => GameKeybinds.PnC_ForceAA,
        _                  => Key.None
    };

    private static void SetBoundKey(string field, Key key)
    {
        switch (field)
        {
            case "Wasd_MoveForward": GameKeybinds.Wasd_MoveForward = key; break;
            case "Wasd_MoveBack":    GameKeybinds.Wasd_MoveBack    = key; break;
            case "Wasd_MoveLeft":    GameKeybinds.Wasd_MoveLeft    = key; break;
            case "Wasd_MoveRight":   GameKeybinds.Wasd_MoveRight   = key; break;
            case "Wasd_Ability1":    GameKeybinds.Wasd_Ability1    = key; break;
            case "Wasd_Ability2":    GameKeybinds.Wasd_Ability2    = key; break;
            case "Wasd_Ability3":    GameKeybinds.Wasd_Ability3    = key; break;
            case "PnC_Stop":         GameKeybinds.PnC_Stop         = key; break;
            case "PnC_Ability1":     GameKeybinds.PnC_Ability1     = key; break;
            case "PnC_Ability2":     GameKeybinds.PnC_Ability2     = key; break;
            case "PnC_Ability3":     GameKeybinds.PnC_Ability3     = key; break;
            case "PnC_ForceAA":      GameKeybinds.PnC_ForceAA      = key; break;
        }
        GameKeybinds.Save();
    }

    private static readonly Key[] BindableKeys =
    {
        Key.A, Key.B,
        Key.C, Key.D,
        Key.E, Key.F,
        Key.G, Key.H,
        Key.I, Key.J,
        Key.K, Key.L,
        Key.M, Key.N,
        Key.O, Key.P,
        Key.Q, Key.R,
        Key.S, Key.T,
        Key.U, Key.V,
        Key.W, Key.X,
        Key.Y, Key.Z,
        Key.Space,
        Key.LeftShift,  Key.RightShift,
        Key.LeftCtrl,   Key.RightCtrl,
        Key.LeftAlt,    Key.RightAlt,
        Key.Tab,        Key.CapsLock,
        Key.Backspace,
        Key.UpArrow,    Key.DownArrow,
        Key.LeftArrow,  Key.RightArrow,
        Key.F1,         Key.F2,
        Key.F3,         Key.F4,
        Key.F5,         Key.F6,
        Key.Digit1,     Key.Digit2,
        Key.Digit3,     Key.Digit4,
        Key.Digit5,     Key.Digit6,
        Key.Digit7,     Key.Digit8,
        Key.Digit9,     Key.Digit0,
        Key.Numpad0,    Key.Numpad1,
        Key.Numpad2,    Key.Numpad3,
        Key.Numpad4,    Key.Numpad5,
        Key.Numpad6,    Key.Numpad7,
        Key.Numpad8,    Key.Numpad9,
    };

    private void TryCompleteRebind()
    {
        if (Keyboard.current == null || _rebindTarget == null) return;
        foreach (var key in BindableKeys)
        {
            if (Keyboard.current[key].wasPressedThisFrame)
            {
                SetBoundKey(_rebindTarget, key);
                _rebindTarget = null;
                return;
            }
        }
    }

    private void DrawSettingsPanel()
    {
        float sw = Screen.width;
        float sh = Screen.height;

        // Reset mutable styles to base sizes so close button isn't affected by tab mutations
        _buttonStyle.fontSize = Sz(20);

        // Dim overlay
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _dimOverlayTex);

        // Panel
        float panelW = Mathf.Min(sw * 0.9f, Sz(720f));
        float panelH = sh * 0.82f;
        Rect  panelR = new Rect((sw - panelW) * 0.5f, (sh - panelH) * 0.5f, panelW, panelH);
        GUI.DrawTexture(panelR, _whitePanelTex);

        float pad = Sz(22f);
        Rect  innerR = new Rect(panelR.x + pad, panelR.y + pad,
                                 panelR.width - pad * 2f, panelR.height - pad * 2f);
        GUILayout.BeginArea(innerR);

        // Title
        _panelTitleStyle.fontSize = Sz(34);
        GUILayout.Label("Settings", _panelTitleStyle);
        GUILayout.Space(Sz(6f));

        // Tab bar
        float tabH = Sz(38f);
        _segActiveStyle.fontSize   = Sz(17);
        _segInactiveStyle.fontSize = Sz(17);
        GUILayout.BeginHorizontal();
        string[] tabNames = { "General", "Graphics", "Keybinds" };
        for (int i = 0; i < 3; i++)
        {
            if (GUILayout.Button(tabNames[i],
                    i == _settingsTab ? _segActiveStyle : _segInactiveStyle,
                    GUILayout.Height(tabH)))
            {
                if (_settingsTab != i)
                {
                    _settingsTab = i;
                    _settingsScrollPos = Vector2.zero;
                }
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(6f));

        // Scroll view — explicit height so title + tab bar are never displaced
        float titleH   = Sz(34f + 6f);   // label + space below
        float tabRowH  = tabH + Sz(6f);  // tab buttons + space below
        float scrollH  = innerR.height - titleH - tabRowH;
        _settingsScrollPos = GUILayout.BeginScrollView(_settingsScrollPos,
                                                        GUILayout.Height(scrollH));
        switch (_settingsTab)
        {
            case 0: DrawGeneralTab();   break;
            case 1: DrawGraphicsTab();  break;
            case 2: DrawKeybindsTab();  break;
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();

        // Close button — top-right of panel
        bool   fromInGame  = _settingsPreviousState == UIState.InGame;
        string closeLabel  = fromInGame ? "✕" : "←";
        float  cbSize      = Sz(48f);
        if (GUI.Button(new Rect(panelR.xMax - cbSize - 8f, panelR.y + 8f, cbSize, cbSize),
                       closeLabel, _buttonStyle))
            _state = _settingsPreviousState;
    }

    private void DrawGeneralTab()
    {
        float segW = 440f * _guiScale;
        float segH = Sz(44f);
        _panelLabelStyle.fontSize  = Sz(18);
        _segActiveStyle.fontSize   = Sz(20);
        _segInactiveStyle.fontSize = Sz(20);
        _toggleStyle.fontSize      = Sz(18);

        // ── Controller ────────────────────────────────────────────────────
        GUILayout.Label("Controller", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("WASD", _useWasd ? _segActiveStyle : _segInactiveStyle,
                             GUILayout.Width(segW * 0.5f), GUILayout.Height(segH)))
        { _useWasd = true;  GameSettings.UseWasd = true;  _keybindTab = 0; }
        if (GUILayout.Button("Point & Click", !_useWasd ? _segActiveStyle : _segInactiveStyle,
                             GUILayout.Width(segW * 0.5f), GUILayout.Height(segH)))
        { _useWasd = false; GameSettings.UseWasd = false; _keybindTab = 1; }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(14f));

        // ── HUD Overlays — single aligned row ────────────────────────────
        GUILayout.Label("HUD Overlays", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        float overlayW = Sz(180f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _showFps              = GUILayout.Toggle(_showFps,              "Show FPS",         _toggleStyle, GUILayout.Width(overlayW));
        _showConnectionStatus = GUILayout.Toggle(_showConnectionStatus, "Show Connection",   _toggleStyle, GUILayout.Width(overlayW));
        _showLatency          = GUILayout.Toggle(_showLatency,          "Show Latency",      _toggleStyle, GUILayout.Width(overlayW));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // ── Relay join code (host only) ───────────────────────────────────
        if (_useRelay && !string.IsNullOrEmpty(_joinCode) &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            GUILayout.Space(Sz(14f));
            GUILayout.Label("Join Code", _panelLabelStyle);
            GUILayout.Space(Sz(4f));
            bool copied = Time.unscaledTime < _copiedUntil;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(copied ? "Copied!" : _joinCode, _joinCodeStyle))
            {
                GUIUtility.systemCopyBuffer = _joinCode;
                _copiedUntil = Time.unscaledTime + 1.5f;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(Sz(18f));

        // ── Reset All Settings ────────────────────────────────────────────
        _buttonStyle.fontSize = Sz(18);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Reset All Settings to Defaults", _buttonStyle,
                             GUILayout.Width(Sz(300f)), GUILayout.Height(Sz(44f))))
        {
            _guiScale     = 1f;
            _bottomBarScale = 1f;
            _cursorScale  = 1f;
            _qualityIndex = 2;
            _targetFps    = 60f;
            _showFps      = false;
            _showConnectionStatus = false;
            _showLatency  = false;
            GameSettings.BottomBarScale = 1f;
            GameSettings.CursorScale    = 1f;
            Application.targetFrameRate = 60;
            ApplyRenderScale(_qualityIndex);
            GameKeybinds.ResetToDefaults();
            GameKeybinds.Save();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(8f));

        // ── Disconnect / Return ───────────────────────────────────────────
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsClient || nm.IsServer))
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Disconnect", _buttonStyle,
                                 GUILayout.Width(Sz(280f)), GUILayout.Height(Sz(44f))))
            {
                nm.Shutdown();
                _relayBusy  = false;
                _joinCode   = "";
                _relayError = "";
                _state      = UIState.MainMenu;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        else if (_settingsPreviousState == UIState.MainMenu)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Return to Menu", _buttonStyle,
                                 GUILayout.Width(Sz(280f)), GUILayout.Height(Sz(44f))))
                _state = UIState.MainMenu;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(Sz(8f));
    }

    private void DrawGraphicsTab()
    {
        float segW = 460f * _guiScale;
        float segH = Sz(44f);
        _panelLabelStyle.fontSize  = Sz(18);
        _segActiveStyle.fontSize   = Sz(20);
        _segInactiveStyle.fontSize = Sz(20);

        GUILayout.Label("Graphics Quality", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        string[] qualityLabels = { "Low", "Medium", "High", "Ultra" };
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        for (int i = 0; i < 4; i++)
        {
            if (GUILayout.Button(qualityLabels[i],
                                 i == _qualityIndex ? _segActiveStyle : _segInactiveStyle,
                                 GUILayout.Width(segW / 4f), GUILayout.Height(segH)))
            {
                _qualityIndex = i;
                ApplyRenderScale(_qualityIndex);
            }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(6f));
        _panelLabelStyle.fontStyle = FontStyle.Normal;
        _panelLabelStyle.fontSize  = Sz(15);
        GUILayout.Label($"Render Scale: {RenderScales[_qualityIndex]:0.00}×", _panelLabelStyle);
        _panelLabelStyle.fontStyle = FontStyle.Bold;
        _panelLabelStyle.fontSize  = Sz(18);

        GUILayout.Space(Sz(14f));

        // ── Frame Rate Cap ────────────────────────────────────────────────
        GUILayout.Label("Frame Rate Cap", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        bool isUncapped = _targetFps >= 241f;
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(isUncapped ? "Uncapped" : $"{Mathf.RoundToInt(_targetFps)} FPS",
                        _panelLabelStyle, GUILayout.Width(Sz(110f)));
        float newFps = GUILayout.HorizontalSlider(_targetFps, 30f, 241f,
                                                   GUILayout.Width(segW - Sz(120f)),
                                                   GUILayout.Height(Sz(32f)));
        newFps = Mathf.Round(newFps);
        if (!Mathf.Approximately(newFps, _targetFps))
        {
            _targetFps = newFps;
            Application.targetFrameRate = _targetFps >= 241f ? -1 : (int)_targetFps;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(14f));

        // ── UI Scale (master) ─────────────────────────────────────────────
        GUILayout.Label("UI Scale", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{_guiScale:0.0}×", _panelLabelStyle, GUILayout.Width(Sz(60f)));
        float newScale = GUILayout.HorizontalSlider(_guiScale, 0.5f, 3f,
                                                    GUILayout.Width(segW - Sz(70f)),
                                                    GUILayout.Height(Sz(32f)));
        newScale = Mathf.Round(newScale * 10f) / 10f;
        if (!Mathf.Approximately(newScale, _guiScale))
            _guiScale = newScale;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(14f));

        // ── Cursor Scale ──────────────────────────────────────────────────
        GUILayout.Label("Cursor Scale", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{_cursorScale:0.0}×", _panelLabelStyle, GUILayout.Width(Sz(60f)));
        float newCursor = GUILayout.HorizontalSlider(_cursorScale, 0.5f, 2f,
                                                      GUILayout.Width(segW - Sz(70f)),
                                                      GUILayout.Height(Sz(32f)));
        newCursor = Mathf.Round(newCursor * 10f) / 10f;
        if (!Mathf.Approximately(newCursor, _cursorScale))
        {
            _cursorScale = newCursor;
            GameSettings.CursorScale = _cursorScale;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(14f));

        // ── Bottom Bar Scale ──────────────────────────────────────────────
        GUILayout.Label("Bottom Bar Scale", _panelLabelStyle);
        GUILayout.Space(Sz(4f));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{_bottomBarScale:0.0}×", _panelLabelStyle, GUILayout.Width(Sz(60f)));
        float newBar = GUILayout.HorizontalSlider(_bottomBarScale, 0.5f, 2f,
                                                   GUILayout.Width(segW - Sz(70f)),
                                                   GUILayout.Height(Sz(32f)));
        newBar = Mathf.Round(newBar * 10f) / 10f;
        if (!Mathf.Approximately(newBar, _bottomBarScale))
        {
            _bottomBarScale = newBar;
            GameSettings.BottomBarScale = _bottomBarScale;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(8f));
    }

    private void DrawKeybindsTab()
    {
        float segH   = Sz(38f);
        float labelW = Sz(230f);
        float btnW   = Sz(120f);
        _panelLabelStyle.fontSize  = Sz(17);
        _panelTitleStyle.fontSize  = Sz(20);
        _segActiveStyle.fontSize   = Sz(16);
        _segInactiveStyle.fontSize = Sz(16);
        _buttonStyle.fontSize      = Sz(17);

        // Sub-tab row
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("WASD Mode",
                _keybindTab == 0 ? _segActiveStyle : _segInactiveStyle,
                GUILayout.Width(Sz(160f)), GUILayout.Height(segH)))
        { _keybindTab = 0; _useWasd = true;  GameSettings.UseWasd = true; }
        if (GUILayout.Button("Point & Click",
                _keybindTab == 1 ? _segActiveStyle : _segInactiveStyle,
                GUILayout.Width(Sz(160f)), GUILayout.Height(segH)))
        { _keybindTab = 1; _useWasd = false; GameSettings.UseWasd = false; }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(10f));

        // Schema header
        GUILayout.Label(_keybindTab == 0 ? "WASD Mode Keybinds" : "Point & Click Keybinds",
                        _panelTitleStyle);
        GUILayout.Space(Sz(8f));

        // Binding rows
        var bindings = _keybindTab == 0 ? WasdBindings : PnCBindings;
        var prevAlign = _panelLabelStyle.alignment;
        foreach (var (label, field) in bindings)
        {
            Key  currentKey  = GetBoundKey(field);
            bool isRebinding = _rebindTarget == field;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            _panelLabelStyle.alignment = TextAnchor.MiddleLeft;
            GUILayout.Label(label, _panelLabelStyle, GUILayout.Width(labelW), GUILayout.Height(segH));
            _panelLabelStyle.alignment = prevAlign;

            GUIStyle btnStyle = isRebinding ? _segActiveStyle : _segInactiveStyle;
            string btnLabel   = isRebinding ? "Press a key..." : GameKeybinds.KeyName(currentKey);
            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Width(btnW), GUILayout.Height(segH)))
                _rebindTarget = field;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(Sz(3f));
        }

        GUILayout.Space(Sz(10f));

        // Info label
        var savedFontSize = _panelLabelStyle.fontSize;
        var savedColor    = _panelLabelStyle.normal.textColor;
        _panelLabelStyle.fontSize            = Sz(14);
        _panelLabelStyle.normal.textColor    = new Color(0.5f, 0.5f, 0.5f);
        GUILayout.Label("Left Click / Right Click — not rebindable", _panelLabelStyle);
        _panelLabelStyle.fontSize            = savedFontSize;
        _panelLabelStyle.normal.textColor    = savedColor;

        GUILayout.Space(Sz(12f));

        // Reset keybinds button
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Reset Keybinds to Defaults", _buttonStyle,
                             GUILayout.Width(Sz(280f)), GUILayout.Height(Sz(44f))))
        {
            GameKeybinds.ResetToDefaults();
            GameKeybinds.Save();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(Sz(8f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Player HUD
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawFloatingNames()
    {
        var cam = Camera.main;
        var nm  = NetworkManager.Singleton;
        if (cam == null || nm == null) return;

        _levelStyle.fontSize  = Mathf.RoundToInt(13f * _guiScale);
        _levelStyle.alignment = TextAnchor.MiddleCenter;

        foreach (var client in nm.ConnectedClientsList)
        {
            var pc = client.PlayerObject?.GetComponent<PlayerController>();
            if (pc == null) continue;

            string name = pc.PlayerName.Value.Length > 0 ? pc.PlayerName.Value.ToString() : "Player";

            // Project at the same anchor PlayerHealth uses (WorldHeadOffset = 2.3f, pivot bottom-center)
            Vector3 screenPos = cam.WorldToScreenPoint(pc.transform.position + Vector3.up * 2.3f);
            if (screenPos.z < 0f) continue;

            // DrawInGameHUD has no GUI.matrix — raw screen pixel space.
            // PlayerHealth bar: pivot=bottom-center, BarH=18px, anchored at screenPos.y (Unity Y-up).
            const float playerBarH = 18f;
            float gx       = screenPos.x;
            float barTopY  = (Screen.height - screenPos.y) - playerBarH; // IMGUI Y of bar top

            float labelW = 120f;
            float nameH  = 16f;
            GUI.Label(new Rect(gx - labelW * 0.5f, barTopY - nameH - 2f, labelW, nameH), name, _levelStyle);
        }

        _levelStyle.alignment = TextAnchor.UpperCenter;
    }

    private void DrawPlayerHUD()
    {
        var nm       = NetworkManager.Singleton;
        var localObj = nm?.LocalClient?.PlayerObject;
        if (localObj == null) return;

        var health  = localObj.GetComponent<PlayerHealth>();
        var shooter = localObj.GetComponent<ProjectileShooter>();
        var pc      = localObj.GetComponent<PlayerController>();
        var mana    = localObj.GetComponent<PlayerMana>();
        var xp      = localObj.GetComponent<PlayerXP>();

        int  charIdx = pc?.CharacterIndex.Value ?? 0;
        bool isTank  = charIdx == 0;

        var shield     = isTank ? localObj.GetComponent<ShieldAbility>()     : null;
        var fanShot    = isTank ? localObj.GetComponent<FanShotAbility>()    : null;
        var dash       = isTank ? null : localObj.GetComponent<DashAbility>();
        var tripleShot = isTank ? null : localObj.GetComponent<TripleShotAbility>();

        Texture2D iconQ = isTank ? _iconTankQ : _iconRangerQ;
        Texture2D iconW = isTank ? _iconTankW : _iconRangerW;
        Texture2D iconE = isTank ? _iconTankE : _iconRangerE;

        // ── Layout constants (scaled) ─────────────────────────────────────
        float s            = _guiScale * GameSettings.BottomBarScale;
        float padBottom    = 20f  * s;
        float circleD      = 90f  * s;
        float xpThickness  = 7f   * s;
        float abilitySize  = 68f  * s;
        float abilityGap   = 6f   * s;
        float barH         = 14f  * s;
        float barGap       = 5f   * s;
        float circleColGap = 8f   * s;

        float colH   = abilitySize + barGap + barH + barGap + barH;  // 106
        float colW   = abilitySize * 3f + abilityGap * 2f;            // 3 abilities
        float totalW = circleD + circleColGap + colW;

        float sw        = Screen.width;
        float sh        = Screen.height;
        float hudLeft   = (sw - totalW) * 0.5f;
        float colTop    = sh - padBottom - colH;
        float circleTop = colTop + (colH - circleD) * 0.5f;
        float circleR   = circleD * 0.5f;
        var   circleC   = new Vector2(hudLeft + circleR, circleTop + circleR);
        float abX       = hudLeft + circleD + circleColGap;

        // ── Player name above circle ──────────────────────────────────────
        var localPc = nm.LocalClient?.PlayerObject?.GetComponent<PlayerController>();
        string playerName = (localPc != null && localPc.PlayerName.Value.Length > 0)
            ? localPc.PlayerName.Value.ToString()
            : _playerName;
        float  nameW = 100f * s, nameH = 20f * s;
        _levelStyle.fontSize = Mathf.RoundToInt(16f * s);
        GUI.Label(new Rect(circleC.x - nameW * 0.5f, circleTop - nameH - 2f, nameW, nameH),
                  playerName, _levelStyle);

        // ── Profile picture (empty circle) ───────────────────────────────
        float innerR   = circleR - xpThickness - 2f;
        int   teamIdx   = localPc?.TeamIndex.Value ?? -1;
        Color teamColor = teamIdx == 0 ? new Color(0.2f, 0.5f,  1f,   0.9f)
                        : teamIdx == 1 ? new Color(1f,   0.25f, 0.25f, 0.9f)
                        :                new Color(1f,   1f,    1f,    0.9f);
        GUI.color = teamColor;
        GUI.DrawTexture(new Rect(circleC.x - innerR, circleC.y - innerR, innerR * 2f, innerR * 2f),
                        _circleTex);
        GUI.color = Color.white;

        float xpRingR = circleR - xpThickness * 0.5f;
        // Background ring (full 270°, dark)
        DrawArc(circleC, xpRingR, xpThickness, 135f, 270f, 1f, new Color(0.15f, 0.15f, 0.15f, 0.9f));
        // Foreground ring (real XP fraction)
        DrawArc(circleC, xpRingR, xpThickness, 135f, 270f, xp?.XPFraction ?? 0f, new Color(1f, 0.78f, 0.08f, 1f));

        // Level number below the circle
        float lvlW = 40f * s, lvlH = 20f * s;
        _levelStyle.fontSize = Mathf.RoundToInt(14f * s);
        GUI.Label(new Rect(circleC.x - lvlW * 0.5f, circleTop + circleD - 2f, lvlW, lvlH),
                  (xp?.Level.Value ?? 1).ToString(), _levelStyle);

        // ── Slot 1 — Q (projectile) ──────────────────────────────────────
        string keybind1 = GameKeybinds.KeyName(GameSettings.UseWasd ? GameKeybinds.Wasd_Ability1 : GameKeybinds.PnC_Ability1);
        DrawAbilitySlot(abX, colTop, abilitySize, s,
            iconQ,
            shooter?.CastFraction      ?? 0f,
            shooter?.CooldownFraction  ?? 0f,
            shooter?.CooldownRemaining ?? 0f,
            false,
            shooter?.ManaCost          ?? 0f,
            keybind1);

        // ── Slot 2 — W (dash / shield) ───────────────────────────────────
        float ab2X    = abX + abilitySize + abilityGap;
        string keybind2 = GameKeybinds.KeyName(GameSettings.UseWasd ? GameKeybinds.Wasd_Ability2 : GameKeybinds.PnC_Ability2);
        if (isTank)
        {
            DrawAbilitySlot(ab2X, colTop, abilitySize, s,
                iconW,
                shield?.CastFraction      ?? 0f,
                shield?.CooldownFraction  ?? 0f,
                shield?.CooldownRemaining ?? 0f,
                shield?.IsAiming ?? false,
                shield?.ManaCost ?? 0f,
                keybind2);
        }
        else
        {
            DrawAbilitySlot(ab2X, colTop, abilitySize, s,
                iconW,
                dash?.CastFraction      ?? 0f,
                dash?.CooldownFraction  ?? 0f,
                dash?.CooldownRemaining ?? 0f,
                dash?.IsAiming ?? false,
                dash?.ManaCost ?? 0f,
                keybind2);
        }

        // ── Slot 3 — E (fan shot / triple shot) ──────────────────────────
        float ab3X    = abX + (abilitySize + abilityGap) * 2f;
        string keybind3 = GameKeybinds.KeyName(GameSettings.UseWasd ? GameKeybinds.Wasd_Ability3 : GameKeybinds.PnC_Ability3);
        if (isTank)
        {
            DrawAbilitySlot(ab3X, colTop, abilitySize, s,
                iconE,
                fanShot?.CastFraction      ?? 0f,
                fanShot?.CooldownFraction  ?? 0f,
                fanShot?.CooldownRemaining ?? 0f,
                fanShot?.IsCharging ?? false,
                fanShot?.ManaCost   ?? 0f,
                keybind3);
        }
        else
        {
            DrawAbilitySlot(ab3X, colTop, abilitySize, s,
                iconE,
                tripleShot?.CastFraction      ?? 0f,
                tripleShot?.CooldownFraction  ?? 0f,
                tripleShot?.CooldownRemaining ?? 0f,
                tripleShot?.IsCharging ?? false,
                tripleShot?.ManaCost   ?? 0f,
                keybind3);
        }

        // ── Slot hover tooltip ────────────────────────────────────────────
        Vector2 mouse = Event.current.mousePosition;
        int hoveredSlot = -1;
        if (new Rect(abX,  colTop, abilitySize, abilitySize).Contains(mouse)) hoveredSlot = 0;
        if (new Rect(ab2X, colTop, abilitySize, abilitySize).Contains(mouse)) hoveredSlot = 1;
        if (new Rect(ab3X, colTop, abilitySize, abilitySize).Contains(mouse)) hoveredSlot = 2;

        if (hoveredSlot >= 0)
        {
            var tooltips = isTank ? TankTooltips : RangerTooltips;
            float[] slotCenters = { abX + abilitySize * 0.5f, ab2X + abilitySize * 0.5f, ab3X + abilitySize * 0.5f };
            float[] manaCosts   = {
                shooter?.ManaCost ?? 0f,
                isTank ? shield?.ManaCost ?? 0f : dash?.ManaCost ?? 0f,
                isTank ? fanShot?.ManaCost ?? 0f : tripleShot?.ManaCost ?? 0f,
            };
            string[] keybinds = { keybind1, keybind2, keybind3 };
            DrawAbilityTooltip(slotCenters[hoveredSlot], colTop, s,
                tooltips[hoveredSlot], keybinds[hoveredSlot], manaCosts[hoveredSlot]);
        }

        // ── Health bar ────────────────────────────────────────────────────
        float barY       = colTop + abilitySize + barGap;
        float curHp      = health?.CurrentHealth ?? 0f;
        float shieldVal  = health?.ShieldHP.Value ?? 0f;
        float maxHp      = health?.MaxHealth ?? 100f;
        bool  atFull     = curHp >= maxHp;
        float effMax     = (atFull && shieldVal > 0f) ? (maxHp + shieldVal) : maxHp;
        float greenFrac  = effMax > 0f ? Mathf.Clamp01(curHp / effMax) : 0f;
        float shieldFrac = effMax > 0f ? Mathf.Clamp01(shieldVal / effMax) : 0f;
        float afterGrey  = Mathf.Min(1f, greenFrac + shieldFrac);
        DrawRect(abX, barY, colW, barH, new Color(0.08f, 0.08f, 0.08f, 0.9f));                                               // background
        if (afterGrey < 1f)  DrawRect(abX + colW * afterGrey, barY, colW * (1f - afterGrey), barH, new Color(0.72f, 0.14f, 0.14f, 1f)); // red (missing)
        if (shieldFrac > 0f) DrawRect(abX + colW * greenFrac,  barY, colW * shieldFrac,       barH, new Color(0.62f, 0.62f, 0.62f, 1f)); // grey (shield)
        if (greenFrac > 0f)  DrawRect(abX,                     barY, colW * greenFrac,        barH, new Color(0.2f, 0.78f, 0.2f, 1f));   // green (health)

        // Health numeric label — drawn over the bar
        _levelStyle.fontSize  = Mathf.RoundToInt(11f * s);
        _levelStyle.alignment = TextAnchor.MiddleCenter;
        int    hpTotal = Mathf.CeilToInt(curHp + shieldVal);
        string hpLabel = $"{hpTotal} / {Mathf.CeilToInt(effMax)}";
        GUI.Label(new Rect(abX, barY, colW, barH), hpLabel, _levelStyle);

        // ── Mana bar ──────────────────────────────────────────────────────
        float manaY    = barY + barH + barGap;
        float manaFrac = mana?.ManaFraction ?? 1f;
        DrawRect(abX, manaY, colW, barH, new Color(0.08f, 0.08f, 0.08f, 0.9f));
        if (manaFrac > 0f) DrawRect(abX, manaY, colW * manaFrac, barH, new Color(0.12f, 0.32f, 0.82f, 1f));

        // Mana numeric label — drawn over the bar
        string manaLabel = $"{Mathf.CeilToInt(mana?.Mana.Value ?? 0)} / {Mathf.CeilToInt(mana?.MaxMana.Value ?? 0)}";
        GUI.Label(new Rect(abX, manaY, colW, barH), manaLabel, _levelStyle);
        _levelStyle.alignment = TextAnchor.UpperCenter;
    }

    // ── Draw helpers ──────────────────────────────────────────────────────────

    private void DrawAbilitySlot(float x, float y, float size, float s,
        Texture2D icon, float castFrac, float cdFrac, float cdRemaining,
        bool isCharging, float manaCost, string keybind)
    {
        // 1. Dark background
        DrawRect(x, y, size, size, new Color(0.12f, 0.12f, 0.12f, 0.92f));

        // 2. Icon
        if (icon != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x, y, size, size), icon, ScaleMode.ScaleToFit);
        }

        // 3. Overlays
        if (castFrac > 0.01f)
        {
            // Cast: white fill growing upward from bottom
            float fillH = castFrac * size;
            DrawRect(x, y + size - fillH, size, fillH, new Color(1f, 1f, 1f, 0.35f));
        }
        else if (isCharging)
        {
            // Charging / active: faint white pulse over entire slot
            DrawRect(x, y, size, size, new Color(1f, 1f, 1f, 0.2f));
        }
        else if (cdFrac > 0.01f)
        {
            // Cooldown: dark overlay anchored at bottom, receding downward (icon reveals top-first)
            float blackH = cdFrac * size;
            DrawRect(x, y + size - blackH, size, blackH, new Color(0f, 0f, 0f, 0.55f));
            _levelStyle.fontSize  = Mathf.RoundToInt(16f * s);
            _levelStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y, size, size), cdRemaining.ToString("0.0"), _levelStyle);
            _levelStyle.alignment = TextAnchor.UpperCenter;
        }

        // 4. Mana cost — top-right (hidden when 0)
        if (manaCost > 0f)
        {
            _levelStyle.fontSize  = Mathf.RoundToInt(10f * s);
            _levelStyle.alignment = TextAnchor.UpperRight;
            string manaStr = Mathf.RoundToInt(manaCost).ToString();
            float  manaW   = size - 3f * s;
            DrawLabelWithOutline(new Rect(x, y, manaW, manaW), manaStr, 1f);
            _levelStyle.alignment = TextAnchor.UpperCenter;
        }

        // 5. Keybind — bottom-left
        _levelStyle.fontSize  = Mathf.RoundToInt(11f * s);
        _levelStyle.alignment = TextAnchor.LowerLeft;
        DrawLabelWithOutline(new Rect(x + 3f * s, y, size - 3f * s, size - 3f * s), keybind, 1f);
        _levelStyle.alignment = TextAnchor.UpperCenter;
    }

    private void DrawAbilityTooltip(float slotCenterX, float slotTopY, float s,
        AbilityTooltipData data, string keybind, float manaCost)
    {
        float pad      = 8f  * s;
        float panelW   = 200f * s;
        float nameH    = 16f * s;
        float metaH    = 14f * s;
        float statH    = !string.IsNullOrEmpty(data.Stat) ? 14f * s : 0f;
        float descH    = 36f * s;   // word-wrapped, ~3 lines
        float panelH   = pad * 2f + nameH + 4f * s + metaH + (statH > 0 ? 4f * s + statH : 0f) + 4f * s + descH;

        float panelX   = Mathf.Clamp(slotCenterX - panelW * 0.5f, 4f, Screen.width - panelW - 4f);
        float panelY   = slotTopY - panelH - 6f * s;

        // Background
        DrawRect(panelX, panelY, panelW, panelH, new Color(0.08f, 0.08f, 0.08f, 0.93f));
        // Top accent line
        DrawRect(panelX, panelY, panelW, 2f * s, new Color(0.4f, 0.6f, 1f, 0.8f));

        float cx = panelX + pad;
        float cw = panelW - pad * 2f;
        float cy = panelY + pad;

        // Ability name
        var nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = Mathf.RoundToInt(14f * s),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            normal    = { textColor = Color.white },
        };
        GUI.Label(new Rect(cx, cy, cw, nameH), data.Name, nameStyle);
        cy += nameH + 4f * s;

        // Keybind + mana
        string metaStr = $"[{keybind}]" + (manaCost > 0f ? $"  ·  {Mathf.RoundToInt(manaCost)} Mana" : "");
        var metaStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = Mathf.RoundToInt(11f * s),
            alignment = TextAnchor.UpperLeft,
            normal    = { textColor = new Color(0.6f, 0.6f, 0.6f) },
        };
        GUI.Label(new Rect(cx, cy, cw, metaH), metaStr, metaStyle);
        cy += metaH;

        // Stat line (hidden if empty)
        if (!string.IsNullOrEmpty(data.Stat))
        {
            cy += 4f * s;
            var statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = Mathf.RoundToInt(11f * s),
                alignment = TextAnchor.UpperLeft,
                normal    = { textColor = new Color(1f, 0.85f, 0.3f) },
            };
            GUI.Label(new Rect(cx, cy, cw, statH), data.Stat, statStyle);
            cy += statH;
        }

        // Description
        cy += 4f * s;
        _tooltipStyle.fontSize = Mathf.RoundToInt(11f * s);
        GUI.Label(new Rect(cx, cy, cw, descH), data.Description, _tooltipStyle);
    }

    private void DrawLabelWithOutline(Rect r, string text, float px)
    {
        Color fg = _levelStyle.normal.textColor;
        var shadow = new Color(0f, 0f, 0f, 0.85f);
        _levelStyle.normal.textColor = shadow;
        _levelStyle.hover.textColor  = shadow;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            GUI.Label(new Rect(r.x + dx * px, r.y + dy * px, r.width, r.height), text, _levelStyle);
        }
        _levelStyle.normal.textColor = fg;
        _levelStyle.hover.textColor  = fg;
        GUI.Label(r, text, _levelStyle);
    }

    private void DrawRect(float x, float y, float w, float h, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, h), _whitePanelTex);
        GUI.color = Color.white;
    }

    private void DrawArc(Vector2 center, float radius, float thickness,
                         float startDeg, float spanDeg, float progress, Color color)
    {
        GUI.color  = color;
        int total  = Mathf.Max(1, Mathf.RoundToInt(spanDeg / 360f * 96f));
        int filled = Mathf.RoundToInt(total * Mathf.Clamp01(progress));
        float halfT = thickness * 0.5f;
        for (int i = 0; i < filled; i++)
        {
            float angle = (startDeg + (float)i / total * spanDeg) * Mathf.Deg2Rad;
            GUI.DrawTexture(
                new Rect(center.x + radius * Mathf.Cos(angle) - halfT,
                         center.y + radius * Mathf.Sin(angle) - halfT,
                         thickness, thickness),
                _whitePanelTex);
        }
        GUI.color = Color.white;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Relay async helpers
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_WEBGL
    private const string RelayConnectionType = "wss";   // browsers require WebSocket
#else
    private const string RelayConnectionType = "dtls";  // secure UDP for Editor/Desktop/Server
#endif

    private void ConfigureTransportForRelay(UnityTransport transport)
    {
        transport.UseWebSockets = RelayConnectionType == "wss";
    }

    private async Task InitializeUgsAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private async Task StartHostWithRelayAsync()
    {
        _relayBusy = true;
        _relayError = "";
        _joinCode = "";
        try
        {
            await InitializeUgsAsync();
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections: 3);
            var hostTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            ConfigureTransportForRelay(hostTransport);
            hostTransport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayConnectionType));
            _joinCode        = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _lobbyMessage    = $"Lobby created  •  Join code: {_joinCode}";
            _lobbyMessageEnd = Time.unscaledTime + 8f;
            NetworkManager.Singleton.StartHost();
            // Update() detects nm.IsHost and transitions to InGame
        }
        catch (System.Exception e)
        {
            _relayError = $"Relay error: {e.Message}";
            Debug.LogException(e);
            _relayBusy = false;
        }
    }

    private async Task StartServerWithRelayAsync()
    {
        _relayBusy  = true;
        _relayError = "";
        _joinCode   = "";
        try
        {
            await InitializeUgsAsync();
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections: 3);
            var serverTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            ConfigureTransportForRelay(serverTransport);
            serverTransport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayConnectionType));
            _joinCode        = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _lobbyMessage    = $"Server started  •  Join code: {_joinCode}";
            _lobbyMessageEnd = Time.unscaledTime + 8f;
            NetworkManager.Singleton.StartServer();
            // Update() detects nm.IsServer and transitions to InGame
        }
        catch (System.Exception e)
        {
            _relayError = $"Relay error: {e.Message}";
            Debug.LogException(e);
            _relayBusy = false;
        }
    }

    private async Task StartClientWithRelayAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode)) { _relayError = "Enter a join code."; return; }
        _relayBusy = true;
        _relayError = "";
        try
        {
            await InitializeUgsAsync();
            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
            var clientTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            ConfigureTransportForRelay(clientTransport);
            clientTransport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayConnectionType));
            NetworkManager.Singleton.StartClient();
        }
        catch (System.Exception e)
        {
            _relayError = $"Join error: {e.Message}";
            Debug.LogException(e);
            _relayBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly float[] RenderScales = { 0.5f, 0.75f, 1.0f, 1.25f };
    // Labels: Low | Medium | High | Ultra

    private static void ApplyRenderScale(int qualityIndex)
    {
        var urpAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null)
            urpAsset.renderScale = RenderScales[qualityIndex];
    }

    private void SetTransport(string ip, ushort port)
    {
        var t = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (t != null) t.SetConnectionData(ip, port);
    }

    private string GetConnectionString()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return "Disconnected";
        if (nm.IsHost) return "Host";
        if (nm.IsServer) return "Server";
        if (nm.IsClient) return $"Client ({_hostIp})";
        return "Connecting...";
    }

    private string GetLatencyString()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsClient && !nm.IsServer)) return "N/A";
        if (nm.IsHost) return "Host";
        var transport = nm.GetComponent<UnityTransport>();
        if (transport == null) return "N/A";
        ulong rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
        return $"{rtt} ms";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Style + Texture utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeCircleTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        float r2 = r * r;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r + 0.5f, dy = y - r + 0.5f;
            tex.SetPixel(x, y, dx * dx + dy * dy <= r2 ? Color.white : Color.clear);
        }
        tex.Apply();
        return tex;
    }

    private void EnsureStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 80,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        _subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };

        _panelTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 40,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.black },
            hover     = { textColor = Color.black },
            active    = { textColor = Color.black }
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18
        };

        _hudLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 18,
            alignment = TextAnchor.MiddleRight,
            normal    = { textColor = new Color(0.65f, 0.65f, 0.65f) }
        };

        _toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 25,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.black },
            active = { textColor = Color.black },
            hover = { textColor = Color.black },
            focused = { textColor = Color.black },
            onNormal = { textColor = Color.black },
            onActive = { textColor = Color.black },
            onHover = { textColor = Color.black },
            onFocused = { textColor = Color.black }
        };

        _smallToggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white },
            active = { textColor = Color.white },
            hover = { textColor = Color.white },
            focused = { textColor = Color.white },
            onNormal = { textColor = Color.white },
            onActive = { textColor = Color.white },
            onHover = { textColor = Color.white },
            onFocused = { textColor = Color.white }
        };

        _joinCodeStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 40,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.black },
            hover     = { textColor = Color.black },
            active    = { textColor = Color.black }
        };

        _smallButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 14,
            fontStyle = FontStyle.Normal
        };

        _announcementStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };

        _levelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter,
            normal    = { textColor = Color.white }
        };

        var sectionColor = new Color(0.25f, 0.25f, 0.25f);
        _panelLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = sectionColor },
            hover = { textColor = sectionColor },
            active = { textColor = sectionColor }
        };

        _segActiveStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 25,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(0, 0, 0, 0),
            normal = { background = _segActiveTex, textColor = Color.white },
            hover = { background = _segActiveTex, textColor = Color.white },
            active = { background = _segActiveTex, textColor = Color.white },
            onNormal = { background = _segActiveTex, textColor = Color.white }
        };

        _segInactiveStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 25,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(0, 0, 0, 0),
            normal = { background = _segInactiveTex, textColor = new Color(0.2f, 0.2f, 0.2f) },
            hover = { background = _segInactiveTex, textColor = Color.black },
            active = { background = _segInactiveTex, textColor = Color.black }
        };

        _textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 18
        };

        _tooltipStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap  = true,
            alignment = TextAnchor.UpperLeft,
            normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };
    }
}
