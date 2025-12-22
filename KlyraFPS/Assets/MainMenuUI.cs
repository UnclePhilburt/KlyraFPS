using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;

// Simple component to mark click zones on the character
public class ClickZone : MonoBehaviour
{
    public string category;
}

/// <summary>
/// Battlefield-style Main Menu - Dark theme with orange accents, soldier showcase, card layout.
/// </summary>
public class MainMenuUI : MonoBehaviourPunCallbacks
{
    [Header("Config")]
    public string gameSceneName = "Demo";
    public string gameTitle = "KLYRA";

    [Header("Showcase Models")]
    public GameObject[] soldierPrefabs;
    public GameObject[] vehiclePrefabs;
    public GameObject[] propPrefabs;

    [Header("Animation")]
    public RuntimeAnimatorController soldierAnimator;
    public AnimationClip idleAnimationClip;

    [Header("Effects")]
    public GameObject dustPrefab;

    [Header("Music")]
    public AudioClip[] menuMusic;
    [Range(0f, 1f)]
    public float musicVolume = 0.5f;
    private AudioSource musicSource;

    [Header("Character Customizer")]
    public CharacterCustomizer characterCustomizer;
    private string selectedCustomizeTeam = "Phantom";
    private GameObject bodyPartPopup;
    private string currentBodyPart;
    private TextMeshProUGUI popupLabel;
    private TextMeshProUGUI popupValue;
    private bool isCustomizing = false;

    [Header("Weapon Customizer")]
    public WeaponCustomizer weaponCustomizer;
    private bool isCustomizingWeapon = false;
    private GameObject weaponPreview;
    private GameObject weaponEditorPanel;
    private TextMeshProUGUI[] statBarLabels;
    private Image[] statBarFills;
    private TextMeshProUGUI currentPartLabel;
    private TextMeshProUGUI currentPartValue;
    private string selectedWeaponPart = "Barrel";
    private int currentEditingLoadout = 0;

    // Colors - Modern Warfare style
    private Color bgDark = new Color(0.06f, 0.06f, 0.08f);
    private Color cardBg = new Color(0.1f, 0.1f, 0.12f);
    private Color cardHover = new Color(0.15f, 0.15f, 0.18f);
    private Color accentOrange = new Color(1f, 0.5f, 0.1f);
    private Color accentAmber = new Color(1f, 0.65f, 0.2f);
    private Color textWhite = new Color(0.95f, 0.95f, 0.95f);
    private Color textGray = new Color(0.5f, 0.5f, 0.55f);

    // Cached shader
    private Shader litShader;

    // Scene
    private Camera menuCamera;
    private GameObject showcaseModel;
    private GameObject playerShowcaseCharacter;
    private GameObject showcaseDog;
    private GameObject[] currentAttachments = new GameObject[3]; // headgear, facewear, backpack

    // Cached original attachment transforms (stored once per character)
    private Vector3 origHeadgearPos, origFacewearPos, origBackpackPos;
    private Quaternion origHeadgearRot, origFacewearRot, origBackpackRot;
    private Vector3 origHeadgearScale, origFacewearScale, origBackpackScale;
    private bool hasOriginalTransforms = false;
    private bool foundOrigHeadgear = false, foundOrigFacewear = false, foundOrigBackpack = false;
    private Light keyLight, fillLight, rimLight;
    private ParticleSystem dustParticles;

    // Camera positions
    private Vector3 menuCameraPos;
    private Quaternion menuCameraRot;
    private float menuCameraFOV;

    // UI
    private Canvas canvas;
    private CanvasGroup mainMenuGroup, settingsGroup, customizeGroup, loadingGroup;
    private TMP_InputField nameInput;
    private TextMeshProUGUI statusText;
    private Image loadingBar;
    private AudioSource sfx;
    private bool isConnected;
    private float loadProgress;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Auto-find CharacterCustomizer if not assigned
        if (characterCustomizer == null)
        {
            characterCustomizer = GetComponent<CharacterCustomizer>();
            if (characterCustomizer == null)
            {
                characterCustomizer = GetComponentInChildren<CharacterCustomizer>();
            }
            if (characterCustomizer == null)
            {
                // Search the entire scene as last resort
                characterCustomizer = FindFirstObjectByType<CharacterCustomizer>();
            }
            if (characterCustomizer != null)
            {
                Debug.Log($"[MainMenuUI] Found CharacterCustomizer on: {characterCustomizer.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("[MainMenuUI] CharacterCustomizer not found! Add a CharacterCustomizer component to the scene.");
            }
        }

        // Auto-find WeaponCustomizer if not assigned
        if (weaponCustomizer == null)
        {
            // Try singleton first
            weaponCustomizer = WeaponCustomizer.Instance;
            if (weaponCustomizer == null)
            {
                weaponCustomizer = GetComponent<WeaponCustomizer>();
            }
            if (weaponCustomizer == null)
            {
                weaponCustomizer = GetComponentInChildren<WeaponCustomizer>();
            }
            if (weaponCustomizer == null)
            {
                weaponCustomizer = FindFirstObjectByType<WeaponCustomizer>();
            }
            if (weaponCustomizer != null)
            {
                Debug.Log($"[MainMenuUI] Found WeaponCustomizer on: {weaponCustomizer.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("[MainMenuUI] WeaponCustomizer not found! Add one to the scene and run Auto-Populate.");
            }
        }

        Build3DScene();
        BuildUI();
        ConnectToPhoton();
        PlayMenuMusic();

        StartCoroutine(AnimateShowcase());
    }

    void PlayMenuMusic()
    {
        if (menuMusic == null || menuMusic.Length == 0) return;

        // Create audio source for music
        var musicObj = new GameObject("MenuMusic");
        musicObj.transform.SetParent(transform);
        musicSource = musicObj.AddComponent<AudioSource>();
        musicSource.loop = false;
        musicSource.volume = musicVolume * PlayerPrefs.GetFloat("MusicVolume", 0.7f);
        musicSource.playOnAwake = false;

        // Start playing a random track
        StartCoroutine(MusicPlaylist());
    }

    IEnumerator MusicPlaylist()
    {
        // Shuffle the playlist
        int[] shuffledIndices = new int[menuMusic.Length];
        for (int i = 0; i < shuffledIndices.Length; i++) shuffledIndices[i] = i;
        for (int i = shuffledIndices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = shuffledIndices[i];
            shuffledIndices[i] = shuffledIndices[j];
            shuffledIndices[j] = temp;
        }

        int currentIndex = 0;
        while (true)
        {
            var clip = menuMusic[shuffledIndices[currentIndex]];
            if (clip != null)
            {
                musicSource.clip = clip;
                musicSource.volume = musicVolume * PlayerPrefs.GetFloat("MusicVolume", 0.7f);
                musicSource.Play();

                // Wait for song to finish
                yield return new WaitForSecondsRealtime(clip.length);
            }

            // Move to next track
            currentIndex = (currentIndex + 1) % shuffledIndices.Length;

            // Small gap between songs
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    Shader GetLitShader()
    {
        if (litShader != null) return litShader;

        // Try URP first
        litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader != null) return litShader;

        // Try HDRP
        litShader = Shader.Find("HDRP/Lit");
        if (litShader != null) return litShader;

        // Fallback to Standard (Built-in)
        litShader = Shader.Find("Standard");
        return litShader;
    }

    Sprite CreateRoundedRectSprite(int width, int height, int radius)
    {
        // Create a texture with rounded corners
        int texSize = Mathf.Max(radius * 2 + 4, 32);
        var tex = new Texture2D(texSize, texSize);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[texSize * texSize];
        float r = radius;
        Vector2 center = new Vector2(texSize / 2f, texSize / 2f);

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - center.x) - (center.x - r), 0);
                float dy = Mathf.Max(Mathf.Abs(y - center.y) - (center.y - r), 0);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Smooth edge with anti-aliasing
                float alpha = Mathf.Clamp01(r - dist + 0.5f);
                pixels[y * texSize + x] = new Color(1, 1, 1, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Create sprite with 9-slice borders
        int border = radius + 1;
        var sprite = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));

        return sprite;
    }

    Material CreateMaterial(Color color, float metallic = 0f, float smoothness = 0.5f)
    {
        var shader = GetLitShader();
        var mat = new Material(shader);

        // Set color - works for both URP and Standard
        mat.SetColor("_BaseColor", color);  // URP
        mat.SetColor("_Color", color);       // Standard fallback
        mat.color = color;

        // Set metallic/smoothness
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);  // URP
        mat.SetFloat("_Glossiness", smoothness);  // Standard

        return mat;
    }

    void Build3DScene()
    {
        // Disable any existing cameras first
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (cam.gameObject.name != "MenuCamera")
            {
                Debug.Log($"[MainMenuUI] Disabling existing camera: {cam.gameObject.name}");
                cam.enabled = false;
            }
        }

        // Camera - NOT parented to anything so it doesn't inherit transforms
        var camObj = new GameObject("MenuCamera");
        camObj.transform.position = new Vector3(0, 1.2f, 3f);
        camObj.transform.rotation = Quaternion.Euler(5, 180, 0);
        menuCamera = camObj.AddComponent<Camera>();
        menuCamera.clearFlags = CameraClearFlags.SolidColor;
        menuCamera.backgroundColor = bgDark;
        menuCamera.fieldOfView = 40;
        menuCamera.depth = 10; // Higher depth to render on top
        camObj.AddComponent<AudioListener>();

        // Three-point lighting for soldier showcase
        // Key light - main dramatic light
        var keyObj = new GameObject("KeyLight");
        keyObj.transform.SetParent(transform);
        keyObj.transform.position = new Vector3(2, 3, 2);
        keyObj.transform.rotation = Quaternion.Euler(40, -135, 0);
        keyLight = keyObj.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1f, 0.95f, 0.9f);
        keyLight.intensity = 1.5f;

        // Fill light - softer, opposite side
        var fillObj = new GameObject("FillLight");
        fillObj.transform.SetParent(transform);
        fillObj.transform.position = new Vector3(-3, 2, 1);
        fillLight = fillObj.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.color = new Color(0.6f, 0.7f, 0.9f);
        fillLight.intensity = 0.8f;
        fillLight.range = 10f;

        // Rim light - edge highlight with orange accent
        var rimObj = new GameObject("RimLight");
        rimObj.transform.SetParent(transform);
        rimObj.transform.position = new Vector3(0, 2, -2);
        rimLight = rimObj.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.color = accentOrange;
        rimLight.intensity = 2f;
        rimLight.range = 8f;

        // Ground plane - large enough for full-size vehicles spread out
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(transform);
        ground.transform.position = new Vector3(0, 0, -15f);
        ground.transform.localScale = new Vector3(15, 1, 15);
        ground.GetComponent<Renderer>().material = CreateMaterial(new Color(0.05f, 0.05f, 0.07f), 0.3f, 0.7f);
        Destroy(ground.GetComponent<Collider>());

        // Showcase model
        SpawnShowcaseModel();

        // Atmospheric dust - use prefab if available, otherwise generate
        if (dustPrefab != null)
        {
            // Spawn multiple dust effects for coverage across the larger scene
            Vector3[] dustPositions = {
                new Vector3(0, 0, -3f),
                new Vector3(-8f, 0, -8f),
                new Vector3(8f, 0, -8f),
                new Vector3(0, 0, -15f),
                new Vector3(-15f, 0, -18f),
                new Vector3(15f, 0, -18f),
                new Vector3(0, 0, -25f),
            };

            foreach (var pos in dustPositions)
            {
                var dust = Instantiate(dustPrefab, pos, Quaternion.identity);
                dust.transform.SetParent(transform);
                dust.name = "DustEffect";
            }
        }
        else
        {
            // Fallback to generated particles
            var dustObj = new GameObject("DustParticles");
            dustObj.transform.SetParent(transform);
            dustObj.transform.position = new Vector3(0, 2, -5f);
            dustParticles = dustObj.AddComponent<ParticleSystem>();
            var dustMain = dustParticles.main;
            dustMain.startLifetime = 8f;
            dustMain.startSpeed = 0.15f;
            dustMain.startSize = 0.03f;
            dustMain.maxParticles = 200;
            dustMain.startColor = new Color(1f, 0.9f, 0.8f, 0.25f);
            dustMain.simulationSpace = ParticleSystemSimulationSpace.World;

            var dustShape = dustParticles.shape;
            dustShape.shapeType = ParticleSystemShapeType.Box;
            dustShape.scale = new Vector3(20, 5, 20);

            var dustEmission = dustParticles.emission;
            dustEmission.rateOverTime = 40;

            var dustRenderer = dustObj.GetComponent<ParticleSystemRenderer>();
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit");
            dustRenderer.material = new Material(particleShader);
            dustRenderer.material.color = new Color(1f, 0.9f, 0.7f, 0.4f);
        }
    }

    void SpawnShowcaseModel()
    {
        // Create showcase container
        showcaseModel = new GameObject("ShowcaseScene");
        showcaseModel.transform.SetParent(transform);
        showcaseModel.transform.position = Vector3.zero;

        bool hasContent = false;

        // Spawn the player's own character from their saved customization
        if (characterCustomizer != null)
        {
            // Try to spawn player's Phantom character as the main showcase
            var prefab = characterCustomizer.GetOptionPrefab("Phantom", "Character",
                characterCustomizer.GetSelection("Phantom", "Character"));

            if (prefab != null)
            {
                hasContent = true;
                playerShowcaseCharacter = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0, 170, 0));
                playerShowcaseCharacter.transform.SetParent(showcaseModel.transform);
                playerShowcaseCharacter.name = "PlayerCharacter";
                SetupShowcaseSoldier(playerShowcaseCharacter);

                // Apply saved attachments
                ApplyShowcaseAttachments("Phantom");

                // Spawn the player's dog next to the character
                SpawnShowcaseDog("Phantom");
            }
        }

        // Fallback to soldierPrefabs if no customizer or no saved character
        if (!hasContent && soldierPrefabs != null && soldierPrefabs.Length > 0)
        {
            hasContent = true;
            playerShowcaseCharacter = Instantiate(soldierPrefabs[0], Vector3.zero, Quaternion.Euler(0, 170, 0));
            playerShowcaseCharacter.transform.SetParent(showcaseModel.transform);
            playerShowcaseCharacter.name = "PlayerCharacter";
            SetupShowcaseSoldier(playerShowcaseCharacter);
        }

        // Spawn vehicles in a circle around the character
        if (vehiclePrefabs != null && vehiclePrefabs.Length > 0)
        {
            hasContent = true;

            float vehicleRadius = 18f; // Distance from center
            int vehicleCount = Mathf.Min(vehiclePrefabs.Length, 8);

            for (int i = 0; i < vehicleCount; i++)
            {
                if (vehiclePrefabs[i] == null) continue;

                // Spread vehicles evenly around a circle
                float angle = (360f / vehicleCount) * i;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    Mathf.Sin(radians) * vehicleRadius,
                    0,
                    Mathf.Cos(radians) * vehicleRadius
                );

                var vehicle = Instantiate(vehiclePrefabs[i], Vector3.zero, Quaternion.identity);
                vehicle.transform.SetParent(showcaseModel.transform);
                vehicle.name = $"Vehicle_{i}";
                DisableGameplayComponents(vehicle);

                vehicle.transform.position = pos;
                // Face toward center with some variation
                vehicle.transform.rotation = Quaternion.Euler(0, angle + 180f + Random.Range(-20f, 20f), 0);
            }
        }

        // Spawn props in rings around the character
        if (propPrefabs != null && propPrefabs.Length > 0)
        {
            hasContent = true;

            int propIndex = 0;

            // Inner ring of props (close to character)
            float innerRadius = 4f;
            int innerCount = 6;
            for (int i = 0; i < innerCount && propIndex < propPrefabs.Length; i++)
            {
                if (propPrefabs[propIndex] == null) { propIndex++; continue; }

                float angle = (360f / innerCount) * i + 30f; // Offset from vehicles
                float radians = angle * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    Mathf.Sin(radians) * innerRadius,
                    0,
                    Mathf.Cos(radians) * innerRadius
                );

                var prop = Instantiate(propPrefabs[propIndex], Vector3.zero, Quaternion.identity);
                prop.transform.SetParent(showcaseModel.transform);
                prop.name = $"Prop_Inner_{i}";
                DisableGameplayComponents(prop);

                prop.transform.position = pos;
                prop.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                propIndex++;
            }

            // Middle ring of props
            float midRadius = 9f;
            int midCount = 8;
            for (int i = 0; i < midCount && propIndex < propPrefabs.Length; i++)
            {
                if (propPrefabs[propIndex] == null) { propIndex++; continue; }

                float angle = (360f / midCount) * i + 22.5f; // Offset between inner props
                float radians = angle * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    Mathf.Sin(radians) * midRadius,
                    0,
                    Mathf.Cos(radians) * midRadius
                );

                var prop = Instantiate(propPrefabs[propIndex], Vector3.zero, Quaternion.identity);
                prop.transform.SetParent(showcaseModel.transform);
                prop.name = $"Prop_Mid_{i}";
                DisableGameplayComponents(prop);

                prop.transform.position = pos;
                prop.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                propIndex++;
            }

            // Outer ring of props (between vehicles)
            float outerRadius = 14f;
            int outerCount = 8;
            for (int i = 0; i < outerCount && propIndex < propPrefabs.Length; i++)
            {
                if (propPrefabs[propIndex] == null) { propIndex++; continue; }

                // Offset to place props between vehicles
                float angle = (360f / outerCount) * i + 22.5f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    Mathf.Sin(radians) * outerRadius,
                    0,
                    Mathf.Cos(radians) * outerRadius
                );

                var prop = Instantiate(propPrefabs[propIndex], Vector3.zero, Quaternion.identity);
                prop.transform.SetParent(showcaseModel.transform);
                prop.name = $"Prop_Outer_{i}";
                DisableGameplayComponents(prop);

                prop.transform.position = pos;
                prop.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                propIndex++;
            }
        }

        // Fallback to placeholder if nothing assigned
        if (!hasContent)
        {
            Destroy(showcaseModel);
            CreatePlaceholderSoldier();
            return;
        }

        // Adjust camera for the full scene with full-size vehicles
        if (menuCamera != null)
        {
            menuCamera.transform.position = new Vector3(0.5f, 2.2f, 6f);
            menuCamera.transform.rotation = Quaternion.Euler(8, 180, 0);
            menuCamera.fieldOfView = 55;
            menuCamera.farClipPlane = 150f;

            // Save default camera position for later
            menuCameraPos = menuCamera.transform.position;
            menuCameraRot = menuCamera.transform.rotation;
            menuCameraFOV = menuCamera.fieldOfView;
        }
    }

    void ApplyShowcaseAttachments(string team)
    {
        if (characterCustomizer == null || playerShowcaseCharacter == null) return;

        // Clear existing attachments
        for (int i = 0; i < currentAttachments.Length; i++)
        {
            if (currentAttachments[i] != null)
            {
                Destroy(currentAttachments[i]);
                currentAttachments[i] = null;
            }
        }

        // Find attachment points on the character
        Transform headPoint = null, facePoint = null, backPoint = null;
        var transforms = playerShowcaseCharacter.GetComponentsInChildren<Transform>();

        // First time: find and cache original attachment transforms, then hide them
        if (!hasOriginalTransforms)
        {
            foundOrigHeadgear = false;
            foundOrigFacewear = false;
            foundOrigBackpack = false;

            foreach (var t in transforms)
            {
                string name = t.name.ToLower();

                // Only look at original attachments (Synty uses "SM_Chr_Attach_" prefix)
                // Skip any objects we created (they'll have "(Clone)" in name)
                if (name.Contains("attach_") && !name.Contains("(clone)"))
                {
                    if (name.Contains("backpack") || name.Contains("pouch") || name.Contains("holster"))
                    {
                        if (!foundOrigBackpack)
                        {
                            origBackpackPos = t.localPosition;
                            origBackpackRot = t.localRotation;
                            origBackpackScale = t.localScale;
                            backPoint = t.parent;
                            foundOrigBackpack = true;
                        }
                        t.gameObject.SetActive(false);
                    }
                    else if (name.Contains("helmet") || name.Contains("hat") || name.Contains("hair") ||
                             name.Contains("beret") || name.Contains("cap"))
                    {
                        if (!foundOrigHeadgear)
                        {
                            origHeadgearPos = t.localPosition;
                            origHeadgearRot = t.localRotation;
                            origHeadgearScale = t.localScale;
                            foundOrigHeadgear = true;
                        }
                        t.gameObject.SetActive(false);
                    }
                    else if (name.Contains("beard") || name.Contains("glasses") || name.Contains("goggles") ||
                             name.Contains("mask") || name.Contains("nvg"))
                    {
                        if (!foundOrigFacewear)
                        {
                            origFacewearPos = t.localPosition;
                            origFacewearRot = t.localRotation;
                            origFacewearScale = t.localScale;
                            foundOrigFacewear = true;
                        }
                        t.gameObject.SetActive(false);
                    }
                    else
                    {
                        t.gameObject.SetActive(false);
                    }
                }
            }
            hasOriginalTransforms = true;
            Debug.Log($"[MainMenuUI] Found original attachments - Headgear: {foundOrigHeadgear}, Facewear: {foundOrigFacewear}, Backpack: {foundOrigBackpack}");
        }
        else
        {
            // Subsequent calls: just hide original attachments and find backPoint
            foreach (var t in transforms)
            {
                string name = t.name.ToLower();
                if (name.Contains("attach_") && !name.Contains("(clone)"))
                {
                    if (backPoint == null && (name.Contains("backpack") || name.Contains("pouch")))
                        backPoint = t.parent;
                    t.gameObject.SetActive(false);
                }
            }
        }

        // Second pass - standard bone names
        foreach (var t in transforms)
        {
            string name = t.name.ToLower();
            if (headPoint == null && name.Contains("head") && !name.Contains("headset"))
                headPoint = t;
            else if (backPoint == null && name.Contains("spine") && name.Contains("03"))
                backPoint = t; // Use spine_03 (upper back) instead of lower spine
            else if (backPoint == null && name.Contains("chest"))
                backPoint = t;
            else if (facePoint == null && (name.Contains("neck") || name.Contains("jaw")))
                facePoint = t;
        }
        if (facePoint == null) facePoint = headPoint;

        Debug.Log($"[MainMenuUI] Attachment points - Head: {headPoint?.name}, Face: {facePoint?.name}, Back: {backPoint?.name}");

        // Apply headgear
        int headIndex = characterCustomizer.GetSelection(team, "Headgear");
        if (headIndex >= 0 && headPoint != null)
        {
            var prefab = characterCustomizer.GetOptionPrefab(team, "Headgear", headIndex);
            if (prefab != null)
            {
                currentAttachments[0] = Instantiate(prefab, headPoint);
                if (foundOrigHeadgear)
                {
                    currentAttachments[0].transform.localPosition = origHeadgearPos;
                    currentAttachments[0].transform.localRotation = origHeadgearRot;
                    currentAttachments[0].transform.localScale = origHeadgearScale;
                }
                else
                {
                    // Default: place at bone origin
                    currentAttachments[0].transform.localPosition = Vector3.zero;
                    currentAttachments[0].transform.localRotation = Quaternion.identity;
                }
            }
        }

        // Apply facewear
        int faceIndex = characterCustomizer.GetSelection(team, "Facewear");
        if (faceIndex >= 0 && facePoint != null)
        {
            var prefab = characterCustomizer.GetOptionPrefab(team, "Facewear", faceIndex);
            if (prefab != null)
            {
                currentAttachments[1] = Instantiate(prefab, facePoint);
                if (foundOrigFacewear)
                {
                    currentAttachments[1].transform.localPosition = origFacewearPos;
                    currentAttachments[1].transform.localRotation = origFacewearRot;
                    currentAttachments[1].transform.localScale = origFacewearScale;
                }
                else
                {
                    // Default: place at bone origin
                    currentAttachments[1].transform.localPosition = Vector3.zero;
                    currentAttachments[1].transform.localRotation = Quaternion.identity;
                }
            }
        }

        // Apply backpack
        int backIndex = characterCustomizer.GetSelection(team, "Backpack");
        if (backIndex >= 0 && backPoint != null)
        {
            var prefab = characterCustomizer.GetOptionPrefab(team, "Backpack", backIndex);
            if (prefab != null)
            {
                currentAttachments[2] = Instantiate(prefab, backPoint);
                if (foundOrigBackpack)
                {
                    currentAttachments[2].transform.localPosition = origBackpackPos;
                    currentAttachments[2].transform.localRotation = origBackpackRot;
                    currentAttachments[2].transform.localScale = origBackpackScale;
                }
                else
                {
                    // Default: place at bone origin
                    currentAttachments[2].transform.localPosition = Vector3.zero;
                    currentAttachments[2].transform.localRotation = Quaternion.identity;
                }
            }
        }
    }

    void SetupShowcaseSoldier(GameObject soldier)
    {
        // Disable gameplay components first
        DisableGameplayComponents(soldier);

        // Setup animator for idle pose
        var animator = soldier.GetComponent<Animator>();
        if (animator == null)
        {
            animator = soldier.GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            // If we have a custom controller assigned, use it
            if (soldierAnimator != null)
            {
                animator.runtimeAnimatorController = soldierAnimator;
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.Play("Idle", 0, Random.Range(0f, 1f));
            }
            else if (idleAnimationClip != null)
            {
                // Use the idle animation clip directly with Legacy animation
                animator.enabled = false;

                // Add Legacy Animation component and play the clip
                var legacyAnim = soldier.GetComponent<Animation>();
                if (legacyAnim == null)
                {
                    legacyAnim = soldier.AddComponent<Animation>();
                }

                legacyAnim.AddClip(idleAnimationClip, "idle");
                legacyAnim.clip = idleAnimationClip;
                legacyAnim.wrapMode = WrapMode.Loop;
                legacyAnim.Play("idle");

                // Start at a random point for variety
                var state = legacyAnim["idle"];
                if (state != null)
                {
                    state.normalizedTime = Random.Range(0f, 1f);
                }
            }
            else
            {
                // Try to force idle on existing controller
                animator.applyRootMotion = false;
                animator.enabled = true;

                // Try common idle state names used in various animator controllers
                string[] idleStateNames = {
                    "Idle", "idle", "IDLE",
                    "Idle_Standing", "Standing",
                    "Base Layer.Idle", "Locomotion.Idle",
                    "Rifle Aiming Idle", "Aiming Idle"
                };

                bool foundIdle = false;
                foreach (var stateName in idleStateNames)
                {
                    // Try to play each state
                    animator.Play(stateName, 0, Random.Range(0f, 1f));
                    foundIdle = true;
                    break;
                }

                if (!foundIdle)
                {
                    // Last resort - just set speed to 0 to freeze animation
                    animator.speed = 0f;
                }

                // Set common parameters that might help
                SetAnimatorIdleParams(animator);
            }
        }
    }

    void SetAnimatorIdleParams(Animator animator)
    {
        // Try setting common parameters that gameplay animators use
        // These will silently fail if the parameter doesn't exist
        try
        {
            animator.SetFloat("Speed", 0f);
            animator.SetFloat("MoveSpeed", 0f);
            animator.SetFloat("Velocity", 0f);
            animator.SetFloat("VerticalSpeed", 0f);
            animator.SetFloat("HorizontalSpeed", 0f);
            animator.SetBool("IsGrounded", true);
            animator.SetBool("Grounded", true);
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsFalling", false);
            animator.SetBool("InAir", false);
        }
        catch { }
    }

    void SpawnShowcaseDog(string team)
    {
        // Destroy existing dog
        if (showcaseDog != null)
        {
            Destroy(showcaseDog);
            showcaseDog = null;
        }

        if (characterCustomizer == null)
        {
            Debug.LogWarning("[MainMenuUI] SpawnShowcaseDog: characterCustomizer is null!");
            return;
        }

        if (playerShowcaseCharacter == null)
        {
            Debug.LogWarning("[MainMenuUI] SpawnShowcaseDog: playerShowcaseCharacter is null!");
            return;
        }

        int dogCount = characterCustomizer.GetOptionCount(team, "Dog");
        Debug.Log($"[MainMenuUI] SpawnShowcaseDog: team={team}, dogCount={dogCount}");

        GameObject dogPrefab = characterCustomizer.GetSelectedDogPrefab(team);
        if (dogPrefab == null)
        {
            Debug.LogWarning($"[MainMenuUI] SpawnShowcaseDog: No dog prefab found for team {team}. Make sure dogPrefabs is assigned in CharacterCustomizer!");
            return;
        }

        Debug.Log($"[MainMenuUI] Spawning dog: {dogPrefab.name}");

        // Position dog to the right of the character, facing slightly toward camera
        Vector3 dogPos = playerShowcaseCharacter.transform.position + new Vector3(1.2f, 0, 0.3f);
        Quaternion dogRot = Quaternion.Euler(0, 200, 0); // Facing similar direction as player

        showcaseDog = Instantiate(dogPrefab, dogPos, dogRot);
        showcaseDog.transform.SetParent(showcaseModel.transform);
        showcaseDog.name = "ShowcaseDog";

        // Disable gameplay components
        DisableGameplayComponents(showcaseDog);

        // Setup idle animation
        var animator = showcaseDog.GetComponent<Animator>();
        if (animator == null)
        {
            animator = showcaseDog.GetComponentInChildren<Animator>();
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            // Set to idle/sit pose
            animator.SetFloat("Movement_f", 0f);
            animator.SetBool("Grounded_b", true);
            animator.SetBool("Sit_b", true); // Make dog sit in menu
        }
    }

    void DisableGameplayComponents(GameObject obj)
    {
        // Disable common gameplay components that shouldn't run in menu
        var components = obj.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var comp in components)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;

            // Disable AI, physics controllers, etc - but NOT Animator
            if (typeName.Contains("AI") ||
                typeName.Contains("Weapon") ||
                typeName.Contains("Health") ||
                typeName.Contains("NavMesh") ||
                typeName.Contains("Photon") ||
                typeName.Contains("Player") ||
                typeName.Contains("Character"))
            {
                comp.enabled = false;
            }
        }

        // Disable rigidbodies
        var rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rigidbodies)
        {
            rb.isKinematic = true;
        }

        // Disable colliders
        var colliders = obj.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
        {
            col.enabled = false;
        }

        // Disable audio sources
        var audioSources = obj.GetComponentsInChildren<AudioSource>(true);
        foreach (var audio in audioSources)
        {
            audio.enabled = false;
        }
    }

    Bounds CalculateBounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(obj.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    void CreatePlaceholderSoldier()
    {
        showcaseModel = new GameObject("SoldierPlaceholder");
        showcaseModel.transform.SetParent(transform);
        showcaseModel.transform.position = Vector3.zero;
        showcaseModel.transform.rotation = Quaternion.Euler(0, 160, 0);

        var darkMat = CreateMaterial(new Color(0.15f, 0.15f, 0.18f), 0.2f, 0.3f);
        var helmetMat = CreateMaterial(new Color(0.2f, 0.22f, 0.2f), 0.1f, 0.2f);
        var rifleMat = CreateMaterial(new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.3f);

        // Body
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(showcaseModel.transform);
        body.transform.localPosition = new Vector3(0, 1.0f, 0);
        body.transform.localScale = new Vector3(0.5f, 0.6f, 0.3f);
        body.GetComponent<Renderer>().material = darkMat;
        Destroy(body.GetComponent<Collider>());

        // Head
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(showcaseModel.transform);
        head.transform.localPosition = new Vector3(0, 1.75f, 0);
        head.transform.localScale = new Vector3(0.28f, 0.3f, 0.28f);
        head.GetComponent<Renderer>().material = darkMat;
        Destroy(head.GetComponent<Collider>());

        // Helmet
        var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        helmet.transform.SetParent(showcaseModel.transform);
        helmet.transform.localPosition = new Vector3(0, 1.82f, 0);
        helmet.transform.localScale = new Vector3(0.32f, 0.25f, 0.32f);
        helmet.GetComponent<Renderer>().material = helmetMat;
        Destroy(helmet.GetComponent<Collider>());

        // Legs
        for (int i = 0; i < 2; i++)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leg.transform.SetParent(showcaseModel.transform);
            leg.transform.localPosition = new Vector3(i == 0 ? -0.12f : 0.12f, 0.4f, 0);
            leg.transform.localScale = new Vector3(0.18f, 0.4f, 0.18f);
            leg.GetComponent<Renderer>().material = darkMat;
            Destroy(leg.GetComponent<Collider>());
        }

        // Arms
        for (int i = 0; i < 2; i++)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            arm.transform.SetParent(showcaseModel.transform);
            arm.transform.localPosition = new Vector3(i == 0 ? -0.35f : 0.35f, 1.1f, 0.1f);
            arm.transform.localRotation = Quaternion.Euler(20, 0, i == 0 ? 15 : -15);
            arm.transform.localScale = new Vector3(0.12f, 0.35f, 0.12f);
            arm.GetComponent<Renderer>().material = darkMat;
            Destroy(arm.GetComponent<Collider>());
        }

        // Weapon (rifle shape)
        var rifle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rifle.transform.SetParent(showcaseModel.transform);
        rifle.transform.localPosition = new Vector3(0.15f, 0.95f, 0.35f);
        rifle.transform.localRotation = Quaternion.Euler(-45, 0, 0);
        rifle.transform.localScale = new Vector3(0.06f, 0.06f, 0.7f);
        rifle.GetComponent<Renderer>().material = rifleMat;
        Destroy(rifle.GetComponent<Collider>());
    }

    void BuildUI()
    {
        // Canvas
        var canvasObj = new GameObject("Canvas");
        canvasObj.transform.SetParent(transform);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // Vignette overlay
        var vignette = CreateImage(canvasObj.transform, "Vignette", Color.clear);
        Stretch(vignette);
        // Dark edges gradient would go here with proper texture

        // === MAIN MENU ===
        var mainPanel = new GameObject("MainMenu");
        mainPanel.transform.SetParent(canvasObj.transform, false);
        var mainRect = mainPanel.AddComponent<RectTransform>();
        Stretch(mainRect);
        mainMenuGroup = mainPanel.AddComponent<CanvasGroup>();

        // Title - top center
        var titleObj = CreateTMP(mainPanel.transform, gameTitle, 96, FontStyles.Bold, textWhite);
        titleObj.rectTransform.anchorMin = new Vector2(0.5f, 1);
        titleObj.rectTransform.anchorMax = new Vector2(0.5f, 1);
        titleObj.rectTransform.pivot = new Vector2(0.5f, 1);
        titleObj.rectTransform.anchoredPosition = new Vector2(0, -80);
        titleObj.rectTransform.sizeDelta = new Vector2(700, 120);
        titleObj.alignment = TextAlignmentOptions.Center;
        titleObj.enableVertexGradient = true;
        titleObj.colorGradient = new VertexGradient(textWhite, textWhite, accentOrange, accentAmber);

        // Callsign section - centered under title
        var callsignLabel = CreateTMP(mainPanel.transform, "CALLSIGN", 12, FontStyles.Bold, textGray);
        callsignLabel.rectTransform.anchorMin = new Vector2(0.5f, 1);
        callsignLabel.rectTransform.anchorMax = new Vector2(0.5f, 1);
        callsignLabel.rectTransform.pivot = new Vector2(0.5f, 1);
        callsignLabel.rectTransform.anchoredPosition = new Vector2(0, -190);
        callsignLabel.rectTransform.sizeDelta = new Vector2(280, 20);
        callsignLabel.alignment = TextAlignmentOptions.Center;
        callsignLabel.characterSpacing = 3;

        nameInput = CreateInputField(mainPanel.transform, new Vector2(0, -220), new Vector2(280, 45), false);
        nameInput.text = PlayerPrefs.GetString("PlayerName", "SOLDIER_" + Random.Range(100, 999));

        // Status - bottom center
        statusText = CreateTMP(mainPanel.transform, "CONNECTING...", 14, FontStyles.Normal, accentOrange);
        statusText.rectTransform.anchorMin = new Vector2(0.5f, 0);
        statusText.rectTransform.anchorMax = new Vector2(0.5f, 0);
        statusText.rectTransform.pivot = new Vector2(0.5f, 0);
        statusText.rectTransform.anchoredPosition = new Vector2(0, 40);
        statusText.rectTransform.sizeDelta = new Vector2(300, 30);
        statusText.alignment = TextAlignmentOptions.Center;

        // Cards container - bottom area
        var cardsContainer = new GameObject("Cards");
        cardsContainer.transform.SetParent(mainPanel.transform, false);
        var cardsRect = cardsContainer.AddComponent<RectTransform>();
        cardsRect.anchorMin = new Vector2(0, 0);
        cardsRect.anchorMax = new Vector2(1, 0);
        cardsRect.pivot = new Vector2(0.5f, 0);
        cardsRect.anchoredPosition = new Vector2(0, 100);
        cardsRect.sizeDelta = new Vector2(-120, 220);

        var cardsLayout = cardsContainer.AddComponent<HorizontalLayoutGroup>();
        cardsLayout.spacing = 20;
        cardsLayout.childAlignment = TextAnchor.MiddleCenter;
        cardsLayout.childControlWidth = true;
        cardsLayout.childControlHeight = true;
        cardsLayout.childForceExpandWidth = true;
        cardsLayout.childForceExpandHeight = true;
        cardsLayout.padding = new RectOffset(60, 60, 0, 0);

        // Create cards
        CreateMenuCard(cardsContainer.transform, "DEPLOY", "Join the battle", true, OnPlay);
        CreateMenuCard(cardsContainer.transform, "CUSTOMIZE", "Loadout & appearance", false, OnCustomize);
        CreateMenuCard(cardsContainer.transform, "SETTINGS", "Audio & controls", false, OnSettings);

        // === SETTINGS PANEL ===
        BuildSettingsPanel(canvasObj.transform);

        // === CUSTOMIZE PANEL ===
        BuildCustomizePanel(canvasObj.transform);

        // === LOADING PANEL ===
        BuildLoadingPanel(canvasObj.transform);

        ShowPanel(mainMenuGroup);
    }

    void CreateMenuCard(Transform parent, string title, string subtitle, bool isPrimary, UnityEngine.Events.UnityAction onClick)
    {
        var card = new GameObject("Card_" + title);
        card.transform.SetParent(parent, false);
        var cardRect = card.AddComponent<RectTransform>();

        var cardImg = card.AddComponent<Image>();
        cardImg.color = isPrimary ? new Color(accentOrange.r * 0.3f, accentOrange.g * 0.3f, accentOrange.b * 0.3f, 0.9f) : cardBg;

        // Accent line at top
        var accent = CreateImage(card.transform, "Accent", isPrimary ? accentOrange : new Color(0.3f, 0.3f, 0.35f));
        accent.rectTransform.anchorMin = new Vector2(0, 1);
        accent.rectTransform.anchorMax = new Vector2(1, 1);
        accent.rectTransform.pivot = new Vector2(0.5f, 1);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(0, 4);

        // Title
        var titleTmp = CreateTMP(card.transform, title, 24, FontStyles.Bold, textWhite);
        titleTmp.rectTransform.anchorMin = new Vector2(0, 0.5f);
        titleTmp.rectTransform.anchorMax = new Vector2(1, 0.5f);
        titleTmp.rectTransform.anchoredPosition = new Vector2(0, 15);
        titleTmp.rectTransform.sizeDelta = new Vector2(-40, 40);
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.characterSpacing = 2;

        // Subtitle
        var subTmp = CreateTMP(card.transform, subtitle, 13, FontStyles.Normal, textGray);
        subTmp.rectTransform.anchorMin = new Vector2(0, 0.5f);
        subTmp.rectTransform.anchorMax = new Vector2(1, 0.5f);
        subTmp.rectTransform.anchoredPosition = new Vector2(0, -20);
        subTmp.rectTransform.sizeDelta = new Vector2(-40, 25);
        subTmp.alignment = TextAlignmentOptions.Center;

        // Button
        var btn = card.AddComponent<Button>();
        btn.targetGraphic = cardImg;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        // Hover effects
        AddCardHoverEffect(card, cardImg, accent, isPrimary);
    }

    void AddCardHoverEffect(GameObject card, Image cardImg, Image accent, bool isPrimary)
    {
        var trigger = card.AddComponent<EventTrigger>();
        Color origColor = cardImg.color;
        Color origAccent = accent.color;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => {
            LeanTween.scale(card, Vector3.one * 1.03f, 0.15f).setEaseOutBack();
            cardImg.color = isPrimary ? new Color(accentOrange.r * 0.4f, accentOrange.g * 0.4f, accentOrange.b * 0.4f, 0.95f) : cardHover;
            accent.color = accentOrange;
        });
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => {
            LeanTween.scale(card, Vector3.one, 0.1f).setEaseOutQuad();
            cardImg.color = origColor;
            accent.color = origAccent;
        });
        trigger.triggers.Add(exit);

        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => LeanTween.scale(card, Vector3.one * 0.97f, 0.05f));
        trigger.triggers.Add(down);

        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => LeanTween.scale(card, Vector3.one * 1.03f, 0.1f).setEaseOutBack());
        trigger.triggers.Add(up);
    }

    void BuildSettingsPanel(Transform parent)
    {
        var panel = CreateOverlayPanel(parent, "Settings");
        settingsGroup = panel.GetComponent<CanvasGroup>();

        var title = CreateTMP(panel.transform, "SETTINGS", 42, FontStyles.Bold, textWhite);
        title.rectTransform.anchoredPosition = new Vector2(0, 180);
        title.rectTransform.sizeDelta = new Vector2(400, 60);

        var divider = CreateImage(panel.transform, "Divider", new Color(1f, 1f, 1f, 0.1f));
        divider.rectTransform.anchoredPosition = new Vector2(0, 140);
        divider.rectTransform.sizeDelta = new Vector2(400, 1);

        CreateSliderRow(panel.transform, "MASTER VOLUME", new Vector2(0, 80), PlayerPrefs.GetFloat("MasterVolume", 1f), v => {
            AudioListener.volume = v;
            PlayerPrefs.SetFloat("MasterVolume", v);
        });

        CreateSliderRow(panel.transform, "MUSIC", new Vector2(0, 20), PlayerPrefs.GetFloat("MusicVolume", 0.7f), v => {
            PlayerPrefs.SetFloat("MusicVolume", v);
        });

        CreateSliderRow(panel.transform, "SENSITIVITY", new Vector2(0, -40), PlayerPrefs.GetFloat("Sensitivity", 2f) / 5f, v => {
            PlayerPrefs.SetFloat("Sensitivity", v * 5f);
        });

        var backBtn = CreateButton(panel.transform, "BACK", new Vector2(0, -140), false);
        backBtn.onClick.AddListener(() => ShowPanel(mainMenuGroup));
    }

    void BuildCustomizePanel(Transform parent)
    {
        Debug.Log("[MainMenuUI] BuildCustomizePanel starting...");

        var panel = new GameObject("CustomizePanel");
        panel.transform.SetParent(parent, false);
        var panelRect = panel.AddComponent<RectTransform>();
        Stretch(panelRect);

        customizeGroup = panel.AddComponent<CanvasGroup>();
        customizeGroup.alpha = 0;
        customizeGroup.blocksRaycasts = false;

        // LEFT SIDE - Team selection (scaled up for readability)
        var leftPanel = new GameObject("LeftPanel");
        leftPanel.transform.SetParent(panel.transform, false);
        var leftRect = leftPanel.AddComponent<RectTransform>();
        leftRect.anchorMin = new Vector2(0, 0.5f);
        leftRect.anchorMax = new Vector2(0, 0.5f);
        leftRect.pivot = new Vector2(0, 0.5f);
        leftRect.anchoredPosition = new Vector2(80, 0);
        leftRect.sizeDelta = new Vector2(320, 500);
        var leftBg = leftPanel.AddComponent<Image>();
        leftBg.sprite = CreateRoundedRectSprite(320, 500, 20);
        leftBg.type = Image.Type.Sliced;
        leftBg.color = new Color(0.05f, 0.05f, 0.08f, 0.9f);

        // Title on left
        var title = CreateTMP(leftPanel.transform, "CUSTOMIZE", 32, FontStyles.Bold, textWhite);
        title.rectTransform.anchoredPosition = new Vector2(0, 210);
        title.rectTransform.sizeDelta = new Vector2(280, 50);

        // Mode tabs (CHARACTER / WEAPONS) - positioned right below title
        var modeTabContainer = new GameObject("ModeTabContainer");
        modeTabContainer.transform.SetParent(leftPanel.transform, false);
        var modeContainerRect = modeTabContainer.AddComponent<RectTransform>();
        modeContainerRect.anchoredPosition = new Vector2(0, 155);
        modeContainerRect.sizeDelta = new Vector2(280, 50);

        var characterModeTab = CreateTeamTab(modeTabContainer.transform, "CHARACTER", new Vector2(-72, 0), true);
        characterModeTab.GetComponent<RectTransform>().sizeDelta = new Vector2(135, 45);
        characterModeTab.GetComponentInChildren<TextMeshProUGUI>().fontSize = 16;

        var weaponsModeTab = CreateTeamTab(modeTabContainer.transform, "WEAPONS", new Vector2(72, 0), false);
        weaponsModeTab.GetComponent<RectTransform>().sizeDelta = new Vector2(135, 45);
        weaponsModeTab.GetComponentInChildren<TextMeshProUGUI>().fontSize = 16;

        Debug.Log("[MainMenuUI] Created mode tabs: CHARACTER and WEAPONS");

        // Character customization content container
        var characterContent = new GameObject("CharacterContent");
        characterContent.transform.SetParent(leftPanel.transform, false);
        var charContentRect = characterContent.AddComponent<RectTransform>();
        charContentRect.anchorMin = Vector2.zero;
        charContentRect.anchorMax = Vector2.one;
        charContentRect.sizeDelta = Vector2.zero;

        // Team label
        var teamLabel = CreateTMP(characterContent.transform, "TEAM", 18, FontStyles.Bold, textGray);
        teamLabel.rectTransform.anchoredPosition = new Vector2(0, 80);
        teamLabel.rectTransform.sizeDelta = new Vector2(280, 30);
        teamLabel.characterSpacing = 3;

        // Team tabs stacked vertically
        var phantomTab = CreateTeamTab(characterContent.transform, "PHANTOM", new Vector2(0, 30), true);
        phantomTab.GetComponent<RectTransform>().sizeDelta = new Vector2(260, 50);
        phantomTab.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18;

        var havocTab = CreateTeamTab(characterContent.transform, "HAVOC", new Vector2(0, -30), false);
        havocTab.GetComponent<RectTransform>().sizeDelta = new Vector2(260, 50);
        havocTab.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18;

        phantomTab.onClick.AddListener(() => {
            selectedCustomizeTeam = "Phantom";
            UpdateTeamTabs(phantomTab, havocTab);
            SwitchShowcaseCharacter();
        });

        havocTab.onClick.AddListener(() => {
            selectedCustomizeTeam = "Havoc";
            UpdateTeamTabs(havocTab, phantomTab);
            SwitchShowcaseCharacter();
        });

        // K-9 Companion selector
        var dogLabel = CreateTMP(characterContent.transform, "K-9 COMPANION", 18, FontStyles.Bold, textGray);
        dogLabel.rectTransform.anchoredPosition = new Vector2(0, -90);
        dogLabel.rectTransform.sizeDelta = new Vector2(280, 30);
        dogLabel.characterSpacing = 2;

        // Dog selector row
        var dogSelectorRow = new GameObject("DogSelector");
        dogSelectorRow.transform.SetParent(characterContent.transform, false);
        var dogRowRect = dogSelectorRow.AddComponent<RectTransform>();
        dogRowRect.anchoredPosition = new Vector2(0, -135);
        dogRowRect.sizeDelta = new Vector2(280, 45);

        var dogLeftBtn = CreateArrowButton(dogSelectorRow.transform, "<", new Vector2(-115, 0));
        dogLeftBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 45);
        dogLeftBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 24;

        var dogValueText = CreateTMP(dogSelectorRow.transform, "German Shepherd", 16, FontStyles.Normal, textWhite);
        dogValueText.rectTransform.anchoredPosition = Vector2.zero;
        dogValueText.rectTransform.sizeDelta = new Vector2(160, 45);

        var dogRightBtn = CreateArrowButton(dogSelectorRow.transform, ">", new Vector2(115, 0));
        dogRightBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 45);
        dogRightBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 24;

        // Update dog display
        System.Action updateDogDisplay = () => {
            if (characterCustomizer != null)
            {
                int idx = characterCustomizer.GetSelection(selectedCustomizeTeam, "Dog");
                dogValueText.text = characterCustomizer.GetOptionName(selectedCustomizeTeam, "Dog", idx);
            }
        };

        dogLeftBtn.onClick.AddListener(() => {
            if (characterCustomizer != null)
            {
                characterCustomizer.CycleSelection(selectedCustomizeTeam, "Dog", -1);
                updateDogDisplay();
                SpawnShowcaseDog(selectedCustomizeTeam);
            }
        });

        dogRightBtn.onClick.AddListener(() => {
            if (characterCustomizer != null)
            {
                characterCustomizer.CycleSelection(selectedCustomizeTeam, "Dog", 1);
                updateDogDisplay();
                SpawnShowcaseDog(selectedCustomizeTeam);
            }
        });

        // Initial update
        updateDogDisplay();

        // === WEAPON CUSTOMIZATION CONTENT ===
        var weaponContent = new GameObject("WeaponContent");
        weaponContent.transform.SetParent(leftPanel.transform, false);
        var weaponContentRect = weaponContent.AddComponent<RectTransform>();
        weaponContentRect.anchorMin = Vector2.zero;
        weaponContentRect.anchorMax = Vector2.one;
        weaponContentRect.sizeDelta = Vector2.zero;
        weaponContent.SetActive(false); // Hidden by default
        weaponEditorPanel = weaponContent;

        // Loadout slots
        var loadoutLabel = CreateTMP(weaponContent.transform, "LOADOUT", 18, FontStyles.Bold, textGray);
        loadoutLabel.rectTransform.anchoredPosition = new Vector2(0, 80);
        loadoutLabel.rectTransform.sizeDelta = new Vector2(280, 30);
        loadoutLabel.characterSpacing = 3;

        var loadoutRow = new GameObject("LoadoutRow");
        loadoutRow.transform.SetParent(weaponContent.transform, false);
        var loadoutRowRect = loadoutRow.AddComponent<RectTransform>();
        loadoutRowRect.anchoredPosition = new Vector2(0, 35);
        loadoutRowRect.sizeDelta = new Vector2(280, 45);

        Button[] loadoutBtns = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var slotBtn = new GameObject($"Loadout{i + 1}");
            slotBtn.transform.SetParent(loadoutRow.transform, false);
            var slotRect = slotBtn.AddComponent<RectTransform>();
            slotRect.anchoredPosition = new Vector2(-100 + i * 50, 0);
            slotRect.sizeDelta = new Vector2(44, 44);

            var slotBg = slotBtn.AddComponent<Image>();
            slotBg.color = i == 0 ? accentOrange : new Color(0.2f, 0.2f, 0.25f);

            var slotBtnComp = slotBtn.AddComponent<Button>();
            loadoutBtns[i] = slotBtnComp;

            var slotLabel = CreateTMP(slotBtn.transform, (i + 1).ToString(), 20, FontStyles.Bold, i == 0 ? bgDark : textWhite);
            slotLabel.rectTransform.anchorMin = Vector2.zero;
            slotLabel.rectTransform.anchorMax = Vector2.one;
            slotLabel.rectTransform.sizeDelta = Vector2.zero;

            slotBtnComp.onClick.AddListener(() => {
                currentEditingLoadout = idx;
                if (weaponCustomizer == null)
                {
                    weaponCustomizer = WeaponCustomizer.Instance;
                }
                if (weaponCustomizer != null)
                {
                    weaponCustomizer.SetCurrentLoadout(idx);
                    weaponCustomizer.SetActiveLoadout(idx); // Also set as active so it's saved for in-game use
                    Debug.Log($"[MainMenuUI] Switched to loadout {idx}, preset: {weaponCustomizer.GetCurrentPresetIndex()}");
                }
                for (int j = 0; j < 5; j++)
                {
                    var bg = loadoutBtns[j].GetComponent<Image>();
                    var lbl = loadoutBtns[j].GetComponentInChildren<TextMeshProUGUI>();
                    bg.color = j == idx ? accentOrange : new Color(0.2f, 0.2f, 0.25f);
                    lbl.color = j == idx ? bgDark : textWhite;
                }
                UpdateWeaponPresetDisplay();
                RefreshWeaponPreview();
                UpdateWeaponStatsDisplay();
            });
        }

        // Platform selector
        var platformLabel = CreateTMP(weaponContent.transform, "PLATFORM", 18, FontStyles.Bold, textGray);
        platformLabel.rectTransform.anchoredPosition = new Vector2(0, -20);
        platformLabel.rectTransform.sizeDelta = new Vector2(280, 30);
        platformLabel.characterSpacing = 3;

        var platformRow = new GameObject("PlatformRow");
        platformRow.transform.SetParent(weaponContent.transform, false);
        var platformRowRect = platformRow.AddComponent<RectTransform>();
        platformRowRect.anchoredPosition = new Vector2(0, -60);
        platformRowRect.sizeDelta = new Vector2(280, 45);

        var platformABtn = CreateTeamTab(platformRow.transform, "TYPE A", new Vector2(-72, 0), true);
        platformABtn.GetComponent<RectTransform>().sizeDelta = new Vector2(135, 45);
        platformABtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 16;

        var platformBBtn = CreateTeamTab(platformRow.transform, "TYPE B", new Vector2(72, 0), false);
        platformBBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(135, 45);
        platformBBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 16;

        platformABtn.onClick.AddListener(() => {
            if (weaponCustomizer != null && weaponCustomizer.GetCurrentPlatform() != "Weapon_A")
            {
                weaponCustomizer.SetPlatform("Weapon_A");
                UpdateTeamTabs(platformABtn, platformBBtn);
                RefreshWeaponPreview();
                UpdateWeaponStatsDisplay();
            }
        });

        platformBBtn.onClick.AddListener(() => {
            if (weaponCustomizer != null && weaponCustomizer.GetCurrentPlatform() != "Weapon_B")
            {
                weaponCustomizer.SetPlatform("Weapon_B");
                UpdateTeamTabs(platformBBtn, platformABtn);
                RefreshWeaponPreview();
                UpdateWeaponStatsDisplay();
            }
        });

        // Weapon preset selector (simplified - cycle through complete weapons)
        var weaponLabel = CreateTMP(weaponContent.transform, "WEAPON", 18, FontStyles.Bold, textGray);
        weaponLabel.rectTransform.anchoredPosition = new Vector2(0, -115);
        weaponLabel.rectTransform.sizeDelta = new Vector2(280, 30);
        weaponLabel.characterSpacing = 3;

        // Weapon selector row
        var weaponSelectorRow = new GameObject("WeaponSelector");
        weaponSelectorRow.transform.SetParent(weaponContent.transform, false);
        var weaponSelectorRect = weaponSelectorRow.AddComponent<RectTransform>();
        weaponSelectorRect.anchoredPosition = new Vector2(0, -160);
        weaponSelectorRect.sizeDelta = new Vector2(280, 50);

        var weaponLeftBtn = CreateArrowButton(weaponSelectorRow.transform, "<", new Vector2(-125, 0));
        weaponLeftBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 50);
        weaponLeftBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

        currentPartLabel = CreateTMP(weaponSelectorRow.transform, "Rifle", 16, FontStyles.Bold, accentOrange);
        currentPartLabel.rectTransform.anchoredPosition = Vector2.zero;
        currentPartLabel.rectTransform.sizeDelta = new Vector2(180, 50);

        var weaponRightBtn = CreateArrowButton(weaponSelectorRow.transform, ">", new Vector2(125, 0));
        weaponRightBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 50);
        weaponRightBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

        // Weapon count display
        currentPartValue = CreateTMP(weaponContent.transform, "1 / 20", 14, FontStyles.Normal, textGray);
        currentPartValue.rectTransform.anchoredPosition = new Vector2(0, -195);
        currentPartValue.rectTransform.sizeDelta = new Vector2(280, 25);

        // Weapon cycling
        weaponLeftBtn.onClick.AddListener(() => {
            if (weaponCustomizer != null)
            {
                weaponCustomizer.CyclePreset(-1);
                UpdateWeaponPresetDisplay();
                RefreshWeaponPreview();
                UpdateWeaponStatsDisplay();
            }
        });

        weaponRightBtn.onClick.AddListener(() => {
            if (weaponCustomizer != null)
            {
                weaponCustomizer.CyclePreset(1);
                UpdateWeaponPresetDisplay();
                RefreshWeaponPreview();
                UpdateWeaponStatsDisplay();
            }
        });

        // Mode switching logic
        characterModeTab.onClick.AddListener(() => {
            isCustomizingWeapon = false;
            characterContent.SetActive(true);
            weaponContent.SetActive(false);
            UpdateTeamTabs(characterModeTab, weaponsModeTab);

            // Show character preview
            if (playerShowcaseCharacter != null) playerShowcaseCharacter.SetActive(true);
            if (showcaseDog != null) showcaseDog.SetActive(true);
            if (weaponPreview != null) weaponPreview.SetActive(false);

            // Hide stats panel
            var statsPanel = characterContent.transform.parent.parent.Find("WeaponStatsPanel");
            if (statsPanel != null) statsPanel.gameObject.SetActive(false);
        });

        weaponsModeTab.onClick.AddListener(() => {
            isCustomizingWeapon = true;
            characterContent.SetActive(false);
            weaponContent.SetActive(true);
            UpdateTeamTabs(weaponsModeTab, characterModeTab);

            // Show weapon preview instead of character
            if (playerShowcaseCharacter != null) playerShowcaseCharacter.SetActive(false);
            if (showcaseDog != null) showcaseDog.SetActive(false);
            SpawnWeaponPreview();
            UpdateWeaponPartDisplay();
            UpdateWeaponStatsDisplay();

            // Show stats panel
            var statsPanel = weaponContent.transform.parent.parent.Find("WeaponStatsPanel");
            if (statsPanel != null) statsPanel.gameObject.SetActive(true);
        });

        // Done button at bottom of left panel
        var doneBtn = CreateButton(leftPanel.transform, "DONE", new Vector2(0, -220), true);
        doneBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 55);
        doneBtn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 20;
        doneBtn.onClick.AddListener(ExitCustomizeMode);

        // === STATS PANEL (Right side for weapon mode) ===
        BuildWeaponStatsPanel(panel.transform);

        // Create body part popup (hidden by default) - will appear near character
        BuildBodyPartPopup(parent);

        Debug.Log($"[MainMenuUI] BuildCustomizePanel complete");
    }

    void BuildWeaponStatsPanel(Transform parent)
    {
        var statsPanel = new GameObject("WeaponStatsPanel");
        statsPanel.transform.SetParent(parent, false);
        var statsRect = statsPanel.AddComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(1, 0.5f);
        statsRect.anchorMax = new Vector2(1, 0.5f);
        statsRect.pivot = new Vector2(1, 0.5f);
        statsRect.anchoredPosition = new Vector2(-80, 0);
        statsRect.sizeDelta = new Vector2(300, 450);

        var statsBg = statsPanel.AddComponent<Image>();
        statsBg.sprite = CreateRoundedRectSprite(300, 450, 20);
        statsBg.type = Image.Type.Sliced;
        statsBg.color = new Color(0.05f, 0.05f, 0.08f, 0.9f);
        statsPanel.SetActive(false); // Show only in weapon mode

        var statsTitle = CreateTMP(statsPanel.transform, "STATS", 24, FontStyles.Bold, textWhite);
        statsTitle.rectTransform.anchoredPosition = new Vector2(0, 190);
        statsTitle.rectTransform.sizeDelta = new Vector2(260, 40);

        string[] statNames = { "DAMAGE", "FIRE RATE", "RANGE", "ACCURACY", "RECOIL", "MOBILITY" };
        statBarLabels = new TextMeshProUGUI[statNames.Length];
        statBarFills = new Image[statNames.Length];

        for (int i = 0; i < statNames.Length; i++)
        {
            float yPos = 130 - i * 55;

            var label = CreateTMP(statsPanel.transform, statNames[i], 14, FontStyles.Bold, textGray);
            label.rectTransform.anchoredPosition = new Vector2(0, yPos + 15);
            label.rectTransform.sizeDelta = new Vector2(260, 22);
            label.alignment = TextAlignmentOptions.Left;
            statBarLabels[i] = label;

            // Bar background
            var barBg = CreateImage(statsPanel.transform, $"StatBg_{i}", new Color(0.15f, 0.15f, 0.18f));
            barBg.rectTransform.anchoredPosition = new Vector2(0, yPos - 8);
            barBg.rectTransform.sizeDelta = new Vector2(260, 14);

            // Bar fill
            var barFill = CreateImage(statsPanel.transform, $"StatFill_{i}", accentOrange);
            barFill.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            barFill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            barFill.rectTransform.pivot = new Vector2(0, 0.5f);
            barFill.rectTransform.anchoredPosition = new Vector2(-130, yPos - 8);
            barFill.rectTransform.sizeDelta = new Vector2(130, 14); // 50% fill default
            statBarFills[i] = barFill;
        }
    }

    void SpawnWeaponPreview()
    {
        if (weaponPreview != null)
        {
            Destroy(weaponPreview);
            weaponPreview = null;
        }

        // Refresh reference if null
        if (weaponCustomizer == null)
        {
            weaponCustomizer = WeaponCustomizer.Instance;
        }
        if (weaponCustomizer == null) return;

        // Create weapon preview in the showcase area
        if (showcaseModel != null)
        {
            weaponPreview = weaponCustomizer.CreatePreviewWeapon(showcaseModel.transform);
            if (weaponPreview != null)
            {
                weaponPreview.transform.localPosition = new Vector3(0, 0.8f, 0.5f);
                weaponPreview.transform.localRotation = Quaternion.Euler(0, 180, 0);
                weaponPreview.transform.localScale = Vector3.one * 2f; // Scale for visibility
            }
        }

        // Show stats panel
        if (weaponEditorPanel != null && weaponEditorPanel.transform.parent != null)
        {
            var statsPanel = weaponEditorPanel.transform.parent.parent.Find("WeaponStatsPanel");
            if (statsPanel != null) statsPanel.gameObject.SetActive(true);
        }

        UpdateWeaponStatsDisplay();
    }

    void RefreshWeaponPreview()
    {
        if (!isCustomizingWeapon)
        {
            Debug.Log("[MainMenuUI] RefreshWeaponPreview: Not in weapon mode");
            return;
        }
        // Refresh reference if null
        if (weaponCustomizer == null)
        {
            weaponCustomizer = WeaponCustomizer.Instance;
        }
        if (weaponCustomizer == null)
        {
            Debug.LogWarning("[MainMenuUI] RefreshWeaponPreview: weaponCustomizer is null!");
            return;
        }

        if (weaponPreview != null)
        {
            Destroy(weaponPreview);
        }

        if (showcaseModel != null)
        {
            Debug.Log("[MainMenuUI] Creating weapon preview...");
            weaponPreview = weaponCustomizer.CreatePreviewWeapon(showcaseModel.transform);
            if (weaponPreview != null)
            {
                weaponPreview.transform.localPosition = new Vector3(0, 0.8f, 0.5f);
                weaponPreview.transform.localRotation = Quaternion.Euler(0, 180, 0);
                weaponPreview.transform.localScale = Vector3.one * 2f;
                Debug.Log("[MainMenuUI] Weapon preview created successfully");
            }
            else
            {
                Debug.LogWarning("[MainMenuUI] Weapon preview is null - check WeaponCustomizer has prefabs assigned");
            }
        }
    }

    void UpdateWeaponPresetDisplay()
    {
        // Refresh reference if null (original may have been destroyed when creating persistent copy)
        if (weaponCustomizer == null)
        {
            weaponCustomizer = WeaponCustomizer.Instance;
        }
        if (weaponCustomizer == null)
        {
            Debug.LogWarning("[MainMenuUI] UpdateWeaponPresetDisplay: weaponCustomizer is null!");
            if (currentPartValue != null) currentPartValue.text = "No Customizer";
            return;
        }
        if (currentPartLabel == null || currentPartValue == null) return;

        string presetName = weaponCustomizer.GetCurrentPresetName();
        int idx = weaponCustomizer.GetCurrentPresetIndex();
        int count = weaponCustomizer.GetPresetCount();

        currentPartLabel.text = presetName;
        currentPartValue.text = $"{idx + 1} / {count}";
        Debug.Log($"[MainMenuUI] Preset display: {presetName} ({idx + 1}/{count})");
    }

    void UpdateWeaponPartDisplay()
    {
        // Redirect to preset display
        UpdateWeaponPresetDisplay();
    }

    void UpdateWeaponStatsDisplay()
    {
        // Refresh reference if null
        if (weaponCustomizer == null)
        {
            weaponCustomizer = WeaponCustomizer.Instance;
        }
        if (weaponCustomizer == null || statBarFills == null) return;

        var stats = weaponCustomizer.GetCurrentStats();
        if (stats == null) return;

        // Normalize stats to 0-1 range for bar display
        float[] normalized = {
            Mathf.Clamp01(stats.damage / 50f),           // Max damage ~50
            Mathf.Clamp01((1f / stats.fireRate) / 15f),  // Convert to RPM-ish, max ~15
            Mathf.Clamp01(stats.range / 150f),           // Max range ~150
            Mathf.Clamp01(stats.accuracy),               // Already 0-1
            Mathf.Clamp01(1f - (stats.recoilVertical / 5f)), // Invert - less recoil is better
            Mathf.Clamp01(stats.moveSpeedMultiplier)     // Already 0-1
        };

        for (int i = 0; i < statBarFills.Length && i < normalized.Length; i++)
        {
            if (statBarFills[i] != null)
            {
                statBarFills[i].rectTransform.sizeDelta = new Vector2(260 * normalized[i], 14);
            }
        }
    }

    void BuildBodyPartPopup(Transform parent)
    {
        bodyPartPopup = new GameObject("BodyPartPopup");
        bodyPartPopup.transform.SetParent(parent, false);
        var popupRect = bodyPartPopup.AddComponent<RectTransform>();
        popupRect.sizeDelta = new Vector2(280, 100);

        var popupBg = bodyPartPopup.AddComponent<Image>();
        popupBg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        var popupOutline = bodyPartPopup.AddComponent<Outline>();
        popupOutline.effectColor = accentOrange;
        popupOutline.effectDistance = new Vector2(2, -2);

        // Category label
        popupLabel = CreateTMP(bodyPartPopup.transform, "HEADGEAR", 12, FontStyles.Bold, accentOrange);
        popupLabel.rectTransform.anchoredPosition = new Vector2(0, 30);
        popupLabel.rectTransform.sizeDelta = new Vector2(260, 25);
        popupLabel.characterSpacing = 2;

        // Left arrow
        var leftBtn = CreateArrowButton(bodyPartPopup.transform, "<", new Vector2(-100, -10));
        leftBtn.onClick.AddListener(() => CycleCurrentBodyPart(-1));

        // Value display
        popupValue = CreateTMP(bodyPartPopup.transform, "None", 18, FontStyles.Normal, textWhite);
        popupValue.rectTransform.anchoredPosition = new Vector2(0, -10);
        popupValue.rectTransform.sizeDelta = new Vector2(140, 35);

        // Right arrow
        var rightBtn = CreateArrowButton(bodyPartPopup.transform, ">", new Vector2(100, -10));
        rightBtn.onClick.AddListener(() => CycleCurrentBodyPart(1));

        bodyPartPopup.SetActive(false);
    }

    void CycleCurrentBodyPart(int direction)
    {
        if (characterCustomizer == null || string.IsNullOrEmpty(currentBodyPart)) return;

        characterCustomizer.CycleSelection(selectedCustomizeTeam, currentBodyPart, direction);
        UpdatePopupDisplay();

        // Character changes require swapping the whole model
        if (currentBodyPart == "Character")
        {
            SwitchShowcaseCharacter();
        }
        else
        {
            ApplyShowcaseAttachments(selectedCustomizeTeam);
        }
    }

    void UpdatePopupDisplay()
    {
        if (popupValue == null || characterCustomizer == null || string.IsNullOrEmpty(currentBodyPart)) return;

        int index = characterCustomizer.GetSelection(selectedCustomizeTeam, currentBodyPart);
        string name = characterCustomizer.GetOptionName(selectedCustomizeTeam, currentBodyPart, index);
        popupValue.text = name;
    }

    void ShowBodyPartPopup(string category, Vector3 worldPos)
    {
        currentBodyPart = category;
        popupLabel.text = category.ToUpper();
        UpdatePopupDisplay();

        // Position popup near the clicked area
        Vector3 screenPos = menuCamera.WorldToScreenPoint(worldPos);
        var popupRect = bodyPartPopup.GetComponent<RectTransform>();

        // Convert to canvas space and offset slightly
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform, screenPos, null, out Vector2 canvasPos);
        popupRect.anchoredPosition = canvasPos + new Vector2(150, 0);

        // Keep on screen
        Vector2 pos = popupRect.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, -800, 800);
        pos.y = Mathf.Clamp(pos.y, -400, 400);
        popupRect.anchoredPosition = pos;

        bodyPartPopup.SetActive(true);
    }

    void HideBodyPartPopup()
    {
        if (bodyPartPopup != null)
            bodyPartPopup.SetActive(false);
        currentBodyPart = null;
    }

    void SwitchShowcaseCharacter()
    {
        if (characterCustomizer == null || playerShowcaseCharacter == null) return;

        // Get the prefab for the selected team's character
        int charIndex = characterCustomizer.GetSelection(selectedCustomizeTeam, "Character");
        var prefab = characterCustomizer.GetOptionPrefab(selectedCustomizeTeam, "Character", charIndex);

        if (prefab == null) return;

        // Remember position and rotation
        Vector3 pos = playerShowcaseCharacter.transform.position;
        Quaternion rot = playerShowcaseCharacter.transform.rotation;
        Transform parent = playerShowcaseCharacter.transform.parent;

        // Destroy old and create new
        Destroy(playerShowcaseCharacter);

        // Reset cached transforms for new character
        hasOriginalTransforms = false;
        foundOrigHeadgear = false;
        foundOrigFacewear = false;
        foundOrigBackpack = false;

        playerShowcaseCharacter = Instantiate(prefab, pos, rot);
        playerShowcaseCharacter.transform.SetParent(parent);
        playerShowcaseCharacter.name = "PlayerCharacter";
        SetupShowcaseSoldier(playerShowcaseCharacter);
        SetupCharacterClickZones();
        ApplyShowcaseAttachments(selectedCustomizeTeam);

        // Also update the dog for the new team
        SpawnShowcaseDog(selectedCustomizeTeam);
    }

    void SetupCharacterClickZones()
    {
        if (playerShowcaseCharacter == null)
        {
            Debug.LogWarning("[MainMenuUI] SetupCharacterClickZones - no character!");
            return;
        }

        int zonesCreated = 0;

        // Add colliders for click detection on different body parts
        var transforms = playerShowcaseCharacter.GetComponentsInChildren<Transform>();

        foreach (var t in transforms)
        {
            // Skip if already has a ClickZone
            if (t.gameObject.GetComponent<ClickZone>() != null) continue;

            string name = t.name.ToLower();

            // Head area - for headgear
            if (name.Contains("head") && !name.Contains("headset"))
            {
                var col = t.gameObject.GetComponent<SphereCollider>();
                if (col == null) col = t.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.15f;
                col.isTrigger = false; // Non-trigger for raycast
                col.enabled = true;
                var zone = t.gameObject.AddComponent<ClickZone>();
                zone.category = "Headgear";
                zonesCreated++;
                Debug.Log($"[MainMenuUI] Added Headgear click zone to: {t.name}");
            }
            // Face/neck area - for facewear
            else if (name.Contains("neck") || name.Contains("jaw"))
            {
                var col = t.gameObject.GetComponent<SphereCollider>();
                if (col == null) col = t.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.1f;
                col.isTrigger = false;
                col.enabled = true;
                var zone = t.gameObject.AddComponent<ClickZone>();
                zone.category = "Facewear";
                zonesCreated++;
                Debug.Log($"[MainMenuUI] Added Facewear click zone to: {t.name}");
            }
            // Spine/chest - for backpack and character
            else if (name.Contains("spine") && name.Contains("01"))
            {
                var col = t.gameObject.GetComponent<BoxCollider>();
                if (col == null) col = t.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(0.4f, 0.4f, 0.3f);
                col.isTrigger = false;
                col.enabled = true;
                var zone = t.gameObject.AddComponent<ClickZone>();
                zone.category = "Character";
                zonesCreated++;
                Debug.Log($"[MainMenuUI] Added Character click zone to: {t.name}");
            }
            else if (name.Contains("spine") && name.Contains("02"))
            {
                var col = t.gameObject.GetComponent<BoxCollider>();
                if (col == null) col = t.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(0.3f, 0.3f, 0.3f);
                col.isTrigger = false;
                col.enabled = true;
                var zone = t.gameObject.AddComponent<ClickZone>();
                zone.category = "Backpack";
                zonesCreated++;
                Debug.Log($"[MainMenuUI] Added Backpack click zone to: {t.name}");
            }
        }

        Debug.Log($"[MainMenuUI] SetupCharacterClickZones complete - {zonesCreated} zones created");
    }

    void ExitCustomizeMode()
    {
        if (characterCustomizer != null)
        {
            characterCustomizer.SaveAllSelections();
        }
        if (weaponCustomizer != null)
        {
            weaponCustomizer.SaveAllLoadouts();
        }
        HideBodyPartPopup();
        isCustomizing = false;
        isCustomizingWeapon = false;

        // Clean up weapon preview and restore character
        if (weaponPreview != null)
        {
            Destroy(weaponPreview);
            weaponPreview = null;
        }
        if (playerShowcaseCharacter != null) playerShowcaseCharacter.SetActive(true);
        if (showcaseDog != null) showcaseDog.SetActive(true);

        // Hide weapon stats panel
        var statsPanel = customizeGroup?.transform.Find("WeaponStatsPanel");
        if (statsPanel != null) statsPanel.gameObject.SetActive(false);

        // Animate camera back
        StartCoroutine(AnimateCameraTo(menuCameraPos, menuCameraRot, menuCameraFOV, 0.5f));
        ShowPanel(mainMenuGroup);
    }

    IEnumerator AnimateCameraTo(Vector3 targetPos, Quaternion targetRot, float targetFOV, float duration)
    {
        if (menuCamera == null)
        {
            Debug.LogWarning("[MainMenuUI] AnimateCameraTo - menuCamera is null!");
            yield break;
        }

        Debug.Log($"[MainMenuUI] AnimateCameraTo started - target: {targetPos}");

        Vector3 startPos = menuCamera.transform.position;
        Quaternion startRot = menuCamera.transform.rotation;
        float startFOV = menuCamera.fieldOfView;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);

            menuCamera.transform.position = Vector3.Lerp(startPos, targetPos, t);
            menuCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            menuCamera.fieldOfView = Mathf.Lerp(startFOV, targetFOV, t);

            yield return null;
        }

        menuCamera.transform.position = targetPos;
        menuCamera.transform.rotation = targetRot;
        menuCamera.fieldOfView = targetFOV;
        Debug.Log($"[MainMenuUI] AnimateCameraTo finished - camera now at: {menuCamera.transform.position}");
    }

    Button CreateTeamTab(Transform parent, string text, Vector2 position, bool isActive)
    {
        var tabObj = new GameObject(text + "Tab");
        tabObj.transform.SetParent(parent, false);

        var rect = tabObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(150, 40);

        var bg = tabObj.AddComponent<Image>();
        bg.color = isActive ? accentOrange : new Color(0.15f, 0.15f, 0.18f);

        var btn = tabObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;

        var label = CreateTMP(tabObj.transform, text, 14, FontStyles.Bold, isActive ? bgDark : textWhite);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;

        return btn;
    }

    void UpdateTeamTabs(Button activeTab, Button inactiveTab)
    {
        var activeBg = activeTab.GetComponent<Image>();
        var inactiveBg = inactiveTab.GetComponent<Image>();

        activeBg.color = accentOrange;
        inactiveBg.color = new Color(0.15f, 0.15f, 0.18f);

        var activeLabel = activeTab.GetComponentInChildren<TextMeshProUGUI>();
        var inactiveLabel = inactiveTab.GetComponentInChildren<TextMeshProUGUI>();

        activeLabel.color = bgDark;
        inactiveLabel.color = textWhite;
    }

    Button CreateArrowButton(Transform parent, string arrow, Vector2 position)
    {
        var btnObj = new GameObject(arrow + "Btn");
        btnObj.transform.SetParent(parent, false);

        var rect = btnObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(40, 40);

        var bg = btnObj.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f);

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;

        var colors = btn.colors;
        colors.highlightedColor = accentOrange;
        colors.pressedColor = accentAmber;
        btn.colors = colors;

        var label = CreateTMP(btnObj.transform, arrow, 24, FontStyles.Bold, textWhite);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;

        return btn;
    }

    void BuildLoadingPanel(Transform parent)
    {
        var panel = CreateOverlayPanel(parent, "Loading");
        loadingGroup = panel.GetComponent<CanvasGroup>();

        var title = CreateTMP(panel.transform, "DEPLOYING", 36, FontStyles.Bold, textWhite);
        title.rectTransform.anchoredPosition = new Vector2(0, 40);
        title.rectTransform.sizeDelta = new Vector2(400, 60);

        // Loading bar background
        var barBg = CreateImage(panel.transform, "LoadBarBg", new Color(0.15f, 0.15f, 0.18f));
        barBg.rectTransform.anchoredPosition = new Vector2(0, -20);
        barBg.rectTransform.sizeDelta = new Vector2(350, 8);

        // Loading bar fill
        loadingBar = CreateImage(panel.transform, "LoadBarFill", accentOrange);
        loadingBar.rectTransform.anchoredPosition = new Vector2(-175, -20);
        loadingBar.rectTransform.pivot = new Vector2(0, 0.5f);
        loadingBar.rectTransform.sizeDelta = new Vector2(0, 8);
    }

    GameObject CreateOverlayPanel(Transform parent, string name)
    {
        var panel = new GameObject(name + "Panel");
        panel.transform.SetParent(parent, false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(500, 450);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(accentOrange.r, accentOrange.g, accentOrange.b, 0.3f);
        outline.effectDistance = new Vector2(1, -1);

        var cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0;
        cg.blocksRaycasts = false;

        return panel;
    }

    TMP_InputField CreateInputField(Transform parent, Vector2 pos, Vector2 size, bool leftAlign)
    {
        var obj = new GameObject("Input");
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();

        if (leftAlign)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
        }
        else
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        }

        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        var img = obj.AddComponent<Image>();
        img.color = cardBg;

        var outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(accentOrange.r, accentOrange.g, accentOrange.b, 0.2f);
        outline.effectDistance = new Vector2(1, -1);

        var input = obj.AddComponent<TMP_InputField>();

        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(obj.transform, false);
        var taRect = textArea.AddComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero;
        taRect.anchorMax = Vector2.one;
        taRect.sizeDelta = new Vector2(-20, -10);
        textArea.AddComponent<RectMask2D>();

        var textComp = CreateTMP(textArea.transform, "", 18, FontStyles.Bold, textWhite);
        textComp.rectTransform.anchorMin = Vector2.zero;
        textComp.rectTransform.anchorMax = Vector2.one;
        textComp.rectTransform.sizeDelta = Vector2.zero;
        textComp.alignment = TextAlignmentOptions.Left;
        textComp.characterSpacing = 1;

        var placeholder = CreateTMP(textArea.transform, "ENTER NAME...", 16, FontStyles.Normal, textGray);
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.sizeDelta = Vector2.zero;
        placeholder.alignment = TextAlignmentOptions.Left;

        input.textViewport = taRect;
        input.textComponent = textComp;
        input.placeholder = placeholder;
        input.characterLimit = 16;

        return input;
    }

    Button CreateButton(Transform parent, string text, Vector2 pos, bool isPrimary)
    {
        var obj = new GameObject("Btn_" + text);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(200, 50);

        var img = obj.AddComponent<Image>();
        img.color = isPrimary ? accentOrange : cardBg;

        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        btn.colors = colors;

        var label = CreateTMP(obj.transform, text, 16, FontStyles.Bold, isPrimary ? Color.black : textWhite);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.sizeDelta = Vector2.zero;
        label.characterSpacing = 2;

        return btn;
    }

    void CreateSliderRow(Transform parent, string label, Vector2 pos, float defaultVal, System.Action<float> onChange)
    {
        var row = new GameObject("Row_" + label);
        row.transform.SetParent(parent, false);
        var rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = pos;
        rowRect.sizeDelta = new Vector2(420, 45);

        var labelTmp = CreateTMP(row.transform, label, 13, FontStyles.Bold, textGray);
        labelTmp.rectTransform.anchoredPosition = new Vector2(-130, 0);
        labelTmp.rectTransform.sizeDelta = new Vector2(150, 30);
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.characterSpacing = 2;

        var sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);
        var sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchoredPosition = new Vector2(60, 0);
        sliderRect.sizeDelta = new Vector2(220, 20);

        var slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = defaultVal;

        // Track BG
        var bgObj = new GameObject("BG");
        bgObj.transform.SetParent(sliderObj.transform, false);
        var bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.5f);
        bgRect.anchorMax = new Vector2(1, 0.5f);
        bgRect.sizeDelta = new Vector2(0, 6);
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.22f);

        // Fill
        var fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(sliderObj.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(-20, 6);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.sizeDelta = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = accentOrange;

        // Handle
        var handleArea = new GameObject("HandleArea");
        handleArea.transform.SetParent(sliderObj.transform, false);
        var handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = new Vector2(-20, 0);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18, 18);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = textWhite;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImg;
        slider.onValueChanged.AddListener(v => onChange?.Invoke(v));
    }

    Image CreateImage(Transform parent, string name, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        var img = obj.AddComponent<Image>();
        img.color = color;
        return img;
    }

    void Stretch(Image img)
    {
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.sizeDelta = Vector2.zero;
    }

    void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
    }

    TextMeshProUGUI CreateTMP(Transform parent, string text, int size, FontStyles style, Color color)
    {
        var obj = new GameObject("Text");
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    void ShowPanel(CanvasGroup panel)
    {
        LeanTween.alphaCanvas(mainMenuGroup, panel == mainMenuGroup ? 1 : 0, 0.2f).setEaseOutQuad();
        LeanTween.alphaCanvas(settingsGroup, panel == settingsGroup ? 1 : 0, 0.2f).setEaseOutQuad();
        LeanTween.alphaCanvas(customizeGroup, panel == customizeGroup ? 1 : 0, 0.2f).setEaseOutQuad();
        LeanTween.alphaCanvas(loadingGroup, panel == loadingGroup ? 1 : 0, 0.2f).setEaseOutQuad();

        mainMenuGroup.blocksRaycasts = panel == mainMenuGroup;
        settingsGroup.blocksRaycasts = panel == settingsGroup;
        customizeGroup.blocksRaycasts = panel == customizeGroup;
        loadingGroup.blocksRaycasts = panel == loadingGroup;
    }

    IEnumerator AnimateShowcase()
    {
        while (true)
        {
            if (showcaseModel != null)
            {
                // Slow rotation
                showcaseModel.transform.Rotate(Vector3.up, 5f * Time.deltaTime);

                // Subtle rim light pulse
                if (rimLight != null)
                {
                    rimLight.intensity = 2f + Mathf.Sin(Time.time * 2f) * 0.3f;
                }
            }
            yield return null;
        }
    }

    void Update()
    {
        // Loading bar animation
        if (loadingGroup != null && loadingGroup.alpha > 0.5f && loadingBar != null)
        {
            loadProgress += Time.deltaTime * 0.4f;
            loadingBar.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(0, 350, Mathf.Clamp01(loadProgress)), 8);
        }

        // Handle character clicks when customizing
        if (isCustomizing && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Check what UI element we're over (if any)
            var pointerData = new PointerEventData(EventSystem.current);
            pointerData.position = Mouse.current.position.ReadValue();
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            // Check if we clicked on actual UI elements (left panel, popup, buttons)
            bool clickedOnUI = false;
            foreach (var result in results)
            {
                // Only block if clicking on the left panel or popup
                if (result.gameObject.name == "LeftPanel" ||
                    result.gameObject.name == "BodyPartPopup" ||
                    result.gameObject.transform.IsChildOf(bodyPartPopup?.transform) ||
                    result.gameObject.GetComponent<Button>() != null)
                {
                    clickedOnUI = true;
                    break;
                }
            }

            if (!clickedOnUI)
            {
                // Clicked in 3D space - do raycast for character
                HandleCharacterClick();
            }
        }

        // Slowly rotate character when customizing
        if (isCustomizing && playerShowcaseCharacter != null)
        {
            playerShowcaseCharacter.transform.Rotate(Vector3.up, 15f * Time.deltaTime, Space.World);
        }

        // Escape key handling
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (isCustomizing)
            {
                ExitCustomizeMode();
            }
            else if (settingsGroup != null && settingsGroup.alpha > 0.5f)
            {
                ShowPanel(mainMenuGroup);
            }
        }
    }

    void HandleCharacterClick()
    {
        if (menuCamera == null)
        {
            Debug.LogWarning("[MainMenuUI] HandleCharacterClick - no camera!");
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = menuCamera.ScreenPointToRay(mousePos);
        RaycastHit hit;

        Debug.Log($"[MainMenuUI] Raycast from mouse pos: {mousePos}");

        if (Physics.Raycast(ray, out hit, 100f))
        {
            Debug.Log($"[MainMenuUI] Hit: {hit.collider.gameObject.name}");
            var clickZone = hit.collider.GetComponent<ClickZone>();
            if (clickZone != null)
            {
                Debug.Log($"[MainMenuUI] ClickZone found: {clickZone.category}");
                ShowBodyPartPopup(clickZone.category, hit.point);
            }
            else
            {
                Debug.Log("[MainMenuUI] No ClickZone on hit object");
                HideBodyPartPopup();
            }
        }
        else
        {
            Debug.Log("[MainMenuUI] Raycast hit nothing");
            HideBodyPartPopup();
        }
    }

    void ConnectToPhoton()
    {
        if (PhotonNetwork.IsConnected)
        {
            isConnected = true;
            statusText.text = "ONLINE - READY";
            statusText.color = new Color(0.3f, 1f, 0.5f);
            return;
        }
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        isConnected = true;
        statusText.text = "ONLINE - READY";
        statusText.color = new Color(0.3f, 1f, 0.5f);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        isConnected = false;
        statusText.text = "OFFLINE";
        statusText.color = new Color(1f, 0.4f, 0.3f);
    }

    void OnPlay()
    {
        if (!isConnected)
        {
            statusText.text = "NOT CONNECTED";
            statusText.color = new Color(1f, 0.4f, 0.3f);
            return;
        }

        PlayerPrefs.SetString("PlayerName", nameInput.text);

        // Save weapon loadout before loading game
        if (weaponCustomizer == null)
        {
            weaponCustomizer = WeaponCustomizer.Instance;
        }
        if (weaponCustomizer != null)
        {
            weaponCustomizer.SaveAllLoadouts();
            Debug.Log($"[MainMenuUI] Saved weapon loadout before deploy, active loadout: {weaponCustomizer.GetActiveLoadoutIndex()}");
        }

        PlayerPrefs.Save();
        PhotonNetwork.NickName = nameInput.text;

        ShowPanel(loadingGroup);
        loadProgress = 0f;

        // Join or create a room, then load scene in OnJoinedRoom
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 10;
        roomOptions.IsVisible = true;
        roomOptions.IsOpen = true;
        PhotonNetwork.JoinOrCreateRoom("KlyraFPS_Main", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[MainMenuUI] Joined room: {PhotonNetwork.CurrentRoom.Name}");
        StartCoroutine(LoadGame());
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[MainMenuUI] Failed to join room: {message}");
        ShowPanel(mainMenuGroup);
        statusText.text = "FAILED TO JOIN";
        statusText.color = new Color(1f, 0.4f, 0.3f);
    }

    void OnCustomize()
    {
        // Force show the customize UI immediately
        if (customizeGroup != null)
        {
            customizeGroup.alpha = 1;
            customizeGroup.blocksRaycasts = true;
            Debug.Log($"[MainMenuUI] customizeGroup has {customizeGroup.transform.childCount} children");
            for (int i = 0; i < customizeGroup.transform.childCount; i++)
            {
                var child = customizeGroup.transform.GetChild(i);
                Debug.Log($"  - Child {i}: {child.name}");
            }
        }
        if (mainMenuGroup != null)
        {
            mainMenuGroup.alpha = 0;
            mainMenuGroup.blocksRaycasts = false;
        }
        isCustomizing = true;

        // Save current camera position to return to later
        if (menuCamera != null)
        {
            menuCameraPos = menuCamera.transform.position;
            menuCameraRot = menuCamera.transform.rotation;
            menuCameraFOV = menuCamera.fieldOfView;
        }

        // Setup click zones on the character
        SetupCharacterClickZones();

        // Zoom camera in on the character
        if (playerShowcaseCharacter != null && menuCamera != null)
        {
            Vector3 charPos = playerShowcaseCharacter.transform.position;
            // Position camera for a nice full-body view
            Vector3 targetCamPos = charPos + new Vector3(0.2f, 1.4f, 2.8f);
            Vector3 lookAtPoint = charPos + Vector3.up * 1f;
            Quaternion targetRot = Quaternion.LookRotation(lookAtPoint - targetCamPos);

            StartCoroutine(AnimateCameraTo(targetCamPos, targetRot, 45f, 0.5f));
        }
        else
        {
            Debug.LogWarning($"[MainMenuUI] Cannot zoom - playerShowcaseCharacter: {playerShowcaseCharacter != null}, menuCamera: {menuCamera != null}");
        }

        Debug.Log($"[MainMenuUI] Customize opened - Team: {selectedCustomizeTeam}");
    }

    void OnSettings()
    {
        ShowPanel(settingsGroup);
    }

    IEnumerator LoadGame()
    {
        // Small delay before starting load
        yield return new WaitForSeconds(0.3f);

        // Start loading the game scene
        PhotonNetwork.LoadLevel(gameSceneName);

        // Wait for Photon to start the level load
        yield return new WaitForSeconds(0.1f);

        // Track loading progress
        while (PhotonNetwork.LevelLoadingProgress < 1f)
        {
            loadProgress = PhotonNetwork.LevelLoadingProgress;
            yield return null;
        }

        // Loading complete - ensure bar is full
        loadProgress = 1f;
        yield return new WaitForSeconds(0.2f);

        // Hide loading panel (in case scene transition hasn't destroyed us yet)
        if (loadingGroup != null)
        {
            loadingGroup.alpha = 0;
            loadingGroup.blocksRaycasts = false;
        }
    }
}
