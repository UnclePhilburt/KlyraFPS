using UnityEngine;
using Mirror;
using UnityEngine.InputSystem;

// NOTE: NetworkTransformReliable component handles position/rotation sync
// Ensure syncDirection is ClientToServer, coordinateSpace is World

public class FPSController : NetworkBehaviour
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

    [Header("ADS Settings")]
    public float aimFOV = 30f;
    public float normalFOV = 60f;
    public float aimSpeed = 10f;
    public Vector3 aimPosition = new Vector3(0f, -0.21f, 0.25f);
    public Vector3 normalPosition = Vector3.zero;

    // Components
    private CharacterController controller;
    private Animator animator;

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

    // Static flag to track scene camera ownership across all player instances
    private static bool sceneMainCameraClaimed = false;

    // Shooting
    private float nextFireTime = 0f;
    private Light muzzleFlash;
    private bool isAiming = false;
    private Camera playerCamera;

    // SyncVars for animation (position/rotation handled by NetworkTransformReliable)
    [SyncVar] private float syncedMoveSpeed;
    [SyncVar] private bool syncedIsGrounded = true;
    [SyncVar] private bool syncedIsJumping;
    [SyncVar] private bool syncedMovementInputHeld;
    [SyncVar] private int syncedCurrentGait;
    [SyncVar] private float syncedStrafeX;
    [SyncVar] private float syncedStrafeZ;

    private bool isInitialized = false;

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[SERVER] Player spawned - netId: {netId}, connId: {connectionToClient?.connectionId}");

        // Grant client authority so NetworkTransform can sync from client to server
        if (connectionToClient != null)
        {
            netIdentity.AssignClientAuthority(connectionToClient);
            Debug.Log($"[SERVER] Granted client authority to connection {connectionToClient.connectionId}");
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        Debug.Log($">>> OnStartLocalPlayer called for netId: {netId}");

        controller = GetComponent<CharacterController>();

        rotationY = transform.eulerAngles.y;
        rotationX = 0f;

        StartCoroutine(InitializePlayerModel());

        // CRITICAL FIX: Use static flag to track camera ownership across all player instances
        if (cameraTransform == null)
        {
            Camera mainCam = Camera.main;

            // Check static flag AND tag to prevent race conditions
            if (!sceneMainCameraClaimed && mainCam != null && mainCam.CompareTag("MainCamera"))
            {
                // First player claims the scene camera - set flag FIRST to prevent race
                sceneMainCameraClaimed = true;

                cameraTransform = mainCam.transform;
                cameraTransform.SetParent(null);
                DontDestroyOnLoad(cameraTransform.gameObject);

                // Remove MainCamera tag as backup
                mainCam.tag = "Untagged";

                playerCamera = mainCam;
                Debug.Log($"Player {netId} claimed existing scene Camera.main (static flag set)");
            }
            else
            {
                // Camera already claimed or unavailable - create new camera for this player
                Debug.Log($"Player {netId}: Creating new camera (sceneMainCameraClaimed={sceneMainCameraClaimed}, Camera.main={(mainCam != null ? "exists" : "null")})");
                GameObject newCameraObj = new GameObject($"PlayerCamera_{netId}");
                playerCamera = newCameraObj.AddComponent<Camera>();
                cameraTransform = newCameraObj.transform;
                cameraTransform.SetParent(null);
                DontDestroyOnLoad(newCameraObj);

                // Copy settings from main camera if it exists
                if (mainCam != null)
                {
                    playerCamera.clearFlags = mainCam.clearFlags;
                    playerCamera.backgroundColor = mainCam.backgroundColor;
                    playerCamera.cullingMask = mainCam.cullingMask;
                    playerCamera.orthographic = mainCam.orthographic;
                    playerCamera.nearClipPlane = mainCam.nearClipPlane;
                    playerCamera.farClipPlane = mainCam.farClipPlane;
                }
                else
                {
                    // Set reasonable defaults
                    playerCamera.clearFlags = CameraClearFlags.Skybox;
                    playerCamera.nearClipPlane = 0.3f;
                    playerCamera.farClipPlane = 1000f;
                }

                playerCamera.tag = "Untagged";
            }
        }

        if (playerCamera == null && cameraTransform != null)
        {
            playerCamera = cameraTransform.GetComponent<Camera>();
        }

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = normalFOV;
        }

        currentWeaponOffset = weaponOffset;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        isInitialized = true;
        Debug.Log($">>> Local player {netId} fully initialized, cursor locked: {Cursor.lockState}");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        Debug.Log($"OnStartClient - isOwned: {isOwned}, isServer: {isServer}, netId: {netId}, pos: {transform.position}");

        // Check NetworkTransform configuration
        var netTransform = GetComponent<NetworkTransformReliable>();
        if (netTransform != null)
        {
            Debug.Log($"[Player {netId}] NetworkTransform - enabled: {netTransform.enabled}, syncDirection: {netTransform.syncDirection}, coordinateSpace: {netTransform.coordinateSpace}, isOwned: {isOwned}");
        }
        else
        {
            Debug.LogError($"[Player {netId}] NetworkTransformReliable NOT found!");
        }

        // CRITICAL: Remote players must NOT have camera references
        // Check isLocalPlayer - should be false for remote players, true for local
        Debug.Log($"[Player {netId}] OnStartClient - isLocalPlayer: {isLocalPlayer}, isOwned: {isOwned}");

        if (!isLocalPlayer)
        {
            cameraTransform = null;
            playerCamera = null;
            weaponTransform = null;  // Remote players don't need first-person weapon
            isInitialized = true;
            Debug.Log($">>> REMOTE PLAYER SPAWNED - netId: {netId}, camera cleared");
        }
        else
        {
            Debug.Log($">>> LOCAL PLAYER in OnStartClient - netId: {netId}, NOT clearing camera");
        }

        StartCoroutine(InitializePlayerModel());
    }

    System.Collections.IEnumerator InitializePlayerModel()
    {
        yield return new WaitForEndOfFrame();

        animator = GetComponentInChildren<Animator>();

        // Only hide body mesh for the LOCAL player (first-person view)
        if (isLocalPlayer)
        {
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
    }

    void Update()
    {
        // Wait for network initialization
        if (!isLocalPlayer && !isInitialized)
        {
            return;
        }

        if (!isLocalPlayer)
        {
            // Disable CharacterController for remote players - NetworkTransformReliable handles position
            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
            }

            // CRITICAL: Clear controller reference so disabled CharacterController state
            // doesn't interfere with NetworkTransformReliable position updates
            controller = null;

            // Log remote player position occasionally for debugging
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"Remote player {netId} at {transform.position}");
            }

            // NetworkTransformReliable handles position/rotation sync automatically
            UpdateRemoteAnimator();
            return;
        }

        // Wait until fully initialized
        if (!isInitialized) return;

        // Enable controller for local player
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }
        if (controller != null && !controller.enabled)
        {
            controller.enabled = true;
        }
        if (controller == null || !controller.enabled) return;

        ReadInput();
        HandleMovement();
        HandleMouseLook();
        HandleShooting();
        HandleADS();
        PositionWeapon();

        // NetworkTransform component handles position/rotation sync automatically
    }

    void PositionWeapon()
    {
        Vector3 cameraPos = transform.position + Vector3.up * cameraHeight;
        Quaternion cameraRot = Quaternion.Euler(rotationX, rotationY, 0f);

        if (weaponTransform != null)
        {
            Vector3 targetOffset = isAiming ? aimPosition : weaponOffset;
            currentWeaponOffset = Vector3.Lerp(currentWeaponOffset, targetOffset, Time.deltaTime * aimSpeed);

            Vector3 bobOffset = Vector3.zero;
            bool isMoving = controller != null && controller.isGrounded && moveInput.magnitude > 0.1f;

            float targetIntensity = (isMoving && !isAiming) ? 1f : 0f;
            bobIntensity = Mathf.Lerp(bobIntensity, targetIntensity, Time.deltaTime * 8f);

            float speedMultiplier = sprintPressed ? 1.5f : 1f;
            bobTimer += Time.deltaTime * bobFrequency * speedMultiplier;

            bobOffset.x = Mathf.Sin(bobTimer) * bobHorizontalAmount * bobIntensity;
            bobOffset.y = Mathf.Sin(bobTimer * 2f) * bobVerticalAmount * bobIntensity;

            Vector3 finalOffset = currentWeaponOffset + bobOffset;
            weaponTransform.position = cameraPos + cameraRot * finalOffset;
            weaponTransform.rotation = cameraRot;
        }
    }

    void LateUpdate()
    {
        // Debug: log which players are trying to control camera
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[LateUpdate] Player {netId} - isLocalPlayer: {isLocalPlayer}, cameraTransform: {(cameraTransform != null ? "SET" : "NULL")}");
        }

        // Use isLocalPlayer instead of isOwned to ensure only THIS client's player controls the camera
        if (isLocalPlayer)
        {
            transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

            if (cameraTransform != null)
            {
                Vector3 targetPosition = transform.position + Vector3.up * cameraHeight;
                cameraTransform.position = targetPosition;
                cameraTransform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
            }

            // Debug log every 5 seconds
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"LOCAL player {netId} - pos: {transform.position}, rotY: {rotationY}");
            }
        }
    }

    void ReadInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null) return;

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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (mouse != null)
        {
            lookInput = mouse.delta.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
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

        float inputMagnitude = new Vector2(moveX, moveZ).magnitude;
        float animatorSpeed = inputMagnitude * currentSpeed;
        bool isMoving = inputMagnitude > 0.1f;

        if (jumpPressed && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            syncedIsJumping = true;
        }
        else if (controller.isGrounded)
        {
            syncedIsJumping = false;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Update synced values for animation
        syncedMoveSpeed = animatorSpeed;
        syncedIsGrounded = controller.isGrounded;
        syncedMovementInputHeld = isMoving;
        syncedStrafeX = moveX;
        syncedStrafeZ = moveZ;

        // Position/rotation sync handled by NetworkTransformReliable component

        if (!isMoving)
            syncedCurrentGait = 0;
        else if (sprintPressed)
            syncedCurrentGait = 2;
        else
            syncedCurrentGait = 1;

        if (animator != null)
        {
            UpdateAnimatorParameters(animatorSpeed, controller.isGrounded, syncedIsJumping,
                                    isMoving, syncedCurrentGait, moveX, moveZ);
        }
    }

    void UpdateAnimatorParameters(float moveSpeed, bool isGrounded, bool isJumping,
                                   bool movementHeld, int currentGait, float strafeX, float strafeZ)
    {
        if (animator == null) return;
        animator.SetFloat("MoveSpeed", moveSpeed);
    }

    void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = lookInput.x * mouseSensitivity * 0.02f;
        float mouseY = lookInput.y * mouseSensitivity * 0.02f;

        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);

        // REMOVED: transform.rotation assignment - now ONLY set in LateUpdate()
        // This prevents double-assignment that caused rotation desync between
        // what the local player sees and what NetworkTransform captures
    }

    void UpdateRemoteAnimator()
    {
        if (animator != null)
        {
            UpdateAnimatorParameters(syncedMoveSpeed, syncedIsGrounded,
                                    syncedIsJumping, syncedMovementInputHeld,
                                    syncedCurrentGait, syncedStrafeX, syncedStrafeZ);
        }
    }

    void HandleShooting()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    void HandleADS()
    {
        if (playerCamera == null) return;  // Safety guard

        var mouse = Mouse.current;
        if (mouse != null)
        {
            isAiming = mouse.rightButton.isPressed;
        }

        float targetFOV = isAiming ? aimFOV : normalFOV;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * aimSpeed);
    }

    void Shoot()
    {
        if (cameraTransform == null) return;  // Safety guard

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;

        Vector3 endPoint;

        if (Physics.Raycast(ray, out hit, range))
        {
            endPoint = hit.point;

            FPSController hitPlayer = hit.collider.GetComponent<FPSController>();
            if (hitPlayer != null && hitPlayer != this)
            {
                Debug.Log($"Hit player at {hit.point}!");
            }
            else
            {
                CmdSpawnImpact(hit.point, hit.normal);
            }
        }
        else
        {
            endPoint = cameraTransform.position + cameraTransform.forward * range;
        }

        Vector3 startPoint = muzzlePoint != null ? muzzlePoint.position : cameraTransform.position;
        CmdShootEffects(startPoint, endPoint);
    }

    [Command]
    void CmdShootEffects(Vector3 start, Vector3 end)
    {
        RpcShootEffects(start, end);
    }

    [ClientRpc]
    void RpcShootEffects(Vector3 start, Vector3 end)
    {
        SpawnTracer(start, end);
        StartCoroutine(MuzzleFlashCoroutine());
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("BulletTracer");
        BulletTracer tracer = tracerObj.AddComponent<BulletTracer>();
        tracer.SetPositions(start, end);
    }

    [Command]
    void CmdSpawnImpact(Vector3 position, Vector3 normal)
    {
        RpcSpawnImpact(position, normal);
    }

    [ClientRpc]
    void RpcSpawnImpact(Vector3 position, Vector3 normal)
    {
        BulletImpact.SpawnImpact(position, normal);
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
}
