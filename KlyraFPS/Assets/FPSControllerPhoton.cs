using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

public class FPSControllerPhoton : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 10f;
    public float jumpHeight = 2f;
    public float gravity = -9.81f;

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

    [Header("Weapon Audio")]
    public AudioClip gunshotSound;
    public float gunshotVolume = 0.7f;
    private AudioSource weaponAudio;

    [Header("ADS Settings")]
    public float aimFOV = 30f;
    public float normalFOV = 60f;
    public float aimSpeed = 10f;
    public Vector3 aimPosition = new Vector3(0f, -0.21f, 0.25f);

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead = false;

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

    private bool isInitialized = false;

    // Static reference to ensure only one local player
    private static FPSControllerPhoton localPlayerInstance;

    // Team system
    public Team playerTeam = Team.None;

    [Header("Team Skins")]
    public string[] phantomSkinNames = { "SM_Chr_Soldier_Male_01", "SM_Chr_Soldier_Male_02", "SM_Chr_Soldier_Female_01" };
    public string[] havocSkinNames = { "SM_Chr_Insurgent_Male_01", "SM_Chr_Insurgent_Male_04", "SM_Chr_Insurgent_Female_01" };

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        currentHealth = maxHealth;

        // Setup weapon audio
        weaponAudio = gameObject.AddComponent<AudioSource>();
        weaponAudio.spatialBlend = 1f; // 3D sound
        weaponAudio.maxDistance = 50f;
        weaponAudio.rolloffMode = AudioRolloffMode.Linear;
        weaponAudio.playOnAwake = false;

        // Disable any cameras that came with the prefab FIRST
        // This prevents multiple cameras from being active
        Camera[] prefabCameras = GetComponentsInChildren<Camera>();
        foreach (Camera cam in prefabCameras)
        {
            Debug.Log($"Disabling prefab camera: {cam.name} on player {photonView.ViewID}");
            cam.enabled = false;
            // Also disable AudioListener if present
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        // Get team from instantiation data
        if (photonView.InstantiationData != null && photonView.InstantiationData.Length > 0)
        {
            playerTeam = (Team)(int)photonView.InstantiationData[0];
            Debug.Log($"Player {photonView.ViewID} assigned to team: {playerTeam}");
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

        // Pick a random skin from the team's options
        if (skinNames.Length > 0)
        {
            string chosenSkin = skinNames[Random.Range(0, skinNames.Length)];

            // Find and enable the chosen skin
            foreach (Transform child in allChildren)
            {
                if (child.name == chosenSkin)
                {
                    child.gameObject.SetActive(true);
                    Debug.Log($"Enabled skin: {chosenSkin} for team {playerTeam}");

                    // Get animator from the skin if it exists
                    Animator skinAnimator = child.GetComponent<Animator>();
                    if (skinAnimator != null)
                    {
                        animator = skinAnimator;
                    }
                    break;
                }
            }
        }
    }

    void InitializeLocalPlayer()
    {
        Debug.Log($"Initializing LOCAL player (ViewID: {photonView.ViewID})");

        // Ensure only one local player exists
        if (localPlayerInstance != null && localPlayerInstance != this)
        {
            Debug.LogWarning($"Another local player already exists! This player (ViewID: {photonView.ViewID}) will be treated as remote.");
            InitializeRemotePlayer();
            return;
        }
        localPlayerInstance = this;

        // Destroy any existing player cameras first
        Camera[] allCameras = FindObjectsOfType<Camera>();
        foreach (Camera cam in allCameras)
        {
            if (cam.gameObject.name.StartsWith("PlayerCamera_"))
            {
                Debug.Log($"Destroying old player camera: {cam.gameObject.name}");
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
            Debug.LogWarning("No main camera found, creating camera from scratch");
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
        AudioListener[] listeners = FindObjectsOfType<AudioListener>();
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
        isInitialized = true;

        Debug.Log($"Local player initialized. Camera: {playerCamera?.name}, IsMine: {photonView.IsMine}");
    }

    void InitializeRemotePlayer()
    {
        Debug.Log($"Initializing REMOTE player (ViewID: {photonView.ViewID})");

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

        ReadInput();
        HandleMovement();
        HandleMouseLook();
        HandleShooting();
        HandleADS();
        // Note: PositionWeapon moved to LateUpdate to sync with camera
    }

    void UpdateRemotePlayer()
    {
        // Smoothly interpolate to network position
        transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
        transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);

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

        // Update player rotation
        transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

        // Update camera
        if (cameraTransform != null)
        {
            Vector3 targetPosition = transform.position + Vector3.up * cameraHeight;
            cameraTransform.position = targetPosition;
            cameraTransform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
        }

        // Position weapon AFTER camera is updated (prevents stutter)
        PositionWeapon();
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

        if (jumpPressed && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
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

        rotationY += mouseX;
        rotationX -= mouseY;
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
                Debug.Log($"Shot blocked by nearby obstacle: {closeHit.collider.gameObject.name}");
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

            // Debug: log what we hit
            Debug.Log($"Raycast hit: {hit.collider.gameObject.name} (layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)})");

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
                    Debug.Log($"Friendly fire prevented - both players on team {playerTeam}");
                }
                else
                {
                    Debug.Log($"Hit enemy player {hitPlayer.photonView.ViewID} (Team: {hitPlayer.playerTeam})!");
                    // Apply damage via RPC - call on the hit player's photonView
                    hitPlayer.photonView.RPC("RPC_TakeDamage", RpcTarget.All, damage, photonView.ViewID);
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
                Debug.Log($"Hit enemy AI {hitAI.gameObject.name} (Team: {hitAI.team})!");
                hitAI.TakeDamage(damage, transform.position);
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
        Debug.Log($"Player {photonView.ViewID} took {damage} damage from {attackerViewID}. Health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log($"Player {photonView.ViewID} died!");

        // Enable ragdoll physics
        RagdollHelper.EnableRagdoll(gameObject, -transform.forward, 6f);

        // Disable controls for local player
        if (photonView.IsMine)
        {
            // Unlock cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Respawn after delay
            StartCoroutine(RespawnCoroutine());
        }
    }

    System.Collections.IEnumerator RespawnCoroutine()
    {
        yield return new WaitForSeconds(5f);

        // Reset health
        currentHealth = maxHealth;
        isDead = false;

        // Clean up ragdoll components
        CleanupRagdoll();

        // Find spawn point based on team - check both UI managers
        Vector3 spawnPos = transform.position;
        Quaternion spawnRot = transform.rotation;
        Transform spawnPointTransform = null;

        // Try new GameUIManager first
        GameUIManager gameUI = FindObjectOfType<GameUIManager>();
        if (gameUI != null)
        {
            if (playerTeam == Team.Phantom && gameUI.phantomSpawnPoint != null)
                spawnPointTransform = gameUI.phantomSpawnPoint;
            else if (playerTeam == Team.Havoc && gameUI.havocSpawnPoint != null)
                spawnPointTransform = gameUI.havocSpawnPoint;
        }

        // Fallback to old PhotonNetworkManager
        if (spawnPointTransform == null)
        {
            PhotonNetworkManager networkManager = FindObjectOfType<PhotonNetworkManager>();
            if (networkManager != null)
            {
                if (playerTeam == Team.Phantom && networkManager.phantomSpawnPoint != null)
                    spawnPointTransform = networkManager.phantomSpawnPoint;
                else if (playerTeam == Team.Havoc && networkManager.havocSpawnPoint != null)
                    spawnPointTransform = networkManager.havocSpawnPoint;
                else if (networkManager.spawnPoint != null)
                    spawnPointTransform = networkManager.spawnPoint;
            }
        }

        if (spawnPointTransform != null)
        {
            spawnPos = spawnPointTransform.position + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
            spawnRot = spawnPointTransform.rotation;
            // Rotate Phantom 180 degrees
            if (playerTeam == Team.Phantom)
            {
                spawnRot *= Quaternion.Euler(0, 180f, 0);
            }
        }

        // Teleport to spawn
        transform.position = spawnPos;
        transform.rotation = spawnRot;
        rotationY = spawnRot.eulerAngles.y;
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
            animator.Play("Idle", 0, 0f); // Reset to idle animation
        }

        // Lock cursor again
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log($"Player {photonView.ViewID} respawned at {spawnPos}");
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
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkMoveSpeed = (float)stream.ReceiveNext();
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
}
