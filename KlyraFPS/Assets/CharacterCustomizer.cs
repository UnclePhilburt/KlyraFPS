using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages character customization data and preview for the main menu.
/// Handles both Phantom (USA) and Havoc (Russia) team customization.
/// </summary>
public class CharacterCustomizer : MonoBehaviour
{
    [System.Serializable]
    public class TeamCustomization
    {
        public string teamName;
        public GameObject[] baseCharacters;
        public string[] characterNames;
        public GameObject[] headgear;
        public string[] headgearNames;
        public GameObject[] facewear;
        public string[] facewearNames;
        public GameObject[] backpacks;
        public string[] backpackNames;
    }

    [Header("Team Options")]
    public TeamCustomization phantomOptions;
    public TeamCustomization havocOptions;

    [Header("Dog Options")]
    public GameObject[] dogPrefabs;
    public string[] dogNames;

    [Header("Preview Settings")]
    public float rotationSpeed = 30f;
    public Vector3 previewPosition = new Vector3(100, 0, 0);
    public Vector3 previewRotation = new Vector3(0, 180, 0);

    // Current preview
    private GameObject currentPreviewCharacter;
    private GameObject currentHeadgear;
    private GameObject currentFacewear;
    private GameObject currentBackpack;
    private Transform headAttachPoint;
    private Transform faceAttachPoint;
    private Transform backAttachPoint;

    // Selection indices
    private Dictionary<string, int> selections = new Dictionary<string, int>();

    void Awake()
    {
        // Ensure team options are initialized
        if (phantomOptions == null)
        {
            phantomOptions = new TeamCustomization();
            phantomOptions.teamName = "Phantom";
        }
        if (havocOptions == null)
        {
            havocOptions = new TeamCustomization();
            havocOptions.teamName = "Havoc";
        }

        Debug.Log($"[CharacterCustomizer] Awake - dogPrefabs assigned: {dogPrefabs != null}, count: {(dogPrefabs != null ? dogPrefabs.Length : 0)}");

        LoadAllSelections();
    }

    public void LoadAllSelections()
    {
        // Load Phantom selections
        selections["Phantom_Character"] = PlayerPrefs.GetInt("Phantom_CharacterIndex", 0);
        selections["Phantom_Headgear"] = PlayerPrefs.GetInt("Phantom_HeadgearIndex", -1);
        selections["Phantom_Facewear"] = PlayerPrefs.GetInt("Phantom_FacewearIndex", -1);
        selections["Phantom_Backpack"] = PlayerPrefs.GetInt("Phantom_BackpackIndex", -1);

        // Load Havoc selections
        selections["Havoc_Character"] = PlayerPrefs.GetInt("Havoc_CharacterIndex", 0);
        selections["Havoc_Headgear"] = PlayerPrefs.GetInt("Havoc_HeadgearIndex", -1);
        selections["Havoc_Facewear"] = PlayerPrefs.GetInt("Havoc_FacewearIndex", -1);
        selections["Havoc_Backpack"] = PlayerPrefs.GetInt("Havoc_BackpackIndex", -1);

        // Load Dog selections (shared across teams, or per-team if desired)
        selections["Phantom_Dog"] = PlayerPrefs.GetInt("Phantom_DogIndex", 0);
        selections["Havoc_Dog"] = PlayerPrefs.GetInt("Havoc_DogIndex", 0);
    }

    public void SaveAllSelections()
    {
        PlayerPrefs.SetInt("Phantom_CharacterIndex", GetSelection("Phantom", "Character"));
        PlayerPrefs.SetInt("Phantom_HeadgearIndex", GetSelection("Phantom", "Headgear"));
        PlayerPrefs.SetInt("Phantom_FacewearIndex", GetSelection("Phantom", "Facewear"));
        PlayerPrefs.SetInt("Phantom_BackpackIndex", GetSelection("Phantom", "Backpack"));

        PlayerPrefs.SetInt("Havoc_CharacterIndex", GetSelection("Havoc", "Character"));
        PlayerPrefs.SetInt("Havoc_HeadgearIndex", GetSelection("Havoc", "Headgear"));
        PlayerPrefs.SetInt("Havoc_FacewearIndex", GetSelection("Havoc", "Facewear"));
        PlayerPrefs.SetInt("Havoc_BackpackIndex", GetSelection("Havoc", "Backpack"));

        // Save dog selections
        PlayerPrefs.SetInt("Phantom_DogIndex", GetSelection("Phantom", "Dog"));
        PlayerPrefs.SetInt("Havoc_DogIndex", GetSelection("Havoc", "Dog"));

        PlayerPrefs.Save();
    }

    public int GetSelection(string team, string category)
    {
        string key = $"{team}_{category}";
        return selections.ContainsKey(key) ? selections[key] : (category == "Character" ? 0 : -1);
    }

    public void SetSelection(string team, string category, int index)
    {
        string key = $"{team}_{category}";
        selections[key] = index;
    }

    public TeamCustomization GetTeamOptions(string team)
    {
        return team == "Phantom" ? phantomOptions : havocOptions;
    }

    public int GetOptionCount(string team, string category)
    {
        // Dog is shared across teams
        if (category == "Dog")
        {
            return dogPrefabs?.Length ?? 0;
        }

        var options = GetTeamOptions(team);
        if (options == null) return 0;

        switch (category)
        {
            case "Character": return options.baseCharacters?.Length ?? 0;
            case "Headgear": return options.headgear?.Length ?? 0;
            case "Facewear": return options.facewear?.Length ?? 0;
            case "Backpack": return options.backpacks?.Length ?? 0;
            default: return 0;
        }
    }

    public string GetOptionName(string team, string category, int index)
    {
        if (index < 0) return "None";

        // Handle dog category separately (shared across teams)
        if (category == "Dog")
        {
            if (dogNames != null && index < dogNames.Length && !string.IsNullOrEmpty(dogNames[index]))
                return dogNames[index];
            if (dogPrefabs != null && index < dogPrefabs.Length && dogPrefabs[index] != null)
                return CleanDogName(dogPrefabs[index].name);
            return "Unknown";
        }

        var options = GetTeamOptions(team);
        if (options == null) return "No Team Data";

        string[] names = null;
        GameObject[] prefabs = null;

        switch (category)
        {
            case "Character":
                names = options.characterNames;
                prefabs = options.baseCharacters;
                break;
            case "Headgear":
                names = options.headgearNames;
                prefabs = options.headgear;
                break;
            case "Facewear":
                names = options.facewearNames;
                prefabs = options.facewear;
                break;
            case "Backpack":
                names = options.backpackNames;
                prefabs = options.backpacks;
                break;
        }

        if (names != null && index < names.Length && !string.IsNullOrEmpty(names[index]))
            return names[index];

        if (prefabs != null && index < prefabs.Length && prefabs[index] != null)
            return CleanPrefabName(prefabs[index].name);

        return "Unknown";
    }

    public GameObject GetOptionPrefab(string team, string category, int index)
    {
        if (index < 0) return null;

        // Handle dog category separately
        if (category == "Dog")
        {
            if (dogPrefabs != null && index < dogPrefabs.Length)
                return dogPrefabs[index];
            return null;
        }

        var options = GetTeamOptions(team);
        if (options == null) return null;

        GameObject[] prefabs = null;

        switch (category)
        {
            case "Character": prefabs = options.baseCharacters; break;
            case "Headgear": prefabs = options.headgear; break;
            case "Facewear": prefabs = options.facewear; break;
            case "Backpack": prefabs = options.backpacks; break;
        }

        if (prefabs != null && index < prefabs.Length)
            return prefabs[index];

        return null;
    }

    // Convenience method to get the currently selected dog prefab for a team
    public GameObject GetSelectedDogPrefab(string team)
    {
        int index = GetSelection(team, "Dog");
        Debug.Log($"[CharacterCustomizer] GetSelectedDogPrefab: team={team}, index={index}, dogPrefabs={dogPrefabs}, length={(dogPrefabs != null ? dogPrefabs.Length : 0)}");

        if (dogPrefabs != null && dogPrefabs.Length > 0)
        {
            Debug.Log($"[CharacterCustomizer] First dog prefab: {(dogPrefabs[0] != null ? dogPrefabs[0].name : "NULL")}");
        }

        return GetOptionPrefab(team, "Dog", index);
    }

    string CleanPrefabName(string name)
    {
        // Remove common prefixes
        name = name.Replace("SM_Chr_", "");
        name = name.Replace("SM_Char_", "");
        name = name.Replace("Attach_", "");

        // Replace underscores with spaces
        name = name.Replace("_", " ");

        return name;
    }

    string CleanDogName(string name)
    {
        // Remove common dog prefab prefixes
        name = name.Replace("Unity_SK_Animals_Dog_", "");
        name = name.Replace("_Collar", " (Collar)");
        name = name.Replace("_01", "");
        name = name.Replace("_", " ");
        return name;
    }

    public void CycleSelection(string team, string category, int direction)
    {
        int current = GetSelection(team, category);
        int count = GetOptionCount(team, category);

        if (count == 0) return;

        // For Character and Dog, always have one selected (no "None" option)
        // For attachments, -1 means "None"
        int minValue = (category == "Character" || category == "Dog") ? 0 : -1;
        int maxValue = count - 1;

        current += direction;

        if (current > maxValue) current = minValue;
        if (current < minValue) current = maxValue;

        SetSelection(team, category, current);
    }

    // Preview management
    public GameObject CreatePreview(string team, Transform parent)
    {
        ClearPreview();

        int charIndex = GetSelection(team, "Character");
        Debug.Log($"[CharacterCustomizer] Creating preview - Team: {team}, CharIndex: {charIndex}");

        var prefab = GetOptionPrefab(team, "Character", charIndex);

        if (prefab == null)
        {
            Debug.LogWarning($"[CharacterCustomizer] No prefab found for {team} character index {charIndex}");
            return null;
        }

        Debug.Log($"[CharacterCustomizer] Instantiating prefab: {prefab.name}");
        currentPreviewCharacter = Instantiate(prefab, parent);
        currentPreviewCharacter.name = "CustomizePreview";
        currentPreviewCharacter.transform.localPosition = Vector3.zero;
        currentPreviewCharacter.transform.localRotation = Quaternion.Euler(previewRotation);

        Debug.Log($"[CharacterCustomizer] Preview created at world position: {currentPreviewCharacter.transform.position}");

        // Disable gameplay components
        DisableGameplayComponents(currentPreviewCharacter);

        // Find attachment points
        FindAttachmentPoints(currentPreviewCharacter);

        // Apply attachments
        ApplyAttachments(team);

        return currentPreviewCharacter;
    }

    void FindAttachmentPoints(GameObject character)
    {
        // Common attachment point names in Synty characters
        var transforms = character.GetComponentsInChildren<Transform>();
        foreach (var t in transforms)
        {
            string name = t.name.ToLower();
            if (name.Contains("head") && !name.Contains("headset"))
            {
                if (headAttachPoint == null) headAttachPoint = t;
            }
            else if (name.Contains("spine") || name.Contains("chest"))
            {
                if (backAttachPoint == null) backAttachPoint = t;
            }
            else if (name.Contains("neck") || name.Contains("jaw"))
            {
                if (faceAttachPoint == null) faceAttachPoint = t;
            }
        }

        // Fallback - use head for all if specific points not found
        if (faceAttachPoint == null) faceAttachPoint = headAttachPoint;
        if (backAttachPoint == null)
        {
            var spineSearch = character.GetComponentsInChildren<Transform>();
            foreach (var t in spineSearch)
            {
                if (t.name.ToLower().Contains("spine"))
                {
                    backAttachPoint = t;
                    break;
                }
            }
        }
    }

    void ApplyAttachments(string team)
    {
        // Headgear
        int headIndex = GetSelection(team, "Headgear");
        if (headIndex >= 0 && headAttachPoint != null)
        {
            var headPrefab = GetOptionPrefab(team, "Headgear", headIndex);
            if (headPrefab != null)
            {
                currentHeadgear = Instantiate(headPrefab, headAttachPoint);
                currentHeadgear.transform.localPosition = Vector3.zero;
                currentHeadgear.transform.localRotation = Quaternion.identity;
            }
        }

        // Facewear
        int faceIndex = GetSelection(team, "Facewear");
        if (faceIndex >= 0 && faceAttachPoint != null)
        {
            var facePrefab = GetOptionPrefab(team, "Facewear", faceIndex);
            if (facePrefab != null)
            {
                currentFacewear = Instantiate(facePrefab, faceAttachPoint);
                currentFacewear.transform.localPosition = Vector3.zero;
                currentFacewear.transform.localRotation = Quaternion.identity;
            }
        }

        // Backpack
        int backIndex = GetSelection(team, "Backpack");
        if (backIndex >= 0 && backAttachPoint != null)
        {
            var backPrefab = GetOptionPrefab(team, "Backpack", backIndex);
            if (backPrefab != null)
            {
                currentBackpack = Instantiate(backPrefab, backAttachPoint);
                currentBackpack.transform.localPosition = Vector3.zero;
                currentBackpack.transform.localRotation = Quaternion.identity;
            }
        }
    }

    public void RefreshPreview(string team, Transform parent)
    {
        CreatePreview(team, parent);
    }

    public void ClearPreview()
    {
        if (currentPreviewCharacter != null) Destroy(currentPreviewCharacter);
        if (currentHeadgear != null) Destroy(currentHeadgear);
        if (currentFacewear != null) Destroy(currentFacewear);
        if (currentBackpack != null) Destroy(currentBackpack);

        currentPreviewCharacter = null;
        currentHeadgear = null;
        currentFacewear = null;
        currentBackpack = null;
        headAttachPoint = null;
        faceAttachPoint = null;
        backAttachPoint = null;
    }

    public void RotatePreview(float deltaTime)
    {
        if (currentPreviewCharacter != null)
        {
            currentPreviewCharacter.transform.Rotate(Vector3.up, rotationSpeed * deltaTime, Space.World);
        }
    }

    void DisableGameplayComponents(GameObject obj)
    {
        // Disable AI, physics, etc.
        var components = obj.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var comp in components)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName.Contains("AI") || typeName.Contains("Weapon") ||
                typeName.Contains("Health") || typeName.Contains("NavMesh") ||
                typeName.Contains("Photon") || typeName.Contains("Player"))
            {
                comp.enabled = false;
            }
        }

        // Disable rigidbodies
        var rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rigidbodies) rb.isKinematic = true;

        // Disable colliders
        var colliders = obj.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders) col.enabled = false;

        // Disable audio
        var audioSources = obj.GetComponentsInChildren<AudioSource>(true);
        foreach (var audio in audioSources) audio.enabled = false;

        // Disable animator (we'll handle idle manually if needed)
        var animators = obj.GetComponentsInChildren<Animator>(true);
        foreach (var anim in animators) anim.enabled = false;
    }
}
