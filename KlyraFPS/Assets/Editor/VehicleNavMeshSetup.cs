using UnityEngine;
using UnityEditor;
using UnityEngine.AI;

/// <summary>
/// Custom inspector for VehicleNavMeshSurface - adds Bake button
/// </summary>
[CustomEditor(typeof(VehicleNavMeshSurface))]
public class VehicleNavMeshSurfaceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VehicleNavMeshSurface surface = (VehicleNavMeshSurface)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Bake Vehicle NavMesh", GUILayout.Height(30)))
        {
            surface.BakeNavMesh();
            EditorUtility.SetDirty(surface);
        }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Clear", GUILayout.Height(30), GUILayout.Width(60)))
        {
            surface.ClearNavMesh();
            EditorUtility.SetDirty(surface);
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Click 'Bake Vehicle NavMesh' to generate navigation mesh sized for tanks.\n\n" +
            "The NavMesh will use the agent radius/height specified above instead of the default humanoid size.",
            MessageType.Info);
    }
}

/// <summary>
/// Editor utility for setting up NavMesh for vehicles (tanks, etc.)
/// Helps configure NavMesh agent types and bake settings for larger agents.
/// </summary>
public class VehicleNavMeshSetup : EditorWindow
{
    // Vehicle settings
    private float vehicleRadius = 2.5f;    // Half the width of the vehicle
    private float vehicleHeight = 3f;      // Height of the vehicle
    private float stepHeight = 0.5f;       // Max step the vehicle can climb
    private float maxSlope = 30f;          // Max slope in degrees

    // NavMesh build settings
    private float voxelSize = 0.3f;        // Smaller = more accurate, slower bake
    private float minRegionArea = 10f;     // Minimum area for a region

    [MenuItem("Tools/Vehicle NavMesh Setup")]
    public static void ShowWindow()
    {
        GetWindow<VehicleNavMeshSetup>("Vehicle NavMesh Setup");
    }

    void OnGUI()
    {
        GUILayout.Label("Vehicle NavMesh Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "This tool helps configure NavMesh settings for vehicles like tanks.\n\n" +
            "Steps:\n" +
            "1. Adjust vehicle dimensions below\n" +
            "2. Click 'Apply Vehicle Agent Settings'\n" +
            "3. Open Navigation window (Window > AI > Navigation)\n" +
            "4. Select your terrain/ground objects\n" +
            "5. In Navigation window, select 'Vehicle' agent type\n" +
            "6. Click 'Bake' to generate NavMesh",
            MessageType.Info);

        EditorGUILayout.Space();
        GUILayout.Label("Vehicle Dimensions", EditorStyles.boldLabel);

        vehicleRadius = EditorGUILayout.FloatField("Radius (half width)", vehicleRadius);
        vehicleHeight = EditorGUILayout.FloatField("Height", vehicleHeight);
        stepHeight = EditorGUILayout.FloatField("Step Height", stepHeight);
        maxSlope = EditorGUILayout.Slider("Max Slope (degrees)", maxSlope, 0f, 60f);

        EditorGUILayout.Space();
        GUILayout.Label("NavMesh Quality", EditorStyles.boldLabel);

        voxelSize = EditorGUILayout.Slider("Voxel Size", voxelSize, 0.1f, 1f);
        EditorGUILayout.HelpBox("Smaller voxel size = more accurate but slower bake", MessageType.None);

        minRegionArea = EditorGUILayout.FloatField("Min Region Area", minRegionArea);

        EditorGUILayout.Space();

        if (GUILayout.Button("Apply Vehicle Agent Settings", GUILayout.Height(30)))
        {
            ApplyVehicleAgentSettings();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Open Navigation Window", GUILayout.Height(25)))
        {
            EditorApplication.ExecuteMenuItem("Window/AI/Navigation");
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "After baking, ensure your tank prefab has:\n" +
            "- TankNavigation component attached\n" +
            "- NavMeshAgent component (auto-added)\n\n" +
            "The TankNavigation component will configure the agent at runtime.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Add TankNavigation to Selected", GUILayout.Height(25)))
        {
            AddTankNavigationToSelected();
        }

        EditorGUILayout.Space();
        GUILayout.Label("Debug", EditorStyles.boldLabel);

        if (GUILayout.Button("Show NavMesh Info"))
        {
            ShowNavMeshInfo();
        }
    }

    void ApplyVehicleAgentSettings()
    {
        // Note: Unity's NavMesh system uses agent types defined in the Navigation settings
        // We can't programmatically add new agent types, but we can modify build settings

        // Get the current NavMesh build settings
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0); // Default agent

        // Create settings for vehicles (larger radius)
        settings.agentRadius = vehicleRadius;
        settings.agentHeight = vehicleHeight;
        settings.agentSlope = maxSlope;
        settings.agentClimb = stepHeight;
        settings.voxelSize = voxelSize;
        settings.minRegionArea = minRegionArea;

        // Unfortunately, we can't directly modify the global NavMesh settings from code
        // User needs to do this in the Navigation window

        EditorUtility.DisplayDialog("Vehicle NavMesh Setup",
            $"Recommended settings for your vehicle:\n\n" +
            $"Agent Radius: {vehicleRadius}\n" +
            $"Agent Height: {vehicleHeight}\n" +
            $"Max Slope: {maxSlope}\n" +
            $"Step Height: {stepHeight}\n" +
            $"Voxel Size: {voxelSize}\n\n" +
            "To apply these settings:\n" +
            "1. Open Navigation window (Window > AI > Navigation)\n" +
            "2. Go to 'Agents' tab\n" +
            "3. Create a new agent type called 'Vehicle'\n" +
            "4. Set the values shown above\n" +
            "5. Go to 'Bake' tab and select 'Vehicle' agent\n" +
            "6. Click 'Bake'",
            "OK");

        Debug.Log($"[VehicleNavMesh] Recommended settings - Radius: {vehicleRadius}, Height: {vehicleHeight}, Slope: {maxSlope}, Step: {stepHeight}");
    }

    void AddTankNavigationToSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        int added = 0;

        foreach (GameObject go in selected)
        {
            TankController tank = go.GetComponent<TankController>();
            if (tank != null)
            {
                TankNavigation nav = go.GetComponent<TankNavigation>();
                if (nav == null)
                {
                    Undo.AddComponent<TankNavigation>(go);
                    added++;
                    Debug.Log($"[VehicleNavMesh] Added TankNavigation to {go.name}");
                }
            }
        }

        if (added > 0)
        {
            EditorUtility.DisplayDialog("TankNavigation Added",
                $"Added TankNavigation component to {added} tank(s).",
                "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("No Tanks Selected",
                "Please select GameObjects with TankController component.",
                "OK");
        }
    }

    void ShowNavMeshInfo()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        string info = $"NavMesh Information:\n\n";
        info += $"Vertices: {triangulation.vertices.Length}\n";
        info += $"Triangles: {triangulation.indices.Length / 3}\n";
        info += $"Areas: {triangulation.areas.Length}\n\n";

        // Check agent types
        for (int i = 0; i < 10; i++)
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(i);
            if (settings.agentTypeID != -1)
            {
                info += $"Agent {i}: Radius={settings.agentRadius:F2}, Height={settings.agentHeight:F2}\n";
            }
        }

        Debug.Log(info);
        EditorUtility.DisplayDialog("NavMesh Info", info, "OK");
    }
}

/// <summary>
/// Menu item to quickly bake NavMesh for the current scene
/// </summary>
public static class VehicleNavMeshQuickBake
{
    [MenuItem("Tools/Quick Bake Vehicle NavMesh")]
    public static void QuickBake()
    {
        // This triggers a NavMesh bake with current settings
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        Debug.Log("[VehicleNavMesh] NavMesh baked!");
    }
}
