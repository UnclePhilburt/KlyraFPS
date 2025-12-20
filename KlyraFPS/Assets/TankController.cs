using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

[RequireComponent(typeof(Rigidbody))]
public class TankController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement")]
    public float moveSpeed = 8f;
    public float turnSpeed = 40f;
    public float acceleration = 5f;
    public float downforce = 5f;  // Extra gravity to keep grounded

    [Header("Turret")]
    public Transform turret;
    public Transform barrel;
    public float turretRotationSpeed = 60f;
    public float barrelMinAngle = -10f;
    public float barrelMaxAngle = 20f;

    [Header("Main Cannon")]
    public Transform firePoint;
    public GameObject shellPrefab;
    public GameObject explosionPrefab;  // Assign your explosion prefab here
    public float shellSpeed = 80f;
    public float shellDamage = 150f;
    public float explosionRadius = 8f;
    public float fireRate = 3f; // seconds between shots
    public AudioClip fireSound;

    [Header("Muzzle Flash")]
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashDuration = 0.1f;
    public float muzzleFlashSize = 3f;
    public Color muzzleFlashColor = new Color(1f, 0.8f, 0.3f);

    [Header("Engine Audio")]
    public AudioClip engineIdleSound;
    public AudioClip engineMovingSound;
    public float idleVolume = 0.3f;
    public float movingVolume = 0.5f;
    public float enginePitchMin = 0.8f;
    public float enginePitchMax = 1.2f;

    [Header("Health")]
    public float maxHealth = 500f;
    public float currentHealth;
    public bool isDestroyed = false;
    public GameObject destroyedPrefab;

    [Header("Team")]
    public Team tankTeam = Team.None;

    [Header("Obstacle Crushing")]
    [Tooltip("Maximum height of obstacles the tank can run over")]
    public float crushableMaxHeight = 1.5f;
    [Tooltip("Maximum mass of objects the tank can push (kg)")]
    public float pushableMaxMass = 500f;
    [Tooltip("Force applied to pushable objects")]
    public float pushForce = 50000f;
    [Tooltip("Damage dealt to destructible objects on collision")]
    public float crushDamage = 200f;

    [Header("Tracks & Wheels")]
    [Tooltip("Wheel transforms to rotate (auto-found if empty)")]
    public Transform[] wheels;
    [Tooltip("Track renderers for UV scrolling")]
    public Renderer[] trackRenderers;
    public float wheelRotationSpeed = 200f;
    public float trackScrollSpeed = 2f;

    [Header("References")]
    public Transform driverSeat;

    [Header("Unstuck")]
    public float unstuckHoldTime = 2f;  // Seconds to hold F
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float unstuckHoldTimer = 0f;
    private bool showUnstuckUI = false;

    [Header("Virtual Passengers")]
    public int maxVirtualPassengers = 4;  // Infantry riding on tank
    public Transform[] passengerSeats;    // Optional seat positions
    private System.Collections.Generic.List<AIController> virtualPassengers = new System.Collections.Generic.List<AIController>();

    // Components
    private Rigidbody rb;
    private AudioSource audioSource;      // For cannon shots
    private AudioSource engineAudioSource; // For engine loop
    private bool isMoving = false;

    // State
    private float currentMoveInput = 0f;
    private float currentTurnInput = 0f;
    private float targetTurretAngle = 0f;
    private float targetBarrelAngle = 0f;
    private float lastFireTime = 0f;

    // Player
    private bool hasPlayerDriver = false;
    private FPSControllerPhoton playerDriver;
    private Camera playerCamera;

    // AI
    private bool hasAIDriver = false;
    private AIController aiDriver;

    // Navigation
    private TankNavigation navigation;
    public TankNavigation Navigation => navigation;
    public bool HasNavigation => navigation != null;

    // Network
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private float networkTurretAngle;
    private bool networkInitialized = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.mass = 40000f; // 40 tons
        rb.linearDamping = 1f;
        rb.angularDamping = 5f;
        rb.centerOfMass = new Vector3(0f, -0.5f, 0f); // Low center of mass
        rb.isKinematic = false;  // Ensure physics works
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Setup engine audio source
        engineAudioSource = gameObject.AddComponent<AudioSource>();
        engineAudioSource.loop = true;
        engineAudioSource.spatialBlend = 1f; // 3D sound
        engineAudioSource.maxDistance = 100f;
        engineAudioSource.rolloffMode = AudioRolloffMode.Linear;
        engineAudioSource.playOnAwake = false;

        currentHealth = maxHealth;

        // Save spawn position for unstuck
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        // Get or create navigation component
        navigation = GetComponent<TankNavigation>();
        if (navigation == null)
        {
            navigation = gameObject.AddComponent<TankNavigation>();
        }

        // Auto-find turret if not assigned
        if (turret == null)
            turret = transform.Find("SM_Veh_Tank_USA_Turret_01");
        if (barrel == null && turret != null)
            barrel = turret.Find("SM_Veh_Tank_USA_Turret_Barrel_01");

        // Also try Russian tank parts
        if (turret == null)
            turret = transform.Find("SM_Veh_Tank_Russian_Turret_01");
        if (barrel == null && turret != null)
            barrel = turret.Find("SM_Veh_Tank_Russian_Turret_Barrel_01");

        // Auto-find wheels if not assigned
        if (wheels == null || wheels.Length == 0)
        {
            FindWheels();
        }

        // Auto-find track renderers if not assigned
        if (trackRenderers == null || trackRenderers.Length == 0)
        {
            FindTrackRenderers();
        }

        Debug.Log($"[Tank] Awake - Rigidbody kinematic: {rb.isKinematic}, mass: {rb.mass}, wheels: {wheels?.Length ?? 0}, tracks: {trackRenderers?.Length ?? 0}");
    }

    void FindWheels()
    {
        // Find all wheel transforms (look for "wheel" in name)
        var wheelList = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            string name = child.name.ToLower();
            if (name.Contains("wheel") || name.Contains("sprocket") || name.Contains("idler"))
            {
                wheelList.Add(child);
            }
        }
        wheels = wheelList.ToArray();
    }

    void FindTrackRenderers()
    {
        // Find track renderers
        var trackList = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            string name = r.gameObject.name.ToLower();
            if (name.Contains("track") || name.Contains("tread"))
            {
                trackList.Add(r);
            }
        }
        trackRenderers = trackList.ToArray();
    }

    void FixedUpdate()
    {
        if (isDestroyed) return;

        bool shouldSimulate = !PhotonNetwork.IsConnected ||
                              !PhotonNetwork.InRoom ||
                              photonView == null ||
                              photonView.IsMine ||
                              (hasAIDriver && PhotonNetwork.IsMasterClient);

        // Debug simulation state
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[Tank] FixedUpdate - shouldSimulate: {shouldSimulate}, Connected: {PhotonNetwork.IsConnected}, InRoom: {PhotonNetwork.InRoom}, hasAI: {hasAIDriver}, IsMaster: {PhotonNetwork.IsMasterClient}");
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
            if (turret != null)
            {
                turret.localRotation = Quaternion.Lerp(turret.localRotation,
                    Quaternion.Euler(0f, networkTurretAngle, 0f), Time.deltaTime * 10f);
            }
            return;
        }

        // Player controls
        if (hasPlayerDriver)
        {
            HandlePlayerInput();
        }

        UpdateTurret();
        UpdateEngineAudio();
        UpdateTracksAndWheels();
    }

    void UpdateTracksAndWheels()
    {
        // Get movement speed (negative if reversing)
        float speed = Vector3.Dot(rb.linearVelocity, transform.forward);

        // Also factor in turning (one side faster than other)
        float turnFactor = currentTurnInput * 0.5f;

        // Rotate wheels
        if (wheels != null)
        {
            foreach (Transform wheel in wheels)
            {
                if (wheel == null) continue;

                // Determine if left or right wheel based on local position
                float sideFactor = wheel.localPosition.x > 0 ? 1f : -1f;
                float wheelSpeed = speed + (turnFactor * sideFactor * moveSpeed);

                // Rotate around local X axis
                wheel.Rotate(Vector3.right, wheelSpeed * wheelRotationSpeed * Time.deltaTime, Space.Self);
            }
        }

        // Scroll track UVs (if material supports it)
        if (trackRenderers != null)
        {
            foreach (Renderer trackRenderer in trackRenderers)
            {
                if (trackRenderer == null) continue;

                // Determine if left or right track
                float sideFactor = trackRenderer.transform.localPosition.x > 0 ? 1f : -1f;
                float trackSpeed = speed + (turnFactor * sideFactor * moveSpeed);

                // Scroll the texture offset (only if material has _MainTex)
                Material mat = trackRenderer.material;
                if (mat != null && mat.HasProperty("_MainTex"))
                {
                    Vector2 offset = mat.mainTextureOffset;
                    offset.y += trackSpeed * trackScrollSpeed * Time.deltaTime;
                    mat.mainTextureOffset = offset;
                }
            }
        }
    }

    void UpdateMovement()
    {
        // Apply extra downforce to keep tank grounded
        // Increases with speed to prevent ramping off hills
        float speedFactor = 1f + (rb.linearVelocity.magnitude / moveSpeed);
        rb.AddForce(Vector3.down * downforce * speedFactor, ForceMode.Acceleration);

        // Forward/backward - use stronger force for heavy tank
        float forceMultiplier = 5f;  // Multiplier for tank acceleration
        Vector3 moveForce = transform.forward * currentMoveInput * moveSpeed * forceMultiplier;
        rb.AddForce(moveForce, ForceMode.Acceleration);

        // Turning (only when moving or with input)
        if (Mathf.Abs(currentTurnInput) > 0.1f)
        {
            float turnAmount = currentTurnInput * turnSpeed * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        // Limit speed
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (flatVel.magnitude > moveSpeed)
        {
            flatVel = flatVel.normalized * moveSpeed;
            rb.linearVelocity = new Vector3(flatVel.x, rb.linearVelocity.y, flatVel.z);
        }

        // Debug movement
        if (Time.frameCount % 60 == 0 && (Mathf.Abs(currentMoveInput) > 0.01f || Mathf.Abs(currentTurnInput) > 0.01f))
        {
            Debug.Log($"[Tank] Moving: input={currentMoveInput:F2}, velocity={rb.linearVelocity.magnitude:F2}");
        }
    }

    void UpdateTurret()
    {
        if (turret == null) return;

        // Smoothly rotate turret to target angle
        float currentAngle = turret.localEulerAngles.y;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetTurretAngle, turretRotationSpeed * Time.deltaTime);
        turret.localRotation = Quaternion.Euler(0f, newAngle, 0f);

        // Barrel elevation
        if (barrel != null)
        {
            float currentBarrel = barrel.localEulerAngles.x;
            if (currentBarrel > 180f) currentBarrel -= 360f;
            float newBarrel = Mathf.MoveTowards(currentBarrel, -targetBarrelAngle, turretRotationSpeed * Time.deltaTime);
            newBarrel = Mathf.Clamp(newBarrel, -barrelMaxAngle, -barrelMinAngle);
            barrel.localRotation = Quaternion.Euler(newBarrel, 0f, 0f);
        }
    }

    void UpdateEngineAudio()
    {
        if (engineAudioSource == null) return;

        // Only play engine if we have a driver
        bool shouldPlayEngine = HasDriver && !isDestroyed;

        if (!shouldPlayEngine)
        {
            if (engineAudioSource.isPlaying)
            {
                engineAudioSource.Stop();
            }
            return;
        }

        // Check if we're moving (any input or significant velocity)
        bool wasMoving = isMoving;
        isMoving = Mathf.Abs(currentMoveInput) > 0.1f || rb.linearVelocity.magnitude > 1f;

        // Switch between idle and moving sounds
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
            {
                engineAudioSource.Play();
            }
        }

        // Adjust pitch based on speed
        float speedRatio = Mathf.Clamp01(rb.linearVelocity.magnitude / moveSpeed);
        float targetPitch = Mathf.Lerp(enginePitchMin, enginePitchMax, speedRatio);
        engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * 3f);

        // Blend volume based on movement intensity
        if (isMoving)
        {
            float moveIntensity = Mathf.Abs(currentMoveInput);
            engineAudioSource.volume = Mathf.Lerp(idleVolume, movingVolume, moveIntensity);
        }
    }

    void StartEngineAudio()
    {
        if (engineAudioSource == null) return;

        // Start with idle sound
        if (engineIdleSound != null)
        {
            engineAudioSource.clip = engineIdleSound;
            engineAudioSource.volume = idleVolume;
            engineAudioSource.pitch = enginePitchMin;
            engineAudioSource.Play();
        }
        else if (engineMovingSound != null)
        {
            // Fallback to moving sound if no idle
            engineAudioSource.clip = engineMovingSound;
            engineAudioSource.volume = idleVolume;
            engineAudioSource.pitch = enginePitchMin;
            engineAudioSource.Play();
        }
    }

    void StopEngineAudio()
    {
        if (engineAudioSource != null && engineAudioSource.isPlaying)
        {
            engineAudioSource.Stop();
        }
    }

    void HandlePlayerInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return;

        // Movement
        currentMoveInput = 0f;
        if (keyboard.wKey.isPressed) currentMoveInput = 1f;
        else if (keyboard.sKey.isPressed) currentMoveInput = -1f;

        // Turning
        currentTurnInput = 0f;
        if (keyboard.dKey.isPressed) currentTurnInput = 1f;
        else if (keyboard.aKey.isPressed) currentTurnInput = -1f;

        // Unstuck - Hold H to respawn at spawn point
        if (keyboard.hKey.isPressed)
        {
            unstuckHoldTimer += Time.deltaTime;
            showUnstuckUI = true;

            if (unstuckHoldTimer >= unstuckHoldTime)
            {
                UnstuckTank();
                unstuckHoldTimer = 0f;
                showUnstuckUI = false;
            }
        }
        else
        {
            unstuckHoldTimer = 0f;
            showUnstuckUI = false;
        }

        // Turret follows mouse
        if (playerCamera != null && mouse != null)
        {
            Ray ray = playerCamera.ScreenPointToRay(mouse.position.ReadValue());
            Plane groundPlane = new Plane(Vector3.up, transform.position);

            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPoint = ray.GetPoint(distance);
                Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
                targetTurretAngle = Mathf.Atan2(localPoint.x, localPoint.z) * Mathf.Rad2Deg;
            }

            targetBarrelAngle = 0f;
        }

        // Fire cannon
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            TryFire();
        }
    }

    void UnstuckTank()
    {
        // Teleport tank back to spawn
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = spawnPosition + Vector3.up * 2f; // Slightly above ground
        transform.rotation = spawnRotation;

        Debug.Log("[Tank] Unstuck - respawned at spawn point");
    }

    void OnGUI()
    {
        if (!hasPlayerDriver) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 14;
        style.fontStyle = FontStyle.Bold;

        // Always show hint at bottom of screen
        GUI.color = new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(new Rect(0, Screen.height - 30, Screen.width, 25), "Hold H to Unstuck Tank", style);

        // Show progress bar when holding F
        if (showUnstuckUI)
        {
            float progress = unstuckHoldTimer / unstuckHoldTime;
            float barWidth = 250f;
            float barHeight = 30f;
            float x = (Screen.width - barWidth) / 2f;
            float y = Screen.height * 0.6f;

            // Background
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(x - 10, y - 35, barWidth + 20, barHeight + 50), Texture2D.whiteTexture);

            // Text
            GUI.color = Color.yellow;
            style.fontSize = 18;
            GUI.Label(new Rect(x, y - 30, barWidth, 25), "UNSTUCKING...", style);

            // Progress bar background
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), Texture2D.whiteTexture);

            // Progress bar fill
            GUI.color = Color.Lerp(Color.red, Color.green, progress);
            GUI.DrawTexture(new Rect(x, y, barWidth * progress, barHeight), Texture2D.whiteTexture);

            // Percentage text
            GUI.color = Color.white;
            style.fontSize = 16;
            GUI.Label(new Rect(x, y, barWidth, barHeight), $"{(int)(progress * 100)}%", style);
        }

        GUI.color = Color.white;
    }

    void TryFire()
    {
        if (Time.time - lastFireTime < fireRate) return;
        lastFireTime = Time.time;

        Fire();
    }

    void Fire()
    {
        if (firePoint == null)
        {
            // Use barrel end as fire point
            if (barrel != null)
                firePoint = barrel;
            else if (turret != null)
                firePoint = turret;
            else
                firePoint = transform;
        }

        Vector3 spawnPos = firePoint.position + firePoint.forward * 3f;
        Quaternion spawnRot = firePoint.rotation;

        // Spawn shell
        if (shellPrefab != null)
        {
            GameObject shell = Instantiate(shellPrefab, spawnPos, spawnRot);
            Rigidbody shellRb = shell.GetComponent<Rigidbody>();
            if (shellRb != null)
            {
                shellRb.linearVelocity = firePoint.forward * shellSpeed;
            }

            // Add damage info to shell
            TankShell tankShell = shell.GetComponent<TankShell>();
            if (tankShell != null)
            {
                tankShell.damage = shellDamage;
                tankShell.ownerTeam = tankTeam;
                tankShell.explosionPrefab = explosionPrefab;
                tankShell.explosionRadius = explosionRadius;
            }

            Destroy(shell, 10f);
        }

        // Sound
        if (fireSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(fireSound);
        }

        // Muzzle flash
        SpawnMuzzleFlash(spawnPos, spawnRot);

        Debug.Log("[Tank] Fired!");
    }

    void SpawnMuzzleFlash(Vector3 position, Quaternion rotation)
    {
        if (muzzleFlashPrefab != null)
        {
            // Use assigned prefab
            GameObject flash = Instantiate(muzzleFlashPrefab, position, rotation);
            flash.transform.SetParent(firePoint);
            Destroy(flash, muzzleFlashDuration + 0.5f);
        }
        else
        {
            // Create procedural muzzle flash
            GameObject flashObj = new GameObject("TankMuzzleFlash");
            flashObj.transform.position = position;
            flashObj.transform.rotation = rotation;
            flashObj.transform.SetParent(firePoint);

            // Add bright point light
            Light flashLight = flashObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = muzzleFlashColor;
            flashLight.intensity = 8f;
            flashLight.range = 20f;

            // Add particle system for the flash
            ParticleSystem ps = flashObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = muzzleFlashSize;
            main.startLifetime = muzzleFlashDuration;
            main.startSpeed = 15f;
            main.startColor = muzzleFlashColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] {
                new ParticleSystem.Burst(0f, 15)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.3f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(muzzleFlashColor, 0.2f),
                    new GradientColorKey(new Color(0.5f, 0.3f, 0.1f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.3f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            // Add muzzle flash fader component
            TankMuzzleFlashFade fader = flashObj.AddComponent<TankMuzzleFlashFade>();
            fader.duration = muzzleFlashDuration;

            Destroy(flashObj, muzzleFlashDuration + 0.1f);
        }
    }

    void LateUpdate()
    {
        // Camera follow for player
        if (hasPlayerDriver && playerCamera != null)
        {
            Vector3 targetPos = transform.position - transform.forward * 15f + Vector3.up * 10f;
            playerCamera.transform.position = Vector3.Lerp(playerCamera.transform.position, targetPos, Time.deltaTime * 5f);
            playerCamera.transform.LookAt(transform.position + Vector3.up * 2f);
        }
    }

    // === PUBLIC API ===

    public void SetPlayerDriver(FPSControllerPhoton player)
    {
        playerDriver = player;
        hasPlayerDriver = player != null;
        if (hasPlayerDriver)
        {
            tankTeam = player.playerTeam;
            SetupPlayerCamera();
            StartEngineAudio();
        }
    }

    public void ClearPlayerDriver()
    {
        playerDriver = null;
        hasPlayerDriver = false;
        currentMoveInput = 0f;
        currentTurnInput = 0f;

        // Stop engine if no driver remains
        if (!HasDriver)
        {
            StopEngineAudio();
        }
    }

    void SetupPlayerCamera()
    {
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            GameObject camObj = new GameObject("TankCamera");
            playerCamera = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }
        playerCamera.transform.SetParent(null);
    }

    public void SetAIDriver(AIController ai)
    {
        aiDriver = ai;
        hasAIDriver = ai != null;
        if (hasAIDriver)
        {
            tankTeam = ai.team;
            StartEngineAudio();
        }
    }

    public void ClearAIDriver()
    {
        aiDriver = null;
        hasAIDriver = false;

        // Stop engine if no driver remains
        if (!HasDriver)
        {
            StopEngineAudio();
        }
    }

    public void SetAIInput(float move, float turn, float turretAngle, bool fire)
    {
        currentMoveInput = Mathf.Clamp(move, -1f, 1f);
        currentTurnInput = Mathf.Clamp(turn, -1f, 1f);
        targetTurretAngle = turretAngle;

        if (fire)
        {
            TryFire();
        }

        // Debug - log every few seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"[Tank AI] Input: move={currentMoveInput:F2}, turn={currentTurnInput:F2}, turret={turretAngle:F0}");
        }
    }

    // Set only turret angle and fire (allows movement to be controlled separately)
    public void SetTurretInput(float turretAngle, bool fire)
    {
        targetTurretAngle = turretAngle;

        if (fire)
        {
            TryFire();
        }
    }

    // ===== NAVIGATION HELPERS =====

    /// <summary>
    /// Set navigation destination (uses TankNavigation component)
    /// </summary>
    public bool NavigateTo(Vector3 destination)
    {
        if (navigation == null) return false;
        return navigation.SetDestination(destination);
    }

    /// <summary>
    /// Stop navigation
    /// </summary>
    public void StopNavigation()
    {
        if (navigation != null)
        {
            navigation.Stop();
        }
        SetAIInput(0f, 0f, targetTurretAngle, false);
    }

    /// <summary>
    /// Check if tank is currently navigating
    /// </summary>
    public bool IsNavigating => navigation != null && navigation.HasPath;

    /// <summary>
    /// Get remaining distance to navigation destination
    /// </summary>
    public float NavigationRemainingDistance => navigation != null ? navigation.RemainingDistance : 0f;

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker)
    {
        if (isDestroyed) return;
        currentHealth -= damage;
        Debug.Log($"[Tank] Took {damage} damage, health: {currentHealth}/{maxHealth}");

        // Record danger zone for AI awareness
        if (hasAIDriver && aiDriver != null)
        {
            aiDriver.TankRecordDangerZone(transform.position);
        }

        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        // Record death location for AI awareness
        AIController.TankRecordDeath(transform.position);

        // Stop engine audio
        StopEngineAudio();

        // Eject driver
        if (hasPlayerDriver && playerDriver != null)
        {
            playerDriver.ExitTank();
        }
        if (hasAIDriver && aiDriver != null)
        {
            aiDriver.ExitTankAsDriver();
        }

        // Eject all virtual passengers
        EjectAllVirtualPassengers();

        // Spawn destroyed version
        if (destroyedPrefab != null)
        {
            Instantiate(destroyedPrefab, transform.position, transform.rotation);
        }

        Destroy(gameObject, 0.1f);
    }

    // === PROPERTIES ===

    public bool HasDriver => hasPlayerDriver || hasAIDriver;
    public Team TankTeam => tankTeam;
    public float CurrentSpeed => rb.linearVelocity.magnitude;

    // === VIRTUAL PASSENGERS ===

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

        // Position passenger on tank
        int seatIndex = virtualPassengers.Count - 1;
        if (passengerSeats != null && seatIndex < passengerSeats.Length && passengerSeats[seatIndex] != null)
        {
            ai.transform.SetParent(passengerSeats[seatIndex]);
            ai.transform.localPosition = Vector3.zero;
            ai.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // Default positions on tank hull
            ai.transform.SetParent(transform);
            Vector3 offset = GetDefaultPassengerOffset(seatIndex);
            ai.transform.localPosition = offset;
        }

        Debug.Log($"[Tank] Added virtual passenger {ai.name}, total: {virtualPassengers.Count}");
        return true;
    }

    Vector3 GetDefaultPassengerOffset(int index)
    {
        // Positions on the tank hull for passengers
        switch (index)
        {
            case 0: return new Vector3(-1.5f, 2f, 1f);   // Left front
            case 1: return new Vector3(1.5f, 2f, 1f);    // Right front
            case 2: return new Vector3(-1.5f, 2f, -1f);  // Left back
            case 3: return new Vector3(1.5f, 2f, -1f);   // Right back
            default: return new Vector3(0f, 2.5f, 0f);   // On top
        }
    }

    public void RemoveVirtualPassenger(AIController ai)
    {
        if (ai == null) return;
        virtualPassengers.Remove(ai);
        ai.transform.SetParent(null);
        Debug.Log($"[Tank] Removed virtual passenger {ai.name}");
    }

    public void EjectAllVirtualPassengers()
    {
        var passengersToEject = new System.Collections.Generic.List<AIController>(virtualPassengers);

        foreach (var passenger in passengersToEject)
        {
            if (passenger != null)
            {
                passenger.transform.SetParent(null);
                // Position them around the tank
                Vector3 exitPos = transform.position + Random.onUnitSphere * 5f;
                exitPos.y = transform.position.y + 1f;

                if (Physics.Raycast(exitPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 15f))
                {
                    exitPos = hit.point + Vector3.up * 0.5f;
                }

                passenger.transform.position = exitPos;

                // Re-enable their AI
                var agent = passenger.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null)
                {
                    agent.enabled = true;
                    agent.Warp(exitPos);
                }
            }
        }

        virtualPassengers.Clear();
        Debug.Log("[Tank] Ejected all virtual passengers");
    }

    public System.Collections.Generic.List<AIController> GetVirtualPassengers()
    {
        virtualPassengers.RemoveAll(p => p == null || p.isDead);
        return virtualPassengers;
    }

    public bool IsWaitingForPassengers => HasDriver && HasVirtualPassengerSpace();

    // === NETWORK ===

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(turret != null ? turret.localEulerAngles.y : 0f);
            stream.SendNext(currentHealth);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkTurretAngle = (float)stream.ReceiveNext();
            currentHealth = (float)stream.ReceiveNext();
            networkInitialized = true;
        }
    }

    // === OBSTACLE CRUSHING ===

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[Tank] COLLISION ENTER with: {collision.gameObject.name}");
        HandleObstacleCollision(collision, true);
    }

    void OnCollisionStay(Collision collision)
    {
        HandleObstacleCollision(collision, false);
    }

    void HandleObstacleCollision(Collision collision, bool isEnter)
    {
        if (isDestroyed) return;
        if (collision.gameObject == null) return;

        // Don't crush terrain, ground, or other tanks
        if (collision.collider is TerrainCollider) return;
        if (collision.gameObject.GetComponent<TankController>() != null) return;

        // Check if we're moving fast enough to crush/push
        float tankSpeed = rb.linearVelocity.magnitude;
        if (tankSpeed < 0.5f) return;

        // Check if object has DestructibleObject - always crushable
        DestructibleObject destructible = collision.gameObject.GetComponent<DestructibleObject>();
        if (destructible == null)
            destructible = collision.gameObject.GetComponentInParent<DestructibleObject>();

        bool isDestructible = destructible != null;

        // If not destructible, check height limit
        if (!isDestructible)
        {
            Bounds bounds = GetColliderBounds(collision.collider);
            float obstacleHeight = bounds.size.y;

            // Skip if obstacle is too tall to crush and not destructible
            if (obstacleHeight > crushableMaxHeight)
            {
                return;
            }
        }

        // Try to deal damage to destructible objects
        if (isEnter)
        {
            TryDamageObstacle(collision.gameObject, collision.GetContact(0).point);
        }

        // Push the object if it has a rigidbody
        Rigidbody obstacleRb = collision.rigidbody;
        if (obstacleRb != null && !obstacleRb.isKinematic)
        {
            // Only push if light enough
            if (obstacleRb.mass <= pushableMaxMass)
            {
                Vector3 pushDir = (collision.transform.position - transform.position).normalized;
                pushDir.y = 0.2f; // Slight upward push
                pushDir.Normalize();

                // Apply force based on tank speed
                float forceMagnitude = pushForce * (tankSpeed / moveSpeed);
                obstacleRb.AddForce(pushDir * forceMagnitude, ForceMode.Impulse);

                if (isEnter)
                {
                    Debug.Log($"[Tank] Pushed obstacle: {collision.gameObject.name}");
                }
            }
        }
    }

    void TryDamageObstacle(GameObject obstacle, Vector3 hitPoint)
    {
        Debug.Log($"[Tank] Attempting to crush: {obstacle.name}");

        // Check for DestructibleObject first (most common for our setup)
        var destructible = obstacle.GetComponent<DestructibleObject>();
        if (destructible == null)
            destructible = obstacle.GetComponentInParent<DestructibleObject>();

        if (destructible != null)
        {
            destructible.TakeDamage(crushDamage, hitPoint, gameObject);
            Debug.Log($"[Tank] Crushed DestructibleObject: {obstacle.name}, dealt {crushDamage} damage");
            return;
        }

        // Try IDamageable interface
        var damageable = obstacle.GetComponent<IDamageable>();
        if (damageable == null)
            damageable = obstacle.GetComponentInParent<IDamageable>();

        if (damageable != null)
        {
            damageable.TakeDamage(crushDamage, hitPoint, gameObject);
            Debug.Log($"[Tank] Crushed IDamageable: {obstacle.name}");
            return;
        }

        // Check for Health component
        var healthComponent = obstacle.GetComponent<Health>();
        if (healthComponent == null)
            healthComponent = obstacle.GetComponentInParent<Health>();

        if (healthComponent != null)
        {
            healthComponent.TakeDamage(crushDamage);
            Debug.Log($"[Tank] Crushed Health component: {obstacle.name}");
            return;
        }

        // No damage component - just push it
        Rigidbody obstacleRb = obstacle.GetComponent<Rigidbody>();
        if (obstacleRb != null)
        {
            Debug.Log($"[Tank] No damage component on {obstacle.name}, pushing it");
            obstacleRb.isKinematic = false; // Make sure it can move
            obstacleRb.AddForce(transform.forward * pushForce * 0.5f + Vector3.up * pushForce * 0.2f, ForceMode.Impulse);
            obstacleRb.AddTorque(Random.insideUnitSphere * 500f, ForceMode.Impulse);
        }
        else
        {
            Debug.Log($"[Tank] {obstacle.name} has no DestructibleObject or Rigidbody!");
        }
    }

    Bounds GetColliderBounds(Collider col)
    {
        if (col == null) return new Bounds();

        // For compound colliders, get the combined bounds
        Collider[] allColliders = col.gameObject.GetComponentsInChildren<Collider>();
        if (allColliders.Length > 1)
        {
            Bounds combined = allColliders[0].bounds;
            for (int i = 1; i < allColliders.Length; i++)
            {
                combined.Encapsulate(allColliders[i].bounds);
            }
            return combined;
        }

        return col.bounds;
    }

    /// <summary>
    /// Check if an obstacle can be run over (for navigation)
    /// </summary>
    public bool CanCrushObstacle(Collider col)
    {
        if (col == null) return true;
        if (col is TerrainCollider) return true;
        if (col.GetComponent<TankController>() != null) return false;

        // If it has DestructibleObject, always crushable
        if (col.GetComponent<DestructibleObject>() != null) return true;
        if (col.GetComponentInParent<DestructibleObject>() != null) return true;

        // If it has Health component, crushable
        if (col.GetComponent<Health>() != null) return true;
        if (col.GetComponentInParent<Health>() != null) return true;

        Bounds bounds = GetColliderBounds(col);
        return bounds.size.y <= crushableMaxHeight;
    }

    /// <summary>
    /// Check if an obstacle can be run over by height
    /// </summary>
    public bool CanCrushObstacleByHeight(float height)
    {
        return height <= crushableMaxHeight;
    }
}

// Helper component to fade muzzle flash light
public class TankMuzzleFlashFade : MonoBehaviour
{
    public float duration = 0.1f;
    private Light flashLight;
    private float startIntensity;
    private float elapsed = 0f;

    void Start()
    {
        flashLight = GetComponent<Light>();
        if (flashLight != null)
            startIntensity = flashLight.intensity;
    }

    void Update()
    {
        if (flashLight == null) return;

        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        flashLight.intensity = Mathf.Lerp(startIntensity, 0f, t);

        if (elapsed >= duration)
        {
            Destroy(flashLight);
            Destroy(this);
        }
    }
}
