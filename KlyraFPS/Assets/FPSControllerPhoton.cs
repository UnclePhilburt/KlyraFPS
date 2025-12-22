using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

public class FPSControllerPhoton : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 10f;
    public float jumpHeight = 1.8f;
    public float gravity = -35f;

    [Header("Mouse Look Settings")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;

    [Header("References")]
    public Transform cameraTransform;
    public Transform muzzlePoint;
    public Transform weaponTransform;

    [Header("Camera Settings")]
    public float cameraHeight = 1.6f;

    [Header("First Person Weapon")]
    public Vector3 weaponOffset = new Vector3(0.15f, -0.33f, 0.22f);

    [Header("Weapon Bob")]
    public float bobFrequency = 10f;
    public float bobHorizontalAmount = 0.03f;
    public float bobVerticalAmount = 0.02f;
    private float bobTimer = 0f;
    private float bobIntensity = 0f;
    private Vector3 currentWeaponOffset;

    [Header("Shooting Settings")]
    public float fireRate = 0.1f;
    public float damage = 25f;
    public float range = 100f;

    [Header("Weapon Effects")]
    public GameObject muzzleFlashPrefab;  // Assign FX_Gunshot_01 from Synty
    public GameObject tracerPrefab;        // Assign FX_Bullet_Trail_Mesh from Synty

    [Header("Weapon Audio")]
    public AudioClip gunshotSound;
    public float gunshotVolume = 0.7f;
    private AudioSource weaponAudio;

    [Header("Hit Feedback Audio")]
    public AudioClip hitMarkerSound;
    public AudioClip killSound;
    public float hitMarkerVolume = 0.5f;
    public float killSoundVolume = 0.6f;
    private AudioSource feedbackAudio;

    [Header("ADS Settings")]
    public float aimFOV = 30f;
    public float normalFOV = 60f;
    public float aimSpeed = 10f;
    public Vector3 aimPosition = new Vector3(0f, -0.21f, 0.25f);

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead = false;
    private int lastAttackerViewID = -1;

    // Components
    private CharacterController controller;
    private Animator animator;
    private Camera playerCamera;

    // Input values
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool jumpPressed;
    private bool sprintPressed;

    // Movement
    private Vector3 velocity;
    private float currentSpeed;

    // Jump buffering
    private float jumpBufferTime = 0.15f;
    private float jumpBufferCounter;
    private float coyoteTime = 0.1f;
    private float coyoteCounter;
    private int jumpCount = 0;
    private int maxJumps = 2;

    // Mouse look
    private float rotationX = 0f;
    private float rotationY = 0f;

    // Shooting
    private float nextFireTime = 0f;
    private Light muzzleFlash;
    private bool isAiming = false;

    // Network sync variables
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private float networkMoveSpeed;

    // Network interpolation for smooth remote players
    private Vector3 networkVelocity;
    private Vector3 lastNetworkPosition;
    private float networkLag;
    private float lastNetworkTime;

    private bool isInitialized = false;

    // Static reference to ensure only one local player
    private static FPSControllerPhoton localPlayerInstance;

    // Team system
    public Team playerTeam = Team.None;

    // Squad system
    [Header("Squad System")]
    public int maxSquadSize = 7;
    public float recruitRadius = 15f;
    private System.Collections.Generic.List<AIController> squadMembers = new System.Collections.Generic.List<AIController>();
    private Transform currentTarget;  // The enemy the player is currently shooting at

    // Squad UI markers
    private AIController lookedAtSquadMember;
    private GUIStyle nameTagStyle;
    private bool uiStylesInitialized = false;

    // Vehicle System
    private bool isInVehicle = false;
    private HelicopterSeat currentVehicleSeat;
    public float vehicleEntryRadius = 5f;

    // Helicopter interaction prompt
    private HelicopterController lookingAtHelicopter = null;
    private string helicopterPromptText = "";
    private float helicopterPromptAlpha = 0f;

    // Jet
    private JetController currentJet = null;
    private bool isInJet = false;
    private JetController lookingAtJet = null;
    private string jetPromptText = "";
    private float jetPromptAlpha = 0f;

    // Tank
    private TankController currentTank = null;
    private bool isInTank = false;
    private TankController lookingAtTank = null;
    private string tankPromptText = "";
    private float tankPromptAlpha = 0f;

    [Header("Team Skins")]
    public string[] phantomSkinNames = {
        "SM_Chr_Soldier_Male_01",
        "SM_Chr_Soldier_Male_02",
        "SM_Chr_Soldier_Female_01",
        "SM_Chr_Soldier_Female_02",
        "SM_Chr_Contractor_Male_01",
        "SM_Chr_Contractor_Male_02",
        "SM_Chr_Contractor_Female_01",
        "SM_Chr_Ghillie_Male_01",
        "SM_Chr_Pilot_Male_01",
        "SM_Chr_Pilot_Female_01"
    };
    public string[] havocSkinNames = {
        "SM_Chr_Insurgent_Male_01",
        "SM_Chr_Insurgent_Male_02",
        "SM_Chr_Insurgent_Male_03",
        "SM_Chr_Insurgent_Male_04",
        "SM_Chr_Insurgent_Male_05",
        "SM_Chr_Insurgent_Female_01",
        "SM_Chr_Insurgent_Female_02"
    };

    [Header("Customization Attachments")]
    public GameObject[] headgearPrefabs;
    public GameObject[] facewearPrefabs;
    public GameObject[] backpackPrefabs;

    [Header("K-9 Companion")]
    public GameObject[] dogPrefabs;
    private GameObject playerDog;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        currentHealth = maxHealth;

        // Clean up any leftover MainMenuUI from scene transition
        CleanupMainMenuUI();

        // Setup weapon audio
        weaponAudio = gameObject.AddComponent<AudioSource>();
        weaponAudio.spatialBlend = 1f; // 3D sound
        weaponAudio.maxDistance = 50f;
        weaponAudio.rolloffMode = AudioRolloffMode.Linear;
        weaponAudio.playOnAwake = false;

        // Setup feedback audio (hit markers, kill sounds) - 2D for local player only
        feedbackAudio = gameObject.AddComponent<AudioSource>();
        feedbackAudio.spatialBlend = 0f; // 2D sound - plays at full volume for local player
        feedbackAudio.playOnAwake = false;

        // Load hit feedback sounds from Resources if not assigned
        if (hitMarkerSound == null)
            hitMarkerSound = Resources.Load<AudioClip>("hitmarker");
        if (killSound == null)
            killSound = Resources.Load<AudioClip>("killsound");

        // Disable any cameras that came with the prefab FIRST
        // This prevents multiple cameras from being active
        Camera[] prefabCameras = GetComponentsInChildren<Camera>();
        foreach (Camera cam in prefabCameras)
        {
            cam.enabled = false;
            // Also disable AudioListener if present
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        // Get team from instantiation data
        if (photonView.InstantiationData != null && photonView.InstantiationData.Length > 0)
        {
            playerTeam = (Team)(int)photonView.InstantiationData[0];
        }

        // Set up the correct skin for this player's team
        SetupTeamSkin();

        if (photonView.IsMine)
        {
            InitializeLocalPlayer();
        }
        else
        {
            InitializeRemotePlayer();
        }
    }

    void SetupTeamSkin()
    {
        // First, disable ALL character models
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            if (child.name.StartsWith("SM_Chr_"))
            {
                child.gameObject.SetActive(false);
            }
        }

        // Choose which skins to use based on team
        string[] skinNames = playerTeam == Team.Phantom ? phantomSkinNames : havocSkinNames;

        // Load saved character index from PlayerPrefs (falls back to random if not set)
        string teamName = playerTeam == Team.Phantom ? "Phantom" : "Havoc";
        int savedCharIndex = PlayerPrefs.GetInt($"{teamName}_CharacterIndex", -1);

        string chosenSkin;
        if (savedCharIndex >= 0 && savedCharIndex < skinNames.Length)
        {
            // Use saved character from customizer
            chosenSkin = skinNames[savedCharIndex];
        }
        else if (skinNames.Length > 0)
        {
            // Fallback to random if no saved customization
            chosenSkin = skinNames[Random.Range(0, skinNames.Length)];
        }
        else
        {
            return;
        }

        // Find and enable the chosen skin
        Transform activeCharacter = null;
        foreach (Transform child in allChildren)
        {
            if (child.name == chosenSkin)
            {
                child.gameObject.SetActive(true);
                activeCharacter = child;

                // Get animator from the skin if it exists
                Animator skinAnimator = child.GetComponent<Animator>();
                if (skinAnimator != null)
                {
                    animator = skinAnimator;
                }
                break;
            }
        }

        // Apply saved attachments
        if (activeCharacter != null)
        {
            ApplyCharacterAttachments(activeCharacter, teamName);
        }
    }

    void ApplyCharacterAttachments(Transform character, string teamName)
    {
        // Find attachment points
        Transform headPoint = null, facePoint = null, backPoint = null;
        var transforms = character.GetComponentsInChildren<Transform>();

        foreach (var t in transforms)
        {
            string name = t.name.ToLower();

            // Hide original attachments
            if (name.Contains("attach_"))
            {
                if (name.Contains("backpack") || name.Contains("pouch"))
                {
                    if (backPoint == null) backPoint = t.parent;
                }
                t.gameObject.SetActive(false);
            }

            // Find bone attachment points
            if (headPoint == null && name.Contains("head") && !name.Contains("headset"))
                headPoint = t;
            else if (facePoint == null && (name.Contains("neck") || name.Contains("jaw")))
                facePoint = t;
            else if (backPoint == null && name.Contains("spine") && name.Contains("02"))
                backPoint = t;
        }

        if (facePoint == null) facePoint = headPoint;

        // Load saved attachment indices
        int headIndex = PlayerPrefs.GetInt($"{teamName}_HeadgearIndex", -1);
        int faceIndex = PlayerPrefs.GetInt($"{teamName}_FacewearIndex", -1);
        int backIndex = PlayerPrefs.GetInt($"{teamName}_BackpackIndex", -1);

        // Apply headgear
        if (headIndex >= 0 && headIndex < headgearPrefabs.Length && headPoint != null && headgearPrefabs[headIndex] != null)
        {
            var attachment = Instantiate(headgearPrefabs[headIndex], headPoint);
            attachment.transform.localPosition = Vector3.zero;
            attachment.transform.localRotation = Quaternion.identity;
        }

        // Apply facewear
        if (faceIndex >= 0 && faceIndex < facewearPrefabs.Length && facePoint != null && facewearPrefabs[faceIndex] != null)
        {
            var attachment = Instantiate(facewearPrefabs[faceIndex], facePoint);
            attachment.transform.localPosition = Vector3.zero;
            attachment.transform.localRotation = Quaternion.identity;
        }

        // Apply backpack
        if (backIndex >= 0 && backIndex < backpackPrefabs.Length && backPoint != null && backpackPrefabs[backIndex] != null)
        {
            var attachment = Instantiate(backpackPrefabs[backIndex], backPoint);
            attachment.transform.localPosition = Vector3.zero;
            attachment.transform.localRotation = Quaternion.identity;
        }
    }

    void SpawnPlayerDog()
    {
        // Only spawn dog for local player
        if (!photonView.IsMine) return;

        // Get saved dog index for this team
        string teamName = playerTeam == Team.Phantom ? "Phantom" : "Havoc";
        int dogIndex = PlayerPrefs.GetInt($"{teamName}_DogIndex", 0);

        // Validate index and prefabs
        if (dogPrefabs == null || dogPrefabs.Length == 0)
        {
            Debug.LogWarning("[FPSControllerPhoton] No dog prefabs assigned!");
            return;
        }

        if (dogIndex < 0 || dogIndex >= dogPrefabs.Length)
        {
            dogIndex = 0; // Fallback to first dog
        }

        GameObject dogPrefab = dogPrefabs[dogIndex];
        if (dogPrefab == null)
        {
            Debug.LogWarning($"[FPSControllerPhoton] Dog prefab at index {dogIndex} is null!");
            return;
        }

        // Spawn dog next to player
        Vector3 spawnPos = transform.position + transform.right * 2f + Vector3.up * 0.5f;
        playerDog = Instantiate(dogPrefab, spawnPos, transform.rotation);
        playerDog.name = $"PlayerDog_{photonView.ViewID}";

        // Setup DogController
        DogController dogController = playerDog.GetComponent<DogController>();
        if (dogController == null)
        {
            dogController = playerDog.AddComponent<DogController>();
        }

        // Configure dog to follow this player
        dogController.team = playerTeam;
        dogController.playerHandler = this;
        dogController.handler = null; // Clear any AI handler

        Debug.Log($"[FPSControllerPhoton] Spawned player dog: {dogPrefab.name} for team {teamName}");
    }

    void InitializeLocalPlayer()
    {
        // Ensure only one local player exists
        if (localPlayerInstance != null && localPlayerInstance != this)
        {
            InitializeRemotePlayer();
            return;
        }
        localPlayerInstance = this;

        // Destroy any existing player cameras first
        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in allCameras)
        {
            if (cam.gameObject.name.StartsWith("PlayerCamera_"))
            {
                Destroy(cam.gameObject);
            }
        }

        // Setup camera
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // Create our own camera
            GameObject camObj = new GameObject($"PlayerCamera_{photonView.ViewID}");
            playerCamera = camObj.AddComponent<Camera>();
            playerCamera.fieldOfView = normalFOV;
            playerCamera.nearClipPlane = 0.1f;
            playerCamera.farClipPlane = 1000f;
            cameraTransform = camObj.transform;

            // Add AudioListener
            camObj.AddComponent<AudioListener>();

            // Copy settings from main camera
            playerCamera.clearFlags = mainCam.clearFlags;
            playerCamera.backgroundColor = mainCam.backgroundColor;
            playerCamera.cullingMask = mainCam.cullingMask;

            // Disable the scene camera completely
            mainCam.gameObject.SetActive(false);
        }
        else
        {
            // No main camera, create one anyway
            GameObject camObj = new GameObject($"PlayerCamera_{photonView.ViewID}");
            playerCamera = camObj.AddComponent<Camera>();
            playerCamera.fieldOfView = normalFOV;
            playerCamera.nearClipPlane = 0.1f;
            playerCamera.farClipPlane = 1000f;
            playerCamera.clearFlags = CameraClearFlags.Skybox;
            cameraTransform = camObj.transform;
            camObj.AddComponent<AudioListener>();
        }

        // Disable AudioListener on main camera if it exists
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        foreach (AudioListener listener in listeners)
        {
            if (listener.gameObject != cameraTransform?.gameObject)
            {
                listener.enabled = false;
            }
        }

        // Initialize rotation from current transform
        rotationY = transform.eulerAngles.y;
        rotationX = 0f;

        // Hide local player body for first person view
        HideLocalPlayerBody();

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        currentWeaponOffset = weaponOffset;

        // Apply player's custom weapon loadout
        ApplyWeaponLoadout();

        isInitialized = true;

        // Spawn player's K-9 companion
        SpawnPlayerDog();
    }

    void ApplyWeaponLoadout()
    {
        // Load the active loadout from PlayerPrefs
        string loadoutJson = PlayerPrefs.GetString("ActiveLoadout", "");
        Debug.Log($"[FPSControllerPhoton] Loading weapon loadout, JSON length: {loadoutJson?.Length ?? 0}");

        if (string.IsNullOrEmpty(loadoutJson))
        {
            Debug.Log("[FPSControllerPhoton] No custom weapon loadout found, using defaults");
            return;
        }

        try
        {
            WeaponBuildData build = WeaponBuildData.FromJson(loadoutJson);
            if (build == null)
            {
                Debug.LogWarning("[FPSControllerPhoton] Failed to parse weapon build from JSON");
                return;
            }

            Debug.Log($"[FPSControllerPhoton] Loaded build: presetIndex={build.presetIndex}, platform={build.platform}");

            // Find WeaponCustomizer (use singleton or search scene)
            WeaponCustomizer customizer = WeaponCustomizer.Instance;
            Debug.Log($"[FPSControllerPhoton] WeaponCustomizer.Instance: {(customizer != null ? customizer.name : "null")}");

            if (customizer == null)
            {
                customizer = FindFirstObjectByType<WeaponCustomizer>();
                Debug.Log($"[FPSControllerPhoton] FindFirstObjectByType result: {(customizer != null ? customizer.name : "null")}");
            }

            if (customizer != null)
            {
                int presetCount = customizer.GetPresetCount();
                Debug.Log($"[FPSControllerPhoton] WeaponCustomizer has {presetCount} presets");

                // Use preset stats (not calculated from parts)
                WeaponStats stats = customizer.GetPresetStats(build.presetIndex);
                if (stats != null)
                {
                    // Apply stats
                    fireRate = stats.fireRate;
                    damage = stats.damage;
                    range = stats.range;
                    aimFOV = stats.aimFOV;
                    aimSpeed = stats.adsSpeed;

                    Debug.Log($"[FPSControllerPhoton] Applied preset {build.presetIndex} - Damage: {damage}, FireRate: {fireRate}, Range: {range}");

                    // Sync weapon preset to other players
                    photonView.RPC("RPC_SyncWeaponPreset", RpcTarget.Others, build.presetIndex);
                }
                else
                {
                    Debug.LogWarning($"[FPSControllerPhoton] GetPresetStats returned null for preset {build.presetIndex}");
                }

                // Swap the weapon model to match the preset
                ApplyWeaponModel(customizer, build.presetIndex);
            }
            else
            {
                Debug.LogWarning("[FPSControllerPhoton] WeaponCustomizer not found, cannot apply loadout stats");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FPSControllerPhoton] Error applying weapon loadout: {e.Message}\n{e.StackTrace}");
        }
    }

    [PunRPC]
    void RPC_SyncWeaponAppearance(int[] attachmentIndices)
    {
        // Apply weapon appearance for remote players
        // This is called when a player joins or when weapon is changed
        Debug.Log($"[FPSControllerPhoton] Received weapon appearance sync with {attachmentIndices?.Length ?? 0} indices");

        // Find the weapon assembler on this player's weapon
        ModularWeaponAssembler assembler = GetComponentInChildren<ModularWeaponAssembler>();
        if (assembler != null)
        {
            WeaponCustomizer customizer = FindFirstObjectByType<WeaponCustomizer>();
            if (customizer != null)
            {
                assembler.ApplyAttachmentIndices(attachmentIndices, customizer);
            }
        }
    }

    [PunRPC]
    void RPC_SyncWeaponPreset(int presetIndex)
    {
        // Apply weapon preset for remote players
        Debug.Log($"[FPSControllerPhoton] Received weapon preset sync: {presetIndex}");

        WeaponCustomizer customizer = WeaponCustomizer.Instance ?? FindFirstObjectByType<WeaponCustomizer>();
        if (customizer != null)
        {
            ApplyWeaponModel(customizer, presetIndex);
        }
    }

    void ApplyWeaponModel(WeaponCustomizer customizer, int presetIndex)
    {
        if (customizer == null || customizer.weaponPresets == null) return;
        if (presetIndex < 0 || presetIndex >= customizer.weaponPresets.Length) return;

        GameObject presetPrefab = customizer.weaponPresets[presetIndex];
        if (presetPrefab == null) return;

        // First, destroy/hide the existing weaponTransform if it exists (the default weapon)
        if (weaponTransform != null)
        {
            Debug.Log($"[FPSControllerPhoton] Destroying existing weapon: {weaponTransform.name}");
            Destroy(weaponTransform.gameObject);
            weaponTransform = null;
        }

        // Find the weapon holder (where the gun model goes)
        Transform weaponHolder = cameraTransform;
        if (weaponHolder == null)
        {
            weaponHolder = transform.Find("WeaponHolder");
        }
        if (weaponHolder == null && playerCamera != null)
        {
            weaponHolder = playerCamera.transform;
        }
        if (weaponHolder == null)
        {
            Debug.LogWarning("[FPSControllerPhoton] No weapon holder found for weapon model");
            return;
        }

        // Remove any other weapon models that might exist on the camera
        foreach (Transform child in weaponHolder)
        {
            if (child.name.ToLower().Contains("weapon") || child.name.ToLower().Contains("gun") ||
                child.name.ToLower().Contains("preset") || child.name.ToLower().Contains("sm_wep"))
            {
                Destroy(child.gameObject);
            }
        }

        // Also check player root for any weapon objects
        foreach (Transform child in transform)
        {
            string lowerName = child.name.ToLower();
            if (lowerName.Contains("weapon") || lowerName.Contains("gun") ||
                lowerName.Contains("sm_wep") || lowerName.Contains("rifle") ||
                lowerName.Contains("pistol") || lowerName.Contains("preset"))
            {
                Debug.Log($"[FPSControllerPhoton] Removing old weapon from player root: {child.name}");
                Destroy(child.gameObject);
            }
        }

        // Instantiate the new weapon model
        GameObject weaponModel = Instantiate(presetPrefab, weaponHolder);
        weaponModel.name = "WeaponModel_" + presetPrefab.name;
        weaponModel.transform.localPosition = new Vector3(0.2f, -0.2f, 0.4f);
        weaponModel.transform.localRotation = Quaternion.identity;
        weaponModel.transform.localScale = Vector3.one;

        // Set as the new weaponTransform so weapon bob/positioning works
        weaponTransform = weaponModel.transform;

        // Disable any colliders on the weapon model
        foreach (var col in weaponModel.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // Try to find muzzle point on the new weapon
        Transform newMuzzle = weaponModel.transform.Find("MuzzlePoint");
        if (newMuzzle == null) newMuzzle = weaponModel.transform.Find("Muzzle");
        if (newMuzzle == null)
        {
            // Search children for anything with muzzle in name
            foreach (Transform child in weaponModel.GetComponentsInChildren<Transform>())
            {
                if (child.name.ToLower().Contains("muzzle"))
                {
                    newMuzzle = child;
                    break;
                }
            }
        }
        if (newMuzzle != null)
        {
            muzzlePoint = newMuzzle;
        }
        else
        {
            // Create a fallback muzzle point at the front of the weapon
            GameObject muzzleObj = new GameObject("MuzzlePoint_Generated");
            muzzleObj.transform.SetParent(weaponModel.transform);
            muzzleObj.transform.localPosition = new Vector3(0, 0, 0.5f); // Front of weapon
            muzzleObj.transform.localRotation = Quaternion.identity;
            muzzlePoint = muzzleObj.transform;
            Debug.Log("[FPSControllerPhoton] Created fallback muzzle point");
        }

        Debug.Log($"[FPSControllerPhoton] Applied weapon model: {presetPrefab.name}, weaponTransform set, muzzle: {(muzzlePoint != null ? muzzlePoint.name : "not found")}");
    }

    void InitializeRemotePlayer()
    {
        // Add a CapsuleCollider for hit detection BEFORE disabling CharacterController
        // CharacterController is a collider, but we need to disable it for remote players
        // So we add a separate collider for raycasts to hit
        CapsuleCollider hitCollider = gameObject.GetComponent<CapsuleCollider>();
        if (hitCollider == null)
        {
            hitCollider = gameObject.AddComponent<CapsuleCollider>();
            // Match CharacterController dimensions
            if (controller != null)
            {
                hitCollider.center = controller.center;
                hitCollider.radius = controller.radius;
                hitCollider.height = controller.height;
            }
            else
            {
                // Default player-sized collider
                hitCollider.center = new Vector3(0, 1f, 0);
                hitCollider.radius = 0.4f;
                hitCollider.height = 2f;
            }
        }

        // Disable character controller for remote players (we use network interpolation)
        if (controller != null)
        {
            controller.enabled = false;
        }

        // Clear references not needed for remote players
        cameraTransform = null;
        playerCamera = null;
        weaponTransform = null;

        networkPosition = transform.position;
        networkRotation = transform.rotation;
        lastNetworkPosition = transform.position;
        lastNetworkTime = Time.time;
        networkVelocity = Vector3.zero;

        isInitialized = true;
    }

    void HideLocalPlayerBody()
    {
        // Hide body mesh for first-person view
        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in skinnedRenderers)
        {
            renderer.enabled = false;
        }

        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in meshRenderers)
        {
            bool isWeapon = renderer.name.Contains("Wep") ||
                           renderer.name.Contains("Weapon") ||
                           renderer.name.Contains("Gun") ||
                           renderer.name.Contains("Rifle") ||
                           renderer.name.Contains("Pistol");

            if (!isWeapon)
            {
                renderer.enabled = false;
            }
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        if (photonView.IsMine)
        {
            UpdateLocalPlayer();
        }
        else
        {
            UpdateRemotePlayer();
        }
    }

    void UpdateLocalPlayer()
    {
        // Don't allow any actions while dead
        if (isDead) return;

        // If in vehicle, only handle vehicle exit
        if (isInVehicle)
        {
            // Handle jet exit and controls
            if (isInJet && currentJet != null)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
                {
                    ExitJet();
                }
                // Jet controls handled by JetController
            }
            // Handle tank exit
            else if (isInTank && currentTank != null)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
                {
                    ExitTank();
                }
                // Tank controls handled by TankController
            }
            // Helicopter controls handled by HelicopterSeat
            return;
        }

        // Always handle squad/TAB input first (so TAB works to close the screen)
        HandleSquad();

        // Check if squad command screen is open
        SquadCommandScreen cmdScreen = SquadCommandScreen.Instance;
        bool cmdScreenActive = cmdScreen != null && cmdScreen.IsActive;

        if (cmdScreenActive)
        {
            return; // Don't process other player input while commanding
        }

        // Check for vehicle entry with F key
        HandleVehicleEntry();

        ReadInput();
        HandleMovement();
        HandleMouseLook();
        HandleShooting();
        HandleADS();
        CheckLookingAtHelicopter();
        CheckLookingAtJet();
        CheckLookingAtTank();
        // Note: PositionWeapon moved to LateUpdate to sync with camera
    }

    void HandleSquad()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Press G to recruit nearby friendly AI
        if (keyboard.gKey.wasPressedThisFrame)
        {
            RecruitNearbySquad();
        }

        // Press TAB to toggle squad command screen
        if (keyboard.tabKey.wasPressedThisFrame)
        {
            SquadCommandScreen screen = SquadCommandScreen.Instance;

            if (screen != null && screen.IsActive)
            {
                // Close the screen
                screen.Hide();
            }
            else if (squadMembers.Count > 0)
            {
                // Open the screen
                OpenSquadCommandScreen();
            }
        }

        // Clean up dead or null squad members
        squadMembers.RemoveAll(ai => ai == null || ai.isDead);

        // Clear target if it's dead or destroyed
        if (currentTarget != null)
        {
            AIController targetAI = currentTarget.GetComponent<AIController>();
            FPSControllerPhoton targetPlayer = currentTarget.GetComponent<FPSControllerPhoton>();

            if ((targetAI != null && targetAI.isDead) || (targetPlayer != null && targetPlayer.isDead))
            {
                currentTarget = null;
            }
        }
    }

    void RecruitNearbySquad()
    {
        // Find all friendly AI within radius
        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);

        int recruited = 0;
        foreach (AIController ai in allAI)
        {
            // Skip if squad is full
            if (squadMembers.Count >= maxSquadSize) break;

            // Skip if already in squad
            if (squadMembers.Contains(ai)) continue;

            // Skip if not on our team
            if (ai.team != playerTeam) continue;

            // Skip if dead
            if (ai.isDead) continue;

            // Skip if stuck (hasn't moved in a while)
            if (ai.IsStuck) continue;

            // Skip if already in another squad
            if (ai.IsInSquad()) continue;

            // Check distance
            float dist = Vector3.Distance(transform.position, ai.transform.position);
            if (dist <= recruitRadius)
            {
                ai.JoinSquad(this);
                squadMembers.Add(ai);
                recruited++;
            }
        }

    }

    public void DisbandSquad()
    {
        foreach (AIController ai in squadMembers)
        {
            if (ai != null)
            {
                ai.LeaveSquad();
            }
        }
        squadMembers.Clear();
    }

    // Property for squad members to get leader's target
    public Transform GetCurrentTarget()
    {
        return currentTarget;
    }

    public int GetSquadSize()
    {
        return squadMembers.Count;
    }

    public System.Collections.Generic.List<AIController> GetSquadMembers()
    {
        return squadMembers;
    }

    void OpenSquadCommandScreen()
    {
        SquadCommandScreen commandScreen = FindFirstObjectByType<SquadCommandScreen>();
        if (commandScreen == null)
        {
            GameObject screenObj = new GameObject("SquadCommandScreen");
            commandScreen = screenObj.AddComponent<SquadCommandScreen>();
        }

        commandScreen.Show(this);
    }

    void HandleVehicleEntry()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.fKey.wasPressedThisFrame)
        {
            // Try tank first, then jet, then helicopter
            if (!TryEnterNearbyTank())
            {
                if (!TryEnterNearbyJet())
                {
                    TryEnterNearbyHelicopter();
                }
            }
        }
    }

    bool TryEnterNearbyTank()
    {
        TankController[] tanks = FindObjectsByType<TankController>(FindObjectsSortMode.None);

        TankController closestTank = null;
        float closestDist = vehicleEntryRadius;

        foreach (var tank in tanks)
        {
            if (tank.isDestroyed) continue;
            if (tank.HasDriver) continue; // Already has driver
            if (tank.TankTeam != Team.None && tank.TankTeam != playerTeam) continue; // Enemy tank

            float dist = Vector3.Distance(transform.position, tank.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestTank = tank;
            }
        }

        if (closestTank != null)
        {
            EnterTank(closestTank);
            return true;
        }

        return false;
    }

    void EnterTank(TankController tank)
    {
        currentTank = tank;
        isInTank = true;
        isInVehicle = true;

        // Disable character controller
        if (controller != null) controller.enabled = false;

        // Hide player model
        SetPlayerModelVisible(false);

        // Disable collider
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = false;

        // Parent to tank
        transform.SetParent(tank.transform);
        if (tank.driverSeat != null)
            transform.localPosition = tank.driverSeat.localPosition;
        else
            transform.localPosition = Vector3.up * 2f;

        // Tell tank we're the driver
        tank.SetPlayerDriver(this);

        Debug.Log("[Player] Entered tank - WASD to move, mouse to aim turret, LMB to fire, F to exit");
    }

    public void ExitTank()
    {
        if (currentTank == null) return;

        // Tell tank we're leaving
        currentTank.ClearPlayerDriver();

        // Unparent and position next to tank
        transform.SetParent(null);
        transform.position = currentTank.transform.position + currentTank.transform.right * 5f + Vector3.up * 2f;

        // Re-enable character controller
        if (controller != null) controller.enabled = true;

        // Show player model
        SetPlayerModelVisible(true);

        // Re-enable collider
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = true;

        currentTank = null;
        isInTank = false;
        isInVehicle = false;

        Debug.Log("[Player] Exited tank");
    }

    bool TryEnterNearbyJet()
    {
        JetController[] jets = FindObjectsByType<JetController>(FindObjectsSortMode.None);

        JetController closestJet = null;
        float closestDist = vehicleEntryRadius;

        foreach (var jet in jets)
        {
            if (jet.isDestroyed) continue;
            if (jet.HasPilot) continue; // Already has pilot
            if (jet.JetTeam != Team.None && jet.JetTeam != playerTeam) continue; // Enemy jet

            float dist = Vector3.Distance(transform.position, jet.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestJet = jet;
            }
        }

        if (closestJet != null)
        {
            EnterJet(closestJet);
            return true;
        }

        return false;
    }

    void EnterJet(JetController jet)
    {
        currentJet = jet;
        isInJet = true;
        isInVehicle = true;

        // Disable character controller
        if (controller != null) controller.enabled = false;

        // Hide player model
        SetPlayerModelVisible(false);

        // Disable collider
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = false;

        // Parent to jet
        transform.SetParent(jet.transform);
        if (jet.pilotSeatPosition != null)
            transform.localPosition = jet.pilotSeatPosition.localPosition;
        else
            transform.localPosition = Vector3.zero;

        // Tell jet we're the pilot
        jet.SetPlayerPilot(this);

        Debug.Log("[Player] Entered jet - F to exit, E to start engine");
    }

    public void ExitJet()
    {
        if (currentJet == null) return;

        // Tell jet we're leaving
        currentJet.ClearPlayerPilot();

        // Unparent and position next to jet
        transform.SetParent(null);
        transform.position = currentJet.transform.position + currentJet.transform.right * 5f + Vector3.up * 2f;

        // Re-enable character controller
        if (controller != null) controller.enabled = true;

        // Show player model
        SetPlayerModelVisible(true);

        // Re-enable collider
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = true;

        currentJet = null;
        isInJet = false;
        isInVehicle = false;

        Debug.Log("[Player] Exited jet");
    }

    void TryEnterNearbyHelicopter()
    {
        // First check if we're close enough to enter directly
        HelicopterController[] helicopters = FindObjectsByType<HelicopterController>(FindObjectsSortMode.None);

        HelicopterController closestHeli = null;
        float closestDist = vehicleEntryRadius;

        foreach (var heli in helicopters)
        {
            if (heli.isDestroyed) continue;

            float dist = Vector3.Distance(transform.position, heli.transform.position);
            if (dist < closestDist)
            {
                // Check if there's an available seat for our team
                HelicopterSeat seat = heli.GetAvailableSeat(playerTeam);
                if (seat != null)
                {
                    closestDist = dist;
                    closestHeli = heli;
                }
            }
        }

        if (closestHeli != null)
        {
            HelicopterSeat seat = closestHeli.GetAvailableSeat(playerTeam);
            if (seat != null)
            {
                seat.TryEnter(this);
                return;
            }
        }

        // Not close enough - check if we're looking at a helicopter to call it
        TryCallHelicopter();
    }

    void TryCallHelicopter()
    {
        // Raycast to see what we're looking at
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 500f))
        {
            // Check if we hit a helicopter
            HelicopterController heli = hit.collider.GetComponentInParent<HelicopterController>();
            if (heli != null && !heli.isDestroyed)
            {
                // Check if it's friendly
                if (heli.helicopterTeam == Team.None || heli.helicopterTeam == playerTeam)
                {
                    // Check if it has an AI pilot
                    if (heli.HasAIPilot)
                    {
                        // Call the helicopter to us
                        heli.CallToPlayer(this);
                        Debug.Log($"Calling helicopter {heli.name} to player");
                    }
                    else
                    {
                        Debug.Log("Helicopter has no pilot to fly it to you");
                    }
                }
                else
                {
                    Debug.Log("Cannot call enemy helicopter");
                }
            }
        }
    }

    void CheckLookingAtHelicopter()
    {
        lookingAtHelicopter = null;
        helicopterPromptText = "";

        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 500f))
        {
            HelicopterController heli = hit.collider.GetComponentInParent<HelicopterController>();
            if (heli != null && !heli.isDestroyed)
            {
                // Check if friendly
                if (heli.helicopterTeam == Team.None || heli.helicopterTeam == playerTeam)
                {
                    lookingAtHelicopter = heli;

                    float dist = Vector3.Distance(transform.position, heli.transform.position);

                    if (dist < vehicleEntryRadius)
                    {
                        // Close enough to enter
                        HelicopterSeat seat = heli.GetAvailableSeat(playerTeam);
                        if (seat != null)
                        {
                            helicopterPromptText = "[F] Enter Helicopter";
                        }
                        else
                        {
                            helicopterPromptText = "Helicopter Full";
                        }
                    }
                    else if (heli.HasAIPilot)
                    {
                        // Can call it
                        helicopterPromptText = "[F] Call Helicopter";
                    }
                    else
                    {
                        helicopterPromptText = "No Pilot";
                    }
                }
            }
        }

        // Animate prompt alpha
        float targetAlpha = string.IsNullOrEmpty(helicopterPromptText) ? 0f : 1f;
        helicopterPromptAlpha = Mathf.Lerp(helicopterPromptAlpha, targetAlpha, Time.deltaTime * 10f);
    }

    void CheckLookingAtJet()
    {
        lookingAtJet = null;
        jetPromptText = "";

        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 500f))
        {
            JetController jet = hit.collider.GetComponentInParent<JetController>();
            if (jet != null && !jet.isDestroyed)
            {
                // Check if friendly
                if (jet.JetTeam == Team.None || jet.JetTeam == playerTeam)
                {
                    lookingAtJet = jet;

                    float dist = Vector3.Distance(transform.position, jet.transform.position);

                    if (dist < vehicleEntryRadius)
                    {
                        // Close enough to enter
                        if (!jet.HasPilot)
                        {
                            jetPromptText = "[F] Enter Jet";
                        }
                        else
                        {
                            jetPromptText = "Jet Occupied";
                        }
                    }
                }
            }
        }

        // Animate prompt alpha
        float jetTargetAlpha = string.IsNullOrEmpty(jetPromptText) ? 0f : 1f;
        jetPromptAlpha = Mathf.Lerp(jetPromptAlpha, jetTargetAlpha, Time.deltaTime * 10f);
    }

    void CheckLookingAtTank()
    {
        lookingAtTank = null;
        tankPromptText = "";

        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f))
        {
            TankController tank = hit.collider.GetComponentInParent<TankController>();
            if (tank != null && !tank.isDestroyed)
            {
                // Check if friendly
                if (tank.TankTeam == Team.None || tank.TankTeam == playerTeam)
                {
                    lookingAtTank = tank;

                    float dist = Vector3.Distance(transform.position, tank.transform.position);

                    if (dist < vehicleEntryRadius)
                    {
                        // Close enough to enter
                        if (!tank.HasDriver)
                        {
                            tankPromptText = "[F] Enter Tank";
                        }
                        else
                        {
                            tankPromptText = "Tank Occupied";
                        }
                    }
                }
            }
        }

        // Animate prompt alpha
        float tankTargetAlpha = string.IsNullOrEmpty(tankPromptText) ? 0f : 1f;
        tankPromptAlpha = Mathf.Lerp(tankPromptAlpha, tankTargetAlpha, Time.deltaTime * 10f);
    }

    void DrawJetPrompt()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        GUIStyle promptStyle = new GUIStyle(GUI.skin.label);
        promptStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
        promptStyle.fontStyle = FontStyle.Bold;
        promptStyle.alignment = TextAnchor.MiddleCenter;

        float boxWidth = 200f;
        float boxHeight = 45f;
        float boxY = centerY + 80f;

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.6f * jetPromptAlpha);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), Texture2D.whiteTexture);

        // Border - orange for jet
        Color borderColor = new Color(1f, 0.6f, 0.2f, jetPromptAlpha);
        GUI.color = borderColor;
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY + boxHeight - 2, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, 2, boxHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX + boxWidth/2 - 2, boxY, 2, boxHeight), Texture2D.whiteTexture);

        // Text shadow
        GUI.color = new Color(0f, 0f, 0f, 0.8f * jetPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2 + 2, boxY + 2, boxWidth, boxHeight), jetPromptText, promptStyle);

        // Text
        GUI.color = new Color(1f, 1f, 1f, jetPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), jetPromptText, promptStyle);

        GUI.color = Color.white;
    }

    void DrawTankPrompt()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        GUIStyle promptStyle = new GUIStyle(GUI.skin.label);
        promptStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
        promptStyle.fontStyle = FontStyle.Bold;
        promptStyle.alignment = TextAnchor.MiddleCenter;

        float boxWidth = 200f;
        float boxHeight = 45f;
        float boxY = centerY + 80f;

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.6f * tankPromptAlpha);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), Texture2D.whiteTexture);

        // Border - dark green for tank
        Color borderColor = new Color(0.4f, 0.7f, 0.3f, tankPromptAlpha);
        GUI.color = borderColor;
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY + boxHeight - 2, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, 2, boxHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX + boxWidth/2 - 2, boxY, 2, boxHeight), Texture2D.whiteTexture);

        // Text shadow
        GUI.color = new Color(0f, 0f, 0f, 0.8f * tankPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2 + 2, boxY + 2, boxWidth, boxHeight), tankPromptText, promptStyle);

        // Text
        GUI.color = new Color(1f, 1f, 1f, tankPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), tankPromptText, promptStyle);

        GUI.color = Color.white;
    }

    void DrawHelicopterPrompt()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        // Style
        GUIStyle promptStyle = new GUIStyle(GUI.skin.label);
        promptStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
        promptStyle.fontStyle = FontStyle.Bold;
        promptStyle.alignment = TextAnchor.MiddleCenter;

        // Calculate size
        float boxWidth = 250f;
        float boxHeight = 45f;
        float boxY = centerY + 80f;

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.6f * helicopterPromptAlpha);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), Texture2D.whiteTexture);

        // Border
        bool isCallPrompt = helicopterPromptText.Contains("Call");
        Color borderColor = isCallPrompt ? new Color(0.3f, 0.8f, 1f, helicopterPromptAlpha) : new Color(0.3f, 1f, 0.5f, helicopterPromptAlpha);
        GUI.color = borderColor;
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY + boxHeight - 2, boxWidth, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - boxWidth/2, boxY, 2, boxHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX + boxWidth/2 - 2, boxY, 2, boxHeight), Texture2D.whiteTexture);

        // Text shadow
        GUI.color = new Color(0f, 0f, 0f, 0.8f * helicopterPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2 + 2, boxY + 2, boxWidth, boxHeight), helicopterPromptText, promptStyle);

        // Text
        GUI.color = new Color(1f, 1f, 1f, helicopterPromptAlpha);
        GUI.Label(new Rect(centerX - boxWidth/2, boxY, boxWidth, boxHeight), helicopterPromptText, promptStyle);

        GUI.color = Color.white;
    }

    public void OnEnterVehicleSeat(HelicopterSeat seat)
    {
        isInVehicle = true;
        currentVehicleSeat = seat;

        // Disable character controller
        if (controller != null)
        {
            controller.enabled = false;
        }

        // Hide player model
        SetPlayerModelVisible(false);

        // Disable collider temporarily
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null)
        {
            col.enabled = false;
        }
    }

    public void OnExitVehicleSeat()
    {
        isInVehicle = false;
        currentVehicleSeat = null;

        // Re-enable character controller
        if (controller != null)
        {
            controller.enabled = true;
        }

        // Show player model (for remote players)
        SetPlayerModelVisible(true);

        // Re-enable collider
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col != null)
        {
            col.enabled = true;
        }

        // Lock cursor again
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void SetPlayerModelVisible(bool visible)
    {
        // For local player, always hide body in first person
        if (photonView.IsMine && visible)
        {
            HideLocalPlayerBody();
            return;
        }

        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in skinnedRenderers)
        {
            renderer.enabled = visible;
        }
    }

    public bool IsInVehicle => isInVehicle;

    void UpdateRemotePlayer()
    {
        // Predict position based on velocity and lag
        Vector3 predictedPosition = networkPosition + networkVelocity * networkLag;

        // Calculate distance to predicted position
        float distance = Vector3.Distance(transform.position, predictedPosition);

        // Use faster lerp when further behind (catches up quicker)
        float lerpSpeed = Mathf.Clamp(distance * 5f, 10f, 30f);

        // Smoothly interpolate to predicted position
        transform.position = Vector3.Lerp(transform.position, predictedPosition, Time.deltaTime * lerpSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, Time.deltaTime * 15f);

        // Snap if too far behind (teleport threshold)
        if (distance > 5f)
        {
            transform.position = predictedPosition;
            transform.rotation = networkRotation;
        }

        // Update animator
        if (animator != null)
        {
            animator.SetFloat("MoveSpeed", networkMoveSpeed);
        }
    }

    void LateUpdate()
    {
        if (!photonView.IsMine || !isInitialized) return;
        if (isDead) return;

        // Don't control camera when command screen or spawn screen is active
        SquadCommandScreen cmdScreen = SquadCommandScreen.Instance;
        SpawnSelectScreen spawnScreen = SpawnSelectScreen.Instance;
        bool screenActive = (cmdScreen != null && cmdScreen.IsActive) || (spawnScreen != null && spawnScreen.IsActive);

        // Update player rotation (always do this, even in vehicle)
        if (!isInVehicle)
        {
            transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        // Update camera (only when no overlay screen is active and NOT in vehicle)
        // When in vehicle, HelicopterSeat controls the camera
        if (cameraTransform != null && !screenActive && !isInVehicle)
        {
            Vector3 targetPosition = transform.position + Vector3.up * cameraHeight;
            cameraTransform.position = targetPosition;
            cameraTransform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
        }

        // Position weapon AFTER camera is updated (prevents stutter)
        // Hide weapon when screen is active or in vehicle
        if (!screenActive && !isInVehicle)
        {
            PositionWeapon();
        }
        else if (weaponTransform != null)
        {
            // Move weapon off-screen when in command view or vehicle
            weaponTransform.position = Vector3.one * 9999f;
        }
    }

    void ReadInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null || mouse == null) return;

        float moveX = 0f;
        float moveZ = 0f;

        if (keyboard.wKey.isPressed) moveZ += 1f;
        if (keyboard.sKey.isPressed) moveZ -= 1f;
        if (keyboard.aKey.isPressed) moveX -= 1f;
        if (keyboard.dKey.isPressed) moveX += 1f;

        moveInput = new Vector2(moveX, moveZ);
        sprintPressed = keyboard.leftShiftKey.isPressed;
        jumpPressed = keyboard.spaceKey.wasPressedThisFrame;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        lookInput = mouse.delta.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void HandleMovement()
    {
        if (controller == null || !controller.enabled) return;

        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float moveX = moveInput.x;
        float moveZ = moveInput.y;

        currentSpeed = sprintPressed ? sprintSpeed : walkSpeed;

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        controller.Move(move * currentSpeed * Time.deltaTime);

        // Coyote time - allow jumping shortly after leaving ground
        if (controller.isGrounded)
        {
            coyoteCounter = coyoteTime;
            jumpCount = 0;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }

        // Jump buffer - remember jump input for a short time
        if (jumpPressed)
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }

        // Execute jump if buffered and can jump (ground jump or double jump)
        if (jumpBufferCounter > 0f && (coyoteCounter > 0f || jumpCount < maxJumps))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
            jumpCount++;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Update animator with proper speed syncing
        float inputMagnitude = new Vector2(moveX, moveZ).magnitude;

        if (animator != null)
        {
            // Normalized speed: 0 = idle, 0.5 = walk, 1.0 = run
            // This works with blend trees that use 0-1 range
            float normalizedSpeed = 0f;
            if (inputMagnitude > 0.1f)
            {
                normalizedSpeed = sprintPressed ? 1f : 0.5f;
                normalizedSpeed *= inputMagnitude; // Scale by input for analog support
            }

            // Smoothly transition the animation parameter
            float currentAnimSpeed = 0f;
            try { currentAnimSpeed = animator.GetFloat("MoveSpeed"); } catch { }
            float smoothedSpeed = Mathf.Lerp(currentAnimSpeed, normalizedSpeed, Time.deltaTime * 10f);

            // Set parameters (only if they exist)
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == "MoveSpeed" && param.type == AnimatorControllerParameterType.Float)
                    animator.SetFloat("MoveSpeed", smoothedSpeed);
                else if (param.name == "Speed" && param.type == AnimatorControllerParameterType.Float)
                    animator.SetFloat("Speed", smoothedSpeed);
                else if (param.name == "IsSprinting" && param.type == AnimatorControllerParameterType.Bool)
                    animator.SetBool("IsSprinting", sprintPressed && inputMagnitude > 0.1f);
            }

            // Adjust animation playback speed to match actual movement
            // This makes footsteps sync with ground movement
            if (inputMagnitude > 0.1f)
            {
                float baseAnimSpeed = sprintPressed ? 1.0f : 0.8f;
                animator.speed = baseAnimSpeed;
            }
            else
            {
                animator.speed = 1f;
            }
        }
    }

    void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = lookInput.x * mouseSensitivity * 0.02f;
        float mouseY = lookInput.y * mouseSensitivity * 0.02f;

        // Check for invert Y setting
        bool invertY = GameUIManager.InvertY;

        rotationY += mouseX;
        if (invertY)
            rotationX += mouseY;  // Inverted
        else
            rotationX -= mouseY;  // Normal
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);
    }

    void HandleShooting()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed && Time.time >= nextFireTime)
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    void Shoot()
    {
        if (cameraTransform == null) return;

        // Play gunshot sound locally
        if (gunshotSound != null && weaponAudio != null)
        {
            weaponAudio.pitch = Random.Range(0.95f, 1.05f);
            weaponAudio.PlayOneShot(gunshotSound, gunshotVolume);
        }

        // Start ray slightly in front of camera to avoid self-collision
        Vector3 rayOrigin = cameraTransform.position + cameraTransform.forward * 0.5f;

        // First, check if there's a wall/obstacle very close in front (prevents shooting through walls)
        RaycastHit closeHit;
        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out closeHit, 0.6f))
        {
            // Something is blocking us very close - check if it's not a player/AI
            FPSControllerPhoton closePlayer = closeHit.collider.GetComponentInParent<FPSControllerPhoton>();
            AIController closeAI = closeHit.collider.GetComponentInParent<AIController>();

            if (closePlayer == null && closeAI == null)
            {
                // It's a wall/obstacle - bullet hits the wall
                Vector3 blockedStart = muzzlePoint != null ? muzzlePoint.position : cameraTransform.position;
                SpawnTracer(blockedStart, closeHit.point);
                StartCoroutine(MuzzleFlashCoroutine());
                photonView.RPC("RPC_ShootEffects", RpcTarget.Others, blockedStart, closeHit.point);
                photonView.RPC("RPC_SpawnImpact", RpcTarget.All, closeHit.point, closeHit.normal);
                return;
            }
        }

        Ray ray = new Ray(rayOrigin, cameraTransform.forward);
        RaycastHit hit;
        Vector3 endPoint;

        if (Physics.Raycast(ray, out hit, range))
        {
            endPoint = hit.point;

            // Check if we hit another player
            FPSControllerPhoton hitPlayer = hit.collider.GetComponent<FPSControllerPhoton>();
            if (hitPlayer == null)
            {
                // Check parent for player component
                hitPlayer = hit.collider.GetComponentInParent<FPSControllerPhoton>();
            }

            if (hitPlayer != null && hitPlayer != this)
            {
                // Check for friendly fire
                if (hitPlayer.playerTeam == this.playerTeam)
                {
                    // Friendly fire - no damage
                }
                else
                {
                    // Apply damage via RPC - call on the hit player's photonView
                    hitPlayer.photonView.RPC("RPC_TakeDamage", RpcTarget.All, damage, photonView.ViewID);

                    // Play hit marker sound (kill sound will play when kill is confirmed)
                    PlayHitMarker();

                    // Set as current target for squad
                    currentTarget = hitPlayer.transform;
                }
            }

            // Check if we hit an AI bot
            AIController hitAI = hit.collider.GetComponent<AIController>();
            if (hitAI == null)
            {
                hitAI = hit.collider.GetComponentInParent<AIController>();
            }

            if (hitAI != null && hitAI.isAIControlled && hitAI.team != playerTeam)
            {
                // Check if this will be a kill
                bool willKill = hitAI.currentHealth - damage <= 0;

                hitAI.TakeDamage(damage, transform.position, hit.point, gameObject);

                // Play appropriate sound
                if (willKill)
                    PlayKillSound();
                else
                    PlayHitMarker();

                // Set as current target for squad
                currentTarget = hitAI.transform;
            }

            // Spawn impact effect if we didn't hit a player or AI
            if (hitPlayer == null && hitAI == null)
            {
                photonView.RPC("RPC_SpawnImpact", RpcTarget.All, hit.point, hit.normal);
            }
        }
        else
        {
            endPoint = cameraTransform.position + cameraTransform.forward * range;
        }

        Vector3 startPoint = muzzlePoint != null ? muzzlePoint.position : cameraTransform.position;

        // Show effects locally
        SpawnTracer(startPoint, endPoint);
        StartCoroutine(MuzzleFlashCoroutine());

        // Tell others to show effects
        photonView.RPC("RPC_ShootEffects", RpcTarget.Others, startPoint, endPoint);
    }

    [PunRPC]
    void RPC_ShootEffects(Vector3 start, Vector3 end)
    {
        // Play gunshot sound for other players
        if (gunshotSound != null && weaponAudio != null)
        {
            weaponAudio.pitch = Random.Range(0.95f, 1.05f);
            weaponAudio.PlayOneShot(gunshotSound, gunshotVolume);
        }

        SpawnTracer(start, end);
        StartCoroutine(MuzzleFlashCoroutine());
    }

    [PunRPC]
    void RPC_SpawnImpact(Vector3 position, Vector3 normal)
    {
        BulletImpact.SpawnImpact(position, normal);
    }

    [PunRPC]
    void RPC_TakeDamage(float damage, int attackerViewID)
    {
        if (isDead) return;

        currentHealth -= damage;
        lastAttackerViewID = attackerViewID;

        // Spawn blood hit effect
        SpawnBloodHit(transform.position + Vector3.up * 1.2f);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // Public method for external damage (e.g., from helicopter weapons)
    public void TakeDamage(float damage, int attackerViewID)
    {
        if (isDead) return;
        photonView.RPC("RPC_TakeDamage", RpcTarget.All, damage, attackerViewID);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        // Exit vehicle if in one
        if (isInVehicle && currentVehicleSeat != null)
        {
            currentVehicleSeat.ForceExit();
            isInVehicle = false;
            currentVehicleSeat = null;
        }

        // Spawn death blood effect
        SpawnBloodDeath(transform.position + Vector3.up * 0.5f);

        // Enable ragdoll physics
        RagdollHelper.EnableRagdoll(gameObject, -transform.forward, 6f);

        // Report kill to kill feed (only from the victim's client to avoid duplicates)
        if (photonView.IsMine)
        {
            // Add death to our stats
            KillFeedManager.AddDeath(PhotonNetwork.LocalPlayer);

            // Find the killer
            string killerName = "Unknown";
            Team killerTeam = Team.None;

            if (lastAttackerViewID == -1)
            {
                killerName = "AI";
                killerTeam = playerTeam == Team.Phantom ? Team.Havoc : Team.Phantom;
            }
            else
            {
                PhotonView attackerView = PhotonView.Find(lastAttackerViewID);
                if (attackerView != null)
                {
                    FPSControllerPhoton attacker = attackerView.GetComponent<FPSControllerPhoton>();
                    if (attacker != null)
                    {
                        killerName = attackerView.Owner.NickName;
                        killerTeam = attacker.playerTeam;

                        // Add kill to attacker's stats
                        KillFeedManager.AddKill(attackerView.Owner);
                    }
                }
            }

            // Broadcast kill to all clients
            photonView.RPC("RPC_BroadcastKill", RpcTarget.All,
                killerName, PhotonNetwork.LocalPlayer.NickName, (int)killerTeam, (int)playerTeam, false);

            // Unlock cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Respawn after delay
            StartCoroutine(RespawnCoroutine());
        }
    }

    void SpawnBloodHit(Vector3 position)
    {
        BloodEffectManager.SpawnBloodHit(position);
    }

    void SpawnBloodDeath(Vector3 position)
    {
        BloodEffectManager.SpawnBloodDeath(position);
    }

    void PlayHitMarker()
    {
        if (!photonView.IsMine) return;
        if (feedbackAudio != null && hitMarkerSound != null)
        {
            feedbackAudio.PlayOneShot(hitMarkerSound, hitMarkerVolume);
        }
    }

    void PlayKillSound()
    {
        if (!photonView.IsMine) return;
        if (feedbackAudio != null && killSound != null)
        {
            feedbackAudio.PlayOneShot(killSound, killSoundVolume);
        }
    }

    [PunRPC]
    void RPC_BroadcastKill(string killerName, string victimName, int killerTeam, int victimTeam, bool isHeadshot)
    {
        // Initialize kill feed manager if needed
        KillFeedManager.Initialize();

        // Add kill to local kill feed
        KillFeedManager.AddKillToFeed(killerName, victimName, killerTeam, victimTeam, isHeadshot);

        // Play kill sound for the killer (check if we're the killer)
        if (killerName == PhotonNetwork.LocalPlayer.NickName)
        {
            // Find our local player and play the sound
            FPSControllerPhoton localPlayer = FindLocalPlayer();
            if (localPlayer != null)
            {
                localPlayer.PlayKillSoundDirect();
            }
        }
    }

    // Called directly to play kill sound without photonView check
    public void PlayKillSoundDirect()
    {
        if (feedbackAudio != null && killSound != null)
        {
            feedbackAudio.PlayOneShot(killSound, killSoundVolume);
        }
    }

    static FPSControllerPhoton FindLocalPlayer()
    {
        FPSControllerPhoton[] players = FindObjectsByType<FPSControllerPhoton>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p.photonView.IsMine)
                return p;
        }
        return null;
    }

    System.Collections.IEnumerator RespawnCoroutine()
    {
        // Short delay before showing spawn screen
        yield return new WaitForSeconds(2f);

        // Clean up ragdoll
        CleanupRagdoll();

        // Show spawn selection screen
        SpawnSelectScreen spawnScreen = FindFirstObjectByType<SpawnSelectScreen>();
        if (spawnScreen == null)
        {
            // Create spawn screen if it doesn't exist
            GameObject spawnScreenObj = new GameObject("SpawnSelectScreen");
            spawnScreen = spawnScreenObj.AddComponent<SpawnSelectScreen>();
        }

        // Wait for player to select spawn
        bool hasSpawned = false;
        Vector3 selectedSpawnPos = Vector3.zero;
        Quaternion selectedSpawnRot = Quaternion.identity;

        spawnScreen.OnSpawnSelected = (pos, rot) =>
        {
            selectedSpawnPos = pos;
            selectedSpawnRot = rot;
            hasSpawned = true;
        };

        spawnScreen.Show(this, 5f); // 5 second respawn timer

        // Wait until player selects spawn
        while (!hasSpawned)
        {
            yield return null;
        }

        // Reset health
        currentHealth = maxHealth;
        isDead = false;

        // Teleport to selected spawn
        transform.position = selectedSpawnPos;
        transform.rotation = selectedSpawnRot;
        rotationY = selectedSpawnRot.eulerAngles.y;
        rotationX = 0f;

        // Re-enable CharacterController
        if (controller != null)
        {
            controller.enabled = true;
        }

        // Re-enable Animator
        if (animator != null)
        {
            animator.enabled = true;
            animator.SetBool("IsDead", false);
            animator.Play("Idle", 0, 0f);
        }

        // Lock cursor again
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void CleanupRagdoll()
    {
        // Remove all CharacterJoints added by ragdoll
        CharacterJoint[] joints = GetComponentsInChildren<CharacterJoint>();
        foreach (var joint in joints)
        {
            Destroy(joint);
        }

        // Remove all Rigidbodies added to bones (except any that were there before)
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            // Only destroy rigidbodies on bone transforms (not the root)
            if (rb.gameObject != gameObject)
            {
                Destroy(rb);
            }
        }

        // Remove bone colliders (CapsuleColliders on child objects)
        CapsuleCollider[] capsules = GetComponentsInChildren<CapsuleCollider>();
        foreach (var col in capsules)
        {
            // Don't remove the hit detection collider on root
            if (col.gameObject != gameObject)
            {
                Destroy(col);
            }
        }
    }

    // Public method for AI to damage player
    public void TakeDamageFromAI(float damage, Vector3 damageSource)
    {
        if (photonView.IsMine)
        {
            // Local player takes damage directly
            RPC_TakeDamage(damage, -1); // -1 indicates AI attacker
        }
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("BulletTracer");
        BulletTracer tracer = tracerObj.AddComponent<BulletTracer>();
        tracer.SetPositions(start, end);
    }

    System.Collections.IEnumerator MuzzleFlashCoroutine()
    {
        if (muzzleFlashPrefab != null && muzzlePoint != null)
        {
            // Use Synty prefab (FX_Gunshot_01)
            GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation);
            flash.transform.SetParent(muzzlePoint);
            Destroy(flash, 0.15f);
            yield break;
        }

        // Fallback: Create procedural muzzle flash light
        if (muzzleFlash == null && muzzlePoint != null)
        {
            GameObject flashObj = new GameObject("MuzzleFlash");
            flashObj.transform.SetParent(muzzlePoint);
            flashObj.transform.localPosition = Vector3.zero;
            muzzleFlash = flashObj.AddComponent<Light>();
            muzzleFlash.type = LightType.Point;
            muzzleFlash.color = Color.yellow;
            muzzleFlash.intensity = 3f;
            muzzleFlash.range = 5f;
            muzzleFlash.enabled = false;
        }

        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = true;
            yield return new WaitForSeconds(0.05f);
            muzzleFlash.enabled = false;
        }
    }

    void HandleADS()
    {
        if (playerCamera == null) return;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            isAiming = mouse.rightButton.isPressed;
        }

        float targetFOV = isAiming ? aimFOV : normalFOV;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * aimSpeed);
    }

    void PositionWeapon()
    {
        if (weaponTransform == null) return;

        Vector3 cameraPos = transform.position + Vector3.up * cameraHeight;
        Quaternion cameraRot = Quaternion.Euler(rotationX, rotationY, 0f);

        Vector3 targetOffset = isAiming ? aimPosition : weaponOffset;
        currentWeaponOffset = Vector3.Lerp(currentWeaponOffset, targetOffset, Time.deltaTime * aimSpeed);

        Vector3 bobOffset = Vector3.zero;
        bool isMoving = controller != null && controller.isGrounded && moveInput.magnitude > 0.1f;

        float targetIntensity = (isMoving && !isAiming) ? 1f : 0f;
        bobIntensity = Mathf.Lerp(bobIntensity, targetIntensity, Time.deltaTime * 8f);

        float speedMultiplier = sprintPressed ? 1.5f : 1f;
        bobTimer += Time.deltaTime * bobFrequency * speedMultiplier;

        if (bobTimer > Mathf.PI * 2f) bobTimer -= Mathf.PI * 2f;

        bobOffset.x = Mathf.Sin(bobTimer) * bobHorizontalAmount * bobIntensity;
        bobOffset.y = Mathf.Sin(bobTimer * 2f) * bobVerticalAmount * bobIntensity;

        Vector3 finalOffset = currentWeaponOffset + bobOffset;
        weaponTransform.position = cameraPos + cameraRot * finalOffset;
        weaponTransform.rotation = cameraRot;
    }

    void CleanupMainMenuUI()
    {
        // Find and destroy any leftover MainMenuUI canvases from the main menu scene
        // This can happen if the MainMenuUI is on a DontDestroyOnLoad object
        MainMenuUI[] menuUIs = FindObjectsByType<MainMenuUI>(FindObjectsSortMode.None);
        foreach (var menuUI in menuUIs)
        {
            Debug.Log($"[FPSControllerPhoton] Destroying leftover MainMenuUI: {menuUI.gameObject.name}");
            Destroy(menuUI.gameObject);
        }

        // Also look for any Canvas with "MainMenu" or "Loading" in the name
        Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var canvas in allCanvases)
        {
            string name = canvas.gameObject.name.ToLower();
            if (name.Contains("mainmenu") || name.Contains("menu_canvas") || name.Contains("loading"))
            {
                Debug.Log($"[FPSControllerPhoton] Destroying leftover canvas: {canvas.gameObject.name}");
                Destroy(canvas.gameObject);
            }
        }
    }

    // IPunObservable - syncs data across network
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Local player sends data
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(currentSpeed * moveInput.magnitude);
        }
        else
        {
            // Remote player receives data
            Vector3 newPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkMoveSpeed = (float)stream.ReceiveNext();

            // Calculate velocity from position change
            float timeSinceLast = Time.time - lastNetworkTime;
            if (timeSinceLast > 0.001f)
            {
                networkVelocity = (newPosition - lastNetworkPosition) / timeSinceLast;
            }

            // Calculate network lag (time since packet was sent)
            networkLag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));

            // Store for next update
            lastNetworkPosition = newPosition;
            networkPosition = newPosition;
            lastNetworkTime = Time.time;
        }
    }

    void OnDestroy()
    {
        // Clear static reference if this was the local player
        if (localPlayerInstance == this)
        {
            localPlayerInstance = null;
        }

        // Cleanup camera
        if (photonView != null && photonView.IsMine && cameraTransform != null)
        {
            Destroy(cameraTransform.gameObject);
        }

        // Cleanup muzzle flash
        if (muzzleFlash != null)
        {
            Destroy(muzzleFlash.gameObject);
        }
    }

    void InitUIStyles()
    {
        if (uiStylesInitialized) return;

        nameTagStyle = new GUIStyle();
        nameTagStyle.fontSize = 14;
        nameTagStyle.fontStyle = FontStyle.Bold;
        nameTagStyle.alignment = TextAnchor.MiddleCenter;
        nameTagStyle.normal.textColor = Color.white;

        uiStylesInitialized = true;
    }

    void OnGUI()
    {
        // Only draw for local player during gameplay
        if (!photonView.IsMine || isDead) return;
        if (playerCamera == null) return;

        // Don't draw when command/spawn screen is active
        SquadCommandScreen cmdScreen = SquadCommandScreen.Instance;
        SpawnSelectScreen spawnScreen = SpawnSelectScreen.Instance;
        if ((cmdScreen != null && cmdScreen.IsActive) || (spawnScreen != null && spawnScreen.IsActive))
            return;

        InitUIStyles();

        // Draw crosshair when aiming
        if (isAiming && !isInVehicle)
        {
            DrawADSCrosshair();
        }

        // Check what squad member we're looking at
        CheckLookingAtSquadMember();

        // Draw markers for squad members (blue dots)
        foreach (var ai in squadMembers)
        {
            if (ai == null || ai.isDead) continue;
            DrawTeammateMarker(ai.transform, true, ai == lookedAtSquadMember, ai.identity);
        }

        // Draw markers for other teammates (green dots)
        DrawOtherTeammateMarkers();

        // Draw helicopter interaction prompt
        if (!isInVehicle && helicopterPromptAlpha > 0.01f && !string.IsNullOrEmpty(helicopterPromptText))
        {
            DrawHelicopterPrompt();
        }

        // Draw jet prompt
        if (!isInVehicle && jetPromptAlpha > 0.01f && !string.IsNullOrEmpty(jetPromptText))
        {
            DrawJetPrompt();
        }

        // Draw tank prompt
        if (!isInVehicle && tankPromptAlpha > 0.01f && !string.IsNullOrEmpty(tankPromptText))
        {
            DrawTankPrompt();
        }
    }

    void CheckLookingAtSquadMember()
    {
        lookedAtSquadMember = null;

        if (cameraTransform == null) return;

        // Raycast from camera forward
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 50f))
        {
            AIController hitAI = hit.collider.GetComponentInParent<AIController>();
            if (hitAI != null && squadMembers.Contains(hitAI))
            {
                lookedAtSquadMember = hitAI;
            }
        }

        // Also check if close to center of screen for any squad member
        foreach (var ai in squadMembers)
        {
            if (ai == null || ai.isDead) continue;

            Vector3 screenPos = playerCamera.WorldToScreenPoint(ai.transform.position + Vector3.up * 2f);
            if (screenPos.z > 0)
            {
                float distFromCenter = Vector2.Distance(
                    new Vector2(screenPos.x, screenPos.y),
                    new Vector2(Screen.width / 2f, Screen.height / 2f)
                );

                if (distFromCenter < 100f)
                {
                    float worldDist = Vector3.Distance(transform.position, ai.transform.position);
                    if (worldDist < 30f)
                    {
                        lookedAtSquadMember = ai;
                        break;
                    }
                }
            }
        }
    }

    void DrawTeammateMarker(Transform target, bool isSquadMember, bool showName, SoldierIdentity identity)
    {
        Vector3 headPos = target.position + Vector3.up * 2.2f;
        Vector3 screenPos = playerCamera.WorldToScreenPoint(headPos);

        // Check if on screen and in front
        if (screenPos.z < 0) return;

        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > 100f) return;

        // Flip Y for GUI
        screenPos.y = Screen.height - screenPos.y;

        // Dot size based on distance
        float dotSize = Mathf.Lerp(12f, 4f, distance / 100f);

        // Draw dot
        Color dotColor = isSquadMember ? new Color(0.3f, 0.6f, 1f) : new Color(0.3f, 1f, 0.3f);
        GUI.color = dotColor;

        Rect dotRect = new Rect(screenPos.x - dotSize / 2f, screenPos.y - dotSize / 2f, dotSize, dotSize);
        GUI.DrawTexture(dotRect, Texture2D.whiteTexture);

        // Draw name if looking at squad member
        if (showName && identity != null && distance < 30f)
        {
            GUI.color = Color.white;

            // Background
            string nameText = identity.RankAndName;
            float nameWidth = nameText.Length * 8f + 20f;
            Rect bgRect = new Rect(screenPos.x - nameWidth / 2f, screenPos.y - dotSize - 25f, nameWidth, 22f);
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

            // Name text
            GUI.color = new Color(0.3f, 0.7f, 1f);
            GUI.Label(bgRect, nameText, nameTagStyle);

            // Show order status below
            if (lookedAtSquadMember != null)
            {
                string orderText = GetOrderStatusText(lookedAtSquadMember);
                Rect orderRect = new Rect(screenPos.x - 60f, screenPos.y + dotSize + 5f, 120f, 18f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                GUIStyle orderStyle = new GUIStyle(nameTagStyle) { fontSize = 11 };
                GUI.Label(orderRect, orderText, orderStyle);
            }
        }

        GUI.color = Color.white;
    }

    string GetOrderStatusText(AIController ai)
    {
        switch (ai.currentOrder)
        {
            case AIController.OrderType.FollowLeader: return "Following";
            case AIController.OrderType.DefendPoint: return $"Defending";
            case AIController.OrderType.CapturePoint: return $"Capturing";
            case AIController.OrderType.HoldPosition: return "Holding";
            default: return "";
        }
    }

    void DrawOtherTeammateMarkers()
    {
        // Draw markers for friendly AI not in squad
        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        foreach (var ai in allAI)
        {
            if (ai == null || ai.isDead) continue;
            if (ai.team != playerTeam) continue;
            if (squadMembers.Contains(ai)) continue; // Skip squad members (already drawn)

            DrawTeammateMarker(ai.transform, false, false, null);
        }

        // Draw markers for friendly players
        FPSControllerPhoton[] allPlayers = FindObjectsByType<FPSControllerPhoton>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player == null || player == this || player.isDead) continue;
            if (player.playerTeam != playerTeam) continue;

            DrawTeammateMarker(player.transform, false, false, null);
        }
    }

    // Static texture for crosshair to prevent memory leak
    private static Texture2D crosshairTexture;

    void DrawADSCrosshair()
    {
        // Create texture once
        if (crosshairTexture == null)
        {
            crosshairTexture = new Texture2D(1, 1);
            crosshairTexture.SetPixel(0, 0, Color.white);
            crosshairTexture.Apply();
        }

        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        // Crosshair settings
        float lineLength = 8f;
        float lineThickness = 2f;
        float gap = 3f;
        Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);

        GUI.color = crosshairColor;

        // Top line
        GUI.DrawTexture(new Rect(
            centerX - lineThickness / 2f,
            centerY - gap - lineLength,
            lineThickness,
            lineLength
        ), crosshairTexture);

        // Bottom line
        GUI.DrawTexture(new Rect(
            centerX - lineThickness / 2f,
            centerY + gap,
            lineThickness,
            lineLength
        ), crosshairTexture);

        // Left line
        GUI.DrawTexture(new Rect(
            centerX - gap - lineLength,
            centerY - lineThickness / 2f,
            lineLength,
            lineThickness
        ), crosshairTexture);

        // Right line
        GUI.DrawTexture(new Rect(
            centerX + gap,
            centerY - lineThickness / 2f,
            lineLength,
            lineThickness
        ), crosshairTexture);

        // Center dot
        float dotSize = 2f;
        GUI.DrawTexture(new Rect(
            centerX - dotSize / 2f,
            centerY - dotSize / 2f,
            dotSize,
            dotSize
        ), crosshairTexture);

        GUI.color = Color.white;
    }
}
