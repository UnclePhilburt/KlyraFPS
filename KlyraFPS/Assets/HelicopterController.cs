using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;
using System.Collections.Generic;

public class HelicopterController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Flight Settings")]
    public float liftPower = 18f;        // Lift force (gravity is ~10)
    public float movePower = 12f;        // Horizontal thrust
    public float turnSpeed = 50f;        // Yaw rotation speed
    public float maxSpeed = 35f;         // Max velocity
    public float tiltAmount = 15f;       // How much it tilts when moving
    public float tiltSpeed = 3f;         // How fast it tilts
    public float groundEffectHeight = 10f;  // For dust effects

    [Header("Health")]
    public float maxHealth = 500f;
    public float currentHealth;
    public bool isDestroyed = false;

    [Header("Rotor Settings")]
    public Transform mainRotor;
    public Transform tailRotor;
    public float maxRotorSpeed = 2000f;
    public float rotorSpinUpTime = 3f;
    private float currentRotorSpeed = 0f;
    [HideInInspector] public bool engineOn = false;

    [Header("Audio")]
    public AudioClip engineStartSound;
    public AudioClip engineLoopSound;
    public AudioClip rotorSound;
    public AudioClip explosionSound;
    private AudioSource engineAudio;
    private AudioSource rotorAudio;

    [Header("Effects")]
    public GameObject dustEffectPrefab;
    public GameObject explosionEffectPrefab;
    public Transform dustSpawnPoint;
    private GameObject activeDustEffect;

    [Header("Seats")]
    public List<HelicopterSeat> seats = new List<HelicopterSeat>();

    [Header("Virtual Passengers (no seat needed)")]
    public int maxVirtualPassengers = 6;
    private List<AIController> virtualPassengers = new List<AIController>();

    [Header("Team")]
    public Team helicopterTeam = Team.None;

    // Components
    private Rigidbody rb;

    // Input
    private float throttleInput = 0f;
    private float collectiveInput = 0f;
    private Vector2 cyclicInput;
    private float yawInput = 0f;
    private Vector2 mouseInput;

    // State
    private bool isGrounded = false;
    private float groundDistance = 0f;
    private HelicopterSeat pilotSeat;
    private FPSControllerPhoton currentPilot;

    // AI Pilot
    private AIController aiPilot;
    private bool hasAIPilot = false;

    // Player calling the helicopter
    private FPSControllerPhoton callingPlayer;
    private bool isBeingCalledByPlayer = false;

    // Network sync
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 networkVelocity;
    private float networkRotorSpeed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.mass = 500f;
        rb.linearDamping = 0.5f;    // Some air resistance
        rb.angularDamping = 3f;     // Dampens rotation
        rb.useGravity = true;       // Gravity pulls it down
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.None;  // Free to tilt

        currentHealth = maxHealth;

        // Setup audio
        SetupAudio();

        // Auto-populate seats list if empty
        if (seats.Count == 0)
        {
            HelicopterSeat[] childSeats = GetComponentsInChildren<HelicopterSeat>();
            foreach (var seat in childSeats)
            {
                seats.Add(seat);
            }
        }

        // Find pilot seat
        foreach (var seat in seats)
        {
            if (seat != null && seat.seatType == SeatType.Pilot)
            {
                pilotSeat = seat;
                break;
            }
        }

        if (pilotSeat == null)
        {
            // If still no pilot seat, use the first seat as pilot
            if (seats.Count > 0 && seats[0] != null)
            {
                pilotSeat = seats[0];
            }
        }
    }

    void SetupAudio()
    {
        engineAudio = gameObject.AddComponent<AudioSource>();
        engineAudio.spatialBlend = 1f;
        engineAudio.maxDistance = 100f;
        engineAudio.rolloffMode = AudioRolloffMode.Linear;
        engineAudio.loop = true;
        engineAudio.playOnAwake = false;

        rotorAudio = gameObject.AddComponent<AudioSource>();
        rotorAudio.spatialBlend = 1f;
        rotorAudio.maxDistance = 150f;
        rotorAudio.rolloffMode = AudioRolloffMode.Linear;
        rotorAudio.loop = true;
        rotorAudio.playOnAwake = false;

        if (rotorSound != null)
        {
            rotorAudio.clip = rotorSound;
        }
        if (engineLoopSound != null)
        {
            engineAudio.clip = engineLoopSound;
        }
    }

    void Start()
    {
        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    void Update()
    {
        if (isDestroyed) return;

        // Update rotors
        UpdateRotors();

        // Debug: Check why we might not have a local pilot
        bool hasPilot = HasLocalPilot();

        // Handle input if we're the pilot (but not if AI is piloting)
        if (hasPilot && !hasAIPilot)
        {
            HandleInput();
        }
        else
        {
            // No local pilot
        }

        // Update audio
        UpdateAudio();

        // Update effects
        UpdateEffects();

        // Network interpolation for remote helicopters
        if (PhotonNetwork.IsConnected && photonView != null && !photonView.IsMine && !hasPilot)
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, Time.deltaTime * 10f);
            currentRotorSpeed = Mathf.Lerp(currentRotorSpeed, networkRotorSpeed, Time.deltaTime * 5f);
        }
    }

    // Debug timer
    private float debugTimer = 0f;

    void FixedUpdate()
    {
        // Handle crashing helicopter
        if (isDestroyed)
        {
            if (isCrashing && rb != null)
            {
                // Keep spinning
                transform.Rotate(Vector3.up, crashSpinSpeed * Time.fixedDeltaTime, Space.World);

                // Nose down slightly
                Vector3 euler = transform.eulerAngles;
                float targetPitch = 30f;  // Nose down
                euler.x = Mathf.MoveTowardsAngle(euler.x, targetPitch, 60f * Time.fixedDeltaTime);
                transform.eulerAngles = euler;
            }
            return;
        }

        // Check if we should control physics
        bool hasPhysicsAuthority = !PhotonNetwork.IsConnected ||
                                   photonView == null ||
                                   photonView.IsMine ||
                                   HasLocalPilot();

        if (!hasPhysicsAuthority) return;

        // Can fly when engine on and rotors spun up
        bool canFly = engineOn && currentRotorSpeed > maxRotorSpeed * 0.5f;

        if (canFly)
        {
            ApplyFlightPhysics();
        }
    }

    bool HasLocalPilot()
    {
        // Check for AI pilot first
        if (hasAIPilot && aiPilot != null)
        {
            return true; // AI controls locally
        }

        if (pilotSeat == null) return false;
        if (!pilotSeat.IsOccupied) return false;
        if (pilotSeat.occupant == null) return false;

        // Check if this is our local player
        // In offline mode, photonView might be null
        var pv = pilotSeat.occupant.photonView;
        if (pv == null)
        {
            // Offline mode - assume local
            return true;
        }
        return pv.IsMine;
    }

    bool HasAnyPilot()
    {
        return hasAIPilot || (pilotSeat != null && pilotSeat.IsOccupied);
    }

    void HandleInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null) return;

        // Engine toggle
        if (keyboard.eKey.wasPressedThisFrame)
        {
            ToggleEngine();
        }

        // Vertical - Space = up, Ctrl = down
        collectiveInput = 0f;
        if (keyboard.spaceKey.isPressed) collectiveInput = 1f;
        if (keyboard.leftCtrlKey.isPressed) collectiveInput = -1f;

        // Horizontal movement - WASD
        cyclicInput = Vector2.zero;
        if (keyboard.wKey.isPressed) cyclicInput.y = 1f;
        if (keyboard.sKey.isPressed) cyclicInput.y = -1f;
        if (keyboard.aKey.isPressed) cyclicInput.x = -1f;
        if (keyboard.dKey.isPressed) cyclicInput.x = 1f;

        // Turn - Q/E or Mouse
        yawInput = 0f;
        if (keyboard.qKey.isPressed) yawInput = -1f;
        if (keyboard.rKey.isPressed) yawInput = 1f;

        if (mouse != null)
        {
            float mouseX = mouse.delta.ReadValue().x * 0.1f;
            yawInput += mouseX;
            yawInput = Mathf.Clamp(yawInput, -2f, 2f);
        }
    }

    void ToggleEngine()
    {
        Debug.Log($"[HELI] ToggleEngine() called - engineOn was {engineOn}");
        engineOn = !engineOn;

        if (engineOn)
        {
            if (engineStartSound != null && engineAudio != null)
            {
                engineAudio.PlayOneShot(engineStartSound);
            }
        }

        // Only send RPC if we have a valid network connection
        if (PhotonNetwork.IsConnected && photonView != null && photonView.ViewID != 0)
        {
            photonView.RPC("RPC_SetEngineState", RpcTarget.Others, engineOn);
        }
    }

    [PunRPC]
    void RPC_SetEngineState(bool on)
    {
        engineOn = on;
    }

    // Public method for AI to start engine
    public void StartEngine()
    {
        Debug.Log("[HELI] StartEngine() called");
        if (engineOn) return;

        Debug.Log("[HELI] Starting engine NOW");
        engineOn = true;
        if (engineStartSound != null && engineAudio != null)
        {
            engineAudio.PlayOneShot(engineStartSound);
        }

        // Sync over network
        if (PhotonNetwork.IsConnected && photonView != null && photonView.ViewID != 0)
        {
            photonView.RPC("RPC_SetEngineState", RpcTarget.Others, engineOn);
        }
    }

    void UpdateRotors()
    {
        float targetSpeed = engineOn ? maxRotorSpeed : 0f;
        float spinRate = engineOn ? (maxRotorSpeed / rotorSpinUpTime) : (maxRotorSpeed / (rotorSpinUpTime * 2f));

        currentRotorSpeed = Mathf.MoveTowards(currentRotorSpeed, targetSpeed, spinRate * Time.deltaTime);

        // Rotate main rotor
        if (mainRotor != null)
        {
            mainRotor.Rotate(Vector3.up, currentRotorSpeed * Time.deltaTime, Space.Self);
        }

        // Rotate tail rotor
        if (tailRotor != null)
        {
            tailRotor.Rotate(Vector3.right, currentRotorSpeed * 1.5f * Time.deltaTime, Space.Self);
        }
    }

    void UpdateAudio()
    {
        float normalizedRotorSpeed = currentRotorSpeed / maxRotorSpeed;

        // Rotor sound
        if (rotorAudio != null)
        {
            if (normalizedRotorSpeed > 0.1f && !rotorAudio.isPlaying)
            {
                rotorAudio.Play();
            }
            else if (normalizedRotorSpeed <= 0.1f && rotorAudio.isPlaying)
            {
                rotorAudio.Stop();
            }

            rotorAudio.volume = normalizedRotorSpeed * 0.8f;
            rotorAudio.pitch = 0.5f + normalizedRotorSpeed * 0.5f;
        }

        // Engine sound
        if (engineAudio != null && engineLoopSound != null)
        {
            if (engineOn && !engineAudio.isPlaying)
            {
                engineAudio.Play();
            }
            else if (!engineOn && engineAudio.isPlaying && normalizedRotorSpeed < 0.1f)
            {
                engineAudio.Stop();
            }

            engineAudio.volume = normalizedRotorSpeed * 0.5f;
            engineAudio.pitch = 0.8f + normalizedRotorSpeed * 0.4f;
        }
    }

    void UpdateEffects()
    {
        // Ground dust effect
        bool shouldShowDust = groundDistance < groundEffectHeight && currentRotorSpeed > maxRotorSpeed * 0.5f;

        if (shouldShowDust && activeDustEffect == null && dustEffectPrefab != null)
        {
            Vector3 dustPos = dustSpawnPoint != null ? dustSpawnPoint.position : transform.position - Vector3.up * 2f;
            activeDustEffect = Instantiate(dustEffectPrefab, dustPos, Quaternion.identity);
        }
        else if (!shouldShowDust && activeDustEffect != null)
        {
            Destroy(activeDustEffect);
            activeDustEffect = null;
        }

        // Update dust position
        if (activeDustEffect != null)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, groundEffectHeight + 5f))
            {
                activeDustEffect.transform.position = hit.point;
            }
        }
    }

    void CheckGround()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 100f))
        {
            groundDistance = hit.distance;
            isGrounded = groundDistance < 2f;
        }
        else
        {
            groundDistance = 100f;
            isGrounded = false;
        }
    }

    // For smooth tilting
    private float currentPitch = 0f;
    private float currentRoll = 0f;

    void ApplyFlightPhysics()
    {
        float power = currentRotorSpeed / maxRotorSpeed;  // 0 to 1 based on rotor speed

        // SAFETY: Clamp collective if somehow invalid
        if (float.IsNaN(collectiveInput) || float.IsInfinity(collectiveInput))
        {
            collectiveInput = 0f;
            Debug.LogWarning("[HELI] collectiveInput was NaN/Infinity, reset to 0");
        }

        // Check ground before applying lift
        CheckGround();

        // DEBUG: Log ground state on first few frames
        if (Time.frameCount < 300 && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[HELI GROUND] Y={transform.position.y:F1} groundDist={groundDistance:F1} isGrounded={isGrounded} collective={collectiveInput:F2}");
        }

        // === LIFT ===
        // Gravity is 9.81, we need exactly 9.81 to hover
        // collective: -1 = fall, 0 = hover, +1 = climb
        float gravity = 9.81f;
        float liftForce;

        // CRITICAL: If grounded OR very low, only apply lift if collective is positive (wanting to take off)
        // Otherwise the ground normal force + our lift = launches into sky
        bool nearGround = isGrounded || groundDistance < 5f;
        if (nearGround && collectiveInput <= 0.1f)
        {
            // On/near ground and not trying to take off - no lift needed
            liftForce = 0f;
        }
        else if (collectiveInput > 0.05f)
        {
            // Climbing: add extra lift above hover
            liftForce = gravity + (collectiveInput * 8f);  // At +1: 9.81 + 8 = 17.81
        }
        else if (collectiveInput < -0.05f)
        {
            // Descending: reduce lift below gravity (will fall)
            liftForce = gravity + (collectiveInput * 12f);  // At -1: 9.81 - 12 = -2.19 (falling)
        }
        else
        {
            // Hover: exactly match gravity
            liftForce = gravity;
        }

        // Soft altitude cap - gradually reduce lift above 35m, force descent above 50m
        float altitude = transform.position.y;
        if (altitude > 35f)
        {
            // Gradually reduce lift from 35m to 50m
            float overAltitude = altitude - 35f;
            float capFactor = Mathf.Clamp01(1f - (overAltitude / 15f));  // 1.0 at 35m, 0.0 at 50m
            liftForce = liftForce * capFactor;

            // Above 50m, force downward
            if (altitude > 50f)
            {
                liftForce = -5f;  // Active push down
            }
        }

        // Add vertical velocity damping to prevent overshoot
        float vertVel = rb.linearVelocity.y;
        if (vertVel > 3f)
        {
            // Climbing too fast - reduce lift
            liftForce -= vertVel * 0.5f;
        }
        else if (vertVel < -5f)
        {
            // Falling too fast - increase lift
            liftForce += Mathf.Abs(vertVel) * 0.3f;
        }

        // DEBUG - log every second
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[HELI PHYSICS] Y={altitude:F1} vVel={vertVel:F1} collective={collectiveInput:F2} lift={liftForce:F2} power={power:F2}");
        }

        // Apply lift force (scaled by power)
        rb.AddForce(Vector3.up * liftForce * power, ForceMode.Acceleration);

        // EMERGENCY: If way too high, apply direct gravity bypass regardless of power
        if (altitude > 50f)
        {
            // Force the helicopter down - this bypasses rotor power
            float emergencyForce = -15f;  // Strong downward force
            rb.AddForce(Vector3.up * emergencyForce, ForceMode.Acceleration);
        }

        // === MOVEMENT ===
        // Forward/back and strafe forces
        Vector3 moveForce = Vector3.zero;
        moveForce += transform.forward * cyclicInput.y * movePower;
        moveForce += transform.right * cyclicInput.x * movePower;
        rb.AddForce(moveForce * power, ForceMode.Acceleration);

        // === TILT ===
        // Helicopter tilts in the direction it's moving
        float targetPitch = cyclicInput.y * tiltAmount;   // Nose down when moving forward
        float targetRoll = cyclicInput.x * tiltAmount;    // Tilt right when strafing right

        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.fixedDeltaTime * tiltSpeed);
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.fixedDeltaTime * tiltSpeed);

        // Apply tilt while keeping current yaw
        float currentYaw = transform.eulerAngles.y;
        Quaternion targetRotation = Quaternion.Euler(currentPitch, currentYaw, currentRoll);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * tiltSpeed * 2f);

        // === YAW ===
        // Rotate helicopter left/right
        rb.AddTorque(Vector3.up * yawInput * turnSpeed * power, ForceMode.Acceleration);

        // === SPEED LIMIT ===
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }
    }

    public void TakeDamage(float damage, int attackerViewID)
    {
        if (isDestroyed) return;

        if (PhotonNetwork.IsConnected && photonView != null && photonView.ViewID != 0)
        {
            photonView.RPC("RPC_TakeDamage", RpcTarget.All, damage, attackerViewID);
        }
        else
        {
            // Offline mode
            RPC_TakeDamage(damage, attackerViewID);
        }
    }

    [PunRPC]
    void RPC_TakeDamage(float damage, int attackerViewID)
    {
        if (isDestroyed) return;

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Explode();
        }
    }

    // Crash state
    private bool isCrashing = false;
    private float crashSpinSpeed = 0f;

    void Explode()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        isCrashing = true;

        // Eject all physical seat passengers
        foreach (var seat in seats)
        {
            if (seat != null && seat.IsOccupied)
            {
                seat.ForceExit();
            }
        }

        // Eject all virtual passengers
        EjectAllVirtualPassengers();

        // Clear AI pilot
        if (hasAIPilot)
        {
            ClearAIPilot();
        }

        // Disable engine but keep rotors slowing down
        engineOn = false;

        // Set up crash physics - spin out of control and dive
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearDamping = 0.2f;  // Less drag for faster fall

            // Add random spin and downward force
            crashSpinSpeed = Random.Range(200f, 400f) * (Random.value > 0.5f ? 1f : -1f);
            rb.angularVelocity = new Vector3(
                Random.Range(-2f, 2f),
                Random.Range(-3f, 3f),
                Random.Range(-2f, 2f)
            );

            // Push in a random direction while falling
            Vector3 crashDirection = new Vector3(Random.Range(-1f, 1f), -1f, Random.Range(-1f, 1f)).normalized;
            rb.AddForce(crashDirection * 10f, ForceMode.VelocityChange);
        }

        // Spawn smoke trail effect
        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        // Play explosion sound
        if (explosionSound != null)
        {
            AudioSource.PlayClipAtPoint(explosionSound, transform.position, 1f);
        }

        // Backup destroy after 10 seconds in case it doesn't hit ground
        if (photonView != null && photonView.IsMine)
        {
            Invoke(nameof(DisableHelicopter), 10f);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // If crashing and hit the ground, explode and disappear
        if (isCrashing)
        {
            // Ignore collisions with characters - only crash on terrain/buildings
            if (collision.gameObject.GetComponent<AIController>() != null ||
                collision.gameObject.GetComponent<FPSControllerPhoton>() != null ||
                collision.gameObject.GetComponent<CharacterController>() != null)
            {
                return;
            }

            // Final explosion effect
            if (explosionEffectPrefab != null)
            {
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            }

            // Explosion sound
            if (explosionSound != null)
            {
                AudioSource.PlayClipAtPoint(explosionSound, transform.position, 1f);
            }

            // Destroy immediately
            CancelInvoke(nameof(DisableHelicopter));
            DisableHelicopter();
            return;
        }

        // Ignore all collision damage - only weapon fire damages helicopters
        // This prevents damage from landing, takeoff, AI bumping into helicopter, etc.
    }

    void OnCollisionStay(Collision collision)
    {
        // Push away characters that are colliding with helicopter
        if (isCrashing) return;  // Don't push when crashing

        Rigidbody otherRb = collision.gameObject.GetComponent<Rigidbody>();
        if (otherRb != null)
        {
            Vector3 pushDir = (collision.transform.position - transform.position).normalized;
            pushDir.y = 0;
            otherRb.AddForce(pushDir * 5f, ForceMode.VelocityChange);
        }
    }

    void DisableHelicopter()
    {
        if (PhotonNetwork.IsConnected && photonView.IsMine)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Called when a player enters the pilot seat
    public void OnPilotEnter(FPSControllerPhoton pilot)
    {
        currentPilot = pilot;

        // Recruit nearby friendly AI as door gunners
        RecruitAIGunners(pilot.playerTeam);
    }

    void RecruitAIGunners(Team pilotTeam)
    {
        // Find nearby friendly AI to become gunners
        AIController[] allAI = FindObjectsOfType<AIController>();
        float recruitRange = 20f;

        foreach (var seat in seats)
        {
            if (seat == null || seat.IsOccupied) continue;
            if (seat.seatType != SeatType.DoorGunnerLeft && seat.seatType != SeatType.DoorGunnerRight) continue;

            // Find closest friendly AI
            AIController closestAI = null;
            float closestDist = recruitRange;

            foreach (var ai in allAI)
            {
                if (ai == null || !ai.isAIControlled) continue;
                if (ai.team != pilotTeam) continue;
                if (ai.currentState == AIController.AIState.Dead) continue;
                if (ai.currentState == AIController.AIState.HeliGunner) continue;

                float dist = Vector3.Distance(transform.position, ai.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestAI = ai;
                }
            }

            if (closestAI != null)
            {
                closestAI.EnterHelicopterGunnerSeat(seat);
            }
        }
    }

    // Called when the pilot exits
    public void OnPilotExit()
    {
        currentPilot = null;
        // Reset inputs
        collectiveInput = 0f;
        cyclicInput = Vector2.zero;
        yawInput = 0f;

        // Eject AI gunners when pilot leaves (they shouldn't stay in an unpiloted helicopter)
        EjectAIGunners();
    }

    void EjectAIGunners()
    {
        foreach (var seat in seats)
        {
            if (seat == null) continue;
            if (seat.seatType != SeatType.DoorGunnerLeft && seat.seatType != SeatType.DoorGunnerRight) continue;

            // Check if there's an AI in this seat using the proper tracking
            if (seat.aiOccupant != null)
            {
                seat.aiOccupant.ExitHelicopterSeat();
            }
        }
    }

    // Get available seat for a player
    public HelicopterSeat GetAvailableSeat(Team playerTeam)
    {
        // Check team compatibility
        if (helicopterTeam != Team.None && helicopterTeam != playerTeam)
        {
            return null; // Enemy helicopter
        }

        // Prefer pilot seat if available
        foreach (var seat in seats)
        {
            if (seat != null && !seat.IsOccupied && seat.seatType == SeatType.Pilot)
            {
                return seat;
            }
        }

        // Then any other seat
        foreach (var seat in seats)
        {
            if (seat != null && !seat.IsOccupied)
            {
                return seat;
            }
        }

        return null;
    }

    public HelicopterSeat GetSeatByIndex(int index)
    {
        if (index >= 0 && index < seats.Count)
        {
            return seats[index];
        }
        return null;
    }

    public int GetSeatIndex(HelicopterSeat seat)
    {
        return seats.IndexOf(seat);
    }

    // Virtual passenger system - no physical seat needed
    public bool HasVirtualPassengerSpace()
    {
        virtualPassengers.RemoveAll(p => p == null || p.isDead);
        return virtualPassengers.Count < maxVirtualPassengers;
    }

    public int GetVirtualPassengerCount()
    {
        virtualPassengers.RemoveAll(p => p == null || p.isDead);
        return virtualPassengers.Count;
    }

    public bool AddVirtualPassenger(AIController ai)
    {
        if (ai == null) return false;
        if (virtualPassengers.Contains(ai)) return false;
        if (!HasVirtualPassengerSpace()) return false;

        virtualPassengers.Add(ai);
        return true;
    }

    public void RemoveVirtualPassenger(AIController ai)
    {
        virtualPassengers.Remove(ai);
    }

    public void EjectAllVirtualPassengers()
    {
        // Make a copy to iterate since ExitHelicopterSeat modifies the list
        var passengersToEject = new List<AIController>(virtualPassengers);
        foreach (var passenger in passengersToEject)
        {
            if (passenger != null && !passenger.isDead)
            {
                passenger.ExitHelicopterSeat();
            }
        }
        virtualPassengers.Clear();
    }

    public List<AIController> GetVirtualPassengers()
    {
        virtualPassengers.RemoveAll(p => p == null || p.isDead);
        return virtualPassengers;
    }

    // Photon serialization
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(rb.linearVelocity);
            stream.SendNext(currentRotorSpeed);
            stream.SendNext(currentHealth);
            stream.SendNext(engineOn);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkVelocity = (Vector3)stream.ReceiveNext();
            networkRotorSpeed = (float)stream.ReceiveNext();
            currentHealth = (float)stream.ReceiveNext();
            engineOn = (bool)stream.ReceiveNext();
        }
    }

    [PunRPC]
    void RPC_EnterSeat(int seatIndex, int playerViewID)
    {
        HelicopterSeat seat = GetSeatByIndex(seatIndex);
        if (seat == null) return;

        PhotonView playerView = PhotonView.Find(playerViewID);
        if (playerView == null) return;

        FPSControllerPhoton player = playerView.GetComponent<FPSControllerPhoton>();
        if (player == null) return;

        seat.EnterSeat(player);
    }

    [PunRPC]
    void RPC_ExitSeat(int seatIndex)
    {
        HelicopterSeat seat = GetSeatByIndex(seatIndex);
        if (seat == null) return;

        seat.ExitSeat();
    }

    // ============ AI PILOT SYSTEM ============

    public bool SetAIPilot(AIController ai)
    {
        if (ai == null) return false;
        if (currentPilot != null) return false; // Player is piloting
        if (hasAIPilot) return false; // Already has AI pilot

        // Check team
        if (helicopterTeam != Team.None && helicopterTeam != ai.team) return false;

        Debug.Log($"[HELI] SetAIPilot called - engineOn was {engineOn}");

        aiPilot = ai;
        hasAIPilot = true;
        helicopterTeam = ai.team;

        // FORCE engine OFF - ensures helicopter waits for boarding
        engineOn = false;
        currentRotorSpeed = 0f;

        // Reset all inputs to zero when AI takes over
        collectiveInput = 0f;
        cyclicInput = Vector2.zero;
        yawInput = 0f;

        // Freeze helicopter to prevent physics glitches from AI entering
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;  // Freeze temporarily
        }

        // Unfreeze after a short delay
        StartCoroutine(UnfreezeAfterDelay(0.2f));

        Debug.Log($"[HELI] SetAIPilot complete - engineOn={engineOn}, rotorSpeed={currentRotorSpeed}");

        return true;
    }

    System.Collections.IEnumerator UnfreezeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (rb != null)
        {
            rb.isKinematic = false;
        }
    }

    public void ClearAIPilot()
    {
        aiPilot = null;
        hasAIPilot = false;

        // Reset inputs
        collectiveInput = 0f;
        cyclicInput = Vector2.zero;
        yawInput = 0f;
    }

    public bool HasAIPilot => hasAIPilot;
    public AIController GetAIPilot() => aiPilot;
    public bool HasPilot() => currentPilot != null || hasAIPilot;

    // AI sets flight inputs
    public void SetAIInput(float collective, Vector2 cyclic, float yaw)
    {
        if (!hasAIPilot) return;

        collectiveInput = Mathf.Clamp(collective, -1f, 1f);
        cyclicInput = new Vector2(
            Mathf.Clamp(cyclic.x, -1f, 1f),
            Mathf.Clamp(cyclic.y, -1f, 1f)
        );
        yawInput = Mathf.Clamp(yaw, -2f, 2f);
    }

    // Helper for AI to get flight info
    public float GetAltitude()
    {
        // Just use Y position - simple and reliable
        return transform.position.y;
    }

    public Vector3 GetVelocity()
    {
        return rb != null ? rb.linearVelocity : Vector3.zero;
    }

    public bool IsEngineOn => engineOn;
    public float GetRotorSpeed() => currentRotorSpeed / maxRotorSpeed;
    public bool IsLanded => isGrounded && !isCrashing && !isDestroyed;
    // More permissive check - allow boarding if low enough (within 5m of ground) and engine on
    public bool IsWaitingForPassengers => (isGrounded || groundDistance < 5f) && engineOn && !isCrashing && !isDestroyed;
    public float GroundDistance => groundDistance;

    // Player calling system
    public bool IsBeingCalledByPlayer => isBeingCalledByPlayer;
    public FPSControllerPhoton CallingPlayer => callingPlayer;

    public void CallToPlayer(FPSControllerPhoton player)
    {
        if (player == null) return;
        if (!hasAIPilot) return;
        if (isDestroyed || isCrashing) return;

        callingPlayer = player;
        isBeingCalledByPlayer = true;

        Debug.Log($"[HELICOPTER] {name} called by player, AI pilot will fly to them");
    }

    public void ClearPlayerCall()
    {
        callingPlayer = null;
        isBeingCalledByPlayer = false;
    }

    void OnDrawGizmosSelected()
    {
        // Draw seat positions
        Gizmos.color = Color.green;
        foreach (var seat in seats)
        {
            if (seat != null)
            {
                Gizmos.DrawWireSphere(seat.transform.position, 0.5f);
            }
        }

        // Draw ground check
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * groundEffectHeight);
    }
}
