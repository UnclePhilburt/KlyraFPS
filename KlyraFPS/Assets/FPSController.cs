using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

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
    public Transform muzzlePoint; // Point where bullets come from
    public Transform weaponTransform; // The weapon model (for ADS movement)

    [Header("Camera Settings")]
    public float cameraHeight = 1.6f; // Height of camera above player feet

    [Header("First Person Weapon")]
    public Vector3 weaponOffset = new Vector3(0.15f, -0.33f, 0.22f); // Offset from camera (right, down, forward)

    [Header("Weapon Bob")]
    public float bobFrequency = 10f; // How fast the bob cycles
    public float bobHorizontalAmount = 0.03f; // Side to side bob
    public float bobVerticalAmount = 0.02f; // Up and down bob
    private float bobTimer = 0f;
    private float bobIntensity = 0f; // Smoothly fades bob in/out
    private Vector3 currentWeaponOffset; // Smoothed offset for ADS transitions

    [Header("Shooting Settings")]
    public float fireRate = 0.1f; // Time between shots
    public float damage = 25f;
    public float range = 100f;

    [Header("ADS Settings")]
    public float aimFOV = 30f; // Field of view when aiming
    public float normalFOV = 60f; // Normal field of view
    public float aimSpeed = 10f; // How fast to zoom in/out
    public Vector3 aimPosition = new Vector3(0f, -0.21f, 0.25f); // Local position when aiming
    public Vector3 normalPosition = Vector3.zero; // Normal weapon position (will be set at start)

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
    private float rotationY = 0f; // Track our own Y rotation (immune to root motion)

    // Shooting
    private float nextFireTime = 0f;
    private Light muzzleFlash;
    private bool isAiming = false;
    private Camera playerCamera;

    // Network synced variables
    private NetworkVariable<float> syncedMoveSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<bool> syncedIsGrounded = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<bool> syncedIsJumping = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // Synty animator specific parameters
    private NetworkVariable<bool> syncedMovementInputHeld = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<int> syncedCurrentGait = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<float> syncedStrafeX = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<float> syncedStrafeZ = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // Manual position and rotation sync (replacing NetworkTransform)
    private NetworkVariable<Vector3> syncedPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> syncedRotationY = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get components
        controller = GetComponent<CharacterController>();

        // Disable CharacterController on remote players (they use synced position)
        if (!IsOwner && controller != null)
        {
            controller.enabled = false;
        }

        // Initialize player model with delay to handle nested prefabs
        StartCoroutine(InitializePlayerModel());

        // Only local player needs camera and cursor locked
        if (IsOwner)
        {
            // Initialize rotation from current transform (so we don't snap to 0)
            rotationY = transform.eulerAngles.y;
            rotationX = 0f;

            // AAA approach: Camera is completely detached from player hierarchy
            // Find or create the camera as a root-level object
            if (cameraTransform == null)
            {
                cameraTransform = Camera.main.transform;
                Debug.Log($"[FPS] Using Camera.main: {cameraTransform?.name}");
            }
            else
            {
                Debug.Log($"[FPS] Using assigned cameraTransform: {cameraTransform.name}");
            }

            if (cameraTransform != null)
            {
                Debug.Log($"[FPS] Camera parent BEFORE unparent: {cameraTransform.parent?.name ?? "NULL (already root)"}");

                // Unparent camera completely - it's now a root object
                cameraTransform.SetParent(null);

                Debug.Log($"[FPS] Camera parent AFTER unparent: {cameraTransform.parent?.name ?? "NULL (root)"}");

                // Mark it to not be destroyed on scene changes (optional)
                DontDestroyOnLoad(cameraTransform.gameObject);
            }
            else
            {
                Debug.LogError("[FPS] No camera found!");
            }

            // Get camera component for ADS
            playerCamera = cameraTransform.GetComponent<Camera>();
            if (playerCamera != null)
            {
                playerCamera.fieldOfView = normalFOV;
            }

            // Initialize weapon offset for ADS
            currentWeaponOffset = weaponOffset;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // Disable camera for remote players
            if (cameraTransform != null)
            {
                Camera cam = cameraTransform.GetComponent<Camera>();
                if (cam != null)
                {
                    cam.enabled = false;
                }
                AudioListener listener = cameraTransform.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }
    }

    System.Collections.IEnumerator InitializePlayerModel()
    {
        // Wait for nested prefab to instantiate
        yield return new WaitForEndOfFrame();

        // Find animator in children
        animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            Debug.Log($"Found animator: {animator.gameObject.name}, Controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULL")}");
        }
        else
        {
            Debug.LogError("No Animator found in children!");
        }

        if (animator != null)
        {
            // Subscribe to network variable changes for remote players
            if (!IsOwner)
            {
                syncedMoveSpeed.OnValueChanged += OnMoveSpeedChanged;
                syncedIsGrounded.OnValueChanged += OnIsGroundedChanged;
                syncedIsJumping.OnValueChanged += OnIsJumpingChanged;
                syncedMovementInputHeld.OnValueChanged += OnMovementInputHeldChanged;
                syncedCurrentGait.OnValueChanged += OnCurrentGaitChanged;
                syncedStrafeX.OnValueChanged += OnStrafeXChanged;
                syncedStrafeZ.OnValueChanged += OnStrafeZChanged;
            }
        }

        // Hide entire player body for local player (first-person = floating gun only)
        if (IsOwner)
        {
            // Hide all skinned meshes (character body)
            SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in skinnedRenderers)
            {
                renderer.enabled = false;
            }

            // Hide all mesh renderers EXCEPT weapons
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
        if (IsOwner)
        {
            // Read input from new Input System
            ReadInput();
            HandleMovement();
            HandleMouseLook();
            HandleShooting();
            HandleADS();

            // Position weapon BEFORE IK runs (OnAnimatorIK happens after Update but before LateUpdate)
            PositionWeapon();

            // Sync position and rotation to network every frame
            if (NetworkManager.Singleton.IsServer)
            {
                // Host - directly update NetworkVariables
                syncedPosition.Value = transform.position;
                syncedRotationY.Value = rotationY;
            }
            else
            {
                // Client - send to server
                UpdatePositionServerRpc(transform.position, rotationY);
            }
        }
        else
        {
            // Apply synced position and rotation from owner
            transform.position = syncedPosition.Value;
            transform.rotation = Quaternion.Euler(0, syncedRotationY.Value, 0);

            UpdateRemoteAnimator();
        }
    }

    void PositionWeapon()
    {
        // Calculate camera position/rotation (same as LateUpdate but we need it here for IK)
        Vector3 cameraPos = transform.position + Vector3.up * cameraHeight;
        Quaternion cameraRot = Quaternion.Euler(rotationX, rotationY, 0f);

        // Position weapon relative to camera (before IK runs)
        if (weaponTransform != null)
        {
            // Choose base offset (ADS or normal)
            Vector3 targetOffset = isAiming ? aimPosition : weaponOffset;

            // Smooth transition between hip and ADS
            currentWeaponOffset = Vector3.Lerp(currentWeaponOffset, targetOffset, Time.deltaTime * aimSpeed);

            // Calculate weapon bob when moving (reduced when aiming)
            Vector3 bobOffset = Vector3.zero;
            bool isMoving = controller != null && controller.isGrounded && moveInput.magnitude > 0.1f;

            // Smoothly fade bob intensity in/out (disable bob when aiming)
            float targetIntensity = (isMoving && !isAiming) ? 1f : 0f;
            bobIntensity = Mathf.Lerp(bobIntensity, targetIntensity, Time.deltaTime * 8f);

            // Always advance timer (keeps motion smooth)
            float speedMultiplier = sprintPressed ? 1.5f : 1f;
            bobTimer += Time.deltaTime * bobFrequency * speedMultiplier;

            // Calculate bob using sine waves, scaled by intensity
            bobOffset.x = Mathf.Sin(bobTimer) * bobHorizontalAmount * bobIntensity;
            bobOffset.y = Mathf.Sin(bobTimer * 2f) * bobVerticalAmount * bobIntensity;

            Vector3 finalOffset = currentWeaponOffset + bobOffset;
            weaponTransform.position = cameraPos + cameraRot * finalOffset;
            weaponTransform.rotation = cameraRot;
        }
    }

    void LateUpdate()
    {
        if (IsOwner)
        {
            // Force player rotation after animations (override root motion)
            transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

            // Position camera (after animations)
            if (cameraTransform != null)
            {
                Vector3 targetPosition = transform.position + Vector3.up * cameraHeight;
                cameraTransform.position = targetPosition;
                cameraTransform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
            }
        }
    }

    [ServerRpc]
    void UpdatePositionServerRpc(Vector3 position, float yRotation)
    {
        syncedPosition.Value = position;
        syncedRotationY.Value = yRotation;
    }

    void ReadInput()
    {
        // Use Keyboard and Mouse classes from new Input System
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null)
        {
            Debug.LogWarning("Keyboard.current is NULL!");
            return;
        }

        if (keyboard != null)
        {
            // WASD movement
            float moveX = 0f;
            float moveZ = 0f;

            if (keyboard.wKey.isPressed) moveZ += 1f;
            if (keyboard.sKey.isPressed) moveZ -= 1f;
            if (keyboard.aKey.isPressed) moveX -= 1f;
            if (keyboard.dKey.isPressed) moveX += 1f;

            moveInput = new Vector2(moveX, moveZ);

            if (moveInput.magnitude > 0)
            {
                Debug.Log($"[{(IsOwner ? "OWNER" : "REMOTE")}] Move input: {moveInput}");
            }

            // Sprint
            sprintPressed = keyboard.leftShiftKey.isPressed;

            // Jump
            jumpPressed = keyboard.spaceKey.wasPressedThisFrame;

            // Unlock cursor with Escape
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (mouse != null)
        {
            // Mouse look
            lookInput = mouse.delta.ReadValue();

            // Re-lock cursor on click
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void HandleMovement()
    {
        if (controller == null) return;

        // Reset velocity when grounded (prevents accumulation)
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // Get input
        float moveX = moveInput.x;
        float moveZ = moveInput.y;

        // Determine speed (sprint or walk)
        currentSpeed = sprintPressed ? sprintSpeed : walkSpeed;

        // Calculate move direction relative to player rotation
        Vector3 move = transform.right * moveX + transform.forward * moveZ;

        // Apply movement
        Vector3 moveAmount = move * currentSpeed * Time.deltaTime;
        controller.Move(moveAmount);

        if (moveAmount.magnitude > 0)
        {
            Debug.Log($"[{(IsOwner ? "OWNER" : "REMOTE")}] Moving by {moveAmount.magnitude:F3}, Position: {transform.position}");
        }

        // Calculate animator speed (magnitude of movement)
        float inputMagnitude = new Vector2(moveX, moveZ).magnitude;
        float animatorSpeed = inputMagnitude * currentSpeed;
        bool isMoving = inputMagnitude > 0.1f;

        // Handle jumping
        if (jumpPressed && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            syncedIsJumping.Value = true;
        }
        else if (controller.isGrounded)
        {
            syncedIsJumping.Value = false;
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Update network variables
        syncedMoveSpeed.Value = animatorSpeed;
        syncedIsGrounded.Value = controller.isGrounded;
        syncedMovementInputHeld.Value = isMoving;
        syncedStrafeX.Value = moveX;
        syncedStrafeZ.Value = moveZ;

        // Set gait: 0 = idle, 1 = walk, 2 = run
        if (!isMoving)
        {
            syncedCurrentGait.Value = 0;
        }
        else if (sprintPressed)
        {
            syncedCurrentGait.Value = 2; // Run
        }
        else
        {
            syncedCurrentGait.Value = 1; // Walk
        }

        // Update local animator
        if (animator != null)
        {
            UpdateAnimatorParameters(animatorSpeed, controller.isGrounded, syncedIsJumping.Value,
                                    isMoving, syncedCurrentGait.Value, moveX, moveZ);
        }
    }

    void UpdateAnimatorParameters(float moveSpeed, bool isGrounded, bool isJumping,
                                   bool movementHeld, int currentGait, float strafeX, float strafeZ)
    {
        if (animator == null) return;

        // Simple animator only needs MoveSpeed
        // Idle = 0, Walk = 1-7, Run = 7+
        animator.SetFloat("MoveSpeed", moveSpeed);
    }

    void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // Get mouse input
        float mouseX = lookInput.x * mouseSensitivity * 0.02f;
        float mouseY = lookInput.y * mouseSensitivity * 0.02f;

        // Track our own rotation (immune to root motion)
        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);

        // Apply Y rotation to player (for movement direction)
        // We set it directly instead of using Rotate() to override any root motion
        transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    void UpdateRemoteAnimator()
    {
        if (animator != null)
        {
            UpdateAnimatorParameters(syncedMoveSpeed.Value, syncedIsGrounded.Value,
                                    syncedIsJumping.Value, syncedMovementInputHeld.Value,
                                    syncedCurrentGait.Value, syncedStrafeX.Value, syncedStrafeZ.Value);
        }
    }

    // Network variable change callbacks
    void OnMoveSpeedChanged(float oldValue, float newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetFloat("MoveSpeed", newValue);
        }
    }

    void OnIsGroundedChanged(bool oldValue, bool newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetBool("IsGrounded", newValue);
        }
    }

    void OnIsJumpingChanged(bool oldValue, bool newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetBool("IsJumping", newValue);
        }
    }

    void OnMovementInputHeldChanged(bool oldValue, bool newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetBool("MovementInputHeld", newValue);
            animator.SetBool("IsStopped", !newValue);
        }
    }

    void OnCurrentGaitChanged(int oldValue, int newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetInteger("CurrentGait", newValue);
        }
    }

    void OnStrafeXChanged(float oldValue, float newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetFloat("StrafeDirectionX", newValue);
        }
    }

    void OnStrafeZChanged(float oldValue, float newValue)
    {
        if (animator != null && !IsOwner)
        {
            animator.SetFloat("StrafeDirectionZ", newValue);
        }
    }

    void HandleShooting()
    {
        // Check if player is shooting
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    void HandleADS()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            isAiming = mouse.rightButton.isPressed;
        }

        // Smoothly zoom camera FOV
        if (playerCamera != null)
        {
            float targetFOV = isAiming ? aimFOV : normalFOV;
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * aimSpeed);
        }
    }

    void Shoot()
    {
        // Raycast from camera forward
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;

        Vector3 endPoint;

        if (Physics.Raycast(ray, out hit, range))
        {
            endPoint = hit.point;

            // Check if we hit another player
            FPSController hitPlayer = hit.collider.GetComponent<FPSController>();
            if (hitPlayer != null && hitPlayer != this)
            {
                Debug.Log($"Hit player at {hit.point}!");
                // TODO: Deal damage when we add health system
            }
            else
            {
                // Hit a wall/prop - spawn impact decal
                SpawnImpactServerRpc(hit.point, hit.normal);
            }
        }
        else
        {
            // No hit - bullet goes to max range
            endPoint = cameraTransform.position + cameraTransform.forward * range;
        }

        // Get muzzle position for tracer start
        Vector3 startPoint = muzzlePoint != null ? muzzlePoint.position : cameraTransform.position;

        // Show tracer and muzzle flash on all clients
        ShootEffectsServerRpc(startPoint, endPoint);
    }

    [ServerRpc]
    void ShootEffectsServerRpc(Vector3 start, Vector3 end)
    {
        // Tell all clients to show effects
        ShootEffectsClientRpc(start, end);
    }

    [ClientRpc]
    void ShootEffectsClientRpc(Vector3 start, Vector3 end)
    {
        // Spawn tracer
        SpawnTracer(start, end);

        // Muzzle flash
        StartCoroutine(MuzzleFlashCoroutine());
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("BulletTracer");
        BulletTracer tracer = tracerObj.AddComponent<BulletTracer>();
        tracer.SetPositions(start, end);
    }

    [ServerRpc]
    void SpawnImpactServerRpc(Vector3 position, Vector3 normal)
    {
        SpawnImpactClientRpc(position, normal);
    }

    [ClientRpc]
    void SpawnImpactClientRpc(Vector3 position, Vector3 normal)
    {
        BulletImpact.SpawnImpact(position, normal);
    }

    [ServerRpc]
    void ShowMuzzleFlashServerRpc()
    {
        // Tell all clients to show muzzle flash
        ShowMuzzleFlashClientRpc();
    }

    [ClientRpc]
    void ShowMuzzleFlashClientRpc()
    {
        StartCoroutine(MuzzleFlashCoroutine());
    }

    System.Collections.IEnumerator MuzzleFlashCoroutine()
    {
        // Create muzzle flash light if it doesn't exist
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

    Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;
            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    public override void OnDestroy()
    {
        // Unsubscribe from events
        if (!IsOwner)
        {
            syncedMoveSpeed.OnValueChanged -= OnMoveSpeedChanged;
            syncedIsGrounded.OnValueChanged -= OnIsGroundedChanged;
            syncedIsJumping.OnValueChanged -= OnIsJumpingChanged;
            syncedMovementInputHeld.OnValueChanged -= OnMovementInputHeldChanged;
            syncedCurrentGait.OnValueChanged -= OnCurrentGaitChanged;
            syncedStrafeX.OnValueChanged -= OnStrafeXChanged;
            syncedStrafeZ.OnValueChanged -= OnStrafeZChanged;
        }
        base.OnDestroy();
    }
}
