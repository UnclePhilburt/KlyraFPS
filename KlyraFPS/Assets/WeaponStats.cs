using UnityEngine;

/// <summary>
/// Defines all stats for a weapon configuration.
/// Used both as base stats and as calculated final stats.
/// </summary>
[System.Serializable]
public class WeaponStats
{
    [Header("Combat Stats")]
    public float damage = 25f;
    public float fireRate = 0.1f;           // Time between shots (lower = faster)
    public float range = 100f;
    public float accuracy = 1f;             // 1.0 = perfect, lower = more spread

    [Header("Recoil")]
    public float recoilVertical = 2f;
    public float recoilHorizontal = 1f;

    [Header("Aiming")]
    public float adsSpeed = 10f;            // ADS transition speed
    public float aimFOV = 30f;              // FOV when aiming down sights

    [Header("Mobility")]
    public float moveSpeedMultiplier = 1f;  // Movement speed while holding weapon
    public float adsMovePenalty = 0.7f;     // Movement speed multiplier while ADS

    [Header("Magazine")]
    public int magazineSize = 30;
    public float reloadTime = 2.5f;

    [Header("Sound")]
    public bool isSilenced = false;
    public float soundRange = 50f;          // Distance enemies can hear shots

    /// <summary>
    /// Creates a deep copy of this stats object.
    /// </summary>
    public WeaponStats Clone()
    {
        return new WeaponStats
        {
            damage = this.damage,
            fireRate = this.fireRate,
            range = this.range,
            accuracy = this.accuracy,
            recoilVertical = this.recoilVertical,
            recoilHorizontal = this.recoilHorizontal,
            adsSpeed = this.adsSpeed,
            aimFOV = this.aimFOV,
            moveSpeedMultiplier = this.moveSpeedMultiplier,
            adsMovePenalty = this.adsMovePenalty,
            magazineSize = this.magazineSize,
            reloadTime = this.reloadTime,
            isSilenced = this.isSilenced,
            soundRange = this.soundRange
        };
    }

    /// <summary>
    /// Applies a stat modifier to this stats object.
    /// </summary>
    public void ApplyModifier(AttachmentStatModifier modifier)
    {
        if (modifier == null) return;

        // Multiplicative modifiers
        damage *= modifier.damageModifier;
        fireRate *= modifier.fireRateModifier;
        range *= modifier.rangeModifier;
        accuracy *= modifier.accuracyModifier;
        recoilVertical *= modifier.recoilModifier;
        recoilHorizontal *= modifier.recoilModifier;
        adsSpeed *= modifier.adsSpeedModifier;
        moveSpeedMultiplier *= modifier.moveSpeedModifier;
        reloadTime *= modifier.reloadTimeModifier;
        soundRange *= modifier.soundRangeModifier;

        // Additive modifiers
        magazineSize += modifier.magazineSizeBonus;

        // Override aim FOV if scope provides one
        if (modifier.aimFOVOverride > 0)
        {
            aimFOV = modifier.aimFOVOverride;
        }

        // Special effects
        if (modifier.addsSilencer)
        {
            isSilenced = true;
        }
    }
}
