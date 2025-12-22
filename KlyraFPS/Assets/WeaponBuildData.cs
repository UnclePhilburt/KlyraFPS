using UnityEngine;

/// <summary>
/// Serializable data for a complete weapon build/loadout.
/// Stores indices into the WeaponCustomizer's attachment arrays.
/// </summary>
[System.Serializable]
public class WeaponBuildData
{
    public string buildName = "Custom Rifle";

    // Preset-based selection (simpler approach)
    public int presetIndex = 0;

    // Platform selection ("Weapon_A" or "Weapon_B") - for modular assembly
    public string platform = "Weapon_A";

    [Header("Core Parts (platform-specific)")]
    public int barrelIndex = 0;
    public int gripIndex = 0;
    public int handguardIndex = 0;
    public int stockIndex = 0;
    public int magazineIndex = 0;
    public int handleIndex = -1;        // Weapon_A only (-1 = none)
    public int triggerIndex = -1;       // Weapon_A only (-1 = none)

    [Header("Universal Attachments (-1 = none)")]
    public int scopeIndex = -1;
    public int muzzleIndex = -1;
    public int foreGripIndex = -1;
    public int bipodIndex = -1;
    public int laserIndex = -1;
    public int flashlightIndex = -1;

    /// <summary>
    /// Serializes this build to JSON for storage.
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    /// <summary>
    /// Deserializes a build from JSON.
    /// </summary>
    public static WeaponBuildData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return CreateDefault();

        try
        {
            return JsonUtility.FromJson<WeaponBuildData>(json);
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>
    /// Creates a default weapon build.
    /// </summary>
    public static WeaponBuildData CreateDefault()
    {
        return new WeaponBuildData
        {
            buildName = "Default Rifle",
            platform = "Weapon_A",
            barrelIndex = 0,
            gripIndex = 0,
            handguardIndex = 0,
            stockIndex = 0,
            magazineIndex = 0,
            handleIndex = -1,
            triggerIndex = -1,
            scopeIndex = -1,
            muzzleIndex = -1,
            foreGripIndex = -1,
            bipodIndex = -1,
            laserIndex = -1,
            flashlightIndex = -1
        };
    }

    /// <summary>
    /// Creates a clone of this build.
    /// </summary>
    public WeaponBuildData Clone()
    {
        return new WeaponBuildData
        {
            buildName = this.buildName,
            presetIndex = this.presetIndex,
            platform = this.platform,
            barrelIndex = this.barrelIndex,
            gripIndex = this.gripIndex,
            handguardIndex = this.handguardIndex,
            stockIndex = this.stockIndex,
            magazineIndex = this.magazineIndex,
            handleIndex = this.handleIndex,
            triggerIndex = this.triggerIndex,
            scopeIndex = this.scopeIndex,
            muzzleIndex = this.muzzleIndex,
            foreGripIndex = this.foreGripIndex,
            bipodIndex = this.bipodIndex,
            laserIndex = this.laserIndex,
            flashlightIndex = this.flashlightIndex
        };
    }

    /// <summary>
    /// Gets all attachment indices as an array for network sync.
    /// </summary>
    public int[] ToAttachmentArray()
    {
        return new int[]
        {
            platform == "Weapon_A" ? 0 : 1,  // Platform index
            barrelIndex,
            gripIndex,
            handguardIndex,
            stockIndex,
            magazineIndex,
            handleIndex,
            triggerIndex,
            scopeIndex,
            muzzleIndex,
            foreGripIndex,
            bipodIndex,
            laserIndex,
            flashlightIndex
        };
    }

    /// <summary>
    /// Sets all attachment indices from an array (for network sync).
    /// </summary>
    public void FromAttachmentArray(int[] indices)
    {
        if (indices == null || indices.Length < 14) return;

        platform = indices[0] == 0 ? "Weapon_A" : "Weapon_B";
        barrelIndex = indices[1];
        gripIndex = indices[2];
        handguardIndex = indices[3];
        stockIndex = indices[4];
        magazineIndex = indices[5];
        handleIndex = indices[6];
        triggerIndex = indices[7];
        scopeIndex = indices[8];
        muzzleIndex = indices[9];
        foreGripIndex = indices[10];
        bipodIndex = indices[11];
        laserIndex = indices[12];
        flashlightIndex = indices[13];
    }
}

/// <summary>
/// Container for a player's loadout (weapon build + other equipment).
/// </summary>
[System.Serializable]
public class LoadoutData
{
    public string loadoutName = "Loadout 1";
    public WeaponBuildData primaryWeapon = new WeaponBuildData();

    // Future expansion
    public int grenadeType = 0;
    public int tacticalEquipment = 0;
}

/// <summary>
/// Container for all player loadouts.
/// </summary>
[System.Serializable]
public class PlayerLoadouts
{
    public const int MAX_LOADOUT_SLOTS = 5;

    public LoadoutData[] loadouts = new LoadoutData[MAX_LOADOUT_SLOTS];
    public int selectedLoadoutIndex = 0;

    public LoadoutData ActiveLoadout => loadouts[selectedLoadoutIndex];
    public WeaponBuildData ActiveWeaponBuild => ActiveLoadout?.primaryWeapon;

    public PlayerLoadouts()
    {
        // Initialize default loadouts
        for (int i = 0; i < MAX_LOADOUT_SLOTS; i++)
        {
            loadouts[i] = new LoadoutData
            {
                loadoutName = $"Loadout {i + 1}",
                primaryWeapon = WeaponBuildData.CreateDefault()
            };
        }
    }
}
