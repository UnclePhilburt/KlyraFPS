using UnityEngine;
using UnityEditor;
using System.IO;

public class JetSetupHelper : EditorWindow
{
    [MenuItem("Tools/Klyra/Setup Jet System")]
    public static void ShowWindow()
    {
        GetWindow<JetSetupHelper>("Jet Setup Helper");
    }

    private GameObject jetModelPrefab;
    private GameObject aiPrefab;
    private GameObject boosterEffectPrefab;

    void OnGUI()
    {
        GUILayout.Label("Jet System Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        GUILayout.Label("Step 1: Assign References", EditorStyles.boldLabel);
        jetModelPrefab = (GameObject)EditorGUILayout.ObjectField("Jet Model Prefab", jetModelPrefab, typeof(GameObject), false);
        aiPrefab = (GameObject)EditorGUILayout.ObjectField("AI Soldier Prefab", aiPrefab, typeof(GameObject), false);
        boosterEffectPrefab = (GameObject)EditorGUILayout.ObjectField("Booster Effect (Optional)", boosterEffectPrefab, typeof(GameObject), false);

        GUILayout.Space(20);

        if (GUILayout.Button("Create Jet Prefab", GUILayout.Height(30)))
        {
            CreateJetPrefab();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Create JetSpawner in Scene", GUILayout.Height(30)))
        {
            CreateJetSpawner();
        }

        GUILayout.Space(20);
        GUILayout.Label("Instructions:", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Assign SM_Veh_Jet_01 (from Synty/PolygonMilitary/Prefabs/Vehicles)\n" +
            "2. Assign your AI soldier prefab\n" +
            "3. Optionally assign FX_Jet_Booster_01\n" +
            "4. Click 'Create Jet Prefab'\n" +
            "5. Click 'Create JetSpawner in Scene'\n" +
            "6. Assign your runways to the JetSpawner",
            MessageType.Info);
    }

    void CreateJetPrefab()
    {
        if (jetModelPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a Jet Model Prefab first!", "OK");
            return;
        }

        // Create root GameObject
        GameObject jetRoot = new GameObject("Jet_Fighter");

        // Add components to root
        Rigidbody rb = jetRoot.AddComponent<Rigidbody>();
        rb.mass = 1000f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 2f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Add box collider
        BoxCollider col = jetRoot.AddComponent<BoxCollider>();
        col.size = new Vector3(12f, 3f, 15f);
        col.center = new Vector3(0f, 1f, 0f);

        // Add JetController
        JetController jetController = jetRoot.AddComponent<JetController>();

        // Add JetWeapon
        JetWeapon jetWeapon = jetRoot.AddComponent<JetWeapon>();
        jetWeapon.jet = jetController;

        // Add PhotonView
        Photon.Pun.PhotonView photonView = jetRoot.AddComponent<Photon.Pun.PhotonView>();
        photonView.ObservedComponents = new System.Collections.Generic.List<Component> { jetController };

        // Add AudioSource for engine
        AudioSource engineAudio = jetRoot.AddComponent<AudioSource>();
        engineAudio.spatialBlend = 1f;
        engineAudio.maxDistance = 500f;
        engineAudio.loop = true;
        engineAudio.playOnAwake = false;

        // Instantiate model as child
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(jetModelPrefab);
        model.transform.SetParent(jetRoot.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.name = "Model";

        // Create pilot seat position
        GameObject pilotSeat = new GameObject("PilotSeat");
        pilotSeat.transform.SetParent(jetRoot.transform);
        pilotSeat.transform.localPosition = new Vector3(0f, 2f, 2f);
        jetController.pilotSeatPosition = pilotSeat.transform;

        // Create gun muzzles
        GameObject weapons = new GameObject("Weapons");
        weapons.transform.SetParent(jetRoot.transform);
        weapons.transform.localPosition = Vector3.zero;

        GameObject gunMuzzlesParent = new GameObject("GunMuzzles");
        gunMuzzlesParent.transform.SetParent(weapons.transform);
        gunMuzzlesParent.transform.localPosition = Vector3.zero;

        // Left gun
        GameObject leftGun = new GameObject("GunMuzzle_L");
        leftGun.transform.SetParent(gunMuzzlesParent.transform);
        leftGun.transform.localPosition = new Vector3(-2f, 0f, 7f);
        leftGun.transform.localRotation = Quaternion.identity;

        // Right gun
        GameObject rightGun = new GameObject("GunMuzzle_R");
        rightGun.transform.SetParent(gunMuzzlesParent.transform);
        rightGun.transform.localPosition = new Vector3(2f, 0f, 7f);
        rightGun.transform.localRotation = Quaternion.identity;

        jetController.gunMuzzles = new Transform[] { leftGun.transform, rightGun.transform };
        jetWeapon.gunMuzzles = new Transform[] { leftGun.transform, rightGun.transform };

        // Create missile pylons
        GameObject missilePylonsParent = new GameObject("MissilePylons");
        missilePylonsParent.transform.SetParent(weapons.transform);
        missilePylonsParent.transform.localPosition = Vector3.zero;

        Transform[] pylons = new Transform[4];
        Vector3[] pylonPositions = new Vector3[]
        {
            new Vector3(-4f, -0.5f, 0f),  // Left outer
            new Vector3(-2.5f, -0.5f, 0f),  // Left inner
            new Vector3(2.5f, -0.5f, 0f),   // Right inner
            new Vector3(4f, -0.5f, 0f)    // Right outer
        };

        for (int i = 0; i < 4; i++)
        {
            GameObject pylon = new GameObject($"MissilePylon_{i + 1}");
            pylon.transform.SetParent(missilePylonsParent.transform);
            pylon.transform.localPosition = pylonPositions[i];
            pylon.transform.localRotation = Quaternion.identity;
            pylons[i] = pylon.transform;
        }

        jetController.missilePylons = pylons;
        jetWeapon.missilePylons = pylons;

        // Create effects parent
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(jetRoot.transform);
        effects.transform.localPosition = Vector3.zero;

        // Engine flame position
        GameObject engineFlame = new GameObject("EngineFlame");
        engineFlame.transform.SetParent(effects.transform);
        engineFlame.transform.localPosition = new Vector3(0f, 0.5f, -7f);

        // Add booster effect if provided
        if (boosterEffectPrefab != null)
        {
            GameObject booster = (GameObject)PrefabUtility.InstantiatePrefab(boosterEffectPrefab);
            booster.transform.SetParent(engineFlame.transform);
            booster.transform.localPosition = Vector3.zero;
            booster.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            jetController.engineFlameEffect = booster;
        }
        else
        {
            jetController.engineFlameEffect = engineFlame;
        }

        // Contrail positions
        GameObject contrails = new GameObject("Contrails");
        contrails.transform.SetParent(effects.transform);
        contrails.transform.localPosition = new Vector3(0f, 0f, -5f);
        jetController.contrailEffect = contrails;

        // Save as prefab
        string prefabPath = "Assets/Prefabs";
        if (!Directory.Exists(prefabPath))
        {
            Directory.CreateDirectory(prefabPath);
        }

        string fullPath = $"{prefabPath}/Jet_Fighter.prefab";

        // Remove existing prefab if it exists
        if (File.Exists(fullPath))
        {
            AssetDatabase.DeleteAsset(fullPath);
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(jetRoot, fullPath);
        DestroyImmediate(jetRoot);

        EditorUtility.DisplayDialog("Success", $"Jet prefab created at:\n{fullPath}\n\nYou may need to adjust the model position and collider size in the prefab.", "OK");

        // Select the new prefab
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    void CreateJetSpawner()
    {
        // Check if JetSpawner already exists
        JetSpawner existing = FindFirstObjectByType<JetSpawner>();
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Info", "JetSpawner already exists in the scene!", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        GameObject spawnerObj = new GameObject("JetSpawner");
        JetSpawner spawner = spawnerObj.AddComponent<JetSpawner>();

        // Try to find and assign prefabs
        string jetPrefabPath = "Assets/Prefabs/Jet_Fighter.prefab";
        GameObject jetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(jetPrefabPath);
        if (jetPrefab != null)
        {
            spawner.jetPrefab = jetPrefab;
        }

        if (aiPrefab != null)
        {
            spawner.aiPrefab = aiPrefab;
        }

        // Find runways in scene
        Runway[] runways = FindObjectsByType<Runway>(FindObjectsSortMode.None);
        if (runways.Length > 0)
        {
            System.Collections.Generic.List<Runway> phantomRunways = new System.Collections.Generic.List<Runway>();
            System.Collections.Generic.List<Runway> havocRunways = new System.Collections.Generic.List<Runway>();

            foreach (Runway r in runways)
            {
                if (r.assignedTeam == Team.Phantom)
                    phantomRunways.Add(r);
                else if (r.assignedTeam == Team.Havoc)
                    havocRunways.Add(r);
                else
                {
                    // Unassigned - add to both for now
                    phantomRunways.Add(r);
                }
            }

            spawner.phantomRunways = phantomRunways.ToArray();
            spawner.havocRunways = havocRunways.ToArray();

            EditorUtility.DisplayDialog("Info", $"Found {runways.Length} runway(s) in scene.\nAssign teams to runways, then reassign them to the spawner.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Warning", "No Runway components found in scene!\nMake sure to add the Runway component to your runway objects.", "OK");
        }

        Selection.activeGameObject = spawnerObj;
        Undo.RegisterCreatedObjectUndo(spawnerObj, "Create JetSpawner");
    }
}
