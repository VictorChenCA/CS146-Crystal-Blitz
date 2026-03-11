using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Menu item: Tools → Setup Hat Prefabs
/// 1. Creates a prefab for each of the 11 hat FBXs in Assets/AddOns/Mg3D_Hats/
///    and saves them to Assets/AddOns/Mg3D_Hats/Prefabs/
/// 2. Adds a HatPoint child to GamePlayer.prefab at local pos (0, 1.05, 0)
///    (skipped if it already exists)
/// 3. Assigns the 11 prefabs (in canonical order) to HatManager._hatPrefabs on GamePlayer.prefab
/// </summary>
public static class HatPrefabSetup
{
    private static readonly string[] HatOrder =
    {
        "CowboyHat",
        "Crown",
        "MagicianHat",
        "MinerHat",
        "Mustache",
        "PajamaHat",
        "PillboxHat",
        "PoliceCap",
        "ShowerCap",
        "Sombrero",
        "VikingHelmet",
    };

    private const string FbxFolder     = "Assets/AddOns/Mg3D_Hats";
    private const string PrefabFolder  = "Assets/AddOns/Mg3D_Hats/Prefabs";
    private const string PlayerPrefab  = "Assets/Prefabs/GamePlayer.prefab";

    [MenuItem("Tools/Setup Hat Prefabs")]
    public static void Run()
    {
        // 1. Ensure prefab output folder exists
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder(FbxFolder, "Prefabs");
            AssetDatabase.Refresh();
        }

        // 2. Create / update one prefab per hat
        var hatPrefabs = new GameObject[HatOrder.Length];
        for (int i = 0; i < HatOrder.Length; i++)
        {
            string name        = HatOrder[i];
            string fbxPath     = $"{FbxFolder}/{name}.fbx";
            string prefabPath  = $"{PrefabFolder}/{name}.prefab";

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[HatPrefabSetup] FBX not found: {fbxPath}");
                continue;
            }

            // If prefab already exists, load it; otherwise save a new one
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing == null)
            {
                // Instantiate into scene temporarily, save as prefab, then destroy
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                inst.name = name;
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                hatPrefabs[i] = saved;
                Debug.Log($"[HatPrefabSetup] Created prefab: {prefabPath}");
            }
            else
            {
                hatPrefabs[i] = existing;
                Debug.Log($"[HatPrefabSetup] Reusing existing prefab: {prefabPath}");
            }
        }

        // 3. Modify GamePlayer.prefab
        GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (playerAsset == null)
        {
            Debug.LogError($"[HatPrefabSetup] GamePlayer.prefab not found at {PlayerPrefab}");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(PlayerPrefab))
        {
            GameObject root = scope.prefabContentsRoot;

            // 3a. Add HatPoint child if missing
            Transform hatPoint = root.transform.Find("HatPoint");
            if (hatPoint == null)
            {
                var go = new GameObject("HatPoint");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale    = Vector3.one;
                hatPoint = go.transform;
                Debug.Log("[HatPrefabSetup] Added HatPoint child to GamePlayer.prefab");
            }
            else
            {
                Debug.Log("[HatPrefabSetup] HatPoint already exists — skipping creation");
            }

            // 3b. Add HatManager component if missing
            HatManager hatManager = root.GetComponent<HatManager>();
            if (hatManager == null)
            {
                hatManager = root.AddComponent<HatManager>();
                Debug.Log("[HatPrefabSetup] Added HatManager component to GamePlayer.prefab");
            }
            else
            {
                Debug.Log("[HatPrefabSetup] HatManager already exists — updating prefab array");
            }

            // 3c. Assign hat prefabs via SerializedObject
            var so = new SerializedObject(hatManager);
            SerializedProperty prefabsProp = so.FindProperty("_hatPrefabs");
            prefabsProp.arraySize = hatPrefabs.Length;
            for (int i = 0; i < hatPrefabs.Length; i++)
                prefabsProp.GetArrayElementAtIndex(i).objectReferenceValue = hatPrefabs[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HatPrefabSetup] Done! All hat prefabs created and assigned.");
    }
}
