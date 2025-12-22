using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

/// <summary>
/// Editor tool to automatically populate WeaponCustomizer with modular weapon parts.
/// </summary>
[CustomEditor(typeof(WeaponCustomizer))]
public class WeaponCustomizerEditor : Editor
{
    private const string MODULAR_WEAPONS_PATH = "Assets/Synty/PolygonMilitary/Prefabs/Weapons/Modular";
    private const string WEAPON_A_PATH = "Assets/Synty/PolygonMilitary/Prefabs/Weapons/Modular/Weapon_A";
    private const string WEAPON_B_PATH = "Assets/Synty/PolygonMilitary/Prefabs/Weapons/Modular/Weapon_B";
    private const string ATTACHMENTS_PATH = "Assets/Synty/PolygonMilitary/Prefabs/Weapons/Modular/Attachments";
    private const string PRESETS_PATH = "Assets/Synty/PolygonMilitary/Prefabs/Weapons/Modular_Presets";

    private bool showWeaponA = true;
    private bool showWeaponB = true;
    private bool showAttachments = true;

    public override void OnInspectorGUI()
    {
        WeaponCustomizer customizer = (WeaponCustomizer)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("Auto-Population Tools", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Populate All", GUILayout.Height(30)))
        {
            AutoPopulateAll(customizer);
        }
        if (GUILayout.Button("Clear All", GUILayout.Height(30)))
        {
            ClearAll(customizer);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Individual section buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Populate Weapon A"))
        {
            PopulateWeaponA(customizer);
        }
        if (GUILayout.Button("Populate Weapon B"))
        {
            PopulateWeaponB(customizer);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Populate Attachments"))
        {
            PopulateAttachments(customizer);
        }
        if (GUILayout.Button("Setup Default Stats"))
        {
            SetupDefaultStats(customizer);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Info section
        EditorGUILayout.HelpBox(
            "Click 'Auto-Populate All' to automatically find and assign all modular weapon parts from:\n" +
            $"  {MODULAR_WEAPONS_PATH}\n\n" +
            "This will populate:\n" +
            "  - Weapon A parts (barrels, grips, stocks, etc.)\n" +
            "  - Weapon B parts\n" +
            "  - Universal attachments (scopes, muzzles, grips)\n" +
            "  - Default stat modifiers",
            MessageType.Info);

        // Show counts
        EditorGUILayout.Space(10);
        showWeaponA = EditorGUILayout.Foldout(showWeaponA, "Weapon A Parts Count");
        if (showWeaponA && customizer.weaponA != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Barrels: {customizer.weaponA.barrels?.Length ?? 0}");
            EditorGUILayout.LabelField($"Grips: {customizer.weaponA.grips?.Length ?? 0}");
            EditorGUILayout.LabelField($"Handguards: {customizer.weaponA.handguards?.Length ?? 0}");
            EditorGUILayout.LabelField($"Stocks: {customizer.weaponA.stocks?.Length ?? 0}");
            EditorGUILayout.LabelField($"Magazines: {customizer.weaponA.magazines?.Length ?? 0}");
            EditorGUILayout.LabelField($"Handles: {customizer.weaponA.handles?.Length ?? 0}");
            EditorGUILayout.LabelField($"Triggers: {customizer.weaponA.triggers?.Length ?? 0}");
            EditorGUI.indentLevel--;
        }

        showWeaponB = EditorGUILayout.Foldout(showWeaponB, "Weapon B Parts Count");
        if (showWeaponB && customizer.weaponB != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Barrels: {customizer.weaponB.barrels?.Length ?? 0}");
            EditorGUILayout.LabelField($"Grips: {customizer.weaponB.grips?.Length ?? 0}");
            EditorGUILayout.LabelField($"Handguards: {customizer.weaponB.handguards?.Length ?? 0}");
            EditorGUILayout.LabelField($"Stocks: {customizer.weaponB.stocks?.Length ?? 0}");
            EditorGUILayout.LabelField($"Magazines: {customizer.weaponB.magazines?.Length ?? 0}");
            EditorGUI.indentLevel--;
        }

        showAttachments = EditorGUILayout.Foldout(showAttachments, "Attachments Count");
        if (showAttachments)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Scopes: {customizer.scopes?.prefabs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Muzzle Devices: {customizer.muzzleDevices?.prefabs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Fore Grips: {customizer.foreGrips?.prefabs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Bipods: {customizer.bipods?.prefabs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Lasers: {customizer.lasers?.prefabs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Flashlights: {customizer.flashlights?.prefabs?.Length ?? 0}");
            EditorGUI.indentLevel--;
        }
    }

    void AutoPopulateAll(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Auto-Populate Weapon Customizer");

        PopulatePresets(customizer);
        PopulateWeaponA(customizer);
        PopulateWeaponB(customizer);
        PopulateAttachments(customizer);
        SetupDefaultStats(customizer);
        FindAndAssignBodyPrefabs(customizer);

        EditorUtility.SetDirty(customizer);
        Debug.Log("[WeaponCustomizerEditor] Auto-population complete!");
    }

    void PopulatePresets(WeaponCustomizer customizer)
    {
        // Find all preset weapons
        string[] guids = AssetDatabase.FindAssets("t:Prefab SM_Wep_Preset", new[] { PRESETS_PATH });
        List<GameObject> presets = new List<GameObject>();
        List<string> names = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                presets.Add(prefab);
                // Clean up name for display
                string name = prefab.name
                    .Replace("SM_Wep_Preset_", "")
                    .Replace("_", " ");
                names.Add(name);
            }
        }

        customizer.weaponPresets = presets.ToArray();
        customizer.presetNames = names.ToArray();

        // Create default stats for each preset
        customizer.presetStats = new WeaponStats[presets.Count];
        for (int i = 0; i < presets.Count; i++)
        {
            customizer.presetStats[i] = CreateStatsForPreset(presets[i].name);
        }

        Debug.Log($"[WeaponCustomizerEditor] Found {presets.Count} weapon presets");
    }

    WeaponStats CreateStatsForPreset(string presetName)
    {
        var stats = new WeaponStats();

        // Vary stats based on weapon type in name
        string lower = presetName.ToLower();

        if (lower.Contains("sniper"))
        {
            stats.damage = 85f;
            stats.fireRate = 1.2f;
            stats.range = 200f;
            stats.accuracy = 0.95f;
            stats.recoilVertical = 4f;
            stats.moveSpeedMultiplier = 0.8f;
            stats.magazineSize = 5;
            stats.aimFOV = 15f;
        }
        else if (lower.Contains("smg"))
        {
            stats.damage = 18f;
            stats.fireRate = 0.06f;
            stats.range = 50f;
            stats.accuracy = 0.75f;
            stats.recoilVertical = 1.5f;
            stats.moveSpeedMultiplier = 1.1f;
            stats.magazineSize = 35;
            stats.aimFOV = 50f;
        }
        else if (lower.Contains("heavy") || lower.Contains("lmg"))
        {
            stats.damage = 35f;
            stats.fireRate = 0.08f;
            stats.range = 100f;
            stats.accuracy = 0.7f;
            stats.recoilVertical = 3f;
            stats.moveSpeedMultiplier = 0.7f;
            stats.magazineSize = 100;
            stats.aimFOV = 40f;
        }
        else if (lower.Contains("shotgun"))
        {
            stats.damage = 120f;
            stats.fireRate = 0.8f;
            stats.range = 25f;
            stats.accuracy = 0.5f;
            stats.recoilVertical = 5f;
            stats.moveSpeedMultiplier = 0.9f;
            stats.magazineSize = 8;
            stats.aimFOV = 55f;
        }
        else // Default rifle stats
        {
            stats.damage = 28f;
            stats.fireRate = 0.1f;
            stats.range = 120f;
            stats.accuracy = 0.85f;
            stats.recoilVertical = 2f;
            stats.moveSpeedMultiplier = 0.95f;
            stats.magazineSize = 30;
            stats.aimFOV = 35f;
        }

        return stats;
    }

    void ClearAll(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Clear Weapon Customizer");

        customizer.weaponA = new WeaponCustomizer.WeaponPlatformConfig { platformName = "Weapon_A" };
        customizer.weaponB = new WeaponCustomizer.WeaponPlatformConfig { platformName = "Weapon_B" };
        customizer.scopes = new WeaponCustomizer.AttachmentCategory { categoryName = "Scopes" };
        customizer.muzzleDevices = new WeaponCustomizer.AttachmentCategory { categoryName = "Muzzle Devices" };
        customizer.foreGrips = new WeaponCustomizer.AttachmentCategory { categoryName = "Fore Grips" };
        customizer.bipods = new WeaponCustomizer.AttachmentCategory { categoryName = "Bipods" };
        customizer.lasers = new WeaponCustomizer.AttachmentCategory { categoryName = "Lasers" };
        customizer.flashlights = new WeaponCustomizer.AttachmentCategory { categoryName = "Flashlights" };

        EditorUtility.SetDirty(customizer);
        Debug.Log("[WeaponCustomizerEditor] Cleared all weapon data");
    }

    void PopulateWeaponA(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Populate Weapon A");

        if (customizer.weaponA == null)
            customizer.weaponA = new WeaponCustomizer.WeaponPlatformConfig();

        customizer.weaponA.platformName = "Weapon_A";

        // Find all Weapon_A parts
        customizer.weaponA.barrels = FindPrefabs(WEAPON_A_PATH, "Barrel");
        customizer.weaponA.grips = FindPrefabs(WEAPON_A_PATH, "Grip");
        customizer.weaponA.handguards = FindPrefabs(WEAPON_A_PATH, "Handguard");
        customizer.weaponA.stocks = FindPrefabs(WEAPON_A_PATH, "Stock");
        customizer.weaponA.magazines = FindPrefabs(WEAPON_A_PATH, "Mag");
        customizer.weaponA.handles = FindPrefabs(WEAPON_A_PATH, "Handle");
        customizer.weaponA.triggers = FindPrefabs(WEAPON_A_PATH, "Trigger");

        // Generate names
        customizer.weaponA.barrelNames = GenerateNames(customizer.weaponA.barrels, "Barrel");
        customizer.weaponA.gripNames = GenerateNames(customizer.weaponA.grips, "Grip");
        customizer.weaponA.handguardNames = GenerateNames(customizer.weaponA.handguards, "Handguard");
        customizer.weaponA.stockNames = GenerateNames(customizer.weaponA.stocks, "Stock");
        customizer.weaponA.magazineNames = GenerateNames(customizer.weaponA.magazines, "Magazine");
        customizer.weaponA.handleNames = GenerateNames(customizer.weaponA.handles, "Handle");
        customizer.weaponA.triggerNames = GenerateNames(customizer.weaponA.triggers, "Trigger");

        EditorUtility.SetDirty(customizer);
        Debug.Log($"[WeaponCustomizerEditor] Populated Weapon A with {CountParts(customizer.weaponA)} parts");
    }

    void PopulateWeaponB(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Populate Weapon B");

        if (customizer.weaponB == null)
            customizer.weaponB = new WeaponCustomizer.WeaponPlatformConfig();

        customizer.weaponB.platformName = "Weapon_B";

        // Find all Weapon_B parts
        customizer.weaponB.barrels = FindPrefabs(WEAPON_B_PATH, "Barrel");
        customizer.weaponB.grips = FindPrefabs(WEAPON_B_PATH, "Grip");
        customizer.weaponB.handguards = FindPrefabs(WEAPON_B_PATH, "Handguard");
        customizer.weaponB.stocks = FindPrefabs(WEAPON_B_PATH, "Stock");
        customizer.weaponB.magazines = FindPrefabs(WEAPON_B_PATH, "Mag");

        // Generate names
        customizer.weaponB.barrelNames = GenerateNames(customizer.weaponB.barrels, "Barrel");
        customizer.weaponB.gripNames = GenerateNames(customizer.weaponB.grips, "Grip");
        customizer.weaponB.handguardNames = GenerateNames(customizer.weaponB.handguards, "Handguard");
        customizer.weaponB.stockNames = GenerateNames(customizer.weaponB.stocks, "Stock");
        customizer.weaponB.magazineNames = GenerateNames(customizer.weaponB.magazines, "Magazine");

        EditorUtility.SetDirty(customizer);
        Debug.Log($"[WeaponCustomizerEditor] Populated Weapon B with {CountParts(customizer.weaponB)} parts");
    }

    void PopulateAttachments(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Populate Attachments");

        // Initialize categories
        if (customizer.scopes == null)
            customizer.scopes = new WeaponCustomizer.AttachmentCategory { categoryName = "Scopes" };
        if (customizer.muzzleDevices == null)
            customizer.muzzleDevices = new WeaponCustomizer.AttachmentCategory { categoryName = "Muzzle Devices" };
        if (customizer.foreGrips == null)
            customizer.foreGrips = new WeaponCustomizer.AttachmentCategory { categoryName = "Fore Grips" };
        if (customizer.bipods == null)
            customizer.bipods = new WeaponCustomizer.AttachmentCategory { categoryName = "Bipods" };
        if (customizer.lasers == null)
            customizer.lasers = new WeaponCustomizer.AttachmentCategory { categoryName = "Lasers" };
        if (customizer.flashlights == null)
            customizer.flashlights = new WeaponCustomizer.AttachmentCategory { categoryName = "Flashlights" };

        // Find attachments - check both Attachments folder and root Modular folder
        string[] searchPaths = { ATTACHMENTS_PATH, MODULAR_WEAPONS_PATH };

        // Scopes (includes Reddot and Scope)
        var scopes = new List<GameObject>();
        scopes.AddRange(FindPrefabsMultiplePaths(searchPaths, "Scope"));
        scopes.AddRange(FindPrefabsMultiplePaths(searchPaths, "Reddot"));
        customizer.scopes.prefabs = scopes.Distinct().ToArray();
        customizer.scopes.displayNames = GenerateNames(customizer.scopes.prefabs, "Scope");

        // Muzzle devices (Silencer, Muzzle)
        var muzzles = new List<GameObject>();
        muzzles.AddRange(FindPrefabsMultiplePaths(searchPaths, "Silencer"));
        muzzles.AddRange(FindPrefabsMultiplePaths(searchPaths, "Muzzle"));
        customizer.muzzleDevices.prefabs = muzzles.Distinct().ToArray();
        customizer.muzzleDevices.displayNames = GenerateNames(customizer.muzzleDevices.prefabs, "Muzzle");

        // Fore grips
        customizer.foreGrips.prefabs = FindPrefabsMultiplePaths(searchPaths, "ForeGrip");
        customizer.foreGrips.displayNames = GenerateNames(customizer.foreGrips.prefabs, "Fore Grip");

        // Bipods
        customizer.bipods.prefabs = FindPrefabsMultiplePaths(searchPaths, "Bipod");
        customizer.bipods.displayNames = GenerateNames(customizer.bipods.prefabs, "Bipod");

        // Lasers
        customizer.lasers.prefabs = FindPrefabsMultiplePaths(searchPaths, "Laser");
        customizer.lasers.displayNames = GenerateNames(customizer.lasers.prefabs, "Laser");

        // Flashlights
        customizer.flashlights.prefabs = FindPrefabsMultiplePaths(searchPaths, "Flashlight");
        customizer.flashlights.displayNames = GenerateNames(customizer.flashlights.prefabs, "Flashlight");

        EditorUtility.SetDirty(customizer);

        int total = (customizer.scopes.prefabs?.Length ?? 0) +
                   (customizer.muzzleDevices.prefabs?.Length ?? 0) +
                   (customizer.foreGrips.prefabs?.Length ?? 0) +
                   (customizer.bipods.prefabs?.Length ?? 0) +
                   (customizer.lasers.prefabs?.Length ?? 0) +
                   (customizer.flashlights.prefabs?.Length ?? 0);

        Debug.Log($"[WeaponCustomizerEditor] Populated {total} attachments");
    }

    void FindAndAssignBodyPrefabs(WeaponCustomizer customizer)
    {
        // Find the actual body prefabs (not presets - presets have all parts already assembled)
        var bodiesA = FindPrefabs(WEAPON_A_PATH, "Body");
        if (bodiesA.Length > 0)
        {
            customizer.weaponA.bodyPrefab = bodiesA[0];
            Debug.Log($"[WeaponCustomizerEditor] Found Weapon A body: {bodiesA[0].name}");
        }
        else
        {
            Debug.LogWarning("[WeaponCustomizerEditor] No body prefab found for Weapon A");
        }

        var bodiesB = FindPrefabs(WEAPON_B_PATH, "Body");
        if (bodiesB.Length > 0)
        {
            customizer.weaponB.bodyPrefab = bodiesB[0];
            Debug.Log($"[WeaponCustomizerEditor] Found Weapon B body: {bodiesB[0].name}");
        }
        else
        {
            Debug.LogWarning("[WeaponCustomizerEditor] No body prefab found for Weapon B");
        }
    }

    void SetupDefaultStats(WeaponCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Setup Default Stats");

        // Setup base stats for both platforms
        customizer.weaponA.baseStats = CreateBaseStats();
        customizer.weaponB.baseStats = CreateBaseStats();

        // Create modifiers for Weapon A parts
        customizer.weaponA.barrelModifiers = CreateBarrelModifiers(customizer.weaponA.barrels?.Length ?? 0);
        customizer.weaponA.gripModifiers = CreateGripModifiers(customizer.weaponA.grips?.Length ?? 0);
        customizer.weaponA.handguardModifiers = CreateHandguardModifiers(customizer.weaponA.handguards?.Length ?? 0);
        customizer.weaponA.stockModifiers = CreateStockModifiers(customizer.weaponA.stocks?.Length ?? 0);
        customizer.weaponA.magazineModifiers = CreateMagazineModifiers(customizer.weaponA.magazines?.Length ?? 0);
        customizer.weaponA.handleModifiers = CreateNeutralModifiers(customizer.weaponA.handles?.Length ?? 0);
        customizer.weaponA.triggerModifiers = CreateNeutralModifiers(customizer.weaponA.triggers?.Length ?? 0);

        // Create modifiers for Weapon B parts
        customizer.weaponB.barrelModifiers = CreateBarrelModifiers(customizer.weaponB.barrels?.Length ?? 0);
        customizer.weaponB.gripModifiers = CreateGripModifiers(customizer.weaponB.grips?.Length ?? 0);
        customizer.weaponB.handguardModifiers = CreateHandguardModifiers(customizer.weaponB.handguards?.Length ?? 0);
        customizer.weaponB.stockModifiers = CreateStockModifiers(customizer.weaponB.stocks?.Length ?? 0);
        customizer.weaponB.magazineModifiers = CreateMagazineModifiers(customizer.weaponB.magazines?.Length ?? 0);

        // Create modifiers for attachments
        customizer.scopes.modifiers = CreateScopeModifiers(customizer.scopes.prefabs?.Length ?? 0);
        customizer.muzzleDevices.modifiers = CreateMuzzleModifiers(customizer.muzzleDevices.prefabs?.Length ?? 0);
        customizer.foreGrips.modifiers = CreateForeGripModifiers(customizer.foreGrips.prefabs?.Length ?? 0);
        customizer.bipods.modifiers = CreateBipodModifiers(customizer.bipods.prefabs?.Length ?? 0);
        customizer.lasers.modifiers = CreateNeutralModifiers(customizer.lasers.prefabs?.Length ?? 0);
        customizer.flashlights.modifiers = CreateNeutralModifiers(customizer.flashlights.prefabs?.Length ?? 0);

        EditorUtility.SetDirty(customizer);
        Debug.Log("[WeaponCustomizerEditor] Setup default stat modifiers");
    }

    #region Helper Methods

    GameObject[] FindPrefabs(string basePath, string partType)
    {
        if (!Directory.Exists(basePath))
        {
            Debug.LogWarning($"[WeaponCustomizerEditor] Path not found: {basePath}");
            return new GameObject[0];
        }

        string searchPattern = $"*{partType}*.prefab";
        var files = Directory.GetFiles(basePath, searchPattern, SearchOption.AllDirectories);

        List<GameObject> prefabs = new List<GameObject>();
        foreach (var file in files)
        {
            string assetPath = file.Replace("\\", "/");
            if (assetPath.StartsWith(Application.dataPath))
            {
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                prefabs.Add(prefab);
            }
        }

        // Sort by name
        prefabs.Sort((a, b) => a.name.CompareTo(b.name));
        return prefabs.ToArray();
    }

    GameObject[] FindPrefabsMultiplePaths(string[] paths, string partType)
    {
        List<GameObject> allPrefabs = new List<GameObject>();
        foreach (var path in paths)
        {
            allPrefabs.AddRange(FindPrefabs(path, partType));
        }
        return allPrefabs.Distinct().ToArray();
    }

    string[] GenerateNames(GameObject[] prefabs, string partType)
    {
        if (prefabs == null) return new string[0];

        return prefabs.Select((p, i) =>
        {
            if (p == null) return $"{partType} {i + 1}";

            string name = p.name;
            // Clean up Synty naming convention
            name = name.Replace("SM_Wep_Mod_A_", "");
            name = name.Replace("SM_Wep_Mod_B_", "");
            name = name.Replace("SM_Wep_Mod_", "");
            name = name.Replace("SM_Wep_", "");
            name = name.Replace("_", " ");

            // Capitalize first letter of each word
            name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());

            return name;
        }).ToArray();
    }

    int CountParts(WeaponCustomizer.WeaponPlatformConfig config)
    {
        if (config == null) return 0;
        return (config.barrels?.Length ?? 0) +
               (config.grips?.Length ?? 0) +
               (config.handguards?.Length ?? 0) +
               (config.stocks?.Length ?? 0) +
               (config.magazines?.Length ?? 0) +
               (config.handles?.Length ?? 0) +
               (config.triggers?.Length ?? 0);
    }

    #endregion

    #region Stat Modifier Generators

    WeaponStats CreateBaseStats()
    {
        return new WeaponStats
        {
            damage = 25f,
            fireRate = 0.1f,
            range = 100f,
            accuracy = 0.9f,
            recoilVertical = 2f,
            recoilHorizontal = 1f,
            adsSpeed = 10f,
            aimFOV = 45f,
            moveSpeedMultiplier = 1f,
            adsMovePenalty = 0.7f,
            magazineSize = 30,
            reloadTime = 2.5f,
            isSilenced = false,
            soundRange = 50f
        };
    }

    AttachmentStatModifier[] CreateBarrelModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Vary stats based on index (simulating short to long barrels)
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;

            // Short barrels: less range/damage, more mobility
            // Long barrels: more range/damage, less mobility
            modifiers[i].rangeModifier = Mathf.Lerp(0.8f, 1.3f, t);
            modifiers[i].damageModifier = Mathf.Lerp(0.95f, 1.1f, t);
            modifiers[i].adsSpeedModifier = Mathf.Lerp(1.1f, 0.85f, t);
            modifiers[i].moveSpeedModifier = Mathf.Lerp(1.05f, 0.95f, t);
            modifiers[i].accuracyModifier = Mathf.Lerp(0.9f, 1.15f, t);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateGripModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();
            // Grips mainly affect recoil
            float variation = 0.85f + (i * 0.05f);
            modifiers[i].recoilModifier = Mathf.Clamp(variation, 0.8f, 1.0f);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateHandguardModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();
            // Handguards mainly cosmetic with slight weight differences
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;
            modifiers[i].moveSpeedModifier = Mathf.Lerp(1.02f, 0.98f, t);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateStockModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Vary from skeleton (fast ADS, more recoil) to full (slow ADS, less recoil)
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;

            modifiers[i].recoilModifier = Mathf.Lerp(1.1f, 0.8f, t);
            modifiers[i].adsSpeedModifier = Mathf.Lerp(1.15f, 0.9f, t);
            modifiers[i].moveSpeedModifier = Mathf.Lerp(1.05f, 0.95f, t);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateMagazineModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Vary magazine sizes
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;

            // Standard (0) to Extended (+20 rounds)
            modifiers[i].magazineSizeBonus = Mathf.RoundToInt(Mathf.Lerp(0, 20, t));
            modifiers[i].reloadTimeModifier = Mathf.Lerp(1f, 1.2f, t); // Longer reload for bigger mags
            modifiers[i].moveSpeedModifier = Mathf.Lerp(1f, 0.98f, t); // Slightly heavier
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateScopeModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Vary from red dot (fast ADS, wide FOV) to sniper scope (slow ADS, narrow FOV)
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;

            modifiers[i].adsSpeedModifier = Mathf.Lerp(1.05f, 0.7f, t);
            modifiers[i].accuracyModifier = Mathf.Lerp(1.05f, 1.3f, t);
            modifiers[i].aimFOVOverride = Mathf.Lerp(55f, 15f, t);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateMuzzleModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Alternate between silencers and muzzle brakes
            if (i % 2 == 0)
            {
                // Silencer
                modifiers[i].addsSilencer = true;
                modifiers[i].damageModifier = 0.95f;
                modifiers[i].recoilModifier = 0.9f;
                modifiers[i].soundRangeModifier = 0.3f;
            }
            else
            {
                // Muzzle brake
                modifiers[i].recoilModifier = 0.85f;
                modifiers[i].soundRangeModifier = 1.2f;
            }
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateForeGripModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();

            // Vary grip types
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;

            // Angled (fast ADS) to Vertical (better recoil)
            modifiers[i].recoilModifier = Mathf.Lerp(0.95f, 0.8f, t);
            modifiers[i].adsSpeedModifier = Mathf.Lerp(1.1f, 0.95f, t);
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateBipodModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier();
            // Bipods reduce recoil significantly but slow ADS
            modifiers[i].recoilModifier = 0.6f;
            modifiers[i].adsSpeedModifier = 0.85f;
            modifiers[i].accuracyModifier = 1.2f;
            modifiers[i].moveSpeedModifier = 0.95f;
        }
        return modifiers;
    }

    AttachmentStatModifier[] CreateNeutralModifiers(int count)
    {
        if (count == 0) return new AttachmentStatModifier[0];

        var modifiers = new AttachmentStatModifier[count];
        for (int i = 0; i < count; i++)
        {
            modifiers[i] = new AttachmentStatModifier(); // All defaults (1.0 multipliers)
        }
        return modifiers;
    }

    #endregion
}

/// <summary>
/// Menu item to create WeaponCustomizer setup wizard.
/// </summary>
public class WeaponCustomizerSetupWizard : EditorWindow
{
    [MenuItem("Tools/Klyra FPS/Setup Weapon Customizer")]
    public static void ShowWindow()
    {
        GetWindow<WeaponCustomizerSetupWizard>("Weapon Customizer Setup");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Weapon Customizer Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This wizard will help you set up the weapon customization system.\n\n" +
            "It will:\n" +
            "1. Find or create a WeaponCustomizer component\n" +
            "2. Auto-populate it with modular weapon parts\n" +
            "3. Set up default stat modifiers",
            MessageType.Info);

        EditorGUILayout.Space(20);

        if (GUILayout.Button("Setup Weapon Customizer", GUILayout.Height(40)))
        {
            SetupWeaponCustomizer();
        }

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Add to Main Menu UI", GUILayout.Height(30)))
        {
            AddToMainMenuUI();
        }
    }

    void SetupWeaponCustomizer()
    {
        // Find existing or create new
        WeaponCustomizer customizer = FindFirstObjectByType<WeaponCustomizer>();

        if (customizer == null)
        {
            // Try to find MainMenuUI
            MainMenuUI menuUI = FindFirstObjectByType<MainMenuUI>();
            if (menuUI != null)
            {
                customizer = menuUI.gameObject.AddComponent<WeaponCustomizer>();
                Debug.Log("[WeaponCustomizerSetupWizard] Added WeaponCustomizer to MainMenuUI");
            }
            else
            {
                // Create a new GameObject
                GameObject obj = new GameObject("WeaponCustomizer");
                customizer = obj.AddComponent<WeaponCustomizer>();
                Debug.Log("[WeaponCustomizerSetupWizard] Created new WeaponCustomizer GameObject");
            }
        }

        // Select it
        Selection.activeGameObject = customizer.gameObject;

        // Auto-populate
        var editor = Editor.CreateEditor(customizer) as WeaponCustomizerEditor;
        if (editor != null)
        {
            // Use reflection to call private method
            var method = typeof(WeaponCustomizerEditor).GetMethod("AutoPopulateAll",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(editor, new object[] { customizer });
            DestroyImmediate(editor);
        }

        EditorUtility.DisplayDialog("Setup Complete",
            "WeaponCustomizer has been set up and populated with weapon parts.\n\n" +
            "Select the WeaponCustomizer in the Inspector to see the configuration.",
            "OK");
    }

    void AddToMainMenuUI()
    {
        MainMenuUI menuUI = FindFirstObjectByType<MainMenuUI>();
        if (menuUI == null)
        {
            EditorUtility.DisplayDialog("Error", "MainMenuUI not found in scene!", "OK");
            return;
        }

        WeaponCustomizer customizer = menuUI.GetComponent<WeaponCustomizer>();
        if (customizer == null)
        {
            customizer = menuUI.gameObject.AddComponent<WeaponCustomizer>();
        }

        // Assign to MainMenuUI
        SerializedObject so = new SerializedObject(menuUI);
        var prop = so.FindProperty("weaponCustomizer");
        if (prop != null)
        {
            prop.objectReferenceValue = customizer;
            so.ApplyModifiedProperties();
        }

        Selection.activeGameObject = menuUI.gameObject;

        EditorUtility.DisplayDialog("Success",
            "WeaponCustomizer has been added to MainMenuUI.\n\n" +
            "Click 'Auto-Populate All' in the WeaponCustomizer inspector to load weapon parts.",
            "OK");
    }
}
