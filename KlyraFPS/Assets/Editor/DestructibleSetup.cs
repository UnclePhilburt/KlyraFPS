using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor tool to automatically add DestructibleObject components to appropriate objects in the scene
/// </summary>
public class DestructibleSetup : EditorWindow
{
    // Keywords that indicate an object should be destructible
    private static readonly string[] destructibleKeywords = new string[]
    {
        // Barriers and barricades
        "barrier", "barricade", "blockade",
        // Fences and wire
        "fence", "wire", "razor",
        // Boxes and crates
        "crate", "box", "cardboard",
        // Barrels and containers
        "barrel", "drum", "canister", "jerry", "can_",
        // Sandbags and fortifications
        "sandbag", "sand_bag", "fortification",
        // Signs and poles
        "sign", "pole", "post", "lamppost", "streetlight",
        // Traffic items
        "cone", "traffic",
        // Pallets and storage
        "pallet", "stack",
        // Debris
        "rubble", "wreck",
        // Military obstacles
        "hedgehog", "czech", "tank_trap", "dragon",
        // Furniture/props that should break
        "chair", "table", "bench", "shelf", "locker",
        // Miscellaneous
        "dummy", "target", "mannequin",
        // Common prop prefixes
        "sm_prop_"
    };

    // Keywords that indicate an object should NOT be destructible
    private static readonly string[] excludeKeywords = new string[]
    {
        "building", "house", "wall_large", "floor", "ground", "terrain",
        "destroyed", "broken",
        "character", "soldier", "player",
        "tank_usa", "tank_russian", "helicopter", "jet_", "truck",
        "weapon", "gun", "rifle",
        "fx_", "vfx_", "particle",
        "فناء", "فائز" // Non-English exclusions
    };

    private float defaultHealth = 50f;
    private bool applyDebrisForce = true;
    private float debrisForce = 500f;
    private Vector2 scrollPos;
    private List<GameObject> foundObjects = new List<GameObject>();
    private string lastSearchInfo = "";

    [MenuItem("Tools/Setup Destructibles")]
    public static void ShowWindow()
    {
        GetWindow<DestructibleSetup>("Destructible Setup");
    }

    void OnGUI()
    {
        GUILayout.Label("Destructible Object Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "This tool finds objects in the scene that should be destructible " +
            "(barriers, crates, barrels, fences, etc.) and adds the DestructibleObject component.\n\n" +
            "1. Click 'Find Destructible Objects' to preview\n" +
            "2. Review the list\n" +
            "3. Click 'Add DestructibleObject to All' to apply",
            MessageType.Info);

        EditorGUILayout.Space();
        GUILayout.Label("Settings", EditorStyles.boldLabel);

        defaultHealth = EditorGUILayout.FloatField("Default Health", defaultHealth);
        applyDebrisForce = EditorGUILayout.Toggle("Apply Debris Force", applyDebrisForce);
        if (applyDebrisForce)
        {
            debrisForce = EditorGUILayout.FloatField("Debris Force", debrisForce);
        }

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Find Destructible Objects", GUILayout.Height(30)))
        {
            FindDestructibleObjects();
        }
        if (GUILayout.Button("Find ALL Props", GUILayout.Height(30)))
        {
            FindAllProps();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Clear List"))
        {
            foundObjects.Clear();
            lastSearchInfo = "";
        }

        EditorGUILayout.Space();

        // Always show search results section
        if (!string.IsNullOrEmpty(lastSearchInfo))
        {
            EditorGUILayout.HelpBox(lastSearchInfo, MessageType.Info);
        }

        // Count objects
        int objectCount = foundObjects.Count;
        int alreadyHave = 0;
        foreach (var obj in foundObjects)
        {
            if (obj != null && obj.GetComponent<DestructibleObject>() != null)
                alreadyHave++;
        }

        // ALWAYS show the add button if we have objects (before the list)
        if (objectCount > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Found: {objectCount} objects | Already setup: {alreadyHave}");

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button($"ADD DestructibleObject TO ALL {objectCount} OBJECTS", GUILayout.Height(50)))
            {
                AddDestructibleToAll();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space();
            GUILayout.Label("Object List (click to select):", EditorStyles.boldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
            foreach (var obj in foundObjects)
            {
                if (obj == null) continue;

                EditorGUILayout.BeginHorizontal();

                bool hasComponent = obj.GetComponent<DestructibleObject>() != null;
                GUI.color = hasComponent ? Color.green : Color.yellow;

                string prefix = hasComponent ? "[DONE]" : "[    ]";
                if (GUILayout.Button($"{prefix} {obj.name}"))
                {
                    Selection.activeGameObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }

                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        else if (!string.IsNullOrEmpty(lastSearchInfo))
        {
            EditorGUILayout.HelpBox("No matching objects found. Try 'Find ALL Props' button.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        GUILayout.Label("Quick Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Add to Selected Objects"))
        {
            AddToSelected();
        }

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "Destructible keywords:\n" +
            "barrier, barricade, fence, wire, crate, box, barrel, sandbag, " +
            "sign, pole, cone, pallet, debris, hedgehog, chair, table, bench",
            MessageType.None);
    }

    void FindDestructibleObjects()
    {
        FindObjectsWithFilter(false);
    }

    void FindAllProps()
    {
        FindObjectsWithFilter(true);
    }

    void FindObjectsWithFilter(bool findAllProps)
    {
        foundObjects.Clear();

        // Find all GameObjects in scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int totalScanned = 0;
        int noCollider = 0;
        int noRenderer = 0;
        int excluded = 0;

        foreach (GameObject obj in allObjects)
        {
            totalScanned++;

            // Skip objects without colliders
            if (obj.GetComponent<Collider>() == null && obj.GetComponentInChildren<Collider>() == null)
            {
                noCollider++;
                continue;
            }

            // Skip objects without renderers
            if (obj.GetComponent<MeshRenderer>() == null && obj.GetComponentInChildren<MeshRenderer>() == null)
            {
                noRenderer++;
                continue;
            }

            string name = obj.name.ToLower();

            // Check exclusions
            bool isExcluded = false;
            foreach (string exclude in excludeKeywords)
            {
                if (name.Contains(exclude.ToLower()))
                {
                    isExcluded = true;
                    excluded++;
                    break;
                }
            }
            if (isExcluded) continue;

            if (findAllProps)
            {
                // Find ALL props (SM_Prop or has collider and is small enough)
                if (name.Contains("sm_prop") || name.Contains("prop_"))
                {
                    foundObjects.Add(obj);
                }
                else
                {
                    // Check size - small objects are likely props
                    Renderer renderer = obj.GetComponent<Renderer>();
                    if (renderer == null) renderer = obj.GetComponentInChildren<Renderer>();

                    if (renderer != null)
                    {
                        Vector3 size = renderer.bounds.size;
                        if (size.x < 8f && size.y < 4f && size.z < 8f)
                        {
                            foundObjects.Add(obj);
                        }
                    }
                }
            }
            else
            {
                // Original keyword-based search
                if (ShouldBeDestructible(obj))
                {
                    foundObjects.Add(obj);
                }
            }
        }

        // Sort by name
        foundObjects.Sort((a, b) => a.name.CompareTo(b.name));

        lastSearchInfo = $"Scanned {totalScanned} objects.\n" +
                         $"Found {foundObjects.Count} destructible objects.\n" +
                         $"Skipped: {noCollider} no collider, {noRenderer} no renderer, {excluded} excluded.";

        Debug.Log($"[DestructibleSetup] {lastSearchInfo}");

        // Force UI refresh
        Repaint();
    }

    bool ShouldBeDestructible(GameObject obj)
    {
        if (obj == null) return false;

        string name = obj.name.ToLower();

        // Check if name matches any destructible keyword
        foreach (string keyword in destructibleKeywords)
        {
            if (name.Contains(keyword.ToLower()))
            {
                return true;
            }
        }

        return false;
    }

    void AddDestructibleToAll()
    {
        int added = 0;
        int skipped = 0;

        Undo.SetCurrentGroupName("Add DestructibleObject Components");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject obj in foundObjects)
        {
            if (obj == null) continue;

            // Skip if already has component
            if (obj.GetComponent<DestructibleObject>() != null)
            {
                skipped++;
                continue;
            }

            // Add the component
            DestructibleObject destructible = Undo.AddComponent<DestructibleObject>(obj);

            // Configure based on object type
            ConfigureDestructible(destructible, obj);

            added++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[DestructibleSetup] Added DestructibleObject to {added} objects, skipped {skipped} (already had component)");
        EditorUtility.DisplayDialog("Destructible Setup",
            $"Added DestructibleObject to {added} objects.\nSkipped {skipped} (already had component).",
            "OK");

        Repaint();
    }

    void AddToSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        int added = 0;

        Undo.SetCurrentGroupName("Add DestructibleObject to Selected");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject obj in selected)
        {
            if (obj.GetComponent<DestructibleObject>() != null) continue;

            DestructibleObject destructible = Undo.AddComponent<DestructibleObject>(obj);
            ConfigureDestructible(destructible, obj);
            added++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[DestructibleSetup] Added DestructibleObject to {added} selected objects");
    }

    void ConfigureDestructible(DestructibleObject destructible, GameObject obj)
    {
        string name = obj.name.ToLower();

        // Set health based on object type
        if (name.Contains("barrel") || name.Contains("drum"))
        {
            destructible.maxHealth = 30f;
        }
        else if (name.Contains("crate") || name.Contains("box") || name.Contains("cardboard"))
        {
            destructible.maxHealth = 20f;
        }
        else if (name.Contains("fence") || name.Contains("wire"))
        {
            destructible.maxHealth = 40f;
        }
        else if (name.Contains("barrier") || name.Contains("barricade"))
        {
            destructible.maxHealth = 60f;
        }
        else if (name.Contains("sandbag"))
        {
            destructible.maxHealth = 100f;
        }
        else if (name.Contains("sign") || name.Contains("pole") || name.Contains("cone"))
        {
            destructible.maxHealth = 15f;
        }
        else if (name.Contains("hedgehog") || name.Contains("tank_trap"))
        {
            destructible.maxHealth = 200f; // These are tough
        }
        else
        {
            destructible.maxHealth = defaultHealth;
        }

        destructible.currentHealth = destructible.maxHealth;
        destructible.applyDebrisForce = applyDebrisForce;
        destructible.debrisForce = debrisForce;
        destructible.crushableByVehicles = true;
        destructible.damageableByWeapons = true;

        // Make sure it has a Rigidbody for physics interaction
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(obj);
            rb.mass = GetMassForObject(name);
            rb.isKinematic = true; // Start kinematic, becomes dynamic when hit
        }
    }

    float GetMassForObject(string name)
    {
        if (name.Contains("barrel") || name.Contains("drum"))
            return 50f;
        if (name.Contains("crate") || name.Contains("box"))
            return 30f;
        if (name.Contains("sandbag"))
            return 40f;
        if (name.Contains("barrier"))
            return 100f;
        if (name.Contains("fence"))
            return 20f;
        if (name.Contains("sign") || name.Contains("pole") || name.Contains("cone"))
            return 10f;
        if (name.Contains("hedgehog") || name.Contains("tank_trap"))
            return 500f;
        return 25f;
    }

    [MenuItem("Tools/Quick Add Destructible to Selection %#d")]
    static void QuickAddDestructible()
    {
        GameObject[] selected = Selection.gameObjects;
        int added = 0;

        foreach (GameObject obj in selected)
        {
            if (obj.GetComponent<DestructibleObject>() != null) continue;

            DestructibleObject destructible = Undo.AddComponent<DestructibleObject>(obj);
            destructible.maxHealth = 50f;
            destructible.currentHealth = 50f;
            destructible.crushableByVehicles = true;
            added++;
        }

        if (added > 0)
        {
            Debug.Log($"[DestructibleSetup] Quick-added DestructibleObject to {added} objects");
        }
    }
}
