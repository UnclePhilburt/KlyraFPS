using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class JetAeroSetup : EditorWindow
{
    [MenuItem("Tools/Klyra/Setup Jet Aerodynamics")]
    public static void ShowWindow()
    {
        GetWindow<JetAeroSetup>("Jet Aero Setup");
    }

    private GameObject jetPrefab;

    void OnGUI()
    {
        GUILayout.Label("Jet Aerodynamics Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        jetPrefab = (GameObject)EditorGUILayout.ObjectField("Jet Prefab/Object", jetPrefab, typeof(GameObject), true);

        GUILayout.Space(20);

        if (GUILayout.Button("Add Aerodynamic Surfaces", GUILayout.Height(40)))
        {
            if (jetPrefab != null)
            {
                SetupAeroSurfaces(jetPrefab);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please assign a jet first!", "OK");
            }
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Create Default Wing Config", GUILayout.Height(30)))
        {
            CreateDefaultConfigs();
        }

        GUILayout.Space(20);
        EditorGUILayout.HelpBox(
            "This will add AeroSurface components to your jet:\n" +
            "- Main Wing (left & right)\n" +
            "- Horizontal Stabilizer (elevator)\n" +
            "- Vertical Stabilizer (rudder)\n" +
            "- Ailerons (roll control)\n\n" +
            "Make sure to create the config assets first!",
            MessageType.Info);
    }

    void CreateDefaultConfigs()
    {
        string path = "Assets/AircraftPhysics/Configs";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder("Assets/AircraftPhysics", "Configs");
        }

        // Main wing config
        var wingConfig = ScriptableObject.CreateInstance<AeroSurfaceConfig>();
        wingConfig.liftSlope = 6.28f;
        wingConfig.skinFriction = 0.02f;
        wingConfig.zeroLiftAoA = -2f;
        wingConfig.stallAngleHigh = 15f;
        wingConfig.stallAngleLow = -15f;
        wingConfig.chord = 3f;
        wingConfig.span = 5f;
        wingConfig.autoAspectRatio = true;
        AssetDatabase.CreateAsset(wingConfig, path + "/WingConfig.asset");

        // Tail config (smaller)
        var tailConfig = ScriptableObject.CreateInstance<AeroSurfaceConfig>();
        tailConfig.liftSlope = 6.28f;
        tailConfig.skinFriction = 0.02f;
        tailConfig.zeroLiftAoA = 0f;
        tailConfig.stallAngleHigh = 15f;
        tailConfig.stallAngleLow = -15f;
        tailConfig.chord = 1.5f;
        tailConfig.span = 3f;
        tailConfig.flapFraction = 0.3f;
        tailConfig.autoAspectRatio = true;
        AssetDatabase.CreateAsset(tailConfig, path + "/TailConfig.asset");

        // Rudder config
        var rudderConfig = ScriptableObject.CreateInstance<AeroSurfaceConfig>();
        rudderConfig.liftSlope = 6.28f;
        rudderConfig.skinFriction = 0.02f;
        rudderConfig.zeroLiftAoA = 0f;
        rudderConfig.stallAngleHigh = 15f;
        rudderConfig.stallAngleLow = -15f;
        rudderConfig.chord = 2f;
        rudderConfig.span = 2.5f;
        rudderConfig.flapFraction = 0.3f;
        rudderConfig.autoAspectRatio = true;
        AssetDatabase.CreateAsset(rudderConfig, path + "/RudderConfig.asset");

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Success", "Created config assets in:\n" + path, "OK");
    }

    void SetupAeroSurfaces(GameObject jet)
    {
        // Check if this is a prefab asset (not an instance)
        bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(jet);

        if (isPrefabAsset)
        {
            // Open prefab for editing
            string prefabPath = AssetDatabase.GetAssetPath(jet);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            SetupAeroSurfacesOnObject(prefabRoot);

            // Save and unload
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            EditorUtility.DisplayDialog("Success", "Aerodynamic surfaces added to prefab!", "OK");
            return;
        }

        // It's a scene instance, modify directly
        SetupAeroSurfacesOnObject(jet);

        EditorUtility.DisplayDialog("Success",
            "Added 6 aerodynamic surfaces!\n\n" +
            "- Left/Right Wing\n" +
            "- Left/Right Aileron\n" +
            "- Elevator\n" +
            "- Rudder",
            "OK");
    }

    void SetupAeroSurfacesOnObject(GameObject jet)
    {
        // Load configs
        var wingConfig = AssetDatabase.LoadAssetAtPath<AeroSurfaceConfig>("Assets/AircraftPhysics/Configs/WingConfig.asset");
        var tailConfig = AssetDatabase.LoadAssetAtPath<AeroSurfaceConfig>("Assets/AircraftPhysics/Configs/TailConfig.asset");
        var rudderConfig = AssetDatabase.LoadAssetAtPath<AeroSurfaceConfig>("Assets/AircraftPhysics/Configs/RudderConfig.asset");

        if (wingConfig == null || tailConfig == null || rudderConfig == null)
        {
            EditorUtility.DisplayDialog("Error", "Please create config assets first!", "OK");
            return;
        }

        // Create parent for aero surfaces
        Transform aeroParent = jet.transform.Find("AeroSurfaces");
        if (aeroParent == null)
        {
            GameObject aeroObj = new GameObject("AeroSurfaces");
            aeroObj.transform.SetParent(jet.transform);
            aeroObj.transform.localPosition = Vector3.zero;
            aeroObj.transform.localRotation = Quaternion.identity;
            aeroParent = aeroObj.transform;
        }

        List<AeroSurface> surfaces = new List<AeroSurface>();

        // Left Wing
        var leftWing = CreateAeroSurface("LeftWing", aeroParent, new Vector3(-3f, 0f, 0f), wingConfig);
        surfaces.Add(leftWing);

        // Right Wing
        var rightWing = CreateAeroSurface("RightWing", aeroParent, new Vector3(3f, 0f, 0f), wingConfig);
        surfaces.Add(rightWing);

        // Left Aileron (control surface)
        var leftAileron = CreateAeroSurface("LeftAileron", aeroParent, new Vector3(-4f, 0f, -1f), tailConfig);
        leftAileron.IsControlSurface = true;
        leftAileron.InputType = ControlInputType.Roll;
        leftAileron.InputMultiplyer = -1f;
        surfaces.Add(leftAileron);

        // Right Aileron (control surface)
        var rightAileron = CreateAeroSurface("RightAileron", aeroParent, new Vector3(4f, 0f, -1f), tailConfig);
        rightAileron.IsControlSurface = true;
        rightAileron.InputType = ControlInputType.Roll;
        rightAileron.InputMultiplyer = 1f;
        surfaces.Add(rightAileron);

        // Horizontal Stabilizer / Elevator
        var elevator = CreateAeroSurface("Elevator", aeroParent, new Vector3(0f, 0.5f, -6f), tailConfig);
        elevator.IsControlSurface = true;
        elevator.InputType = ControlInputType.Pitch;
        elevator.InputMultiplyer = 1f;
        surfaces.Add(elevator);

        // Vertical Stabilizer / Rudder
        var rudder = CreateAeroSurface("Rudder", aeroParent, new Vector3(0f, 1.5f, -6f), rudderConfig);
        rudder.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        rudder.IsControlSurface = true;
        rudder.InputType = ControlInputType.Yaw;
        rudder.InputMultiplyer = 1f;
        surfaces.Add(rudder);

        // Assign to JetController
        var jetController = jet.GetComponent<JetController>();
        if (jetController != null)
        {
            jetController.controlSurfaces = surfaces;
            EditorUtility.SetDirty(jetController);
        }
    }

    AeroSurface CreateAeroSurface(string name, Transform parent, Vector3 localPos, AeroSurfaceConfig config)
    {
        GameObject surfaceObj = new GameObject(name);
        surfaceObj.transform.SetParent(parent);
        surfaceObj.transform.localPosition = localPos;
        surfaceObj.transform.localRotation = Quaternion.identity;

        AeroSurface surface = surfaceObj.AddComponent<AeroSurface>();

        // Use SerializedObject to set the private config field
        SerializedObject so = new SerializedObject(surface);
        so.FindProperty("config").objectReferenceValue = config;
        so.ApplyModifiedProperties();

        return surface;
    }
}
