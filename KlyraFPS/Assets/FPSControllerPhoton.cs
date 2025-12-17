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

    [Header("ADS Settings")]
    public float aimFOV = 30f;
    public float normalFOV = 60f;
    public float aimSpeed = 10f;
    public Vector3 aimPosition = new Vector3(0f, -0.21f, 0.25f);

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

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();

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

        if (photonView.IsMine)
        {
            InitializeLocalPlayer();
        }
        else
        {
            InitializeRemotePlayer();
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

        // Disable character controller for remote players
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
        ReadInput();
        HandleMovement();
        HandleMouseLook();
        HandleShooting();
        HandleADS();
        PositionWeapon();
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

        // Update player rotation
        transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

        // Update camera
        if (cameraTransform != null)
        {
            Vector3 targetPosition = transform.position + Vector3.up * cameraHeight;
            cameraTransform.position = targetPosition;
            cameraTransform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
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

        if (jumpPressed && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Update animator
        float inputMagnitude = new Vector2(moveX, moveZ).magnitude;
        float animatorSpeed = inputMagnitude * currentSpeed;

        if (animator != null)
        {
            animator.SetFloat("MoveSpeed", animatorSpeed);
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

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;
        Vector3 endPoint;

        if (Physics.Raycast(ray, out hit, range))
        {
            endPoint = hit.point;

            // Check if we hit another player
            FPSControllerPhoton hitPlayer = hit.collider.GetComponent<FPSControllerPhoton>();
            if (hitPlayer != null && hitPlayer != this)
            {
                Debug.Log($"Hit player {hitPlayer.photonView.ViewID}!");
                // TODO: Implement damage via RPC
            }
            else
            {
                // Spawn impact effect via RPC
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
        SpawnTracer(start, end);
        StartCoroutine(MuzzleFlashCoroutine());
    }

    [PunRPC]
    void RPC_SpawnImpact(Vector3 position, Vector3 normal)
    {
        BulletImpact.SpawnImpact(position, normal);
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
