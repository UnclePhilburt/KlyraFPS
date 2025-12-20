using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class AIController : MonoBehaviour
{
    [Header("Team")]
    public Team team = Team.Phantom;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 5f;

    [Header("Combat")]
    public float detectionRange = 30f;
    public float attackRange = 25f;
    public float fireRate = 0.3f;
    public float damage = 20f;
    public float accuracy = 0.7f;

    [Header("Capture")]
    public float captureWaitTime = 3f;

    [Header("Effects")]
    public AudioClip gunshotSound;
    public float gunshotVolume = 0.5f;
    public Color tracerColor = Color.yellow;
    public float tracerDuration = 0.05f;
    public GameObject muzzleFlashPrefab;  // Assign FX_Gunshot_01 from Synty
    public GameObject tracerPrefab;        // Assign FX_Bullet_Trail_Mesh from Synty
    private AudioSource audioSource;
    private LineRenderer tracerLine;


    // Personality (randomized per bot)
    private enum Personality { Balanced, Aggressive, Defensive, LoneWolf, Camper }
    private Personality personality;
    private float initiative; // 0-1, how likely to do their own thing
    private float bravery;    // 0-1, how likely to engage vs retreat
    private float patience;   // 0-1, how long they wait at objectives

    // Squad system (AI-to-AI)
    public enum SquadRole { Leader, Member }
    public SquadRole squadRole = SquadRole.Member;
    public AIController squadLeader;
    public List<AIController> squadMembers = new List<AIController>();
    private float squadCheckTimer = 0f;
    private static List<AIController> unassignedBots = new List<AIController>();
    private static Dictionary<Team, List<AIController>> teamLeaders = new Dictionary<Team, List<AIController>>();

    // Static prefab reference for respawning (set by AISpawner)
    public static GameObject aiPrefabReference;

    // Player-led squad system
    private bool inPlayerSquad = false;
    private FPSControllerPhoton playerSquadLeader;
    private float followDistance = 4f;
    private float squadFormationOffset = 0f;  // Offset for formation positioning
    private int squadIndex = 0;  // Position in squad for formations

    // Soldier identity
    [HideInInspector]
    public SoldierIdentity identity;

    // Orders
    public enum OrderType { FollowLeader, DefendPoint, CapturePoint, HoldPosition, BoardHelicopter }
    public OrderType currentOrder = OrderType.FollowLeader;
    public CapturePoint orderedPoint;
    public HelicopterController orderedHelicopter;  // Helicopter to board
    public Vector3 holdPosition;
    public Vector3 holdFacingDirection; // Direction to face when holding position

    [Header("Skins")]
    public string[] phantomSkinNames = { "SM_Chr_Soldier_Male_01", "SM_Chr_Soldier_Male_02" };
    public string[] havocSkinNames = { "SM_Chr_Insurgent_Male_01", "SM_Chr_Insurgent_Male_04" };

    // State machine
    public enum AIState { Idle, MovingToPoint, Capturing, Combat, Dead, HeliGunner, HeliPilot, HeliPassenger, BoardingHelicopter, JetPilot, TankDriver, TankPassenger }
    public AIState currentState = AIState.Idle;

    // Helicopter gunner
    private HelicopterSeat currentHeliSeat;
    private HelicopterController currentHelicopter;
    private HelicopterController targetBoardingHelicopter;  // Helicopter we're trying to board
    private float boardingTimeout = 0f;  // Timer to cancel boarding if taking too long
    private const float MAX_BOARDING_TIME = 15f;  // Max seconds to reach helicopter

    // Helicopter pilot
    private HelicopterController pilotingHelicopter;

    // Jet pilot
    private JetController pilotingJet;
    private enum JetMissionPhase { OnRunway, Takeoff, Climbing, AttackRun, Diving, PullUp, Patrol, ReturnToBase, Landing, Landed }
    private JetMissionPhase jetMissionPhase = JetMissionPhase.OnRunway;
    private Vector3 jetTargetPosition;
    private float jetTargetAltitude = 100f;
    private Transform jetCurrentTarget;  // Current attack target
    private float jetAttackTimer = 0f;
    private float jetPatrolTimer = 0f;
    private Runway jetHomeRunway;
    private Vector3 jetPatrolCenter;  // Stored patrol center (runway position at spawn)
    private const float JET_CRUISE_ALTITUDE = 60f;   // Initial climb altitude
    private const float JET_ATTACK_ALTITUDE = 40f;   // Altitude during attack runs
    private const float JET_PATROL_ALTITUDE = 50f;   // Low patrol for player presence

    // Tank driver
    private TankController drivingTank;
    private bool isDedicatedTankDriver = false;
    private TankController assignedTank;
    private TankController currentTank;  // Tank we're riding on as passenger
    private enum TankMissionPhase { Idle, FollowingInfantry, MovingToObjective, Engaging, SupportingCombat, Retreating }
    private TankMissionPhase tankMissionPhase = TankMissionPhase.Idle;
    private Vector3 tankPatrolCenter;
    private Vector3 tankTargetPosition;
    private Transform tankCurrentTarget;
    private Transform tankFollowTarget;  // Infantry/player we're following
    private CapturePoint tankObjective;  // Objective we're moving to
    private float tankPatrolTimer = 0f;
    private float tankEngageTimer = 0f;
    private float tankFireCooldown = 0f;
    private float tankDecisionTimer = 0f;  // Timer for making tactical decisions
    private const float TANK_FOLLOW_DISTANCE = 15f;  // How far behind infantry to stay
    private const float TANK_ENGAGE_RANGE = 80f;
    private const float TANK_FIRE_RANGE = 60f;
    private const float TANK_SUPPORT_RANGE = 50f;  // Range to detect friendly combat

    // Tank obstacle avoidance
    private float tankStuckTimer = 0f;
    private float tankReverseTimer = 0f;
    private const float TANK_OBSTACLE_DETECT_DISTANCE = 20f;  // How far to check for obstacles
    private const float TANK_OBSTACLE_SIDE_DISTANCE = 12f;    // Side ray distance
    private const float TANK_STUCK_THRESHOLD = 1.5f;          // Seconds before considered stuck
    private const float TANK_REVERSE_DURATION = 2f;           // How long to reverse when stuck
    private const float TANK_TURN_IN_PLACE_ANGLE = 60f;       // Angle threshold to stop and turn

    // Tank NavMesh pathfinding
    private NavMeshPath tankNavPath;
    private Vector3[] tankPathCorners = new Vector3[0];
    private int tankCurrentWaypoint = 0;
    private Vector3 tankLastDestination;
    private float tankPathRecalcTimer = 0f;
    private const float TANK_PATH_RECALC_INTERVAL = 3f;  // Recalculate path every 3 seconds
    private const float TANK_WAYPOINT_REACH_DIST = 5f;   // Distance to consider waypoint reached

    // Tank Path Smoothing (AAA-style)
    private List<Vector3> tankSmoothedPath = new List<Vector3>();
    private int tankSmoothedIndex = 0;
    private const int TANK_SMOOTH_SUBDIVISIONS = 4;      // Points between each corner
    private const float TANK_SMOOTH_POINT_SPACING = 3f;  // Meters between smoothed points

    // Tank Look-Ahead Steering (carrot on stick)
    private const float TANK_LOOK_AHEAD_MIN = 8f;        // Minimum look-ahead distance
    private const float TANK_LOOK_AHEAD_MAX = 25f;       // Maximum look-ahead distance
    private const float TANK_LOOK_AHEAD_SPEED_MULT = 2f; // Look-ahead = speed * this
    private Vector3 tankSteerTarget = Vector3.zero;      // The "carrot" we're steering toward

    // Tank Steering Behaviors
    private float tankCurrentSpeed = 0f;                 // Actual current speed
    private float tankTargetSpeed = 0f;                  // Desired speed
    private const float TANK_ACCEL_RATE = 3f;            // Acceleration m/s^2
    private const float TANK_DECEL_RATE = 5f;            // Deceleration m/s^2
    private const float TANK_TURN_RATE_STATIONARY = 60f; // Turn rate when stopped (deg/s)
    private const float TANK_TURN_RATE_MOVING = 30f;     // Turn rate when moving fast (deg/s)
    private float tankDesiredHeading = 0f;               // Where we want to face
    private float tankCurrentTurnRate = 0f;              // Smoothed turn input

    // Tank Curvature Speed Control - slow down for upcoming turns
    private const float TANK_CURVATURE_LOOKAHEAD = 25f;  // How far ahead to check for turns
    private const float TANK_MIN_CURVE_SPEED = 0.3f;     // Minimum speed during sharp turns
    private float tankPathCurvature = 0f;                // Current path curvature (0=straight, 1=sharp)

    // Tank Escalating Stuck Recovery
    private int tankStuckAttempts = 0;                   // How many times we've tried to unstick
    private const int TANK_MAX_STUCK_ATTEMPTS = 4;       // Before giving up on current path
    private float tankStuckEscalationTimer = 0f;         // Timer for escalating recovery
    private Vector3 tankLastStuckPosition = Vector3.zero;// Where we got stuck
    private List<Vector3> tankFailedPathSegments = new List<Vector3>(); // Segments to avoid

    // Tank Hull-Down Combat Positioning
    private Vector3 tankHullDownPosition = Vector3.zero; // Current hull-down position we're seeking
    private bool tankSeekingHullDown = false;            // Whether we're moving to hull-down position
    private float tankHullDownSearchTimer = 0f;          // Time since last hull-down search
    private const float TANK_HULL_DOWN_SEARCH_INTERVAL = 5f;  // How often to search for new position
    private const float TANK_HULL_HEIGHT = 1.5f;         // Height of tank hull (raycast from enemy to here)
    private const float TANK_TURRET_HEIGHT = 2.8f;       // Height of tank turret (must have LOS)

    // Tank Flanking Maneuvers
    private Vector3 tankFlankingPosition = Vector3.zero; // Current flanking position we're approaching
    private bool tankIsFlanking = false;                 // Whether we're executing a flanking maneuver
    private float tankFlankingTimer = 0f;                // How long we've been flanking
    private const float TANK_FLANKING_TIMEOUT = 15f;     // Max time to attempt a flank
    private const float TANK_FLANK_ANGLE = 70f;          // Desired angle from enemy front (degrees)

    // Tank Road/Terrain Preference
    private const float TANK_ROAD_SPEED_BONUS = 1.3f;    // Speed multiplier when on road
    private const float TANK_OFFROAD_SPEED_PENALTY = 0.7f; // Speed multiplier when off-road
    private bool tankOnRoad = false;                     // Whether tank is currently on a road
    private float tankTerrainCheckTimer = 0f;            // Timer for terrain checks
    private const float TANK_TERRAIN_CHECK_INTERVAL = 0.5f; // How often to check terrain
    // Road detection keywords
    private static readonly string[] ROAD_KEYWORDS = { "road", "street", "path", "pavement", "asphalt", "concrete", "highway", "bridge" };
    private static readonly string[] OFFROAD_KEYWORDS = { "grass", "mud", "dirt", "sand", "water", "swamp", "forest", "bush" };

    // Use new TankNavigation component for movement (simpler, more reliable)
    private bool useNewTankNavigation = true;
    private Vector3 tankNavigationTarget = Vector3.zero;
    private bool tankHasNavigationTarget = false;

    // Tank Awareness - Turret Scanning
    private float tankTurretScanAngle = 0f;
    private float tankTurretScanDirection = 1f;
    private float tankTurretScanSpeed = 30f;  // Degrees per second
    private float tankLastCombatTime = 0f;
    private const float TANK_SCAN_DELAY = 3f;  // Seconds after combat before scanning

    // Tank Awareness - Flank Detection
    private float tankFlankCheckTimer = 0f;
    private const float TANK_FLANK_CHECK_INTERVAL = 2f;
    private Transform tankFlankThreat = null;

    // Tank Awareness - Danger Zones (where we took damage)
    private static List<Vector3> tankDangerZones = new List<Vector3>();
    private const float TANK_DANGER_ZONE_RADIUS = 30f;
    private const int TANK_MAX_DANGER_ZONES = 20;

    // Tank Awareness - Death Memory (where friendlies died)
    private static List<Vector3> tankDeathMemory = new List<Vector3>();
    private const float TANK_DEATH_MEMORY_RADIUS = 25f;
    private const int TANK_MAX_DEATH_MEMORIES = 15;

    // Tank Awareness - Sound Detection
    private Vector3 tankLastHeardCombat = Vector3.zero;
    private float tankSoundAlertTimer = 0f;
    private const float TANK_HEARING_RANGE = 100f;

    // Tank Awareness - Contact Sharing
    private static List<Transform> tankSharedContacts = new List<Transform>();
    private static float tankContactShareTimer = 0f;

    // Tank Navigation - Terrain Memory (stuck spots)
    private static List<Vector3> tankStuckSpots = new List<Vector3>();
    private const float TANK_STUCK_SPOT_RADIUS = 10f;
    private const int TANK_MAX_STUCK_SPOTS = 30;

    // Tank Navigation - Width/Slope
    private const float TANK_WIDTH = 4f;
    private const float TANK_MAX_SLOPE = 35f;  // Max degrees

    // Tank Navigation - Smart Reversing
    private Vector3 tankReverseDirection = Vector3.zero;
    private bool tankIsSmartReversing = false;

    // Tank Navigation - Ambush Caution
    private bool tankInChokePoint = false;
    private float tankCautionSpeedMult = 1f;

    private Vector3 heliTargetPosition = Vector3.zero;
    private float heliTargetAltitude = 20f;
    private float heliHoverTimer = 10f;  // Start with time so we don't immediately pick new target
    private bool heliFirstFrame = true;  // Track if we just started piloting
    private float heliBoardingTimer = 0f;  // Wait for passengers to board
    private float heliGunnerCheckTimer = 0f;
    private Transform heliGunnerTarget;

    // Transport mission system
    private enum HeliMissionPhase { WaitingForTroops, FlyingToPickup, LandingForPickup, LoadingTroops, FlyingToObjective, LandingAtObjective, UnloadingTroops }
    private HeliMissionPhase heliMissionPhase = HeliMissionPhase.WaitingForTroops;
    private Vector3 heliPickupPosition;
    private Vector3 heliDropoffPosition;
    private Vector3 heliPlayerCallLandingPos;  // Landing zone when player calls helicopter
    private bool heliPlayerCallHasLandingPos = false;  // Track if we've found a landing spot
    private float heliLandingTimer = 0f;
    private float heliUnloadTimer = 0f;
    private float heliStuckTimer = 0f;
    private CapturePoint heliTargetObjective;
    private List<AIController> heliPassengers = new List<AIController>();
    private const float HELI_LANDING_HEIGHT = 3f;  // Height to consider "landed"
    private const float HELI_CRUISE_ALTITUDE = 45f;  // Higher altitude to clear most obstacles
    private const float HELI_LANDING_WAIT_TIME = 12f;  // Time to wait for troops to board/exit

    // Auto-boarding system for infantry
    private float heliBoardingCheckTimer = 0f;
    private const float HELI_BOARDING_CHECK_INTERVAL = 1f;  // Check every second
    private const float HELI_BOARDING_RANGE = 50f;  // Distance to detect landed helicopters
    private float heliExitCooldown = 0f;  // Cooldown after exiting helicopter before can board again
    private const float HELI_EXIT_COOLDOWN_TIME = 30f;  // 30 seconds before can re-board

    // Transport request system - troops can call for helicopter pickup
    public class TransportRequest
    {
        public Vector3 position;
        public Team team;
        public float timestamp;
        public int requestingTroopCount;
    }
    private static List<TransportRequest> pendingTransportRequests = new List<TransportRequest>();
    private float transportRequestCooldown = 0f;
    private const float TRANSPORT_REQUEST_INTERVAL = 10f;  // Can request every 10 seconds

    // Targets
    private CapturePoint targetPoint;
    private Transform targetEnemy;
    private List<CapturePoint> allCapturePoints = new List<CapturePoint>();

    // Timers
    private float nextFireTime = 0f;
    private float captureTimer = 0f;

    // Health
    public float maxHealth = 100f;
    public float currentHealth;

    // Cached references (performance) - GLOBAL cache, updated once per second
    private static FPSControllerPhoton[] cachedPlayers;
    private static AIController[] cachedAIs;
    private static float globalCacheTimer = 0f;
    private static int globalFrameCount = 0;

    // Per-bot frame offset for staggered updates
    private int frameOffset = 0;
    private int combatFrameCounter = 0;

    // Flag to identify AI vs player-controlled
    [HideInInspector]
    public bool isAIControlled = false;

    // NavMesh pathfinding - using NavMeshAgent for proper navigation
    private NavMeshAgent agent;
    private bool agentEnabled = false;

    // Simple movement
    private Vector3 targetPosition;
    private float stuckTimer = 0f;
    private Vector3 lastPosition;
    private Vector3 spawnPosition;
    private float timeSinceSpawn = 0f;

    // Animation
    private Animator animator;
    private string animSpeedParam = null;
    private string animMovingParam = null;

    // Combat smarts
    private Vector3 lastDamageDirection;
    private float lastDamageTime;
    private GameObject lastAttacker;
    private float strafeDirection = 1f;
    private float strafeTimer = 0f;
    private Transform lastKnownEnemyPos;
    private Vector3 lastKnownEnemyLocation;
    private float suppressedTimer = 0f;
    private int burstCount = 0;
    private float reloadTimer = 0f;

    // Intelligence
    private int recentDeaths = 0;          // Track how often we die (adaptive tactics)
    private float lastDeathTime = 0f;
    private Vector3 lastDeathPosition;
    private float tacticalWaitTimer = 0f;  // Patience - wait for right moment
    private bool isAmbushing = false;
    private float ambushTimer = 0f;
    private int killStreak = 0;
    private float confidence = 0.5f;       // 0 = terrified, 1 = overconfident

    // === ADVANCED AI SYSTEMS ===

    // Cover System
    private Vector3 currentCoverPosition;
    private Vector3 coverPeekDirection;
    private bool isInCover = false;
    private bool isPeeking = false;
    private float coverSearchTimer = 0f;
    private float peekTimer = 0f;
    private float coverQuality = 0f;       // 0-1, how good is current cover
    private static string[] coverTags = { "Cover", "Wall", "Obstacle" };
    private static LayerMask coverLayerMask = -1;  // All layers by default

    // Flanking & Tactics
    private Vector3 flankTarget;
    private bool isFlankingTarget = false;
    private float flankTimer = 0f;
    private bool enemyIsDistracted = false;  // Enemy focused on someone else
    private AIController distractingAlly;    // Who is distracting our target

    // Aim System
    private Vector3 predictedEnemyPos;
    private Vector3 lastEnemyVelocity;
    private Vector3 previousEnemyPos;
    private float aimOffset = 0f;           // Current aim inaccuracy
    private float aimSettleTimer = 0f;      // Time spent aiming at current target
    private bool isAimingForHead = false;
    private float reactionTimer = 0f;       // Delay before reacting to new threat

    // Team Coordination
    private static Dictionary<Team, List<Vector3>> sharedEnemyPositions = new Dictionary<Team, List<Vector3>>();
    private static Dictionary<Team, float> lastCoordinatedPush = new Dictionary<Team, float>();
    private float lastCalloutTime = 0f;
    private bool isProvidingCoverFire = false;
    private AIController coveringFor;        // Who we're covering
    private float coordinationTimer = 0f;

    void Start()
    {
        currentHealth = maxHealth;

        // Remove any AudioListeners (only player should have one)
        AudioListener[] listeners = GetComponentsInChildren<AudioListener>();
        foreach (var listener in listeners)
        {
            Destroy(listener);
        }


        // Disable cameras on AI
        Camera[] cameras = GetComponentsInChildren<Camera>();
        foreach (var cam in cameras)
        {
            cam.enabled = false;
        }

        // Get animator for animations
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // Cache which parameters exist
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float)
                {
                    if (param.name == "Speed" || param.name == "MoveSpeed" || param.name == "Velocity")
                    {
                        animSpeedParam = param.name;
                    }
                }
                else if (param.type == AnimatorControllerParameterType.Bool)
                {
                    if (param.name == "IsMoving" || param.name == "isMoving" || param.name == "Moving")
                    {
                        animMovingParam = param.name;
                    }
                }
            }
        }

        // Disable Rigidbody physics (we use simple movement)
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        // Disable CharacterController if present, but add a CapsuleCollider for hit detection
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            // Add a CapsuleCollider for hit detection before disabling CharacterController
            CapsuleCollider hitCollider = GetComponent<CapsuleCollider>();
            if (hitCollider == null)
            {
                hitCollider = gameObject.AddComponent<CapsuleCollider>();
                hitCollider.center = cc.center;
                hitCollider.radius = cc.radius;
                hitCollider.height = cc.height;
            }
            cc.enabled = false;
        }
        else
        {
            // No CharacterController, ensure we have a collider anyway
            CapsuleCollider hitCollider = GetComponent<CapsuleCollider>();
            if (hitCollider == null)
            {
                hitCollider = gameObject.AddComponent<CapsuleCollider>();
                hitCollider.center = new Vector3(0, 1f, 0);
                hitCollider.radius = 0.4f;
                hitCollider.height = 2f;
            }
        }

        // Setup NavMeshAgent with optimized settings
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        // Optimize agent settings for performance
        agent.speed = moveSpeed;
        agent.angularSpeed = rotationSpeed * 60f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 1.5f;
        agent.autoBraking = false;
        agent.autoRepath = true;

        // Disable avoidance (we handle obstacles ourselves for more control)
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        // Reduce update frequency for performance
        agent.updatePosition = true;
        agent.updateRotation = true;

        // Start disabled - enable when we have a destination
        agent.enabled = false;
        agentEnabled = false;

        // Stagger think cycles - each bot thinks on different frames
        frameOffset = Random.Range(0, 60);

        // Setup audio for gunshots
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f; // 3D sound
        audioSource.maxDistance = 50f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.playOnAwake = false;

        // Setup tracer line renderer
        GameObject tracerObj = new GameObject("Tracer");
        tracerObj.transform.SetParent(transform);
        tracerLine = tracerObj.AddComponent<LineRenderer>();
        tracerLine.startWidth = 0.02f;
        tracerLine.endWidth = 0.02f;
        tracerLine.positionCount = 2;
        tracerLine.material = new Material(Shader.Find("Sprites/Default"));
        tracerLine.startColor = tracerColor;
        tracerLine.endColor = tracerColor;
        tracerLine.enabled = false;

        // Randomize personality
        RandomizePersonality();

        allCapturePoints.AddRange(FindObjectsOfType<CapturePoint>());

        // Team-dependent initialization is done in InitializeTeam() after AISpawner sets the team
        lastPosition = transform.position;
    }

    // Called by AISpawner AFTER setting the team
    public void InitializeTeam()
    {
        isAIControlled = true; // Mark as AI, not player

        // Find capture points if not already found (Start() may not have run yet)
        if (allCapturePoints.Count == 0)
        {
            allCapturePoints.AddRange(FindObjectsOfType<CapturePoint>());
        }

        // Make sure we're on the NavMesh
        if (agent != null)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }
            else
            {
                Debug.LogWarning($"{gameObject.name} could not find NavMesh near spawn position!");
            }
        }

        spawnPosition = transform.position;
        timeSinceSpawn = 0f;

        SetupTeamSkin();

        // Generate soldier identity
        identity = SoldierIdentity.Generate(team);
        gameObject.name = $"AI_{identity.RankAndName}";

        // Register with kill feed manager for scoreboard
        KillFeedManager.RegisterAI(this);

        AssignToSquad();
        FindNextCapturePoint();
    }

    void AssignToSquad()
    {
        // Lone wolves don't join squads
        if (personality == Personality.LoneWolf)
        {
            squadRole = SquadRole.Leader; // They lead themselves
            squadLeader = this;
            return;
        }

        // Initialize team leaders dict if needed
        if (!teamLeaders.ContainsKey(team))
        {
            teamLeaders[team] = new List<AIController>();
        }

        // Clean up dead leaders
        teamLeaders[team].RemoveAll(l => l == null || l.currentState == AIState.Dead);

        // Try to join an existing squad that needs members
        foreach (var leader in teamLeaders[team])
        {
            if (leader != null && leader.squadMembers.Count < 2) // Max 3 per squad (leader + 2)
            {
                squadRole = SquadRole.Member;
                squadLeader = leader;
                leader.squadMembers.Add(this);
                return;
            }
        }

        // No squad available - become a leader
        squadRole = SquadRole.Leader;
        squadLeader = this;
        teamLeaders[team].Add(this);
    }

    void OnDestroy()
    {
        // Clean up squad references
        if (squadRole == SquadRole.Leader)
        {
            // Promote a member to leader
            if (squadMembers.Count > 0)
            {
                AIController newLeader = squadMembers[0];
                squadMembers.RemoveAt(0);

                if (newLeader != null)
                {
                    newLeader.squadRole = SquadRole.Leader;
                    newLeader.squadLeader = newLeader;
                    newLeader.squadMembers = new List<AIController>(squadMembers);

                    foreach (var member in newLeader.squadMembers)
                    {
                        if (member != null)
                            member.squadLeader = newLeader;
                    }

                    if (teamLeaders.ContainsKey(team))
                    {
                        teamLeaders[team].Remove(this);
                        teamLeaders[team].Add(newLeader);
                    }
                }
            }
            else if (teamLeaders.ContainsKey(team))
            {
                teamLeaders[team].Remove(this);
            }
        }
        else if (squadLeader != null)
        {
            squadLeader.squadMembers.Remove(this);
        }
    }

    void RandomizePersonality()
    {
        // Random personality type
        float roll = Random.value;
        if (roll < 0.3f)
            personality = Personality.Balanced;
        else if (roll < 0.5f)
            personality = Personality.Aggressive;
        else if (roll < 0.7f)
            personality = Personality.Defensive;
        else if (roll < 0.85f)
            personality = Personality.LoneWolf;
        else
            personality = Personality.Camper;

        // Base traits with random variation
        initiative = Random.Range(0.2f, 0.8f);
        bravery = Random.Range(0.3f, 0.9f);
        patience = Random.Range(0.3f, 1f);

        // Modify traits based on personality
        switch (personality)
        {
            case Personality.Aggressive:
                bravery += 0.3f;
                initiative += 0.2f;
                patience -= 0.3f;
                detectionRange *= 1.2f;
                attackRange *= 1.1f;
                break;
            case Personality.Defensive:
                bravery -= 0.2f;
                patience += 0.3f;
                accuracy *= 1.1f;
                break;
            case Personality.LoneWolf:
                initiative += 0.4f;
                bravery += 0.1f;
                moveSpeed *= 1.15f;
                break;
            case Personality.Camper:
                patience += 0.5f;
                bravery -= 0.3f;
                accuracy *= 1.2f;
                captureWaitTime *= 2f;
                break;
        }

        // Clamp values
        initiative = Mathf.Clamp01(initiative);
        bravery = Mathf.Clamp01(bravery);
        patience = Mathf.Clamp01(patience);
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
        string[] skinNames = team == Team.Phantom ? phantomSkinNames : havocSkinNames;

        // Pick a random skin from the team's options
        bool skinFound = false;
        if (skinNames.Length > 0)
        {
            string chosenSkin = skinNames[Random.Range(0, skinNames.Length)];

            // Find and enable the chosen skin
            foreach (Transform child in allChildren)
            {
                if (child.name == chosenSkin)
                {
                    child.gameObject.SetActive(true);
                    skinFound = true;
                    break;
                }
            }
        }

        // FALLBACK: If we couldn't find the chosen skin, enable the FIRST character model we find
        // This prevents the AI from being completely invisible
        if (!skinFound)
        {
            foreach (Transform child in allChildren)
            {
                if (child.name.StartsWith("SM_Chr_"))
                {
                    child.gameObject.SetActive(true);
                    Debug.LogWarning($"{gameObject.name}: Could not find chosen skin, using fallback: {child.name}");
                    break;
                }
            }
        }
    }

    void Update()
    {
        // Skip if this is a player, not an AI bot
        if (!isAIControlled) return;

        if (currentState == AIState.Dead) return;

        // Update global cache once per second (needed for targeting)
        globalCacheTimer -= Time.deltaTime;
        if (globalCacheTimer <= 0f)
        {
            cachedPlayers = FindObjectsOfType<FPSControllerPhoton>();
            cachedAIs = FindObjectsOfType<AIController>();
            globalCacheTimer = 1f;
        }

        // Handle helicopter pilot behavior FIRST
        if (currentState == AIState.HeliPilot)
        {
            UpdateHeliPilotBehavior();
            return;
        }

        // Handle jet pilot behavior
        if (currentState == AIState.JetPilot)
        {
            UpdateJetPilotBehavior();
            return;
        }

        // Handle tank driver behavior
        if (currentState == AIState.TankDriver)
        {
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"[AI] Tank driver state active, calling UpdateTankDriverBehavior");
            }
            UpdateTankDriverBehavior();
            return;
        }

        // Handle tank passenger behavior
        if (currentState == AIState.TankPassenger)
        {
            UpdateTankPassengerBehavior();
            return;
        }

        // Handle helicopter gunner behavior
        if (currentState == AIState.HeliGunner)
        {
            UpdateHeliGunnerBehavior();
            return;
        }

        // Handle helicopter passenger behavior
        if (currentState == AIState.HeliPassenger)
        {
            UpdateHeliPassengerBehavior();
            return;
        }

        // Handle player-led squad behavior
        if (inPlayerSquad)
        {
            UpdatePlayerSquadBehavior();
            return; // Skip normal AI behavior when in player squad
        }

        // Dedicated pilots should always return to their helicopter
        if (isDedicatedPilot && currentState != AIState.HeliPilot)
        {
            CheckAssignedHelicopter();
            return;  // Dedicated pilots don't do normal AI stuff
        }

        // Handle BoardingHelicopter state - dedicated focus on getting to helicopter
        if (currentState == AIState.BoardingHelicopter)
        {
            UpdateBoardingHelicopter();
            return;  // Don't do other AI logic while boarding
        }

        // Decrement helicopter exit cooldown
        if (heliExitCooldown > 0f)
        {
            heliExitCooldown -= Time.deltaTime;
        }

        // BACKUP STUCK DETECTION: If we just exited helicopter and aren't moving, force it
        if (heliExitRecoveryTimer > 0f)
        {
            heliExitRecoveryTimer -= Time.deltaTime;

            // Check if we're stuck (not moving when we should be)
            float movedSinceExit = Vector3.Distance(transform.position, lastHeliExitPosition);

            if (movedSinceExit < 1f && heliExitRecoveryTimer < 8f)  // After 2 seconds, if we haven't moved 1m
            {
                heliExitStuckFrames++;

                if (heliExitStuckFrames > 60)  // Stuck for ~1 second
                {
                    Debug.LogWarning($"[AI BACKUP] {gameObject.name} STUCK after heli exit! Force fixing...");

                    // Nuclear option: Force everything
                    if (agent != null)
                    {
                        // Re-enable agent
                        agent.enabled = true;
                        agentEnabled = true;
                        agent.isStopped = false;
                        agent.speed = moveSpeed;
                        agent.updatePosition = true;
                        agent.updateRotation = true;

                        // Try to get on NavMesh
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(transform.position, out hit, 100f, NavMesh.AllAreas))
                        {
                            agent.Warp(hit.position);
                            transform.position = hit.position;
                        }

                        // Force a destination
                        if (agent.isOnNavMesh && targetPoint != null)
                        {
                            agent.SetDestination(targetPoint.transform.position);
                            currentState = AIState.MovingToPoint;
                        }
                        else if (agent.isOnNavMesh && allCapturePoints.Count > 0)
                        {
                            CapturePoint randomPoint = allCapturePoints[Random.Range(0, allCapturePoints.Count)];
                            if (randomPoint != null)
                            {
                                agent.SetDestination(randomPoint.transform.position);
                                targetPoint = randomPoint;
                                currentState = AIState.MovingToPoint;
                            }
                        }
                    }

                    heliExitStuckFrames = 0;
                    lastHeliExitPosition = transform.position;  // Reset so we check from new position
                }
            }
            else
            {
                heliExitStuckFrames = 0;
                lastHeliExitPosition = transform.position;
            }
        }

        // Check for nearby landed helicopters to board (not in combat, not already in heli, not on cooldown)
        if (currentState != AIState.Combat && currentHelicopter == null && heliExitCooldown <= 0f)
        {
            heliBoardingCheckTimer -= Time.deltaTime;
            if (heliBoardingCheckTimer <= 0f)
            {
                heliBoardingCheckTimer = HELI_BOARDING_CHECK_INTERVAL;
                TryBoardNearbyHelicopter();
            }
        }

        // Occasionally try to pilot a helicopter (low chance per frame)
        if (currentState == AIState.Idle && Random.value < 0.001f)
        {
            TryFindAndPilotHelicopter();
        }

        // Update global frame counter (shared across all bots)
        globalFrameCount++;

        // Always move (smooth movement)
        MoveTowardTarget();

        // Combat actions run every 3 frames (not every frame) for performance
        if (currentState == AIState.Combat)
        {
            combatFrameCounter++;
            if (combatFrameCounter >= 3)
            {
                combatFrameCounter = 0;
                UpdateCombatContinuous();
            }
        }

        // Quick combat check - if enemy is very close, react immediately
        if (currentState != AIState.Combat && currentState != AIState.Dead && cachedAIs != null)
        {
            // Check for enemy AI bots
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this) continue;
                if (!ai.isAIControlled) continue;
                if (ai.currentState == AIState.Dead) continue;
                if (ai.team == team) continue;

                float dist = Vector3.Distance(transform.position, ai.transform.position);
                if (dist <= attackRange)
                {
                    targetEnemy = ai.transform;
                    currentState = AIState.Combat;
                    break;
                }
            }

            // Also check for enemy PLAYERS
            if (currentState != AIState.Combat && cachedPlayers != null)
            {
                foreach (var player in cachedPlayers)
                {
                    if (player == null) continue;
                    if (player.playerTeam == team || player.playerTeam == Team.None) continue;

                    float dist = Vector3.Distance(transform.position, player.transform.position);
                    if (dist <= attackRange)
                    {
                        targetEnemy = player.transform;
                        currentState = AIState.Combat;
                        break;
                    }
                }
            }
        }

        // Staggered thinking - each bot thinks on different frames
        // This spreads CPU load across frames instead of all bots thinking at once
        if ((globalFrameCount + frameOffset) % 60 != 0) return;

        // AI "thinks" roughly once per second (60 frames at 60fps)
        timeSinceSpawn += 1f;

        // FORCE MOVE: If still near spawn after 5 seconds, something is wrong
        float distFromSpawn = Vector3.Distance(transform.position, spawnPosition);
        if (timeSinceSpawn > 5f && distFromSpawn < 3f && currentState != AIState.Dead)
        {
            Debug.LogWarning($"{gameObject.name} STUCK AT SPAWN! state={currentState}, agentEnabled={agentEnabled}, agent.isOnNavMesh={agent?.isOnNavMesh}");

            // Force warp to NavMesh and try again
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position + Random.insideUnitSphere * 3f, out hit, 10f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                if (agent != null)
                {
                    agent.enabled = true;
                    agent.Warp(hit.position);
                    agentEnabled = true;
                }
            }
            currentState = AIState.Idle;
            timeSinceSpawn = 0f; // Reset timer to try again
        }

        // If we're idle or stuck, find something to do
        if (currentState == AIState.Idle ||
            (currentState == AIState.MovingToPoint && !agentEnabled))
        {
            // First ensure we're on NavMesh before trying to find objective
            if (agent != null && !agent.isOnNavMesh)
            {
                NavMeshHit hit;
                float[] searchRadii = { 10f, 25f, 50f, 100f };
                foreach (float radius in searchRadii)
                {
                    if (NavMesh.SamplePosition(transform.position, out hit, radius, NavMesh.AllAreas))
                    {
                        agent.enabled = true;
                        agent.Warp(hit.position);
                        transform.position = hit.position;
                        agentEnabled = true;
                        Debug.Log($"[AI] {gameObject.name} recovery in Update: warped to NavMesh at radius {radius}m");
                        break;
                    }
                }
            }
            FindNextCapturePoint();
        }

        // Request helicopter transport if we're far from all objectives
        if (transportRequestCooldown > 0f)
        {
            transportRequestCooldown -= Time.deltaTime;
        }
        else if (currentState == AIState.MovingToPoint && currentHelicopter == null && heliExitCooldown <= 0f)
        {
            // Check if we're far from our target objective
            if (targetPoint != null)
            {
                float distToObjective = Vector3.Distance(transform.position, targetPoint.transform.position);
                if (distToObjective > 150f)  // More than 150m from objective
                {
                    // Count nearby friendlies who also need transport
                    int nearbyFriendlies = CountNearbyFriendliesNeedingTransport();
                    if (nearbyFriendlies >= 2)  // At least 2 troops together
                    {
                        RequestTransport(transform.position, team, nearbyFriendlies);
                        transportRequestCooldown = TRANSPORT_REQUEST_INTERVAL;
                    }
                }
            }
        }

        // Update target enemy every think cycle
        FindNearestEnemy();

        // Immediately check for nearby enemies and enter combat
        if (targetEnemy != null && currentState != AIState.Combat)
        {
            float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
            if (enemyDist <= attackRange)
            {
                currentState = AIState.Combat;
            }
        }

        // Check if stuck
        CheckIfStuck();

        // State machine
        switch (currentState)
        {
            case AIState.Idle:
                FindNextCapturePoint();
                break;

            case AIState.MovingToPoint:
                UpdateMovingToPoint();
                break;

            case AIState.Capturing:
                UpdateCapturing();
                break;

            case AIState.Combat:
                UpdateCombat();
                break;
        }
    }

    void SetDestination(Vector3 destination)
    {
        if (agent == null) return;

        // Enable agent if not already
        if (!agentEnabled)
        {
            // Try increasingly larger search radii to find NavMesh
            NavMeshHit hit;
            float[] searchRadii = { 5f, 15f, 30f, 50f, 100f };
            bool foundNavMesh = false;

            foreach (float radius in searchRadii)
            {
                if (NavMesh.SamplePosition(transform.position, out hit, radius, NavMesh.AllAreas))
                {
                    agent.enabled = true;
                    agent.Warp(hit.position);
                    transform.position = hit.position;
                    agentEnabled = true;
                    foundNavMesh = true;
                    break;
                }
            }

            if (!foundNavMesh)
            {
                Debug.LogWarning($"{gameObject.name} not on NavMesh even with large search radius!");
                return;
            }
        }

        // Make sure destination is on NavMesh (use larger search radius)
        NavMeshHit destHit;
        Vector3 finalDest = destination;
        if (NavMesh.SamplePosition(destination, out destHit, 20f, NavMesh.AllAreas))
        {
            finalDest = destHit.position;
        }

        // Set destination
        if (agent.isOnNavMesh)
        {
            agent.SetDestination(finalDest);
            targetPosition = finalDest;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name} agent not on NavMesh!");
        }
    }

    void StopAgent()
    {
        if (agent != null && agentEnabled)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            agent.enabled = false;
            agentEnabled = false;
        }
    }

    void UpdateAnimation(bool isMoving, bool isSprinting = false)
    {
        if (animator == null) return;

        // Calculate target speed: 0 = idle, 0.5 = walk, 1.0 = run
        float targetSpeed = 0f;
        if (isMoving)
        {
            targetSpeed = isSprinting ? 1f : 0.5f;
        }

        // Smoothly transition animation
        if (!string.IsNullOrEmpty(animSpeedParam))
        {
            float currentSpeed = animator.GetFloat(animSpeedParam);
            float smoothedSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 8f);
            animator.SetFloat(animSpeedParam, smoothedSpeed);
        }

        if (!string.IsNullOrEmpty(animMovingParam))
        {
            animator.SetBool(animMovingParam, isMoving);
        }

        // Adjust playback speed for better foot sync
        animator.speed = isMoving ? (isSprinting ? 1.0f : 0.8f) : 1f;
    }

    // ============ INTELLIGENCE SYSTEM ============

    // Evaluate the tactical situation before engaging
    ThreatAssessment AssessThreat()
    {
        ThreatAssessment assessment = new ThreatAssessment();

        // Count nearby enemies and allies
        float checkRadius = detectionRange;
        int nearbyEnemies = 0;
        int nearbyAllies = 0;
        float closestEnemyDist = float.MaxValue;
        float totalEnemyHealth = 0f;
        float totalAllyHealth = currentHealth;

        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.currentState == AIState.Dead) continue;

                float dist = Vector3.Distance(transform.position, ai.transform.position);
                if (dist > checkRadius) continue;

                if (ai.team == team)
                {
                    nearbyAllies++;
                    totalAllyHealth += ai.currentHealth;
                }
                else
                {
                    nearbyEnemies++;
                    totalEnemyHealth += ai.currentHealth;
                    if (dist < closestEnemyDist) closestEnemyDist = dist;
                }
            }
        }

        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null) continue;
                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist > checkRadius) continue;

                if (player.playerTeam != team && player.playerTeam != Team.None)
                {
                    nearbyEnemies++;
                    totalEnemyHealth += 100f; // Assume players have full health
                    if (dist < closestEnemyDist) closestEnemyDist = dist;
                }
            }
        }

        assessment.nearbyEnemies = nearbyEnemies;
        assessment.nearbyAllies = nearbyAllies;
        assessment.outnumbered = nearbyEnemies > nearbyAllies + 1;
        assessment.hasBackup = nearbyAllies > 0;
        assessment.healthAdvantage = totalAllyHealth > totalEnemyHealth;
        assessment.closestEnemyDistance = closestEnemyDist;

        // Calculate threat level (0 = safe, 1 = extreme danger)
        assessment.threatLevel = 0f;
        if (nearbyEnemies > 0) assessment.threatLevel += 0.3f;
        if (assessment.outnumbered) assessment.threatLevel += 0.3f;
        if (currentHealth < maxHealth * 0.3f) assessment.threatLevel += 0.2f;
        if (!assessment.hasBackup) assessment.threatLevel += 0.1f;
        if (suppressedTimer > 0f) assessment.threatLevel += 0.1f;

        assessment.threatLevel = Mathf.Clamp01(assessment.threatLevel);

        // Decision: should we engage?
        assessment.shouldEngage = true;

        // Don't engage if heavily outnumbered (unless aggressive/confident)
        if (assessment.outnumbered && !assessment.hasBackup)
        {
            if (personality != Personality.Aggressive && confidence < 0.7f)
            {
                assessment.shouldEngage = false;
            }
        }

        // Don't engage at low health unless cornered
        if (currentHealth < maxHealth * 0.2f && assessment.closestEnemyDistance > 10f)
        {
            assessment.shouldEngage = false;
        }

        return assessment;
    }

    struct ThreatAssessment
    {
        public int nearbyEnemies;
        public int nearbyAllies;
        public bool outnumbered;
        public bool hasBackup;
        public bool healthAdvantage;
        public float closestEnemyDistance;
        public float threatLevel;
        public bool shouldEngage;
    }

    // Should I wait for backup before pushing?
    bool ShouldWaitForSquad()
    {
        if (squadRole != SquadRole.Leader) return false;
        if (personality == Personality.Aggressive) return false;
        if (personality == Personality.LoneWolf) return false;

        // Check if squad members are nearby
        int nearbyMembers = 0;
        foreach (var member in squadMembers)
        {
            if (member == null || member.currentState == AIState.Dead) continue;
            float dist = Vector3.Distance(transform.position, member.transform.position);
            if (dist < 15f) nearbyMembers++;
        }

        // Wait if squad is scattered
        return nearbyMembers < squadMembers.Count && squadMembers.Count > 0;
    }

    // Decide if this is a good ambush spot
    bool IsGoodAmbushPosition()
    {
        if (personality == Personality.Aggressive) return false; // Too impatient

        // Near a capture point we own?
        if (targetPoint != null && targetPoint.owningTeam == team)
        {
            float distToPoint = Vector3.Distance(transform.position, targetPoint.transform.position);
            if (distToPoint < targetPoint.captureRadius * 1.5f)
            {
                return true; // Good defensive position
            }
        }

        // Campers love to ambush
        if (personality == Personality.Camper && Random.value < 0.3f)
        {
            return true;
        }

        return false;
    }

    // ============ COVER SYSTEM ============

    // Find nearby cover relative to a threat
    Vector3 FindCoverPosition(Vector3 threatPos)
    {
        Vector3 bestCover = Vector3.zero;
        float bestScore = -1f;

        // Search for cover in a radius
        float searchRadius = 15f;
        Collider[] nearby = Physics.OverlapSphere(transform.position, searchRadius);

        foreach (var col in nearby)
        {
            // Skip small objects and triggers
            if (col.isTrigger) continue;
            if (col.bounds.size.magnitude < 0.5f) continue;

            // Check if this object can provide cover
            Vector3 coverPos = GetCoverPointBehindObject(col, threatPos);
            if (coverPos == Vector3.zero) continue;

            // Score this cover position
            float score = ScoreCoverPosition(coverPos, threatPos, col);
            if (score > bestScore)
            {
                bestScore = score;
                bestCover = coverPos;
            }
        }

        coverQuality = bestScore;
        return bestCover;
    }

    Vector3 GetCoverPointBehindObject(Collider coverObject, Vector3 threatPos)
    {
        // Get the direction from threat to cover object
        Vector3 coverCenter = coverObject.bounds.center;
        Vector3 dirFromThreat = (coverCenter - threatPos).normalized;

        // Position behind the cover relative to threat
        Vector3 coverPoint = coverCenter + dirFromThreat * (coverObject.bounds.extents.magnitude + 1f);
        coverPoint.y = transform.position.y; // Keep at our height

        // Check if position is on NavMesh
        NavMeshHit navHit;
        if (NavMesh.SamplePosition(coverPoint, out navHit, 2f, NavMesh.AllAreas))
        {
            // Verify we can actually reach it
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, navHit.position, NavMesh.AllAreas, path))
            {
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    return navHit.position;
                }
            }
        }

        return Vector3.zero;
    }

    float ScoreCoverPosition(Vector3 coverPos, Vector3 threatPos, Collider coverObject)
    {
        float score = 0f;

        // Distance from current position (closer is better, but not too close)
        float distFromMe = Vector3.Distance(transform.position, coverPos);
        if (distFromMe < 2f) score -= 0.2f;  // Too close, not worth moving
        else if (distFromMe < 8f) score += 0.3f;  // Good distance
        else if (distFromMe < 15f) score += 0.1f;  // Acceptable
        else return -1f;  // Too far

        // Check if cover actually blocks line of sight to threat
        Vector3 eyeHeight = coverPos + Vector3.up * 1.5f;
        if (Physics.Linecast(eyeHeight, threatPos + Vector3.up * 1.5f, out RaycastHit hit))
        {
            if (hit.collider == coverObject || Vector3.Distance(hit.point, threatPos) > 2f)
            {
                score += 0.5f;  // Good cover!
            }
        }

        // Prefer cover that still allows us to shoot (peek potential)
        Vector3 peekLeft = coverPos + Vector3.Cross(Vector3.up, (threatPos - coverPos).normalized) * 1.5f;
        Vector3 peekRight = coverPos - Vector3.Cross(Vector3.up, (threatPos - coverPos).normalized) * 1.5f;

        if (!Physics.Linecast(peekLeft + Vector3.up * 1.5f, threatPos + Vector3.up * 1.5f))
            score += 0.2f;
        if (!Physics.Linecast(peekRight + Vector3.up * 1.5f, threatPos + Vector3.up * 1.5f))
            score += 0.2f;

        // Defensive personalities prefer better cover
        if (personality == Personality.Defensive || personality == Personality.Camper)
            score *= 1.3f;

        return score;
    }

    void MoveToCover(Vector3 coverPos, Vector3 threatPos)
    {
        currentCoverPosition = coverPos;
        coverPeekDirection = (threatPos - coverPos).normalized;
        isInCover = false;  // Will be true when we arrive
        SetDestination(coverPos);
    }

    void UpdateCoverBehavior()
    {
        if (!isInCover || targetEnemy == null) return;

        peekTimer -= Time.deltaTime;

        if (isPeeking)
        {
            // While peeking, shoot at enemy
            if (peekTimer <= 0f)
            {
                // Stop peeking, get back in cover
                isPeeking = false;
                peekTimer = Random.Range(1f, 3f) * (personality == Personality.Aggressive ? 0.5f : 1f);
            }
        }
        else
        {
            // In cover, wait for good moment to peek
            if (peekTimer <= 0f && targetEnemy != null)
            {
                isPeeking = true;
                peekTimer = Random.Range(1.5f, 4f);  // How long to peek
            }
        }
    }

    // ============ FLANKING SYSTEM ============

    bool ShouldAttemptFlank()
    {
        if (targetEnemy == null) return false;
        if (personality == Personality.Defensive || personality == Personality.Camper) return false;

        // Check if enemy is focused on someone else
        AIController targetAI = targetEnemy.GetComponent<AIController>();
        FPSControllerPhoton targetPlayer = targetEnemy.GetComponent<FPSControllerPhoton>();

        if (targetAI != null && targetAI.targetEnemy != null && targetAI.targetEnemy != transform)
        {
            enemyIsDistracted = true;
            return true;
        }

        // Check if we have allies engaging the same target
        if (cachedAIs != null)
        {
            foreach (var ally in cachedAIs)
            {
                if (ally == null || ally == this || ally.team != team) continue;
                if (ally.currentState != AIState.Combat) continue;

                if (ally.targetEnemy == targetEnemy)
                {
                    distractingAlly = ally;
                    enemyIsDistracted = true;
                    return Random.value < 0.4f;  // 40% chance to flank when ally is engaging
                }
            }
        }

        return false;
    }

    Vector3 CalculateFlankPosition()
    {
        if (targetEnemy == null) return Vector3.zero;

        Vector3 enemyPos = targetEnemy.position;
        Vector3 enemyForward = targetEnemy.forward;

        // Try to get behind or to the side of the enemy
        float flankAngle = Random.value > 0.5f ? 90f : -90f;  // Left or right
        if (personality == Personality.Aggressive)
            flankAngle = Random.Range(120f, 180f) * (Random.value > 0.5f ? 1f : -1f);  // More aggressive = try to get behind

        Vector3 flankDir = Quaternion.Euler(0, flankAngle, 0) * enemyForward;
        Vector3 flankPos = enemyPos + flankDir * Random.Range(8f, 15f);

        // Validate position is on NavMesh
        NavMeshHit navHit;
        if (NavMesh.SamplePosition(flankPos, out navHit, 5f, NavMesh.AllAreas))
        {
            // Check we can path there
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, navHit.position, NavMesh.AllAreas, path))
            {
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    return navHit.position;
                }
            }
        }

        return Vector3.zero;
    }

    void ExecuteFlank()
    {
        if (flankTarget == Vector3.zero || targetEnemy == null)
        {
            isFlankingTarget = false;
            return;
        }

        float distToFlankPos = Vector3.Distance(transform.position, flankTarget);

        if (distToFlankPos < 3f)
        {
            // Reached flank position - attack!
            isFlankingTarget = false;
            currentState = AIState.Combat;
        }
        else
        {
            // Keep moving to flank position
            SetDestination(flankTarget);
        }
    }

    // ============ AIM PREDICTION SYSTEM ============

    Vector3 PredictEnemyPosition(float predictionTime = 0.2f)
    {
        if (targetEnemy == null) return Vector3.zero;

        Vector3 currentPos = targetEnemy.position;

        // Calculate enemy velocity
        if (previousEnemyPos != Vector3.zero)
        {
            lastEnemyVelocity = (currentPos - previousEnemyPos) / Time.deltaTime;
        }
        previousEnemyPos = currentPos;

        // Predict where enemy will be
        Vector3 predicted = currentPos + lastEnemyVelocity * predictionTime;

        // Add skill-based inaccuracy
        float skillFactor = accuracy * (1f - aimOffset);
        predicted = Vector3.Lerp(currentPos, predicted, skillFactor);

        return predicted;
    }

    Vector3 GetAimPoint()
    {
        if (targetEnemy == null) return transform.position + transform.forward * 10f;

        // Base aim point with prediction
        Vector3 baseAim = PredictEnemyPosition(0.15f);

        // Aim for head if confident and skilled
        float headChance = accuracy * confidence * 0.5f;
        if (personality == Personality.Aggressive) headChance *= 1.2f;

        if (Random.value < headChance)
        {
            isAimingForHead = true;
            baseAim += Vector3.up * 1.6f;  // Head height
        }
        else
        {
            isAimingForHead = false;
            baseAim += Vector3.up * 1.0f;  // Center mass
        }

        // Add accuracy-based spread
        float spread = (1f - accuracy) * 2f;
        spread *= Mathf.Lerp(1.5f, 0.5f, aimSettleTimer / 2f);  // More accurate when aiming longer

        // More spread when moving or suppressed
        if (agent != null && agent.velocity.magnitude > 1f) spread *= 1.5f;
        if (suppressedTimer > 0f) spread *= 2f;

        // Distance-based accuracy falloff - much harder to hit at range
        float distToTarget = Vector3.Distance(transform.position, targetEnemy.position);
        if (distToTarget > 20f)
        {
            // Spread increases significantly with distance
            // At 20m: 1x spread, at 50m: 2.5x spread, at 100m: 5x spread
            float distanceFactor = 1f + (distToTarget - 20f) / 20f;
            spread *= distanceFactor;
        }

        // Extra penalty for shooting at targets in vehicles (harder to hit)
        FPSControllerPhoton targetPlayer = targetEnemy.GetComponent<FPSControllerPhoton>();
        if (targetPlayer != null && targetPlayer.IsInVehicle)
        {
            spread *= 2f;  // Much harder to hit someone in a vehicle
        }

        baseAim += new Vector3(
            Random.Range(-spread, spread),
            Random.Range(-spread * 0.5f, spread * 0.5f),
            Random.Range(-spread, spread)
        );

        return baseAim;
    }

    void UpdateAimTracking()
    {
        if (targetEnemy != null)
        {
            aimSettleTimer += Time.deltaTime;
            aimSettleTimer = Mathf.Min(aimSettleTimer, 3f);

            // Decrease aim offset over time (getting more accurate)
            aimOffset = Mathf.Lerp(aimOffset, 0f, Time.deltaTime * 0.5f);
        }
        else
        {
            aimSettleTimer = 0f;
            aimOffset = 0.3f;  // Reset inaccuracy when changing targets
        }
    }

    // ============ TEAM COORDINATION SYSTEM ============

    void CalloutEnemyPosition(Vector3 enemyPos)
    {
        if (Time.time - lastCalloutTime < 3f) return;  // Don't spam callouts
        lastCalloutTime = Time.time;

        // Share enemy position with team
        if (!sharedEnemyPositions.ContainsKey(team))
            sharedEnemyPositions[team] = new List<Vector3>();

        // Remove old positions
        sharedEnemyPositions[team].RemoveAll(p => Vector3.Distance(p, enemyPos) < 10f);
        sharedEnemyPositions[team].Add(enemyPos);

        // Limit list size
        while (sharedEnemyPositions[team].Count > 10)
            sharedEnemyPositions[team].RemoveAt(0);
    }

    Vector3 GetSharedEnemyPosition()
    {
        if (!sharedEnemyPositions.ContainsKey(team)) return Vector3.zero;
        if (sharedEnemyPositions[team].Count == 0) return Vector3.zero;

        // Find closest shared enemy position
        Vector3 closest = Vector3.zero;
        float closestDist = float.MaxValue;

        foreach (var pos in sharedEnemyPositions[team])
        {
            float dist = Vector3.Distance(transform.position, pos);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = pos;
            }
        }

        return closest;
    }

    bool ShouldProvideCoverFire()
    {
        if (personality == Personality.LoneWolf) return false;
        if (currentHealth < maxHealth * 0.3f) return false;

        // Check if any ally is moving towards enemy
        if (cachedAIs == null) return false;

        foreach (var ally in cachedAIs)
        {
            if (ally == null || ally == this || ally.team != team) continue;
            if (ally.currentState != AIState.MovingToPoint && ally.currentState != AIState.Combat) continue;

            // Is ally pushing towards an enemy we can see?
            if (targetEnemy != null && ally.targetEnemy == targetEnemy)
            {
                float allyDistToEnemy = Vector3.Distance(ally.transform.position, targetEnemy.position);
                float myDistToEnemy = Vector3.Distance(transform.position, targetEnemy.position);

                // If ally is closer and pushing, provide cover
                if (allyDistToEnemy < myDistToEnemy)
                {
                    coveringFor = ally;
                    return true;
                }
            }
        }

        return false;
    }

    void ProvideCoverFire()
    {
        if (targetEnemy == null || coveringFor == null) return;

        // Shoot more aggressively to suppress enemy
        if (Time.time >= nextFireTime && reloadTimer <= 0f)
        {
            Vector3 suppressionPoint = targetEnemy.position + Random.insideUnitSphere * 2f;
            suppressionPoint.y = targetEnemy.position.y + 1f;

            // Face the enemy
            Vector3 lookDir = (suppressionPoint - transform.position).normalized;
            lookDir.y = 0;
            transform.rotation = Quaternion.LookRotation(lookDir);

            Shoot();
            nextFireTime = Time.time + fireRate * 0.7f;  // Faster fire rate for suppression
        }
    }

    bool TryCoordinatedPush()
    {
        if (personality == Personality.LoneWolf || personality == Personality.Camper) return false;
        if (!lastCoordinatedPush.ContainsKey(team))
            lastCoordinatedPush[team] = 0f;

        if (Time.time - lastCoordinatedPush[team] < 10f) return false;  // Cooldown

        // Count nearby ready allies
        int readyAllies = 0;
        if (cachedAIs != null)
        {
            foreach (var ally in cachedAIs)
            {
                if (ally == null || ally == this || ally.team != team) continue;
                if (ally.currentState == AIState.Dead) continue;

                float dist = Vector3.Distance(transform.position, ally.transform.position);
                if (dist < 20f && ally.currentHealth > ally.maxHealth * 0.5f)
                {
                    readyAllies++;
                }
            }
        }

        // Need at least 2 allies to coordinate push
        if (readyAllies >= 2)
        {
            lastCoordinatedPush[team] = Time.time;
            return true;
        }

        return false;
    }

    // Update confidence based on events
    void UpdateConfidence(float delta)
    {
        confidence = Mathf.Clamp01(confidence + delta);
    }

    // Track deaths for adaptive learning
    void RememberDeath()
    {
        recentDeaths++;
        lastDeathTime = Time.time;
        lastDeathPosition = transform.position;

        // Lose confidence on death
        UpdateConfidence(-0.2f);
    }

    // Should I avoid an area where I died?
    bool IsNearDeathZone(Vector3 position)
    {
        if (Time.time - lastDeathTime > 60f) return false; // Forget after 1 min
        if (lastDeathPosition == Vector3.zero) return false;

        float dist = Vector3.Distance(position, lastDeathPosition);
        return dist < 15f && recentDeaths > 0;
    }

    Vector3 GetAvoidanceDirection()
    {
        float checkDistance = 2.5f;

        // Check multiple directions and find the clearest path
        float[] angles = { 0f, -30f, 30f, -60f, 60f, -90f, 90f, -120f, 120f, 180f };
        float[] distances = new float[angles.Length];

        float maxDist = 0f;
        int bestIndex = 0;

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 dir = Quaternion.Euler(0, angles[i], 0) * transform.forward;

            // Use SphereCast to detect thin obstacles like poles
            // Check at multiple heights to catch tall thin objects
            float minDist = checkDistance;

            // Check at 3 heights: knee, waist, chest
            float[] heights = { 0.3f, 0.8f, 1.3f };
            foreach (float h in heights)
            {
                Vector3 origin = transform.position + Vector3.up * h;
                RaycastHit hit;

                // SphereCast with 0.3m radius catches thin poles better than Raycast
                if (Physics.SphereCast(origin, 0.3f, dir, out hit, checkDistance))
                {
                    if (hit.distance < minDist)
                    {
                        minDist = hit.distance;
                    }
                }
            }

            distances[i] = minDist;

            if (distances[i] > maxDist)
            {
                maxDist = distances[i];
                bestIndex = i;
            }
        }

        // If forward is mostly clear (>80% of check distance), no avoidance needed
        if (distances[0] > checkDistance * 0.8f)
        {
            return Vector3.zero;
        }

        // Return the clearest direction
        Vector3 bestDir = Quaternion.Euler(0, angles[bestIndex], 0) * transform.forward;

        // If even the best direction is blocked, we're stuck
        if (maxDist < 0.5f)
        {
            return -transform.forward; // Back up
        }

        return bestDir;
    }

    // Additional ground check to prevent walking off edges
    bool IsGroundAhead(Vector3 direction, float distance)
    {
        Vector3 checkPos = transform.position + direction.normalized * distance + Vector3.up * 0.5f;
        return Physics.Raycast(checkPos, Vector3.down, 2f);
    }

    private float heliExitDebugTimer = 0f;

    void MoveTowardTarget()
    {
        if (currentState == AIState.Capturing || currentState == AIState.Dead || currentState == AIState.HeliGunner || currentState == AIState.HeliPilot)
        {
            StopAgent();
            UpdateAnimation(false);
            return;
        }

        // Debug: Track movement after helicopter exit
        if (heliExitDebugTimer > 0f)
        {
            heliExitDebugTimer -= Time.deltaTime;
            Debug.Log($"[MOVE DEBUG] {gameObject.name}: state={currentState}, agentEnabled={agentEnabled}, " +
                      $"agent.enabled={agent?.enabled}, isOnNavMesh={agent?.isOnNavMesh}, " +
                      $"hasPath={agent?.hasPath}, pathPending={agent?.pathPending}, " +
                      $"velocity={agent?.velocity.magnitude:F2}, speed={agent?.speed}, " +
                      $"isStopped={agent?.isStopped}, remainingDist={agent?.remainingDistance:F1}, " +
                      $"pos={transform.position}, destination={agent?.destination}");
        }

        // AGGRESSIVE FIX: If we should be moving but agent isn't on NavMesh, fix it NOW
        if (currentState == AIState.MovingToPoint && agent != null && !agent.isOnNavMesh)
        {
            // Try to get on NavMesh immediately
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 50f, NavMesh.AllAreas))
            {
                agent.enabled = true;
                agentEnabled = true;
                agent.Warp(hit.position);
                transform.position = hit.position;
            }
        }

        // AGGRESSIVE FIX: If agent is on NavMesh but not enabled, enable it
        if (agent != null && agent.isOnNavMesh && (!agent.enabled || !agentEnabled))
        {
            agent.enabled = true;
            agentEnabled = true;
            agent.isStopped = false;
            agent.speed = moveSpeed;
        }

        // NavMeshAgent handles movement - we just update animation
        if (agent != null && agentEnabled && agent.isOnNavMesh)
        {
            // Make sure agent isn't stopped
            if (agent.isStopped && currentState == AIState.MovingToPoint)
            {
                agent.isStopped = false;
            }

            // Check if agent is moving
            float velocity = agent.velocity.magnitude;
            bool isMoving = velocity > 0.1f;

            // Sprint if moving fast or if aggressive personality
            bool isSprinting = velocity > moveSpeed * 0.7f ||
                              (targetEnemy != null && personality == Personality.Aggressive);

            UpdateAnimation(isMoving, isSprinting);

            // If agent reached destination or has no path, we're done
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                UpdateAnimation(false);

                // Check if we arrived at cover position
                if (currentCoverPosition != Vector3.zero &&
                    Vector3.Distance(transform.position, currentCoverPosition) < 2f)
                {
                    isInCover = true;
                    isPeeking = false;
                    peekTimer = Random.Range(0.5f, 1.5f);  // Wait before first peek
                    StopAgent();
                }
            }
        }
        else
        {
            UpdateAnimation(false);
        }
    }

    private int stuckAttempts = 0;
    private Vector3 lastStuckPosition;

    void CheckIfStuck()
    {
        if (agent == null || !agentEnabled) return;

        float moved = Vector3.Distance(transform.position, lastPosition);

        if (moved < 0.15f && currentState == AIState.MovingToPoint)
        {
            stuckTimer += 1f;

            if (stuckTimer > 3f)
            {
                stuckAttempts++;

                if (stuckAttempts <= 2)
                {
                    // Warp agent to valid NavMesh position and retry
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(transform.position, out hit, 2f, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                    }
                    if (targetPoint != null)
                    {
                        SetDestination(targetPoint.transform.position);
                    }
                }
                else
                {
                    // Pick a different destination
                    FindNextCapturePoint();
                    stuckAttempts = 0;
                }

                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
            if (moved > 0.5f)
            {
                stuckAttempts = 0;
            }
        }
        lastPosition = transform.position;
    }

    void FindNextCapturePoint()
    {
        // === SQUAD MEMBER BEHAVIOR ===
        // If we're a squad member, follow our leader
        if (squadRole == SquadRole.Member && squadLeader != null && squadLeader != this)
        {
            // Check if leader is alive
            if (squadLeader.currentState == AIState.Dead)
            {
                // Leader died, we should have been promoted or reassigned
                AssignToSquad();
            }
            else if (squadLeader.targetPoint != null)
            {
                // Follow leader's objective
                targetPoint = squadLeader.targetPoint;

                // Go to a position near leader, not exact same spot
                Vector3 destination = squadLeader.transform.position;
                Vector3 offset = (transform.position - squadLeader.transform.position).normalized * 3f;
                if (offset.magnitude < 0.5f)
                {
                    offset = Random.insideUnitSphere * 3f;
                    offset.y = 0;
                }
                destination += offset;

                SetDestination(destination);
                currentState = AIState.MovingToPoint;
                return;
            }
            // If leader has no target, don't wait - find our own objective below
        }

        // === LEADER / LONE WOLF DECISION MAKING ===

        // High initiative bots might randomly ignore objectives
        if (personality == Personality.LoneWolf && Random.value < initiative * 0.3f)
        {
            if (targetEnemy != null)
            {
                SetDestination(targetEnemy.position);
                currentState = AIState.MovingToPoint;
                return;
            }
        }

        // Analyze the battlefield
        int ourPoints = 0;
        int enemyPoints = 0;
        int neutralPoints = 0;
        CapturePoint contestedPoint = null;

        foreach (var point in allCapturePoints)
        {
            if (point.owningTeam == team) ourPoints++;
            else if (point.owningTeam == Team.None) neutralPoints++;
            else enemyPoints++;

            if (point.isContested) contestedPoint = point;
        }

        CapturePoint bestPoint = null;
        float bestScore = float.MinValue;

        foreach (var point in allCapturePoints)
        {
            float score = 0f;
            float distance = Vector3.Distance(transform.position, point.transform.position);

            // === STRATEGIC SCORING ===

            // Skip owned non-contested points (unless we're defensive/camper or it's our only option)
            if (point.owningTeam == team && !point.isContested)
            {
                // Only consider defending if defensive personality or all points owned
                bool shouldDefend = personality == Personality.Defensive ||
                                    personality == Personality.Camper ||
                                    (enemyPoints == 0 && neutralPoints == 0);
                if (!shouldDefend) continue;
            }

            // Contested points are urgent!
            if (point.isContested)
            {
                if (point.owningTeam == team)
                    score = 150f; // Defend our contested point!
                else
                    score = 120f; // Help take contested point
            }
            // Enemy points
            else if (point.owningTeam != team && point.owningTeam != Team.None)
            {
                score = 100f;
                if (personality == Personality.Aggressive) score += 30f;

                // If we're losing, prioritize attack
                if (enemyPoints > ourPoints) score += 30f;
            }
            // Neutral points
            else if (point.owningTeam == Team.None)
            {
                score = 70f;
                // Early game - grab neutrals fast
                if (ourPoints == 0) score += 50f;
            }
            // Our points (only reached if defensive/camper or no other options)
            else
            {
                score = 10f;
                if (personality == Personality.Defensive) score += 40f;
                if (personality == Personality.Camper) score += 60f;

                // If we're winning, defend more
                if (ourPoints > enemyPoints) score += 20f;
            }

            // Don't all go to same point - check if other squads are going there
            int squadsGoingHere = 0;
            if (teamLeaders.ContainsKey(team))
            {
                foreach (var leader in teamLeaders[team])
                {
                    if (leader != null && leader != this && leader.targetPoint == point)
                        squadsGoingHere++;
                }
            }
            score -= squadsGoingHere * 40f; // Spread out squads

            // Avoid death zones - learned behavior
            if (IsNearDeathZone(point.transform.position))
            {
                score -= 50f; // Avoid where we died recently
                // Unless we're confident or aggressive
                if (confidence > 0.7f || personality == Personality.Aggressive)
                {
                    score += 30f; // Confident bots go back for revenge
                }
            }

            // Confidence affects aggression
            if (confidence > 0.7f)
            {
                // High confidence - prefer attacking
                if (point.owningTeam != team) score += 20f;
            }
            else if (confidence < 0.3f)
            {
                // Low confidence - prefer defending
                if (point.owningTeam == team) score += 30f;
            }

            // Distance preference
            if (personality == Personality.LoneWolf)
            {
                score -= distance * 0.2f;
                score += Random.Range(-30f, 30f);
            }
            else
            {
                score -= distance * 0.5f;
            }

            // Randomness
            score += Random.Range(-10f, 10f) * (1f + initiative);

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        if (bestPoint != null)
        {
            targetPoint = bestPoint;

            // Add random offset
            Vector3 destination = targetPoint.transform.position;
            float offsetRadius = targetPoint.captureRadius * (0.3f + initiative * 0.4f);
            Vector2 randomOffset = Random.insideUnitCircle * offsetRadius;
            destination += new Vector3(randomOffset.x, 0, randomOffset.y);
            SetDestination(destination);
            currentState = AIState.MovingToPoint;

        }
        else
        {
            Debug.LogWarning($"{gameObject.name} could not find a capture point! allCapturePoints.Count={allCapturePoints.Count}");

            // Fallback: just wander forward
            if (allCapturePoints.Count == 0)
            {
                allCapturePoints.AddRange(FindObjectsOfType<CapturePoint>());
            }
        }
    }

    void UpdateMovingToPoint()
    {
        // Squad coordination - help leader in combat
        if (squadRole == SquadRole.Member && squadLeader != null && squadLeader.currentState == AIState.Combat)
        {
            // Leader is fighting - join the fight!
            if (squadLeader.targetEnemy != null)
            {
                float distToFight = Vector3.Distance(transform.position, squadLeader.targetEnemy.position);
                if (distToFight <= detectionRange)
                {
                    targetEnemy = squadLeader.targetEnemy;
                    currentState = AIState.Combat;
                    return;
                }
                else
                {
                    // Move toward the fight
                    SetDestination(squadLeader.transform.position);
                }
            }
        }

        // Check for enemies
        if (targetEnemy != null)
        {
            float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
            if (enemyDist <= attackRange)
            {
                currentState = AIState.Combat;
                targetPosition = transform.position; // Stop moving
                return;
            }
        }

        // Check if we reached the point
        if (targetPoint != null)
        {
            float dist = Vector3.Distance(transform.position, targetPoint.transform.position);
            if (dist <= targetPoint.captureRadius)
            {
                currentState = AIState.Capturing;
                captureTimer = 0f;
            }
        }
        else
        {
            currentState = AIState.Idle;
        }
    }

    void UpdateCapturing()
    {
        // Check for enemies while capturing
        if (targetEnemy != null)
        {
            float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
            // Aggressive bots will chase enemies even while capturing
            float engageRange = attackRange * (personality == Personality.Aggressive ? 1.5f : 1f);
            if (enemyDist <= engageRange)
            {
                // Low patience bots are more likely to abandon capture for a fight
                if (personality == Personality.Aggressive || Random.value > patience * 0.7f)
                {
                    currentState = AIState.Combat;
                    return;
                }
            }
        }

        // Impatient bots might randomly leave even our own points
        if (targetPoint != null && targetPoint.owningTeam == team)
        {
            captureTimer += 1f; // Using think rate

            // Impatient/aggressive bots get bored faster
            float effectiveWaitTime = captureWaitTime * patience;
            if (personality == Personality.Aggressive) effectiveWaitTime *= 0.5f;
            if (personality == Personality.LoneWolf) effectiveWaitTime *= 0.3f;

            if (captureTimer >= effectiveWaitTime)
            {
                currentState = AIState.Idle;
            }

            // High initiative bots might just leave randomly
            if (initiative > 0.6f && Random.value < (1f - patience) * 0.1f)
            {
                currentState = AIState.Idle;
            }
        }
        else if (targetPoint != null && targetPoint.owningTeam != team)
        {
            captureTimer = 0f;
        }
        else
        {
            currentState = AIState.Idle;
        }
    }

    // Runs every frame during combat - handles aiming, movement, shooting
    void UpdateCombatContinuous()
    {
        if (targetEnemy == null) return;

        // Update aim tracking system
        UpdateAimTracking();

        // Decrement timers every frame
        suppressedTimer -= Time.deltaTime;
        strafeTimer -= Time.deltaTime;
        reloadTimer -= Time.deltaTime;
        coverSearchTimer -= Time.deltaTime;
        flankTimer -= Time.deltaTime;

        float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
        float healthPercent = currentHealth / maxHealth;

        // === COVER BEHAVIOR ===
        if (isInCover)
        {
            UpdateCoverBehavior();
            if (!isPeeking)
            {
                UpdateAnimation(false);
                return;  // Stay in cover, don't move or shoot
            }
        }

        // === FLANKING BEHAVIOR ===
        if (isFlankingTarget)
        {
            ExecuteFlank();
            // While flanking, don't engage directly - stay quiet
            if (enemyDist > attackRange * 0.5f)
            {
                // Face movement direction, not enemy
                if (agent != null && agent.velocity.magnitude > 0.5f)
                {
                    Vector3 moveDir = agent.velocity.normalized;
                    moveDir.y = 0;
                    if (moveDir != Vector3.zero)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), rotationSpeed * Time.deltaTime);
                }
                UpdateAnimation(true);
                return;
            }
            // Close enough to attack from flank!
            isFlankingTarget = false;
        }

        // === COVER FIRE BEHAVIOR ===
        if (isProvidingCoverFire && coveringFor != null)
        {
            ProvideCoverFire();
            UpdateAnimation(false);
            return;
        }

        // Disable NavMeshAgent during direct combat for manual strafing
        if (agentEnabled && !isFlankingTarget)
        {
            StopAgent();
        }

        // === FACING with PREDICTION ===
        Vector3 aimPoint = GetAimPoint();
        Vector3 lookDir = (aimPoint - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            float aimSpeed = rotationSpeed * 2f * (1f + aimSettleTimer * 0.5f);  // Faster when settled
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, aimSpeed * Time.deltaTime);
        }

        // === TACTICAL MOVEMENT ===
        Vector3 moveDirection = Vector3.zero;
        bool isMoving = false;

        // Strafe during combat (change direction periodically)
        if (strafeTimer <= 0f)
        {
            strafeTimer = Random.Range(0.5f, 2f);
            if (Random.value > 0.6f)
            {
                strafeDirection *= -1f;
            }
        }

        // Different behavior based on situation
        if (suppressedTimer > 0f)
        {
            // Being shot at - seek cover or retreat
            if (healthPercent < 0.5f && coverSearchTimer <= 0f)
            {
                // Low health + suppressed = find cover NOW
                Vector3 coverPos = FindCoverPosition(targetEnemy.position);
                if (coverPos != Vector3.zero)
                {
                    MoveToCover(coverPos, targetEnemy.position);
                    coverSearchTimer = 3f;
                    return;
                }
            }

            // No cover found, retreat while strafing
            if (healthPercent < 0.5f)
            {
                moveDirection = -transform.forward * 0.5f + transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
            else
            {
                moveDirection = transform.right * strafeDirection;
                isMoving = true;
            }
        }
        else if (enemyDist > attackRange * 0.8f)
        {
            // Too far - advance or hold
            if (personality == Personality.Aggressive || (healthPercent > 0.7f && bravery > 0.5f))
            {
                moveDirection = transform.forward * 0.7f + transform.right * strafeDirection * 0.3f;
                isMoving = true;
            }
            else
            {
                moveDirection = transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
        }
        else if (enemyDist < attackRange * 0.3f)
        {
            // Too close - back off unless aggressive
            if (personality != Personality.Aggressive)
            {
                moveDirection = -transform.forward * 0.5f + transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
        }
        else
        {
            // Good range - strafe and shoot
            moveDirection = transform.right * strafeDirection * 0.7f;
            isMoving = Random.value > 0.3f;
        }

        // Apply movement
        if (isMoving && moveDirection != Vector3.zero)
        {
            Vector3 newPos = transform.position + moveDirection.normalized * moveSpeed * 0.6f * Time.deltaTime;

            // Check for obstacles at multiple heights (catches poles)
            bool blocked = false;
            float[] heights = { 0.3f, 0.8f, 1.3f };
            foreach (float h in heights)
            {
                if (Physics.SphereCast(transform.position + Vector3.up * h, 0.25f, moveDirection.normalized, out _, 1f))
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                // Keep AI on the NavMesh/ground during combat strafing
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(newPos, out navHit, 5f, NavMesh.AllAreas))
                {
                    transform.position = navHit.position;
                }
                else
                {
                    // Fallback: raycast to find ground
                    RaycastHit groundHit;
                    if (Physics.Raycast(newPos + Vector3.up * 2f, Vector3.down, out groundHit, 10f))
                    {
                        transform.position = groundHit.point;
                    }
                    else
                    {
                        // Last fallback: just move directly (may cause floating but at least moves)
                        transform.position = newPos;
                    }
                }
            }
            else
            {
                strafeDirection *= -1f;
            }
            UpdateAnimation(true);
        }
        else
        {
            UpdateAnimation(false);
        }

        // === SHOOTING with PREDICTION ===
        if (burstCount >= 8)
        {
            burstCount = 0;
            reloadTimer = Random.Range(1.5f, 2.5f);
        }

        Vector3 toEnemy = (targetEnemy.position - transform.position).normalized;
        float facingAngle = Vector3.Angle(transform.forward, toEnemy);

        if (reloadTimer <= 0f && enemyDist <= attackRange && Time.time >= nextFireTime)
        {
            // More accurate when aiming longer and settled
            float effectiveAccuracy = accuracy * (0.7f + aimSettleTimer * 0.15f);
            float angleThreshold = Mathf.Lerp(50f, 25f, effectiveAccuracy);

            if (facingAngle < angleThreshold)
            {
                ShootAtPoint(aimPoint);
                nextFireTime = Time.time + fireRate * Random.Range(0.8f, 1.2f);

                // Reset aim settle on shot (recoil)
                aimSettleTimer *= 0.7f;
            }
        }
    }

    // Runs once per second - handles strategic decisions
    void UpdateCombat()
    {
        if (targetEnemy == null)
        {
            // Check last known position
            if (lastKnownEnemyLocation != Vector3.zero && Time.time - lastDamageTime < 5f)
            {
                SetDestination(lastKnownEnemyLocation);
                currentState = AIState.MovingToPoint;
                lastKnownEnemyLocation = Vector3.zero;
                return;
            }

            // Lost target, go back to capturing
            currentState = AIState.MovingToPoint;
            if (targetPoint != null)
                SetDestination(targetPoint.transform.position);
            return;
        }

        // Remember enemy position
        lastKnownEnemyLocation = targetEnemy.position;

        float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
        float healthPercent = currentHealth / maxHealth;

        // === INTELLIGENT THREAT ASSESSMENT ===
        ThreatAssessment threat = AssessThreat();

        // Intelligent retreat - don't fight losing battles
        if (!threat.shouldEngage)
        {
            targetEnemy = null;
            currentState = AIState.MovingToPoint;

            // Find nearest ally to retreat toward
            if (threat.outnumbered && cachedAIs != null)
            {
                AIController nearestAlly = null;
                float nearestDist = float.MaxValue;
                foreach (var ai in cachedAIs)
                {
                    if (ai == null || ai == this || ai.team != team) continue;
                    if (ai.currentState == AIState.Dead) continue;
                    float dist = Vector3.Distance(transform.position, ai.transform.position);
                    if (dist < nearestDist && dist > 5f)
                    {
                        nearestDist = dist;
                        nearestAlly = ai;
                    }
                }
                if (nearestAlly != null)
                {
                    SetDestination(nearestAlly.transform.position);
                    return;
                }
            }

            FindNextCapturePoint();
            return;
        }

        // Wait for squad before pushing
        if (ShouldWaitForSquad() && enemyDist > attackRange * 0.7f)
        {
            tacticalWaitTimer = 2f;
        }

        // === RETREAT LOGIC ===
        if (healthPercent < 0.25f && bravery < 0.6f && confidence < 0.5f)
        {
            if (Random.value > bravery * 0.5f)
            {
                targetEnemy = null;
                currentState = AIState.MovingToPoint;
                UpdateConfidence(-0.1f);
                FindNextCapturePoint();
                return;
            }
        }

        // === ADVANCED TACTICS DECISIONS ===

        // Check if we should seek cover (defensive/low health)
        if (healthPercent < 0.6f && !isInCover && coverSearchTimer <= 0f)
        {
            if (personality == Personality.Defensive || personality == Personality.Camper || healthPercent < 0.4f)
            {
                Vector3 coverPos = FindCoverPosition(targetEnemy.position);
                if (coverPos != Vector3.zero)
                {
                    MoveToCover(coverPos, targetEnemy.position);
                    coverSearchTimer = 5f;
                    return;
                }
            }
            coverSearchTimer = 2f;  // Don't search again immediately
        }

        // Check if we should attempt a flank
        if (!isFlankingTarget && flankTimer <= 0f && healthPercent > 0.5f)
        {
            if (ShouldAttemptFlank())
            {
                flankTarget = CalculateFlankPosition();
                if (flankTarget != Vector3.zero)
                {
                    isFlankingTarget = true;
                    flankTimer = 10f;  // Don't try another flank for a while
                    SetDestination(flankTarget);
                    return;
                }
            }
            flankTimer = 3f;  // Don't check again immediately
        }

        // Check if we should provide cover fire for an ally pushing
        if (!isProvidingCoverFire && healthPercent > 0.4f)
        {
            if (ShouldProvideCoverFire())
            {
                isProvidingCoverFire = true;
            }
        }
        else if (isProvidingCoverFire)
        {
            // Stop covering if ally is no longer pushing or dead
            if (coveringFor == null || coveringFor.currentState == AIState.Dead ||
                coveringFor.currentState == AIState.Idle)
            {
                isProvidingCoverFire = false;
                coveringFor = null;
            }
        }

        // Check for coordinated push opportunity
        if (squadRole == SquadRole.Leader && TryCoordinatedPush())
        {
            // Signal squad to push together
            foreach (var member in squadMembers)
            {
                if (member != null && member.currentState != AIState.Dead)
                {
                    member.confidence = Mathf.Min(member.confidence + 0.2f, 1f);
                }
            }
            confidence = Mathf.Min(confidence + 0.2f, 1f);
        }

        // Check for shared enemy intel if we have no target
        if (targetEnemy == null)
        {
            Vector3 sharedPos = GetSharedEnemyPosition();
            if (sharedPos != Vector3.zero && Vector3.Distance(transform.position, sharedPos) < detectionRange)
            {
                SetDestination(sharedPos);
                currentState = AIState.MovingToPoint;
                return;
            }
        }

        // If enemy too far, go back to objective
        float effectiveRange = detectionRange * (0.7f + patience * 0.3f);
        if (enemyDist > effectiveRange)
        {
            targetEnemy = null;
            currentState = AIState.MovingToPoint;
            isFlankingTarget = false;
            isProvidingCoverFire = false;
            if (targetPoint != null)
                SetDestination(targetPoint.transform.position);
            return;
        }

        // Reset flags if target changed
        enemyIsDistracted = false;
    }

    void FindNearestEnemy()
    {
        targetEnemy = null;
        float bestScore = float.MinValue;

        // Cache is updated globally once per second in Update()
        // Debug: count enemies in range
        int enemiesInRange = 0;

        // Squad targeting - prefer what squad is shooting at
        Transform squadTarget = null;
        if (squadLeader != null && squadLeader.targetEnemy != null)
        {
            squadTarget = squadLeader.targetEnemy;
        }
        // If I'm leader, check what my members are shooting
        if (squadRole == SquadRole.Leader)
        {
            foreach (var member in squadMembers)
            {
                if (member != null && member.targetEnemy != null)
                {
                    squadTarget = member.targetEnemy;
                    break;
                }
            }
        }

        // Count how many teammates are targeting each enemy (for focus fire / spread)
        Dictionary<Transform, int> targetCounts = new Dictionary<Transform, int>();
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.team != team) continue;
                if (ai.targetEnemy != null)
                {
                    if (!targetCounts.ContainsKey(ai.targetEnemy))
                        targetCounts[ai.targetEnemy] = 0;
                    targetCounts[ai.targetEnemy]++;
                }
            }
        }

        // Find player enemies
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null) continue;
                if (player.playerTeam != team && player.playerTeam != Team.None)
                {
                    float dist = Vector3.Distance(transform.position, player.transform.position);
                    if (dist > detectionRange) continue;

                    float score = 100f;

                    // Closer is better
                    score -= dist * 2f;

                    // Squad focus - prioritize what squad is shooting
                    if (squadTarget != null && player.transform == squadTarget)
                    {
                        score += 60f;
                    }

                    // Prioritize if they shot us recently
                    if (Time.time - lastDamageTime < 3f && lastDamageDirection != Vector3.zero)
                    {
                        Vector3 toPlayer = (player.transform.position - transform.position).normalized;
                        if (Vector3.Dot(toPlayer, lastDamageDirection) > 0.7f)
                        {
                            score += 50f; // Likely our attacker
                        }
                    }

                    // Spread targets (don't all shoot same enemy) - unless aggressive
                    if (personality != Personality.Aggressive)
                    {
                        int alreadyTargeting = 0;
                        targetCounts.TryGetValue(player.transform, out alreadyTargeting);
                        score -= alreadyTargeting * 15f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        targetEnemy = player.transform;
                    }
                }
            }
        }

        // Find AI enemies
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this) continue;
                if (!ai.isAIControlled) continue; // Skip player-controlled objects
                if (ai.team != team && ai.currentState != AIState.Dead)
                {
                    float dist = Vector3.Distance(transform.position, ai.transform.position);
                    if (dist > detectionRange) continue;

                    enemiesInRange++;
                    float score = 100f;

                    // Closer is better
                    score -= dist * 2f;

                    // Prioritize low health enemies (finish them off)
                    float healthPercent = ai.currentHealth / ai.maxHealth;
                    score += (1f - healthPercent) * 40f;

                    // Squad focus - prioritize what squad is shooting
                    if (squadTarget != null && ai.transform == squadTarget)
                    {
                        score += 60f; // Big bonus for squad coordination
                    }

                    // Prioritize if they shot us
                    if (Time.time - lastDamageTime < 3f && lastDamageDirection != Vector3.zero)
                    {
                        Vector3 toEnemy = (ai.transform.position - transform.position).normalized;
                        if (Vector3.Dot(toEnemy, lastDamageDirection) > 0.7f)
                        {
                            score += 50f;
                        }
                    }

                    // Spread targets (unless aggressive - they focus fire)
                    if (personality != Personality.Aggressive)
                    {
                        int alreadyTargeting = 0;
                        targetCounts.TryGetValue(ai.transform, out alreadyTargeting);
                        score -= alreadyTargeting * 15f;
                    }
                    else
                    {
                        // Aggressive bots prefer focus fire
                        int alreadyTargeting = 0;
                        targetCounts.TryGetValue(ai.transform, out alreadyTargeting);
                        score += alreadyTargeting * 10f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        targetEnemy = ai.transform;
                    }
                }
            }
        }

        // Debug output (only log occasionally to avoid spam)
        if (targetEnemy != null && Random.value < 0.1f)
        {
        }
    }

    void Shoot()
    {
        if (targetEnemy == null) return;
        ShootAtPoint(targetEnemy.position + Vector3.up * 1f);
    }

    // Shoot at a specific predicted/calculated aim point
    void ShootAtPoint(Vector3 aimPoint)
    {
        if (targetEnemy == null) return;

        // Muzzle position (approximate - in front of character)
        Vector3 muzzlePos = transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;

        // The aim point already has prediction and spread calculated by GetAimPoint()
        Vector3 targetPos = aimPoint;

        float dist = Vector3.Distance(transform.position, targetEnemy.position);

        // Play gunshot sound
        if (gunshotSound != null && audioSource != null)
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(gunshotSound, gunshotVolume);
        }

        // Spawn muzzle flash
        SpawnMuzzleFlash(muzzlePos);

        // Show tracer
        if (tracerLine != null)
        {
            StartCoroutine(ShowTracer(muzzlePos, targetPos));
        }

        // Calculate hit chance based on multiple factors
        float baseHitChance = accuracy;

        // Distance penalty
        baseHitChance *= (1f - (dist / attackRange) * 0.4f);

        // Bonus for longer aim time (settled aim)
        baseHitChance *= (0.8f + aimSettleTimer * 0.1f);

        // Penalty when suppressed
        if (suppressedTimer > 0f) baseHitChance *= 0.6f;

        // Penalty when target is moving fast
        if (lastEnemyVelocity.magnitude > 3f) baseHitChance *= 0.8f;

        // Bonus for flanking (enemy not facing us)
        if (enemyIsDistracted) baseHitChance *= 1.3f;

        // Headshot bonus damage calculation
        float damageMultiplier = 1f;
        if (isAimingForHead && Random.value < baseHitChance * 0.7f)
        {
            damageMultiplier = 2f;  // Headshot!
        }

        bool didHit = Random.value < baseHitChance;

        if (didHit)
        {
            float finalDamage = damage * damageMultiplier;

            // Check if target is player
            FPSControllerPhoton player = targetEnemy.GetComponent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != team)
            {
                // Apply damage to player
                player.TakeDamageFromAI(finalDamage, transform.position);
            }

            // Check if target is AI
            AIController ai = targetEnemy.GetComponent<AIController>();
            if (ai != null && ai.team != team)
            {
                ai.TakeDamage(finalDamage, transform.position, targetPos, gameObject);

                // Check if we killed them
                if (ai.currentHealth <= 0)
                {
                    killStreak++;
                    UpdateConfidence(0.15f);

                    // Share kill with team
                    CalloutEnemyPosition(ai.transform.position);
                }
            }
        }

        burstCount++;

        // Call out enemy position when shooting
        CalloutEnemyPosition(targetEnemy.position);
    }

    System.Collections.IEnumerator ShowTracer(Vector3 start, Vector3 end)
    {
        tracerLine.SetPosition(0, start);
        tracerLine.SetPosition(1, end);
        tracerLine.enabled = true;

        yield return new WaitForSeconds(tracerDuration);

        tracerLine.enabled = false;
    }

    void SpawnMuzzleFlash(Vector3 position)
    {
        if (muzzleFlashPrefab != null)
        {
            // Use assigned prefab (FX_Gunshot_01)
            GameObject flash = Instantiate(muzzleFlashPrefab, position, transform.rotation);
            Destroy(flash, 0.15f);
        }
        else
        {
            // Fallback: Create simple procedural muzzle flash
            GameObject flashObj = new GameObject("MuzzleFlash");
            flashObj.transform.position = position;
            flashObj.transform.rotation = transform.rotation;

            Light flashLight = flashObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.8f, 0.3f);
            flashLight.intensity = 3f;
            flashLight.range = 5f;

            Destroy(flashObj, 0.05f);
        }
    }

    public void TakeDamage(float amount, Vector3 damageSource = default, Vector3 hitPoint = default, GameObject attacker = null)
    {
        currentHealth -= amount;
        lastDamageTime = Time.time;
        suppressedTimer = 1f; // Suppressed for 1 second
        if (attacker != null) lastAttacker = attacker;

        // Spawn blood effect at hit point
        SpawnBloodHit(hitPoint != default ? hitPoint : transform.position + Vector3.up, damageSource);

        // Track where damage came from
        if (damageSource != default)
        {
            lastDamageDirection = (damageSource - transform.position).normalized;
            lastKnownEnemyLocation = damageSource;

            // If we don't have a target, react to damage
            if (targetEnemy == null && currentState != AIState.Dead)
            {
                // Turn toward attacker
                currentState = AIState.Combat;
                FindNearestEnemy();
            }
        }

        // Change strafe direction when hit
        if (Random.value > 0.5f)
        {
            strafeDirection *= -1f;
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void SpawnBloodHit(Vector3 position, Vector3 damageSource)
    {
        BloodEffectManager.SpawnBloodHit(position, damageSource);
    }

    void SpawnBloodDeath(Vector3 position)
    {
        BloodEffectManager.SpawnBloodDeath(position);
    }

    void Die()
    {
        RememberDeath();
        currentState = AIState.Dead;

        // Spawn death blood effect
        SpawnBloodDeath(transform.position + Vector3.up * 0.5f);

        // Report kill to kill feed
        ReportDeath();

        // Leave player squad if in one
        if (inPlayerSquad)
        {
            LeaveSquad();
        }

        // Enable ragdoll physics
        Vector3 forceDir = lastDamageDirection != Vector3.zero ? -lastDamageDirection : Vector3.back;
        RagdollHelper.EnableRagdoll(gameObject, forceDir, 8f);

        Destroy(gameObject, 8f);
    }

    void ReportDeath()
    {
        // Track death for scoreboard
        KillFeedManager.AddAIDeath(this);

        // Get victim name
        string victimName = identity != null ? identity.RankAndName : gameObject.name;
        Team victimTeam = team;

        // Determine killer
        string killerName = "Unknown";
        Team killerTeam = team == Team.Phantom ? Team.Havoc : Team.Phantom;

        if (lastAttacker != null)
        {
            // Check if killed by player
            FPSControllerPhoton player = lastAttacker.GetComponent<FPSControllerPhoton>();
            if (player != null)
            {
                killerName = player.photonView.Owner.NickName;
                killerTeam = player.playerTeam;

                // Add kill to player's stats
                if (player.photonView.IsMine)
                {
                    KillFeedManager.AddKill(Photon.Pun.PhotonNetwork.LocalPlayer);
                }
            }
            else
            {
                // Killed by another AI
                AIController killerAI = lastAttacker.GetComponent<AIController>();
                if (killerAI != null && killerAI.identity != null)
                {
                    killerName = killerAI.identity.RankAndName;
                    killerTeam = killerAI.team;

                    // Add kill to killer AI's stats
                    KillFeedManager.AddAIKill(killerAI);
                }
            }
        }

        // Report to kill feed
        KillFeedManager killFeed = FindObjectOfType<KillFeedManager>();
        if (killFeed != null)
        {
            killFeed.ReportKill(killerName, victimName, killerTeam, victimTeam);
        }
    }

    // Property to check if dead
    public bool isDead => currentState == AIState.Dead;

    // Property to check if stuck (hasn't moved in a while)
    public bool IsStuck => stuckTimer > 2f || stuckAttempts > 1;

    // Player-led squad methods
    public bool IsInSquad()
    {
        return inPlayerSquad;
    }

    public void JoinSquad(FPSControllerPhoton leader, int index = -1)
    {
        if (leader == null || leader.playerTeam != team) return;

        inPlayerSquad = true;
        playerSquadLeader = leader;
        squadIndex = index >= 0 ? index : leader.GetSquadSize();
        currentOrder = OrderType.FollowLeader;

        // Formation offset based on squad index
        squadFormationOffset = squadIndex * 45f;

    }

    public void SetOrder(OrderType order, CapturePoint point = null, Vector3? position = null, Vector3? facingDirection = null)
    {
        currentOrder = order;
        orderedPoint = point;
        if (position.HasValue)
        {
            holdPosition = position.Value;
        }
        if (facingDirection.HasValue)
        {
            holdFacingDirection = facingDirection.Value;
        }
        else
        {
            holdFacingDirection = Vector3.zero; // No specific facing direction
        }
    }

    public void SetOrder(OrderType order, HelicopterController helicopter)
    {
        currentOrder = order;
        orderedHelicopter = helicopter;
        orderedPoint = null;
    }

    public void LeaveSquad()
    {
        inPlayerSquad = false;
        playerSquadLeader = null;
        squadFormationOffset = 0f;

    }

    public FPSControllerPhoton GetPlayerSquadLeader()
    {
        return playerSquadLeader;
    }

    void UpdatePlayerSquadBehavior()
    {
        // If leader is dead or gone, leave squad
        if (playerSquadLeader == null || playerSquadLeader.isDead)
        {
            LeaveSquad();
            return;
        }

        // Handle different orders
        switch (currentOrder)
        {
            case OrderType.FollowLeader:
                ExecuteFollowLeaderOrder();
                break;
            case OrderType.DefendPoint:
                ExecuteDefendPointOrder();
                break;
            case OrderType.CapturePoint:
                ExecuteCapturePointOrder();
                break;
            case OrderType.HoldPosition:
                ExecuteHoldPositionOrder();
                break;
            case OrderType.BoardHelicopter:
                ExecuteBoardHelicopterOrder();
                break;
        }

        // Update animation based on movement
        bool isMoving = agent != null && agent.velocity.magnitude > 0.1f;
        bool isSprinting = agent != null && agent.velocity.magnitude > moveSpeed;
        UpdateAnimation(isMoving, isSprinting);
    }

    void ExecuteFollowLeaderOrder()
    {
        // Get leader's current target
        Transform leaderTarget = playerSquadLeader.GetCurrentTarget();

        // If leader has a target, prioritize attacking it
        if (leaderTarget != null)
        {
            float distToTarget = Vector3.Distance(transform.position, leaderTarget.position);

            // Move toward target if too far
            if (distToTarget > attackRange * 0.8f)
            {
                Vector3 dirToTarget = (leaderTarget.position - transform.position).normalized;
                Vector3 combatPos = leaderTarget.position - dirToTarget * (attackRange * 0.6f);

                if (agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(combatPos);
                }
            }

            // Face and shoot the target
            if (distToTarget <= attackRange)
            {
                targetEnemy = leaderTarget;
                currentState = AIState.Combat;

                // Look at target
                Vector3 lookDir = (leaderTarget.position - transform.position).normalized;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
                }

                // Shoot at target
                if (Time.time >= nextFireTime)
                {
                    ShootAtTarget(leaderTarget);
                    nextFireTime = Time.time + fireRate;
                }
            }
        }
        else
        {
            // No leader target - follow the leader
            currentState = AIState.MovingToPoint;
            FollowPlayerLeader();

            // But still react to close enemies
            CheckForNearbyEnemiesInSquad();
        }
    }

    void ExecuteDefendPointOrder()
    {
        if (orderedPoint == null)
        {
            currentOrder = OrderType.FollowLeader;
            return;
        }

        float distToPoint = Vector3.Distance(transform.position, orderedPoint.transform.position);

        // Move to point if too far
        if (distToPoint > 10f)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(orderedPoint.transform.position);
            }
            currentState = AIState.MovingToPoint;
        }
        else
        {
            // At point - patrol around it and engage enemies
            currentState = AIState.Capturing;
            CheckForNearbyEnemiesInSquad();
        }
    }

    void ExecuteCapturePointOrder()
    {
        if (orderedPoint == null)
        {
            currentOrder = OrderType.FollowLeader;
            return;
        }

        float distToPoint = Vector3.Distance(transform.position, orderedPoint.transform.position);

        // Move to capture zone
        if (distToPoint > orderedPoint.captureRadius * 0.5f)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(orderedPoint.transform.position);
            }
            currentState = AIState.MovingToPoint;
        }
        else
        {
            // Inside capture zone
            currentState = AIState.Capturing;

            // If point is captured, switch to defend
            if (orderedPoint.owningTeam == team)
            {
                currentOrder = OrderType.DefendPoint;
            }
        }

        CheckForNearbyEnemiesInSquad();
    }

    void ExecuteHoldPositionOrder()
    {
        float distToPos = Vector3.Distance(transform.position, holdPosition);

        if (distToPos > 2f)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(holdPosition);
            }
            currentState = AIState.MovingToPoint;
        }
        else
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            currentState = AIState.Idle;

            // Face the specified direction if one was given
            if (holdFacingDirection != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(holdFacingDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }
        }

        CheckForNearbyEnemiesInSquad();
    }

    void ExecuteBoardHelicopterOrder()
    {
        // Check if ordered helicopter is still valid
        if (orderedHelicopter == null)
        {
            // Helicopter gone - fall back to follow
            currentOrder = OrderType.FollowLeader;
            return;
        }

        // Check if already in the helicopter
        if (currentState == AIState.HeliGunner || currentState == AIState.HeliPilot)
        {
            // Already in helicopter - done
            return;
        }

        // Find available seat in the helicopter
        // Priority: Pilot (if no pilot) > Gunner > Passenger
        HelicopterSeat[] seats = orderedHelicopter.GetComponentsInChildren<HelicopterSeat>();
        HelicopterSeat bestSeat = null;
        HelicopterSeat pilotSeat = null;
        HelicopterSeat gunnerSeat = null;
        float closestGunnerDist = float.MaxValue;
        float closestPassengerDist = float.MaxValue;
        HelicopterSeat closestPassenger = null;

        // Check if helicopter already has a pilot
        bool hasPilot = orderedHelicopter.HasPilot();

        Debug.Log($"[AI BOARDING] {gameObject.name}: Found {seats.Length} seats, hasPilot={hasPilot}");

        foreach (var seat in seats)
        {
            Debug.Log($"[AI BOARDING] Checking seat {seat.seatType}: IsOccupied={seat.IsOccupied}");

            if (!seat.IsOccupied)
            {
                float dist = Vector3.Distance(transform.position, seat.transform.position);

                if (seat.seatType == SeatType.Pilot && !hasPilot)
                {
                    // Prioritize empty pilot seat
                    pilotSeat = seat;
                }
                else if (seat.GetComponent<HelicopterWeapon>() != null)
                {
                    // Gunner seat
                    if (dist < closestGunnerDist)
                    {
                        closestGunnerDist = dist;
                        gunnerSeat = seat;
                    }
                }
                else if (seat.seatType != SeatType.Pilot)
                {
                    // Passenger seat (any non-pilot seat without weapon)
                    if (dist < closestPassengerDist)
                    {
                        closestPassengerDist = dist;
                        closestPassenger = seat;
                    }
                }
            }
        }

        // Pick best seat: pilot > gunner > passenger
        if (pilotSeat != null)
            bestSeat = pilotSeat;
        else if (gunnerSeat != null)
            bestSeat = gunnerSeat;
        else
            bestSeat = closestPassenger;

        Debug.Log($"[AI BOARDING] {gameObject.name}: bestSeat={bestSeat?.seatType}, pilotSeat={pilotSeat != null}, gunnerSeat={gunnerSeat != null}, passengerSeat={closestPassenger != null}");

        if (bestSeat == null)
        {
            // No available seats - helicopter is full, revert to follow
            Debug.Log($"[AI BOARDING] {gameObject.name}: No seats available!");
            currentOrder = OrderType.FollowLeader;
            orderedHelicopter = null;
            return;
        }

        // Move toward the helicopter
        float distToHeli = Vector3.Distance(transform.position, orderedHelicopter.transform.position);

        if (distToHeli > 5f)
        {
            // Move toward helicopter
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(orderedHelicopter.transform.position);
            }
            currentState = AIState.MovingToPoint;
        }
        else
        {
            // Close enough - try to enter the seat
            if (bestSeat.TryEnterAI(this))
            {
                Debug.Log($"[AI BOARDING] {gameObject.name}: Successfully entered {bestSeat.seatType}");

                // Successfully entered
                currentHeliSeat = bestSeat;
                currentHelicopter = orderedHelicopter;

                // Determine if this is a gunner seat or passenger seat
                HelicopterWeapon weapon = bestSeat.GetComponent<HelicopterWeapon>();
                if (weapon != null)
                {
                    // This is a gunner seat
                    weapon.SetAIGunner(team);
                    currentState = AIState.HeliGunner;
                    agentEnabled = false;
                    SetModelVisible(false);  // Hide AI model when in seat
                }
                else if (bestSeat.seatType == SeatType.Pilot)
                {
                    // This is pilot seat
                    EnterHelicopterAsPilot(orderedHelicopter);
                }
                else
                {
                    // Passenger seat - just sit there
                    currentState = AIState.HeliGunner; // Use same state for passengers
                    agentEnabled = false;
                    SetModelVisible(false);  // Hide AI model when in seat
                }

                // Clear order, we're in
                orderedHelicopter = null;
            }
            else
            {
                // Failed to enter this seat (probably taken) - clear bestSeat so we re-evaluate next frame
                Debug.Log($"[AI BOARDING] {gameObject.name}: Failed to enter {bestSeat.seatType} (occupied), will retry");
                // Don't clear order - keep trying to find another seat
            }
        }
    }

    void FollowPlayerLeader()
    {
        if (playerSquadLeader == null) return;

        Vector3 leaderPos = playerSquadLeader.transform.position;
        float distToLeader = Vector3.Distance(transform.position, leaderPos);

        // Calculate formation position (spread out behind leader)
        float angle = squadFormationOffset;
        Vector3 offset = new Vector3(
            Mathf.Sin(angle * Mathf.Deg2Rad) * followDistance,
            0,
            -Mathf.Cos(angle * Mathf.Deg2Rad) * followDistance * 0.5f - 2f // Behind leader
        );

        // Transform offset to be relative to leader's facing direction
        Vector3 formationPos = leaderPos + playerSquadLeader.transform.TransformDirection(offset);

        // Only move if too far from formation position
        if (distToLeader > followDistance * 2f)
        {
            // Too far - run to catch up
            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = moveSpeed * 1.5f;
                agent.SetDestination(formationPos);
            }
        }
        else if (Vector3.Distance(transform.position, formationPos) > 2f)
        {
            // Move to formation position
            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = moveSpeed;
                agent.SetDestination(formationPos);
            }
        }
        else
        {
            // In position - stop
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }

            // Face same direction as leader
            Quaternion targetRot = playerSquadLeader.transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
        }
    }

    void CheckForNearbyEnemiesInSquad()
    {
        // Check for enemies within detection range while following
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null || player.isDead) continue;
                if (player.playerTeam == team) continue;

                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist <= detectionRange * 0.5f) // React at half range when in squad
                {
                    targetEnemy = player.transform;
                    // Shoot at them but don't leave formation
                    if (Time.time >= nextFireTime && dist <= attackRange)
                    {
                        ShootAtTarget(player.transform);
                        nextFireTime = Time.time + fireRate;
                    }
                }
            }
        }

        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.team == team) continue;

                float dist = Vector3.Distance(transform.position, ai.transform.position);
                if (dist <= detectionRange * 0.5f)
                {
                    targetEnemy = ai.transform;
                    if (Time.time >= nextFireTime && dist <= attackRange)
                    {
                        ShootAtTarget(ai.transform);
                        nextFireTime = Time.time + fireRate;
                    }
                }
            }
        }
    }

    void ShootAtTarget(Transform target)
    {
        if (target == null) return;

        // Face the target
        Vector3 dirToTarget = (target.position - transform.position).normalized;
        dirToTarget.y = 0;
        if (dirToTarget != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(dirToTarget);
        }

        // Fire at target
        Vector3 aimPoint = target.position + Vector3.up * 1.2f; // Aim at chest height

        // Apply accuracy spread
        float spread = (1f - accuracy) * 2f;

        // Distance-based accuracy falloff
        float distToTarget = Vector3.Distance(transform.position, target.position);
        if (distToTarget > 20f)
        {
            float distanceFactor = 1f + (distToTarget - 20f) / 20f;
            spread *= distanceFactor;
        }

        // Extra penalty for shooting at targets in vehicles
        FPSControllerPhoton targetPlayer = target.GetComponent<FPSControllerPhoton>();
        if (targetPlayer != null && targetPlayer.IsInVehicle)
        {
            spread *= 2f;
        }

        aimPoint += new Vector3(
            Random.Range(-spread, spread),
            Random.Range(-spread * 0.5f, spread * 0.5f),
            Random.Range(-spread, spread)
        );

        Vector3 shootOrigin = transform.position + Vector3.up * 1.5f;
        Vector3 shootDir = (aimPoint - shootOrigin).normalized;

        // Play sound
        if (gunshotSound != null && audioSource != null)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(gunshotSound, gunshotVolume);
        }

        // Spawn muzzle flash
        SpawnMuzzleFlash(shootOrigin);

        // Raycast
        RaycastHit hit;
        if (Physics.Raycast(shootOrigin, shootDir, out hit, attackRange * 1.5f))
        {
            // Show tracer
            if (tracerLine != null)
            {
                StartCoroutine(ShowTracer(shootOrigin, hit.point));
            }

            // Check if we hit enemy
            FPSControllerPhoton hitPlayer = hit.collider.GetComponentInParent<FPSControllerPhoton>();
            if (hitPlayer != null && hitPlayer.playerTeam != team)
            {
                hitPlayer.photonView.RPC("RPC_TakeDamage", Photon.Pun.RpcTarget.All, damage, -1);
            }

            AIController hitAI = hit.collider.GetComponentInParent<AIController>();
            if (hitAI != null && hitAI.team != team)
            {
                hitAI.TakeDamage(damage, transform.position, hit.point, gameObject);
            }
        }
        else
        {
            // Show tracer to max range
            if (tracerLine != null)
            {
                StartCoroutine(ShowTracer(shootOrigin, shootOrigin + shootDir * attackRange));
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }

    // === HELICOPTER GUNNER SYSTEM ===

    public void EnterHelicopterGunnerSeat(HelicopterSeat seat)
    {
        if (seat == null || seat.IsOccupied) return;

        // Mark seat as occupied by AI
        if (!seat.TryEnterAI(this)) return;

        currentHeliSeat = seat;
        currentHelicopter = seat.GetComponentInParent<HelicopterController>();

        // Disable collider BEFORE parenting to prevent physics collision
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Disable NavMeshAgent BEFORE parenting
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            agent.enabled = false;
            agentEnabled = false;
        }

        // Parent AI to seat (after disabling physics)
        transform.SetParent(seat.seatPosition != null ? seat.seatPosition : seat.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Set state to helicopter gunner
        currentState = AIState.HeliGunner;

        // Setup weapon for AI control
        if (seat.mountedWeapon != null)
        {
            seat.mountedWeapon.SetAIGunner(team);
        }
    }

    // Wrapper for entering gunner seat with seat parameter
    void EnterHelicopterAsGunner(HelicopterController helicopter, HelicopterSeat seat)
    {
        if (helicopter == null || seat == null) return;
        EnterHelicopterGunnerSeat(seat);
    }

    // Enter helicopter as passenger (no weapon, just riding)
    void EnterHelicopterAsPassenger(HelicopterController helicopter, HelicopterSeat seat)
    {
        if (helicopter == null || seat == null || seat.IsOccupied) return;

        // Mark seat as occupied by AI
        if (!seat.TryEnterAI(this)) return;

        currentHeliSeat = seat;
        currentHelicopter = helicopter;

        // Disable collider BEFORE parenting to prevent physics collision
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Disable NavMeshAgent BEFORE parenting
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            agent.enabled = false;
            agentEnabled = false;
        }

        // Parent AI to seat (after disabling physics)
        transform.SetParent(seat.seatPosition != null ? seat.seatPosition : seat.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Set state to helicopter passenger
        currentState = AIState.HeliPassenger;

        // Hide model
        SetModelVisible(false);

        Debug.Log($"[AI PASSENGER] {gameObject.name} entered {helicopter.name} as passenger");
    }

    // Enter helicopter as virtual passenger (no physical seat)
    void EnterHelicopterAsVirtualPassenger(HelicopterController helicopter)
    {
        if (helicopter == null) return;
        if (!helicopter.AddVirtualPassenger(this)) return;

        currentHeliSeat = null;  // No physical seat
        currentHelicopter = helicopter;

        // Disable collider BEFORE parenting to prevent physics collision
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Disable NavMeshAgent BEFORE parenting
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            agent.enabled = false;
            agentEnabled = false;
        }

        // Parent AI to helicopter (after disabling physics)
        transform.SetParent(helicopter.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Set state to helicopter passenger
        currentState = AIState.HeliPassenger;

        // Hide model
        SetModelVisible(false);

        Debug.Log($"[AI PASSENGER] {gameObject.name} entered {helicopter.name} as virtual passenger");
    }

    public void ExitHelicopterSeat()
    {
        // SAVE helicopter position BEFORE clearing anything
        Vector3 heliPosition = Vector3.zero;
        Quaternion heliRotation = Quaternion.identity;
        if (currentHelicopter != null)
        {
            heliPosition = currentHelicopter.transform.position;
            heliRotation = currentHelicopter.transform.rotation;
        }

        // Handle virtual passenger (no seat)
        if (currentHeliSeat == null && currentHelicopter != null)
        {
            currentHelicopter.RemoveVirtualPassenger(this);
        }

        // Clear seat's AI occupant (also clears weapon)
        if (currentHeliSeat != null)
        {
            currentHeliSeat.ExitAI();
        }

        // Unparent
        transform.SetParent(null);

        // Find valid ground position near helicopter using SAVED position
        Vector3 exitPos = FindValidExitNearPosition(heliPosition, heliRotation);

        // NUCLEAR OPTION: Spawn a fresh AI instead of trying to fix this one
        // This completely bypasses all NavMesh/Rigidbody state corruption issues
        if (aiPrefabReference != null)
        {
            Debug.Log($"[AI] {gameObject.name} exiting helicopter at {heliPosition} - spawning fresh AI at {exitPos}");
            SpawnFreshAIAndDestroySelf(exitPos, heliPosition);
            return;  // This object will be destroyed, don't continue
        }

        // Fallback if no prefab reference (shouldn't happen)
        Debug.LogWarning($"[AI] No aiPrefabReference set! Falling back to old exit method.");
        FallbackExitHelicopter(exitPos);
    }

    // Find exit position using saved helicopter position (not currentHelicopter which may be cleared)
    Vector3 FindValidExitNearPosition(Vector3 heliPos, Quaternion heliRot)
    {
        if (heliPos == Vector3.zero)
        {
            heliPos = transform.position;
        }

        // Try multiple exit directions around the helicopter
        Vector3 heliRight = heliRot * Vector3.right;
        Vector3 heliForward = heliRot * Vector3.forward;

        Vector3[] exitOffsets = {
            heliRight * 6f,
            -heliRight * 6f,
            heliForward * 6f,
            -heliForward * 6f,
            (heliRight + heliForward).normalized * 6f,
            (-heliRight + heliForward).normalized * 6f,
            (heliRight - heliForward).normalized * 6f,
            (-heliRight - heliForward).normalized * 6f
        };

        foreach (var offset in exitOffsets)
        {
            Vector3 testPos = heliPos + offset;

            // Raycast down to find ground
            RaycastHit hit;
            if (Physics.Raycast(testPos + Vector3.up * 20f, Vector3.down, out hit, 60f))
            {
                Vector3 groundPos = hit.point + Vector3.up * 0.5f;
                return groundPos;  // Return ground position even without NavMesh check
            }
        }

        // Fallback: raycast straight down from helicopter
        RaycastHit groundHit;
        if (Physics.Raycast(heliPos + Vector3.up * 20f, Vector3.down, out groundHit, 60f))
        {
            return groundHit.point + Vector3.up * 0.5f;
        }

        return heliPos;
    }

    void SpawnFreshAIAndDestroySelf(Vector3 spawnPos, Vector3 heliPos)
    {
        // Find NavMesh position for spawn - but ONLY within reasonable distance of helicopter
        NavMeshHit navHit;
        Vector3 finalSpawnPos = spawnPos;
        bool foundNavMesh = false;

        // Try small radii first to stay near helicopter
        float[] searchRadii = { 3f, 8f, 15f, 25f };

        foreach (float radius in searchRadii)
        {
            if (NavMesh.SamplePosition(spawnPos, out navHit, radius, NavMesh.AllAreas))
            {
                // CRITICAL: Make sure the NavMesh position isn't too far from helicopter
                float distFromHeli = Vector3.Distance(navHit.position, heliPos);
                if (distFromHeli < 50f)  // Must be within 50m of helicopter
                {
                    finalSpawnPos = navHit.position;
                    foundNavMesh = true;
                    Debug.Log($"[AI] Found NavMesh at radius {radius}m, dist from heli: {distFromHeli}m");
                    break;
                }
                else
                {
                    Debug.Log($"[AI] NavMesh at radius {radius}m is too far ({distFromHeli}m from heli), trying larger radius...");
                }
            }
        }

        // If no nearby NavMesh, just spawn at the exit position anyway
        // The AI's Start() will handle getting on NavMesh
        if (!foundNavMesh)
        {
            Debug.LogWarning($"[AI] No NavMesh within 50m of helicopter! Spawning at exit pos {spawnPos} anyway.");
            finalSpawnPos = spawnPos;
        }

        // Spawn the new AI
        Quaternion spawnRot = transform.rotation;
        GameObject newAI = Instantiate(aiPrefabReference, finalSpawnPos, spawnRot);
        AIController newController = newAI.GetComponent<AIController>();

        if (newController != null)
        {
            // Copy team
            newController.team = this.team;
            newController.InitializeTeam();

            // Copy squad membership if in player squad
            if (this.inPlayerSquad && this.playerSquadLeader != null)
            {
                newController.JoinSquad(this.playerSquadLeader, this.squadIndex);
            }

            // CRITICAL: Reset state to Idle - prevents Update() from calling UpdateHeliPassengerBehavior()
            // which would immediately destroy the new AI when it sees currentHelicopter == null
            newController.currentState = AIState.Idle;
            newController.currentHelicopter = null;
            newController.currentHeliSeat = null;

            // CRITICAL: Ensure model is visible and colliders are enabled
            newController.SetModelVisible(true);

            // Re-enable collider
            Collider col = newController.GetComponent<Collider>();
            if (col != null) col.enabled = true;

            // Enable NavMeshAgent and try to get on NavMesh immediately
            if (newController.agent != null && foundNavMesh)
            {
                newController.agent.enabled = true;
                newController.agentEnabled = true;
                newController.agent.Warp(finalSpawnPos);
            }

            // Name it
            newAI.name = $"AI_{team}_HeliDrop_{Time.frameCount}";

            Debug.Log($"[AI] Fresh AI spawned at {finalSpawnPos}, team={team}, state reset to Idle");
        }

        // Clear our references
        currentHeliSeat = null;
        currentHelicopter = null;

        // Destroy this corrupted AI
        Destroy(gameObject);
    }

    void FallbackExitHelicopter(Vector3 exitPos)
    {
        // Old exit code as fallback
        transform.position = exitPos;

        // Re-enable collider first
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Show model again
        SetModelVisible(true);

        // Re-enable NavMeshAgent and find valid NavMesh position
        if (agent != null)
        {
            agent.enabled = true;
            agentEnabled = true;

            NavMeshHit navHit;
            if (NavMesh.SamplePosition(transform.position, out navHit, 50f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
                transform.position = navHit.position;
            }
        }

        currentHeliSeat = null;
        currentHelicopter = null;
        heliGunnerTarget = null;
        targetPoint = null;
        targetEnemy = null;
        targetPosition = Vector3.zero;

        if (agent != null && agent.enabled)
        {
            agent.speed = moveSpeed;
            agent.isStopped = false;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
        }

        currentState = AIState.Idle;
        heliExitCooldown = HELI_EXIT_COOLDOWN_TIME;
        heliExitDebugTimer = 5f;
        justExitedHelicopter = true;
        heliExitRecoveryTimer = 10f;

        StartCoroutine(ForceMovementAfterHeliExit());
        Debug.Log($"[AI] {gameObject.name} exited helicopter (fallback method)");
    }

    // Flags for helicopter exit recovery
    private bool justExitedHelicopter = false;
    private float heliExitRecoveryTimer = 0f;
    private Vector3 lastHeliExitPosition;
    private int heliExitStuckFrames = 0;

    // BULLETPROOF coroutine - will NOT fail to get the AI moving
    System.Collections.IEnumerator ForceMovementAfterHeliExit()
    {
        lastHeliExitPosition = transform.position;

        // PHASE 1: Get on NavMesh no matter what (try for 5 seconds)
        float phase1Timeout = 5f;
        float elapsed = 0f;
        bool onNavMesh = false;

        Debug.Log($"[AI FORCE] {gameObject.name} Phase 1: Getting on NavMesh...");

        while (elapsed < phase1Timeout && !onNavMesh)
        {
            elapsed += Time.deltaTime;

            // Make absolutely sure agent is enabled
            if (agent != null)
            {
                if (!agent.enabled)
                {
                    agent.enabled = true;
                    agentEnabled = true;
                }

                // Try increasingly large search radii
                float[] radii = { 5f, 10f, 25f, 50f, 100f, 200f, 500f };
                foreach (float radius in radii)
                {
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(transform.position, out hit, radius, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                        transform.position = hit.position;

                        // Wait a frame and check if it worked
                        yield return null;

                        if (agent.isOnNavMesh)
                        {
                            onNavMesh = true;
                            Debug.Log($"[AI FORCE] {gameObject.name} ON NAVMESH at radius {radius}m after {elapsed:F2}s");
                            break;
                        }
                    }
                }

                if (!onNavMesh)
                {
                    yield return null;
                }
            }
            else
            {
                yield return null;
            }
        }

        // PHASE 2: If still not on NavMesh, teleport to a known good location
        if (!onNavMesh)
        {
            Debug.LogWarning($"[AI FORCE] {gameObject.name} Phase 2: Emergency teleport to capture point...");

            // Try every capture point
            foreach (var point in allCapturePoints)
            {
                if (point == null) continue;

                NavMeshHit hit;
                Vector3 testPos = point.transform.position;

                if (NavMesh.SamplePosition(testPos, out hit, 20f, NavMesh.AllAreas))
                {
                    transform.position = hit.position;

                    if (agent != null)
                    {
                        agent.enabled = true;
                        agentEnabled = true;
                        agent.Warp(hit.position);

                        yield return null;
                        yield return null;  // Extra frame

                        if (agent.isOnNavMesh)
                        {
                            onNavMesh = true;
                            Debug.Log($"[AI FORCE] {gameObject.name} TELEPORTED to {point.name}");
                            break;
                        }
                    }
                }
            }
        }

        // PHASE 3: Force all movement properties
        Debug.Log($"[AI FORCE] {gameObject.name} Phase 3: Forcing movement properties...");

        if (agent != null)
        {
            agent.enabled = true;
            agentEnabled = true;
            agent.isStopped = false;
            agent.speed = moveSpeed;
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            agent.stoppingDistance = 0.5f;
            agent.autoBraking = true;
            agent.updatePosition = true;
            agent.updateRotation = true;
        }

        // PHASE 4: Set destination and change state
        Debug.Log($"[AI FORCE] {gameObject.name} Phase 4: Setting destination...");

        // Find the nearest capture point
        CapturePoint nearestPoint = null;
        float nearestDist = float.MaxValue;

        foreach (var point in allCapturePoints)
        {
            if (point == null) continue;
            float dist = Vector3.Distance(transform.position, point.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestPoint = point;
            }
        }

        if (nearestPoint != null && agent != null && agent.isOnNavMesh)
        {
            targetPoint = nearestPoint;

            NavMeshHit destHit;
            Vector3 destination = nearestPoint.transform.position;
            if (NavMesh.SamplePosition(destination, out destHit, 20f, NavMesh.AllAreas))
            {
                destination = destHit.position;
            }

            agent.SetDestination(destination);
            targetPosition = destination;
            currentState = AIState.MovingToPoint;

            Debug.Log($"[AI FORCE] {gameObject.name} DESTINATION SET to {nearestPoint.name}, state={currentState}");
        }
        else
        {
            // Even if NavMesh failed, try to set state
            currentState = AIState.Idle;
            Debug.LogWarning($"[AI FORCE] {gameObject.name} Could not set destination, state=Idle");
        }

        // PHASE 5: Monitor for 5 seconds and force recovery if stuck
        Debug.Log($"[AI FORCE] {gameObject.name} Phase 5: Monitoring movement...");

        float monitorTime = 5f;
        float monitorElapsed = 0f;
        Vector3 lastPos = transform.position;
        int stuckCount = 0;

        while (monitorElapsed < monitorTime)
        {
            monitorElapsed += Time.deltaTime;

            // Check if we're actually moving
            float moved = Vector3.Distance(transform.position, lastPos);

            if (moved < 0.1f && currentState == AIState.MovingToPoint)
            {
                stuckCount++;

                if (stuckCount > 30)  // Stuck for 30 frames (~0.5 seconds)
                {
                    Debug.LogWarning($"[AI FORCE] {gameObject.name} STUCK! Forcing movement...");

                    // Force a new destination
                    if (agent != null && agent.isOnNavMesh)
                    {
                        agent.isStopped = false;
                        agent.speed = moveSpeed;

                        // Pick a random nearby point
                        Vector3 randomDir = Random.insideUnitSphere * 20f;
                        randomDir.y = 0;
                        Vector3 newDest = transform.position + randomDir;

                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(newDest, out hit, 25f, NavMesh.AllAreas))
                        {
                            agent.SetDestination(hit.position);
                            Debug.Log($"[AI FORCE] {gameObject.name} Forced new destination");
                        }
                    }

                    stuckCount = 0;
                }
            }
            else
            {
                stuckCount = 0;
            }

            lastPos = transform.position;
            yield return null;
        }

        justExitedHelicopter = false;
        Debug.Log($"[AI FORCE] {gameObject.name} Movement recovery complete. Final state={currentState}, isOnNavMesh={agent?.isOnNavMesh}");
    }

    // Coroutine to find objective after exiting helicopter (gives NavMesh time to settle)
    System.Collections.IEnumerator FindObjectiveAfterExit()
    {
        // Wait a couple frames for NavMesh warp to fully complete
        yield return null;
        yield return null;

        // Make sure we're still in Idle state and not dead
        if (currentState != AIState.Idle || isDead) yield break;

        // Ensure agent is properly on NavMesh
        if (agent != null && agent.enabled)
        {
            if (!agent.isOnNavMesh)
            {
                Debug.LogWarning($"[AI] {gameObject.name} not on NavMesh after exit, attempting recovery...");

                // Try increasingly larger search radii
                NavMeshHit hit;
                float[] searchRadii = { 10f, 25f, 50f, 100f, 200f };
                bool foundNavMesh = false;

                foreach (float radius in searchRadii)
                {
                    if (NavMesh.SamplePosition(transform.position, out hit, radius, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                        transform.position = hit.position;
                        foundNavMesh = true;
                        Debug.Log($"[AI] {gameObject.name} recovery: found NavMesh at radius {radius}m");
                        break;
                    }
                }

                if (!foundNavMesh)
                {
                    // Emergency fallback: warp to a capture point
                    foreach (var point in allCapturePoints)
                    {
                        if (point != null && NavMesh.SamplePosition(point.transform.position, out hit, 10f, NavMesh.AllAreas))
                        {
                            agent.Warp(hit.position);
                            transform.position = hit.position;
                            foundNavMesh = true;
                            Debug.Log($"[AI] {gameObject.name} recovery: warped to {point.name}");
                            break;
                        }
                    }
                }

                yield return null;  // Wait another frame after warp
            }

            // Now find objective
            if (agent.isOnNavMesh)
            {
                // Extra safety: ensure agent is fully ready
                agent.isStopped = false;
                agent.speed = moveSpeed;

                FindNextCapturePoint();
                Debug.Log($"[AI] {gameObject.name} found objective after helicopter exit, state={currentState}, agentEnabled={agentEnabled}, isOnNavMesh={agent.isOnNavMesh}");
            }
            else
            {
                Debug.LogError($"[AI] {gameObject.name} CRITICAL: Still not on NavMesh after all recovery attempts!");
            }
        }
    }

    // Find a valid exit position when leaving helicopter
    Vector3 FindValidExitFromHelicopter()
    {
        Vector3 heliPos = currentHelicopter != null ? currentHelicopter.transform.position : transform.position;

        // Try multiple exit directions
        Vector3[] exitOffsets = {
            currentHelicopter != null ? currentHelicopter.transform.right * 6f : Vector3.right * 6f,
            currentHelicopter != null ? -currentHelicopter.transform.right * 6f : Vector3.left * 6f,
            currentHelicopter != null ? currentHelicopter.transform.forward * 6f : Vector3.forward * 6f,
            currentHelicopter != null ? -currentHelicopter.transform.forward * 6f : Vector3.back * 6f
        };

        foreach (var offset in exitOffsets)
        {
            Vector3 testPos = heliPos + offset;

            // Raycast down to find ground
            RaycastHit hit;
            if (Physics.Raycast(testPos + Vector3.up * 15f, Vector3.down, out hit, 50f))
            {
                Vector3 groundPos = hit.point + Vector3.up * 0.5f;

                // Check if this position has NavMesh
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(groundPos, out navHit, 5f, NavMesh.AllAreas))
                {
                    return navHit.position;
                }
            }
        }

        // Fallback: raycast straight down from helicopter
        RaycastHit groundHit;
        if (Physics.Raycast(heliPos + Vector3.up * 15f, Vector3.down, out groundHit, 50f))
        {
            return groundHit.point + Vector3.up * 0.5f;
        }

        return heliPos + Vector3.up * 2f;
    }

    // Update function for when AI is actively trying to board a helicopter
    void UpdateBoardingHelicopter()
    {
        // Check if target helicopter is still valid
        if (targetBoardingHelicopter == null || targetBoardingHelicopter.isDestroyed)
        {
            Debug.Log($"[AI BOARDING] {gameObject.name} - target helicopter gone, canceling boarding");
            CancelBoarding();
            return;
        }

        // Check if helicopter is still waiting for passengers
        if (!targetBoardingHelicopter.IsWaitingForPassengers)
        {
            Debug.Log($"[AI BOARDING] {gameObject.name} - helicopter no longer waiting, canceling boarding");
            CancelBoarding();
            return;
        }

        // Check boarding timeout
        boardingTimeout -= Time.deltaTime;
        if (boardingTimeout <= 0f)
        {
            Debug.Log($"[AI BOARDING] {gameObject.name} - boarding timeout, canceling");
            CancelBoarding();
            return;
        }

        // Update destination to helicopter's current position (in case it moved slightly)
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.SetDestination(targetBoardingHelicopter.transform.position);
        }

        // Check distance
        float dist = Vector3.Distance(transform.position, targetBoardingHelicopter.transform.position);

        // Close enough to board?
        if (dist <= 10f)
        {
            // Try to board
            HelicopterSeat availableSeat = null;
            foreach (var seat in targetBoardingHelicopter.seats)
            {
                if (seat == null || seat.IsOccupied) continue;
                if (seat.seatType == SeatType.Pilot) continue;

                bool isGunnerSeat = seat.seatType == SeatType.DoorGunnerLeft || seat.seatType == SeatType.DoorGunnerRight;
                if (availableSeat == null || isGunnerSeat)
                {
                    availableSeat = seat;
                }
            }

            if (availableSeat != null)
            {
                // Board physical seat
                bool isGunnerSeat = availableSeat.seatType == SeatType.DoorGunnerLeft || availableSeat.seatType == SeatType.DoorGunnerRight;
                Debug.Log($"[AI BOARDING] {gameObject.name} boarding {targetBoardingHelicopter.name} physical seat {availableSeat.seatType}");
                HelicopterController heliToBoard = targetBoardingHelicopter;
                targetBoardingHelicopter = null;
                if (isGunnerSeat)
                {
                    EnterHelicopterAsGunner(heliToBoard, availableSeat);
                }
                else
                {
                    EnterHelicopterAsPassenger(heliToBoard, availableSeat);
                }
            }
            else if (targetBoardingHelicopter.HasVirtualPassengerSpace())
            {
                // Board as virtual passenger
                Debug.Log($"[AI BOARDING] {gameObject.name} boarding {targetBoardingHelicopter.name} as virtual passenger");
                HelicopterController heliToBoard = targetBoardingHelicopter;
                targetBoardingHelicopter = null;
                EnterHelicopterAsVirtualPassenger(heliToBoard);
            }
            else
            {
                // No space - cancel
                Debug.Log($"[AI BOARDING] {gameObject.name} - no space on helicopter, canceling boarding");
                CancelBoarding();
            }
        }

        // Keep sprinting
        if (agent != null && agent.enabled)
        {
            agent.speed = moveSpeed * 1.5f;
        }
    }

    void CancelBoarding()
    {
        targetBoardingHelicopter = null;
        boardingTimeout = 0f;
        currentState = AIState.Idle;

        // Reset speed
        if (agent != null && agent.enabled)
        {
            agent.speed = moveSpeed;
        }
    }

    void UpdateHeliPassengerBehavior()
    {
        // Exit if helicopter destroyed (seat may be null for virtual passengers)
        if (currentHelicopter == null || currentHelicopter.isDestroyed)
        {
            ExitHelicopterSeat();
            return;
        }

        // Passengers just ride along - they'll be ejected by the pilot when landing
        // No active behavior needed
    }

    private float heliGunnerDebugTimer = 0f;

    void UpdateHeliGunnerBehavior()
    {
        // Exit if helicopter destroyed
        if (currentHelicopter == null || currentHelicopter.isDestroyed || currentHeliSeat == null)
        {
            ExitHelicopterSeat();
            return;
        }

        // Debug logging every 2 seconds
        heliGunnerDebugTimer -= Time.deltaTime;
        if (heliGunnerDebugTimer <= 0f)
        {
            heliGunnerDebugTimer = 2f;
        }

        // Look for enemies
        heliGunnerCheckTimer -= Time.deltaTime;
        if (heliGunnerCheckTimer <= 0f)
        {
            heliGunnerCheckTimer = 0.3f;
            FindHeliGunnerTarget();
        }

        // Handle weapon
        HelicopterWeapon weapon = currentHeliSeat.mountedWeapon;
        if (weapon == null)
        {
            return;
        }

        // Shoot at target
        if (heliGunnerTarget != null)
        {
            // Aim at target
            Vector3 targetPos = heliGunnerTarget.position + Vector3.up * 1f;
            Vector3 aimDir = (targetPos - weapon.muzzlePoint.position).normalized;

            // Check angle - can we see the target from this gun position?
            float angle = Vector3.Angle(currentHeliSeat.transform.forward, aimDir);
            if (angle < 70f)
            {
                // Rotate the weapon/muzzle toward target
                if (weapon.muzzlePoint != null)
                {
                    Quaternion targetRot = Quaternion.LookRotation(aimDir);
                    weapon.muzzlePoint.rotation = Quaternion.Slerp(
                        weapon.muzzlePoint.rotation,
                        targetRot,
                        Time.deltaTime * 8f
                    );
                }

                // Fire weapon continuously to spin up and fire
                // Fire() handles its own rate limiting and spin-up
                weapon.Fire();

                // Debug log occasionally
                if (Random.value < 0.01f)
                {
                }
            }
            else
            {
                weapon.StopFiring();
            }
        }
        else
        {
            // No target - stop firing
            weapon.StopFiring();
        }
    }

    void FindHeliGunnerTarget()
    {
        heliGunnerTarget = null;
        float closestDist = 150f;
        Vector3 myPos = currentHelicopter != null ? currentHelicopter.transform.position : transform.position;

        // Find enemy players
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null || player.isDead) continue;
                if (player.playerTeam == team) continue;

                float dist = Vector3.Distance(myPos, player.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    heliGunnerTarget = player.transform;
                }
            }
        }

        // Find enemy AI
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.team == team) continue;
                // Skip AI that are also in helicopters (harder to hit)
                if (ai.currentState == AIState.HeliGunner) continue;

                float dist = Vector3.Distance(myPos, ai.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    heliGunnerTarget = ai.transform;
                }
            }
        }

        if (heliGunnerTarget != null)
        {
        }
    }

    // Called by helicopter when AI should enter as gunner
    public static void TryEnterHelicopterAsGunner(AIController ai, HelicopterController helicopter)
    {
        if (ai == null || helicopter == null) return;
        if (ai.currentState == AIState.Dead || ai.currentState == AIState.HeliGunner) return;
        if (helicopter.helicopterTeam != Team.None && helicopter.helicopterTeam != ai.team) return;

        // Find available gunner seat
        foreach (var seat in helicopter.seats)
        {
            if (seat == null || seat.IsOccupied) continue;
            if (seat.seatType == SeatType.DoorGunnerLeft || seat.seatType == SeatType.DoorGunnerRight)
            {
                ai.EnterHelicopterGunnerSeat(seat);
                return;
            }
        }
    }

    // ============ AI HELICOPTER PILOT SYSTEM ============

    // Assigned helicopter for dedicated pilots
    private HelicopterController assignedHelicopter;
    private bool isDedicatedPilot = false;

    // Called by HelicopterSpawner to assign this AI as a dedicated pilot
    public void AssignAsHelicopterPilot(HelicopterController helicopter)
    {
        if (helicopter == null) return;

        assignedHelicopter = helicopter;
        isDedicatedPilot = true;

        // Immediately enter the helicopter as pilot
        EnterHelicopterAsPilot(helicopter);

        Debug.Log($"[PILOT] {gameObject.name} assigned as dedicated pilot for {helicopter.name}");
    }

    // Check if this AI should re-pilot their assigned helicopter
    void CheckAssignedHelicopter()
    {
        if (!isDedicatedPilot) return;
        if (assignedHelicopter == null || assignedHelicopter.isDestroyed)
        {
            // Helicopter destroyed - no longer a dedicated pilot
            isDedicatedPilot = false;
            assignedHelicopter = null;
            return;
        }

        // If we're not piloting but should be, go back to helicopter
        if (currentState != AIState.HeliPilot && !assignedHelicopter.HasAIPilot)
        {
            float dist = Vector3.Distance(transform.position, assignedHelicopter.transform.position);
            if (dist < 10f)
            {
                // Close enough - enter
                EnterHelicopterAsPilot(assignedHelicopter);
            }
            else
            {
                // Move toward helicopter
                targetPosition = assignedHelicopter.transform.position;
                currentState = AIState.MovingToPoint;
            }
        }
    }

    public void EnterHelicopterAsPilot(HelicopterController helicopter)
    {
        if (helicopter == null) return;
        if (currentState == AIState.Dead || currentState == AIState.HeliPilot || currentState == AIState.HeliGunner) return;

        if (!helicopter.SetAIPilot(this)) return;

        pilotingHelicopter = helicopter;
        currentState = AIState.HeliPilot;

        // Initialize pilot state
        heliFirstFrame = true;
        heliHoverTimer = 10f;
        heliBoardingTimer = 5f;  // Wait 5 seconds for passengers to board
        heliTargetAltitude = 20f;  // Start with a reasonable target altitude
        heliTargetPosition = helicopter.transform.position + helicopter.transform.forward * 100f;

        // Disable collider to prevent physics collision with helicopter
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Disable NavMeshAgent (check enabled and isOnNavMesh before ResetPath)
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            agent.enabled = false;
            agentEnabled = false;
        }

        // Hide the AI model (they're "inside" the helicopter)
        SetModelVisible(false);

    }

    public void ExitHelicopterAsPilot()
    {
        if (pilotingHelicopter == null) return;

        pilotingHelicopter.ClearAIPilot();
        pilotingHelicopter = null;
        currentState = AIState.Idle;
        heliFirstFrame = true;  // Reset for next time

        // Re-enable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = true;
            agentEnabled = true;
            agent.Warp(transform.position);
        }

        // Re-enable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Show the AI model (also re-enables colliders)
        SetModelVisible(true);

    }

    void SetModelVisible(bool visible)
    {
        // Hide/show all renderers
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            r.enabled = visible;
        }

        // Also disable/enable colliders to prevent physics interactions
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            c.enabled = visible;
        }

        // Disable CharacterController if we have one
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = visible;
        }

        // Rigidbody should ALWAYS be kinematic for AI - NavMeshAgent controls movement
        // Setting isKinematic=false causes Rigidbody to fight with NavMeshAgent!
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;  // Always kinematic - NavMeshAgent handles movement
            rb.detectCollisions = false;  // No physics collisions needed
        }
    }

    void PickNewHeliDestination()
    {
        // Pick a capture point or random location to fly to
        CapturePoint[] points = FindObjectsOfType<CapturePoint>();
        if (points.Length > 0)
        {
            // Prefer enemy or neutral points
            List<CapturePoint> targets = new List<CapturePoint>();
            foreach (var p in points)
            {
                if (p.owningTeam != team)
                {
                    targets.Add(p);
                }
            }

            if (targets.Count > 0)
            {
                heliTargetPosition = targets[Random.Range(0, targets.Count)].transform.position;
            }
            else
            {
                heliTargetPosition = points[Random.Range(0, points.Length)].transform.position;
            }
        }
        else
        {
            // Random position
            heliTargetPosition = transform.position + new Vector3(
                Random.Range(-200f, 200f),
                0,
                Random.Range(-200f, 200f)
            );
        }

        heliTargetAltitude = Random.Range(15f, 25f);
    }

    void UpdateHeliPilotBehavior()
    {
        if (pilotingHelicopter == null || pilotingHelicopter.isDestroyed)
        {
            ExitHelicopterAsPilot();
            return;
        }

        // Keep AI position synced with helicopter
        transform.position = pilotingHelicopter.transform.position;

        // First frame - initialize transport mission
        if (heliFirstFrame)
        {
            heliFirstFrame = false;
            heliMissionPhase = HeliMissionPhase.WaitingForTroops;
            heliPassengers.Clear();
            Debug.Log($"[AI PILOT] Started piloting, initializing transport mission...");
        }

        // Wait for passengers to board before starting engine (initial boarding)
        if (heliBoardingTimer > 0f)
        {
            heliBoardingTimer -= Time.deltaTime;
            pilotingHelicopter.SetAIInput(0f, Vector2.zero, 0f);
            return;
        }

        // Start engine if not running
        if (!pilotingHelicopter.IsEngineOn)
        {
            pilotingHelicopter.StartEngine();
            return;
        }

        // Wait for rotors to spin up
        if (pilotingHelicopter.GetRotorSpeed() < 0.8f)
        {
            pilotingHelicopter.SetAIInput(0f, Vector2.zero, 0f);
            return;
        }

        // PRIORITY: If a player called this helicopter, fly to them
        if (pilotingHelicopter.IsBeingCalledByPlayer && pilotingHelicopter.CallingPlayer != null)
        {
            ExecuteFlyToCallingPlayer();
            return;
        }

        // Execute transport mission based on phase
        switch (heliMissionPhase)
        {
            case HeliMissionPhase.WaitingForTroops:
                ExecuteHeliWaitingForTroops();
                break;
            case HeliMissionPhase.FlyingToPickup:
                ExecuteHeliFlyingToPickup();
                break;
            case HeliMissionPhase.LandingForPickup:
                ExecuteHeliLandingForPickup();
                break;
            case HeliMissionPhase.LoadingTroops:
                ExecuteHeliLoadingTroops();
                break;
            case HeliMissionPhase.FlyingToObjective:
                ExecuteHeliFlyingToObjective();
                break;
            case HeliMissionPhase.LandingAtObjective:
                ExecuteHeliLandingAtObjective();
                break;
            case HeliMissionPhase.UnloadingTroops:
                ExecuteHeliUnloadingTroops();
                break;
        }
    }

    void ExecuteFlyToCallingPlayer()
    {
        FPSControllerPhoton player = pilotingHelicopter.CallingPlayer;
        if (player == null || player.isDead)
        {
            // Player died or disconnected - cancel call
            pilotingHelicopter.ClearPlayerCall();
            heliPlayerCallHasLandingPos = false;
            return;
        }

        Vector3 playerPos = player.transform.position;
        Vector3 heliPos = pilotingHelicopter.transform.position;

        // Find a landing zone near the player if we haven't already
        if (!heliPlayerCallHasLandingPos)
        {
            // First try designated landing zones near player
            LandingZone zone = LandingZone.FindNearestToPlayer(playerPos, team, 150f);
            if (zone != null)
            {
                heliPlayerCallLandingPos = zone.LandingPosition;
                zone.SetOccupied();
                currentLandingZone = zone;
                Debug.Log($"[AI PILOT] Player called - using designated landing zone at {zone.LandingPosition}");
            }
            else
            {
                // Fallback to finding a clear spot near the player
                heliPlayerCallLandingPos = FindClearLandingZone(playerPos, 10f, 40f);
                Debug.Log($"[AI PILOT] Player called - found clear landing zone at {heliPlayerCallLandingPos}");
            }
            heliPlayerCallHasLandingPos = true;
        }

        float horizontalDist = Vector3.Distance(new Vector3(heliPos.x, 0, heliPos.z), new Vector3(heliPlayerCallLandingPos.x, 0, heliPlayerCallLandingPos.z));
        float heightAboveGround = GetHeliHeightAboveGround();

        Debug.Log($"[AI PILOT] Flying to player LZ: horizontalDist={horizontalDist:F1}m, heightAboveGround={heightAboveGround:F1}m, isLanded={pilotingHelicopter.IsLanded}");

        // Only consider landing when we're actually close horizontally
        if (horizontalDist < 30f)
        {
            // We're near the landing zone - now check if we should land
            if (pilotingHelicopter.IsLanded && heightAboveGround < 3f)
            {
                // Actually landed on the ground - exit for the player
                Debug.Log($"[AI PILOT] Landed for player, exiting helicopter");
                pilotingHelicopter.ClearPlayerCall();
                heliPlayerCallHasLandingPos = false;

                // Release landing zone
                if (currentLandingZone != null)
                {
                    currentLandingZone.SetFree();
                    currentLandingZone = null;
                }

                // Eject passengers first
                EjectHeliPassengers();

                // Exit as pilot
                ExitHelicopterAsPilot();
                return;
            }
            else if (heightAboveGround < 10f)
            {
                // Low enough - descend to land
                HeliDescendToLanding();
            }
            else
            {
                // Still too high - descend while hovering over landing zone
                heliTargetPosition = heliPlayerCallLandingPos + Vector3.up * 5f;
                heliTargetAltitude = HELI_LANDING_HEIGHT;
                HeliFlyToTarget();
            }
        }
        else
        {
            // Fly toward the landing zone
            heliTargetPosition = heliPlayerCallLandingPos;
            heliTargetAltitude = HELI_CRUISE_ALTITUDE;

            if (horizontalDist < 80f)
            {
                // Getting close - start descending
                heliTargetAltitude = HELI_LANDING_HEIGHT + 10f;
            }

            HeliFlyToTarget();
        }
    }

    private float heliWaitingAtSpawnTimer = 0f;
    private const float HELI_MAX_WAIT_AT_SPAWN = 45f;  // Max time to wait at spawn for troops
    private const int HELI_MIN_PASSENGERS_TO_DEPART = 3;  // Minimum passengers before departing
    private bool heliIsAtSpawn = true;  // Track if we're at initial spawn or responding to calls

    void ExecuteHeliWaitingForTroops()
    {
        if (!pilotingHelicopter.IsEngineOn)
        {
            pilotingHelicopter.StartEngine();
        }

        int totalPassengers = CountHeliPassengers();
        heliWaitingAtSpawnTimer += Time.deltaTime;

        // If we're at initial spawn, stay grounded and wait for troops
        if (heliIsAtSpawn)
        {
            // Stay on the ground at spawn
            pilotingHelicopter.SetAIInput(-0.2f, Vector2.zero, 0f);

            // Debug every 5 seconds
            if (Mathf.FloorToInt(heliWaitingAtSpawnTimer) % 5 == 0 &&
                Mathf.Abs(heliWaitingAtSpawnTimer - Mathf.Floor(heliWaitingAtSpawnTimer)) < Time.deltaTime)
            {
                Debug.Log($"[AI PILOT] Waiting at spawn... {totalPassengers}/{HELI_MIN_PASSENGERS_TO_DEPART} passengers, {heliWaitingAtSpawnTimer:F0}s/{HELI_MAX_WAIT_AT_SPAWN}s");
            }

            // Depart conditions at spawn
            bool shouldDepart = false;
            if (totalPassengers >= HELI_MIN_PASSENGERS_TO_DEPART)
            {
                shouldDepart = true;
                Debug.Log($"[AI PILOT] Full load ({totalPassengers}). Departing spawn!");
            }
            else if (totalPassengers >= 1 && heliWaitingAtSpawnTimer >= HELI_MAX_WAIT_AT_SPAWN * 0.5f)
            {
                shouldDepart = true;
                Debug.Log($"[AI PILOT] Partial load ({totalPassengers}), waited long enough. Departing!");
            }
            else if (heliWaitingAtSpawnTimer >= HELI_MAX_WAIT_AT_SPAWN)
            {
                shouldDepart = true;
                Debug.Log($"[AI PILOT] Max wait time. Taking off to patrol for transport requests.");
            }

            if (shouldDepart)
            {
                heliWaitingAtSpawnTimer = 0f;
                heliIsAtSpawn = false;  // No longer at spawn after first departure

                if (totalPassengers > 0)
                {
                    heliMissionPhase = HeliMissionPhase.LoadingTroops;
                    heliLandingTimer = 0.1f;  // Trigger immediate departure
                }
                else
                {
                    // Take off and hover, waiting for transport calls
                    heliTargetAltitude = HELI_CRUISE_ALTITUDE;
                    HeliHover();
                }
            }
        }
        else
        {
            // We're NOT at spawn - hover and check for transport requests
            HeliHover();

            // Check for transport requests every few seconds
            if (heliWaitingAtSpawnTimer > 3f)
            {
                heliWaitingAtSpawnTimer = 0f;

                TransportRequest request = GetBestTransportRequest();
                if (request != null)
                {
                    // Respond to transport request!
                    Debug.Log($"[AI PILOT] Responding to transport request at {request.position} ({request.requestingTroopCount} troops)");
                    ClearTransportRequest(request);

                    heliPickupPosition = FindClearLandingZone(request.position, 10f, 25f);
                    heliTargetPosition = heliPickupPosition;
                    heliTargetAltitude = HELI_CRUISE_ALTITUDE;
                    heliMissionPhase = HeliMissionPhase.FlyingToPickup;
                }
                else
                {
                    // No requests - check if we already have passengers from troops boarding while hovering
                    if (totalPassengers >= HELI_MIN_PASSENGERS_TO_DEPART)
                    {
                        Debug.Log($"[AI PILOT] Picked up {totalPassengers} passengers while hovering. Finding objective...");
                        heliMissionPhase = HeliMissionPhase.LoadingTroops;
                        heliLandingTimer = 0.1f;
                    }
                }
            }
        }
    }

    void ExecuteHeliFlyingToPickup()
    {
        float distToPickup = Vector3.Distance(pilotingHelicopter.transform.position, heliPickupPosition);

        if (distToPickup < 50f)
        {
            // Start landing
            heliTargetAltitude = HELI_LANDING_HEIGHT;
            heliMissionPhase = HeliMissionPhase.LandingForPickup;
            Debug.Log("[AI PILOT] Near pickup, landing...");
        }
        else
        {
            HeliFlyToTarget();
        }
    }

    void ExecuteHeliLandingForPickup()
    {
        // Check height above ground, not absolute Y position
        float heightAboveGround = GetHeliHeightAboveGround();

        if (heightAboveGround < HELI_LANDING_HEIGHT + 2f || pilotingHelicopter.IsLanded)
        {
            // Landed - start loading
            heliLandingTimer = HELI_LANDING_WAIT_TIME;
            heliMissionPhase = HeliMissionPhase.LoadingTroops;
            Debug.Log($"[AI PILOT] Landed for pickup (height: {heightAboveGround}m), waiting for troops to board...");
        }
        else
        {
            // Descend to landing
            HeliDescendToLanding();
        }
    }

    void ExecuteHeliLoadingTroops()
    {
        heliLandingTimer -= Time.deltaTime;

        // IMPORTANT: Keep engine on so IsWaitingForPassengers returns true
        if (!pilotingHelicopter.IsEngineOn)
        {
            pilotingHelicopter.StartEngine();
        }

        // Keep helicopter still on ground
        pilotingHelicopter.SetAIInput(-0.2f, Vector2.zero, 0f);

        // Count passengers
        int passengerCount = CountHeliPassengers();
        int virtualCount = pilotingHelicopter.GetVirtualPassengerCount();

        // Debug every few seconds
        if (Mathf.FloorToInt(heliLandingTimer) % 3 == 0 && Mathf.Abs(heliLandingTimer - Mathf.Floor(heliLandingTimer)) < Time.deltaTime)
        {
            Debug.Log($"[AI PILOT] Loading troops... timer={heliLandingTimer:F1}s, passengers={passengerCount} (virtual={virtualCount})");
        }

        if (heliLandingTimer <= 0f || passengerCount >= 4)
        {
            Debug.Log($"[AI PILOT] Loading complete! Timer={heliLandingTimer:F1}, passengers={passengerCount}. Finding objective...");

            Vector3 currentPos = pilotingHelicopter.transform.position;

            // Done loading - find tactical objective at least 100m away
            if (FindObjectiveToAttack(out CapturePoint objective, 100f))
            {
                heliTargetObjective = objective;
                // Find TACTICAL landing zone - flanks enemies, avoids fire
                heliDropoffPosition = FindTacticalLandingZone(objective.transform.position, 25f, 50f);

                // Ensure we're actually going somewhere (not dropping off near pickup)
                float dropoffFromPickup = Vector3.Distance(heliDropoffPosition, currentPos);
                if (dropoffFromPickup < 80f)
                {
                    // Try closer to objective
                    heliDropoffPosition = FindTacticalLandingZone(objective.transform.position, 15f, 30f);
                }

                heliTargetPosition = heliDropoffPosition;
                heliTargetAltitude = HELI_CRUISE_ALTITUDE;
                heliMissionPhase = HeliMissionPhase.FlyingToObjective;
                Debug.Log($"[AI PILOT TACTICAL] Deploying {passengerCount} troops to {objective.pointName} (flight: {Vector3.Distance(currentPos, heliDropoffPosition):F0}m)");
            }
            else if (FindObjectiveToAttack(out CapturePoint nearerObjective, 50f))
            {
                // Fallback to closer objective
                heliTargetObjective = nearerObjective;
                heliDropoffPosition = FindTacticalLandingZone(nearerObjective.transform.position, 20f, 35f);
                heliTargetPosition = heliDropoffPosition;
                heliTargetAltitude = HELI_CRUISE_ALTITUDE;
                heliMissionPhase = HeliMissionPhase.FlyingToObjective;
                Debug.Log($"[AI PILOT TACTICAL] Deploying to nearby {nearerObjective.pointName}");
            }
            else
            {
                // No objectives - take off and patrol
                Debug.Log($"[AI PILOT] No tactical objectives found. Patrolling.");
                heliTargetAltitude = HELI_CRUISE_ALTITUDE;
                heliMissionPhase = HeliMissionPhase.WaitingForTroops;
            }
        }
    }

    private float flyingDebugTimer = 0f;
    private float tacticalReassessTimer = 0f;

    void ExecuteHeliFlyingToObjective()
    {
        float distToObjective = Vector3.Distance(pilotingHelicopter.transform.position, heliDropoffPosition);
        float heightAboveGround = GetHeliHeightAboveGround();

        // TACTICAL REASSESSMENT - check every 5 seconds if we should change targets
        tacticalReassessTimer -= Time.deltaTime;
        if (tacticalReassessTimer <= 0f)
        {
            tacticalReassessTimer = 5f;

            // Check if our target is still valid
            if (heliTargetObjective != null)
            {
                // Did we capture it already?
                if (heliTargetObjective.owningTeam == team && !heliTargetObjective.isContested)
                {
                    Debug.Log($"[AI PILOT TACTICAL] {heliTargetObjective.pointName} already captured! Finding new target...");
                    if (FindObjectiveToAttack(out CapturePoint newObjective, 50f))
                    {
                        heliTargetObjective = newObjective;
                        heliDropoffPosition = FindTacticalLandingZone(newObjective.transform.position, 25f, 50f);
                        heliTargetPosition = heliDropoffPosition;
                        Debug.Log($"[AI PILOT TACTICAL] Redirecting to {newObjective.pointName}");
                    }
                }

                // Is there an EMERGENCY somewhere else?
                CapturePoint[] points = FindObjectsOfType<CapturePoint>();
                foreach (var point in points)
                {
                    if (point.isContested && point.owningTeam == team)
                    {
                        int friendlies = CountFriendliesNearPosition(point.transform.position, 40f);
                        int enemies = CountEnemiesNearPosition(point.transform.position, 40f);

                        // Our point is being overrun - redirect!
                        if (enemies > friendlies + 2 && point != heliTargetObjective)
                        {
                            float distToEmergency = Vector3.Distance(pilotingHelicopter.transform.position, point.transform.position);
                            if (distToEmergency < distToObjective + 100f)  // Don't redirect if emergency is much farther
                            {
                                Debug.Log($"[AI PILOT TACTICAL] EMERGENCY REDIRECT: {point.pointName} is being overrun!");
                                heliTargetObjective = point;
                                heliDropoffPosition = FindTacticalLandingZone(point.transform.position, 20f, 40f);
                                heliTargetPosition = heliDropoffPosition;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Debug logging every 3 seconds
        flyingDebugTimer -= Time.deltaTime;
        if (flyingDebugTimer <= 0f)
        {
            string targetName = heliTargetObjective != null ? heliTargetObjective.pointName : "unknown";
            Debug.Log($"[AI PILOT] Flying to {targetName}: dist={distToObjective:F0}m, alt={heightAboveGround:F1}m");
            flyingDebugTimer = 3f;
        }

        if (distToObjective < 60f)
        {
            // Start landing
            heliTargetAltitude = HELI_LANDING_HEIGHT;
            heliMissionPhase = HeliMissionPhase.LandingAtObjective;
            Debug.Log($"[AI PILOT] Approaching LZ for {heliTargetObjective?.pointName ?? "objective"}...");
        }
        else
        {
            HeliFlyToTarget();
        }
    }

    void ExecuteHeliLandingAtObjective()
    {
        // Check if landing zone is still safe - abort if too many enemies
        int enemiesNearLZ = CountEnemiesNearPosition(heliDropoffPosition, 30f);
        int friendliesNearLZ = CountFriendliesNearPosition(heliDropoffPosition, 30f);

        if (enemiesNearLZ > 3 && enemiesNearLZ > friendliesNearLZ)
        {
            // Too dangerous! Abort landing and find a new spot
            Debug.LogWarning($"[AI PILOT] ABORT LANDING! Too many enemies ({enemiesNearLZ}) near LZ. Finding new spot...");

            // Try to find a safer landing zone further out
            heliDropoffPosition = FindClearLandingZone(heliTargetObjective.transform.position, 60f, 100f);
            heliTargetPosition = heliDropoffPosition;
            heliTargetAltitude = HELI_CRUISE_ALTITUDE;
            heliMissionPhase = HeliMissionPhase.FlyingToObjective;
            return;
        }

        // Check height above ground, not absolute Y
        float heightAboveGround = GetHeliHeightAboveGround();

        if (heightAboveGround < HELI_LANDING_HEIGHT + 2f || pilotingHelicopter.IsLanded)
        {
            // Landed - start unloading
            heliUnloadTimer = HELI_LANDING_WAIT_TIME;
            heliMissionPhase = HeliMissionPhase.UnloadingTroops;

            // Tell passengers to exit
            EjectHeliPassengers();
            Debug.Log($"[AI PILOT] Landed at objective (height: {heightAboveGround}m), unloading troops...");
        }
        else
        {
            HeliDescendToLanding();
        }
    }

    void ExecuteHeliUnloadingTroops()
    {
        heliUnloadTimer -= Time.deltaTime;

        // Keep helicopter still on ground
        pilotingHelicopter.SetAIInput(-0.2f, Vector2.zero, 0f);

        if (heliUnloadTimer <= 0f)
        {
            // Done unloading - take off and wait for transport requests
            heliMissionPhase = HeliMissionPhase.WaitingForTroops;
            heliTargetAltitude = HELI_CRUISE_ALTITUDE;
            heliWaitingAtSpawnTimer = 0f;  // Reset wait timer
            Debug.Log("[AI PILOT] Unloading complete, taking off to respond to transport calls...");
        }
    }

    // Request transport pickup - called by AI troops who need a ride
    public static void RequestTransport(Vector3 position, Team requestingTeam, int troopCount)
    {
        // Don't add duplicate requests for same area
        foreach (var existing in pendingTransportRequests)
        {
            if (existing.team == requestingTeam && Vector3.Distance(existing.position, position) < 50f)
            {
                // Update existing request with more troops
                existing.requestingTroopCount = Mathf.Max(existing.requestingTroopCount, troopCount);
                existing.timestamp = Time.time;
                return;
            }
        }

        // Add new request
        pendingTransportRequests.Add(new TransportRequest
        {
            position = position,
            team = requestingTeam,
            timestamp = Time.time,
            requestingTroopCount = troopCount
        });

        Debug.Log($"[TRANSPORT] New pickup request at {position} for {requestingTeam} ({troopCount} troops)");
    }

    // Get best transport request for this team's helicopter
    TransportRequest GetBestTransportRequest()
    {
        // Clean up old requests (older than 60 seconds)
        pendingTransportRequests.RemoveAll(r => Time.time - r.timestamp > 60f);

        TransportRequest best = null;
        float bestScore = -1f;

        foreach (var request in pendingTransportRequests)
        {
            if (request.team != team) continue;

            float score = request.requestingTroopCount * 20f;  // More troops = higher priority

            // Prefer closer requests
            float dist = Vector3.Distance(pilotingHelicopter.transform.position, request.position);
            score -= dist * 0.1f;

            // Prefer recent requests
            float age = Time.time - request.timestamp;
            score -= age * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = request;
            }
        }

        return best;
    }

    // Remove a transport request (when we're responding to it)
    void ClearTransportRequest(TransportRequest request)
    {
        pendingTransportRequests.Remove(request);
    }

    // Helper: Find troops that need transport (not already near an objective)
    bool FindTroopsNeedingTransport(out Vector3 pickupPosition)
    {
        pickupPosition = Vector3.zero;
        AIController[] allAI = FindObjectsOfType<AIController>();
        CapturePoint[] objectives = FindObjectsOfType<CapturePoint>();
        List<AIController> availableTroops = new List<AIController>();

        foreach (var ai in allAI)
        {
            if (ai == this) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.Dead) continue;
            if (ai.currentState == AIState.HeliGunner || ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliPassenger) continue;
            if (ai.currentState == AIState.BoardingHelicopter) continue;  // Already trying to board
            if (ai.currentHelicopter != null) continue;  // Already in a helicopter

            // Skip troops that are already near an objective they should attack
            bool nearObjective = false;
            foreach (var obj in objectives)
            {
                if (obj.owningTeam == team) continue;  // Skip friendly objectives
                float distToObj = Vector3.Distance(ai.transform.position, obj.transform.position);
                if (distToObj < 60f)
                {
                    nearObjective = true;
                    break;
                }
            }
            if (nearObjective) continue;  // Don't pick up troops already in combat range

            availableTroops.Add(ai);
        }

        if (availableTroops.Count >= 2)  // Need at least 2 troops to bother picking up
        {
            // Find center of troops
            Vector3 center = Vector3.zero;
            foreach (var ai in availableTroops)
            {
                center += ai.transform.position;
            }
            pickupPosition = center / availableTroops.Count;
            Debug.Log($"[AI PILOT] Found {availableTroops.Count} troops needing transport");
            return true;
        }

        return false;
    }

    // Helper: Find objective to attack - TACTICAL DECISION MAKING
    // Prioritizes: 1) Contested points we're losing, 2) Weak enemy points, 3) Support friendly attacks
    bool FindObjectiveToAttack(out CapturePoint objective, float minDistanceFromHeli = 0f)
    {
        objective = null;
        CapturePoint[] points = FindObjectsOfType<CapturePoint>();
        Vector3 heliPos = pilotingHelicopter != null ? pilotingHelicopter.transform.position : transform.position;

        // First, analyze the overall battlefield situation
        int ourPoints = 0;
        int enemyPoints = 0;
        int neutralPoints = 0;
        CapturePoint criticalPoint = null;  // Point that urgently needs help

        foreach (var point in points)
        {
            if (point.owningTeam == team) ourPoints++;
            else if (point.owningTeam == Team.None) neutralPoints++;
            else enemyPoints++;

            // Check for critical situations - contested point we're about to lose
            if (point.isContested && point.owningTeam == team)
            {
                int friendlies = CountFriendliesNearPosition(point.transform.position, 40f);
                int enemies = CountEnemiesNearPosition(point.transform.position, 40f);
                if (enemies > friendlies)
                {
                    criticalPoint = point;  // We're losing this point!
                }
            }
        }

        // PRIORITY 1: Emergency reinforcement of point we're losing
        if (criticalPoint != null)
        {
            float dist = Vector3.Distance(heliPos, criticalPoint.transform.position);
            if (dist >= minDistanceFromHeli)
            {
                objective = criticalPoint;
                Debug.Log($"[AI PILOT TACTICAL] EMERGENCY: Reinforcing {criticalPoint.pointName} - we're being overrun!");
                return true;
            }
        }

        // Score each capturable point
        CapturePoint bestPoint = null;
        float bestScore = float.MinValue;

        foreach (var point in points)
        {
            // Skip points we already own (unless contested)
            if (point.owningTeam == team && !point.isContested) continue;

            // Skip points too close to helicopter
            float distFromHeli = Vector3.Distance(heliPos, point.transform.position);
            if (minDistanceFromHeli > 0f && distFromHeli < minDistanceFromHeli)
            {
                continue;
            }

            float score = 0f;
            int friendliesNear = CountFriendliesNearPosition(point.transform.position, 50f);
            int enemiesNear = CountEnemiesNearPosition(point.transform.position, 50f);

            // === TACTICAL SCORING ===

            // PRIORITY 2: Contested enemy/neutral points where we have a chance
            if (point.isContested)
            {
                if (point.owningTeam != team)
                {
                    // We're attacking - big bonus if we're winning
                    if (friendliesNear >= enemiesNear)
                        score += 150f;  // Push the advantage!
                    else if (friendliesNear > 0)
                        score += 100f;  // Reinforce the attack
                }
                else
                {
                    // Our point being contested - defend it
                    score += 120f;
                }
            }

            // PRIORITY 3: Weakly defended enemy points (soft targets)
            if (point.owningTeam != team && point.owningTeam != Team.None)
            {
                if (enemiesNear == 0)
                    score += 130f;  // Undefended enemy point - easy capture!
                else if (enemiesNear <= 2)
                    score += 80f;   // Lightly defended
                else
                    score += 40f;   // Heavily defended - less attractive
            }

            // PRIORITY 4: Neutral points
            if (point.owningTeam == Team.None)
            {
                score += 60f;
                if (enemiesNear == 0)
                    score += 40f;  // Free capture
            }

            // BONUS: Support existing friendly push
            int friendliesHeading = CountFriendliesHeadingToPosition(point.transform.position, 60f);
            if (friendliesHeading >= 3)
                score += 50f;  // Big push incoming - support it!
            else if (friendliesHeading >= 1)
                score += 20f;

            // PENALTY: Don't send troops where we already have enough
            if (friendliesNear >= 5)
                score -= 60f;  // Already have plenty of troops there

            // PENALTY: Avoid heavily defended points when we're outnumbered
            if (enemiesNear > friendliesNear + 2)
                score -= 40f;  // Bad odds

            // Small distance factor (prefer closer, but not heavily)
            score -= distFromHeli * 0.02f;

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        if (bestPoint != null)
        {
            objective = bestPoint;
            int friendlies = CountFriendliesNearPosition(bestPoint.transform.position, 50f);
            int enemies = CountEnemiesNearPosition(bestPoint.transform.position, 50f);
            Debug.Log($"[AI PILOT TACTICAL] Target: {bestPoint.pointName} (score:{bestScore:F0}, friendlies:{friendlies}, enemies:{enemies})");
            return true;
        }

        return false;
    }

    // Currently used landing zone (to mark as occupied)
    private LandingZone currentLandingZone = null;

    // Find a TACTICAL landing zone - PREFERS designated LandingZone objects
    Vector3 FindTacticalLandingZone(Vector3 objectivePos, float minDist, float maxDist)
    {
        // FIRST: Check for designated landing zones near the objective
        LandingZone zone = LandingZone.FindNearestToObjective(objectivePos, team, minDist, maxDist * 1.5f);
        if (zone != null)
        {
            zone.SetOccupied();
            currentLandingZone = zone;
            Debug.Log($"[AI PILOT] Using designated landing zone at {zone.LandingPosition}");
            return zone.LandingPosition;
        }

        // FALLBACK: Procedural landing zone finding (old behavior)
        Debug.Log($"[AI PILOT] No landing zones found, using procedural search...");

        // Find where the enemies are concentrated
        Vector3 enemyCenter = Vector3.zero;
        int enemyCount = 0;

        AIController[] allAI = FindObjectsOfType<AIController>();
        foreach (var ai in allAI)
        {
            if (ai.team == team) continue;
            if (ai.currentState == AIState.Dead) continue;
            float dist = Vector3.Distance(ai.transform.position, objectivePos);
            if (dist < 80f)
            {
                enemyCenter += ai.transform.position;
                enemyCount++;
            }
        }

        Vector3 flankDirection;
        if (enemyCount > 0)
        {
            enemyCenter /= enemyCount;
            // Land on the OPPOSITE side from enemies (flank)
            flankDirection = (objectivePos - enemyCenter).normalized;
        }
        else
        {
            // No enemies - just pick a random direction
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            flankDirection = new Vector3(Mathf.Cos(randomAngle), 0f, Mathf.Sin(randomAngle));
        }

        // Try to find a good landing spot on the flank
        Vector3 bestPos = objectivePos + flankDirection * ((minDist + maxDist) / 2f);
        float bestScore = -1000f;

        // Test multiple positions, preferring the flank direction
        for (int i = 0; i < 16; i++)
        {
            float angle = (i / 16f) * 360f * Mathf.Deg2Rad;
            float distance = Mathf.Lerp(minDist, maxDist, (i % 4) / 3f);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            Vector3 testPos = objectivePos + dir * distance;

            // Raycast to find ground - use RaycastAll to find terrain, not rooftops
            RaycastHit[] hits = Physics.RaycastAll(testPos + Vector3.up * 80f, Vector3.down, 150f);
            if (hits.Length > 0)
            {
                // Sort by distance
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                // Find terrain hit, or use closest hit
                RaycastHit hit = hits[0];
                bool foundTerrain = false;
                foreach (var h in hits)
                {
                    bool isTerrain = h.collider.gameObject.layer == LayerMask.NameToLayer("Terrain") ||
                                     h.collider.GetComponent<Terrain>() != null ||
                                     h.collider.gameObject.name.ToLower().Contains("terrain") ||
                                     h.collider.gameObject.name.ToLower().Contains("ground");
                    if (isTerrain)
                    {
                        hit = h;
                        foundTerrain = true;
                        break;
                    }
                }

                testPos = hit.point;
                float score = 0f;

                // If we didn't find terrain, penalize heavily (we're on a rooftop)
                if (!foundTerrain)
                {
                    score -= 150f;
                }

                // BONUS: Prefer flank direction (away from enemies)
                float flankAlignment = Vector3.Dot(dir, flankDirection);
                score += flankAlignment * 50f;

                // BONUS: Flat ground
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeAngle < 10f) score += 30f;
                else if (slopeAngle > 25f) score -= 50f;

                // PENALTY: Close to enemies
                int nearbyEnemies = CountEnemiesNearPosition(testPos, 30f);
                score -= nearbyEnemies * 40f;

                // BONUS: Has cover nearby (buildings, terrain)
                if (Physics.Raycast(testPos + Vector3.up * 2f, flankDirection, 15f))
                {
                    // Something behind us for cover
                    score += 20f;
                }

                // BONUS: Clear approach to objective
                Vector3 toObjective = objectivePos - testPos;
                if (!Physics.Raycast(testPos + Vector3.up * 2f, toObjective.normalized, toObjective.magnitude * 0.8f))
                {
                    score += 25f;  // Clear path to objective
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = testPos;
                }
            }
        }

        Debug.Log($"[AI PILOT TACTICAL] Landing zone: flank score={bestScore:F0}, {(enemyCount > 0 ? "flanking enemies" : "no enemies detected")}");
        return bestPos;
    }

    // Helper: Count friendly soldiers near a position
    int CountFriendliesNearPosition(Vector3 position, float radius)
    {
        int count = 0;
        AIController[] allAI = FindObjectsOfType<AIController>();

        foreach (var ai in allAI)
        {
            if (ai == this) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.Dead) continue;
            // Don't count helicopter crew
            if (ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliGunner || ai.currentState == AIState.HeliPassenger) continue;

            float dist = Vector3.Distance(ai.transform.position, position);
            if (dist < radius)
            {
                count++;
            }
        }

        return count;
    }

    // Helper: Count nearby friendly soldiers who need transport (far from objectives, not in heli)
    int CountNearbyFriendliesNeedingTransport()
    {
        int count = 1;  // Include self
        AIController[] allAI = FindObjectsOfType<AIController>();

        foreach (var ai in allAI)
        {
            if (ai == this) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.Dead) continue;
            if (ai.currentHelicopter != null) continue;  // Already in heli
            if (ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliGunner ||
                ai.currentState == AIState.HeliPassenger || ai.currentState == AIState.BoardingHelicopter) continue;
            if (ai.heliExitCooldown > 0f) continue;  // Just exited, don't need transport

            // Must be nearby (within 30m)
            float dist = Vector3.Distance(ai.transform.position, transform.position);
            if (dist > 30f) continue;

            // Must also be far from objectives
            if (ai.targetPoint != null)
            {
                float distToObj = Vector3.Distance(ai.transform.position, ai.targetPoint.transform.position);
                if (distToObj > 100f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // Helper: Count friendly soldiers heading toward a position
    int CountFriendliesHeadingToPosition(Vector3 position, float radius)
    {
        int count = 0;
        AIController[] allAI = FindObjectsOfType<AIController>();

        foreach (var ai in allAI)
        {
            if (ai == this) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.Dead) continue;
            if (ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliGunner || ai.currentState == AIState.HeliPassenger) continue;

            // Check if this AI's target position is near the objective
            float distToTarget = Vector3.Distance(ai.targetPosition, position);
            if (distToTarget < radius)
            {
                count++;
            }
        }

        return count;
    }

    // Helper: Count passengers in helicopter
    int CountHeliPassengers()
    {
        if (pilotingHelicopter == null) return 0;

        int count = 0;

        // Count physical seat passengers
        HelicopterSeat[] seats = pilotingHelicopter.GetComponentsInChildren<HelicopterSeat>();
        foreach (var seat in seats)
        {
            if (seat.seatType != SeatType.Pilot && seat.IsOccupied)
            {
                count++;
            }
        }

        // Count virtual passengers
        count += pilotingHelicopter.GetVirtualPassengerCount();

        return count;
    }

    // Helper: Eject all passengers
    void EjectHeliPassengers()
    {
        if (pilotingHelicopter == null) return;

        // Eject physical seat passengers
        HelicopterSeat[] seats = pilotingHelicopter.GetComponentsInChildren<HelicopterSeat>();
        foreach (var seat in seats)
        {
            if (seat.seatType == SeatType.Pilot) continue;
            if (seat.aiOccupant != null)
            {
                seat.aiOccupant.ExitHelicopterSeat();
            }
        }

        // Eject virtual passengers (no physical seat)
        pilotingHelicopter.EjectAllVirtualPassengers();
    }

    // ==================== JET PILOT METHODS ====================

    public void AssignAsJetPilot(JetController jet, Runway runway = null)
    {
        if (jet == null) return;

        pilotingJet = jet;
        jetHomeRunway = runway;
        jet.SetAIPilot(this);
        currentState = AIState.JetPilot;
        jetAttackTimer = 10f; // Wait 10 seconds before takeoff
        jetPatrolCenter = jet.transform.position; // Patrol around spawn point

        // Store runway for takeoff direction
        if (runway != null)
        {
            jet.SetOnRunway(runway);
        }

        // Hide the AI soldier model (we're in the cockpit)
        SetModelVisible(false);

        // Disable NavMesh
        if (agent != null)
        {
            agent.enabled = false;
            agentEnabled = false;
        }

        Debug.Log($"[AI JET PILOT] {gameObject.name} assigned to jet {jet.name}, runway: {runway?.name ?? "none"}");
    }

    public void ExitJet()
    {
        if (pilotingJet != null)
        {
            pilotingJet.ClearAIPilot();
        }

        pilotingJet = null;
        jetHomeRunway = null;
        currentState = AIState.Idle;

        // Show model again
        SetModelVisible(true);

        // Re-enable NavMesh
        if (agent != null)
        {
            agent.enabled = true;
            agentEnabled = true;
        }

        Debug.Log($"[AI JET PILOT] {gameObject.name} exited jet");
    }

    Transform FindJetTarget()
    {
        if (pilotingJet == null) return null;

        Transform bestTarget = null;
        float bestScore = float.MinValue;
        float maxRange = 800f;

        // Search for enemy AI soldiers
        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        foreach (var ai in allAI)
        {
            if (ai == null || ai == this) continue;
            if (ai.team == team) continue;  // Same team
            if (ai.isDead) continue;

            float dist = Vector3.Distance(pilotingJet.transform.position, ai.transform.position);
            if (dist > maxRange) continue;

            // Prefer closer targets and targets in front
            Vector3 toTarget = (ai.transform.position - pilotingJet.transform.position).normalized;
            float dotProduct = Vector3.Dot(pilotingJet.transform.forward, toTarget);

            float score = (maxRange - dist) + dotProduct * 200f;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = ai.transform;
            }
        }

        // Also check for enemy helicopters
        HelicopterController[] helis = FindObjectsByType<HelicopterController>(FindObjectsSortMode.None);
        foreach (var heli in helis)
        {
            if (heli == null || heli.isDestroyed) continue;
            if (heli.helicopterTeam == team) continue;

            float dist = Vector3.Distance(pilotingJet.transform.position, heli.transform.position);
            if (dist > maxRange) continue;

            Vector3 toTarget = (heli.transform.position - pilotingJet.transform.position).normalized;
            float dotProduct = Vector3.Dot(pilotingJet.transform.forward, toTarget);

            // Prioritize helicopters slightly
            float score = (maxRange - dist) + dotProduct * 200f + 100f;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = heli.transform;
            }
        }

        // Check for enemy jets
        JetController[] jets = FindObjectsByType<JetController>(FindObjectsSortMode.None);
        foreach (var jet in jets)
        {
            if (jet == null || jet.isDestroyed) continue;
            if (jet == pilotingJet) continue;
            if (jet.jetTeam == team) continue;

            float dist = Vector3.Distance(pilotingJet.transform.position, jet.transform.position);
            if (dist > maxRange) continue;

            Vector3 toTarget = (jet.transform.position - pilotingJet.transform.position).normalized;
            float dotProduct = Vector3.Dot(pilotingJet.transform.forward, toTarget);

            // High priority for enemy jets
            float score = (maxRange - dist) + dotProduct * 200f + 150f;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = jet.transform;
            }
        }

        return bestTarget;
    }

    /// <summary>
    /// AI Jet Pilot Behavior - Updated for new flight physics system
    ///
    /// This AI pilot uses a phased approach:
    /// 1. Ground ops - taxi and takeoff roll
    /// 2. Takeoff - rotation and liftoff
    /// 3. Climb - safe climb to cruise altitude
    /// 4. Combat/Patrol - normal operations
    ///
    /// Key principles:
    /// - Smooth, progressive inputs (no sudden stick movements)
    /// - Bank-to-turn methodology (roll then pitch)
    /// - Altitude awareness (crash prevention)
    /// - Energy management (speed vs altitude)
    /// </summary>
    void UpdateJetPilotBehavior()
    {
        if (pilotingJet == null || pilotingJet.isDestroyed)
        {
            ExitJet();
            return;
        }

        // Keep AI controller position synced with jet
        transform.position = pilotingJet.transform.position;

        // Wait for engine start
        if (!pilotingJet.IsEngineOn)
        {
            jetAttackTimer -= Time.deltaTime;
            if (jetAttackTimer <= 0f)
            {
                pilotingJet.StartEngine();
                Debug.Log("[AI Pilot] Engine started");
            }
            return;
        }

        // Get current flight data
        float speed = pilotingJet.CurrentSpeed;
        float altitude = pilotingJet.Altitude;
        float bankAngle = pilotingJet.BankAngle;
        float pitchAngle = pilotingJet.PitchAngle;
        bool isOnGround = pilotingJet.IsOnGround;

        // Initialize control inputs
        float throttle = 1f;
        float pitchInput = 0f;
        float rollInput = 0f;
        float yawInput = 0f;
        bool fireGuns = false;

        // ==========================================
        // PHASE 1: GROUND OPERATIONS & TAKEOFF
        // ==========================================
        if (isOnGround)
        {
            // Full throttle for takeoff
            throttle = 1f;

            // Rotation speed check (when to pull back for liftoff)
            float rotationSpeed = 42f; // 80% of takeoff speed (55 * 0.8 = 44)

            if (speed >= rotationSpeed)
            {
                // Rotation phase - pull back firmly to lift nose
                pitchInput = 0.6f; // Strong pull to ensure rotation
                Debug.Log($"[AI Pilot] Rotation at {speed:F1} m/s, pitch input: {pitchInput}");
            }
            else
            {
                // Takeoff roll - keep nose level
                pitchInput = 0f;
            }

            // Keep wings absolutely level on ground
            rollInput = -bankAngle * 0.05f;

            // Runway tracking with nose wheel steering
            if (jetHomeRunway != null && jetHomeRunway.takeoffPoint != null)
            {
                Vector3 runwayDir = jetHomeRunway.RunwayDirection;
                Vector3 jetForwardFlat = Vector3.ProjectOnPlane(pilotingJet.transform.forward, Vector3.up).normalized;
                float angleToRunway = Vector3.SignedAngle(jetForwardFlat, runwayDir, Vector3.up);

                // Gentle correction to stay aligned
                yawInput = Mathf.Clamp(angleToRunway * 0.05f, -0.5f, 0.5f);
            }

            pilotingJet.SetAIInput(throttle, pitchInput, rollInput, yawInput);
            return;
        }

        // ==========================================
        // PHASE 2: INITIAL CLIMB (Just left ground)
        // ==========================================
        // Goal: Safely climb away from ground, maintain runway heading
        if (altitude < 100f)
        {
            throttle = 1f; // Full power for climb

            // Climb at a safe pitch angle (15-20 degrees)
            float targetClimbPitch = 18f;
            float pitchError = targetClimbPitch - pitchAngle;
            pitchInput = Mathf.Clamp(pitchError * 0.05f, -0.3f, 0.5f);

            // Prevent excessive pitch
            if (pitchAngle > 25f)
            {
                pitchInput = -0.3f; // Push nose down
            }
            else if (pitchAngle < 5f)
            {
                pitchInput = 0.4f; // Pull nose up
            }

            // Keep wings level during climb
            rollInput = -bankAngle * 0.04f;

            pilotingJet.SetAIInput(throttle, pitchInput, rollInput, 0f);
            Debug.Log($"[AI Pilot] Climbing - Alt: {altitude:F0}m, Pitch: {pitchAngle:F1}°, Speed: {speed:F1}");
            return;
        }

        // ==========================================
        // PHASE 3: CLIMB TO CRUISE ALTITUDE
        // ==========================================
        if (altitude < 150f)
        {
            throttle = 0.9f;

            // Gentler climb as we approach cruise altitude
            float targetClimbPitch = 12f;
            float pitchError = targetClimbPitch - pitchAngle;
            pitchInput = Mathf.Clamp(pitchError * 0.04f, -0.3f, 0.4f);

            // Keep wings level
            rollInput = -bankAngle * 0.04f;

            pilotingJet.SetAIInput(throttle, pitchInput, rollInput, 0f);
            return;
        }

        // ==========================================
        // PHASE 4: COMBAT / PATROL OPERATIONS
        // ==========================================

        // HARD MAP BOUNDARIES - Force turn if outside bounds
        float mapBoundary = 500f;
        Vector3 jetPos = pilotingJet.transform.position;
        bool outOfBounds = Mathf.Abs(jetPos.x) > mapBoundary || Mathf.Abs(jetPos.z) > mapBoundary;

        if (outOfBounds)
        {
            // Emergency return to map center!
            Vector3 toCenter = -jetPos;
            toCenter.y = 0;
            toCenter = toCenter.normalized;

            Vector3 jetForward = pilotingJet.transform.forward;
            jetForward.y = 0;
            jetForward = jetForward.normalized;

            float angleToCenter = Vector3.SignedAngle(jetForward, toCenter, Vector3.up);

            // Aggressive bank toward center
            float bankDir = Mathf.Sign(angleToCenter);
            rollInput = bankDir * 0.8f;

            // Pull back while banked to turn
            if (Mathf.Abs(bankAngle) > 20f)
            {
                pitchInput = 0.5f;
            }

            // Maintain altitude
            if (pitchAngle < -5f) pitchInput = 0.6f;
            if (pitchAngle > 30f) pitchInput = -0.3f;

            throttle = 0.9f;

            Debug.LogWarning($"[AI Pilot] OUT OF BOUNDS at ({jetPos.x:F0}, {jetPos.z:F0})! Turning back, angle: {angleToCenter:F0}");
            pilotingJet.SetAIInput(throttle, pitchInput, rollInput, 0f);
            return;
        }

        // Determine mission target
        Vector3 targetPos;
        float targetAlt = 120f; // Default cruise altitude
        bool hasEnemy = false;

        // Search for enemies
        Transform enemy = FindJetTarget();
        if (enemy != null)
        {
            targetPos = enemy.position;
            targetAlt = Mathf.Max(80f, enemy.position.y); // Match or exceed enemy altitude
            hasEnemy = true;
        }
        else
        {
            // Patrol behavior - circle near map center
            Vector3 mapCenter = Vector3.zero;

            // Stay close to center
            float distFromCenter = Vector3.Distance(new Vector3(jetPos.x, 0, jetPos.z), mapCenter);
            if (distFromCenter > 250f)
            {
                // Head back toward center
                targetPos = mapCenter;
                Debug.Log($"[AI Pilot] Returning to center, dist: {distFromCenter:F0}m");
            }
            else if (jetPatrolTimer <= 0f || Vector3.Distance(pilotingJet.transform.position, jetPatrolCenter) < 80f)
            {
                // Pick new patrol waypoint near center (within 200m)
                jetPatrolCenter = new Vector3(
                    Random.Range(-200f, 200f),
                    0f,
                    Random.Range(-200f, 200f)
                );
                jetPatrolTimer = 20f;
                Debug.Log($"[AI Pilot] New patrol waypoint: {jetPatrolCenter}");
            }
            jetPatrolTimer -= Time.deltaTime;
            targetPos = jetPatrolCenter;
        }

        // Calculate navigation vectors
        Vector3 toTarget = targetPos - pilotingJet.transform.position;
        float distToTarget = toTarget.magnitude;

        // Flatten target vector for horizontal navigation
        Vector3 toTargetFlat = new Vector3(toTarget.x, 0f, toTarget.z);
        Vector3 forwardFlat = new Vector3(pilotingJet.transform.forward.x, 0f, pilotingJet.transform.forward.z);
        float angleToTarget = Vector3.SignedAngle(forwardFlat, toTargetFlat, Vector3.up);

        // === CRASH PREVENTION - HIGHEST PRIORITY ===
        bool emergencyMode = false;

        if (altitude < 35f)
        {
            // CRITICAL ALTITUDE - Emergency pull-up!
            pitchInput = 1f;
            rollInput = -bankAngle * 0.1f; // Level wings urgently
            throttle = 1f;
            emergencyMode = true;
            Debug.LogWarning($"[AI Pilot] EMERGENCY PULL-UP at {altitude:F0}m!");
        }
        else if (altitude < 60f && pitchAngle < -10f)
        {
            // Low altitude with nose down - dangerous
            pitchInput = 0.8f;
            rollInput = -bankAngle * 0.06f;
            throttle = 1f;
            emergencyMode = true;
            Debug.LogWarning($"[AI Pilot] Low altitude recovery");
        }

        if (!emergencyMode)
        {
            // === NORMAL FLIGHT CONTROL ===

            // STEP 1: ROLL CONTROL (Bank to turn)
            // Calculate desired bank angle based on how much we need to turn
            float desiredBank = 0f;

            if (Mathf.Abs(angleToTarget) > 8f)
            {
                // Need to turn - bank into the turn
                // Larger turns need more bank, but cap at 40 degrees for safety
                float turnSharpness = Mathf.Abs(angleToTarget) / 180f; // 0 to 1
                float maxBank = hasEnemy ? 50f : 40f; // More aggressive in combat

                desiredBank = Mathf.Sign(angleToTarget) * Mathf.Lerp(15f, maxBank, turnSharpness);
            }
            else
            {
                // On heading - level wings
                desiredBank = 0f;
            }

            // Apply roll input to achieve desired bank
            float bankError = desiredBank - bankAngle;
            rollInput = Mathf.Clamp(bankError * 0.06f, -1f, 1f);

            // STEP 2: PITCH CONTROL (Altitude + Turn execution)
            pitchInput = 0f;

            // When banked, pull back to execute the turn (bank-to-turn)
            if (Mathf.Abs(bankAngle) > 12f)
            {
                // More bank = more pull needed to maintain altitude in turn
                float turnPull = Mathf.Abs(bankAngle) / 50f; // 0 to 1
                pitchInput += turnPull * 0.6f;
            }

            // Altitude management (only when wings are relatively level)
            if (Mathf.Abs(bankAngle) < 35f)
            {
                float altError = targetAlt - altitude;

                if (Mathf.Abs(altError) > 30f)
                {
                    // Significant altitude error - make correction
                    float correction = Mathf.Clamp(altError * 0.01f, -0.3f, 0.4f);
                    pitchInput += correction;
                }
                else if (Mathf.Abs(altError) > 10f)
                {
                    // Fine altitude adjustment
                    float correction = Mathf.Clamp(altError * 0.008f, -0.15f, 0.2f);
                    pitchInput += correction;
                }
            }

            // Pitch safety limits (prevent dangerous attitudes)
            if (pitchAngle > 35f)
            {
                // Nose too high - risk of stall
                pitchInput = Mathf.Min(pitchInput, -0.3f);
            }
            else if (pitchAngle < -25f)
            {
                // Nose too low - steep dive
                pitchInput = Mathf.Max(pitchInput, 0.5f);
            }

            // Clamp final pitch input
            pitchInput = Mathf.Clamp(pitchInput, -1f, 1f);

            // STEP 3: THROTTLE MANAGEMENT
            if (hasEnemy)
            {
                // Combat - full throttle
                throttle = 1f;
            }
            else
            {
                // Cruise - efficient throttle
                // Adjust based on speed
                if (speed < 90f)
                {
                    throttle = 0.9f; // Speed up
                }
                else if (speed > 140f)
                {
                    throttle = 0.6f; // Slow down
                }
                else
                {
                    throttle = 0.75f; // Cruise
                }
            }

            // STEP 4: WEAPONS
            if (hasEnemy)
            {
                // Check if target is in firing solution
                float angleOffTarget = Mathf.Abs(angleToTarget);
                bool targetInRange = distToTarget < 500f && distToTarget > 50f;
                bool targetInSights = angleOffTarget < 12f;

                if (targetInRange && targetInSights)
                {
                    fireGuns = true;
                    Debug.Log($"[AI Pilot] Engaging target! Range: {distToTarget:F0}m, Angle: {angleOffTarget:F1}°");
                }
            }
        }

        // Send inputs to jet
        pilotingJet.SetAIInput(throttle, pitchInput, rollInput, yawInput, fireGuns);
    }

    void ExecuteJetOnRunway()
    {
        // Start engine and prepare for takeoff
        if (!pilotingJet.IsEngineOn)
        {
            pilotingJet.StartEngine();
            jetAttackTimer = 5f;  // Wait 5 seconds before takeoff
            return;
        }

        jetAttackTimer -= Time.deltaTime;
        if (jetAttackTimer <= 0f)
        {
            jetMissionPhase = JetMissionPhase.Takeoff;
            Debug.Log("[AI JET PILOT] Starting takeoff");
        }

        // Hold still on runway
        pilotingJet.SetAIInput(0.1f, 0f, 0f, 0f);
    }

    // Helper to get current pitch angle of the jet
    float GetJetPitchAngle()
    {
        if (pilotingJet == null) return 0f;
        Vector3 forward = pilotingJet.transform.forward;
        Vector3 forwardFlat = new Vector3(forward.x, 0f, forward.z);
        if (forwardFlat.sqrMagnitude < 0.001f) return 0f;
        forwardFlat.Normalize();
        return Vector3.SignedAngle(forwardFlat, forward, pilotingJet.transform.right);
    }

    void ExecuteJetTakeoff()
    {
        float speed = pilotingJet.CurrentSpeed;
        float altitude = pilotingJet.Altitude;
        float currentPitch = GetJetPitchAngle();

        // Full throttle for takeoff
        float throttle = 1f;
        float pitch = 0f;

        // Target a gentle 10 degree climb once we have speed
        if (speed > pilotingJet.minSpeed * 0.8f)
        {
            float targetPitch = 10f;
            float pitchError = targetPitch - currentPitch;
            pitch = Mathf.Clamp(pitchError * 0.03f, -0.2f, 0.2f);
        }

        // Check if we're airborne
        if (!pilotingJet.IsOnGround && altitude > 20f)
        {
            jetMissionPhase = JetMissionPhase.Climbing;
            jetTargetAltitude = JET_CRUISE_ALTITUDE;
            Debug.Log("[AI JET PILOT] Airborne, climbing to cruise altitude");
        }

        pilotingJet.SetAIInput(throttle, pitch, 0f, 0f);
    }

    void ExecuteJetClimbing()
    {
        float altitude = pilotingJet.Altitude;

        if (altitude >= jetTargetAltitude)
        {
            jetMissionPhase = JetMissionPhase.Patrol;
        }

        pilotingJet.SetAIInput(1f, 0.2f, 0f, 0.5f);
    }

    void ExecuteJetAttackRun()
    {
        if (jetCurrentTarget == null || !jetCurrentTarget.gameObject.activeInHierarchy)
        {
            // Lost target - find new one or patrol
            if (!FindJetAttackTarget())
            {
                jetMissionPhase = JetMissionPhase.Patrol;
                jetPatrolTimer = 20f;
                return;
            }
        }

        Vector3 toTarget = jetCurrentTarget.position - pilotingJet.transform.position;
        float distToTarget = toTarget.magnitude;
        float currentPitch = GetJetPitchAngle();
        float currentRoll = GetJetRollAngle();

        // Fly toward target at attack altitude
        float throttle = 0.9f;
        float desiredYaw = GetJetYawToward(jetCurrentTarget.position);

        // Bank toward target - target roll angle proportional to yaw needed
        float targetRoll = Mathf.Clamp(desiredYaw * 0.5f, -30f, 30f);
        float rollError = targetRoll - currentRoll;
        float roll = Mathf.Clamp(rollError * 0.02f, -0.3f, 0.3f);

        // Maintain altitude during approach using feedback
        float targetPitch = 0f;
        if (pilotingJet.Altitude < JET_ATTACK_ALTITUDE)
            targetPitch = 8f;
        else if (pilotingJet.Altitude > JET_ATTACK_ALTITUDE + 20f)
            targetPitch = -5f;

        float pitchError = targetPitch - currentPitch;
        float pitch = Mathf.Clamp(pitchError * 0.03f, -0.15f, 0.15f);

        // Start dive when close enough and lined up
        if (distToTarget < 400f && Mathf.Abs(desiredYaw) < 30f)
        {
            jetMissionPhase = JetMissionPhase.Diving;
            Debug.Log("[AI JET PILOT] Beginning attack dive");
        }

        pilotingJet.SetAIInput(throttle, pitch, roll, 0f);
    }

    void ExecuteJetDiving()
    {
        if (jetCurrentTarget == null)
        {
            jetMissionPhase = JetMissionPhase.PullUp;
            return;
        }

        Vector3 toTarget = jetCurrentTarget.position - pilotingJet.transform.position;
        float distToTarget = toTarget.magnitude;
        float altitude = pilotingJet.Altitude;
        float currentPitch = GetJetPitchAngle();
        float currentRoll = GetJetRollAngle();

        // Dive toward target
        float throttle = 0.7f;
        float desiredYaw = GetJetYawToward(jetCurrentTarget.position);

        // Target a -15 degree dive
        float targetPitch = -15f;
        float pitchError = targetPitch - currentPitch;
        float pitch = Mathf.Clamp(pitchError * 0.03f, -0.2f, 0.2f);

        // Bank toward target
        float targetRoll = Mathf.Clamp(desiredYaw * 0.3f, -20f, 20f);
        float rollError = targetRoll - currentRoll;
        float roll = Mathf.Clamp(rollError * 0.02f, -0.2f, 0.2f);

        // Fire guns when close and lined up
        bool fireGuns = distToTarget < 300f && Mathf.Abs(desiredYaw) < 15f;

        // Pull up if too low or past target
        if (altitude < 40f || distToTarget < 50f)
        {
            jetMissionPhase = JetMissionPhase.PullUp;
            Debug.Log("[AI JET PILOT] Pull up!");
        }

        pilotingJet.SetAIInput(throttle, pitch, roll, 0f, fireGuns);
    }

    void ExecuteJetPullUp()
    {
        float altitude = pilotingJet.Altitude;
        float currentPitch = GetJetPitchAngle();
        float currentRoll = GetJetRollAngle();

        // Target 20 degree climb
        float targetPitch = 20f;
        float pitchError = targetPitch - currentPitch;
        float pitch = Mathf.Clamp(pitchError * 0.03f, -0.2f, 0.25f);

        // Level wings
        float rollError = 0f - currentRoll;
        float roll = Mathf.Clamp(rollError * 0.02f, -0.2f, 0.2f);

        float throttle = 1f;

        // Once we're climbing and at safe altitude, decide next action
        if (altitude > 80f && pilotingJet.CurrentSpeed > pilotingJet.minSpeed)
        {
            // Look for new target or go back for another pass
            if (FindJetAttackTarget())
            {
                jetMissionPhase = JetMissionPhase.AttackRun;
            }
            else
            {
                jetMissionPhase = JetMissionPhase.Patrol;
                jetPatrolTimer = 30f;
            }
        }

        pilotingJet.SetAIInput(throttle, pitch, roll, 0f);
    }

    // Helper to get current roll angle of the jet
    float GetJetRollAngle()
    {
        if (pilotingJet == null) return 0f;
        return Vector3.SignedAngle(Vector3.up, pilotingJet.transform.up, pilotingJet.transform.forward);
    }

    void ExecuteJetPatrol()
    {
        float altitude = pilotingJet.Altitude;
        float pitch = altitude < JET_PATROL_ALTITUDE ? 0.15f : -0.05f;

        pilotingJet.SetAIInput(0.6f, pitch, 0f, 0.5f);
    }

    void ExecuteJetReturnToBase()
    {
        if (jetHomeRunway == null)
        {
            jetHomeRunway = Runway.FindBestForLanding(
                pilotingJet.transform.position,
                pilotingJet.transform.forward,
                team
            );
        }

        if (jetHomeRunway == null)
        {
            // No runway found - just patrol
            jetMissionPhase = JetMissionPhase.Patrol;
            return;
        }

        Vector3 approachPos = jetHomeRunway.approachPoint.position;
        float distToApproach = Vector3.Distance(pilotingJet.transform.position, approachPos);

        float throttle = 0.8f;
        float desiredYaw = GetJetYawToward(approachPos);
        float roll = Mathf.Clamp(desiredYaw * 0.3f, -0.5f, 0.5f);
        float pitch = 0f;

        // Descend toward approach point
        if (pilotingJet.Altitude > approachPos.y + 20f)
        {
            pitch = -0.1f;
        }

        // Start landing approach when close
        if (distToApproach < 200f && Mathf.Abs(desiredYaw) < 30f)
        {
            jetMissionPhase = JetMissionPhase.Landing;
            Debug.Log("[AI JET PILOT] Beginning landing approach");
        }

        pilotingJet.SetAIInput(throttle, pitch, roll, desiredYaw * 0.1f);
    }

    void ExecuteJetLanding()
    {
        if (jetHomeRunway == null)
        {
            jetMissionPhase = JetMissionPhase.Patrol;
            return;
        }

        Vector3 touchdownPos = jetHomeRunway.touchdownPoint.position;
        float distToTouchdown = Vector3.Distance(pilotingJet.transform.position, touchdownPos);
        float altitude = pilotingJet.Altitude;

        // Reduce throttle for landing
        float throttle = Mathf.Lerp(0.3f, 0.5f, distToTouchdown / 500f);

        // Align with runway
        float desiredYaw = GetJetYawToward(touchdownPos);
        float roll = Mathf.Clamp(desiredYaw * 0.2f, -0.3f, 0.3f);

        // Glide path - descend gradually
        float targetAlt = Mathf.Lerp(touchdownPos.y, altitude, distToTouchdown / 500f);
        float pitch = altitude > targetAlt + 5f ? -0.15f : (altitude < targetAlt - 5f ? 0.1f : 0f);

        // Check if landed
        if (pilotingJet.IsOnGround)
        {
            jetMissionPhase = JetMissionPhase.Landed;
            Debug.Log("[AI JET PILOT] Touchdown!");
        }

        pilotingJet.SetAIInput(throttle, pitch, roll, desiredYaw * 0.1f);
    }

    void ExecuteJetLanded()
    {
        float speed = pilotingJet.CurrentSpeed;

        // Brake on runway
        float throttle = 0f;
        float pitch = 0f;

        // Steer to stay on runway
        float yaw = 0f;
        if (jetHomeRunway != null)
        {
            Vector3 runwayDir = jetHomeRunway.RunwayDirection;
            float alignment = Vector3.SignedAngle(pilotingJet.transform.forward, runwayDir, Vector3.up);
            yaw = Mathf.Clamp(alignment * 0.05f, -0.5f, 0.5f);
        }

        // Once stopped, prepare for next mission
        if (speed < 5f)
        {
            pilotingJet.StopEngine();
            jetAttackTimer = 10f;  // Wait before next mission

            // After waiting, take off again
            if (jetAttackTimer <= 0f)
            {
                jetMissionPhase = JetMissionPhase.OnRunway;
            }
        }

        pilotingJet.SetAIInput(throttle, pitch, 0f, yaw);
    }

    bool FindJetAttackTarget()
    {
        // Look for enemy capture points first
        float bestScore = -1f;
        Transform bestTarget = null;

        foreach (var point in allCapturePoints)
        {
            if (point == null) continue;
            if (point.owningTeam == team) continue;  // Don't attack our own

            float dist = Vector3.Distance(pilotingJet.transform.position, point.transform.position);
            if (dist > 2000f) continue;

            // Score based on distance and tactical value
            float score = 1000f - dist;

            // Prefer contested points
            if (point.isContested) score += 500f;

            // Prefer points being captured by enemy
            if (point.owningTeam != Team.None && point.owningTeam != team) score += 300f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = point.transform;
            }
        }

        // Also look for ground enemies
        foreach (var ai in cachedAIs)
        {
            if (ai == null || ai.team == team || ai.currentState == AIState.Dead) continue;

            float dist = Vector3.Distance(pilotingJet.transform.position, ai.transform.position);
            if (dist > 1000f) continue;

            float score = 800f - dist;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = ai.transform;
            }
        }

        jetCurrentTarget = bestTarget;
        return bestTarget != null;
    }

    bool FindJetAirTarget()
    {
        // Look for enemy helicopters or other jets
        HelicopterController[] helis = FindObjectsOfType<HelicopterController>();

        foreach (var heli in helis)
        {
            if (heli == null || heli.isDestroyed) continue;
            if (heli.helicopterTeam == team) continue;  // Don't attack friendly

            float dist = Vector3.Distance(pilotingJet.transform.position, heli.transform.position);
            if (dist < 800f)
            {
                jetCurrentTarget = heli.transform;
                Debug.Log($"[AI JET PILOT] Air target acquired: {heli.name}");
                return true;
            }
        }

        // TODO: Check for enemy jets too

        return false;
    }

    float GetJetYawToward(Vector3 targetPos)
    {
        Vector3 toTarget = targetPos - pilotingJet.transform.position;
        toTarget.y = 0;  // Ignore vertical for yaw calculation

        float angle = Vector3.SignedAngle(pilotingJet.transform.forward, toTarget, Vector3.up);
        return angle;
    }

    // Helper: Find a clear landing zone near a target position
    Vector3 FindClearLandingZone(Vector3 targetPosition, float minDistance, float maxDistance)
    {
        // FIRST: Check for designated landing zones placed in the scene
        LandingZone zone = LandingZone.FindNearestToObjective(targetPosition, team, minDistance, maxDistance * 1.5f);
        if (zone != null)
        {
            zone.SetOccupied();
            currentLandingZone = zone;
            Debug.Log($"[AI PILOT] Using designated landing zone at {zone.LandingPosition}");
            return zone.LandingPosition;
        }

        // FALLBACK: Try multiple positions around the target if no landing zones available
        int attempts = 16;
        float bestScore = -1f;
        Vector3 bestPosition = targetPosition;

        for (int i = 0; i < attempts; i++)
        {
            // Generate a position at varying distances and angles
            float angle = (i / (float)attempts) * 360f * Mathf.Deg2Rad;
            float distance = Mathf.Lerp(minDistance, maxDistance, (i % 4) / 3f);

            Vector3 testPos = targetPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                80f, // Start high for raycast
                Mathf.Sin(angle) * distance
            );

            // Raycast down to find ground - use RaycastAll to find actual terrain, not rooftops
            RaycastHit[] hits = Physics.RaycastAll(testPos, Vector3.down, 150f);
            if (hits.Length > 0)
            {
                // Sort by distance (closest first)
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                // Find the best hit - prefer terrain over structures
                RaycastHit bestHit = hits[0];
                foreach (var hit in hits)
                {
                    bool isTerrain = hit.collider.gameObject.layer == LayerMask.NameToLayer("Terrain") ||
                                     hit.collider.GetComponent<Terrain>() != null ||
                                     hit.collider.gameObject.name.ToLower().Contains("terrain") ||
                                     hit.collider.gameObject.name.ToLower().Contains("ground");

                    if (isTerrain)
                    {
                        bestHit = hit;
                        break; // Use terrain if found
                    }
                }

                // Check if this is a valid landing spot
                float score = EvaluateLandingSpot(bestHit.point, bestHit.normal);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = bestHit.point;
                }
            }
        }

        Debug.Log($"[AI PILOT] Found landing zone at {bestPosition}, score: {bestScore}");
        return bestPosition;
    }

    float EvaluateLandingSpot(Vector3 position, Vector3 groundNormal)
    {
        float score = 100f;

        // CRITICAL: Check if this is a rooftop - raycast down to see if there's ground below
        RaycastHit roofCheck;
        if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out roofCheck, 100f))
        {
            // Check if what we hit is NOT terrain (i.e., it's a building/structure)
            bool isOnTerrain = roofCheck.collider.gameObject.layer == LayerMask.NameToLayer("Terrain") ||
                               roofCheck.collider.GetComponent<Terrain>() != null ||
                               roofCheck.collider.gameObject.name.ToLower().Contains("terrain") ||
                               roofCheck.collider.gameObject.name.ToLower().Contains("ground");

            if (!isOnTerrain)
            {
                // We're on a structure - check if there's more ground below
                Vector3 belowStructure = roofCheck.point + Vector3.down * 0.5f;
                RaycastHit groundBelow;
                if (Physics.Raycast(belowStructure, Vector3.down, out groundBelow, 50f))
                {
                    // There's ground below this structure - we're on a rooftop!
                    float roofHeight = position.y - groundBelow.point.y;
                    if (roofHeight > 3f)
                    {
                        // Severe penalty for rooftops - we don't want to land here
                        score -= 200f;
                        Debug.Log($"[LANDING] Rejecting rooftop at {position}, height above ground: {roofHeight}m");
                    }
                }
            }
        }

        // Penalize steep slopes (normal should be close to up)
        float slopePenalty = (1f - Vector3.Dot(groundNormal, Vector3.up)) * 50f;
        score -= slopePenalty;

        // Check for obstructions above (buildings, trees)
        RaycastHit upHit;
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.up, out upHit, 20f))
        {
            // Something above - bad landing spot
            score -= 80f;
        }

        // Check clearance in a radius around the landing spot
        float checkRadius = 8f;
        Collider[] obstacles = Physics.OverlapSphere(position + Vector3.up * 3f, checkRadius);
        foreach (var col in obstacles)
        {
            // Ignore terrain and triggers
            if (col.isTrigger) continue;
            if (col.gameObject.layer == LayerMask.NameToLayer("Terrain")) continue;

            // Check if it's a building or large obstacle
            if (col.bounds.size.y > 3f)
            {
                score -= 30f;
            }
        }

        // CRITICAL: Check for enemies near landing spot - avoid hot zones!
        float enemyCheckRadius = 40f;
        int enemiesNearby = CountEnemiesNearPosition(position, enemyCheckRadius);
        int friendliesNearby = CountFriendliesNearPosition(position, enemyCheckRadius);

        // Heavy penalty for enemies - don't land in the middle of a firefight
        score -= enemiesNearby * 25f;

        // Bonus for friendly presence (safer landing)
        score += friendliesNearby * 10f;

        // If more enemies than friendlies, very dangerous
        if (enemiesNearby > friendliesNearby)
        {
            score -= 40f;
        }

        // Prefer flat ground (check multiple points)
        Vector3[] checkPoints = {
            position + Vector3.forward * 5f,
            position + Vector3.back * 5f,
            position + Vector3.left * 5f,
            position + Vector3.right * 5f
        };

        foreach (var checkPoint in checkPoints)
        {
            RaycastHit groundHit;
            if (Physics.Raycast(checkPoint + Vector3.up * 10f, Vector3.down, out groundHit, 20f))
            {
                float heightDiff = Mathf.Abs(groundHit.point.y - position.y);
                if (heightDiff > 2f)
                {
                    score -= heightDiff * 5f; // Penalize uneven terrain
                }
            }
        }

        return Mathf.Max(0f, score);
    }

    // Helper: Count enemies near a position
    int CountEnemiesNearPosition(Vector3 position, float radius)
    {
        int count = 0;

        // Check enemy AI
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this) continue;
                if (ai.team == team) continue;  // Same team = not enemy
                if (ai.currentState == AIState.Dead) continue;
                if (ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliGunner || ai.currentState == AIState.HeliPassenger) continue;

                float dist = Vector3.Distance(ai.transform.position, position);
                if (dist < radius)
                {
                    count++;
                }
            }
        }

        // Check enemy players
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null) continue;
                if (player.playerTeam == team) continue;  // Same team = not enemy
                if (player.isDead) continue;

                float dist = Vector3.Distance(player.transform.position, position);
                if (dist < radius)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // Helper: Fly toward target position
    void HeliFlyToTarget()
    {
        Vector3 heliPos = pilotingHelicopter.transform.position;
        float collective = CalculateHeliCollective(heliPos.y);

        Vector3 toTarget = heliTargetPosition - heliPos;
        toTarget.y = 0;

        float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float currentYaw = pilotingHelicopter.transform.eulerAngles.y;
        float yawDiff = Mathf.DeltaAngle(currentYaw, desiredYaw);
        float yawInput = Mathf.Clamp(yawDiff / 45f, -1f, 1f);

        float forward = 0f;
        float strafe = 0f;

        if (Mathf.Abs(yawDiff) < 20f)
            forward = 0.8f;
        else if (Mathf.Abs(yawDiff) < 45f)
            forward = 0.4f;

        // Obstacle avoidance - raycast in multiple directions
        float avoidDistance = 25f;
        float obstacleCollective = 0f;

        // Check forward
        Vector3 forwardDir = pilotingHelicopter.transform.forward;
        if (Physics.Raycast(heliPos, forwardDir, out RaycastHit forwardHit, avoidDistance))
        {
            // Obstacle ahead - slow down and climb
            forward *= 0.3f;
            obstacleCollective = 0.5f;
        }

        // Check forward-left and forward-right for better avoidance
        Vector3 forwardLeft = Quaternion.Euler(0, -30f, 0) * forwardDir;
        Vector3 forwardRight = Quaternion.Euler(0, 30f, 0) * forwardDir;

        bool obstacleLeft = Physics.Raycast(heliPos, forwardLeft, avoidDistance * 0.8f);
        bool obstacleRight = Physics.Raycast(heliPos, forwardRight, avoidDistance * 0.8f);

        if (obstacleLeft && !obstacleRight)
        {
            // Obstacle on left - strafe right
            strafe = 0.5f;
            yawInput += 0.3f;
        }
        else if (obstacleRight && !obstacleLeft)
        {
            // Obstacle on right - strafe left
            strafe = -0.5f;
            yawInput -= 0.3f;
        }
        else if (obstacleLeft && obstacleRight)
        {
            // Obstacles on both sides - climb
            obstacleCollective = 0.7f;
            forward *= 0.2f;
        }

        // Check if stuck (low velocity while trying to move)
        if (pilotingHelicopter.GetComponent<Rigidbody>() != null)
        {
            float velocity = pilotingHelicopter.GetComponent<Rigidbody>().linearVelocity.magnitude;
            if (velocity < 2f && forward > 0.3f)
            {
                // Stuck - climb aggressively
                obstacleCollective = 0.8f;
                heliStuckTimer += Time.deltaTime;

                if (heliStuckTimer > 2f)
                {
                    // Been stuck for a while - try strafing
                    strafe = (Time.time % 2f < 1f) ? 0.6f : -0.6f;
                }
            }
            else
            {
                heliStuckTimer = 0f;
            }
        }

        // Apply obstacle avoidance collective boost
        if (obstacleCollective > 0f)
        {
            collective = Mathf.Max(collective, obstacleCollective);
        }

        pilotingHelicopter.SetAIInput(collective, new Vector2(strafe, forward), yawInput);
    }

    // Helper: Descend to landing
    void HeliDescendToLanding()
    {
        Vector3 heliPos = pilotingHelicopter.transform.position;

        // Slow descent
        float collective = -0.3f;

        // Move toward landing spot
        Vector3 toTarget = heliTargetPosition - heliPos;
        toTarget.y = 0;
        float dist = toTarget.magnitude;

        float yawInput = 0f;
        float forward = 0f;

        if (dist > 10f)
        {
            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float currentYaw = pilotingHelicopter.transform.eulerAngles.y;
            float yawDiff = Mathf.DeltaAngle(currentYaw, desiredYaw);
            yawInput = Mathf.Clamp(yawDiff / 45f, -1f, 1f);

            if (Mathf.Abs(yawDiff) < 30f)
                forward = 0.3f;
        }

        pilotingHelicopter.SetAIInput(collective, new Vector2(0f, forward), yawInput);
    }

    // Helper: Hover in place
    void HeliHover()
    {
        float collective = CalculateHeliCollective(pilotingHelicopter.transform.position.y);
        pilotingHelicopter.SetAIInput(collective, Vector2.zero, 0f);
    }

    // Helper: Get helicopter's actual height above ground (not absolute Y position)
    float GetHeliHeightAboveGround()
    {
        if (pilotingHelicopter == null) return 0f;

        Vector3 heliPos = pilotingHelicopter.transform.position;

        // Raycast straight down from the helicopter
        RaycastHit hit;
        if (Physics.Raycast(heliPos, Vector3.down, out hit, 500f))
        {
            return hit.distance;
        }

        // No ground found - return large value
        return 500f;
    }

    // Helper: Calculate collective for altitude
    float CalculateHeliCollective(float currentAltitude)
    {
        // Use height above ground instead of absolute Y position
        float heightAboveGround = GetHeliHeightAboveGround();
        float altitudeError = heliTargetAltitude - heightAboveGround;

        float collective;
        if (altitudeError > 5f)
        {
            // Need to climb significantly - strong upward thrust
            collective = Mathf.Min(altitudeError * 0.1f, 0.7f);
        }
        else if (altitudeError > 0)
        {
            collective = Mathf.Min(altitudeError * 0.08f, 0.5f);
        }
        else
        {
            collective = Mathf.Max(altitudeError * 0.12f, -0.6f);
        }

        // Only hover (collective=0) when very close to target altitude
        if (Mathf.Abs(altitudeError) < 1.5f)
            collective = 0f;

        return collective;
    }

    // Try to board a nearby landed helicopter as passenger/gunner
    void TryBoardNearbyHelicopter()
    {
        if (currentState == AIState.Dead) return;
        if (currentState == AIState.HeliPilot || currentState == AIState.HeliGunner || currentState == AIState.HeliPassenger) return;
        if (currentState == AIState.BoardingHelicopter) return;  // Already boarding
        if (currentHelicopter != null) return;

        HelicopterController[] helicopters = FindObjectsOfType<HelicopterController>();
        HelicopterController bestHeli = null;
        HelicopterSeat bestSeat = null;
        float closestDist = HELI_BOARDING_RANGE;

        foreach (var heli in helicopters)
        {
            if (heli == null || heli.isDestroyed) continue;

            float dist = Vector3.Distance(transform.position, heli.transform.position);

            // Debug: Log why helicopters are being skipped
            if (dist < 20f)
            {
                if (heli.helicopterTeam != Team.None && heli.helicopterTeam != team)
                {
                    Debug.Log($"[AI BOARDING DEBUG] {gameObject.name}: {heli.name} is enemy team");
                    continue;
                }

                if (!heli.IsWaitingForPassengers)
                {
                    Debug.Log($"[AI BOARDING DEBUG] {gameObject.name}: {heli.name} not waiting (grounded={heli.IsLanded}, engineOn={heli.engineOn})");
                    continue;
                }
            }
            else
            {
                // Must be friendly helicopter
                if (heli.helicopterTeam != Team.None && heli.helicopterTeam != team) continue;

                // Must be landed (waiting for passengers)
                if (!heli.IsWaitingForPassengers) continue;
            }

            if (dist > closestDist) continue;

            // Find available seat (prefer gunner seats)
            HelicopterSeat availableSeat = null;
            foreach (var seat in heli.seats)
            {
                if (seat == null || seat.IsOccupied) continue;
                if (seat.seatType == SeatType.Pilot) continue;  // Don't auto-board as pilot

                // Prefer gunner seats
                bool isGunnerSeat = seat.seatType == SeatType.DoorGunnerLeft || seat.seatType == SeatType.DoorGunnerRight;
                if (availableSeat == null || isGunnerSeat)
                {
                    availableSeat = seat;
                }
            }

            // Check if we found a physical seat or if virtual passenger space is available
            bool hasSpace = availableSeat != null || heli.HasVirtualPassengerSpace();

            if (hasSpace && dist < closestDist)
            {
                closestDist = dist;
                bestHeli = heli;
                bestSeat = availableSeat;  // May be null if using virtual passenger
            }
        }

        if (bestHeli != null)
        {
            // Move toward helicopter first if too far
            float finalDist = Vector3.Distance(transform.position, bestHeli.transform.position);
            if (finalDist > 10f)
            {
                // Set dedicated boarding state so we don't get distracted
                targetBoardingHelicopter = bestHeli;
                boardingTimeout = MAX_BOARDING_TIME;  // Reset timeout
                targetPoint = null;
                targetPosition = bestHeli.transform.position;
                currentState = AIState.BoardingHelicopter;

                // Sprint toward helicopter
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.speed = moveSpeed * 1.5f;  // Run faster to the helicopter
                    agent.SetDestination(bestHeli.transform.position);
                }

                Debug.Log($"[AI BOARDING] {gameObject.name} entering BoardingHelicopter state toward {bestHeli.name} (dist={finalDist:F1}m)");
                return;
            }

            // Close enough - board the helicopter
            if (bestSeat != null)
            {
                // Physical seat available
                bool isGunnerSeat = bestSeat.seatType == SeatType.DoorGunnerLeft || bestSeat.seatType == SeatType.DoorGunnerRight;
                Debug.Log($"[AI BOARDING] {gameObject.name} boarding {bestHeli.name} physical seat {bestSeat.seatType}");
                if (isGunnerSeat)
                {
                    EnterHelicopterAsGunner(bestHeli, bestSeat);
                }
                else
                {
                    EnterHelicopterAsPassenger(bestHeli, bestSeat);
                }
            }
            else
            {
                // No physical seat - use virtual passenger slot
                Debug.Log($"[AI BOARDING] {gameObject.name} trying to board {bestHeli.name} as virtual passenger (space available: {bestHeli.HasVirtualPassengerSpace()}, current count: {bestHeli.GetVirtualPassengerCount()})");
                EnterHelicopterAsVirtualPassenger(bestHeli);

                // Verify boarding succeeded
                if (currentState == AIState.HeliPassenger)
                {
                    Debug.Log($"[AI BOARDING] {gameObject.name} successfully boarded as virtual passenger!");
                }
                else
                {
                    Debug.LogWarning($"[AI BOARDING] {gameObject.name} FAILED to board as virtual passenger!");
                }
            }
        }
    }

    // Find and enter a nearby helicopter as pilot
    public void TryFindAndPilotHelicopter()
    {
        if (currentState != AIState.Idle && currentState != AIState.MovingToPoint) return;

        HelicopterController[] helicopters = FindObjectsOfType<HelicopterController>();
        float closestDist = 30f;
        HelicopterController closestHeli = null;

        foreach (var heli in helicopters)
        {
            if (heli == null || heli.isDestroyed) continue;
            if (heli.HasAIPilot) continue; // Already has AI pilot

            // Check team
            if (heli.helicopterTeam != Team.None && heli.helicopterTeam != team) continue;

            // Check if has player pilot
            // We can't easily check this, so skip if any seat is occupied by player
            bool hasPlayerPilot = false;
            foreach (var seat in heli.seats)
            {
                if (seat != null && seat.seatType == SeatType.Pilot && seat.occupant != null)
                {
                    hasPlayerPilot = true;
                    break;
                }
            }
            if (hasPlayerPilot) continue;

            float dist = Vector3.Distance(transform.position, heli.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestHeli = heli;
            }
        }

        if (closestHeli != null)
        {
            EnterHelicopterAsPilot(closestHeli);
        }
    }

    // ==================== TANK DRIVER METHODS ====================

    // Called by TankSpawner to assign this AI as a dedicated tank driver
    public void AssignAsTankDriver(TankController tank)
    {
        if (tank == null) return;

        isDedicatedTankDriver = true;
        assignedTank = tank;
        tankPatrolCenter = tank.transform.position;

        // Enter the tank immediately
        EnterTankAsDriver(tank);
    }

    public void EnterTankAsDriver(TankController tank)
    {
        if (tank == null) return;
        if (currentState == AIState.Dead || currentState == AIState.TankDriver) return;
        if (tank.HasDriver) return;

        Debug.Log($"[Tank AI] {name} entering tank {tank.name} as driver at position {tank.transform.position}");

        drivingTank = tank;
        currentState = AIState.TankDriver;
        tankMissionPhase = TankMissionPhase.Idle;
        tankPatrolCenter = tank.transform.position;

        // Initialize NavMesh path for tank navigation
        tankNavPath = new NavMeshPath();
        tankPathCorners = new Vector3[0];
        tankCurrentWaypoint = 0;
        tankLastDestination = Vector3.zero;

        // Disable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = false;
        }

        // Tell tank we're the driver
        tank.SetAIDriver(this);

        // Hide the AI model (we're inside the tank)
        SetModelVisible(false);

        // Disable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Parent to tank
        transform.SetParent(tank.transform);
        if (tank.driverSeat != null)
            transform.localPosition = tank.driverSeat.localPosition;
        else
            transform.localPosition = Vector3.up * 2f;

        Debug.Log($"[AI TANK] {gameObject.name} entered tank as driver");
    }

    public void ExitTankAsDriver()
    {
        if (drivingTank == null) return;

        TankController tank = drivingTank;

        // Clear tank reference first
        tank.ClearAIDriver();
        drivingTank = null;

        // Unparent
        transform.SetParent(null);

        // Find exit position
        Vector3 exitPos = tank.transform.position + tank.transform.right * 5f;
        exitPos.y = tank.transform.position.y + 2f;

        // Raycast to find ground
        if (Physics.Raycast(exitPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f))
        {
            exitPos = hit.point + Vector3.up * 0.5f;
        }

        transform.position = exitPos;

        // Re-enable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Show model
        SetModelVisible(true);

        // Re-enable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = true;
            agent.Warp(transform.position);
        }

        currentState = AIState.Idle;
        isDedicatedTankDriver = false;
        assignedTank = null;

        Debug.Log($"[AI TANK] {gameObject.name} exited tank");
    }

    void UpdateTankDriverBehavior()
    {
        // Check if tank is destroyed
        if (drivingTank == null || drivingTank.isDestroyed)
        {
            ExitTankAsDriver();
            return;
        }

        // Keep AI at tank position
        transform.position = drivingTank.transform.position;

        // Update cooldowns
        tankFireCooldown -= Time.deltaTime;
        tankEngageTimer -= Time.deltaTime;
        tankDecisionTimer -= Time.deltaTime;

        // Update awareness systems
        UpdateTankTurretScanning();
        UpdateTankFlankAwareness();
        UpdateTankSoundDetection();
        UpdateTankTerrainCheck();

        // Check for flank threats - if detected, prioritize them
        if (tankFlankThreat != null && tankCurrentTarget == null)
        {
            tankCurrentTarget = tankFlankThreat;
            tankMissionPhase = TankMissionPhase.Engaging;
            tankEngageTimer = 3f;
            Debug.Log($"[Tank AI] Flank threat detected: {tankFlankThreat.name}");
        }

        // Check shared contacts if we have no target
        if (tankCurrentTarget == null)
        {
            Transform sharedContact = TankGetSharedContact();
            if (sharedContact != null)
            {
                float dist = Vector3.Distance(drivingTank.transform.position, sharedContact.position);
                if (dist < TANK_ENGAGE_RANGE)
                {
                    tankCurrentTarget = sharedContact;
                    tankMissionPhase = TankMissionPhase.Engaging;
                    Debug.Log($"[Tank AI] Engaging shared contact: {sharedContact.name}");
                }
            }
        }

        // React to heard combat if idle
        if (TankHeardCombatNearby() && tankMissionPhase == TankMissionPhase.Idle)
        {
            tankTargetPosition = tankLastHeardCombat;
            tankMissionPhase = TankMissionPhase.SupportingCombat;
            Debug.Log("[Tank AI] Heard combat, moving to investigate");
        }

        // Debug log
        if (Time.frameCount % 120 == 0)
        {
            string followName = tankFollowTarget != null ? tankFollowTarget.name : "none";
            string objName = tankObjective != null ? tankObjective.name : "none";
            Debug.Log($"[Tank AI] Phase: {tankMissionPhase}, Following: {followName}, Objective: {objName}");
        }

        // Always look for enemies first
        Transform enemy = FindTankTarget();
        if (enemy != null)
        {
            tankCurrentTarget = enemy;
            if (tankMissionPhase != TankMissionPhase.Engaging)
            {
                tankMissionPhase = TankMissionPhase.Engaging;
            }
            tankEngageTimer = 5f;
        }
        else if (tankEngageTimer <= 0f && tankMissionPhase == TankMissionPhase.Engaging)
        {
            tankCurrentTarget = null;
            // Return to previous behavior
            MakeTankTacticalDecision();
        }

        // Make tactical decisions periodically
        if (tankDecisionTimer <= 0f && tankMissionPhase != TankMissionPhase.Engaging)
        {
            MakeTankTacticalDecision();
            tankDecisionTimer = 3f; // Decide every 3 seconds
        }

        // Execute behavior based on mission phase
        switch (tankMissionPhase)
        {
            case TankMissionPhase.Idle:
                MakeTankTacticalDecision();
                break;

            case TankMissionPhase.FollowingInfantry:
                UpdateTankFollowInfantry();
                break;

            case TankMissionPhase.MovingToObjective:
                UpdateTankMoveToObjective();
                break;

            case TankMissionPhase.Engaging:
                UpdateTankEngage();
                break;

            case TankMissionPhase.SupportingCombat:
                UpdateTankSupportCombat();
                break;

            case TankMissionPhase.Retreating:
                UpdateTankRetreat();
                break;
        }
    }

    void MakeTankTacticalDecision()
    {
        if (drivingTank == null) return;

        Vector3 tankPos = drivingTank.transform.position;
        Debug.Log($"[Tank AI] Making tactical decision at {tankPos}");

        // Priority 1: Move to contested or enemy objective - ATTACK!
        CapturePoint bestObjective = FindTankObjective();
        if (bestObjective != null)
        {
            tankObjective = bestObjective;
            tankMissionPhase = TankMissionPhase.MovingToObjective;
            Debug.Log($"[Tank AI] Decision: Attacking objective {bestObjective.name}");
            return;
        }

        // Priority 2: Hunt enemies - find any enemy-held objectives
        CapturePoint enemyPoint = FindEnemyHeldObjective();
        if (enemyPoint != null)
        {
            tankObjective = enemyPoint;
            tankMissionPhase = TankMissionPhase.MovingToObjective;
            Debug.Log($"[Tank AI] Decision: Hunting enemy at {enemyPoint.name}");
            return;
        }

        // Priority 3: Support combat only if very close (within 30m)
        Transform combatLocation = FindFriendlyCombat();
        if (combatLocation != null)
        {
            float distToCombat = Vector3.Distance(tankPos, combatLocation.position);
            if (distToCombat < 30f)
            {
                tankTargetPosition = combatLocation.position;
                tankMissionPhase = TankMissionPhase.SupportingCombat;
                Debug.Log($"[Tank AI] Decision: Joining nearby combat at {combatLocation.name}");
                return;
            }
        }

        // Priority 4: Patrol to random UNOWNED or CONTESTED objective
        if (allCapturePoints != null && allCapturePoints.Count > 0)
        {
            // Build list of valid patrol targets (not owned by us, or contested)
            var validTargets = new System.Collections.Generic.List<CapturePoint>();
            foreach (var point in allCapturePoints)
            {
                if (point == null) continue;
                // Skip our owned points unless contested
                if (point.owningTeam == team && !point.isContested) continue;
                validTargets.Add(point);
            }

            if (validTargets.Count > 0)
            {
                CapturePoint randomPoint = validTargets[Random.Range(0, validTargets.Count)];
                tankObjective = randomPoint;
                tankMissionPhase = TankMissionPhase.MovingToObjective;
                Debug.Log($"[Tank AI] Decision: Patrolling to {randomPoint.name}");
                return;
            }
        }

        Debug.Log("[Tank AI] Decision: All objectives owned - holding position");
    }

    CapturePoint FindEnemyHeldObjective()
    {
        if (allCapturePoints == null) return null;

        CapturePoint nearest = null;
        float nearestDist = float.MaxValue;
        Vector3 tankPos = drivingTank.transform.position;

        foreach (var point in allCapturePoints)
        {
            if (point == null) continue;

            // Only enemy-held points
            if (point.owningTeam == team || point.owningTeam == Team.None) continue;

            float dist = Vector3.Distance(tankPos, point.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = point;
            }
        }

        return nearest;
    }

    Transform FindFriendlyCombat()
    {
        // Find friendly AI that are in combat - prioritize nearest combat
        if (cachedAIs == null) return null;

        Vector3 tankPos = drivingTank.transform.position;
        Transform nearestCombat = null;
        float nearestDist = float.MaxValue;

        foreach (var ai in cachedAIs)
        {
            if (ai == null || ai == this || ai.isDead) continue;
            if (ai.team != team) continue;
            if (ai.currentState != AIState.Combat) continue;

            float dist = Vector3.Distance(tankPos, ai.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestCombat = ai.transform;
            }
        }

        return nearestCombat;
    }

    FPSControllerPhoton FindNearestFriendlyPlayer()
    {
        if (cachedPlayers == null) return null;

        FPSControllerPhoton nearest = null;
        float nearestDist = float.MaxValue;
        Vector3 tankPos = drivingTank.transform.position;

        foreach (var player in cachedPlayers)
        {
            if (player == null || player.isDead) continue;
            if (player.playerTeam != team) continue;

            float dist = Vector3.Distance(tankPos, player.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = player;
            }
        }

        return nearest;
    }

    Transform FindFriendlyInfantryGroup()
    {
        // Find a group of friendly infantry (3+ together)
        if (cachedAIs == null) return null;

        Vector3 tankPos = drivingTank.transform.position;
        Transform bestGroup = null;
        int bestCount = 0;

        foreach (var ai in cachedAIs)
        {
            if (ai == null || ai == this || ai.isDead) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.TankDriver || ai.currentState == AIState.TankPassenger) continue;
            if (ai.currentState == AIState.HeliPilot || ai.currentState == AIState.HeliGunner) continue;

            // Count friendlies near this AI
            int nearbyCount = 0;
            foreach (var other in cachedAIs)
            {
                if (other == null || other == ai || other.isDead) continue;
                if (other.team != team) continue;
                if (other.currentState == AIState.TankDriver || other.currentState == AIState.HeliPilot) continue;

                float d = Vector3.Distance(ai.transform.position, other.transform.position);
                if (d < 20f) nearbyCount++;
            }

            // Prefer larger groups, then closer groups
            if (nearbyCount >= 2 && nearbyCount > bestCount)
            {
                bestCount = nearbyCount;
                bestGroup = ai.transform;
            }
        }

        return bestGroup;
    }

    CapturePoint FindTankObjective()
    {
        // Find a contested or enemy capture point to attack
        CapturePoint best = null;
        float bestScore = float.MaxValue;
        Vector3 tankPos = drivingTank.transform.position;

        foreach (var point in allCapturePoints)
        {
            if (point == null) continue;

            float dist = Vector3.Distance(tankPos, point.transform.position);
            float score = dist;

            // Prefer contested points
            if (point.isContested)
            {
                score *= 0.5f;
            }
            // Then enemy points
            else if (point.owningTeam != team && point.owningTeam != Team.None)
            {
                score *= 0.7f;
            }
            // Skip our own points unless contested
            else if (point.owningTeam == team && !point.isContested)
            {
                continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        return best;
    }

    Transform FindAnyNearbyFriendly()
    {
        if (cachedAIs == null) return null;

        Vector3 tankPos = drivingTank.transform.position;
        Transform nearest = null;
        float nearestDist = float.MaxValue;  // No distance limit

        foreach (var ai in cachedAIs)
        {
            if (ai == null || ai == this || ai.isDead) continue;
            if (ai.team != team) continue;
            if (ai.currentState == AIState.TankDriver || ai.currentState == AIState.HeliPilot) continue;

            float dist = Vector3.Distance(tankPos, ai.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = ai.transform;
            }
        }

        return nearest;
    }

    void UpdateTankFollowInfantry()
    {
        if (drivingTank == null) return;

        // Check if follow target is still valid
        if (tankFollowTarget == null)
        {
            MakeTankTacticalDecision();
            return;
        }

        // Check if target died
        AIController followAI = tankFollowTarget.GetComponent<AIController>();
        FPSControllerPhoton followPlayer = tankFollowTarget.GetComponent<FPSControllerPhoton>();
        if ((followAI != null && followAI.isDead) || (followPlayer != null && followPlayer.isDead))
        {
            tankFollowTarget = null;
            MakeTankTacticalDecision();
            return;
        }

        Vector3 tankPos = drivingTank.transform.position;
        Vector3 targetPos = tankFollowTarget.position;
        float dist = Vector3.Distance(tankPos, targetPos);

        // Stay behind the infantry at follow distance
        if (dist > TANK_FOLLOW_DISTANCE + 5f)
        {
            // Move toward them
            Vector3 followPos = targetPos - (targetPos - tankPos).normalized * TANK_FOLLOW_DISTANCE;
            DriveTankToward(followPos);
        }
        else if (dist < TANK_FOLLOW_DISTANCE - 5f)
        {
            // Too close, stop or back up slightly
            drivingTank.SetAIInput(0f, 0f, 0f, false);
        }
        else
        {
            // Good distance, match their movement direction
            Vector3 followDir = tankFollowTarget.forward;
            Vector3 targetPoint = tankPos + followDir * 10f;
            DriveTankToward(targetPoint);
        }
    }

    void UpdateTankMoveToObjective()
    {
        if (drivingTank == null) return;

        if (tankObjective == null)
        {
            MakeTankTacticalDecision();
            return;
        }

        Vector3 tankPos = drivingTank.transform.position;
        float dist = Vector3.Distance(tankPos, tankObjective.transform.position);

        // Look for enemies and shoot while moving!
        Transform enemy = FindTankTarget();
        if (enemy != null)
        {
            // Found enemy - switch to engage mode
            tankCurrentTarget = enemy;
            tankMissionPhase = TankMissionPhase.Engaging;
            tankEngageTimer = 5f;
            return;
        }

        // Arrived at objective
        if (dist < 30f)
        {
            drivingTank.SetAIInput(0f, 0f, 0f, false);

            if (tankObjective.owningTeam == team && !tankObjective.isContested)
            {
                tankObjective = null;
                MakeTankTacticalDecision();
            }
        }
        else
        {
            DriveTankToward(tankObjective.transform.position);
        }
    }

    void UpdateTankSupportCombat()
    {
        if (drivingTank == null) return;

        // Look for enemies to shoot!
        Transform enemy = FindTankTarget();
        if (enemy != null)
        {
            tankCurrentTarget = enemy;
            tankMissionPhase = TankMissionPhase.Engaging;
            tankEngageTimer = 5f;
            return;
        }

        Vector3 tankPos = drivingTank.transform.position;
        float dist = Vector3.Distance(tankPos, tankTargetPosition);

        if (dist > TANK_SUPPORT_RANGE)
        {
            DriveTankToward(tankTargetPosition);
        }
        else
        {
            drivingTank.SetAIInput(0f, 0f, 0f, false);
            Transform combat = FindFriendlyCombat();
            if (combat == null)
            {
                MakeTankTacticalDecision();
            }
        }
    }

    Transform FindTankTarget()
    {
        Transform bestTarget = null;
        float bestScore = float.MaxValue;
        Vector3 tankPos = drivingTank.transform.position;

        // Check for enemy players
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null || player.isDead) continue;
                if (player.playerTeam == team) continue; // Same team

                float dist = Vector3.Distance(tankPos, player.transform.position);
                if (dist < TANK_ENGAGE_RANGE)
                {
                    // Prioritize closer targets
                    if (dist < bestScore)
                    {
                        bestScore = dist;
                        bestTarget = player.transform;
                    }
                }
            }
        }

        // Check for enemy AI
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.team == team) continue;
                if (ai.currentState == AIState.TankDriver) continue; // Don't target drivers (target their tank)

                float dist = Vector3.Distance(tankPos, ai.transform.position);
                if (dist < TANK_ENGAGE_RANGE)
                {
                    if (dist < bestScore)
                    {
                        bestScore = dist;
                        bestTarget = ai.transform;
                    }
                }
            }
        }

        // Check for enemy tanks
        TankController[] tanks = FindObjectsOfType<TankController>();
        foreach (var tank in tanks)
        {
            if (tank == null || tank == drivingTank || tank.isDestroyed) continue;
            if (tank.TankTeam == team) continue;

            float dist = Vector3.Distance(tankPos, tank.transform.position);
            if (dist < TANK_ENGAGE_RANGE)
            {
                // Tanks are high priority targets - reduce score
                float score = dist * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = tank.transform;
                }
            }
        }

        // Check for enemy helicopters
        HelicopterController[] helis = FindObjectsOfType<HelicopterController>();
        foreach (var heli in helis)
        {
            if (heli == null || heli.isDestroyed) continue;
            if (heli.helicopterTeam == team) continue;

            float dist = Vector3.Distance(tankPos, heli.transform.position);
            // Only engage helicopters that are low and close
            if (dist < TANK_ENGAGE_RANGE && heli.transform.position.y - tankPos.y < 30f)
            {
                float score = dist * 0.7f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = heli.transform;
                }
            }
        }

        return bestTarget;
    }


    void UpdateTankEngage()
    {
        if (drivingTank == null) return;

        if (tankCurrentTarget == null)
        {
            MakeTankTacticalDecision();
            return;
        }

        // Check if target is still valid
        bool targetValid = true;
        FPSControllerPhoton targetPlayer = tankCurrentTarget.GetComponent<FPSControllerPhoton>();
        AIController targetAI = tankCurrentTarget.GetComponent<AIController>();
        TankController targetTank = tankCurrentTarget.GetComponent<TankController>();
        HelicopterController targetHeli = tankCurrentTarget.GetComponent<HelicopterController>();

        if (targetPlayer != null && targetPlayer.isDead) targetValid = false;
        if (targetAI != null && targetAI.isDead) targetValid = false;
        if (targetTank != null && targetTank.isDestroyed) targetValid = false;
        if (targetHeli != null && targetHeli.isDestroyed) targetValid = false;

        if (!targetValid)
        {
            tankCurrentTarget = null;
            MakeTankTacticalDecision();
            return;
        }

        Vector3 tankPos = drivingTank.transform.position;
        float dist = Vector3.Distance(tankPos, tankCurrentTarget.position);

        // Calculate turret angle to target
        Vector3 localTarget = drivingTank.transform.InverseTransformPoint(tankCurrentTarget.position);
        float turretAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

        // ALWAYS try to fire if we can - be aggressive!
        bool shouldFire = false;
        if (tankFireCooldown <= 0f && Mathf.Abs(turretAngle) < 20f && dist < TANK_FIRE_RANGE * 1.5f)
        {
            shouldFire = true;
            tankFireCooldown = drivingTank.fireRate + Random.Range(0f, 0.5f);
        }

        // If target is too far, move closer while shooting
        if (dist > TANK_FIRE_RANGE)
        {
            DriveTankToward(tankCurrentTarget.position);
            // Keep turret aimed and firing while moving
            drivingTank.SetTurretInput(turretAngle, shouldFire);
        }
        else
        {
            // In range - stop and blast them
            float moveInput = 0f;
            float turnInput = 0f;

            // Turn hull toward target if turret angle is extreme
            if (Mathf.Abs(turretAngle) > 60f)
            {
                turnInput = Mathf.Sign(turretAngle) * 0.7f;
            }

            drivingTank.SetAIInput(moveInput, turnInput, turretAngle, shouldFire);
        }
    }

    // Find a hull-down position - where hull is covered but turret has line of sight
    Vector3 FindHullDownPosition(Vector3 tankPos, Vector3 enemyPos)
    {
        Vector3 bestPosition = Vector3.zero;
        float bestScore = float.MinValue;

        Vector3 toEnemy = (enemyPos - tankPos).normalized;
        Vector3 perpendicular = Vector3.Cross(toEnemy, Vector3.up).normalized;

        // Search for positions in an arc around current position, facing enemy
        float[] distances = { 10f, 20f, 30f, 40f };
        float[] angles = { -60f, -40f, -20f, 0f, 20f, 40f, 60f };

        foreach (float dist in distances)
        {
            foreach (float angle in angles)
            {
                // Calculate candidate position
                Vector3 offset = Quaternion.Euler(0, angle, 0) * (-toEnemy * dist);
                Vector3 candidatePos = tankPos + offset;

                // Must be on NavMesh
                NavMeshHit navHit;
                if (!NavMesh.SamplePosition(candidatePos, out navHit, 5f, NavMesh.AllAreas))
                    continue;
                candidatePos = navHit.position;

                // Must be within reasonable range to enemy
                float distToEnemy = Vector3.Distance(candidatePos, enemyPos);
                if (distToEnemy < 20f || distToEnemy > TANK_FIRE_RANGE)
                    continue;

                // Score this position based on hull-down quality
                float score = ScoreHullDownPosition(candidatePos, enemyPos);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = candidatePos;
                }
            }
        }

        // Also check positions perpendicular to enemy (flanking positions with cover)
        foreach (float dist in new float[] { 15f, 25f, 35f })
        {
            foreach (float side in new float[] { -1f, 1f })
            {
                Vector3 candidatePos = tankPos + perpendicular * side * dist;

                NavMeshHit navHit;
                if (!NavMesh.SamplePosition(candidatePos, out navHit, 5f, NavMesh.AllAreas))
                    continue;
                candidatePos = navHit.position;

                float distToEnemy = Vector3.Distance(candidatePos, enemyPos);
                if (distToEnemy < 20f || distToEnemy > TANK_FIRE_RANGE)
                    continue;

                float score = ScoreHullDownPosition(candidatePos, enemyPos);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = candidatePos;
                }
            }
        }

        return bestPosition;
    }

    // Score a position for hull-down quality
    // Higher score = better (hull covered, turret has LOS, good angle)
    float ScoreHullDownPosition(Vector3 position, Vector3 enemyPos)
    {
        float score = 0f;

        // Check if hull is protected (raycast from enemy to hull height hits something)
        Vector3 hullPos = position + Vector3.up * TANK_HULL_HEIGHT;
        Vector3 toHull = hullPos - enemyPos;
        float hullDist = toHull.magnitude;

        if (Physics.Raycast(enemyPos + Vector3.up * 1.5f, toHull.normalized, out RaycastHit hullHit, hullDist - 1f))
        {
            // Something blocks LOS to hull - good!
            // Ignore terrain/ground colliders for this check
            if (!ShouldIgnoreCollider(hullHit.collider))
            {
                score += 50f; // Major bonus for hull cover
            }
        }

        // Check if turret has line of sight (must NOT be blocked)
        Vector3 turretPos = position + Vector3.up * TANK_TURRET_HEIGHT;
        Vector3 toTurret = turretPos - enemyPos;
        float turretDist = toTurret.magnitude;

        if (!Physics.Raycast(enemyPos + Vector3.up * 1.5f, toTurret.normalized, turretDist - 1f))
        {
            score += 30f; // Turret has clear shot
        }
        else
        {
            score -= 100f; // Turret blocked - very bad position
        }

        // Prefer higher ground (adds to hull-down effect)
        float heightDiff = position.y - enemyPos.y;
        if (heightDiff > 0)
        {
            score += Mathf.Min(heightDiff * 5f, 20f); // Up to 20 bonus for height
        }

        // Prefer not too close, not too far
        float distToEnemy = Vector3.Distance(position, enemyPos);
        if (distToEnemy > 40f && distToEnemy < 55f)
        {
            score += 15f; // Ideal engagement range
        }

        // Slight preference for positions closer to current location (less travel time)
        float travelDist = Vector3.Distance(drivingTank.transform.position, position);
        score -= travelDist * 0.5f;

        return score;
    }

    // Find a flanking position to approach an enemy tank from the side
    Vector3 FindFlankingPosition(TankController enemyTank)
    {
        if (enemyTank == null || drivingTank == null) return Vector3.zero;

        Vector3 tankPos = drivingTank.transform.position;
        Vector3 enemyPos = enemyTank.transform.position;
        Vector3 enemyForward = enemyTank.transform.forward;

        // Calculate positions at various angles from enemy's front
        // We want to be 70-110 degrees from their front (their side)
        Vector3 bestPosition = Vector3.zero;
        float bestScore = float.MinValue;

        // Check both sides of the enemy
        float[] flankAngles = { 70f, 90f, 110f, -70f, -90f, -110f };
        float[] distances = { 35f, 45f, 55f };

        foreach (float angle in flankAngles)
        {
            foreach (float dist in distances)
            {
                // Calculate position at this angle from enemy's front
                Vector3 flankDir = Quaternion.Euler(0, angle, 0) * enemyForward;
                Vector3 candidatePos = enemyPos + flankDir * dist;

                // Must be on NavMesh
                NavMeshHit navHit;
                if (!NavMesh.SamplePosition(candidatePos, out navHit, 10f, NavMesh.AllAreas))
                    continue;
                candidatePos = navHit.position;

                // Score this flanking position
                float score = 0f;

                // Closer to ideal 90-degree angle is better
                float angleFromFront = Mathf.Abs(angle);
                float angleDiffFrom90 = Mathf.Abs(90f - angleFromFront);
                score += 30f - angleDiffFrom90; // Max 30 at perfect 90 degrees

                // Prefer positions on our side (less travel time)
                Vector3 toCandidate = candidatePos - tankPos;
                Vector3 toEnemy = enemyPos - tankPos;
                float dotProduct = Vector3.Dot(toCandidate.normalized, toEnemy.normalized);
                if (dotProduct > 0) // Position is "forward" relative to us
                    score += 20f;

                // Prefer shorter travel distance
                float travelDist = Vector3.Distance(tankPos, candidatePos);
                score -= travelDist * 0.3f;

                // Make sure we can actually see the enemy from this position
                Vector3 toEnemyFromCandidate = enemyPos - candidatePos;
                if (Physics.Raycast(candidatePos + Vector3.up * 2f, toEnemyFromCandidate.normalized, toEnemyFromCandidate.magnitude - 3f))
                {
                    score -= 50f; // LOS blocked, bad position
                }

                // Check we can path there
                NavMeshPath testPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(tankPos, candidatePos, NavMesh.AllAreas, testPath) ||
                    testPath.status != NavMeshPathStatus.PathComplete)
                {
                    continue; // Can't reach this position
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = candidatePos;
                }
            }
        }

        return bestPosition;
    }

    // Check if we're flanking an enemy (at their side, not front)
    bool IsInFlankingPosition(TankController enemyTank)
    {
        if (enemyTank == null || drivingTank == null) return false;

        Vector3 toUs = drivingTank.transform.position - enemyTank.transform.position;
        toUs.y = 0;
        toUs.Normalize();

        Vector3 enemyForward = enemyTank.transform.forward;
        enemyForward.y = 0;
        enemyForward.Normalize();

        float angle = Vector3.Angle(enemyForward, toUs);

        // We're flanking if we're more than 60 degrees from their front
        return angle > 60f;
    }

    // ===== ROAD/TERRAIN PREFERENCE SYSTEM =====

    // Check what type of terrain is at a position
    enum TerrainType { Road, Normal, OffRoad, Water }

    TerrainType GetTerrainTypeAt(Vector3 position)
    {
        // Raycast down to find ground
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f))
        {
            string objName = hit.collider.gameObject.name.ToLower();

            // Check for road keywords
            foreach (string keyword in ROAD_KEYWORDS)
            {
                if (objName.Contains(keyword))
                    return TerrainType.Road;
            }

            // Check for off-road keywords
            foreach (string keyword in OFFROAD_KEYWORDS)
            {
                if (objName.Contains(keyword))
                    return TerrainType.OffRoad;
            }

            // Check parent objects too
            Transform parent = hit.collider.transform.parent;
            if (parent != null)
            {
                string parentName = parent.name.ToLower();
                foreach (string keyword in ROAD_KEYWORDS)
                {
                    if (parentName.Contains(keyword))
                        return TerrainType.Road;
                }
            }

            // Check material name if available
            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                string matName = renderer.sharedMaterial.name.ToLower();
                foreach (string keyword in ROAD_KEYWORDS)
                {
                    if (matName.Contains(keyword))
                        return TerrainType.Road;
                }
                foreach (string keyword in OFFROAD_KEYWORDS)
                {
                    if (matName.Contains(keyword))
                        return TerrainType.OffRoad;
                }
            }
        }

        return TerrainType.Normal;
    }

    // Update terrain check for current tank position
    void UpdateTankTerrainCheck()
    {
        if (drivingTank == null) return;

        tankTerrainCheckTimer -= Time.deltaTime;
        if (tankTerrainCheckTimer <= 0f)
        {
            tankTerrainCheckTimer = TANK_TERRAIN_CHECK_INTERVAL;
            TerrainType terrain = GetTerrainTypeAt(drivingTank.transform.position);
            tankOnRoad = (terrain == TerrainType.Road);
        }
    }

    // Get speed modifier based on current terrain
    float GetTerrainSpeedModifier()
    {
        if (tankOnRoad)
            return TANK_ROAD_SPEED_BONUS;

        TerrainType terrain = GetTerrainTypeAt(drivingTank.transform.position);
        switch (terrain)
        {
            case TerrainType.Road:
                return TANK_ROAD_SPEED_BONUS;
            case TerrainType.OffRoad:
                return TANK_OFFROAD_SPEED_PENALTY;
            case TerrainType.Water:
                return 0.4f; // Very slow in water
            default:
                return 1f;
        }
    }

    // Score a path based on how much of it is on roads
    float ScorePathForRoads(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return 0f;

        float totalDistance = 0f;
        float roadDistance = 0f;

        // Sample every 5th point for performance
        for (int i = 0; i < path.Count - 1; i += 5)
        {
            Vector3 point = path[i];
            Vector3 nextPoint = path[Mathf.Min(i + 5, path.Count - 1)];
            float segmentDist = Vector3.Distance(point, nextPoint);

            TerrainType terrain = GetTerrainTypeAt(point);
            if (terrain == TerrainType.Road)
                roadDistance += segmentDist;
            else if (terrain == TerrainType.OffRoad)
                roadDistance -= segmentDist * 0.5f; // Penalty for off-road

            totalDistance += segmentDist;
        }

        if (totalDistance <= 0f) return 0f;
        return roadDistance / totalDistance; // -0.5 to 1.0 score
    }

    // Try to find a road-friendly path to target
    // Returns true if a better road path was found
    bool TryFindRoadPath(Vector3 target, out List<Vector3> roadPath)
    {
        roadPath = null;
        if (drivingTank == null) return false;

        Vector3 tankPos = drivingTank.transform.position;

        // First, get the direct NavMesh path
        NavMeshPath directPath = new NavMeshPath();
        if (!NavMesh.CalculatePath(tankPos, target, NavMesh.AllAreas, directPath))
            return false;

        List<Vector3> directPathList = new List<Vector3>(directPath.corners);
        float directScore = ScorePathForRoads(directPathList);

        // If direct path is mostly on road, use it
        if (directScore > 0.6f)
        {
            roadPath = directPathList;
            return true;
        }

        // Try to find waypoints through roads
        // Look for nearby road points that could serve as intermediate waypoints
        List<Vector3> roadWaypoints = FindNearbyRoadPoints(tankPos, target);

        float bestScore = directScore;
        List<Vector3> bestPath = directPathList;

        // Try routing through each road waypoint
        foreach (Vector3 waypoint in roadWaypoints)
        {
            NavMeshPath path1 = new NavMeshPath();
            NavMeshPath path2 = new NavMeshPath();

            if (NavMesh.CalculatePath(tankPos, waypoint, NavMesh.AllAreas, path1) &&
                NavMesh.CalculatePath(waypoint, target, NavMesh.AllAreas, path2))
            {
                List<Vector3> combinedPath = new List<Vector3>(path1.corners);
                combinedPath.AddRange(path2.corners);

                float score = ScorePathForRoads(combinedPath);

                // Penalize longer paths slightly
                float directDist = Vector3.Distance(tankPos, target);
                float pathDist = CalculatePathDistance(combinedPath);
                float lengthPenalty = (pathDist / directDist - 1f) * 0.3f; // 30% penalty per extra distance ratio
                score -= lengthPenalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = combinedPath;
                }
            }
        }

        // Only use road path if significantly better
        if (bestScore > directScore + 0.2f)
        {
            roadPath = bestPath;
            return true;
        }

        return false;
    }

    // Find nearby points that are on roads
    List<Vector3> FindNearbyRoadPoints(Vector3 from, Vector3 to)
    {
        List<Vector3> roadPoints = new List<Vector3>();

        Vector3 midpoint = (from + to) / 2f;
        float searchRadius = Vector3.Distance(from, to) * 0.6f;
        searchRadius = Mathf.Clamp(searchRadius, 20f, 100f);

        // Sample points in a grid pattern
        int samples = 8;
        for (int x = -samples; x <= samples; x += 2)
        {
            for (int z = -samples; z <= samples; z += 2)
            {
                Vector3 samplePos = midpoint + new Vector3(x * searchRadius / samples, 0, z * searchRadius / samples);

                NavMeshHit navHit;
                if (NavMesh.SamplePosition(samplePos, out navHit, 10f, NavMesh.AllAreas))
                {
                    samplePos = navHit.position;

                    if (GetTerrainTypeAt(samplePos) == TerrainType.Road)
                    {
                        // Check it's not too close to existing points
                        bool tooClose = false;
                        foreach (Vector3 existing in roadPoints)
                        {
                            if (Vector3.Distance(existing, samplePos) < 15f)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (!tooClose)
                            roadPoints.Add(samplePos);
                    }
                }
            }
        }

        return roadPoints;
    }

    float CalculatePathDistance(List<Vector3> path)
    {
        float dist = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            dist += Vector3.Distance(path[i], path[i + 1]);
        }
        return dist;
    }

    void UpdateTankRetreat()
    {
        if (drivingTank == null) return;

        // Retreat toward patrol center
        DriveTankToward(tankPatrolCenter);

        float dist = Vector3.Distance(drivingTank.transform.position, tankPatrolCenter);
        if (dist < 20f)
        {
            MakeTankTacticalDecision();
        }
    }

    // ===== NEW SIMPLIFIED NAVIGATION =====
    // Uses TankNavigation component - handles all pathfinding and movement internally
    void DriveTankTowardNew(Vector3 target)
    {
        if (drivingTank == null || !drivingTank.HasNavigation) return;

        TankNavigation nav = drivingTank.Navigation;
        Vector3 tankPos = drivingTank.transform.position;
        float distToTarget = Vector3.Distance(tankPos, target);

        // Close enough - stop
        if (distToTarget < 8f)
        {
            nav.Stop();
            tankHasNavigationTarget = false;
            return;
        }

        // Set new destination if target changed significantly
        if (!tankHasNavigationTarget || Vector3.Distance(target, tankNavigationTarget) > 10f)
        {
            if (nav.SetDestination(target))
            {
                tankNavigationTarget = target;
                tankHasNavigationTarget = true;
                Debug.Log($"[Tank AI] New navigation target: {target}");
            }
            else
            {
                Debug.LogWarning("[Tank AI] Failed to set navigation destination");
                tankHasNavigationTarget = false;
            }
        }

        // The TankNavigation component handles all movement automatically
        // We just need to control the turret

        // Update turret to scan/aim while moving
        float turretAngle = GetTankTurretAngle();
        drivingTank.SetTurretInput(turretAngle, false);
    }

    void DriveTankToward(Vector3 target)
    {
        if (drivingTank == null) return;

        // ===== NEW NAVIGATION SYSTEM =====
        // Uses TankNavigation component for simpler, more reliable pathfinding
        if (useNewTankNavigation && drivingTank.HasNavigation)
        {
            DriveTankTowardNew(target);
            return;
        }

        // ===== LEGACY NAVIGATION SYSTEM =====
        // Falls back to this if new system not available

        Vector3 tankPos = drivingTank.transform.position;
        float distanceToFinalTarget = Vector3.Distance(tankPos, target);

        // Close enough to final target, stop smoothly
        if (distanceToFinalTarget < 8f)
        {
            tankStuckTimer = 0f;
            tankReverseTimer = 0f;
            tankSmoothedPath.Clear();
            tankTargetSpeed = 0f;

            // Smooth stop
            float stopInput = Mathf.MoveTowards(tankCurrentSpeed, 0f, TANK_DECEL_RATE * Time.deltaTime);
            tankCurrentSpeed = stopInput;
            drivingTank.SetAIInput(stopInput * 0.1f, 0f, GetTankTurretAngle(), false);
            return;
        }

        // ===== PATH CALCULATION =====
        tankPathRecalcTimer -= Time.deltaTime;
        bool needNewPath = tankSmoothedPath.Count == 0 ||
                           tankSmoothedIndex >= tankSmoothedPath.Count - 1 ||
                           Vector3.Distance(target, tankLastDestination) > 15f ||
                           tankPathRecalcTimer <= 0f;

        if (needNewPath)
        {
            CalculateSmoothedTankPath(target);
            tankPathRecalcTimer = TANK_PATH_RECALC_INTERVAL;

            // If path calculation failed or path is too short, wait
            if (tankSmoothedPath.Count < 2)
            {
                Debug.LogWarning("[Tank AI] No valid path - waiting");
                drivingTank.SetAIInput(0f, 0f, GetTankTurretAngle(), false);
                tankPathRecalcTimer = 0.5f; // Try again soon
                return;
            }
        }

        // Don't drive without a valid path
        if (tankSmoothedPath.Count < 2)
        {
            drivingTank.SetAIInput(0f, 0f, GetTankTurretAngle(), false);
            return;
        }

        // ===== LOOK-AHEAD STEERING =====
        // Find the "carrot" - a point on the path ahead of us
        tankSteerTarget = GetLookAheadPoint(tankPos);

        // ===== DRIVE TOWARD CARROT =====
        DriveTowardPoint(tankSteerTarget, target);
    }

    void CalculateSmoothedTankPath(Vector3 target)
    {
        if (drivingTank == null) return;

        Vector3 tankPos = drivingTank.transform.position;
        tankSmoothedPath.Clear();
        tankSmoothedIndex = 0;
        tankLastDestination = target;

        // Get NavMesh path first
        if (tankNavPath == null) tankNavPath = new NavMeshPath();

        NavMeshHit tankHit, targetHit;
        Vector3 navTankPos = NavMesh.SamplePosition(tankPos, out tankHit, 30f, NavMesh.AllAreas) ? tankHit.position : tankPos;
        Vector3 navTargetPos = NavMesh.SamplePosition(target, out targetHit, 30f, NavMesh.AllAreas) ? targetHit.position : target;

        if (!NavMesh.CalculatePath(navTankPos, navTargetPos, NavMesh.AllAreas, tankNavPath) ||
            tankNavPath.corners.Length < 2)
        {
            Debug.LogWarning("[Tank AI] No NavMesh path found to target");
            return; // Return empty path - tank won't move
        }

        Vector3[] corners = tankNavPath.corners;

        // ===== STEP 0.5: Try to find a road-friendly path =====
        // Only do this for longer paths (>50m) to avoid overhead for short trips
        float directDist = Vector3.Distance(tankPos, target);
        if (directDist > 50f)
        {
            List<Vector3> roadPath;
            if (TryFindRoadPath(target, out roadPath) && roadPath != null && roadPath.Count >= 2)
            {
                corners = roadPath.ToArray();
                Debug.Log($"[Tank AI] Using road-friendly path with {corners.Length} waypoints");
            }
        }

        // ===== STEP 1: Generate candidate path points =====
        List<Vector3> candidatePath = new List<Vector3>();
        candidatePath.Add(tankPos);

        for (int i = 1; i < corners.Length; i++)
        {
            // Add intermediate points every 3 meters
            Vector3 from = candidatePath[candidatePath.Count - 1];
            Vector3 to = corners[i];
            float dist = Vector3.Distance(from, to);
            int steps = Mathf.CeilToInt(dist / 3f);

            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                candidatePath.Add(Vector3.Lerp(from, to, t));
            }
        }

        // ===== STEP 2: VERIFY EVERY SINGLE POINT WITH PHYSICS =====
        List<Vector3> verifiedPath = new List<Vector3>();
        verifiedPath.Add(tankPos);

        // Skip physics checks for the first 15m from spawn (tank is already there)
        const float SKIP_CHECK_DISTANCE = 15f;

        for (int i = 1; i < candidatePath.Count; i++)
        {
            Vector3 point = candidatePath[i];
            Vector3 prevPoint = verifiedPath[verifiedPath.Count - 1];
            float distFromStart = Vector3.Distance(tankPos, point);

            // Skip physics verification near spawn - assume tank can move from where it is
            if (distFromStart < SKIP_CHECK_DISTANCE)
            {
                verifiedPath.Add(point);
                continue;
            }

            // Check if tank can physically exist at this point
            if (!CanTankFitAt(point))
            {
                Debug.LogWarning($"[Tank AI] BLOCKED at point {i}/{candidatePath.Count}: {point}");

                // Try to find alternate around this obstacle
                Vector3 alternate = FindClearAlternate(prevPoint, point);
                if (alternate != Vector3.zero)
                {
                    verifiedPath.Add(alternate);
                    Debug.Log($"[Tank AI] Found alternate at {alternate}");
                }
                else
                {
                    // Can't get around - path fails here
                    Debug.LogError($"[Tank AI] PATH BLOCKED - Cannot reach target. Obstacle at {point}");

                    // Return partial path to the last good point
                    if (verifiedPath.Count >= 2)
                    {
                        tankSmoothedPath = new List<Vector3>(verifiedPath);
                        return;
                    }
                    return; // Complete failure
                }
            }
            else
            {
                // Also verify we can drive FROM previous point TO this point
                if (CanTankDriveBetween(prevPoint, point))
                {
                    verifiedPath.Add(point);
                }
                else
                {
                    Debug.LogWarning($"[Tank AI] Can't drive between {prevPoint} and {point}");

                    Vector3 alternate = FindClearAlternate(prevPoint, point);
                    if (alternate != Vector3.zero)
                    {
                        verifiedPath.Add(alternate);
                    }
                }
            }
        }

        if (verifiedPath.Count < 2)
        {
            Debug.LogWarning("[Tank AI] Path verification failed - using raw NavMesh path as fallback");
            // Fallback: Use raw NavMesh corners as path (less safe but better than not moving)
            verifiedPath.Clear();
            foreach (var corner in corners)
            {
                verifiedPath.Add(corner);
            }
            if (verifiedPath.Count < 2)
            {
                Debug.LogError("[Tank AI] No valid path at all");
                return;
            }
        }

        // ===== STEP 3: Smooth the verified path =====
        tankSmoothedPath.Clear();

        for (int i = 0; i < verifiedPath.Count - 1; i++)
        {
            Vector3 p0 = (i == 0) ? verifiedPath[0] : verifiedPath[i - 1];
            Vector3 p1 = verifiedPath[i];
            Vector3 p2 = verifiedPath[i + 1];
            Vector3 p3 = (i + 2 < verifiedPath.Count) ? verifiedPath[i + 2] : verifiedPath[i + 1];

            float segLen = Vector3.Distance(p1, p2);
            int subdivs = Mathf.Max(2, Mathf.CeilToInt(segLen / 2f));

            for (int j = 0; j < subdivs; j++)
            {
                float t = (float)j / subdivs;
                Vector3 smoothPoint = CatmullRom(p0, p1, p2, p3, t);
                tankSmoothedPath.Add(smoothPoint);
            }
        }
        tankSmoothedPath.Add(verifiedPath[verifiedPath.Count - 1]);

        Debug.Log($"[Tank AI] PATH VERIFIED: {candidatePath.Count} points checked -> {verifiedPath.Count} clear -> {tankSmoothedPath.Count} smooth");
    }

    // Check if the tank can physically exist at this position (no collisions)
    bool CanTankFitAt(Vector3 position)
    {
        float tankRadius = TANK_WIDTH / 2f + 0.5f; // Half width + margin
        float tankHeight = 2.5f;

        // Check ABOVE ground level to avoid hitting the ground itself
        // Box starts at 2m above position (above most ground colliders)
        Vector3 boxCenter = position + Vector3.up * 2f;
        Vector3 boxHalfExtents = new Vector3(tankRadius, tankHeight / 2f, tankRadius * 1.5f);

        Collider[] hits = Physics.OverlapBox(boxCenter, boxHalfExtents, Quaternion.identity);

        // Debug: Log all detected colliders for first time diagnostics
        if (hits.Length > 0 && Time.frameCount % 300 == 0)
        {
            string allHits = string.Join(", ", System.Array.ConvertAll(hits, h => h.gameObject.name));
            Debug.Log($"[Tank AI] CanTankFitAt at {position} found {hits.Length} colliders: {allHits}");
        }

        foreach (Collider hit in hits)
        {
            if (ShouldIgnoreCollider(hit)) continue;

            // Found a blocking collider
            Debug.DrawLine(position + Vector3.up, hit.transform.position, Color.red, 2f);
            Debug.Log($"[Tank AI] CanTankFitAt BLOCKED by: {hit.gameObject.name} (type: {hit.GetType().Name}, layer: {LayerMask.LayerToName(hit.gameObject.layer)})");
            return false;
        }

        return true;
    }

    // Check if tank can drive from A to B without hitting anything
    bool CanTankDriveBetween(Vector3 from, Vector3 to)
    {
        Vector3 direction = (to - from).normalized;
        float distance = Vector3.Distance(from, to);
        float tankRadius = TANK_WIDTH / 2f;

        // Only check at heights ABOVE ground (1.5m and 2.5m)
        // Skip low height to avoid hitting ground
        float[] heights = { 1.5f, 2.5f };

        foreach (float h in heights)
        {
            Vector3 origin = from + Vector3.up * h;

            if (Physics.SphereCast(origin, tankRadius, direction, out RaycastHit hit, distance))
            {
                if (!ShouldIgnoreCollider(hit.collider))
                {
                    Debug.DrawLine(origin, hit.point, Color.magenta, 2f);
                    Debug.Log($"[Tank AI] CanTankDriveBetween blocked by: {hit.collider.gameObject.name} at height {h}");
                    return false;
                }
            }
        }

        return true;
    }

    // Unified check for colliders we should ignore
    bool ShouldIgnoreCollider(Collider col)
    {
        if (col == null) return true;
        if (col.isTrigger) return true;

        // Ignore the tank itself
        if (drivingTank != null && col.transform.IsChildOf(drivingTank.transform)) return true;

        // Check for Unity Terrain
        if (col.GetComponent<Terrain>() != null) return true;
        if (col is TerrainCollider) return true;

        // Check for MeshCollider on very large objects (likely ground)
        if (col is MeshCollider mc)
        {
            // Large mesh colliders are usually terrain/ground
            Bounds bounds = col.bounds;
            if (bounds.size.x > 20f && bounds.size.z > 20f && bounds.size.y < 5f)
                return true;
        }

        // Check for large BoxColliders (ground planes)
        if (col is BoxCollider box)
        {
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            if (size.x > 20f && size.z > 20f && size.y < 3f)
                return true;
        }

        // Check object name for ground-like names
        string name = col.gameObject.name.ToLower();
        if (name.Contains("terrain") || name.Contains("ground") || name.Contains("floor") ||
            name.Contains("plane") || name.Contains("navmesh"))
            return true;

        // Check layer
        int layer = col.gameObject.layer;
        string layerName = LayerMask.LayerToName(layer);
        if (layerName.ToLower().Contains("ground") || layerName.ToLower().Contains("terrain"))
            return true;

        // Ignore other AI soldiers and players (they move)
        if (col.GetComponent<AIController>() != null) return true;
        if (col.GetComponent<FPSControllerPhoton>() != null) return true;
        if (col.GetComponentInParent<AIController>() != null) return true;
        if (col.GetComponentInParent<FPSControllerPhoton>() != null) return true;

        // Ignore other tanks (they move)
        if (col.GetComponent<TankController>() != null) return true;
        if (col.GetComponentInParent<TankController>() != null) return true;

        // Ignore helicopters and jets (they move)
        if (col.GetComponent<HelicopterController>() != null) return true;
        if (col.GetComponentInParent<HelicopterController>() != null) return true;
        if (col.GetComponent<JetController>() != null) return true;
        if (col.GetComponentInParent<JetController>() != null) return true;

        // Ignore spawner objects (they usually have trigger colliders but just in case)
        string nameLower = col.gameObject.name.ToLower();
        if (nameLower.Contains("spawner") || nameLower.Contains("spawn"))
            return true;

        return false;
    }

    // Find a clear alternate point around an obstacle
    Vector3 FindClearAlternate(Vector3 from, Vector3 blockedPoint)
    {
        Vector3 direction = (blockedPoint - from).normalized;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
        float distance = Vector3.Distance(from, blockedPoint);

        // Try increasingly large offsets
        float[] offsets = { 8f, 15f, 25f, 40f, -8f, -15f, -25f, -40f };

        foreach (float offset in offsets)
        {
            // Create a point offset from the midpoint
            Vector3 midpoint = (from + blockedPoint) / 2f;
            Vector3 testPoint = midpoint + perpendicular * offset;

            // Snap to NavMesh
            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(testPoint, out navHit, 10f, NavMesh.AllAreas))
                continue;

            testPoint = navHit.position;

            // Verify tank can fit there AND can drive there from 'from' AND can drive to blocked point
            if (CanTankFitAt(testPoint) &&
                CanTankDriveBetween(from, testPoint) &&
                CanTankDriveBetween(testPoint, blockedPoint))
            {
                return testPoint;
            }
        }

        // Try going further back and around
        foreach (float offset in new float[] { 20f, 35f, 50f, -20f, -35f, -50f })
        {
            Vector3 testPoint = from + perpendicular * offset + direction * (distance * 0.3f);

            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(testPoint, out navHit, 15f, NavMesh.AllAreas))
                continue;

            testPoint = navHit.position;

            if (CanTankFitAt(testPoint) && CanTankDriveBetween(from, testPoint))
            {
                return testPoint;
            }
        }

        return Vector3.zero; // No alternate found
    }

    // Catmull-Rom spline interpolation
    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    // Get the look-ahead point on the path (the "carrot")
    Vector3 GetLookAheadPoint(Vector3 tankPos)
    {
        if (tankSmoothedPath.Count == 0)
            return tankPos + drivingTank.transform.forward * 10f;

        // Look-ahead distance based on current speed
        Rigidbody rb = drivingTank.GetComponent<Rigidbody>();
        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float lookAhead = Mathf.Clamp(
            TANK_LOOK_AHEAD_MIN + speed * TANK_LOOK_AHEAD_SPEED_MULT,
            TANK_LOOK_AHEAD_MIN,
            TANK_LOOK_AHEAD_MAX
        );

        // Find closest point on path
        float minDist = float.MaxValue;
        int closestIndex = tankSmoothedIndex;

        for (int i = tankSmoothedIndex; i < Mathf.Min(tankSmoothedIndex + 20, tankSmoothedPath.Count); i++)
        {
            float dist = Vector3.Distance(tankPos, tankSmoothedPath[i]);
            if (dist < minDist)
            {
                minDist = dist;
                closestIndex = i;
            }
        }

        // Update our current index (don't go backwards)
        if (closestIndex > tankSmoothedIndex)
            tankSmoothedIndex = closestIndex;

        // Now find the point that is 'lookAhead' distance along the path
        float distanceAccum = 0f;
        Vector3 lookAheadPoint = tankSmoothedPath[tankSmoothedPath.Count - 1]; // Default to end

        for (int i = tankSmoothedIndex; i < tankSmoothedPath.Count - 1; i++)
        {
            Vector3 segStart = tankSmoothedPath[i];
            Vector3 segEnd = tankSmoothedPath[i + 1];
            float segLength = Vector3.Distance(segStart, segEnd);

            if (distanceAccum + segLength >= lookAhead)
            {
                // Interpolate to find exact point
                float remaining = lookAhead - distanceAccum;
                float t = remaining / segLength;
                lookAheadPoint = Vector3.Lerp(segStart, segEnd, t);
                break;
            }

            distanceAccum += segLength;
        }

        return lookAheadPoint;
    }

    // Calculate path curvature ahead - returns 0 (straight) to 1 (sharp turn)
    float CalculatePathCurvature(Vector3 tankPos)
    {
        if (tankSmoothedPath.Count < 3 || tankSmoothedIndex >= tankSmoothedPath.Count - 2)
            return 0f;

        float maxCurvature = 0f;
        float distanceChecked = 0f;

        // Check curvature at multiple points ahead
        for (int i = tankSmoothedIndex; i < tankSmoothedPath.Count - 2 && distanceChecked < TANK_CURVATURE_LOOKAHEAD; i++)
        {
            Vector3 p0 = tankSmoothedPath[i];
            Vector3 p1 = tankSmoothedPath[i + 1];
            Vector3 p2 = tankSmoothedPath[i + 2];

            // Calculate angle between segments
            Vector3 dir1 = (p1 - p0).normalized;
            Vector3 dir2 = (p2 - p1).normalized;
            dir1.y = 0; dir2.y = 0;

            if (dir1.magnitude > 0.01f && dir2.magnitude > 0.01f)
            {
                float angle = Vector3.Angle(dir1, dir2);
                float curvature = angle / 90f; // Normalize: 90° = 1.0

                // Weight curvature by distance - closer curves matter more
                float distanceToPoint = Vector3.Distance(tankPos, p1);
                float distanceWeight = 1f - Mathf.Clamp01(distanceToPoint / TANK_CURVATURE_LOOKAHEAD);
                curvature *= (0.5f + distanceWeight * 0.5f);

                maxCurvature = Mathf.Max(maxCurvature, curvature);
            }

            distanceChecked += Vector3.Distance(p0, p1);
        }

        return Mathf.Clamp01(maxCurvature);
    }

    float GetTankTurretAngle()
    {
        if (drivingTank == null) return 0f;

        if (tankCurrentTarget != null)
        {
            Vector3 localTarget = drivingTank.transform.InverseTransformPoint(tankCurrentTarget.position);
            return Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        }
        return GetTankTurretScanAngle();
    }

    void CalculateTankPath(Vector3 target)
    {
        if (drivingTank == null || tankNavPath == null) return;

        Vector3 tankPos = drivingTank.transform.position;

        // Find nearest point on NavMesh for tank position
        NavMeshHit tankHit;
        Vector3 navTankPos = tankPos;
        if (NavMesh.SamplePosition(tankPos, out tankHit, 20f, NavMesh.AllAreas))
        {
            navTankPos = tankHit.position;
        }

        // Find nearest point on NavMesh for target
        NavMeshHit targetHit;
        Vector3 navTargetPos = target;
        if (NavMesh.SamplePosition(target, out targetHit, 20f, NavMesh.AllAreas))
        {
            navTargetPos = targetHit.position;
        }

        // Calculate path
        if (NavMesh.CalculatePath(navTankPos, navTargetPos, NavMesh.AllAreas, tankNavPath))
        {
            if (tankNavPath.status == NavMeshPathStatus.PathComplete ||
                tankNavPath.status == NavMeshPathStatus.PathPartial)
            {
                tankPathCorners = tankNavPath.corners;
                tankCurrentWaypoint = 0;
                tankLastDestination = target;

                // Skip first waypoint if it's behind us or very close
                if (tankPathCorners.Length > 1)
                {
                    float distToFirst = Vector3.Distance(tankPos, tankPathCorners[0]);
                    if (distToFirst < 5f)
                    {
                        tankCurrentWaypoint = 1;
                    }
                }

                Debug.Log($"[Tank AI] Path calculated with {tankPathCorners.Length} waypoints");
            }
            else
            {
                Debug.LogWarning("[Tank AI] Path invalid, driving direct");
                tankPathCorners = new Vector3[] { target };
                tankCurrentWaypoint = 0;
            }
        }
        else
        {
            Debug.LogWarning("[Tank AI] No path found, driving direct");
            tankPathCorners = new Vector3[] { target };
            tankCurrentWaypoint = 0;
        }
    }

    void DriveTowardPoint(Vector3 steerTarget, Vector3 finalTarget)
    {
        if (drivingTank == null) return;

        Vector3 tankPos = drivingTank.transform.position;
        Rigidbody tankRb = drivingTank.GetComponent<Rigidbody>();
        float actualSpeed = tankRb != null ? tankRb.linearVelocity.magnitude : 0f;

        Vector3 forward = drivingTank.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 toTarget = steerTarget - tankPos;
        toTarget.y = 0f;
        float distToTarget = toTarget.magnitude;

        float angleToTarget = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);

        // ===== REVERSE MODE (Stuck Recovery) =====
        if (tankReverseTimer > 0f)
        {
            tankReverseTimer -= Time.deltaTime;

            float reverseTurn = 0f;
            if (tankIsSmartReversing && tankReverseDirection != Vector3.zero)
            {
                float angleToReverse = Vector3.SignedAngle(forward, tankReverseDirection, Vector3.up);
                reverseTurn = Mathf.Clamp(angleToReverse / 30f, -1f, 1f);
            }
            else
            {
                reverseTurn = (Mathf.FloorToInt(Time.time * 0.5f) % 2 == 0) ? 1f : -1f;
            }

            if (tankReverseTimer <= 0f)
            {
                tankIsSmartReversing = false;
                tankReverseDirection = Vector3.zero;
                tankSmoothedPath.Clear(); // Force path recalculation
            }

            drivingTank.SetAIInput(-0.6f, reverseTurn, GetTankTurretAngle(), false);
            return;
        }

        // ===== OBSTACLE DETECTION (Simplified but effective) =====
        float obstacleDistance = float.MaxValue;
        float leftClearance = TANK_OBSTACLE_SIDE_DISTANCE;
        float rightClearance = TANK_OBSTACLE_SIDE_DISTANCE;
        bool criticalObstacle = false;

        Vector3 castOrigin = tankPos + Vector3.up * 1.2f;

        // Forward cone - check multiple points across tank width
        for (float widthOffset = -2f; widthOffset <= 2f; widthOffset += 1f)
        {
            for (float angleOffset = -20f; angleOffset <= 20f; angleOffset += 10f)
            {
                Vector3 rayDir = Quaternion.Euler(0, angleOffset, 0) * forward;
                Vector3 rayOrigin = castOrigin + drivingTank.transform.right * widthOffset;

                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, TANK_OBSTACLE_DETECT_DISTANCE))
                {
                    if (!hit.collider.transform.IsChildOf(drivingTank.transform) &&
                        !hit.collider.isTrigger &&
                        hit.collider.gameObject.layer != LayerMask.NameToLayer("Terrain"))
                    {
                        if (hit.distance < obstacleDistance)
                            obstacleDistance = hit.distance;

                        if (hit.distance < 5f)
                            criticalObstacle = true;
                    }
                }
            }
        }

        // Check side clearances
        Vector3 leftRayDir = Quaternion.Euler(0, -60f, 0) * forward;
        Vector3 rightRayDir = Quaternion.Euler(0, 60f, 0) * forward;

        if (Physics.Raycast(castOrigin, leftRayDir, out RaycastHit leftHit, TANK_OBSTACLE_SIDE_DISTANCE))
        {
            if (!leftHit.collider.transform.IsChildOf(drivingTank.transform) && !leftHit.collider.isTrigger)
                leftClearance = leftHit.distance;
        }
        if (Physics.Raycast(castOrigin, rightRayDir, out RaycastHit rightHit, TANK_OBSTACLE_SIDE_DISTANCE))
        {
            if (!rightHit.collider.transform.IsChildOf(drivingTank.transform) && !rightHit.collider.isTrigger)
                rightClearance = rightHit.distance;
        }

        // ===== CALCULATE DESIRED SPEED (Arrive behavior) =====
        float maxSpeed = 1f;

        // Slow down for sharp turns (immediate angle)
        float turnSharpness = Mathf.Abs(angleToTarget) / 90f;
        maxSpeed *= (1f - turnSharpness * 0.7f);

        // Slow down for upcoming path curvature (look-ahead for turns)
        tankPathCurvature = CalculatePathCurvature(tankPos);
        if (tankPathCurvature > 0.1f)
        {
            // Reduce speed based on upcoming curve severity
            float curvatureSpeedFactor = 1f - (tankPathCurvature * 0.6f);
            curvatureSpeedFactor = Mathf.Clamp(curvatureSpeedFactor, TANK_MIN_CURVE_SPEED, 1f);
            maxSpeed *= curvatureSpeedFactor;
        }

        // Slow down near obstacles
        if (obstacleDistance < TANK_OBSTACLE_DETECT_DISTANCE)
        {
            float obstacleFactor = obstacleDistance / TANK_OBSTACLE_DETECT_DISTANCE;
            maxSpeed *= obstacleFactor;
        }

        // Slow down approaching final target (arrive behavior)
        float distToFinal = Vector3.Distance(tankPos, finalTarget);
        if (distToFinal < 20f)
        {
            maxSpeed *= Mathf.Clamp(distToFinal / 20f, 0.2f, 1f);
        }

        // Slow down in danger zones (where we've taken damage before)
        if (IsInTankDangerZone(tankPos))
        {
            maxSpeed *= 0.6f;
            tankCautionSpeedMult = 0.6f;
        }
        else
        {
            tankCautionSpeedMult = Mathf.MoveTowards(tankCautionSpeedMult, 1f, Time.deltaTime * 0.5f);
        }

        // Apply terrain speed modifier (faster on roads, slower off-road)
        float terrainMod = GetTerrainSpeedModifier();
        maxSpeed *= terrainMod;

        tankTargetSpeed = maxSpeed;

        // ===== SMOOTH ACCELERATION =====
        if (tankCurrentSpeed < tankTargetSpeed)
        {
            tankCurrentSpeed = Mathf.MoveTowards(tankCurrentSpeed, tankTargetSpeed, TANK_ACCEL_RATE * Time.deltaTime);
        }
        else
        {
            tankCurrentSpeed = Mathf.MoveTowards(tankCurrentSpeed, tankTargetSpeed, TANK_DECEL_RATE * Time.deltaTime);
        }

        float moveInput = tankCurrentSpeed;

        // ===== CALCULATE STEERING =====
        // Turn rate depends on speed - faster turning when slow/stopped
        float speedRatio = Mathf.Clamp01(actualSpeed / 8f);
        float currentMaxTurnRate = Mathf.Lerp(TANK_TURN_RATE_STATIONARY, TANK_TURN_RATE_MOVING, speedRatio);

        // Desired turn to face target
        float desiredTurnRate = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);

        // Smooth the turn input
        tankCurrentTurnRate = Mathf.MoveTowards(tankCurrentTurnRate, desiredTurnRate, 3f * Time.deltaTime);
        float turnInput = tankCurrentTurnRate;

        // ===== OBSTACLE AVOIDANCE OVERRIDE =====
        if (criticalObstacle)
        {
            // Stop and turn toward clearer side
            moveInput = 0f;
            tankCurrentSpeed = 0f;

            if (leftClearance > rightClearance + 2f)
                turnInput = -1f;
            else if (rightClearance > leftClearance + 2f)
                turnInput = 1f;
            else
                turnInput = (angleToTarget > 0) ? 1f : -1f;

            // Both sides blocked? Reverse
            if (leftClearance < 4f && rightClearance < 4f)
            {
                tankReverseDirection = TankCalculateReverseDirection();
                tankIsSmartReversing = true;
                tankReverseTimer = TANK_REVERSE_DURATION;
                Debug.Log("[Tank AI] Critical obstacle - reversing");
            }
        }
        else if (obstacleDistance < 10f)
        {
            // Steer away from obstacle
            float avoidStrength = 1f - (obstacleDistance / 10f);
            float avoidDir = (rightClearance > leftClearance) ? 1f : -1f;
            turnInput = Mathf.Lerp(turnInput, avoidDir, avoidStrength * 0.8f);
            moveInput *= (0.4f + obstacleDistance / 10f * 0.6f);
        }

        // ===== TURN IN PLACE FOR LARGE ANGLES =====
        if (Mathf.Abs(angleToTarget) > TANK_TURN_IN_PLACE_ANGLE && !criticalObstacle)
        {
            moveInput = 0.1f; // Creep forward slightly while turning
            turnInput = Mathf.Sign(angleToTarget);
        }

        // ===== ESCALATING STUCK DETECTION & RECOVERY =====
        if (tankRb != null && Mathf.Abs(moveInput) > 0.2f && actualSpeed < 0.5f)
        {
            tankStuckTimer += Time.deltaTime;

            if (tankStuckTimer > TANK_STUCK_THRESHOLD)
            {
                tankStuckAttempts++;
                tankLastStuckPosition = tankPos;
                tankStuckTimer = 0f;

                // Escalating recovery based on how many times we've been stuck
                switch (tankStuckAttempts)
                {
                    case 1:
                        // First attempt: Try pivoting while moving forward slightly
                        Debug.Log("[Tank AI] STUCK (attempt 1) - Pivot turn");
                        tankReverseTimer = 1f;
                        tankReverseDirection = Vector3.zero; // Will use random turn
                        tankIsSmartReversing = false;
                        break;

                    case 2:
                        // Second attempt: Full reverse with smart direction
                        Debug.Log("[Tank AI] STUCK (attempt 2) - Smart reverse");
                        TankRecordStuckSpot(tankPos);
                        tankReverseDirection = TankCalculateReverseDirection();
                        tankIsSmartReversing = true;
                        tankReverseTimer = TANK_REVERSE_DURATION;
                        tankSmoothedPath.Clear();
                        break;

                    case 3:
                        // Third attempt: Longer reverse + record failed segment
                        Debug.Log("[Tank AI] STUCK (attempt 3) - Extended reverse + path rejection");
                        TankRecordStuckSpot(tankPos);
                        tankReverseDirection = TankCalculateReverseDirection();
                        tankIsSmartReversing = true;
                        tankReverseTimer = TANK_REVERSE_DURATION * 2f; // Longer reverse
                        tankSmoothedPath.Clear();

                        // Mark this path segment as failed
                        if (tankFailedPathSegments.Count < 10)
                            tankFailedPathSegments.Add(tankPos);
                        break;

                    default:
                        // Fourth+ attempt: Give up on this path, find completely new route
                        Debug.Log("[Tank AI] STUCK (attempt 4+) - Abandoning path, seeking alternate");
                        TankRecordStuckSpot(tankPos);
                        tankStuckAttempts = 0;
                        tankSmoothedPath.Clear();
                        tankFailedPathSegments.Clear();

                        // Force a new tactical decision
                        tankMissionPhase = TankMissionPhase.Idle;
                        tankPathRecalcTimer = 0f;

                        // Reverse to clear area
                        tankReverseDirection = TankCalculateReverseDirection();
                        tankIsSmartReversing = true;
                        tankReverseTimer = TANK_REVERSE_DURATION * 2f;
                        break;
                }
            }
        }
        else
        {
            tankStuckTimer = Mathf.Max(0f, tankStuckTimer - Time.deltaTime);

            // Reset stuck attempts if we've been moving well for a while
            if (actualSpeed > 2f)
            {
                tankStuckEscalationTimer += Time.deltaTime;
                if (tankStuckEscalationTimer > 5f)
                {
                    tankStuckAttempts = 0;
                    tankStuckEscalationTimer = 0f;
                }
            }
            else
            {
                tankStuckEscalationTimer = 0f;
            }
        }

        // Apply awareness speed modifier
        moveInput *= TankGetAwarenessSpeedMult();

        // ===== SEND INPUT =====
        drivingTank.SetAIInput(moveInput, turnInput, GetTankTurretAngle(), false);

        // ===== DEBUG VISUALIZATION =====
        if (Time.frameCount % 5 == 0)
        {
            // Draw smoothed path
            for (int i = 0; i < tankSmoothedPath.Count - 1; i++)
            {
                Color pathColor = (i < tankSmoothedIndex) ? Color.gray : Color.cyan;
                Debug.DrawLine(tankSmoothedPath[i] + Vector3.up, tankSmoothedPath[i + 1] + Vector3.up, pathColor, 0.2f);
            }

            // Draw look-ahead target (the carrot)
            Debug.DrawLine(tankPos + Vector3.up, steerTarget + Vector3.up * 2f, Color.green, 0.2f);
            DrawWireSphere(steerTarget + Vector3.up, 1f, Color.green, 0.2f);

            // Draw tank heading
            Debug.DrawRay(tankPos + Vector3.up * 2.5f, forward * 8f, Color.blue, 0.2f);

            // Draw obstacle detection
            if (obstacleDistance < TANK_OBSTACLE_DETECT_DISTANCE)
            {
                Debug.DrawRay(castOrigin, forward * obstacleDistance, Color.red, 0.2f);
            }
        }
    }

    // Helper for debug visualization
    void DrawWireSphere(Vector3 center, float radius, Color color, float duration)
    {
        // Draw 3 circles
        int segments = 16;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 prevPoint = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * 360f * Mathf.Deg2Rad;
                Vector3 point = center;
                if (axis == 0) point += new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                else if (axis == 1) point += new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                else point += new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                if (i > 0) Debug.DrawLine(prevPoint, point, color, duration);
                prevPoint = point;
            }
        }
    }

    // Check for assigned tank (called from Update for dedicated tank drivers)
    void CheckAssignedTank()
    {
        if (!isDedicatedTankDriver) return;

        if (assignedTank == null || assignedTank.isDestroyed)
        {
            // Tank destroyed - no longer a dedicated driver
            isDedicatedTankDriver = false;
            assignedTank = null;
            return;
        }

        // If not in tank and tank is available, enter it
        if (currentState != AIState.TankDriver && !assignedTank.HasDriver)
        {
            float dist = Vector3.Distance(transform.position, assignedTank.transform.position);
            if (dist < 10f)
            {
                EnterTankAsDriver(assignedTank);
            }
            else
            {
                // Move toward tank
                targetPosition = assignedTank.transform.position;
                currentState = AIState.MovingToPoint;
            }
        }
    }

    // Try to find and enter a nearby tank (for non-dedicated drivers)
    public void TryFindAndDriveTank()
    {
        if (!isDedicatedPilot && !isDedicatedTankDriver) return; // Only dedicated drivers seek tanks

        TankController[] tanks = FindObjectsOfType<TankController>();
        float closestDist = 30f;
        TankController closestTank = null;

        foreach (var tank in tanks)
        {
            if (tank == null || tank.isDestroyed) continue;
            if (tank.HasDriver) continue; // Already has driver

            // Check team
            if (tank.TankTeam != Team.None && tank.TankTeam != team) continue;

            float dist = Vector3.Distance(transform.position, tank.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestTank = tank;
            }
        }

        if (closestTank != null)
        {
            EnterTankAsDriver(closestTank);
        }
    }

    // ==================== TANK AWARENESS METHODS ====================

    // Turret scanning when not in combat
    void UpdateTankTurretScanning()
    {
        if (drivingTank == null) return;
        if (tankCurrentTarget != null)
        {
            tankLastCombatTime = Time.time;
            return;  // Don't scan when we have a target
        }

        // Wait before starting to scan after combat
        if (Time.time - tankLastCombatTime < TANK_SCAN_DELAY) return;

        // Sweep turret back and forth
        tankTurretScanAngle += tankTurretScanDirection * tankTurretScanSpeed * Time.deltaTime;

        // Reverse direction at limits
        if (tankTurretScanAngle > 60f)
        {
            tankTurretScanAngle = 60f;
            tankTurretScanDirection = -1f;
        }
        else if (tankTurretScanAngle < -60f)
        {
            tankTurretScanAngle = -60f;
            tankTurretScanDirection = 1f;
        }
    }

    float GetTankTurretScanAngle()
    {
        if (tankCurrentTarget != null) return 0f;  // Normal aiming takes over
        if (Time.time - tankLastCombatTime < TANK_SCAN_DELAY) return 0f;
        return tankTurretScanAngle;
    }

    // Flank awareness - check sides and rear for threats
    void UpdateTankFlankAwareness()
    {
        if (drivingTank == null) return;

        tankFlankCheckTimer -= Time.deltaTime;
        if (tankFlankCheckTimer > 0f) return;
        tankFlankCheckTimer = TANK_FLANK_CHECK_INTERVAL;

        tankFlankThreat = null;
        Vector3 tankPos = drivingTank.transform.position;
        float checkRadius = 40f;

        // Check for enemies to sides and rear
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.team == team) continue;  // Friendly

                Vector3 toEnemy = ai.transform.position - tankPos;
                float dist = toEnemy.magnitude;
                if (dist > checkRadius) continue;

                // Check if enemy is in our blind spots (sides/rear)
                float angle = Vector3.Angle(drivingTank.transform.forward, toEnemy);
                if (angle > 70f)  // Not in front arc
                {
                    tankFlankThreat = ai.transform;
                    break;
                }
            }
        }

        // Also check players
        if (tankFlankThreat == null && cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null || player.isDead) continue;
                if (player.playerTeam == team) continue;

                Vector3 toEnemy = player.transform.position - tankPos;
                float dist = toEnemy.magnitude;
                if (dist > checkRadius) continue;

                float angle = Vector3.Angle(drivingTank.transform.forward, toEnemy);
                if (angle > 70f)
                {
                    tankFlankThreat = player.transform;
                    break;
                }
            }
        }
    }

    // Track danger zones where we took damage
    public void TankRecordDangerZone(Vector3 position)
    {
        // Don't add duplicates
        foreach (var zone in tankDangerZones)
        {
            if (Vector3.Distance(zone, position) < TANK_DANGER_ZONE_RADIUS)
                return;
        }

        tankDangerZones.Add(position);
        if (tankDangerZones.Count > TANK_MAX_DANGER_ZONES)
            tankDangerZones.RemoveAt(0);

        Debug.Log($"[Tank AI] Recorded danger zone at {position}");
    }

    bool IsInTankDangerZone(Vector3 position)
    {
        foreach (var zone in tankDangerZones)
        {
            if (Vector3.Distance(zone, position) < TANK_DANGER_ZONE_RADIUS)
                return true;
        }
        return false;
    }

    bool TankIsInDangerZone(Vector3 position)
    {
        foreach (var zone in tankDangerZones)
        {
            if (Vector3.Distance(zone, position) < TANK_DANGER_ZONE_RADIUS)
                return true;
        }
        return false;
    }

    // Track where friendly tanks died
    public static void TankRecordDeath(Vector3 position)
    {
        foreach (var pos in tankDeathMemory)
        {
            if (Vector3.Distance(pos, position) < TANK_DEATH_MEMORY_RADIUS)
                return;
        }

        tankDeathMemory.Add(position);
        if (tankDeathMemory.Count > TANK_MAX_DEATH_MEMORIES)
            tankDeathMemory.RemoveAt(0);

        Debug.Log($"[Tank AI] Recorded death location at {position}");
    }

    bool TankIsNearDeathZone(Vector3 position)
    {
        foreach (var pos in tankDeathMemory)
        {
            if (Vector3.Distance(pos, position) < TANK_DEATH_MEMORY_RADIUS)
                return true;
        }
        return false;
    }

    // Sound detection - react to nearby combat
    void UpdateTankSoundDetection()
    {
        if (drivingTank == null) return;

        tankSoundAlertTimer -= Time.deltaTime;

        Vector3 tankPos = drivingTank.transform.position;

        // Check for AI in combat nearby
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.currentState != AIState.Combat) continue;

                float dist = Vector3.Distance(tankPos, ai.transform.position);
                if (dist < TANK_HEARING_RANGE)
                {
                    tankLastHeardCombat = ai.transform.position;
                    tankSoundAlertTimer = 5f;
                    break;
                }
            }
        }
    }

    bool TankHeardCombatNearby()
    {
        return tankSoundAlertTimer > 0f;
    }

    // Contact sharing between tanks
    void TankShareContact(Transform enemy)
    {
        if (enemy == null) return;
        if (tankSharedContacts.Contains(enemy)) return;

        tankSharedContacts.Add(enemy);
        if (tankSharedContacts.Count > 10)
            tankSharedContacts.RemoveAt(0);

        tankContactShareTimer = 10f;  // Contacts valid for 10 seconds
    }

    Transform TankGetSharedContact()
    {
        // Clean up stale contacts
        tankContactShareTimer -= Time.deltaTime;
        if (tankContactShareTimer <= 0f)
        {
            tankSharedContacts.Clear();
            return null;
        }

        tankSharedContacts.RemoveAll(t => t == null);
        if (tankSharedContacts.Count == 0) return null;

        // Return closest shared contact
        Vector3 tankPos = drivingTank != null ? drivingTank.transform.position : transform.position;
        Transform closest = null;
        float closestDist = float.MaxValue;

        foreach (var contact in tankSharedContacts)
        {
            float dist = Vector3.Distance(tankPos, contact.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = contact;
            }
        }

        return closest;
    }

    // ==================== TANK NAVIGATION METHODS ====================

    // Record stuck spots to avoid
    void TankRecordStuckSpot(Vector3 position)
    {
        foreach (var spot in tankStuckSpots)
        {
            if (Vector3.Distance(spot, position) < TANK_STUCK_SPOT_RADIUS)
                return;
        }

        tankStuckSpots.Add(position);
        if (tankStuckSpots.Count > TANK_MAX_STUCK_SPOTS)
            tankStuckSpots.RemoveAt(0);

        Debug.Log($"[Tank AI] Recorded stuck spot at {position}");
    }

    bool TankIsNearStuckSpot(Vector3 position)
    {
        foreach (var spot in tankStuckSpots)
        {
            if (Vector3.Distance(spot, position) < TANK_STUCK_SPOT_RADIUS)
                return true;
        }
        return false;
    }

    // Check if path is wide enough for tank
    bool TankCanFitThroughPath(Vector3 from, Vector3 to)
    {
        Vector3 direction = (to - from).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        float checkDist = Vector3.Distance(from, to);

        Vector3 leftStart = from + right * (TANK_WIDTH * 0.5f) + Vector3.up * 1f;
        Vector3 rightStart = from - right * (TANK_WIDTH * 0.5f) + Vector3.up * 1f;

        bool leftClear = !Physics.Raycast(leftStart, direction, checkDist);
        bool rightClear = !Physics.Raycast(rightStart, direction, checkDist);

        return leftClear && rightClear;
    }

    // Check slope at position
    float TankGetSlopeAngle(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f))
        {
            return Vector3.Angle(hit.normal, Vector3.up);
        }
        return 0f;
    }

    bool TankSlopeIsTooSteep(Vector3 position)
    {
        return TankGetSlopeAngle(position) > TANK_MAX_SLOPE;
    }

    // Check for upcoming sharp turn
    float TankGetUpcomingTurnAngle()
    {
        if (tankPathCorners.Length < 2 || tankCurrentWaypoint >= tankPathCorners.Length - 1)
            return 0f;

        Vector3 current = tankPathCorners[tankCurrentWaypoint];
        Vector3 next = tankPathCorners[tankCurrentWaypoint + 1];

        Vector3 currentDir = (current - (drivingTank != null ? drivingTank.transform.position : transform.position)).normalized;
        Vector3 nextDir = (next - current).normalized;

        return Vector3.Angle(currentDir, nextDir);
    }

    // Get speed multiplier based on upcoming path
    float TankGetCornerSpeedMult()
    {
        float turnAngle = TankGetUpcomingTurnAngle();
        if (turnAngle > 60f) return 0.3f;
        if (turnAngle > 40f) return 0.5f;
        if (turnAngle > 20f) return 0.7f;
        return 1f;
    }

    // Smart reversing - find best direction to reverse
    Vector3 TankCalculateReverseDirection()
    {
        if (drivingTank == null) return -drivingTank.transform.forward;

        Vector3 tankPos = drivingTank.transform.position + Vector3.up * 1f;
        Vector3 bestDir = -drivingTank.transform.forward;
        float bestScore = 0f;

        // Check multiple reverse angles
        for (int angle = -60; angle <= 60; angle += 30)
        {
            Vector3 testDir = Quaternion.Euler(0, angle, 0) * -drivingTank.transform.forward;

            // Raycast to see how far we can go
            if (Physics.Raycast(tankPos, testDir, out RaycastHit hit, 20f))
            {
                if (hit.distance > bestScore && !hit.collider.transform.IsChildOf(drivingTank.transform))
                {
                    bestScore = hit.distance;
                    bestDir = testDir;
                }
            }
            else
            {
                // No hit means clear path
                bestScore = 20f;
                bestDir = testDir;
            }
        }

        return bestDir;
    }

    // Check if we're in a choke point (narrow passage)
    bool TankCheckChokePoint()
    {
        if (drivingTank == null) return false;

        Vector3 tankPos = drivingTank.transform.position + Vector3.up * 1f;
        int wallCount = 0;

        // Cast rays in 8 directions
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            if (Physics.Raycast(tankPos, dir, 15f))
            {
                wallCount++;
            }
        }

        // If more than half the directions are blocked, we're in a choke point
        return wallCount >= 5;
    }

    // Friendly avoidance - detect friendlies ahead
    bool TankFriendlyAhead(out float friendlyDist)
    {
        friendlyDist = float.MaxValue;
        if (drivingTank == null) return false;

        Vector3 tankPos = drivingTank.transform.position;
        Vector3 forward = drivingTank.transform.forward;

        // Check for friendly AI ahead
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null || ai == this || ai.isDead) continue;
                if (ai.team != team) continue;  // Not friendly
                if (ai.currentState == AIState.TankDriver) continue;  // Other tank

                Vector3 toFriendly = ai.transform.position - tankPos;
                float dist = toFriendly.magnitude;
                if (dist > 20f) continue;

                // Check if in front of us
                float angle = Vector3.Angle(forward, toFriendly);
                if (angle < 45f && dist < friendlyDist)
                {
                    friendlyDist = dist;
                }
            }
        }

        // Check for friendly players
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null || player.isDead) continue;
                if (player.playerTeam != team) continue;

                Vector3 toFriendly = player.transform.position - tankPos;
                float dist = toFriendly.magnitude;
                if (dist > 20f) continue;

                float angle = Vector3.Angle(forward, toFriendly);
                if (angle < 45f && dist < friendlyDist)
                {
                    friendlyDist = dist;
                }
            }
        }

        return friendlyDist < 15f;
    }

    // Get speed modifier based on all awareness factors
    float TankGetAwarenessSpeedMult()
    {
        float mult = 1f;

        // Slow down in choke points
        if (TankCheckChokePoint())
        {
            mult *= 0.6f;
            tankInChokePoint = true;
        }
        else
        {
            tankInChokePoint = false;
        }

        // Slow down for corners
        mult *= TankGetCornerSpeedMult();

        // Slow down in danger zones
        if (drivingTank != null && TankIsInDangerZone(drivingTank.transform.position))
        {
            mult *= 0.7f;
        }

        // Slow down near death zones
        if (drivingTank != null && TankIsNearDeathZone(drivingTank.transform.position))
        {
            mult *= 0.8f;
        }

        // Slow down if friendly ahead
        float friendlyDist;
        if (TankFriendlyAhead(out friendlyDist))
        {
            // More slowdown the closer they are
            float friendlyMult = Mathf.Clamp01(friendlyDist / 15f);
            mult *= Mathf.Max(0.2f, friendlyMult);
        }

        tankCautionSpeedMult = mult;
        return mult;
    }

    // Predictive path check - look ahead for problems
    bool TankPathHasProblems(out int problemWaypoint)
    {
        problemWaypoint = -1;
        if (tankPathCorners.Length < 2) return false;

        // Check next few waypoints
        int checkCount = Mathf.Min(4, tankPathCorners.Length - tankCurrentWaypoint);

        for (int i = 0; i < checkCount - 1; i++)
        {
            int idx = tankCurrentWaypoint + i;
            if (idx >= tankPathCorners.Length - 1) break;

            Vector3 from = tankPathCorners[idx];
            Vector3 to = tankPathCorners[idx + 1];

            // Check width
            if (!TankCanFitThroughPath(from, to))
            {
                problemWaypoint = idx;
                return true;
            }

            // Check slope
            if (TankSlopeIsTooSteep(to))
            {
                problemWaypoint = idx;
                return true;
            }

            // Check stuck spots
            if (TankIsNearStuckSpot(to))
            {
                problemWaypoint = idx;
                return true;
            }
        }

        return false;
    }

    // ==================== TANK PASSENGER METHODS ====================

    public void EnterTankAsPassenger(TankController tank)
    {
        if (tank == null) return;
        if (currentState == AIState.Dead || currentState == AIState.TankDriver || currentState == AIState.TankPassenger) return;
        if (!tank.HasVirtualPassengerSpace()) return;

        currentTank = tank;
        currentState = AIState.TankPassenger;

        // Disable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = false;
        }

        // Disable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Add as virtual passenger (this handles positioning)
        tank.AddVirtualPassenger(this);

        Debug.Log($"[AI TANK] {gameObject.name} boarded tank as passenger");
    }

    public void ExitTankAsPassenger()
    {
        if (currentTank == null) return;

        TankController tank = currentTank;

        // Remove from tank
        tank.RemoveVirtualPassenger(this);
        currentTank = null;

        // Unparent
        transform.SetParent(null);

        // Find exit position
        Vector3 exitPos = tank.transform.position + tank.transform.right * 4f;
        exitPos.y = tank.transform.position.y + 1f;

        if (Physics.Raycast(exitPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 15f))
        {
            exitPos = hit.point + Vector3.up * 0.5f;
        }

        transform.position = exitPos;

        // Re-enable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Re-enable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = true;
            agent.Warp(transform.position);
        }

        currentState = AIState.Idle;

        Debug.Log($"[AI TANK] {gameObject.name} exited tank as passenger");
    }

    void UpdateTankPassengerBehavior()
    {
        // Check if tank is destroyed or gone
        if (currentTank == null || currentTank.isDestroyed)
        {
            // Tank was destroyed - we should already be ejected by the tank
            currentTank = null;
            currentState = AIState.Idle;

            // Re-enable components
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = true;
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(transform.position);
            }
            return;
        }

        // Stay on the tank - position is managed by tank
        // Passengers can shoot at enemies while riding
        UpdateTankPassengerCombat();
    }

    void UpdateTankPassengerCombat()
    {
        if (currentTank == null) return;

        // Look for enemies to shoot at while riding
        FindNearestEnemy();

        if (targetEnemy != null)
        {
            float dist = Vector3.Distance(transform.position, targetEnemy.position);
            if (dist < attackRange)
            {
                // Face enemy
                Vector3 lookDir = targetEnemy.position - transform.position;
                lookDir.y = 0f;
                if (lookDir.magnitude > 0.1f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }

                // Shoot using existing fire logic
                if (Time.time >= nextFireTime)
                {
                    ShootAtTarget(targetEnemy);
                    nextFireTime = Time.time + fireRate;
                }
            }
        }
    }

    // Try to board a nearby tank as passenger
    public void TryBoardNearbyTank()
    {
        if (currentState == AIState.TankDriver || currentState == AIState.TankPassenger) return;
        if (currentState == AIState.HeliPilot || currentState == AIState.HeliGunner || currentState == AIState.HeliPassenger) return;

        TankController[] tanks = FindObjectsOfType<TankController>();
        TankController bestTank = null;
        float bestDist = 20f;  // Max boarding distance

        foreach (var tank in tanks)
        {
            if (tank == null || tank.isDestroyed) continue;
            if (tank.TankTeam != Team.None && tank.TankTeam != team) continue;  // Enemy tank
            if (!tank.HasVirtualPassengerSpace()) continue;  // Full

            float dist = Vector3.Distance(transform.position, tank.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTank = tank;
            }
        }

        if (bestTank != null)
        {
            EnterTankAsPassenger(bestTank);
        }
    }
}
