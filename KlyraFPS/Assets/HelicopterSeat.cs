using UnityEngine;
using Photon.Pun;

public enum SeatType
{
    Pilot,
    CoPilot,
    DoorGunnerLeft,
    DoorGunnerRight,
    Passenger
}

public class HelicopterSeat : MonoBehaviour
{
    [Header("Seat Configuration")]
    public SeatType seatType = SeatType.Passenger;
    public Transform seatPosition;
    public Transform exitPosition;
    public Transform cameraPosition;

    [Header("Camera Settings")]
    public float cameraFOV = 75f;
    public Vector2 lookLimitsHorizontal = new Vector2(-120f, 120f);
    public Vector2 lookLimitsVertical = new Vector2(-60f, 60f);

    [Header("Door Gunner Settings")]
    public bool hasMountedWeapon = false;
    public HelicopterWeapon mountedWeapon;

    // State
    public bool IsOccupied => occupant != null || aiOccupant != null;
    public FPSControllerPhoton occupant { get; private set; }
    public AIController aiOccupant { get; private set; }

    private HelicopterController helicopter;
    private Camera seatCamera;
    private float currentYaw = 0f;
    private float currentPitch = 10f;

    // Third person camera
    private float cameraDistance = 20f;
    private float minCameraDistance = 8f;
    private float maxCameraDistance = 40f;
    private float cameraHeight = 8f;

    // Camera control state
    private bool cameraSetupComplete = false;
    private bool isLocalOccupant = false;

    void Awake()
    {
        helicopter = GetComponentInParent<HelicopterController>();

        if (seatPosition == null)
            seatPosition = transform;

        if (cameraPosition == null)
            cameraPosition = transform;

        // Auto-detect mounted weapon
        if (mountedWeapon == null)
            mountedWeapon = GetComponentInChildren<HelicopterWeapon>();

        hasMountedWeapon = mountedWeapon != null;
    }

    public bool TryEnter(FPSControllerPhoton player)
    {
        if (IsOccupied) return false;
        if (player == null) return false;

        // Get seat index for network sync
        int seatIndex = helicopter != null ? helicopter.GetSeatIndex(this) : -1;

        // Network sync the entry via helicopter's PhotonView
        if (helicopter != null && helicopter.photonView != null &&
            PhotonNetwork.IsConnected && helicopter.photonView.ViewID != 0)
        {
            helicopter.photonView.RPC("RPC_EnterSeat", RpcTarget.All, seatIndex, player.photonView.ViewID);
        }
        else
        {
            // Offline mode or invalid view ID
            EnterSeat(player);
        }
        return true;
    }

    // AI entering seat
    public bool TryEnterAI(AIController ai)
    {
        if (IsOccupied) return false;
        if (ai == null) return false;

        aiOccupant = ai;
        return true;
    }

    // AI exiting seat
    public void ExitAI()
    {
        if (aiOccupant == null) return;

        aiOccupant = null;

        // Clear weapon
        if (mountedWeapon != null)
        {
            mountedWeapon.ClearAIGunner();
        }
    }

    public void EnterSeat(FPSControllerPhoton player)
    {
        if (player == null) return;

        occupant = player;

        // Parent player to seat
        player.transform.SetParent(seatPosition);
        player.transform.localPosition = Vector3.zero;
        player.transform.localRotation = Quaternion.identity;

        // Notify helicopter if we're the pilot
        if (seatType == SeatType.Pilot && helicopter != null)
        {
            helicopter.OnPilotEnter(player);
        }

        // Setup camera for local player
        isLocalOccupant = player.photonView == null || player.photonView.IsMine;
        if (isLocalOccupant)
        {
            SetupSeatCamera(player);
            player.OnEnterVehicleSeat(this);
        }

        // Enable weapon if applicable
        if (mountedWeapon != null && (seatType == SeatType.DoorGunnerLeft || seatType == SeatType.DoorGunnerRight))
        {
            mountedWeapon.SetOperator(player);
        }
    }

    void SetupSeatCamera(FPSControllerPhoton player)
    {
        cameraSetupComplete = false;

        // Find camera - try Camera.main first, then search for any camera
        Camera playerCam = Camera.main;
        if (playerCam == null)
        {
            Camera[] cameras = FindObjectsOfType<Camera>();
            foreach (Camera cam in cameras)
            {
                if (cam.enabled)
                {
                    playerCam = cam;
                    break;
                }
            }
        }

        // Try to find helicopter if not set
        if (helicopter == null)
            helicopter = GetComponentInParent<HelicopterController>();

        if (playerCam == null)
        {
            Debug.LogError("[HELI] CRITICAL: No camera found! Cannot setup seat camera.");
            return;
        }

        if (helicopter == null && (seatType == SeatType.Pilot || seatType == SeatType.CoPilot || seatType == SeatType.Passenger))
        {
            Debug.LogError("[HELI] CRITICAL: No helicopter reference for third-person camera!");
            return;
        }

        seatCamera = playerCam;

        // Third person camera for pilot, first person for gunners
        if (seatType == SeatType.Pilot || seatType == SeatType.CoPilot || seatType == SeatType.Passenger)
        {
            // Third person - unparent camera so we control it freely
            seatCamera.transform.SetParent(null);
            seatCamera.fieldOfView = 60f;

            // Set initial yaw to match helicopter facing
            currentYaw = helicopter.transform.eulerAngles.y;
            currentPitch = 15f; // Slightly above helicopter

            // Calculate initial camera position
            Vector3 offset = Quaternion.Euler(currentPitch, currentYaw, 0f) * new Vector3(0, 0, -cameraDistance);
            offset.y += cameraHeight;
            Vector3 targetPos = helicopter.transform.position + offset;
            Vector3 lookTarget = helicopter.transform.position + Vector3.up * 2f;

            seatCamera.transform.position = targetPos;
            seatCamera.transform.LookAt(lookTarget);
        }
        else
        {
            // First person for door gunners
            seatCamera.transform.SetParent(cameraPosition != null ? cameraPosition : transform);
            seatCamera.transform.localPosition = Vector3.zero;
            seatCamera.transform.localRotation = Quaternion.identity;
            seatCamera.fieldOfView = cameraFOV;
        }

        // Reset look angles only for non-pilots (pilots keep helicopter yaw)
        if (seatType != SeatType.Pilot && seatType != SeatType.CoPilot && seatType != SeatType.Passenger)
        {
            currentYaw = 0f;
            currentPitch = 0f;
        }

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Mark camera setup as complete
        cameraSetupComplete = true;

        // Setup HUD
        SetupHUD();
    }

    void SetupHUD()
    {
        // Find or create helicopter HUD
        HelicopterHUD hud = FindObjectOfType<HelicopterHUD>();
        if (hud == null)
        {
            GameObject hudObj = new GameObject("HelicopterHUD");
            hud = hudObj.AddComponent<HelicopterHUD>();
        }

        hud.SetHelicopter(helicopter);
        hud.SetSeat(this);
    }

    void CleanupHUD()
    {
        HelicopterHUD hud = FindObjectOfType<HelicopterHUD>();
        if (hud != null)
        {
            Destroy(hud.gameObject);
        }
    }

    public void Exit()
    {
        if (!IsOccupied) return;

        int seatIndex = helicopter != null ? helicopter.GetSeatIndex(this) : -1;

        if (helicopter != null && helicopter.photonView != null &&
            PhotonNetwork.IsConnected && helicopter.photonView.ViewID != 0)
        {
            helicopter.photonView.RPC("RPC_ExitSeat", RpcTarget.All, seatIndex);
        }
        else
        {
            ExitSeat();
        }
    }

    public void ForceExit()
    {
        // Called when helicopter is destroyed
        if (!IsOccupied) return;

        FPSControllerPhoton player = occupant;

        // Unparent and position at valid exit point
        if (player != null)
        {
            player.transform.SetParent(null);

            // Find valid ground position
            Vector3 exitPos = FindValidExitPosition();
            player.transform.position = exitPos;

            if (player.photonView.IsMine)
            {
                CleanupHUD();
                RestorePlayerCamera(player);
                player.OnExitVehicleSeat();
            }
        }

        // Notify helicopter
        if (seatType == SeatType.Pilot && helicopter != null)
        {
            helicopter.OnPilotExit();
        }

        // Clear weapon operator
        if (mountedWeapon != null)
        {
            mountedWeapon.SetOperator(null);
        }

        // Reset camera control state
        cameraSetupComplete = false;
        isLocalOccupant = false;
        seatCamera = null;

        occupant = null;
    }

    public void ExitSeat()
    {
        if (!IsOccupied) return;

        FPSControllerPhoton player = occupant;

        // Unparent player
        if (player != null)
        {
            player.transform.SetParent(null);

            // Find a valid exit position on ground
            Vector3 exitPos = FindValidExitPosition();
            player.transform.position = exitPos;

            // If local player, restore FPS controls
            if (player.photonView.IsMine)
            {
                CleanupHUD();
                RestorePlayerCamera(player);
                player.OnExitVehicleSeat();
            }
        }

        // Notify helicopter
        if (seatType == SeatType.Pilot && helicopter != null)
        {
            helicopter.OnPilotExit();
        }

        // Clear weapon operator
        if (mountedWeapon != null)
        {
            mountedWeapon.SetOperator(null);
        }

        // Reset camera control state
        cameraSetupComplete = false;
        isLocalOccupant = false;
        seatCamera = null;

        occupant = null;
    }

    // Find a valid position on ground for exiting
    Vector3 FindValidExitPosition()
    {
        Vector3 heliPos = helicopter != null ? helicopter.transform.position : transform.position;

        // Try multiple exit positions around the helicopter
        Vector3[] exitOffsets = {
            helicopter != null ? helicopter.transform.right * 5f : transform.right * 5f,
            helicopter != null ? -helicopter.transform.right * 5f : -transform.right * 5f,
            helicopter != null ? helicopter.transform.forward * 5f : transform.forward * 5f,
            helicopter != null ? -helicopter.transform.forward * 5f : -transform.forward * 5f
        };

        foreach (var offset in exitOffsets)
        {
            Vector3 testPos = heliPos + offset;

            // Raycast down to find ground
            RaycastHit hit;
            if (Physics.Raycast(testPos + Vector3.up * 10f, Vector3.down, out hit, 50f))
            {
                Vector3 groundPos = hit.point + Vector3.up * 0.5f;

                // Check if position is clear (no obstacles)
                if (!Physics.CheckSphere(groundPos + Vector3.up * 1f, 0.5f))
                {
                    return groundPos;
                }
            }
        }

        // Fallback: use helicopter position with ground check
        RaycastHit groundHit;
        if (Physics.Raycast(heliPos + Vector3.up * 10f, Vector3.down, out groundHit, 50f))
        {
            return groundHit.point + Vector3.up * 0.5f;
        }

        // Last resort: just offset from helicopter
        return heliPos + Vector3.up * 2f;
    }

    void RestorePlayerCamera(FPSControllerPhoton player)
    {
        Camera playerCam = Camera.main;
        if (playerCam == null)
        {
            Camera[] cameras = FindObjectsOfType<Camera>();
            foreach (Camera cam in cameras)
            {
                if (cam.enabled)
                {
                    playerCam = cam;
                    break;
                }
            }
        }

        if (playerCam != null && player != null)
        {
            // FPSControllerPhoton uses an unparented camera that it positions manually
            // So we just need to ensure the camera reference is correct
            playerCam.transform.SetParent(null); // Unparent (FPSController manages position directly)

            // Position camera at player's eye level
            Vector3 eyePos = player.transform.position + Vector3.up * 1.6f;
            playerCam.transform.position = eyePos;
            playerCam.transform.rotation = player.transform.rotation;

            // Update player's cameraTransform reference
            player.cameraTransform = playerCam.transform;
            playerCam.fieldOfView = GameUIManager.FOV;
        }
    }

    void Update()
    {
        if (!IsOccupied) return;
        if (!isLocalOccupant) return;

        // Handle seat-specific input (not camera - that's in LateUpdate)
        switch (seatType)
        {
            case SeatType.Pilot:
            case SeatType.CoPilot:
            case SeatType.Passenger:
                HandlePilotInput();
                break;

            case SeatType.DoorGunnerLeft:
            case SeatType.DoorGunnerRight:
                HandleGunnerInput();
                break;
        }

        // Check for exit
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
        {
            // Only allow exit when helicopter is slow or grounded
            if (helicopter != null)
            {
                Rigidbody heliRb = helicopter.GetComponent<Rigidbody>();
                if (heliRb == null || heliRb.linearVelocity.magnitude < 5f)
                {
                    Exit();
                }
            }
            else
            {
                Exit();
            }
        }

        // Check for seat switching
        HandleSeatSwitch();
    }

    void LateUpdate()
    {
        // Only handle camera for local occupant
        if (!IsOccupied || !isLocalOccupant) return;
        if (!cameraSetupComplete) return;

        // Position camera AFTER everything else (including FPSControllerPhoton.LateUpdate)
        if (seatType == SeatType.Pilot || seatType == SeatType.CoPilot || seatType == SeatType.Passenger)
        {
            UpdateThirdPersonCamera();
        }
    }

    void HandleGunnerInput()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        // Look around within limits
        Vector2 mouseDelta = mouse.delta.ReadValue() * 0.1f;

        currentYaw += mouseDelta.x * 2f;
        currentPitch -= mouseDelta.y * 2f;

        // Clamp
        currentYaw = Mathf.Clamp(currentYaw, lookLimitsHorizontal.x, lookLimitsHorizontal.y);
        currentPitch = Mathf.Clamp(currentPitch, lookLimitsVertical.x, lookLimitsVertical.y);

        // Apply to camera
        if (cameraPosition != null)
        {
            cameraPosition.localRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        }

        // Handle weapon firing
        if (mountedWeapon != null && mouse.leftButton.isPressed)
        {
            mountedWeapon.Fire();
        }
    }

    // Track if we're in free look mode
    private bool isFreeLook = false;
    private float freeLookYawOffset = 0f;

    // Handle input for camera orbit (runs in Update)
    void HandlePilotInput()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        // Right mouse button for free look
        if (mouse.rightButton.isPressed)
        {
            isFreeLook = true;
            Vector2 mouseDelta = mouse.delta.ReadValue() * 0.3f;
            freeLookYawOffset += mouseDelta.x;
            currentPitch -= mouseDelta.y;
            currentPitch = Mathf.Clamp(currentPitch, -10f, 60f);
        }
        else
        {
            // Release free look - smoothly return to following helicopter
            isFreeLook = false;
            freeLookYawOffset = Mathf.Lerp(freeLookYawOffset, 0f, Time.deltaTime * 5f);
        }

        // Scroll wheel to zoom in/out
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            cameraDistance -= scroll * 0.5f;
            cameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);
        }
    }

    // Position camera around helicopter (runs in LateUpdate)
    void UpdateThirdPersonCamera()
    {
        // Ensure we have valid references
        if (seatCamera == null)
        {
            seatCamera = Camera.main;
            if (seatCamera == null)
            {
                Camera[] cameras = FindObjectsOfType<Camera>();
                foreach (Camera cam in cameras)
                {
                    if (cam.enabled)
                    {
                        seatCamera = cam;
                        break;
                    }
                }
            }
            if (seatCamera == null)
            {
                Debug.LogError("[HELI] UpdateThirdPersonCamera: No camera found!");
                return;
            }
        }

        if (helicopter == null)
        {
            helicopter = GetComponentInParent<HelicopterController>();
            if (helicopter == null)
            {
                Debug.LogError("[HELI] UpdateThirdPersonCamera: No helicopter reference!");
                return;
            }
        }

        // Ensure camera is unparented (critical for third person)
        if (seatCamera.transform.parent != null)
        {
            seatCamera.transform.SetParent(null);
        }

        // Camera follows helicopter yaw by default, plus any free look offset
        float cameraYaw = helicopter.transform.eulerAngles.y + freeLookYawOffset;

        // Calculate camera position orbiting around helicopter
        Vector3 offset = Quaternion.Euler(currentPitch, cameraYaw, 0f) * new Vector3(0, 0, -cameraDistance);
        offset.y += cameraHeight;

        Vector3 targetPos = helicopter.transform.position + offset;
        Vector3 lookTarget = helicopter.transform.position + Vector3.up * 2f;

        // Smoothly move camera (reduces jitter)
        seatCamera.transform.position = Vector3.Lerp(seatCamera.transform.position, targetPos, Time.deltaTime * 15f);
        seatCamera.transform.LookAt(lookTarget);
    }

    void HandleSeatSwitch()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null || helicopter == null) return;

        int targetSeatIndex = -1;

        if (keyboard.digit1Key.wasPressedThisFrame) targetSeatIndex = 0;
        else if (keyboard.digit2Key.wasPressedThisFrame) targetSeatIndex = 1;
        else if (keyboard.digit3Key.wasPressedThisFrame) targetSeatIndex = 2;
        else if (keyboard.digit4Key.wasPressedThisFrame) targetSeatIndex = 3;
        else if (keyboard.digit5Key.wasPressedThisFrame) targetSeatIndex = 4;
        else if (keyboard.digit6Key.wasPressedThisFrame) targetSeatIndex = 5;

        if (targetSeatIndex >= 0)
        {
            HelicopterSeat targetSeat = helicopter.GetSeatByIndex(targetSeatIndex);
            if (targetSeat != null && !targetSeat.IsOccupied && targetSeat != this)
            {
                // Switch seats
                FPSControllerPhoton player = occupant;
                Exit();
                targetSeat.TryEnter(player);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw seat position
        Gizmos.color = Color.blue;
        Vector3 pos = seatPosition != null ? seatPosition.position : transform.position;
        Gizmos.DrawWireCube(pos, new Vector3(0.5f, 1f, 0.5f));

        // Draw exit position
        if (exitPosition != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(exitPosition.position, 0.3f);
            Gizmos.DrawLine(pos, exitPosition.position);
        }

        // Draw camera position
        if (cameraPosition != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(cameraPosition.position, 0.2f);
        }

        // Draw look limits for gunners
        if (seatType == SeatType.DoorGunnerLeft || seatType == SeatType.DoorGunnerRight)
        {
            Gizmos.color = Color.red;
            Vector3 camPos = cameraPosition != null ? cameraPosition.position : transform.position;

            // Draw arc for horizontal limits
            Quaternion leftLimit = Quaternion.Euler(0, lookLimitsHorizontal.x, 0);
            Quaternion rightLimit = Quaternion.Euler(0, lookLimitsHorizontal.y, 0);

            Gizmos.DrawLine(camPos, camPos + leftLimit * transform.forward * 3f);
            Gizmos.DrawLine(camPos, camPos + rightLimit * transform.forward * 3f);
        }
    }
}
