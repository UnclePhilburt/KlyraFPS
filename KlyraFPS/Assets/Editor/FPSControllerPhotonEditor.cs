#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(FPSControllerPhoton))]
public class FPSControllerPhotonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FPSControllerPhoton controller = (FPSControllerPhoton)target;

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Auto-Find Attachment Prefabs", GUILayout.Height(35)))
        {
            AutoPopulateAttachments(controller);
        }

        EditorGUILayout.Space(5);

        int headgear = controller.headgearPrefabs?.Length ?? 0;
        int facewear = controller.facewearPrefabs?.Length ?? 0;
        int backpacks = controller.backpackPrefabs?.Length ?? 0;

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
                $"Headgear: {headgear} | Facewear: {facewear} | Backpacks: {backpacks}",
                MessageType.None);
        }
    }

    void AutoPopulateAttachments(FPSControllerPhoton controller)
    {
        Undo.RecordObject(controller, "Auto-populate FPSControllerPhoton attachments");

        // Find attachments in the same order as CharacterCustomizer
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

            // Headgear
            foreach (var pattern in headgearPatterns)
            {
                if (filename.Contains(pattern))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !headgear.Contains(prefab))
                    {
                        headgear.Add(prefab);
                    }
                    break;
                }
            }

            // Facewear
            foreach (var pattern in facewearPatterns)
            {
                if (filename.Contains(pattern))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !facewear.Contains(prefab))
                    {
                        facewear.Add(prefab);
                    }
                    break;
                }
            }

            // Backpacks
            foreach (var pattern in backpackPatterns)
            {
                if (filename.Contains(pattern) && backpacks.Count < 20)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !backpacks.Contains(prefab))
                    {
                        backpacks.Add(prefab);
                    }
                    break;
                }
            }
        }

        controller.headgearPrefabs = headgear.ToArray();
        controller.facewearPrefabs = facewear.ToArray();
        controller.backpackPrefabs = backpacks.ToArray();

        EditorUtility.SetDirty(controller);

        Debug.Log($"[FPSControllerPhoton] Found {headgear.Count} headgear, {facewear.Count} facewear, {backpacks.Count} backpacks");
    }
}
#endif
