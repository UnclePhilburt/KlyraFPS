using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

/// <summary>
/// Jet controller using the Aircraft-Physics system from GitHub.
/// Wraps the aerodynamic simulation and provides AI/player interface.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class JetController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Engine")]
    public float maxThrust = 150f;    // Thrust as acceleration (m/s²)
    public float throttleResponse = 2f;
    public float jetMass = 5000f;     // Mass in kg

    [Header("Control Surfaces")]
    public List<AeroSurface> controlSurfaces;
    public float pitchControlSensitivity = 0.3f;
    public float rollControlSensitivity = 0.3f;
    public float yawControlSensitivity = 0.1f;

    [Header("Flight Limits")]
    public float minSpeed = 40f;
    public float maxSpeed = 250f;

    [Header("Ground Handling")]
    public float groundDrag = 0.5f;
    public float wheelBrakeForce = 20f;
    public float noseWheelSteering = 20f;
    public Transform[] wheelTransforms;

    [Header("Health")]
    public float maxHealth = 300f;
    public float currentHealth;
    public bool isDestroyed = false;

    [Header("Team")]
    public Team jetTeam = Team.None;

    [Header("Player Control")]
    private Camera playerCamera;

    [Header("References")]
    public Transform pilotSeatPosition;
    public Transform[] gunMuzzles;
    public Transform[] missilePylons;
    public GameObject engineFlameEffect;
    public GameObject contrailEffect;

    // Components
    private Rigidbody rb;
    private AudioSource engineAudio;

    // Flight state
    private bool isEngineOn = false;
    private float currentThrottle = 0f;
    private float targetThrottle = 0f;
    private bool isOnGround = true;

    // Control inputs
    private float pitchInput = 0f;
    private float rollInput = 0f;
    private float yawInput = 0f;

    // AI
    private bool hasAIPilot = false;
    private AIController aiPilot;

    // Player pilot
    private bool hasPlayerPilot = false;
    private FPSControllerPhoton playerPilot;

    // Runway
    private Runway currentRunway;

    // Network
    private Vector3 networkPosition;
    private Quaternion networkRotation = Quaternion.identity;
    private float networkThrottle;
    private bool networkInitialized = false;

    // Ground detection
    private LayerMask groundMask;
    private float groundCheckDistance = 3f;

    // Cached calculations
    private float currentSpeed;
    private BiVector3 currentForceAndTorque;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = jetMass;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.5f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        engineAudio = GetComponent<AudioSource>();
        groundMask = LayerMask.GetMask("Default", "Terrain", "Ground");

        currentHealth = maxHealth;

        // Auto-find control surfaces if not assigned
        if (controlSurfaces == null || controlSurfaces.Count == 0)
        {
            controlSurfaces = new List<AeroSurface>(GetComponentsInChildren<AeroSurface>());
        }
    }

    void Start()
    {
        if (engineFlameEffect != null) engineFlameEffect.SetActive(false);
        if (contrailEffect != null) contrailEffect.SetActive(false);
    }

    void SetupPlayerCamera()
    {
        // Use main camera or create one
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            GameObject camObj = new GameObject("JetCamera");
            playerCamera = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }

        // Position behind and above the jet
        playerCamera.transform.SetParent(null); // Don't parent to jet (causes jitter)
        Debug.Log("[Jet] Player camera setup complete");
    }

    void LateUpdate()
    {
        // Follow camera for player
        if (hasPlayerPilot && playerCamera != null)
        {
            Vector3 targetPos = transform.position - transform.forward * 20f + Vector3.up * 8f;
            playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 5f);
            playerCamera.transform.LookAt(transform.position + transform.forward * 20f);
        }
    }

    void FixedUpdate()
    {
        if (isDestroyed) return;

        bool shouldSimulate = !PhotonNetwork.IsConnected ||
                              !PhotonNetwork.InRoom ||
                              photonView == null ||
                              photonView.IsMine ||
                              (hasAIPilot && PhotonNetwork.IsMasterClient);

        if (!shouldSimulate) return;

        currentSpeed = rb.linearVelocity.magnitude;
        CheckGround();

        // Debug every few seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"[Jet] Speed: {currentSpeed:F1}, OnGround: {isOnGround}, Engine: {isEngineOn}, Throttle: {currentThrottle:F2}, Pitch: {pitchInput:F2}");
        }

        if (isOnGround)
        {
            UpdateGroundPhysics();
        }
        else
        {
            UpdateFlightPhysics();
        }

        UpdateThrust();
        UpdateEffects();
    }

    void CheckGround()
    {
        bool anyHit = false;
        float minDist = float.MaxValue;

        // Check from center
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask))
        {
            anyHit = true;
            minDist = Mathf.Min(minDist, hit.distance);
        }

        // Check from wheels
        if (wheelTransforms != null)
        {
            foreach (var wheel in wheelTransforms)
            {
                if (wheel != null && Physics.Raycast(wheel.position, Vector3.down, out hit, groundCheckDistance, groundMask))
                {
                    anyHit = true;
                    minDist = Mathf.Min(minDist, hit.distance);
                }
            }
        }

        isOnGround = anyHit && minDist < 2f && transform.position.y < 20f;
    }

    void UpdateGroundPhysics()
    {
        // Ground friction/drag
        if (currentSpeed > 0.1f)
        {
            Vector3 lateralVel = Vector3.ProjectOnPlane(rb.linearVelocity, transform.forward);
            rb.AddForce(-lateralVel * groundDrag, ForceMode.Acceleration);
        }

        // Keep wings level - direct rotation
        Vector3 euler = transform.eulerAngles;
        float roll = euler.z > 180f ? euler.z - 360f : euler.z;
        if (Mathf.Abs(roll) > 0.5f)
        {
            euler.z = Mathf.LerpAngle(euler.z, 0f, Time.fixedDeltaTime * 10f);
            transform.eulerAngles = euler;
        }

        // Kill roll angular velocity
        Vector3 angVel = rb.angularVelocity;
        angVel.z = 0f;
        rb.angularVelocity = angVel;

        // Nose wheel steering
        if (currentSpeed > 3f)
        {
            float steer = yawInput * noseWheelSteering;
            rb.AddTorque(Vector3.up * steer, ForceMode.Acceleration);
        }

        // Pitch for takeoff rotation when fast enough
        float rotationSpeed = minSpeed * 0.75f;  // Start rotating at 75% of stall speed
        if (currentSpeed > rotationSpeed && pitchInput > 0.1f)
        {
            float rotateTorque = pitchInput * 50f;
            rb.AddTorque(transform.right * rotateTorque, ForceMode.Acceleration);
            Debug.Log($"[Jet] Rotating! Speed: {currentSpeed:F1}, Pitch: {pitchInput:F2}");
        }
        else
        {
            // Keep nose level
            float pitch = PitchAngle;
            if (Mathf.Abs(pitch) > 2f)
            {
                float correction = -pitch * 5f;
                rb.AddTorque(transform.right * correction, ForceMode.Acceleration);
            }
        }

        // Apply lift when going fast (helps with takeoff)
        if (currentSpeed > minSpeed * 0.7f)
        {
            float liftFactor = Mathf.Clamp01((currentSpeed - minSpeed * 0.7f) / (minSpeed * 0.3f));
            float lift = liftFactor * 15f;  // Strong lift to get airborne
            rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
        }
    }

    void UpdateFlightPhysics()
    {
        // Check if we have VALID AeroSurfaces configured (not null entries)
        bool hasAeroSurfaces = false;
        if (controlSurfaces != null && controlSurfaces.Count > 0)
        {
            foreach (var surface in controlSurfaces)
            {
                if (surface != null)
                {
                    hasAeroSurfaces = true;
                    break;
                }
            }
        }

        if (hasAeroSurfaces)
        {
            // Use full aerodynamic simulation
            UpdateAerodynamicFlight();
        }
        else
        {
            // Use simple arcade flight physics
            UpdateArcadeFlight();
        }

        // Speed limiter
        if (currentSpeed > maxSpeed)
        {
            float excess = currentSpeed - maxSpeed;
            rb.AddForce(-rb.linearVelocity.normalized * excess * rb.mass, ForceMode.Force);
        }

        // Flight envelope protection
        EnforceFlightEnvelope();
    }

    void UpdateArcadeFlight()
    {
        // === ARCADE FLIGHT PHYSICS ===
        // Simple, reliable flight model that works without AeroSurface components

        float speedFactor = Mathf.Clamp01(currentSpeed / minSpeed);
        float controlAuthority = speedFactor * speedFactor; // Quadratic control scaling

        // --- LIFT ---
        // Generate lift based on speed and pitch
        if (currentSpeed > minSpeed * 0.5f)
        {
            // Base lift to counteract gravity
            float liftMultiplier = Mathf.Clamp01((currentSpeed - minSpeed * 0.5f) / (minSpeed * 0.5f));
            float baseLift = 9.81f * liftMultiplier; // Counteract gravity

            // Additional lift from pitch (climbing/diving)
            float pitchAngle = PitchAngle;
            float pitchLift = Mathf.Sin(pitchAngle * Mathf.Deg2Rad) * currentSpeed * 0.1f;

            rb.AddForce(transform.up * baseLift, ForceMode.Acceleration);
        }

        // --- CONTROL TORQUES ---
        float pitchTorque = 80f;  // Pitch rate
        float rollTorque = 120f;  // Roll rate
        float yawTorque = 30f;    // Yaw rate

        // Pitch control (pull up / push down)
        rb.AddTorque(transform.right * pitchInput * pitchTorque * controlAuthority, ForceMode.Acceleration);

        // Roll control (bank left / right)
        rb.AddTorque(transform.forward * -rollInput * rollTorque * controlAuthority, ForceMode.Acceleration);

        // Yaw control (rudder)
        rb.AddTorque(transform.up * yawInput * yawTorque * controlAuthority, ForceMode.Acceleration);

        // --- VELOCITY ALIGNMENT ---
        // Make velocity follow nose direction (no slipping/sliding)
        Vector3 currentVel = rb.linearVelocity;
        if (currentVel.magnitude > 5f)
        {
            Vector3 desiredVel = transform.forward * currentVel.magnitude;
            Vector3 correction = (desiredVel - currentVel) * 3f;
            rb.AddForce(correction, ForceMode.Acceleration);
        }

        // --- NATURAL TURN (Bank-to-Turn) ---
        // When banked, the jet naturally turns
        float bank = BankAngle;
        if (Mathf.Abs(bank) > 5f && currentSpeed > minSpeed)
        {
            float turnRate = Mathf.Sin(bank * Mathf.Deg2Rad) * 0.5f;
            rb.AddTorque(Vector3.up * turnRate, ForceMode.Acceleration);
        }

        // --- PITCH STABILITY ---
        // Gently return to level flight when no input
        if (Mathf.Abs(pitchInput) < 0.1f)
        {
            float pitchAngle = PitchAngle;
            float stabilityForce = -pitchAngle * 0.02f * controlAuthority;
            rb.AddTorque(transform.right * stabilityForce, ForceMode.Acceleration);
        }

        // --- ROLL STABILITY ---
        // Gently level wings when no input
        if (Mathf.Abs(rollInput) < 0.1f)
        {
            float rollAngle = BankAngle;
            float stabilityForce = -rollAngle * 0.03f * controlAuthority;
            rb.AddTorque(transform.forward * stabilityForce, ForceMode.Acceleration);
        }

        // --- ANGULAR DAMPING ---
        // Prevent wild spinning
        Vector3 angVel = rb.angularVelocity;
        rb.AddTorque(-angVel * 2f, ForceMode.Acceleration);

        // Debug output
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"[Jet Arcade] Speed: {currentSpeed:F1}, Pitch: {PitchAngle:F1}°, Bank: {BankAngle:F1}°, Control: {controlAuthority:F2}");
        }
    }

    void UpdateAerodynamicFlight()
    {
        // Set control surface deflections
        foreach (var surface in controlSurfaces)
        {
            if (surface.IsControlSurface)
            {
                float input = 0f;
                switch (surface.InputType)
                {
                    case ControlInputType.Pitch:
                        input = pitchInput * pitchControlSensitivity;
                        break;
                    case ControlInputType.Roll:
                        input = rollInput * rollControlSensitivity;
                        break;
                    case ControlInputType.Yaw:
                        input = yawInput * yawControlSensitivity;
                        break;
                }
                surface.SetFlapAngle(input * surface.InputMultiplyer);
            }
        }

        // Calculate aerodynamic forces
        currentForceAndTorque = CalculateAerodynamicForces(
            rb.linearVelocity,
            rb.angularVelocity,
            Vector3.zero,
            1.2f,
            rb.worldCenterOfMass
        );

        // Prediction step for stability
        Vector3 velocityPrediction = rb.linearVelocity + Time.fixedDeltaTime * 0.5f *
            (currentForceAndTorque.p + transform.forward * maxThrust * currentThrottle + Physics.gravity * rb.mass) / rb.mass;

        BiVector3 predictedForces = CalculateAerodynamicForces(
            velocityPrediction,
            rb.angularVelocity,
            Vector3.zero,
            1.2f,
            rb.worldCenterOfMass
        );

        // Average current and predicted
        BiVector3 finalForces = (currentForceAndTorque + predictedForces) * 0.5f;

        rb.AddForce(finalForces.p);
        rb.AddTorque(finalForces.q);
    }

    BiVector3 CalculateAerodynamicForces(Vector3 velocity, Vector3 angularVelocity, Vector3 wind, float airDensity, Vector3 centerOfMass)
    {
        BiVector3 forceAndTorque = new BiVector3();

        foreach (var surface in controlSurfaces)
        {
            if (surface == null) continue;

            Vector3 relativePosition = surface.transform.position - centerOfMass;
            forceAndTorque += surface.CalculateForces(
                -velocity + wind - Vector3.Cross(angularVelocity, relativePosition),
                airDensity,
                relativePosition
            );
        }

        return forceAndTorque;
    }

    void EnforceFlightEnvelope()
    {
        float pitch = PitchAngle;
        float bank = BankAngle;

        // Prevent backflip (pitch > 60)
        if (pitch > 60f)
        {
            float correction = (pitch - 60f) * 2f * Mathf.Deg2Rad;
            rb.AddTorque(transform.right * correction * rb.mass, ForceMode.Force);
        }

        // Prevent unrecoverable dive (pitch < -70)
        if (pitch < -70f)
        {
            float correction = (-70f - pitch) * 2f * Mathf.Deg2Rad;
            rb.AddTorque(transform.right * -correction * rb.mass, ForceMode.Force);
        }

        // Limit bank angle
        if (Mathf.Abs(bank) > 80f)
        {
            float correction = (80f - Mathf.Abs(bank)) * Mathf.Sign(bank) * 2f * Mathf.Deg2Rad;
            rb.AddTorque(transform.forward * correction * rb.mass, ForceMode.Force);
        }

        // Low altitude protection
        if (transform.position.y < 25f && rb.linearVelocity.y < -5f)
        {
            rb.AddTorque(transform.right * -3f * rb.mass, ForceMode.Force);
            rb.AddTorque(transform.forward * -bank * 0.05f * rb.mass, ForceMode.Force);
        }
    }

    void UpdateThrust()
    {
        if (!isEngineOn)
        {
            currentThrottle = 0f;
            return;
        }

        currentThrottle = Mathf.MoveTowards(currentThrottle, targetThrottle, throttleResponse * Time.fixedDeltaTime);
        rb.AddForce(transform.forward * maxThrust * currentThrottle, ForceMode.Acceleration);
    }

    void UpdateEffects()
    {
        if (engineFlameEffect != null)
        {
            engineFlameEffect.SetActive(isEngineOn && currentThrottle > 0.5f);
        }

        if (contrailEffect != null)
        {
            contrailEffect.SetActive(!isOnGround && currentSpeed > 80f && transform.position.y > 50f);
        }
    }

    // === PUBLIC API ===

    public void StartEngine()
    {
        if (isDestroyed) return;
        isEngineOn = true;
        if (engineAudio != null) engineAudio.Play();
    }

    public void StopEngine()
    {
        isEngineOn = false;
        targetThrottle = 0f;
        if (engineAudio != null) engineAudio.Stop();
    }

    public void SetAIPilot(AIController pilot)
    {
        aiPilot = pilot;
        hasAIPilot = pilot != null;
        if (hasAIPilot) jetTeam = pilot.team;
    }

    public void ClearAIPilot()
    {
        aiPilot = null;
        hasAIPilot = false;
    }

    public void SetPlayerPilot(FPSControllerPhoton pilot)
    {
        playerPilot = pilot;
        hasPlayerPilot = pilot != null;
        if (hasPlayerPilot)
        {
            jetTeam = pilot.playerTeam;
            SetupPlayerCamera();
        }
    }

    public void ClearPlayerPilot()
    {
        playerPilot = null;
        hasPlayerPilot = false;
        pitchInput = 0f;
        rollInput = 0f;
        yawInput = 0f;
        targetThrottle = 0f;
    }

    public void SetAIInput(float throttle, float pitch, float roll, float yaw, bool fireGuns = false, bool fireMissile = false)
    {
        targetThrottle = Mathf.Clamp01(throttle);
        pitchInput = Mathf.Clamp(pitch, -1f, 1f);
        rollInput = Mathf.Clamp(roll, -1f, 1f);
        yawInput = Mathf.Clamp(yaw, -1f, 1f);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker)
    {
        if (isDestroyed) return;
        currentHealth -= damage;
        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        StopEngine();
        if (aiPilot != null) aiPilot.ExitJet();
        Destroy(gameObject, 5f);
    }

    public void SetOnRunway(Runway runway)
    {
        currentRunway = runway;
        isOnGround = true;
    }

    // === PROPERTIES ===

    public bool IsEngineOn => isEngineOn;
    public bool IsOnGround => isOnGround;
    public bool HasPilot => hasAIPilot || hasPlayerPilot;
    public float CurrentSpeed => currentSpeed;
    public float CurrentThrottle => currentThrottle;
    public float Altitude => transform.position.y;
    public Team JetTeam => jetTeam;
    public Runway CurrentRunway => currentRunway;

    public float BankAngle
    {
        get
        {
            Vector3 projectedUp = Vector3.ProjectOnPlane(transform.up, transform.forward).normalized;
            return Vector3.SignedAngle(Vector3.up, projectedUp, transform.forward);
        }
    }

    public float PitchAngle
    {
        get
        {
            Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forwardFlat.sqrMagnitude < 0.001f) return 90f;
            return Vector3.SignedAngle(forwardFlat.normalized, transform.forward, transform.right);
        }
    }

    public float Heading
    {
        get
        {
            Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float angle = Vector3.SignedAngle(Vector3.forward, forwardFlat, Vector3.up);
            return (angle + 360f) % 360f;
        }
    }

    // === NETWORK ===

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(currentThrottle);
            stream.SendNext(isEngineOn);
            stream.SendNext(currentHealth);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkThrottle = (float)stream.ReceiveNext();
            isEngineOn = (bool)stream.ReceiveNext();
            currentHealth = (float)stream.ReceiveNext();
            networkInitialized = true;
        }
    }

    void Update()
    {
        // Network interpolation for remote jets
        if (PhotonNetwork.IsConnected && photonView != null && !photonView.IsMine && networkInitialized)
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);
            currentThrottle = networkThrottle;
            return;
        }

        // Player controls when player is piloting
        if (hasPlayerPilot)
        {
            HandlePlayerInput();
        }
    }

    void HandlePlayerInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Engine start with E
        if (keyboard.eKey.wasPressedThisFrame)
        {
            if (isEngineOn) StopEngine();
            else StartEngine();
        }

        // Throttle
        if (keyboard.leftShiftKey.isPressed)
        {
            targetThrottle = Mathf.MoveTowards(targetThrottle, 1f, Time.deltaTime);
        }
        else if (keyboard.leftCtrlKey.isPressed)
        {
            targetThrottle = Mathf.MoveTowards(targetThrottle, 0f, Time.deltaTime * 2f);
        }

        // Pitch - W/S or Up/Down arrows
        pitchInput = 0f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            pitchInput = 1f; // Pull up
        }
        else if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            pitchInput = -1f; // Push down
        }

        // Roll - A/D or Left/Right arrows
        rollInput = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            rollInput = -1f; // Roll left
        }
        else if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            rollInput = 1f; // Roll right
        }

        // Yaw - Q/R
        yawInput = 0f;
        if (keyboard.qKey.isPressed)
        {
            yawInput = -1f; // Yaw left
        }
        else if (keyboard.rKey.isPressed)
        {
            yawInput = 1f; // Yaw right
        }

        // Debug HUD
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[Player Jet] Speed: {currentSpeed:F0} | Alt: {Altitude:F0} | Throttle: {currentThrottle:F2} | Pitch: {PitchAngle:F1}° | Bank: {BankAngle:F1}°");
        }
    }
}
