using UnityEngine;
using UnityEditor;
using Mirror;
using Mirror.SimpleWeb;

public class NetworkSetup : EditorWindow
{
    [MenuItem("Tools/Setup Mirror Network")]
    public static void SetupNetwork()
    {
        // Find or create NetworkManager
        GameObject nmObj = GameObject.Find("NetworkManager");
        if (nmObj == null)
        {
            nmObj = new GameObject("NetworkManager");
        }

        // Add NetworkManager
        NetworkManager nm = nmObj.GetComponent<NetworkManager>();
        if (nm == null)
        {
            nm = nmObj.AddComponent<NetworkManager>();
        }

        // Add SimpleWebTransport
        var transport = nmObj.GetComponent<SimpleWebTransport>();
        if (transport == null)
        {
            transport = nmObj.AddComponent<SimpleWebTransport>();
        }
        transport.port = 7777;

        // Set transport on NetworkManager
        nm.transport = transport;

        // Add SimpleNetworkMenu
        var menu = nmObj.GetComponent<SimpleNetworkMenu>();
        if (menu == null)
        {
            menu = nmObj.AddComponent<SimpleNetworkMenu>();
        }
        menu.serverAddress = "klyrafps.fly.dev";
        menu.serverPort = 7777;

        // Find player prefab and setup
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null && prefab.GetComponent<FPSController>() != null)
            {
                // Add NetworkIdentity if missing
                if (prefab.GetComponent<NetworkIdentity>() == null)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    instance.AddComponent<NetworkIdentity>();
                    PrefabUtility.SaveAsPrefabAsset(instance, path);
                    DestroyImmediate(instance);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }

                nm.playerPrefab = prefab;
                Debug.Log($"Set player prefab to: {prefab.name}");
                break;
            }
        }

        // Mark scene dirty
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Selection.activeGameObject = nmObj;

        Debug.Log("Mirror Network setup complete!");
        EditorUtility.DisplayDialog("Setup Complete",
            "Mirror NetworkManager has been configured!\n\n" +
            "- NetworkManager added\n" +
            "- SimpleWebTransport added (port 7777)\n" +
            "- SimpleNetworkMenu added\n" +
            "- Player prefab configured", "OK");
    }
}
