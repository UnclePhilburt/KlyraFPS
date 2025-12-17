using UnityEngine;
using UnityEditor;

public class RemoveMissingScripts : EditorWindow
{
    [MenuItem("Tools/Remove Missing Scripts From Player")]
    public static void RemoveMissing()
    {
        string prefabPath = "Assets/Player.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            // Try to find it
            string[] guids = AssetDatabase.FindAssets("Player t:Prefab");
            foreach (string guid in guids)
            {
                prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null) break;
            }
        }

        if (prefab == null)
        {
            Debug.LogError("Could not find Player prefab!");
            return;
        }

        // Load prefab contents
        GameObject instance = PrefabUtility.LoadPrefabContents(prefabPath);

        int count = 0;

        // Remove from root and all children
        foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            count += removed;
        }

        // Save prefab
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        PrefabUtility.UnloadPrefabContents(instance);

        Debug.Log($"Removed {count} missing scripts from {prefabPath}");
        EditorUtility.DisplayDialog("Done", $"Removed {count} missing scripts from Player prefab.", "OK");
    }
}
