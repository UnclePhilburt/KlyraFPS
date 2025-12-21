#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(CharacterCustomizer))]
public class CharacterCustomizerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CharacterCustomizer customizer = (CharacterCustomizer)target;

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Auto-Find Synty Character Assets", GUILayout.Height(35)))
        {
            AutoPopulateAssets(customizer);
        }

        EditorGUILayout.Space(5);

        // Show status
        int phantomChars = customizer.phantomOptions?.baseCharacters?.Length ?? 0;
        int havocChars = customizer.havocOptions?.baseCharacters?.Length ?? 0;
        int headgear = customizer.phantomOptions?.headgear?.Length ?? 0;
        int facewear = customizer.phantomOptions?.facewear?.Length ?? 0;
        int backpacks = customizer.phantomOptions?.backpacks?.Length ?? 0;

        if (phantomChars == 0 || havocChars == 0)
        {
            EditorGUILayout.HelpBox(
                "Click 'Auto-Find Synty Character Assets' to populate:\n" +
                "- Phantom (USA) characters\n" +
                "- Havoc (Russia) characters\n" +
                "- Helmets, hats, hair\n" +
                "- Glasses, masks, beards\n" +
                "- Backpacks, gear",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Phantom: {phantomChars} chars | Havoc: {havocChars} chars\n" +
                $"Headgear: {headgear} | Facewear: {facewear} | Backpacks: {backpacks}",
                MessageType.None);
        }
    }

    void AutoPopulateAssets(CharacterCustomizer customizer)
    {
        Undo.RecordObject(customizer, "Auto-populate CharacterCustomizer assets");

        Debug.Log("[CharacterCustomizer] Starting auto-populate...");

        // Initialize team options if null
        if (customizer.phantomOptions == null)
        {
            customizer.phantomOptions = new CharacterCustomizer.TeamCustomization();
            Debug.Log("[CharacterCustomizer] Created new phantomOptions");
        }
        if (customizer.havocOptions == null)
        {
            customizer.havocOptions = new CharacterCustomizer.TeamCustomization();
            Debug.Log("[CharacterCustomizer] Created new havocOptions");
        }

        customizer.phantomOptions.teamName = "Phantom";
        customizer.havocOptions.teamName = "Havoc";

        // Find Phantom (USA) characters
        List<GameObject> phantomChars = new List<GameObject>();
        string[] phantomPaths = new string[]
        {
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Male_02.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Female_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Female_02.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Contractor_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Contractor_Male_02.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Contractor_Female_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Ghillie_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Pilot_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Pilot_Female_01.prefab",
        };

        // Also find Alt soldiers
        string[] altSoldierGuids = AssetDatabase.FindAssets("t:Prefab SM_Chr_Soldier",
            new[] { "Assets/Synty/PolygonMilitary/Prefabs/Characters/Alt_Soldiers" });

        foreach (var path in phantomPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                phantomChars.Add(prefab);
                Debug.Log($"[CharacterCustomizer] Found Phantom char: {prefab.name}");
            }
            else
            {
                Debug.LogWarning($"[CharacterCustomizer] Could not find: {path}");
            }
        }

        foreach (var guid in altSoldierGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && !phantomChars.Contains(prefab))
                phantomChars.Add(prefab);
        }

        customizer.phantomOptions.baseCharacters = phantomChars.ToArray();
        Debug.Log($"[CharacterCustomizer] Found {phantomChars.Count} Phantom characters");

        // Find Havoc (Russia/Insurgent) characters
        List<GameObject> havocChars = new List<GameObject>();
        string[] havocPaths = new string[]
        {
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Male_02.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Male_03.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Male_04.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Male_05.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Female_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Insurgent_Female_02.prefab",
        };

        foreach (var path in havocPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) havocChars.Add(prefab);
        }

        customizer.havocOptions.baseCharacters = havocChars.ToArray();
        Debug.Log($"[CharacterCustomizer] Found {havocChars.Count} Havoc characters");

        // Find headgear (helmets, hats, hair) - shared between teams
        List<GameObject> headgear = new List<GameObject>();
        string[] headgearPatterns = new string[] { "Helmet", "Hat", "Hair", "Beret", "Beanie", "Cap", "Turban", "Pilot_Helmet" };

        string[] attachGuids = AssetDatabase.FindAssets("t:Prefab",
            new[] { "Assets/Synty/PolygonMilitary/Prefabs/Characters/Attachments" });

        foreach (var guid in attachGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var pattern in headgearPatterns)
            {
                if (filename.Contains(pattern))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !headgear.Contains(prefab))
                    {
                        headgear.Add(prefab);
                        break;
                    }
                }
            }
        }

        customizer.phantomOptions.headgear = headgear.ToArray();
        customizer.havocOptions.headgear = headgear.ToArray();
        Debug.Log($"[CharacterCustomizer] Found {headgear.Count} headgear items");

        // Find facewear (glasses, masks, beards, goggles)
        List<GameObject> facewear = new List<GameObject>();
        string[] facewearPatterns = new string[] { "Beard", "Mustache", "Glasses", "Goggles", "Gas_Mask", "NVG", "Eyepatch", "SunGlasses" };

        foreach (var guid in attachGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var pattern in facewearPatterns)
            {
                if (filename.Contains(pattern))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !facewear.Contains(prefab))
                    {
                        facewear.Add(prefab);
                        break;
                    }
                }
            }
        }

        customizer.phantomOptions.facewear = facewear.ToArray();
        customizer.havocOptions.facewear = facewear.ToArray();
        Debug.Log($"[CharacterCustomizer] Found {facewear.Count} facewear items");

        // Find backpacks and gear
        List<GameObject> backpacks = new List<GameObject>();
        string[] backpackPatterns = new string[] { "Backpack", "Pouch", "Holster", "Radio" };

        foreach (var guid in attachGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = System.IO.Path.GetFileNameWithoutExtension(path);

            foreach (var pattern in backpackPatterns)
            {
                if (filename.Contains(pattern) && backpacks.Count < 20) // Limit to prevent too many
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !backpacks.Contains(prefab))
                    {
                        backpacks.Add(prefab);
                        break;
                    }
                }
            }
        }

        customizer.phantomOptions.backpacks = backpacks.ToArray();
        customizer.havocOptions.backpacks = backpacks.ToArray();
        Debug.Log($"[CharacterCustomizer] Found {backpacks.Count} backpack/gear items");

        EditorUtility.SetDirty(customizer);

        Debug.Log($"[CharacterCustomizer] Asset population complete!");
    }

    [MenuItem("CONTEXT/CharacterCustomizer/Auto-Find Synty Character Assets")]
    static void AutoFindFromContextMenu(MenuCommand command)
    {
        CharacterCustomizer customizer = (CharacterCustomizer)command.context;
        var editor = CreateEditor(customizer) as CharacterCustomizerEditor;
        editor.AutoPopulateAssets(customizer);
        DestroyImmediate(editor);
    }
}
#endif
