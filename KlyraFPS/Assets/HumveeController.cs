using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// Humvee controller - Light armored vehicle with mounted gun.
/// Fast, agile, can carry driver + gunner + passengers.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HumveeController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement")]
    public float maxSpeed = 28f;          // Fast!
    public float acceleration = 30f;      // Strong acceleration
    public float brakeForce = 20f;
    public float turnSpeed = 60f;         // Reduced - less spin outs
    public float steerAngle = 30f;        // Max wheel turn angle
    public float downforce = 5f;
    public float obstacleClimbForce = 20f; // Force to climb over stuff
    public float groundCheckDistance = 1.5f;

    [Header("Turret (Mounted Gun)")]
    public Transform turret;              // The gun mount (handles yaw/horizontal rotation)
    public Transform gunBarrel;           // Optional separate barrel (handles pitch/vertical rotation)
    public float turretRotationSpeed = 120f;
    public float turretMinPitch = -15f;
    public float turretMaxPitch = 45f;

    [Header("Mounted Gun")]
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletSpeed = 200f;
    public float bulletDamage = 25f;
    public float fireRate = 0.1f;         // Rapid fire!
    public int magazineSize = 100;
    public float reloadTime = 3f;
    public AudioClip fireSound;
    public AudioClip reloadSound;

    [Header("Muzzle Flash")]
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashDuration = 0.05f;

    [Header("Engine Audio")]
    public AudioClip engineIdleSound;
    public AudioClip engineMovingSound;
    public float idleVolume = 0.4f;
    public float movingVolume = 0.6f;
    public float enginePitchMin = 0.8f;
    public float enginePitchMax = 1.4f;

    [Header("Health")]
    public float maxHealth = 250f;        // Less than tank
    public float currentHealth;
    public bool isDestroyed = false;
    public GameObject destroyedPrefab;
    public GameObject explosionPrefab;

    [Header("Team")]
    public Team humveeTeam = Team.None;

    [Header("Wheels")]
    public Transform[] wheels;            // Visual wheel transforms
    public float wheelRadius = 0.4f;
    public Transform frontLeftWheel;
    public Transform frontRightWheel;

    [Header("Seats")]
    public Transform driverSeat;
    public Transform gunnerSeat;
    public Transform[] passengerSeats;
    public int maxPassengers = 2;

    [Header("Unstuck")]
    public float unstuckHoldTime = 2f;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float unstuckHoldTimer = 0f;
    private bool showUnstuckUI = false;

    // Components
    private Rigidbody rb;
    private AudioSource audioSource;
    private AudioSource engineAudioSource;
    private bool isMoving = false;

    // State
    private float currentMoveInput = 0f;
    private float currentTurnInput = 0f;
    private float targetTurretYaw = 0f;
    private float targetTurretPitch = 0f;
    private float currentTurretYaw = 0f;
    private float currentTurretPitch = 0f;
    private float lastFireTime = 0f;
    private int currentAmmo;
    private bool isReloading = false;
    private float reloadTimer = 0f;

    // Driver
    private bool hasPlayerDriver = false;
    private FPSControllerPhoton playerDriver;
    private Camera playerCamera;

    // Gunner
    private bool hasPlayerGunner = false;
    private FPSControllerPhoton playerGunner;

    // AI
    private bool hasAIDriver = false;
    private AIController aiDriver;
    private bool hasAIGunner = false;
    private AIController aiGunner;

    // Passengers
    private List<AIController> passengers = new List<AIController>();

    // Network
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private float networkTurretYaw;
    private float networkTurretPitch;
    private bool networkInitialized = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 5000f;  // ~5 tons - heavy to push through stuff
        rb.linearDamping = 0.3f;  // Some drag for control
        rb.angularDamping = 5f;   // High - resist spinning out
        rb.centerOfMass = new Vector3(0f, -0.4f, 0f);  // Low center = stable
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.None; // Ensure no movement constraints

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        engineAudioSource = gameObject.AddComponent<AudioSource>();
        engineAudioSource.loop = true;
        engineAudioSource.spatialBlend = 1f;
        engineAudioSource.maxDistance = 80f;
        engineAudioSource.rolloffMode = AudioRolloffMode.Linear;
        engineAudioSource.playOnAwake = false;

        currentHealth = maxHealth;
        currentAmmo = magazineSize;

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        // Auto-find turret
        if (turret == null)
            turret = transform.Find("Turret");
        if (turret == null)
            turret = transform.Find("GunMount");

        // Auto-find wheels
        if (wheels == null || wheels.Length == 0)
            FindWheels();

        Debug.Log($"[Humvee] Awake - mass: {rb.mass}, wheels: {wheels?.Length ?? 0}");
    }

    void Start()
    {
        // Double-check rigidbody settings
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.None;
            Debug.Log($"[Humvee] Start - rb.isKinematic={rb.isKinematic}, constraints={rb.constraints}");
        }
    }

    void FindWheels()
    {
        var wheelList = new List<Transform>();
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            string name = child.name.ToLower();
            if (name.Contains("wheel") || name.Contains("tire") || name.Contains("tyre"))
            {
                wheelList.Add(child);

                // Auto-assign front wheels for steering
                if (name.Contains("front") && name.Contains("left"))
                    frontLeftWheel = child;
                else if (name.Contains("front") && name.Contains("right"))
                    frontRightWheel = child;
            }
        }
        wheels = wheelList.ToArray();
    }

    void FixedUpdate()
    {
        if (isDestroyed) return;

        // Simplified: Always simulate locally unless in a networked game where we're not the owner
        bool shouldSimulate = true;

        // Only restrict simulation in actual networked games
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && photonView != null)
        {
            // In networked game: only owner or master client (for AI) simulates
            shouldSimulate = photonView.IsMine || (hasAIDriver && PhotonNetwork.IsMasterClient);
        }

        if (!shouldSimulate) return;

        UpdateMovement();
    }

    void Update()
    {
        if (isDestroyed) return;

        // Network interpolation
        if (PhotonNetwork.IsConnected && photonView != null && !photonView.IsMine && networkInitialized)
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);
            currentTurretYaw = Mathf.LerpAngle(currentTurretYaw, networkTurretYaw, Time.deltaTime * 10f);
            currentTurretPitch = Mathf.Lerp(currentTurretPitch, networkTurretPitch, Time.deltaTime * 10f);
            UpdateTurretVisuals();
            return;
        }

        // Player driver input
        if (hasPlayerDriver)
        {
            HandlePlayerDriverInput();
        }

        // Player gunner input
        if (hasPlayerGunner)
        {
            HandlePlayerGunnerInput();
        }

        // Reload timer
        if (isReloading)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f)
            {
                currentAmmo = magazineSize;
                isReloading = false;
                Debug.Log("[Humvee] Reloaded!");
            }
        }

        UpdateTurret();
        UpdateEngineAudio();
        UpdateWheelVisuals();
    }

    void UpdateMovement()
    {
        // Ensure rigidbody is not kinematic
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
        }

        // Check if grounded
        bool isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, groundCheckDistance + 0.5f);

        // Stabilization - keep vehicle level
        float pitchAngle = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized);
        if (transform.forward.y < -0.1f)  // Nose diving
        {
            rb.AddTorque(-transform.right * 10f, ForceMode.Acceleration);
        }
        else if (transform.forward.y > 0.1f)  // Nose up
        {
            rb.AddTorque(transform.right * 5f, ForceMode.Acceleration);
        }

        // Light downforce at center
        float speedFactor = 1f + (rb.linearVelocity.magnitude / maxSpeed);
        rb.AddForce(Vector3.down * downforce * speedFactor * 0.5f, ForceMode.Acceleration);

        // Forward/backward acceleration
        Vector3 moveForce = transform.forward * currentMoveInput * acceleration;
        rb.AddForce(moveForce, ForceMode.Acceleration);

        // Obstacle detection - check if something is blocking us
        if (Mathf.Abs(currentMoveInput) > 0.1f)
        {
            Vector3 checkDir = currentMoveInput > 0 ? transform.forward : -transform.forward;
            Vector3 checkStart = transform.position + Vector3.up * 0.4f;

            // Check for obstacle at bumper height
            if (Physics.Raycast(checkStart, checkDir, out RaycastHit hit, 3f))
            {
                // Don't try to climb buildings/walls - everything else is fair game!
                string hitName = hit.collider.name.ToLower();
                bool isBuilding = hitName.Contains("building") || hitName.Contains("wall") ||
                                  hitName.Contains("house") || hitName.Contains("structure");

                if (!isBuilding)
                {
                    // Check how high the obstacle is
                    float obstacleHeight = hit.point.y - transform.position.y;

                    // Climb over anything under 2m tall!
                    if (obstacleHeight < 2f && obstacleHeight > 0.05f)
                    {
                        // Strong upward + forward force to climb
                        Vector3 climbForce = (Vector3.up * 0.8f + checkDir * 0.4f) * obstacleClimbForce;
                        rb.AddForce(climbForce, ForceMode.Acceleration);
                    }

                    // Push through anything - add extra forward force
                    rb.AddForce(checkDir * acceleration * 0.5f, ForceMode.Acceleration);
                }
            }

            // Extra push if we're going slow but trying to move
            float currentSpeed = rb.linearVelocity.magnitude;
            if (currentSpeed < 3f && Mathf.Abs(currentMoveInput) > 0.5f)
            {
                // Strong boost to push through
                rb.AddForce(transform.forward * currentMoveInput * acceleration * 2f, ForceMode.Acceleration);
                rb.AddForce(Vector3.up * 5f, ForceMode.Acceleration);
            }
        }

        // Debug log
        if (Time.frameCount % 120 == 0 && (hasAIDriver || hasPlayerDriver))
        {
            Debug.Log($"[Humvee] Movement: input={currentMoveInput:F2}, turn={currentTurnInput:F2}, velocity={rb.linearVelocity.magnitude:F2}, isKinematic={rb.isKinematic}");
        }

        // Braking when no input
        if (Mathf.Abs(currentMoveInput) < 0.1f && rb.linearVelocity.magnitude > 0.5f)
        {
            Vector3 brakeDir = -rb.linearVelocity.normalized;
            rb.AddForce(brakeDir * brakeForce, ForceMode.Acceleration);
        }

        // Turning - works even at low speed for getting unstuck
        if (Mathf.Abs(currentTurnInput) > 0.1f)
        {
            float speedRatio = Mathf.Clamp01(rb.linearVelocity.magnitude / 3f);  // Turn at lower speeds
            speedRatio = Mathf.Max(speedRatio, 0.3f);  // Minimum turn ability
            float actualTurnSpeed = turnSpeed * speedRatio;

            // Reverse steering when going backward
            float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
            if (forwardSpeed < -0.5f)
                actualTurnSpeed *= -1f;

            float turnAmount = currentTurnInput * actualTurnSpeed * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        // Speed limit
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (flatVel.magnitude > maxSpeed)
        {
            flatVel = flatVel.normalized * maxSpeed;
            rb.linearVelocity = new Vector3(flatVel.x, rb.linearVelocity.y, flatVel.z);
        }

        // Auto-flip if upside down
        if (Vector3.Dot(transform.up, Vector3.up) < 0.3f && isGrounded)
        {
            rb.AddTorque(transform.forward * 20f, ForceMode.Acceleration);
        }
    }

    void UpdateTurret()
    {
        // Smooth turret rotation
        currentTurretYaw = Mathf.MoveTowardsAngle(currentTurretYaw, targetTurretYaw, turretRotationSpeed * Time.deltaTime);
        currentTurretPitch = Mathf.MoveTowards(currentTurretPitch, targetTurretPitch, turretRotationSpeed * Time.deltaTime);
        currentTurretPitch = Mathf.Clamp(currentTurretPitch, turretMinPitch, turretMaxPitch);

        UpdateTurretVisuals();
    }

    void UpdateTurretVisuals()
    {
        if (turret != null)
        {
            // If we have a separate gun barrel, turret only handles yaw
            if (gunBarrel != null)
            {
                turret.localRotation = Quaternion.Euler(0f, currentTurretYaw, 0f);
                gunBarrel.localRotation = Quaternion.Euler(-currentTurretPitch, 0f, 0f);
            }
            else
            {
                // Single turret handles both yaw and pitch
                turret.localRotation = Quaternion.Euler(-currentTurretPitch, currentTurretYaw, 0f);
            }
        }
    }

    void UpdateWheelVisuals()
    {
        if (wheels == null) return;

        float speed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float rotationAngle = (speed / wheelRadius) * Mathf.Rad2Deg * Time.deltaTime;

        foreach (Transform wheel in wheels)
        {
            if (wheel == null) continue;

            // Rotate wheel around its axis
            wheel.Rotate(Vector3.right, rotationAngle, Space.Self);
        }

        // Steer front wheels
        float steerAmount = currentTurnInput * steerAngle;
        if (frontLeftWheel != null)
        {
            Vector3 euler = frontLeftWheel.localEulerAngles;
            frontLeftWheel.localRotation = Quaternion.Euler(euler.x, steerAmount, euler.z);
        }
        if (frontRightWheel != null)
        {
            Vector3 euler = frontRightWheel.localEulerAngles;
            frontRightWheel.localRotation = Quaternion.Euler(euler.x, steerAmount, euler.z);
        }
    }

    void UpdateEngineAudio()
    {
        if (engineAudioSource == null) return;

        bool shouldPlayEngine = HasDriver && !isDestroyed;

        if (!shouldPlayEngine)
        {
            if (engineAudioSource.isPlaying)
                engineAudioSource.Stop();
            return;
        }

        bool wasMoving = isMoving;
        isMoving = Mathf.Abs(currentMoveInput) > 0.1f || rb.linearVelocity.magnitude > 1f;

        if (isMoving != wasMoving || !engineAudioSource.isPlaying)
        {
            if (isMoving && engineMovingSound != null)
            {
                engineAudioSource.clip = engineMovingSound;
                engineAudioSource.volume = movingVolume;
            }
            else if (!isMoving && engineIdleSound != null)
            {
                engineAudioSource.clip = engineIdleSound;
                engineAudioSource.volume = idleVolume;
            }

            if (engineAudioSource.clip != null && !engineAudioSource.isPlaying)
                engineAudioSource.Play();
        }

        float speedRatio = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed);
        float targetPitch = Mathf.Lerp(enginePitchMin, enginePitchMax, speedRatio);
        engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * 3f);
    }

    void HandlePlayerDriverInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Movement
        currentMoveInput = 0f;
        if (keyboard.wKey.isPressed) currentMoveInput = 1f;
        else if (keyboard.sKey.isPressed) currentMoveInput = -1f;

        // Turning
        currentTurnInput = 0f;
        if (keyboard.dKey.isPressed) currentTurnInput = 1f;
        else if (keyboard.aKey.isPressed) currentTurnInput = -1f;

        // Unstuck
        if (keyboard.hKey.isPressed)
        {
            unstuckHoldTimer += Time.deltaTime;
            showUnstuckUI = true;

            if (unstuckHoldTimer >= unstuckHoldTime)
            {
                UnstuckVehicle();
                unstuckHoldTimer = 0f;
                showUnstuckUI = false;
            }
        }
        else
        {
            unstuckHoldTimer = 0f;
            showUnstuckUI = false;
        }
    }

    void HandlePlayerGunnerInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Turret follows mouse
        if (playerCamera != null)
        {
            Ray ray = playerCamera.ScreenPointToRay(mouse.position.ReadValue());

            // Raycast to find where we're aiming
            if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            {
                Vector3 aimPoint = hit.point;
                Vector3 localAim = transform.InverseTransformPoint(aimPoint);
                targetTurretYaw = Mathf.Atan2(localAim.x, localAim.z) * Mathf.Rad2Deg;

                // Calculate pitch
                if (turret != null)
                {
                    Vector3 turretToTarget = aimPoint - turret.position;
                    float horizontalDist = new Vector2(turretToTarget.x, turretToTarget.z).magnitude;
                    targetTurretPitch = Mathf.Atan2(turretToTarget.y, horizontalDist) * Mathf.Rad2Deg;
                }
            }
        }

        // Fire
        if (mouse.leftButton.isPressed)
        {
            TryFire();
        }

        // Manual reload
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && !isReloading)
        {
            StartReload();
        }
    }

    void UnstuckVehicle()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = spawnPosition + Vector3.up * 1f;
        transform.rotation = spawnRotation;
        Debug.Log("[Humvee] Unstuck - respawned at spawn point");
    }

    void OnGUI()
    {
        if (!hasPlayerDriver && !hasPlayerGunner) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 14;
        style.fontStyle = FontStyle.Bold;

        // Show ammo for gunner
        if (hasPlayerGunner)
        {
            GUI.color = Color.white;
            string ammoText = isReloading ? "RELOADING..." : $"AMMO: {currentAmmo}/{magazineSize}";
            GUI.Label(new Rect(Screen.width - 200, Screen.height - 50, 180, 30), ammoText, style);
        }

        // Unstuck hint
        if (hasPlayerDriver)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(0, Screen.height - 30, Screen.width, 25), "Hold H to Unstuck", style);

            if (showUnstuckUI)
            {
                float progress = unstuckHoldTimer / unstuckHoldTime;
                float barWidth = 250f;
                float barHeight = 30f;
                float x = (Screen.width - barWidth) / 2f;
                float y = Screen.height * 0.6f;

                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(new Rect(x - 10, y - 35, barWidth + 20, barHeight + 50), Texture2D.whiteTexture);

                GUI.color = Color.yellow;
                style.fontSize = 18;
                GUI.Label(new Rect(x, y - 30, barWidth, 25), "UNSTUCKING...", style);

                GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), Texture2D.whiteTexture);

                GUI.color = Color.Lerp(Color.red, Color.green, progress);
                GUI.DrawTexture(new Rect(x, y, barWidth * progress, barHeight), Texture2D.whiteTexture);

                GUI.color = Color.white;
                style.fontSize = 16;
                GUI.Label(new Rect(x, y, barWidth, barHeight), $"{(int)(progress * 100)}%", style);
            }
        }

        GUI.color = Color.white;
    }

    void TryFire()
    {
        if (isReloading) return;
        if (currentAmmo <= 0)
        {
            StartReload();
            return;
        }
        if (Time.time - lastFireTime < fireRate) return;

        lastFireTime = Time.time;
        Fire();
    }

    void Fire()
    {
        currentAmmo--;

        Vector3 spawnPos = firePoint != null ? firePoint.position : (turret != null ? turret.position + turret.forward * 1.5f : transform.position + transform.forward * 2f);
        Quaternion spawnRot = firePoint != null ? firePoint.rotation : (turret != null ? turret.rotation : transform.rotation);

        // Spawn bullet
        if (bulletPrefab != null)
        {
            GameObject bullet = Instantiate(bulletPrefab, spawnPos, spawnRot);
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.linearVelocity = spawnRot * Vector3.forward * bulletSpeed;
            }

            // Set damage info
            var tankShell = bullet.GetComponent<TankShell>();
            if (tankShell != null)
            {
                tankShell.damage = bulletDamage;
                tankShell.ownerTeam = humveeTeam;
            }

            Destroy(bullet, 5f);
        }

        // Sound
        if (fireSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(fireSound, 0.5f);
        }

        // Muzzle flash
        SpawnMuzzleFlash(spawnPos, spawnRot);
    }

    void StartReload()
    {
        if (isReloading) return;
        isReloading = true;
        reloadTimer = reloadTime;

        if (reloadSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(reloadSound);
        }
    }

    void SpawnMuzzleFlash(Vector3 position, Quaternion rotation)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, position, rotation);
            if (firePoint != null) flash.transform.SetParent(firePoint);
            Destroy(flash, muzzleFlashDuration + 0.3f);
        }
        else
        {
            // Simple procedural flash
            GameObject flashObj = new GameObject("MuzzleFlash");
            flashObj.transform.position = position;
            Light flashLight = flashObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.8f, 0.3f);
            flashLight.intensity = 3f;
            flashLight.range = 8f;
            Destroy(flashObj, muzzleFlashDuration);
        }
    }

    void LateUpdate()
    {
        // Camera follow for gunner
        if (hasPlayerGunner && playerCamera != null)
        {
            Vector3 targetPos = (turret != null ? turret.position : transform.position) - transform.forward * 8f + Vector3.up * 5f;
            playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 5f);
            playerCamera.transform.LookAt(turret != null ? turret.position : transform.position + Vector3.up * 2f);
        }
        // Camera follow for driver (if no gunner)
        else if (hasPlayerDriver && playerCamera != null)
        {
            Vector3 targetPos = transform.position - transform.forward * 10f + Vector3.up * 6f;
            playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 5f);
            playerCamera.transform.LookAt(transform.position + Vector3.up * 1.5f);
        }
    }

    // === PUBLIC API ===

    public void SetPlayerDriver(FPSControllerPhoton player)
    {
        playerDriver = player;
        hasPlayerDriver = player != null;
        if (hasPlayerDriver)
        {
            humveeTeam = player.playerTeam;
            if (playerCamera == null) SetupPlayerCamera();
            StartEngineAudio();
        }
    }

    public void ClearPlayerDriver()
    {
        playerDriver = null;
        hasPlayerDriver = false;
        currentMoveInput = 0f;
        currentTurnInput = 0f;
        if (!HasDriver) StopEngineAudio();
    }

    public void SetPlayerGunner(FPSControllerPhoton player)
    {
        playerGunner = player;
        hasPlayerGunner = player != null;
        if (hasPlayerGunner)
        {
            humveeTeam = player.playerTeam;
            if (playerCamera == null) SetupPlayerCamera();
        }
    }

    public void ClearPlayerGunner()
    {
        playerGunner = null;
        hasPlayerGunner = false;
    }

    void SetupPlayerCamera()
    {
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            GameObject camObj = new GameObject("HumveeCamera");
            playerCamera = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }
        playerCamera.transform.SetParent(null);
    }

    void StartEngineAudio()
    {
        if (engineAudioSource == null) return;
        if (engineIdleSound != null)
        {
            engineAudioSource.clip = engineIdleSound;
            engineAudioSource.volume = idleVolume;
            engineAudioSource.pitch = enginePitchMin;
            engineAudioSource.Play();
        }
    }

    void StopEngineAudio()
    {
        if (engineAudioSource != null && engineAudioSource.isPlaying)
            engineAudioSource.Stop();
    }

    public void SetAIDriver(AIController ai)
    {
        aiDriver = ai;
        hasAIDriver = ai != null;
        if (hasAIDriver)
        {
            humveeTeam = ai.team;
            StartEngineAudio();
        }
    }

    public void ClearAIDriver()
    {
        aiDriver = null;
        hasAIDriver = false;
        if (!HasDriver) StopEngineAudio();
    }

    public void SetAIGunner(AIController ai)
    {
        aiGunner = ai;
        hasAIGunner = ai != null;
        if (hasAIGunner && humveeTeam == Team.None)
            humveeTeam = ai.team;
    }

    public void ClearAIGunner()
    {
        aiGunner = null;
        hasAIGunner = false;
    }

    /// <summary>
    /// AI input for driving
    /// </summary>
    public void SetAIDriveInput(float move, float turn)
    {
        currentMoveInput = Mathf.Clamp(move, -1f, 1f);
        currentTurnInput = Mathf.Clamp(turn, -1f, 1f);
    }

    /// <summary>
    /// AI input for turret
    /// </summary>
    public void SetAITurretInput(float yaw, float pitch, bool fire)
    {
        targetTurretYaw = yaw;
        targetTurretPitch = pitch;

        if (fire)
            TryFire();
    }

    /// <summary>
    /// Combined AI input
    /// </summary>
    public void SetAIInput(float move, float turn, float turretYaw, float turretPitch, bool fire)
    {
        SetAIDriveInput(move, turn);
        SetAITurretInput(turretYaw, turretPitch, fire);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker)
    {
        if (isDestroyed) return;
        currentHealth -= damage;
        Debug.Log($"[Humvee] Took {damage} damage, health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        StopEngineAudio();

        // Eject AI crew (player humvee driving not yet implemented)
        if (hasAIDriver && aiDriver != null)
            aiDriver.ExitVehicle();
        if (hasAIGunner && aiGunner != null)
            aiGunner.ExitVehicle();

        EjectAllPassengers();

        // Explosion
        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }

        // Spawn destroyed version
        if (destroyedPrefab != null)
        {
            Instantiate(destroyedPrefab, transform.position, transform.rotation);
        }

        Destroy(gameObject, 0.1f);
    }

    // === PASSENGERS ===

    public bool HasPassengerSpace()
    {
        passengers.RemoveAll(p => p == null || p.isDead);
        return passengers.Count < maxPassengers;
    }

    public bool AddPassenger(AIController ai)
    {
        if (ai == null || !HasPassengerSpace()) return false;
        if (passengers.Contains(ai)) return false;

        passengers.Add(ai);

        // Position in seat
        int seatIndex = passengers.Count - 1;
        if (passengerSeats != null && seatIndex < passengerSeats.Length && passengerSeats[seatIndex] != null)
        {
            ai.transform.SetParent(passengerSeats[seatIndex]);
            ai.transform.localPosition = Vector3.zero;
            ai.transform.localRotation = Quaternion.identity;
        }
        else
        {
            ai.transform.SetParent(transform);
            ai.transform.localPosition = new Vector3(seatIndex == 0 ? -0.8f : 0.8f, 1.5f, -1f);
        }

        Debug.Log($"[Humvee] Added passenger {ai.name}");
        return true;
    }

    public void RemovePassenger(AIController ai)
    {
        if (ai == null) return;
        passengers.Remove(ai);
        ai.transform.SetParent(null);
    }

    public void EjectAllPassengers()
    {
        var toEject = new List<AIController>(passengers);
        foreach (var p in toEject)
        {
            if (p != null)
            {
                p.transform.SetParent(null);
                Vector3 exitPos = transform.position + Random.onUnitSphere * 3f;
                exitPos.y = transform.position.y + 1f;
                p.transform.position = exitPos;

                var agent = p.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null)
                {
                    agent.enabled = true;
                    agent.Warp(exitPos);
                }
            }
        }
        passengers.Clear();
    }

    // === PROPERTIES ===

    public bool HasDriver => hasPlayerDriver || hasAIDriver;
    public bool HasGunner => hasPlayerGunner || hasAIGunner;
    public Team HumveeTeam => humveeTeam;
    public float CurrentSpeed => rb.linearVelocity.magnitude;
    public Vector3 TurretForward => turret != null ? turret.forward : transform.forward;
    public int CurrentAmmo => currentAmmo;
    public bool IsReloading => isReloading;

    // Navigation
    private HumveeNavigation _navigation;
    public HumveeNavigation Navigation
    {
        get
        {
            if (_navigation == null)
                _navigation = GetComponent<HumveeNavigation>();
            return _navigation;
        }
    }
    public bool HasNavigation => Navigation != null;

    // === NETWORK ===

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(currentTurretYaw);
            stream.SendNext(currentTurretPitch);
            stream.SendNext(currentHealth);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkTurretYaw = (float)stream.ReceiveNext();
            networkTurretPitch = (float)stream.ReceiveNext();
            currentHealth = (float)stream.ReceiveNext();
            networkInitialized = true;
        }
    }
}
