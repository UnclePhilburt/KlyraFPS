using UnityEngine;

/// <summary>
/// Defines how an attachment modifies weapon stats.
/// Multiplicative values: 1.0 = no change, 1.1 = +10%, 0.9 = -10%
/// Additive values: 0 = no change
/// </summary>
[System.Serializable]
public class AttachmentStatModifier
{
    [Header("Multiplicative Modifiers (1.0 = no change)")]
    [Range(0.5f, 1.5f)] public float damageModifier = 1f;
    [Range(0.5f, 1.5f)] public float fireRateModifier = 1f;
    [Range(0.5f, 2f)] public float rangeModifier = 1f;
    [Range(0.5f, 1.5f)] public float accuracyModifier = 1f;
    [Range(0.5f, 1.5f)] public float recoilModifier = 1f;
    [Range(0.5f, 1.5f)] public float adsSpeedModifier = 1f;
    [Range(0.5f, 1.5f)] public float moveSpeedModifier = 1f;
    [Range(0.5f, 1.5f)] public float reloadTimeModifier = 1f;
    [Range(0f, 1.5f)] public float soundRangeModifier = 1f;

    [Header("Additive Modifiers")]
    public int magazineSizeBonus = 0;       // Extra rounds (e.g., +10 for extended mag)

    [Header("Override Values (0 = don't override)")]
    public float aimFOVOverride = 0f;       // Scope zoom level (e.g., 30 for 4x scope)

    [Header("Special Effects")]
    public bool addsSilencer = false;

    /// <summary>
    /// Creates a neutral modifier that doesn't change any stats.
    /// </summary>
    public static AttachmentStatModifier Neutral()
    {
        return new AttachmentStatModifier();
    }

    /// <summary>
    /// Creates a combined modifier from multiple modifiers.
    /// Multiplicative values are multiplied together.
    /// Additive values are summed.
    /// </summary>
    public static AttachmentStatModifier Combine(params AttachmentStatModifier[] modifiers)
    {
        var result = new AttachmentStatModifier();

        foreach (var mod in modifiers)
        {
            if (mod == null) continue;

            result.damageModifier *= mod.damageModifier;
            result.fireRateModifier *= mod.fireRateModifier;
            result.rangeModifier *= mod.rangeModifier;
            result.accuracyModifier *= mod.accuracyModifier;
            result.recoilModifier *= mod.recoilModifier;
            result.adsSpeedModifier *= mod.adsSpeedModifier;
            result.moveSpeedModifier *= mod.moveSpeedModifier;
            result.reloadTimeModifier *= mod.reloadTimeModifier;
            result.soundRangeModifier *= mod.soundRangeModifier;

            result.magazineSizeBonus += mod.magazineSizeBonus;

            // Last non-zero FOV override wins
            if (mod.aimFOVOverride > 0)
                result.aimFOVOverride = mod.aimFOVOverride;

            if (mod.addsSilencer)
                result.addsSilencer = true;
        }

        return result;
    }
}

/// <summary>
/// Defines a single attachment that can be equipped on a weapon.
/// </summary>
[System.Serializable]
public class AttachmentData
{
    public string attachmentName;
    public string description;
    public GameObject prefab;
    public Sprite icon;
    public AttachmentSlot slot;
    public AttachmentStatModifier statModifiers = new AttachmentStatModifier();

    // Compatibility
    public bool compatibleWithWeaponA = true;
    public bool compatibleWithWeaponB = true;
}

/// <summary>
/// Defines the slot types for weapon attachments.
/// </summary>
public enum AttachmentSlot
{
    // Core weapon parts (platform-specific)
    Barrel,
    Grip,
    Handguard,
    Stock,
    Magazine,
    Handle,         // Weapon_A only
    Trigger,        // Weapon_A only

    // Universal attachments
    Scope,
    Muzzle,         // Silencers, muzzle brakes, flash hiders
    ForeGrip,       // Underbarrel grips
    Bipod,
    Laser,
    Flashlight,
    UnderbarrelWeapon   // Grenade launcher, shotgun
}
