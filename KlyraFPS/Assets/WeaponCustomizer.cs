using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages weapon customization data and loadouts.
/// Follows the same pattern as CharacterCustomizer.
/// </summary>
public class WeaponCustomizer : MonoBehaviour
{
    [System.Serializable]
    public class WeaponPlatformConfig
    {
        public string platformName = "Weapon_A";
        public GameObject bodyPrefab;
        public WeaponStats baseStats = new WeaponStats();

        [Header("Core Parts")]
        public GameObject[] barrels;
        public string[] barrelNames;
        public AttachmentStatModifier[] barrelModifiers;

        public GameObject[] grips;
        public string[] gripNames;
        public AttachmentStatModifier[] gripModifiers;

        public GameObject[] handguards;
        public string[] handguardNames;
        public AttachmentStatModifier[] handguardModifiers;

        public GameObject[] stocks;
        public string[] stockNames;
        public AttachmentStatModifier[] stockModifiers;

        public GameObject[] magazines;
        public string[] magazineNames;
        public AttachmentStatModifier[] magazineModifiers;

        public GameObject[] handles;        // Weapon_A only
        public string[] handleNames;
        public AttachmentStatModifier[] handleModifiers;

        public GameObject[] triggers;       // Weapon_A only
        public string[] triggerNames;
        public AttachmentStatModifier[] triggerModifiers;
    }

    [System.Serializable]
    public class AttachmentCategory
    {
        public string categoryName;
        public GameObject[] prefabs;
        public string[] displayNames;
        public AttachmentStatModifier[] modifiers;
        public Sprite[] icons;
    }

    [Header("Weapon Platforms")]
    public WeaponPlatformConfig weaponA;
    public WeaponPlatformConfig weaponB;

    [Header("Universal Attachments")]
    public AttachmentCategory scopes;
    public AttachmentCategory muzzleDevices;
    public AttachmentCategory foreGrips;
    public AttachmentCategory bipods;
    public AttachmentCategory lasers;
    public AttachmentCategory flashlights;

    [Header("Weapon Presets (Complete Weapons)")]
    public GameObject[] weaponPresets;
    public string[] presetNames;
    public WeaponStats[] presetStats;

    [Header("Preview Settings")]
    public float rotationSpeed = 30f;

    // Runtime data
    private PlayerLoadouts playerLoadouts = new PlayerLoadouts();
    private int currentEditingLoadout = 0;
    private int currentPresetIndex = 0;
    private GameObject currentPreviewWeapon;

    private static WeaponCustomizer instance;

    // Reset static instance when scripts reload (editor only)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic()
    {
        instance = null;
    }

    // Static flag to indicate we're creating a copy (set before AddComponent, checked in Awake)
    private static bool creatingCopy = false;
    private bool isCopyInstance = false;

    void Awake()
    {
        // If we're being created as a copy, just mark ourselves and return
        if (creatingCopy)
        {
            isCopyInstance = true;
            creatingCopy = false; // Reset the flag
            return;
        }

        // Singleton pattern - persist across scenes
        // Check if instance exists AND is not destroyed (important for editor play mode)
        if (instance != null && instance != this && instance.gameObject != null)
        {
            // Only destroy this component, not the whole GameObject
            Destroy(this);
            return;
        }

        // If this component is on a shared GameObject (like MainMenuUI),
        // move to a dedicated persistent object
        if (GetComponent<MainMenuUI>() != null || gameObject.name.Contains("Menu"))
        {
            // Create a dedicated persistent object for WeaponCustomizer
            GameObject persistentObj = new GameObject("WeaponCustomizer_Persistent");

            // Set flag BEFORE AddComponent so new Awake knows to skip init
            creatingCopy = true;
            WeaponCustomizer newInstance = persistentObj.AddComponent<WeaponCustomizer>();

            // Copy all settings to the new instance
            CopySettingsTo(newInstance);

            // Initialize the copy manually after settings are copied
            newInstance.InitializeAfterCopy();

            // The new instance will become the singleton
            DontDestroyOnLoad(persistentObj);
            instance = newInstance;

            Debug.Log("[WeaponCustomizer] Moved to dedicated persistent object");

            // Destroy this component (not the whole GameObject)
            Destroy(this);
            return;
        }

        instance = this;

        // Persist across scene loads so weapon data is available in game scene
        DontDestroyOnLoad(gameObject);

        InitializeData();
    }

    void InitializeData()
    {
        // Initialize platform configs if needed
        if (weaponA == null)
        {
            weaponA = new WeaponPlatformConfig { platformName = "Weapon_A" };
        }
        if (weaponB == null)
        {
            weaponB = new WeaponPlatformConfig { platformName = "Weapon_B" };
        }

        LoadAllLoadouts();

        Debug.Log($"[WeaponCustomizer] Initialized with {PlayerLoadouts.MAX_LOADOUT_SLOTS} loadout slots, {weaponPresets?.Length ?? 0} presets");
    }

    void InitializeAfterCopy()
    {
        // Initialize platform configs if they weren't copied
        if (weaponA == null)
        {
            weaponA = new WeaponPlatformConfig { platformName = "Weapon_A" };
        }
        if (weaponB == null)
        {
            weaponB = new WeaponPlatformConfig { platformName = "Weapon_B" };
        }

        LoadAllLoadouts();

        Debug.Log($"[WeaponCustomizer] Copy initialized with {PlayerLoadouts.MAX_LOADOUT_SLOTS} loadout slots, {weaponPresets?.Length ?? 0} presets");
    }

    void CopySettingsTo(WeaponCustomizer target)
    {
        target.weaponA = this.weaponA;
        target.weaponB = this.weaponB;
        target.scopes = this.scopes;
        target.muzzleDevices = this.muzzleDevices;
        target.foreGrips = this.foreGrips;
        target.bipods = this.bipods;
        target.lasers = this.lasers;
        target.flashlights = this.flashlights;
        target.weaponPresets = this.weaponPresets;
        target.presetNames = this.presetNames;
        target.presetStats = this.presetStats;
        target.rotationSpeed = this.rotationSpeed;
    }

    public static WeaponCustomizer Instance => instance;

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    #region Save/Load

    public void LoadAllLoadouts()
    {
        for (int i = 0; i < PlayerLoadouts.MAX_LOADOUT_SLOTS; i++)
        {
            string key = $"Loadout_{i}";
            string json = PlayerPrefs.GetString(key, "");

            if (!string.IsNullOrEmpty(json))
            {
                playerLoadouts.loadouts[i] = new LoadoutData
                {
                    loadoutName = $"Loadout {i + 1}",
                    primaryWeapon = WeaponBuildData.FromJson(json)
                };
            }
            else
            {
                playerLoadouts.loadouts[i] = new LoadoutData
                {
                    loadoutName = $"Loadout {i + 1}",
                    primaryWeapon = WeaponBuildData.CreateDefault()
                };
            }
        }

        playerLoadouts.selectedLoadoutIndex = PlayerPrefs.GetInt("SelectedLoadoutIndex", 0);
        Debug.Log($"[WeaponCustomizer] Loaded {PlayerLoadouts.MAX_LOADOUT_SLOTS} loadouts, active: {playerLoadouts.selectedLoadoutIndex}");
    }

    public void SaveAllLoadouts()
    {
        for (int i = 0; i < PlayerLoadouts.MAX_LOADOUT_SLOTS; i++)
        {
            string key = $"Loadout_{i}";
            string json = playerLoadouts.loadouts[i].primaryWeapon.ToJson();
            PlayerPrefs.SetString(key, json);
        }

        PlayerPrefs.SetInt("SelectedLoadoutIndex", playerLoadouts.selectedLoadoutIndex);

        // Also save active loadout for quick access in-game
        PlayerPrefs.SetString("ActiveLoadout", playerLoadouts.ActiveWeaponBuild.ToJson());

        PlayerPrefs.Save();
        Debug.Log($"[WeaponCustomizer] Saved all loadouts");
    }

    #endregion

    #region Loadout Management

    public int GetLoadoutCount() => PlayerLoadouts.MAX_LOADOUT_SLOTS;

    public int GetCurrentLoadoutIndex() => currentEditingLoadout;

    public void SetCurrentLoadout(int index)
    {
        if (index >= 0 && index < PlayerLoadouts.MAX_LOADOUT_SLOTS)
        {
            currentEditingLoadout = index;
            // Sync currentPresetIndex with the loadout's preset
            currentPresetIndex = GetCurrentBuild().presetIndex;
            Debug.Log($"[WeaponCustomizer] Switched to editing loadout {index}, preset {currentPresetIndex}");
        }
    }

    public void SetActiveLoadout(int index)
    {
        if (index >= 0 && index < PlayerLoadouts.MAX_LOADOUT_SLOTS)
        {
            playerLoadouts.selectedLoadoutIndex = index;
            Debug.Log($"[WeaponCustomizer] Set active loadout to {index}");
        }
    }

    public int GetActiveLoadoutIndex() => playerLoadouts.selectedLoadoutIndex;

    public WeaponBuildData GetCurrentBuild()
    {
        return playerLoadouts.loadouts[currentEditingLoadout].primaryWeapon;
    }

    public WeaponBuildData GetActiveBuild()
    {
        return playerLoadouts.ActiveWeaponBuild;
    }

    public string GetLoadoutName(int index)
    {
        if (index >= 0 && index < PlayerLoadouts.MAX_LOADOUT_SLOTS)
        {
            return playerLoadouts.loadouts[index].loadoutName;
        }
        return "Unknown";
    }

    #endregion

    #region Preset Selection

    public int GetPresetCount()
    {
        return weaponPresets?.Length ?? 0;
    }

    public int GetCurrentPresetIndex()
    {
        return currentPresetIndex;
    }

    public void SetPresetIndex(int index)
    {
        if (weaponPresets != null && weaponPresets.Length > 0)
        {
            currentPresetIndex = Mathf.Clamp(index, 0, weaponPresets.Length - 1);
            // Save to current loadout
            GetCurrentBuild().presetIndex = currentPresetIndex;
        }
    }

    public void CyclePreset(int direction)
    {
        if (weaponPresets == null || weaponPresets.Length == 0) return;

        currentPresetIndex += direction;
        if (currentPresetIndex >= weaponPresets.Length) currentPresetIndex = 0;
        if (currentPresetIndex < 0) currentPresetIndex = weaponPresets.Length - 1;

        GetCurrentBuild().presetIndex = currentPresetIndex;
    }

    public string GetPresetName(int index)
    {
        if (presetNames != null && index >= 0 && index < presetNames.Length)
        {
            return presetNames[index];
        }
        if (weaponPresets != null && index >= 0 && index < weaponPresets.Length && weaponPresets[index] != null)
        {
            return CleanPresetName(weaponPresets[index].name);
        }
        return "Unknown";
    }

    public string GetCurrentPresetName()
    {
        return GetPresetName(currentPresetIndex);
    }

    public GameObject GetCurrentPresetPrefab()
    {
        if (weaponPresets != null && currentPresetIndex >= 0 && currentPresetIndex < weaponPresets.Length)
        {
            return weaponPresets[currentPresetIndex];
        }
        return null;
    }

    public WeaponStats GetPresetStats(int index)
    {
        if (presetStats != null && index >= 0 && index < presetStats.Length)
        {
            return presetStats[index];
        }
        // Return default stats
        return new WeaponStats();
    }

    string CleanPresetName(string name)
    {
        name = name.Replace("SM_Wep_Preset_", "");
        name = name.Replace("SM_Wep_", "");
        name = name.Replace("_", " ");
        return name;
    }

    #endregion

    #region Part Selection

    public WeaponPlatformConfig GetPlatformConfig(string platform)
    {
        return platform == "Weapon_A" ? weaponA : weaponB;
    }

    public string GetCurrentPlatform()
    {
        return GetCurrentBuild().platform;
    }

    public void SetPlatform(string platform)
    {
        GetCurrentBuild().platform = platform;
    }

    public void TogglePlatform()
    {
        var build = GetCurrentBuild();
        build.platform = build.platform == "Weapon_A" ? "Weapon_B" : "Weapon_A";

        // Reset platform-specific parts when switching
        build.barrelIndex = 0;
        build.gripIndex = 0;
        build.handguardIndex = 0;
        build.stockIndex = 0;
        build.magazineIndex = 0;
        build.handleIndex = -1;
        build.triggerIndex = -1;
    }

    public int GetPartSelection(string category)
    {
        var build = GetCurrentBuild();

        switch (category)
        {
            case "Barrel": return build.barrelIndex;
            case "Grip": return build.gripIndex;
            case "Handguard": return build.handguardIndex;
            case "Stock": return build.stockIndex;
            case "Magazine": return build.magazineIndex;
            case "Handle": return build.handleIndex;
            case "Trigger": return build.triggerIndex;
            case "Scope": return build.scopeIndex;
            case "Muzzle": return build.muzzleIndex;
            case "ForeGrip": return build.foreGripIndex;
            case "Bipod": return build.bipodIndex;
            case "Laser": return build.laserIndex;
            case "Flashlight": return build.flashlightIndex;
            default: return -1;
        }
    }

    public void SetPartSelection(string category, int index)
    {
        var build = GetCurrentBuild();

        switch (category)
        {
            case "Barrel": build.barrelIndex = index; break;
            case "Grip": build.gripIndex = index; break;
            case "Handguard": build.handguardIndex = index; break;
            case "Stock": build.stockIndex = index; break;
            case "Magazine": build.magazineIndex = index; break;
            case "Handle": build.handleIndex = index; break;
            case "Trigger": build.triggerIndex = index; break;
            case "Scope": build.scopeIndex = index; break;
            case "Muzzle": build.muzzleIndex = index; break;
            case "ForeGrip": build.foreGripIndex = index; break;
            case "Bipod": build.bipodIndex = index; break;
            case "Laser": build.laserIndex = index; break;
            case "Flashlight": build.flashlightIndex = index; break;
        }
    }

    public void CyclePartSelection(string category, int direction)
    {
        int current = GetPartSelection(category);
        int count = GetPartCount(category);

        if (count == 0) return;

        // Core parts always have a selection, attachments can be "None" (-1)
        bool isCorePart = category == "Barrel" || category == "Grip" ||
                          category == "Handguard" || category == "Stock" || category == "Magazine";

        int minValue = isCorePart ? 0 : -1;
        int maxValue = count - 1;

        current += direction;

        if (current > maxValue) current = minValue;
        if (current < minValue) current = maxValue;

        SetPartSelection(category, current);
    }

    public int GetPartCount(string category)
    {
        var platform = GetPlatformConfig(GetCurrentPlatform());

        switch (category)
        {
            case "Barrel": return platform.barrels?.Length ?? 0;
            case "Grip": return platform.grips?.Length ?? 0;
            case "Handguard": return platform.handguards?.Length ?? 0;
            case "Stock": return platform.stocks?.Length ?? 0;
            case "Magazine": return platform.magazines?.Length ?? 0;
            case "Handle": return platform.handles?.Length ?? 0;
            case "Trigger": return platform.triggers?.Length ?? 0;
            case "Scope": return scopes?.prefabs?.Length ?? 0;
            case "Muzzle": return muzzleDevices?.prefabs?.Length ?? 0;
            case "ForeGrip": return foreGrips?.prefabs?.Length ?? 0;
            case "Bipod": return bipods?.prefabs?.Length ?? 0;
            case "Laser": return lasers?.prefabs?.Length ?? 0;
            case "Flashlight": return flashlights?.prefabs?.Length ?? 0;
            default: return 0;
        }
    }

    public string GetPartName(string category, int index)
    {
        if (index < 0) return "None";

        var platform = GetPlatformConfig(GetCurrentPlatform());
        string[] names = null;
        GameObject[] prefabs = null;

        switch (category)
        {
            case "Barrel":
                names = platform.barrelNames;
                prefabs = platform.barrels;
                break;
            case "Grip":
                names = platform.gripNames;
                prefabs = platform.grips;
                break;
            case "Handguard":
                names = platform.handguardNames;
                prefabs = platform.handguards;
                break;
            case "Stock":
                names = platform.stockNames;
                prefabs = platform.stocks;
                break;
            case "Magazine":
                names = platform.magazineNames;
                prefabs = platform.magazines;
                break;
            case "Handle":
                names = platform.handleNames;
                prefabs = platform.handles;
                break;
            case "Trigger":
                names = platform.triggerNames;
                prefabs = platform.triggers;
                break;
            case "Scope":
                names = scopes?.displayNames;
                prefabs = scopes?.prefabs;
                break;
            case "Muzzle":
                names = muzzleDevices?.displayNames;
                prefabs = muzzleDevices?.prefabs;
                break;
            case "ForeGrip":
                names = foreGrips?.displayNames;
                prefabs = foreGrips?.prefabs;
                break;
            case "Bipod":
                names = bipods?.displayNames;
                prefabs = bipods?.prefabs;
                break;
            case "Laser":
                names = lasers?.displayNames;
                prefabs = lasers?.prefabs;
                break;
            case "Flashlight":
                names = flashlights?.displayNames;
                prefabs = flashlights?.prefabs;
                break;
        }

        if (names != null && index < names.Length && !string.IsNullOrEmpty(names[index]))
            return names[index];

        if (prefabs != null && index < prefabs.Length && prefabs[index] != null)
            return CleanPrefabName(prefabs[index].name);

        return "Unknown";
    }

    public GameObject GetPartPrefab(string category, int index)
    {
        if (index < 0) return null;

        var platform = GetPlatformConfig(GetCurrentPlatform());
        GameObject[] prefabs = null;

        switch (category)
        {
            case "Barrel": prefabs = platform.barrels; break;
            case "Grip": prefabs = platform.grips; break;
            case "Handguard": prefabs = platform.handguards; break;
            case "Stock": prefabs = platform.stocks; break;
            case "Magazine": prefabs = platform.magazines; break;
            case "Handle": prefabs = platform.handles; break;
            case "Trigger": prefabs = platform.triggers; break;
            case "Scope": prefabs = scopes?.prefabs; break;
            case "Muzzle": prefabs = muzzleDevices?.prefabs; break;
            case "ForeGrip": prefabs = foreGrips?.prefabs; break;
            case "Bipod": prefabs = bipods?.prefabs; break;
            case "Laser": prefabs = lasers?.prefabs; break;
            case "Flashlight": prefabs = flashlights?.prefabs; break;
        }

        if (prefabs != null && index < prefabs.Length)
            return prefabs[index];

        return null;
    }

    public AttachmentStatModifier GetPartModifier(string category, int index)
    {
        if (index < 0) return null;

        var platform = GetPlatformConfig(GetCurrentPlatform());
        AttachmentStatModifier[] mods = null;

        switch (category)
        {
            case "Barrel": mods = platform.barrelModifiers; break;
            case "Grip": mods = platform.gripModifiers; break;
            case "Handguard": mods = platform.handguardModifiers; break;
            case "Stock": mods = platform.stockModifiers; break;
            case "Magazine": mods = platform.magazineModifiers; break;
            case "Handle": mods = platform.handleModifiers; break;
            case "Trigger": mods = platform.triggerModifiers; break;
            case "Scope": mods = scopes?.modifiers; break;
            case "Muzzle": mods = muzzleDevices?.modifiers; break;
            case "ForeGrip": mods = foreGrips?.modifiers; break;
            case "Bipod": mods = bipods?.modifiers; break;
            case "Laser": mods = lasers?.modifiers; break;
            case "Flashlight": mods = flashlights?.modifiers; break;
        }

        if (mods != null && index < mods.Length)
            return mods[index];

        return null;
    }

    string CleanPrefabName(string name)
    {
        name = name.Replace("SM_Wep_Mod_", "");
        name = name.Replace("SM_Wep_", "");
        name = name.Replace("_", " ");
        return name;
    }

    #endregion

    #region Stats Calculation

    public WeaponStats CalculateStats(WeaponBuildData build)
    {
        var platform = GetPlatformConfig(build.platform);
        var stats = platform.baseStats.Clone();

        // Collect all modifiers
        List<AttachmentStatModifier> modifiers = new List<AttachmentStatModifier>();

        // Platform-specific parts
        AddModifierIfValid(modifiers, platform.barrelModifiers, build.barrelIndex);
        AddModifierIfValid(modifiers, platform.gripModifiers, build.gripIndex);
        AddModifierIfValid(modifiers, platform.handguardModifiers, build.handguardIndex);
        AddModifierIfValid(modifiers, platform.stockModifiers, build.stockIndex);
        AddModifierIfValid(modifiers, platform.magazineModifiers, build.magazineIndex);
        AddModifierIfValid(modifiers, platform.handleModifiers, build.handleIndex);
        AddModifierIfValid(modifiers, platform.triggerModifiers, build.triggerIndex);

        // Universal attachments
        AddModifierIfValid(modifiers, scopes?.modifiers, build.scopeIndex);
        AddModifierIfValid(modifiers, muzzleDevices?.modifiers, build.muzzleIndex);
        AddModifierIfValid(modifiers, foreGrips?.modifiers, build.foreGripIndex);
        AddModifierIfValid(modifiers, bipods?.modifiers, build.bipodIndex);
        AddModifierIfValid(modifiers, lasers?.modifiers, build.laserIndex);
        AddModifierIfValid(modifiers, flashlights?.modifiers, build.flashlightIndex);

        // Apply all modifiers
        foreach (var mod in modifiers)
        {
            stats.ApplyModifier(mod);
        }

        return stats;
    }

    void AddModifierIfValid(List<AttachmentStatModifier> list, AttachmentStatModifier[] mods, int index)
    {
        if (index >= 0 && mods != null && index < mods.Length && mods[index] != null)
        {
            list.Add(mods[index]);
        }
    }

    public WeaponStats GetCurrentStats()
    {
        // Use preset stats if available
        if (presetStats != null && currentPresetIndex >= 0 && currentPresetIndex < presetStats.Length)
        {
            return presetStats[currentPresetIndex];
        }
        // Fallback to calculated stats
        return CalculateStats(GetCurrentBuild());
    }

    #endregion

    #region Preview

    public GameObject CreatePreviewWeapon(Transform parent)
    {
        ClearPreview();

        // Use preset-based approach (simpler, guaranteed to look correct)
        var presetPrefab = GetCurrentPresetPrefab();

        if (presetPrefab == null)
        {
            Debug.LogWarning("[WeaponCustomizer] No preset prefab available");
            return null;
        }

        // Instantiate the preset directly
        currentPreviewWeapon = Instantiate(presetPrefab, parent);
        currentPreviewWeapon.name = "WeaponPreview";
        currentPreviewWeapon.transform.localPosition = Vector3.zero;
        currentPreviewWeapon.transform.localRotation = Quaternion.identity;
        currentPreviewWeapon.transform.localScale = Vector3.one;

        // Disable physics for preview
        DisablePreviewComponents(currentPreviewWeapon);

        Debug.Log($"[WeaponCustomizer] Created preview from preset: {presetPrefab.name}");

        return currentPreviewWeapon;
    }

    public void RefreshPreview(Transform parent)
    {
        CreatePreviewWeapon(parent);
    }

    public void ClearPreview()
    {
        if (currentPreviewWeapon != null)
        {
            Destroy(currentPreviewWeapon);
            currentPreviewWeapon = null;
        }
    }

    public void RotatePreview(float deltaTime)
    {
        if (currentPreviewWeapon != null)
        {
            currentPreviewWeapon.transform.Rotate(Vector3.up, rotationSpeed * deltaTime, Space.World);
        }
    }

    void DisablePreviewComponents(GameObject obj)
    {
        // Disable rigidbodies
        var rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rigidbodies) rb.isKinematic = true;

        // Disable colliders
        var colliders = obj.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders) col.enabled = false;

        // Disable audio
        var audioSources = obj.GetComponentsInChildren<AudioSource>(true);
        foreach (var audio in audioSources) audio.enabled = false;
    }

    #endregion

    #region Utility

    public void ResetCurrentLoadout()
    {
        playerLoadouts.loadouts[currentEditingLoadout].primaryWeapon = WeaponBuildData.CreateDefault();
    }

    public void RandomizeCurrentLoadout()
    {
        var build = GetCurrentBuild();
        var platform = GetPlatformConfig(build.platform);

        // Randomize core parts
        if (platform.barrels != null && platform.barrels.Length > 0)
            build.barrelIndex = Random.Range(0, platform.barrels.Length);
        if (platform.grips != null && platform.grips.Length > 0)
            build.gripIndex = Random.Range(0, platform.grips.Length);
        if (platform.handguards != null && platform.handguards.Length > 0)
            build.handguardIndex = Random.Range(0, platform.handguards.Length);
        if (platform.stocks != null && platform.stocks.Length > 0)
            build.stockIndex = Random.Range(0, platform.stocks.Length);
        if (platform.magazines != null && platform.magazines.Length > 0)
            build.magazineIndex = Random.Range(0, platform.magazines.Length);

        // Randomize attachments (50% chance each)
        build.scopeIndex = Random.value > 0.5f && scopes?.prefabs?.Length > 0 ?
            Random.Range(0, scopes.prefabs.Length) : -1;
        build.muzzleIndex = Random.value > 0.5f && muzzleDevices?.prefabs?.Length > 0 ?
            Random.Range(0, muzzleDevices.prefabs.Length) : -1;
        build.foreGripIndex = Random.value > 0.5f && foreGrips?.prefabs?.Length > 0 ?
            Random.Range(0, foreGrips.prefabs.Length) : -1;
    }

    /// <summary>
    /// Gets the list of all part categories for UI building.
    /// </summary>
    public string[] GetAllCategories()
    {
        var platform = GetCurrentPlatform();
        var categories = new List<string>
        {
            "Barrel", "Grip", "Handguard", "Stock", "Magazine"
        };

        // Add platform-specific categories
        if (platform == "Weapon_A")
        {
            if (weaponA.handles != null && weaponA.handles.Length > 0)
                categories.Add("Handle");
            if (weaponA.triggers != null && weaponA.triggers.Length > 0)
                categories.Add("Trigger");
        }

        // Add universal attachment categories
        categories.Add("Scope");
        categories.Add("Muzzle");
        categories.Add("ForeGrip");

        if (bipods?.prefabs?.Length > 0)
            categories.Add("Bipod");
        if (lasers?.prefabs?.Length > 0)
            categories.Add("Laser");
        if (flashlights?.prefabs?.Length > 0)
            categories.Add("Flashlight");

        return categories.ToArray();
    }

    #endregion
}
