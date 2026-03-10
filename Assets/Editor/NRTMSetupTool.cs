using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using TMPro;

/// <summary>
/// NRTM Lobby & Game Scene Setup Tool.
/// Open via: Tools > NRTM > Scene Setup Tool
/// </summary>
public class NRTMSetupTool : EditorWindow
{
    // ── Lobby ─────────────────────────────────────────────────────────────────
    private Vector3 _lobbyCenter    = new Vector3(0f, 0f, 100f);
    private float   _lobbyFloorY    = 0f;   // floor surface Y
    private float   _lobbyObjectY   = 1f;   // capsule / trigger centre Y

    // ── Game spawns ───────────────────────────────────────────────────────────
    private Vector3 _blueSpawn = new Vector3(-14f, 0f, -14f);
    private Vector3 _redSpawn  = new Vector3( 14f, 0f,  14f);

    // ── Scroll ────────────────────────────────────────────────────────────────
    private Vector2 _scroll;

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/NRTM/Scene Setup Tool")]
    public static void ShowWindow() => GetWindow<NRTMSetupTool>("NRTM Setup");

    private void OnGUI()
    {
        _scroll = GUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(6);
        GUILayout.Label("NRTM Scene Setup Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each section creates GameObjects in the active scene.\n" +
            "Run sections independently or press 'Setup Everything'.\n" +
            "Undo (Ctrl+Z) works on each operation.",
            MessageType.Info);

        // ── Config ────────────────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        GUILayout.Label("Configuration", EditorStyles.boldLabel);
        _lobbyCenter  = EditorGUILayout.Vector3Field("Lobby Center (floor)", _lobbyCenter);
        _lobbyFloorY  = EditorGUILayout.FloatField("  Floor surface Y", _lobbyFloorY);
        _lobbyObjectY = EditorGUILayout.FloatField("  Object centre Y (capsules)", _lobbyObjectY);
        _blueSpawn    = EditorGUILayout.Vector3Field("Blue team spawn base", _blueSpawn);
        _redSpawn     = EditorGUILayout.Vector3Field("Red  team spawn base",  _redSpawn);

        // ── Full setup ────────────────────────────────────────────────────────
        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("▶  Setup Everything", GUILayout.Height(38)))
        {
            SetupGamePhaseManager();
            SetupLobbyZones();
            SetupTrainingDummy();
            SetupStructures();
            SetupSpawnBarriers();
            WireGamePhaseManager();
            Debug.Log("[NRTM] Full scene setup complete. See instructions for NavMesh baking.");
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(6);
        GUILayout.Label("― or run sections individually ―", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space(6);

        // ── Individual buttons ────────────────────────────────────────────────
        Section("1. GamePhaseManager",
            "Empty NetworkObject that drives the Lobby→InGame→GameOver state machine.",
            SetupGamePhaseManager);

        Section("2. Lobby Zones",
            "BlueTeamZone, RedTeamZone, StartGameZone, CharSelect1/2 — each is a trigger sphere with a coloured flat disc.",
            SetupLobbyZones);

        Section("3. Training Dummy",
            "Capsule NetworkObject at lobby centre with TrainingDummy component.",
            SetupTrainingDummy);

        Section("4. Structures (towers + crystals)",
            "BlueCrystal, BlueTower1/2, RedCrystal, RedTower1/2 — each is a NetworkObject with StructureHealth (towers also get TowerAttack).",
            SetupStructures);

        Section("5. Spawn Barriers",
            "SpawnBarrier_Team0 and _Team1, each with 4 wall child colliders (start disabled). GamePhaseManager enables them for 10 s at game start.",
            SetupSpawnBarriers);

        Section("6. Wire GamePhaseManager references",
            "Finds all StructureHealth and SpawnBarrierController objects in scene and assigns them to GamePhaseManager.",
            WireGamePhaseManager);

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "MANUAL STEPS after running this tool:\n\n" +
            "A) NavMesh — select LobbyFloor, add NavMeshSurface (collectObjects = Children), bake.\n" +
            "   Then select the game ground plane, add NavMeshSurface (collectObjects = Children), bake.\n\n" +
            "B) Prefabs — GamePhaseManager, TrainingDummy, and all 6 structures are in-scene NetworkObjects (no prefab needed, but mark the scene dirty and save).\n\n" +
            "C) Verify — enter Play → Host → check Console for any missing-reference errors.",
            MessageType.Warning);

        GUILayout.EndScrollView();
    }

    // ── Section helper ────────────────────────────────────────────────────────

    private static void Section(string title, string desc, System.Action action)
    {
        EditorGUILayout.Space(4);
        GUILayout.Label(title, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(desc, MessageType.None);
        if (GUILayout.Button($"Create: {title}"))
            action();
        EditorGUILayout.Space(2);
    }

    // =========================================================================
    // 1. GamePhaseManager
    // =========================================================================

    private void SetupGamePhaseManager()
    {
        if (FindFirstObjectByType<GamePhaseManager>() != null)
        {
            Debug.LogWarning("[NRTM] GamePhaseManager already exists in scene — skipped.");
            return;
        }

        var go = new GameObject("GamePhaseManager");
        go.AddComponent<NetworkObject>();
        go.AddComponent<GamePhaseManager>();

        Undo.RegisterCreatedObjectUndo(go, "Create GamePhaseManager");
        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        Debug.Log("[NRTM] GamePhaseManager created. Run 'Wire References' after creating structures + barriers.");
    }

    // =========================================================================
    // 2. Lobby Zones
    // =========================================================================

    private void SetupLobbyZones()
    {
        // Offsets are 5× the original compact layout so zones spread across the wider lobby area
        CreateLobbyZone("BlueTeamZone",  LobbyPos(-14f,   0f),  1.8f, new Color(0.2f, 0.5f,  1f,   0.55f), LobbyZone.ZoneType.BlueTeam,   "Join Blue Team");
        CreateLobbyZone("RedTeamZone",   LobbyPos( 14f,   0f),  1.8f, new Color(1f,  0.25f,  0.25f,0.55f), LobbyZone.ZoneType.RedTeam,    "Join Red Team");
        CreateLobbyZone("StartGameZone", LobbyPos(  0f,  16f),  1.6f, new Color(0.2f, 0.85f, 0.2f, 0.55f), LobbyZone.ZoneType.StartGame,  "Start Game");
        CreateLobbyZone("CharSelect1",   LobbyPos(-10f, -14f),  1.4f, new Color(0.7f, 0.7f,  0.7f, 0.55f), LobbyZone.ZoneType.CharSelect1,"Character 1");
        CreateLobbyZone("CharSelect2",   LobbyPos( 10f, -14f),  1.4f, new Color(0.7f, 0.7f,  0.7f, 0.55f), LobbyZone.ZoneType.CharSelect2,"Character 2");
        Debug.Log("[NRTM] Lobby zones created.");
    }

    /// <summary>X/Z offset from lobby centre; returns world position at floor Y.</summary>
    private Vector3 LobbyPos(float dx, float dz) =>
        new Vector3(_lobbyCenter.x + dx, _lobbyFloorY, _lobbyCenter.z + dz);

    private void CreateLobbyZone(string name, Vector3 pos, float radius, Color discColor, LobbyZone.ZoneType type, string label)
    {
        var go = new GameObject(name);
        go.transform.position = pos;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = radius;

        var zone = go.AddComponent<LobbyZone>();
        zone.zoneType = type;

        // Flat disc child (visual only)
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Disc";
        disc.transform.SetParent(go.transform, false);
        disc.transform.localPosition = Vector3.zero;
        disc.transform.localScale    = new Vector3(radius * 2f, 0.04f, radius * 2f);
        DestroyImmediate(disc.GetComponent<Collider>());

        var mat   = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = discColor;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        disc.GetComponent<Renderer>().sharedMaterial = mat;

        // World-space TMP label — lies flat on the disc, readable from the isometric camera (45,45,0)
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        // Lay text flat on the ground (X=90) then rotate to face camera's view direction (Y=-45 for camera Y=45)
        textGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);   // just above disc surface
        textGo.transform.localRotation = Quaternion.Euler(90f, -45f, 0f);

        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text      = label;
        tmp.fontSize  = 3f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        // Size the rect to contain the text
        tmp.rectTransform.sizeDelta = new Vector2(radius * 4f, radius * 2f);

        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
    }

    // =========================================================================
    // 3. Training Dummy
    // =========================================================================

    private void SetupTrainingDummy()
    {
        if (FindFirstObjectByType<TrainingDummy>() != null)
        {
            Debug.LogWarning("[NRTM] TrainingDummy already exists — skipped.");
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "TrainingDummy";
        go.transform.position = new Vector3(_lobbyCenter.x, _lobbyObjectY, _lobbyCenter.z);

        // Grey colour
        var mat   = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.45f, 0.45f, 0.45f);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        go.AddComponent<NetworkObject>();
        go.AddComponent<TrainingDummy>();

        Undo.RegisterCreatedObjectUndo(go, "Create TrainingDummy");
        Debug.Log("[NRTM] TrainingDummy created at lobby centre.");
    }

    // =========================================================================
    // 4. Structures
    // =========================================================================

    private void SetupStructures()
    {
        // ── Blue team ─────────────────────────────────────────────────────────
        CreateCrystal("BlueCrystal", new Vector3(-20f, 1f, -20f), 0, new Color(0.2f, 0.5f, 1f));
        CreateTower("BlueTower1",    new Vector3(-13f, 1.5f,  -5f), 0, new Color(0.35f, 0.6f, 1f));
        CreateTower("BlueTower2",    new Vector3( -5f, 1.5f, -13f), 0, new Color(0.35f, 0.6f, 1f));

        // ── Red team ──────────────────────────────────────────────────────────
        CreateCrystal("RedCrystal", new Vector3( 20f, 1f,  20f), 1, new Color(1f, 0.25f, 0.25f));
        CreateTower("RedTower1",    new Vector3( 13f, 1.5f,  5f), 1, new Color(1f, 0.45f, 0.45f));
        CreateTower("RedTower2",    new Vector3(  5f, 1.5f, 13f), 1, new Color(1f, 0.45f, 0.45f));

        Debug.Log("[NRTM] 6 structures created.");
    }

    private void CreateCrystal(string name, Vector3 pos, int team, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = new Vector3(2f, 2f, 2f);

        ApplyMat(go, color);
        go.AddComponent<NetworkObject>();

        var sh = go.AddComponent<StructureHealth>();
        // Use SerializedObject to set private/serialized fields
        var so = new SerializedObject(sh);
        so.FindProperty("TeamIndex").intValue     = team;
        so.FindProperty("maxHealth").floatValue   = 2000f;
        so.FindProperty("IsCrystal").boolValue    = true;
        so.FindProperty("StructureName").stringValue = name;
        so.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
    }

    private void CreateTower(string name, Vector3 pos, int team, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = new Vector3(1f, 1.5f, 1f);

        ApplyMat(go, color);
        go.AddComponent<NetworkObject>();

        var sh = go.AddComponent<StructureHealth>();
        var so = new SerializedObject(sh);
        so.FindProperty("TeamIndex").intValue      = team;
        so.FindProperty("maxHealth").floatValue    = 1000f;
        so.FindProperty("IsCrystal").boolValue     = false;
        so.FindProperty("StructureName").stringValue = name;
        so.ApplyModifiedProperties();

        var ta = go.AddComponent<TowerAttack>();
        var taSo = new SerializedObject(ta);
        taSo.FindProperty("teamIndex").intValue = team;
        taSo.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
    }

    // =========================================================================
    // 5. Spawn Barriers
    // =========================================================================

    private void SetupSpawnBarriers()
    {
        CreateBarrier("SpawnBarrier_Team0", _blueSpawn + Vector3.up);
        CreateBarrier("SpawnBarrier_Team1", _redSpawn  + Vector3.up);
        Debug.Log("[NRTM] Spawn barriers created (walls disabled by default).");
    }

    private void CreateBarrier(string name, Vector3 pos)
    {
        var parent = new GameObject(name);
        parent.transform.position = pos;
        parent.AddComponent<SpawnBarrierController>();

        // 4 walls: N, S, E, W   (local coords relative to parent)
        // Each wall: a box collider child with a renderer (disabled by default via SpawnBarrierController.Awake)
        AddWall(parent, "Wall_N", new Vector3( 0f, 1.5f,  5f), new Vector3(10f, 3f, 0.4f));
        AddWall(parent, "Wall_S", new Vector3( 0f, 1.5f, -5f), new Vector3(10f, 3f, 0.4f));
        AddWall(parent, "Wall_E", new Vector3( 5f, 1.5f,  0f), new Vector3(0.4f, 3f, 10f));
        AddWall(parent, "Wall_W", new Vector3(-5f, 1.5f,  0f), new Vector3(0.4f, 3f, 10f));

        Undo.RegisterCreatedObjectUndo(parent, $"Create {name}");
    }

    private static void AddWall(GameObject parent, string wallName, Vector3 localPos, Vector3 localScale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = wallName;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = localScale;

        // Semi-transparent white material
        var mat   = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(1f, 1f, 1f, 0.3f);
        mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        go.GetComponent<Renderer>().sharedMaterial = mat;

        // SpawnBarrierController.Awake disables collider + renderer automatically
    }

    // =========================================================================
    // 6. Wire GamePhaseManager references
    // =========================================================================

    private void WireGamePhaseManager()
    {
        var gpm = FindFirstObjectByType<GamePhaseManager>();
        if (gpm == null)
        {
            Debug.LogError("[NRTM] GamePhaseManager not found — run section 1 first.");
            return;
        }

        var so = new SerializedObject(gpm);

        // ── Structures ────────────────────────────────────────────────────────
        var structures = FindObjectsByType<StructureHealth>(FindObjectsSortMode.None);
        var structProp = so.FindProperty("_allStructures");
        structProp.arraySize = structures.Length;
        for (int i = 0; i < structures.Length; i++)
            structProp.GetArrayElementAtIndex(i).objectReferenceValue = structures[i];

        // ── Barriers ──────────────────────────────────────────────────────────
        var barriers   = FindObjectsByType<SpawnBarrierController>(FindObjectsSortMode.None);
        var barrierProp = so.FindProperty("_spawnBarriers");
        barrierProp.arraySize = barriers.Length;
        for (int i = 0; i < barriers.Length; i++)
            barrierProp.GetArrayElementAtIndex(i).objectReferenceValue = barriers[i];

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(gpm);

        Debug.Log($"[NRTM] GamePhaseManager wired: {structures.Length} structures, {barriers.Length} barriers.");
        Selection.activeGameObject = gpm.gameObject;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static void ApplyMat(GameObject go, Color color)
    {
        var mat   = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
