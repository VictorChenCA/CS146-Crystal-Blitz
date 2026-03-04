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
    private int _qualityIndex = 1;       // 0=Low 1=Med 2=High
    private float _targetFps = 60f;     // 241 = uncapped

    // ── Textures (created in Awake, destroyed in OnDestroy) ──────────────────
    private Texture2D _dimOverlayTex;
    private Texture2D _whitePanelTex;
    private Texture2D _segActiveTex;
    private Texture2D _segInactiveTex;

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
    private GUIStyle _segActiveStyle;
    private GUIStyle _segInactiveStyle;
    private GUIStyle _panelLabelStyle;
    private GUIStyle _textFieldStyle;
    private bool _stylesInitialized;

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _dimOverlayTex = MakeTex(new Color(0f, 0f, 0f, 0.6f));
        _whitePanelTex = MakeTex(new Color(1f, 1f, 1f, 0.95f));
        _segActiveTex = MakeTex(new Color(0.18f, 0.38f, 0.72f, 1f));
        _segInactiveTex = MakeTex(new Color(0.82f, 0.82f, 0.82f, 1f));

        ApplyRenderScale(_qualityIndex);
        Application.targetFrameRate = (int)_targetFps;
    }

    private void OnDestroy()
    {
        Destroy(_dimOverlayTex);
        Destroy(_whitePanelTex);
        Destroy(_segActiveTex);
        Destroy(_segInactiveTex);
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
        switch (_state)
        {
            case UIState.MainMenu: DrawMainMenu(); break;
            case UIState.ConnectionScreen: DrawConnectionScreen(); break;
            case UIState.InGame: DrawInGameHUD(); break;
            case UIState.Settings: DrawInGameHUD(); DrawSettingsPanel(); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Main Menu
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawMainMenu()
    {
        float sw = Screen.width;
        float sh = Screen.height;

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

        if (GUI.Button(new Rect(btnX, btn1Y, btnW, btnH), "Play", _buttonStyle))
            _state = UIState.ConnectionScreen;

        if (GUI.Button(new Rect(btnX, btn1Y + (btnH + btnGap), btnW, btnH), "Settings", _buttonStyle))
        {
            _settingsPreviousState = UIState.MainMenu;
            _state = UIState.Settings;
        }

        if (GUI.Button(new Rect(btnX, btn1Y + 2f * (btnH + btnGap), btnW, btnH), "Quit", _buttonStyle))
            Application.Quit();

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
        float panelY = (logH - panelH) * 0.5f;

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

        // Lobby created announcement (top-centre, 8 s)
        if (_lobbyMessageEnd > Time.time)
        {
            float w = 700f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, 10f, w, h), _lobbyMessage, _announcementStyle);
        }

        // Kill announcement (top-centre, 4 s)
        if (_killMessageEnd > Time.time)
        {
            float w = 700f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, 106f, w, h), _killMessage, _announcementStyle);
        }

        // Death countdown (centre screen)
        if (_deathTimerEnd > Time.time)
        {
            int secs = Mathf.CeilToInt(_deathTimerEnd - Time.time);
            float w = 500f, h = 88f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h),
                    $"Respawning in {secs}...", _announcementStyle);
        }

        // Optional HUD: top-right corner
        if (!_showFps && !_showConnectionStatus && !_showLatency) return;

        float hudX = Screen.width - 220f;
        float hudY = 10f;
        float lineH = 28f;

        if (_showFps)
        {
            GUI.Label(new Rect(hudX, hudY, 210f, lineH),
                      $"FPS: {Mathf.RoundToInt(_fpsDisplay)}", _hudLabelStyle);
            hudY += lineH;
        }
        if (_showConnectionStatus)
        {
            GUI.Label(new Rect(hudX, hudY, 210f, lineH), GetConnectionString(), _hudLabelStyle);
            hudY += lineH;
        }
        if (_showLatency)
        {
            GUI.Label(new Rect(hudX, hudY, 210f, lineH),
                      $"RTT: {GetLatencyString()}", _hudLabelStyle);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw: Settings Panel
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawSettingsPanel()
    {
        float sw = Screen.width;
        float sh = Screen.height;

        // Dim overlay
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _dimOverlayTex);

        // Panel
        float panelW = sw * 0.5f;
        float panelH = sh * 0.72f;
        Rect panelR = new Rect((sw - panelW) * 0.5f, (sh - panelH) * 0.5f, panelW, panelH);
        GUI.DrawTexture(panelR, _whitePanelTex);

        // Inner area with padding
        float pad = 24f;
        Rect innerR = new Rect(panelR.x + pad, panelR.y + pad,
                                panelR.width - pad * 2f, panelR.height - pad * 2f);
        GUILayout.BeginArea(innerR);

        GUILayout.Label("Settings", _panelTitleStyle);
        GUILayout.Space(10f);

        // ── Controller type ───────────────────────────────────────────────
        GUILayout.Label("Controller", _panelLabelStyle);
        GUILayout.Space(4f);
        float segW = 460f, segH = 44f;
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("WASD", _useWasd ? _segActiveStyle : _segInactiveStyle,
                             GUILayout.Width(segW * 0.5f), GUILayout.Height(segH)))
            _useWasd = true;
        if (GUILayout.Button("Point & Click", !_useWasd ? _segActiveStyle : _segInactiveStyle,
                             GUILayout.Width(segW * 0.5f), GUILayout.Height(segH)))
            _useWasd = false;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);

        // ── Graphics quality ──────────────────────────────────────────────
        GUILayout.Label("Graphics Quality", _panelLabelStyle);
        GUILayout.Space(4f);
        string[] qualityLabels = { "Low", "Medium", "High" };
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        for (int i = 0; i < 3; i++)
        {
            if (GUILayout.Button(qualityLabels[i],
                                 i == _qualityIndex ? _segActiveStyle : _segInactiveStyle,
                                 GUILayout.Width(segW / 3f), GUILayout.Height(segH)))
            {
                _qualityIndex = i;
                ApplyRenderScale(_qualityIndex);
            }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);

        // ── FPS cap ───────────────────────────────────────────────────────
        GUILayout.Label("Frame Rate Cap", _panelLabelStyle);
        GUILayout.Space(4f);
        bool isUncapped = _targetFps >= 241f;
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(isUncapped ? "Uncapped" : $"{Mathf.RoundToInt(_targetFps)} FPS",
                        _panelLabelStyle, GUILayout.Width(110f));
        float newFps = GUILayout.HorizontalSlider(_targetFps, 30f, 241f,
                                                   GUILayout.Width(segW - 120f));
        newFps = Mathf.Round(newFps);
        if (!Mathf.Approximately(newFps, _targetFps))
        {
            _targetFps = newFps;
            Application.targetFrameRate = _targetFps >= 241f ? -1 : (int)_targetFps;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);

        // ── HUD toggles ───────────────────────────────────────────────────
        GUILayout.Label("HUD Overlays", _panelLabelStyle);
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _showFps = GUILayout.Toggle(_showFps, "Show FPS", _toggleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _showConnectionStatus = GUILayout.Toggle(_showConnectionStatus, "Show Connection Status", _toggleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _showLatency = GUILayout.Toggle(_showLatency, "Show Latency", _toggleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();

        // ── Relay join code (host only) ───────────────────────────────────
        if (_useRelay && !string.IsNullOrEmpty(_joinCode) &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            GUILayout.Label("Relay Join Code", _panelLabelStyle);
            GUILayout.Space(4f);
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
            GUILayout.Space(12f);
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsClient || nm.IsServer))
        {
            if (GUILayout.Button("Disconnect & Return to Main Menu", _buttonStyle))
            {
                nm.Shutdown();
                _relayBusy = false;
                _joinCode = "";
                _relayError = "";
                _state = UIState.MainMenu;
            }
            GUILayout.Space(8f);
        }

        if (GUILayout.Button("Close  [ESC]", _buttonStyle))
            _state = _settingsPreviousState;

        GUILayout.EndArea();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Relay async helpers
    // ─────────────────────────────────────────────────────────────────────────

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
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
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
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
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
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
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

    private static readonly float[] RenderScales = { 0.5f, 0.75f, 1.0f };

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
            fontSize = 18,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = Color.white }
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
    }
}
