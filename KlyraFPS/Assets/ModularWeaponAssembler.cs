using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles the 3D assembly of modular weapon parts.
/// Simplified version that parents all parts to the weapon root.
/// Synty modular parts are designed to align when parented to the same transform.
/// </summary>
public class ModularWeaponAssembler : MonoBehaviour
{
    // Currently attached parts
    private Dictionary<string, GameObject> attachedParts = new Dictionary<string, GameObject>();

    // Cache of attachment indices for networking
    private int[] cachedIndices;

    /// <summary>
    /// Assembles the weapon from a build configuration.
    /// All parts are instantiated as children of this transform.
    /// </summary>
    public void AssembleFromBuild(WeaponBuildData build, WeaponCustomizer customizer)
    {
        ClearAllParts();

        if (customizer == null || build == null)
        {
            Debug.LogWarning("[ModularWeaponAssembler] customizer or build is null");
            return;
        }

        // Core parts - always attach to root
        AttachPart("Barrel", customizer.GetPartPrefab("Barrel", build.barrelIndex));
        AttachPart("Grip", customizer.GetPartPrefab("Grip", build.gripIndex));
        AttachPart("Handguard", customizer.GetPartPrefab("Handguard", build.handguardIndex));
        AttachPart("Stock", customizer.GetPartPrefab("Stock", build.stockIndex));
        AttachPart("Magazine", customizer.GetPartPrefab("Magazine", build.magazineIndex));
        AttachPart("Handle", customizer.GetPartPrefab("Handle", build.handleIndex));
        AttachPart("Trigger", customizer.GetPartPrefab("Trigger", build.triggerIndex));

        // Universal attachments
        AttachPart("Scope", customizer.GetPartPrefab("Scope", build.scopeIndex));
        AttachPart("Muzzle", customizer.GetPartPrefab("Muzzle", build.muzzleIndex));
        AttachPart("ForeGrip", customizer.GetPartPrefab("ForeGrip", build.foreGripIndex));
        AttachPart("Bipod", customizer.GetPartPrefab("Bipod", build.bipodIndex));
        AttachPart("Laser", customizer.GetPartPrefab("Laser", build.laserIndex));
        AttachPart("Flashlight", customizer.GetPartPrefab("Flashlight", build.flashlightIndex));

        // Cache indices for networking
        cachedIndices = build.ToAttachmentArray();

        Debug.Log($"[ModularWeaponAssembler] Assembled weapon with {attachedParts.Count} parts");
    }

    /// <summary>
    /// Attaches a part prefab as a child of this transform.
    /// </summary>
    public void AttachPart(string slot, GameObject prefab)
    {
        // Remove existing part in this slot
        DetachPart(slot);

        if (prefab == null) return;

        // Instantiate as child of this transform
        // Keep the prefab's original local position/rotation - Synty parts have specific placements
        GameObject part = Instantiate(prefab, transform);
        part.name = $"Part_{slot}";

        // Don't modify transform - let prefab keep its designed position
        // part.transform.localPosition = Vector3.zero;  // Removed
        // part.transform.localRotation = Quaternion.identity;  // Removed
        part.transform.localScale = Vector3.one;

        attachedParts[slot] = part;

        Debug.Log($"[ModularWeaponAssembler] Attached {slot}: {prefab.name} at {part.transform.localPosition}");
    }

    /// <summary>
    /// Detaches a part from the specified slot.
    /// </summary>
    public void DetachPart(string slot)
    {
        if (attachedParts.TryGetValue(slot, out GameObject part))
        {
            if (part != null)
            {
                if (Application.isPlaying)
                    Destroy(part);
                else
                    DestroyImmediate(part);
            }
            attachedParts.Remove(slot);
        }
    }

    /// <summary>
    /// Removes all attached parts.
    /// </summary>
    public void ClearAllParts()
    {
        var slots = new List<string>(attachedParts.Keys);
        foreach (var slot in slots)
        {
            DetachPart(slot);
        }
        attachedParts.Clear();
    }

    /// <summary>
    /// Gets the GameObject for an attached part.
    /// </summary>
    public GameObject GetAttachedPart(string slot)
    {
        attachedParts.TryGetValue(slot, out GameObject part);
        return part;
    }

    /// <summary>
    /// Gets all attachment indices as an array for network synchronization.
    /// </summary>
    public int[] GetAttachmentIndices()
    {
        return cachedIndices ?? new int[14];
    }

    /// <summary>
    /// Applies attachment indices from network sync.
    /// Requires a WeaponCustomizer to resolve prefabs.
    /// </summary>
    public void ApplyAttachmentIndices(int[] indices, WeaponCustomizer customizer)
    {
        if (indices == null || indices.Length < 14 || customizer == null) return;

        var build = new WeaponBuildData();
        build.FromAttachmentArray(indices);

        AssembleFromBuild(build, customizer);
    }

    /// <summary>
    /// Finds and returns the muzzle point for weapon effects.
    /// </summary>
    public Transform GetMuzzlePoint()
    {
        // Try to find muzzle on attached barrel first
        if (attachedParts.TryGetValue("Barrel", out GameObject barrel) && barrel != null)
        {
            Transform muzzle = FindChildRecursive(barrel.transform, "muzzle", "flash", "fire");
            if (muzzle != null) return muzzle;

            // Return the barrel's furthest point (approximation)
            return barrel.transform;
        }

        return transform;
    }

    /// <summary>
    /// Finds and returns the scope aim point for ADS.
    /// </summary>
    public Transform GetAimPoint()
    {
        // Try to find aim point on attached scope first
        if (attachedParts.TryGetValue("Scope", out GameObject scope) && scope != null)
        {
            Transform aimPoint = FindChildRecursive(scope.transform, "aim", "eye", "sight");
            if (aimPoint != null) return aimPoint;
            return scope.transform;
        }

        return transform;
    }

    Transform FindChildRecursive(Transform parent, params string[] keywords)
    {
        foreach (Transform child in parent)
        {
            string name = child.name.ToLower();
            foreach (var keyword in keywords)
            {
                if (name.Contains(keyword.ToLower()))
                {
                    return child;
                }
            }

            Transform found = FindChildRecursive(child, keywords);
            if (found != null) return found;
        }
        return null;
    }
}
