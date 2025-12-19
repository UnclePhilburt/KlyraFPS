using UnityEngine;

public class BloodEffectManager : MonoBehaviour
{
    public static BloodEffectManager Instance { get; private set; }

    [Header("Blood Prefabs (Auto-loaded if empty)")]
    public GameObject bloodHitPrefab;
    public GameObject bloodDeathPrefab;

    private static bool initialized = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPrefabs();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LoadPrefabs()
    {
        if (initialized) return;

        // Try to find prefabs in the project
        if (bloodHitPrefab == null)
        {
            bloodHitPrefab = FindPrefabByName("FX_BloodSplat_Small_01");
        }

        if (bloodDeathPrefab == null)
        {
            bloodDeathPrefab = FindPrefabByName("FX_BloodSplat_01");

            // If not found, try the bloody explosion
            if (bloodDeathPrefab == null)
            {
                bloodDeathPrefab = FindPrefabByName("FX_Explosion_Body_Bloody_01");
            }
        }

        initialized = true;
        Debug.Log($"BloodEffectManager initialized - Hit: {(bloodHitPrefab != null ? bloodHitPrefab.name : "NULL")}, Death: {(bloodDeathPrefab != null ? bloodDeathPrefab.name : "NULL")}");
    }

    GameObject FindPrefabByName(string name)
    {
        // Try Resources with FX subfolder first
        GameObject prefab = Resources.Load<GameObject>($"FX/{name}");
        if (prefab != null)
        {
            Debug.Log($"Loaded blood prefab from Resources/FX/{name}");
            return prefab;
        }

        // Try Resources root
        prefab = Resources.Load<GameObject>(name);
        if (prefab != null)
        {
            Debug.Log($"Loaded blood prefab from Resources/{name}");
            return prefab;
        }

        #if UNITY_EDITOR
        // In editor, search the asset database
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:Prefab {name}");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains(name))
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    Debug.Log($"Found blood prefab via AssetDatabase: {path}");
                    return prefab;
                }
            }
        }
        #endif

        Debug.LogWarning($"BloodEffectManager: Could not find prefab '{name}'");
        return null;
    }

    // Static method to spawn blood hit effect
    public static void SpawnBloodHit(Vector3 position, Vector3 damageSource = default)
    {
        EnsureInstance();

        if (Instance == null || Instance.bloodHitPrefab == null) return;

        Quaternion rotation = Quaternion.identity;
        if (damageSource != default)
        {
            Vector3 bloodDir = (position - damageSource).normalized;
            if (bloodDir != Vector3.zero)
            {
                rotation = Quaternion.LookRotation(bloodDir);
            }
        }

        GameObject blood = Instantiate(Instance.bloodHitPrefab, position, rotation);
        Destroy(blood, 3f);
    }

    // Static method to spawn blood death effect
    public static void SpawnBloodDeath(Vector3 position)
    {
        EnsureInstance();

        if (Instance == null || Instance.bloodDeathPrefab == null) return;

        GameObject blood = Instantiate(Instance.bloodDeathPrefab, position, Quaternion.identity);
        Destroy(blood, 5f);
    }

    static void EnsureInstance()
    {
        if (Instance == null)
        {
            GameObject managerObj = new GameObject("BloodEffectManager");
            Instance = managerObj.AddComponent<BloodEffectManager>();
        }
    }

    // Call this from any script's Start to ensure manager exists
    public static void Initialize()
    {
        EnsureInstance();
    }
}
