#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(AIController))]
public class AIControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        AIController ai = (AIController)target;

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Auto-Find Attachment Prefabs", GUILayout.Height(35)))
        {
            AutoPopulateAttachments(ai);
        }

        EditorGUILayout.Space(5);

        int headgear = ai.headgearPrefabs?.Length ?? 0;
        int facewear = ai.facewearPrefabs?.Length ?? 0;
        int backpacks = ai.backpackPrefabs?.Length ?? 0;

        if (headgear == 0 && facewear == 0 && backpacks == 0)
        {
            EditorGUILayout.HelpBox(
                "Click 'Auto-Find Attachment Prefabs' to populate:\n" +
                "- Headgear (helmets, hats, hair)\n" +
                "- Facewear (beards, glasses, masks)\n" +
                "- Backpacks (backpacks, pouches)",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Headgear: {headgear} | Facewear: {facewear} | Backpacks: {backpacks}\n" +
                $"Chances: Head {ai.headgearChance:P0} | Face {ai.facewearChance:P0} | Back {ai.backpackChance:P0}",
                MessageType.None);
        }
    }

    void AutoPopulateAttachments(AIController ai)
    {
        Undo.RecordObject(ai, "Auto-populate AIController attachments");

        string[] attachGuids = AssetDatabase.FindAssets("t:Prefab",
            new[] { "Assets/Synty/PolygonMilitary/Prefabs/Characters/Attachments" });

        List<GameObject> headgear = new List<GameObject>();
        List<GameObject> facewear = new List<GameObject>();
        List<GameObject> backpacks = new List<GameObject>();

        string[] headgearPatterns = { "Helmet", "Hat", "Hair", "Beret", "Beanie", "Cap", "Turban", "Pilot_Helmet" };
        string[] facewearPatterns = { "Beard", "Mustache", "Glasses", "Goggles", "Gas_Mask", "NVG", "Eyepatch", "SunGlasses" };
        string[] backpackPatterns = { "Backpack", "Pouch", "Holster", "Radio" };

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
                        headgear.Add(prefab);
                    break;
                }
            }

            foreach (var pattern in facewearPatterns)
            {
                if (filename.Contains(pattern))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !facewear.Contains(prefab))
                        facewear.Add(prefab);
                    break;
                }
            }

            foreach (var pattern in backpackPatterns)
            {
                if (filename.Contains(pattern) && backpacks.Count < 20)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !backpacks.Contains(prefab))
                        backpacks.Add(prefab);
                    break;
                }
            }
        }

        ai.headgearPrefabs = headgear.ToArray();
        ai.facewearPrefabs = facewear.ToArray();
        ai.backpackPrefabs = backpacks.ToArray();

        EditorUtility.SetDirty(ai);

        Debug.Log($"[AIController] Found {headgear.Count} headgear, {facewear.Count} facewear, {backpacks.Count} backpacks");
    }
}
#endif
