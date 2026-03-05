using UnityEngine;
using UnityEditor;

public class CloudBorderGenerator : EditorWindow
{
    float worldSpacing   = 1.5f;
    float yOffset        = 0.5f;
    float density        = 2.0f;   // mean number of spheres per border point per layer
    float densityStdDev  = 0.5f;   // spread around the mean
    float radiusMin      = 0.5f;
    float radiusMax      = 1.1f;
    float radiusBias     = 3.0f;   // how many times more likely radiusMin is than radiusMax
    float outerRandomXZ  = 0.6f;
    float outerRandomY   = 0.2f;
    int   outerLayers    = 2;
    int   randomSeed     = 42;

    [MenuItem("Tools/Cloud Border Generator")]
    static void Open() => GetWindow<CloudBorderGenerator>("Cloud Border");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Spacing & Placement", EditorStyles.boldLabel);
        worldSpacing = EditorGUILayout.FloatField("World Spacing", worldSpacing);
        yOffset      = EditorGUILayout.FloatField("Y Offset",      yOffset);
        density      = EditorGUILayout.FloatField("Density (mean)",  density);
        densityStdDev= EditorGUILayout.FloatField("Density Std Dev", densityStdDev);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clouds (CloudMesh)", EditorStyles.boldLabel);
        radiusMin     = EditorGUILayout.FloatField("Radius Min",        radiusMin);
        radiusMax     = EditorGUILayout.FloatField("Radius Max",        radiusMax);
        radiusBias    = EditorGUILayout.FloatField("Small Bias (min/max likelihood)", radiusBias);
        outerRandomXZ = EditorGUILayout.FloatField("XZ Jitter",   outerRandomXZ);
        outerRandomY  = EditorGUILayout.FloatField("Y Jitter",    outerRandomY);
        outerLayers   = EditorGUILayout.IntField("Layers",        outerLayers);
        randomSeed    = EditorGUILayout.IntField("Random Seed",   randomSeed);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate", GUILayout.Height(30)))
            Generate();
        if (GUILayout.Button("Clear"))
            Clear();
    }

    void Clear()
    {
        GameObject existing = GameObject.Find("CloudBorder");
        if (existing) Undo.DestroyObjectImmediate(existing);
    }

    void Generate()
    {
        GameObject ground = GameObject.Find("Ground");
        if (!ground) { Debug.LogError("CloudBorderGenerator: no GameObject named 'Ground' found."); return; }

        Material cloudMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/CloudMesh.mat");

        Clear();

        GameObject parent = new GameObject("CloudBorder");
        Undo.RegisterCreatedObjectUndo(parent, "Generate Cloud Border");

        Transform gt = ground.transform;
        var pts = BuildPerimeter(5f, 5f);
        Random.InitState(randomSeed);

        foreach (Vector3 lp in pts)
        {
            Vector3 worldPt = gt.TransformPoint(lp);
            Vector3 outDir  = new Vector3(worldPt.x - gt.position.x, 0f, worldPt.z - gt.position.z).normalized;

            for (int layer = 1; layer <= outerLayers; layer++)
            {
                int count = Mathf.Max(0, Mathf.RoundToInt(SampleNormal(density, densityStdDev)));

                for (int e = 0; e < count; e++)
                {
                    Vector3 pos = worldPt
                        + outDir * (layer * worldSpacing * 0.6f)
                        + new Vector3(
                            Random.Range(-outerRandomXZ, outerRandomXZ),
                            Random.Range(0f, outerRandomY),
                            Random.Range(-outerRandomXZ, outerRandomXZ));

                    float radius = SampleLinearBiased(radiusMin, radiusMax, radiusBias);
                    SpawnSphere(parent.transform, pos, radius, cloudMat);
                }
            }
        }

        Debug.Log($"CloudBorderGenerator: spawned {parent.transform.childCount} spheres.");
    }

    System.Collections.Generic.List<Vector3> BuildPerimeter(float hx, float hz)
    {
        var pts = new System.Collections.Generic.List<Vector3>();
        Vector3[] corners = { new(-hx, 0, -hz), new(hx, 0, -hz), new(hx, 0, hz), new(-hx, 0, hz) };

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4];
            int count = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / worldSpacing));
            for (int j = 0; j < count; j++)
                pts.Add(Vector3.Lerp(a, b, (float)j / count));
        }
        return pts;
    }

    // Linear PDF from p(min)=a to p(max)=b where a/b = bias.
    // Sampled via inverse CDF (quadratic solve).
    static float SampleLinearBiased(float min, float max, float bias)
    {
        bias = Mathf.Max(bias, 0.01f);
        // Normalise so PDF integrates to 1 over [0,1]: (a+b)/2 = 1 → a=2*bias/(bias+1), b=2/(bias+1)
        float a = 2f * bias / (bias + 1f); // weight at min
        float b = 2f        / (bias + 1f); // weight at max
        float u = Random.value;
        float t;
        if (Mathf.Approximately(a, b))
        {
            t = u; // uniform fallback (bias == 1)
        }
        else
        {
            // Solve: (b-a)/2 * t^2 + a*t - u = 0
            float A = (b - a) * 0.5f;
            t = (-a + Mathf.Sqrt(a * a + 2f * A * u)) / (2f * A);
            // t goes 0→1 meaning min→max, but a>b so smaller t (smaller radius) is more likely
            // PDF is a - (a-b)*t, which decreases → flip t so small radius maps to high probability
            t = 1f - t;
        }
        return Mathf.Lerp(min, max, t);
    }

    // Box-Muller transform — produces a standard normal sample
    static float SampleNormal(float mean, float stdDev)
    {
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stdDev * z;
    }

    void SpawnSphere(Transform parent, Vector3 worldPos, float radius, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Cloud";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position   = worldPos + Vector3.up * yOffset;
        go.transform.localScale = Vector3.one * radius;
        if (mat) go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
