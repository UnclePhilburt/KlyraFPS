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
    public enum OrderType { FollowLeader, DefendPoint, CapturePoint, HoldPosition }
    public OrderType currentOrder = OrderType.FollowLeader;
    public CapturePoint orderedPoint;
    public Vector3 holdPosition;
    public Vector3 holdFacingDirection; // Direction to face when holding position

    [Header("Skins")]
    public string[] phantomSkinNames = { "SM_Chr_Soldier_Male_01", "SM_Chr_Soldier_Male_02" };
    public string[] havocSkinNames = { "SM_Chr_Insurgent_Male_01", "SM_Chr_Insurgent_Male_04" };

    // State machine
    public enum AIState { Idle, MovingToPoint, Capturing, Combat, Dead }
    public AIState currentState = AIState.Idle;

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
            Debug.Log($"Found {allCapturePoints.Count} capture points");
        }

        // Make sure we're on the NavMesh
        if (agent != null)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                Debug.Log($"{gameObject.name} warped to NavMesh at {hit.position}");
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

        AssignToSquad();
        FindNextCapturePoint();

        Debug.Log($"AI Initialized: {identity.RankAndName} on team {team}, state={currentState}");
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
        if (skinNames.Length > 0)
        {
            string chosenSkin = skinNames[Random.Range(0, skinNames.Length)];

            // Find and enable the chosen skin
            foreach (Transform child in allChildren)
            {
                if (child.name == chosenSkin)
                {
                    child.gameObject.SetActive(true);
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

        // Handle player-led squad behavior
        if (inPlayerSquad)
        {
            UpdatePlayerSquadBehavior();
            return; // Skip normal AI behavior when in player squad
        }

        // Update global frame counter (shared across all bots)
        globalFrameCount++;

        // Update global cache once per second (not per bot!)
        globalCacheTimer -= Time.deltaTime;
        if (globalCacheTimer <= 0f)
        {
            cachedPlayers = FindObjectsOfType<FPSControllerPhoton>();
            cachedAIs = FindObjectsOfType<AIController>();
            globalCacheTimer = 1f;
        }

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
                    Debug.Log($"{gameObject.name} ({team}) ENGAGING AI {ai.gameObject.name} ({ai.team}) at {dist:F1}m!");
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
                        Debug.Log($"{gameObject.name} ({team}) ENGAGING PLAYER {player.gameObject.name} ({player.playerTeam}) at {dist:F1}m!");
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
            FindNextCapturePoint();
        }

        // Update target enemy every think cycle
        FindNearestEnemy();

        // Immediately check for nearby enemies and enter combat
        if (targetEnemy != null && currentState != AIState.Combat)
        {
            float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
            if (enemyDist <= attackRange)
            {
                Debug.Log($"{gameObject.name} ({team}) entering combat with enemy at {enemyDist:F1}m");
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
            // Make sure we're on the NavMesh first
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas))
            {
                agent.enabled = true;
                agent.Warp(hit.position);
                agentEnabled = true;
            }
            else
            {
                Debug.LogWarning($"{gameObject.name} not on NavMesh, can't navigate!");
                return;
            }
        }

        // Make sure destination is on NavMesh
        NavMeshHit destHit;
        Vector3 finalDest = destination;
        if (NavMesh.SamplePosition(destination, out destHit, 5f, NavMesh.AllAreas))
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
            agent.ResetPath();
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

    void MoveTowardTarget()
    {
        if (currentState == AIState.Capturing || currentState == AIState.Dead)
        {
            StopAgent();
            UpdateAnimation(false);
            return;
        }

        // NavMeshAgent handles movement - we just update animation
        if (agent != null && agentEnabled)
        {
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
            // Our points
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

            Debug.Log($"{gameObject.name} heading to {bestPoint.pointName}");
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

        // Disable NavMeshAgent during combat for manual strafing control
        if (agentEnabled)
        {
            StopAgent();
        }

        // Decrement timers every frame
        suppressedTimer -= Time.deltaTime;
        strafeTimer -= Time.deltaTime;
        reloadTimer -= Time.deltaTime;

        float enemyDist = Vector3.Distance(transform.position, targetEnemy.position);
        float healthPercent = currentHealth / maxHealth;

        // === FACING - every frame for smooth aiming ===
        Vector3 lookDir = (targetEnemy.position - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * 2f * Time.deltaTime);
        }

        // === TACTICAL MOVEMENT ===
        Vector3 moveDir = Vector3.zero;
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
            // Being shot at - take cover or retreat
            if (healthPercent < 0.5f)
            {
                moveDir = -transform.forward * 0.5f + transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
            else
            {
                moveDir = transform.right * strafeDirection;
                isMoving = true;
            }
        }
        else if (enemyDist > attackRange * 0.8f)
        {
            // Too far - advance or hold
            if (personality == Personality.Aggressive || (healthPercent > 0.7f && bravery > 0.5f))
            {
                moveDir = transform.forward * 0.7f + transform.right * strafeDirection * 0.3f;
                isMoving = true;
            }
            else
            {
                moveDir = transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
        }
        else if (enemyDist < attackRange * 0.3f)
        {
            if (personality != Personality.Aggressive)
            {
                moveDir = -transform.forward * 0.5f + transform.right * strafeDirection * 0.5f;
                isMoving = true;
            }
        }
        else
        {
            moveDir = transform.right * strafeDirection * 0.7f;
            isMoving = Random.value > 0.3f;
        }

        // Apply movement
        if (isMoving && moveDir != Vector3.zero)
        {
            Vector3 newPos = transform.position + moveDir.normalized * moveSpeed * 0.6f * Time.deltaTime;

            // Check for obstacles at multiple heights (catches poles)
            bool blocked = false;
            float[] heights = { 0.3f, 0.8f, 1.3f };
            foreach (float h in heights)
            {
                if (Physics.SphereCast(transform.position + Vector3.up * h, 0.25f, moveDir.normalized, out _, 1f))
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                // Keep AI on the NavMesh/ground during combat strafing
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(newPos, out navHit, 2f, NavMesh.AllAreas))
                {
                    transform.position = navHit.position;
                }
                else
                {
                    // Fallback: raycast to find ground
                    RaycastHit groundHit;
                    if (Physics.Raycast(newPos + Vector3.up * 2f, Vector3.down, out groundHit, 5f))
                    {
                        transform.position = groundHit.point;
                    }
                    // else don't move - no valid ground
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

        // === SHOOTING ===
        if (burstCount >= 8)
        {
            burstCount = 0;
            reloadTimer = Random.Range(1.5f, 2.5f);
        }

        float facingAngle = Vector3.Angle(transform.forward, lookDir);

        if (reloadTimer <= 0f && enemyDist <= attackRange && Time.time >= nextFireTime)
        {
            if (facingAngle < 45f) // Increased from 30 to 45 degrees
            {
                Shoot();
                nextFireTime = Time.time + fireRate * Random.Range(0.8f, 1.2f);
            }
        }

        // Debug: log why we're not shooting (only occasionally)
        if (Random.value < 0.01f)
        {
            string reason = "";
            if (reloadTimer > 0f) reason = $"reloading ({reloadTimer:F1}s)";
            else if (enemyDist > attackRange) reason = $"too far ({enemyDist:F1}m > {attackRange}m)";
            else if (Time.time < nextFireTime) reason = $"fire cooldown";
            else if (facingAngle >= 45f) reason = $"not facing ({facingAngle:F0}°)";
            else reason = "SHOOTING";

            Debug.Log($"{gameObject.name} combat: target={targetEnemy?.name}, dist={enemyDist:F1}m, facing={facingAngle:F0}°, {reason}");
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

        // If enemy too far, go back to objective
        float effectiveRange = detectionRange * (0.7f + patience * 0.3f);
        if (enemyDist > effectiveRange)
        {
            targetEnemy = null;
            currentState = AIState.MovingToPoint;
            if (targetPoint != null)
                SetDestination(targetPoint.transform.position);
            return;
        }
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
            Debug.Log($"{gameObject.name} ({team}) found {enemiesInRange} enemies, targeting {targetEnemy.name}");
        }
    }

    void Shoot()
    {
        if (targetEnemy == null) return;

        // Muzzle position (approximate - in front of character)
        Vector3 muzzlePos = transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;

        // Target position with some spread based on accuracy
        Vector3 targetPos = targetEnemy.position + Vector3.up * 1f;
        float spread = (1f - accuracy) * 2f;
        targetPos += new Vector3(
            Random.Range(-spread, spread),
            Random.Range(-spread * 0.5f, spread * 0.5f),
            Random.Range(-spread, spread)
        );

        float dist = Vector3.Distance(transform.position, targetEnemy.position);

        // Play gunshot sound
        if (gunshotSound != null && audioSource != null)
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(gunshotSound, gunshotVolume);
        }

        // Show tracer
        if (tracerLine != null)
        {
            StartCoroutine(ShowTracer(muzzlePos, targetPos));
        }

        // Simple hit chance based on distance
        float hitChance = accuracy * (1f - (dist / attackRange) * 0.5f);
        bool didHit = Random.value < hitChance;

        if (didHit)
        {
            // Check if target is player
            FPSControllerPhoton player = targetEnemy.GetComponent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != team)
            {
                // Apply damage to player
                player.TakeDamageFromAI(damage, transform.position);
                Debug.Log($"{gameObject.name} hit player {player.photonView.ViewID} for {damage} damage!");
            }

            // Check if target is AI
            AIController ai = targetEnemy.GetComponent<AIController>();
            if (ai != null && ai.team != team)
            {
                ai.TakeDamage(damage, transform.position, targetPos);

                // Check if we killed them
                if (ai.currentHealth <= 0)
                {
                    killStreak++;
                    UpdateConfidence(0.15f);
                }
            }
        }

        burstCount++;
    }

    System.Collections.IEnumerator ShowTracer(Vector3 start, Vector3 end)
    {
        tracerLine.SetPosition(0, start);
        tracerLine.SetPosition(1, end);
        tracerLine.enabled = true;

        yield return new WaitForSeconds(tracerDuration);

        tracerLine.enabled = false;
    }

    public void TakeDamage(float amount, Vector3 damageSource = default, Vector3 hitPoint = default)
    {
        currentHealth -= amount;
        lastDamageTime = Time.time;
        suppressedTimer = 1f; // Suppressed for 1 second

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

    // Property to check if dead
    public bool isDead => currentState == AIState.Dead;

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

        Debug.Log($"{identity?.RankAndName ?? gameObject.name} joined player squad at position {squadIndex}!");
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
        Debug.Log($"{identity?.RankAndName ?? gameObject.name} received order: {order}");
    }

    public void LeaveSquad()
    {
        inPlayerSquad = false;
        playerSquadLeader = null;
        squadFormationOffset = 0f;

        Debug.Log($"{gameObject.name} left player squad.");
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
                hitAI.TakeDamage(damage, transform.position, hit.point);
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
}
