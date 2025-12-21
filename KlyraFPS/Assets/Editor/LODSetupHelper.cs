using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor tool to quickly set up LOD groups on selected objects.
/// Automatically creates simplified versions or just fades out at distance.
/// </summary>
public class LODSetupHelper : EditorWindow
{
    private float lodDistance1 = 25f;   // Full detail up to this distance
    private float lodDistance2 = 50f;   // Medium detail
    private float lodDistance3 = 100f;  // Low detail / cull
    private bool createCullLOD = true;  // Add a cull level (invisible at far distance)
    private float cullDistance = 150f;

    [MenuItem("Tools/LOD Setup Helper")]
    public static void ShowWindow()
    {
        GetWindow<LODSetupHelper>("LOD Setup Helper");
    }

    void OnGUI()
    {
        GUILayout.Label("LOD Setup Helper", EditorStyles.boldLabel);
        GUILayout.Space(10);

        GUILayout.Label("LOD Distances (meters):", EditorStyles.label);
        lodDistance1 = EditorGUILayout.FloatField("LOD 0 (Full Detail)", lodDistance1);
        lodDistance2 = EditorGUILayout.FloatField("LOD 1 (Medium)", lodDistance2);
        lodDistance3 = EditorGUILayout.FloatField("LOD 2 (Low)", lodDistance3);

        GUILayout.Space(10);
        createCullLOD = EditorGUILayout.Toggle("Add Cull Distance", createCullLOD);
        if (createCullLOD)
        {
            cullDistance = EditorGUILayout.FloatField("Cull Distance", cullDistance);
        }

        GUILayout.Space(20);

        if (GUILayout.Button("Add LOD Group to Selected", GUILayout.Height(30)))
        {
            AddLODToSelected();
        }

        if (GUILayout.Button("Add Simple Fade LOD to Selected", GUILayout.Height(30)))
        {
            AddSimpleFadeLOD();
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Select objects in the scene/hierarchy, then click a button.\n\n" +
            "'Add LOD Group' - Adds LOD group with current renderers as LOD0.\n" +
            "'Simple Fade LOD' - Just adds cull distance (disappear at range).",
            MessageType.Info);

        GUILayout.Space(10);
        GUILayout.Label("Batch Operations:", EditorStyles.boldLabel);

        if (GUILayout.Button("Add Cull LOD to All Props"))
        {
            AddCullLODToAllProps();
        }

        if (GUILayout.Button("Setup Camera Layer Culling"))
        {
            SetupCameraLayerCulling();
        }
    }

    void AddLODToSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select one or more objects.", "OK");
            return;
        }

        int count = 0;
        foreach (var go in selected)
        {
            if (go.GetComponent<LODGroup>() != null)
            {
                Debug.LogWarning($"[LODSetup] {go.name} already has LOD group, skipping");
                continue;
            }

            // Get all renderers
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[LODSetup] {go.name} has no renderers, skipping");
                continue;
            }

            // Add LOD group
            LODGroup lodGroup = go.AddComponent<LODGroup>();

            // Create LOD levels
            List<LOD> lods = new List<LOD>();

            // LOD 0 - Full detail (all current renderers)
            float screenSize0 = DistanceToScreenSize(lodDistance1);
            lods.Add(new LOD(screenSize0, renderers));

            // If we want cull, add final LOD with no renderers
            if (createCullLOD)
            {
                float cullScreenSize = DistanceToScreenSize(cullDistance);
                lods.Add(new LOD(cullScreenSize, new Renderer[0]));
            }

            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();

            Undo.RegisterCreatedObjectUndo(lodGroup, "Add LOD Group");
            count++;
        }

        Debug.Log($"[LODSetup] Added LOD groups to {count} objects");
    }

    void AddSimpleFadeLOD()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select one or more objects.", "OK");
            return;
        }

        int count = 0;
        foreach (var go in selected)
        {
            if (go.GetComponent<LODGroup>() != null)
            {
                Debug.LogWarning($"[LODSetup] {go.name} already has LOD group, skipping");
                continue;
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            LODGroup lodGroup = go.AddComponent<LODGroup>();

            // Just two levels: visible and culled
            float visibleScreenSize = DistanceToScreenSize(cullDistance);
            LOD[] lods = new LOD[]
            {
                new LOD(visibleScreenSize, renderers),
                new LOD(0f, new Renderer[0])  // Culled
            };

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

            Undo.RegisterCreatedObjectUndo(lodGroup, "Add Simple LOD");
            count++;
        }

        Debug.Log($"[LODSetup] Added simple fade LOD to {count} objects");
    }

    void AddCullLODToAllProps()
    {
        // Find all static objects that might be props
        MeshRenderer[] allRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var renderer in allRenderers)
        {
            GameObject go = renderer.gameObject;

            // Skip if already has LOD
            if (go.GetComponentInParent<LODGroup>() != null) continue;

            // Skip if it's a large object (buildings, terrain, etc)
            Bounds bounds = renderer.bounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size > 10f) continue;  // Skip large objects

            // Skip dynamic objects
            if (!go.isStatic) continue;

            // Add simple cull LOD
            LODGroup lodGroup = go.AddComponent<LODGroup>();
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

            float cullScreenSize = DistanceToScreenSize(100f);  // Cull at 100m
            LOD[] lods = new LOD[]
            {
                new LOD(cullScreenSize, renderers),
                new LOD(0f, new Renderer[0])
            };

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
            count++;
        }

        Debug.Log($"[LODSetup] Added cull LOD to {count} small static props");
    }

    void SetupCameraLayerCulling()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            EditorUtility.DisplayDialog("No Camera", "No main camera found in scene.", "OK");
            return;
        }

        // Set up per-layer culling distances
        float[] distances = new float[32];

        // Default distance
        for (int i = 0; i < 32; i++)
        {
            distances[i] = 0;  // 0 means use camera's far clip
        }

        // Set specific layer distances
        int effectsLayer = LayerMask.NameToLayer("Effects");
        int debrisLayer = LayerMask.NameToLayer("Debris");
        int propsLayer = LayerMask.NameToLayer("Props");

        if (effectsLayer >= 0) distances[effectsLayer] = 100f;
        if (debrisLayer >= 0) distances[debrisLayer] = 80f;
        if (propsLayer >= 0) distances[propsLayer] = 150f;

        mainCam.layerCullDistances = distances;
        mainCam.layerCullSpherical = true;

        EditorUtility.SetDirty(mainCam);
        Debug.Log("[LODSetup] Camera layer culling configured");
    }

    // Convert world distance to LOD screen size (0-1)
    // This is approximate - assumes standard FOV
    float DistanceToScreenSize(float distance)
    {
        // Screen size is roughly: objectSize / (2 * distance * tan(fov/2))
        // For a 1m object at given distance with 60 degree FOV
        float fov = 60f * Mathf.Deg2Rad;
        float objectSize = 1f;  // Reference size
        float screenSize = objectSize / (2f * distance * Mathf.Tan(fov / 2f));
        return Mathf.Clamp01(screenSize);
    }
}
