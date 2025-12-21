using UnityEngine;
using UnityEditor;

public class HumveeSetupHelper : EditorWindow
{
    [MenuItem("Tools/Setup Humvee Prefab")]
    public static void SetupHumvee()
    {
        // Find the light armored car model (exclude destroyed variants)
        string[] guids = AssetDatabase.FindAssets("SM_Veh_Light_Armored_Car_01 t:Prefab");

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "Could not find SM_Veh_Light_Armored_Car_01 prefab!", "OK");
            return;
        }

        // Find the non-destroyed version
        string path = null;
        foreach (string guid in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            // Skip destroyed variants
            if (p.ToLower().Contains("destroyed") || p.ToLower().Contains("wreck"))
                continue;
            path = p;
            break;
        }

        if (path == null)
        {
            // Fallback to first one if no non-destroyed found
            path = AssetDatabase.GUIDToAssetPath(guids[0]);
        }

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (sourcePrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load prefab!", "OK");
            return;
        }

        Debug.Log($"[HumveeSetup] Using prefab: {path}");

        // Instantiate in scene
        GameObject humvee = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        humvee.name = "Humvee_USA";

        // Unpack prefab so we can modify it
        PrefabUtility.UnpackPrefabInstance(humvee, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Add HumveeController
        HumveeController controller = humvee.GetComponent<HumveeController>();
        if (controller == null)
            controller = humvee.AddComponent<HumveeController>();

        // Add Rigidbody if needed (HumveeController requires it but let's make sure it's configured)
        Rigidbody rb = humvee.GetComponent<Rigidbody>();
        if (rb == null)
            rb = humvee.AddComponent<Rigidbody>();
        rb.mass = 3500f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 3f;

        // Add BoxCollider if no collider exists
        if (humvee.GetComponent<Collider>() == null)
        {
            BoxCollider col = humvee.AddComponent<BoxCollider>();
            // Approximate humvee size
            col.size = new Vector3(2.2f, 2f, 4.5f);
            col.center = new Vector3(0f, 1f, 0f);
        }

        // Find existing gun/turret meshes in the Synty model
        Transform existingGunMesh = null;
        Transform existingTurretBase = null;
        foreach (Transform child in humvee.GetComponentsInChildren<Transform>())
        {
            string name = child.name.ToLower();
            // Look for the gun mesh (usually has "gun" or "turret" in the name)
            if (name.Contains("gun") || name.Contains("turret") || name.Contains("weapon") || name.Contains("mount"))
            {
                if (existingGunMesh == null)
                    existingGunMesh = child;
                if (name.Contains("base") || name.Contains("ring") || name.Contains("mount"))
                    existingTurretBase = child;
            }
        }

        // Create turret pivot point
        Transform turret = humvee.transform.Find("TurretPivot");
        if (turret == null)
        {
            GameObject turretObj = new GameObject("TurretPivot");
            turretObj.transform.SetParent(humvee.transform);
            // Position at top of vehicle
            turretObj.transform.localPosition = new Vector3(0f, 1.8f, -0.3f);
            turretObj.transform.localRotation = Quaternion.identity;
            turret = turretObj.transform;
        }

        // Create gun barrel pivot (for pitch)
        Transform gunBarrel = turret.Find("GunBarrel");
        if (gunBarrel == null)
        {
            GameObject barrelObj = new GameObject("GunBarrel");
            barrelObj.transform.SetParent(turret);
            barrelObj.transform.localPosition = Vector3.zero;
            barrelObj.transform.localRotation = Quaternion.identity;
            gunBarrel = barrelObj.transform;
        }

        // If we found an existing gun mesh, re-parent it under our gun barrel
        if (existingGunMesh != null && existingGunMesh.parent != gunBarrel)
        {
            Vector3 worldPos = existingGunMesh.position;
            Quaternion worldRot = existingGunMesh.rotation;

            // Move turret pivot to where the gun currently is
            turret.position = worldPos;

            // Re-parent the gun mesh under the barrel
            existingGunMesh.SetParent(gunBarrel);
            existingGunMesh.localPosition = Vector3.zero;
            existingGunMesh.localRotation = Quaternion.identity;

            Debug.Log($"[HumveeSetup] Re-parented {existingGunMesh.name} under GunBarrel");
        }

        controller.turret = turret;
        controller.gunBarrel = gunBarrel;

        // Create fire point at end of gun barrel
        Transform firePoint = gunBarrel.Find("FirePoint");
        if (firePoint == null)
        {
            GameObject firePointObj = new GameObject("FirePoint");
            firePointObj.transform.SetParent(gunBarrel);
            firePointObj.transform.localPosition = new Vector3(0f, 0.1f, 1.5f);
            firePointObj.transform.localRotation = Quaternion.identity;
            firePoint = firePointObj.transform;
        }

        controller.firePoint = firePoint;

        // Create driver seat
        Transform driverSeat = humvee.transform.Find("DriverSeat");
        if (driverSeat == null)
        {
            GameObject seatObj = new GameObject("DriverSeat");
            seatObj.transform.SetParent(humvee.transform);
            seatObj.transform.localPosition = new Vector3(-0.5f, 1f, 0.8f);
            driverSeat = seatObj.transform;
        }
        controller.driverSeat = driverSeat;

        // Create gunner seat
        Transform gunnerSeat = humvee.transform.Find("GunnerSeat");
        if (gunnerSeat == null)
        {
            GameObject seatObj = new GameObject("GunnerSeat");
            seatObj.transform.SetParent(humvee.transform);
            seatObj.transform.localPosition = new Vector3(0f, 1.8f, -0.3f);
            gunnerSeat = seatObj.transform;
        }
        controller.gunnerSeat = gunnerSeat;

        // Create passenger seats
        Transform[] passengerSeats = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            string seatName = $"PassengerSeat_{i}";
            Transform seat = humvee.transform.Find(seatName);
            if (seat == null)
            {
                GameObject seatObj = new GameObject(seatName);
                seatObj.transform.SetParent(humvee.transform);
                seatObj.transform.localPosition = new Vector3(i == 0 ? -0.5f : 0.5f, 1f, -0.8f);
                seat = seatObj.transform;
            }
            passengerSeats[i] = seat;
        }
        controller.passengerSeats = passengerSeats;

        // Set team
        controller.humveeTeam = Team.Phantom;

        // Find wheels
        foreach (Transform child in humvee.GetComponentsInChildren<Transform>())
        {
            string name = child.name.ToLower();
            if (name.Contains("wheel") && name.Contains("front") && name.Contains("left"))
                controller.frontLeftWheel = child;
            else if (name.Contains("wheel") && name.Contains("front") && name.Contains("right"))
                controller.frontRightWheel = child;
        }

        // Try to find explosion prefab
        string[] explosionGuids = AssetDatabase.FindAssets("FX_Explosion t:Prefab");
        if (explosionGuids.Length > 0)
        {
            string explosionPath = AssetDatabase.GUIDToAssetPath(explosionGuids[0]);
            controller.explosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(explosionPath);
        }

        // Try to find bullet prefab (use TankShell if available)
        string[] bulletGuids = AssetDatabase.FindAssets("TankShell t:Prefab");
        if (bulletGuids.Length > 0)
        {
            string bulletPath = AssetDatabase.GUIDToAssetPath(bulletGuids[0]);
            controller.bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bulletPath);
        }

        // Add HumveeNavigation for waypoint-based navigation
        HumveeNavigation nav = humvee.GetComponent<HumveeNavigation>();
        if (nav == null)
        {
            nav = humvee.AddComponent<HumveeNavigation>();
        }

        // Save as new prefab in Resources folder
        string savePath = "Assets/Resources/Humvee_USA.prefab";

        // Create Resources folder if it doesn't exist
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        // Save prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(humvee, savePath);

        Selection.activeGameObject = humvee;

        EditorUtility.DisplayDialog("Success",
            $"Humvee prefab created!\n\n" +
            $"Saved to: {savePath}\n\n" +
            $"The Humvee is in your scene - you can adjust positions if needed, then:\n" +
            $"1. Run Tools > Create Humvee Spawner\n" +
            $"2. Position the spawner where you want Humvees\n" +
            $"3. Make sure AI prefab is assigned", "OK");

        Debug.Log($"[HumveeSetup] Created Humvee prefab at {savePath}");
    }

    [MenuItem("Tools/Create Humvee Spawner")]
    public static void CreateHumveeSpawner()
    {
        // Find the humvee prefab in Resources
        GameObject humveePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Humvee_USA.prefab");

        // Fallback: search for it anywhere
        if (humveePrefab == null)
        {
            string[] guids = AssetDatabase.FindAssets("Humvee_USA t:Prefab");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                humveePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }

        // Find AI prefab
        string[] aiGuids = AssetDatabase.FindAssets("AI_Soldier t:Prefab");
        GameObject aiPrefab = null;

        if (aiGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(aiGuids[0]);
            aiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // If no AI_Soldier, try to find any prefab with AIController
        if (aiPrefab == null)
        {
            string[] allPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string guid in allPrefabs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<AIController>() != null)
                {
                    aiPrefab = prefab;
                    break;
                }
            }
        }

        // Create spawner
        GameObject spawnerObj = new GameObject("HumveeSpawner_Phantom");
        HumveeSpawner spawner = spawnerObj.AddComponent<HumveeSpawner>();

        spawner.humveePrefab = humveePrefab;
        spawner.aiPrefab = aiPrefab;
        spawner.spawnTeam = Team.Phantom;
        spawner.humveesToSpawn = 1;
        spawner.spawnDriver = true;
        spawner.spawnGunner = true;

        // Position at origin or near camera
        if (SceneView.lastActiveSceneView != null)
        {
            spawnerObj.transform.position = SceneView.lastActiveSceneView.pivot;
        }

        Selection.activeGameObject = spawnerObj;

        string message = "Humvee Spawner created!\n\n";
        if (humveePrefab == null)
            message += "WARNING: Humvee prefab not found - run 'Tools > Setup Humvee Prefab' first!\n";
        if (aiPrefab == null)
            message += "WARNING: AI prefab not found - assign manually!\n";

        message += "\nPosition the spawner where you want Humvees to appear.";

        EditorUtility.DisplayDialog("Spawner Created", message, "OK");
    }
}
