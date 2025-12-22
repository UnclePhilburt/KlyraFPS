using UnityEngine;
using UnityEngine.AI;

public class DogController : MonoBehaviour
{
    public enum DogState { Idle, Follow, Chase, Attack, Dead }

    [Header("Settings")]
    public Team team = Team.Phantom;
    public float followDistance = 3f;
    public float chaseRange = 100f;
    public float attackRange = 2.5f;
    public float attackDamage = 45f;
    public float attackCooldown = 0.7f;
    public float moveSpeed = 10f;
    public float runSpeed = 18f;

    [Header("Health")]
    public float maxHealth = 120f;
    public float currentHealth;

    [Header("References")]
    public AIController handler; // The AI soldier this dog follows
    public FPSControllerPhoton playerHandler; // The player this dog follows (takes priority)
    public Transform currentTarget;

    // State
    public DogState currentState = DogState.Idle;
    private NavMeshAgent agent;
    private Animator animator;
    private float lastAttackTime;
    private float stateTimer;
    private float spawnTime;

    // Animation parameters (from Animation_Dog controller)
    private const string MOVEMENT_PARAM = "Movement_f";
    private const string GROUNDED_PARAM = "Grounded_b";
    private const string ATTACK_READY_PARAM = "AttackReady_b";
    private const string ATTACK_TYPE_PARAM = "AttackType_int";
    private const string DEATH_PARAM = "Death_b";
    private const string JUMP_TRIGGER = "Jump_tr";
    private const string SIT_PARAM = "Sit_b";
    private const string ACTION_TYPE_PARAM = "ActionType_int";
    private const string TONGUE_OUT_PARAM = "Advanced_Mouth_Tongue_Out_b";
    private const string TAIL_HORIZONTAL_PARAM = "Advanced_Tail_Horizontal_f";

    // Idle behavior
    private float idleTimer = 0f;
    private float nextIdleActionTime = 5f;
    private bool isDoingIdleAction = false;
    private float idleActionEndTime = 0f;
    private float timeSinceLastRun = 0f;
    private bool isPanting = false;

    // Exploration behavior
    private bool isExploring = false;
    private Vector3 exploreTarget;
    private float exploreTimer = 0f;
    private float nextExploreTime = 10f;
    private float exploreRadius = 8f; // How far from player the dog will wander
    private Vector3 lastPlayerPosition;

    void Start()
    {
        currentHealth = maxHealth;
        spawnTime = Time.time;

        // Setup NavMeshAgent
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }
        agent.speed = moveSpeed;
        agent.stoppingDistance = 1.5f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;

        // Get animator
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // Set grounded for animations to work
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool(GROUNDED_PARAM, true);
        }

        // If no handler assigned at spawn, try to find one
        if (handler == null)
        {
            FindNearbyHandler();
        }
    }

    void Update()
    {
        if (currentState == DogState.Dead) return;

        // Debug - log state every few seconds
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[Dog] {name} state={currentState}, handler={(handler != null ? handler.name : "NULL")}, playerHandler={(playerHandler != null ? "PLAYER" : "NULL")}, hasTarget={(currentTarget != null)}");
        }

        // Update animation speed
        UpdateAnimations();

        // Random idle behaviors (sniff, poop, bark, etc.)
        UpdateIdleBehaviors();

        switch (currentState)
        {
            case DogState.Idle:
                UpdateIdle();
                break;
            case DogState.Follow:
                UpdateFollow();
                break;
            case DogState.Chase:
                UpdateChase();
                break;
            case DogState.Attack:
                UpdateAttack();
                break;
        }

        // Scan for nearby enemies to attack
        ScanForEnemies();
    }

    void UpdateAnimations()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        // Set movement based on agent velocity
        float speed = agent.velocity.magnitude / runSpeed;
        animator.SetFloat(MOVEMENT_PARAM, speed);

        // Track running for panting
        if (speed > 0.5f)
        {
            timeSinceLastRun = 0f;
            if (!isPanting && speed > 0.8f)
            {
                // Start panting after running
                isPanting = true;
                animator.SetBool(TONGUE_OUT_PARAM, true);
            }
        }
        else
        {
            timeSinceLastRun += Time.deltaTime;
            // Stop panting after resting for 10 seconds
            if (isPanting && timeSinceLastRun > 10f)
            {
                isPanting = false;
                animator.SetBool(TONGUE_OUT_PARAM, false);
            }
        }

        // Wag tail when near player
        if (playerHandler != null)
        {
            float distToPlayer = Vector3.Distance(transform.position, playerHandler.transform.position);
            float wagAmount = distToPlayer < 5f ? Mathf.Sin(Time.time * 8f) * 0.5f : 0f;
            animator.SetFloat(TAIL_HORIZONTAL_PARAM, wagAmount);
        }
    }

    void UpdateIdleBehaviors()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (currentState != DogState.Follow) return;
        if (agent.velocity.magnitude > 0.5f)
        {
            // Moving - reset idle timer and clear any idle action
            idleTimer = 0f;
            if (isDoingIdleAction)
            {
                EndIdleAction();
            }
            return;
        }

        // Check if current idle action is done
        if (isDoingIdleAction)
        {
            if (Time.time >= idleActionEndTime)
            {
                EndIdleAction();
            }
            return;
        }

        // Count up idle time
        idleTimer += Time.deltaTime;

        // Trigger random idle action
        if (idleTimer >= nextIdleActionTime)
        {
            DoRandomIdleAction();
            idleTimer = 0f;
            nextIdleActionTime = Random.Range(8f, 20f); // Next action in 8-20 seconds
        }
    }

    void DoRandomIdleAction()
    {
        // ActionType_int values for standing actions:
        // 1 = Eat, 2 = Drink, 3 = Cower, 4 = Dig, 5 = Peeing, 6 = Pooping
        // 7 = Howl, 8 = TailWag, 9 = ShakeToy, 10 = Sniff, 11 = Shake
        // 12 = Beg, 13 = Bark, 14 = Yawn

        int[] funnyActions = { 5, 6, 10, 11, 13, 14, 8 }; // Peeing, Pooping, Sniff, Shake, Bark, Yawn, TailWag
        float[] actionDurations = { 3f, 4f, 2f, 2f, 1.5f, 2f, 2f };

        int randomIndex = Random.Range(0, funnyActions.Length);
        int actionType = funnyActions[randomIndex];
        float duration = actionDurations[randomIndex];

        // Pooping is rare but funny - give it lower chance but keep it possible
        if (actionType == 6 && Random.value > 0.3f)
        {
            // 70% chance to re-roll if pooping was selected
            randomIndex = Random.Range(0, funnyActions.Length);
            actionType = funnyActions[randomIndex];
            duration = actionDurations[randomIndex];
        }

        animator.SetInteger(ACTION_TYPE_PARAM, actionType);
        isDoingIdleAction = true;
        idleActionEndTime = Time.time + duration;

        Debug.Log($"[Dog] {name} doing idle action: {actionType}");
    }

    void EndIdleAction()
    {
        if (animator != null)
        {
            animator.SetInteger(ACTION_TYPE_PARAM, 0);
        }
        isDoingIdleAction = false;
    }

    void UpdateIdle()
    {
        // If we have a player handler or AI handler, follow them
        if (playerHandler != null || handler != null)
        {
            SetState(DogState.Follow);
        }
        else
        {
            // Keep looking for a handler (only AI dogs without player handlers)
            stateTimer += Time.deltaTime;
            if (stateTimer > 2f)
            {
                stateTimer = 0f;
                FindNearbyHandler();
            }
        }
    }

    void UpdateFollow()
    {
        Transform followTarget = null;
        bool isPlayerHandler = false;

        // Player handler takes priority
        if (playerHandler != null)
        {
            // Check if player is dead or in vehicle
            if (playerHandler.isDead)
            {
                // Wait for player to respawn - just stay idle near last position
                agent.ResetPath();
                return;
            }

            if (playerHandler.IsInVehicle)
            {
                // Player is in vehicle - stay put and wait
                agent.ResetPath();
                return;
            }

            followTarget = playerHandler.transform;
            isPlayerHandler = true;
        }
        else if (handler != null)
        {
            // AI handler logic
            // If handler got into a vehicle or died, immediately find a new handler
            if (handler.currentState == AIController.AIState.TankDriver ||
                handler.currentState == AIController.AIState.TankPassenger ||
                handler.currentState == AIController.AIState.HumveeDriver ||
                handler.currentState == AIController.AIState.HumveeGunner ||
                handler.currentState == AIController.AIState.HumveePassenger ||
                handler.currentState == AIController.AIState.HeliPilot ||
                handler.currentState == AIController.AIState.HeliGunner ||
                handler.currentState == AIController.AIState.HeliPassenger ||
                handler.currentState == AIController.AIState.JetPilot ||
                handler.currentState == AIController.AIState.BoardingHelicopter ||
                handler.currentState == AIController.AIState.Dead)
            {
                handler = null;
                FindNearbyHandler();
                if (handler == null)
                {
                    SetState(DogState.Idle);
                }
                return;
            }

            followTarget = handler.transform;
        }
        else
        {
            SetState(DogState.Idle);
            return;
        }

        // For player's dog, try to stay in front (slightly to the right so not blocking view)
        Vector3 targetPosition;
        if (isPlayerHandler)
        {
            // Position in front and slightly to the right of player
            Vector3 frontOffset = followTarget.forward * followDistance;
            Vector3 rightOffset = followTarget.right * 1.5f;
            targetPosition = followTarget.position + frontOffset + rightOffset;
        }
        else
        {
            // AI handlers - just follow behind
            targetPosition = followTarget.position;
        }

        float distToTarget = Vector3.Distance(transform.position, targetPosition);
        float distToHandler = Vector3.Distance(transform.position, followTarget.position);

        // Check if player is moving
        bool playerIsMoving = false;
        if (isPlayerHandler)
        {
            float playerMovement = Vector3.Distance(followTarget.position, lastPlayerPosition);
            playerIsMoving = playerMovement > 0.1f;
            lastPlayerPosition = followTarget.position;
        }

        // If player is moving or we're too far, stop exploring and follow
        if (playerIsMoving || distToHandler > exploreRadius * 1.5f)
        {
            if (isExploring)
            {
                isExploring = false;
                exploreTimer = 0f;
            }
        }

        // If too far from handler, run to catch up
        if (distToHandler > followDistance * 3)
        {
            isExploring = false;
            agent.speed = runSpeed;
            agent.SetDestination(targetPosition);
            return;
        }

        // Exploration behavior for player's dog when player is idle
        if (isPlayerHandler && !playerIsMoving && !isDoingIdleAction)
        {
            exploreTimer += Time.deltaTime;

            if (isExploring)
            {
                // Check if we reached explore target or got close enough
                float distToExplore = Vector3.Distance(transform.position, exploreTarget);
                if (distToExplore < 1f)
                {
                    // Reached target, stop and sniff
                    agent.ResetPath();
                    isExploring = false;
                    exploreTimer = 0f;
                    nextExploreTime = Random.Range(5f, 12f);

                    // Trigger sniff animation
                    if (animator != null)
                    {
                        animator.SetInteger(ACTION_TYPE_PARAM, 10); // Sniff
                        isDoingIdleAction = true;
                        idleActionEndTime = Time.time + 2f;
                    }
                }
                return;
            }

            // Start exploring after being idle for a while
            if (exploreTimer >= nextExploreTime)
            {
                // Pick random point near player
                Vector2 randomCircle = Random.insideUnitCircle * exploreRadius;
                exploreTarget = followTarget.position + new Vector3(randomCircle.x, 0, randomCircle.y);

                // Make sure it's on the navmesh
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(exploreTarget, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    exploreTarget = hit.position;
                    isExploring = true;
                    agent.speed = moveSpeed * 0.7f; // Walk slowly while exploring
                    agent.SetDestination(exploreTarget);
                }
                else
                {
                    // Couldn't find valid position, try again later
                    exploreTimer = 0f;
                    nextExploreTime = Random.Range(3f, 6f);
                }
            }
            return;
        }

        // Normal following behavior
        agent.speed = moveSpeed;

        // Move to target position if not already there
        if (distToTarget > 1.5f)
        {
            agent.SetDestination(targetPosition);
        }
        else
        {
            agent.ResetPath();

            // Face the same direction as player when idle in front
            if (isPlayerHandler)
            {
                Vector3 lookDir = followTarget.forward;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 5f);
                }
            }
        }
    }

    void UpdateChase()
    {
        if (currentTarget == null || IsTargetDead())
        {
            currentTarget = null;
            SetState((playerHandler != null || handler != null) ? DogState.Follow : DogState.Idle);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, currentTarget.position);

        // If in attack range, attack
        if (distToTarget <= attackRange)
        {
            SetState(DogState.Attack);
            return;
        }

        // If target too far, give up (but chase pretty far before giving up)
        if (distToTarget > chaseRange * 2f)
        {
            currentTarget = null;
            SetState((playerHandler != null || handler != null) ? DogState.Follow : DogState.Idle);
            return;
        }

        // Chase at run speed
        agent.speed = runSpeed;
        agent.SetDestination(currentTarget.position);
    }

    void UpdateAttack()
    {
        if (currentTarget == null || IsTargetDead())
        {
            currentTarget = null;
            SetState((playerHandler != null || handler != null) ? DogState.Follow : DogState.Idle);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, currentTarget.position);

        // If target moved out of range, chase
        if (distToTarget > attackRange * 1.5f)
        {
            SetState(DogState.Chase);
            return;
        }

        // Face target
        Vector3 lookDir = (currentTarget.position - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);
        }

        // Stop moving
        agent.ResetPath();

        // Attack if cooldown ready
        if (Time.time - lastAttackTime >= attackCooldown)
        {
            PerformAttack();
        }
    }

    void PerformAttack()
    {
        lastAttackTime = Time.time;

        // Play attack animation
        if (animator != null)
        {
            animator.SetBool(ATTACK_READY_PARAM, true);
            animator.SetInteger(ATTACK_TYPE_PARAM, Random.Range(1, 3));
        }

        // Deal damage
        if (currentTarget != null)
        {
            // Check if it's an AI
            AIController targetAI = currentTarget.GetComponent<AIController>();
            if (targetAI != null)
            {
                targetAI.TakeDamage(attackDamage, transform.position, currentTarget.position, gameObject);
            }

            // Check if it's a player
            FPSControllerPhoton targetPlayer = currentTarget.GetComponent<FPSControllerPhoton>();
            if (targetPlayer != null)
            {
                targetPlayer.TakeDamageFromAI(attackDamage, transform.position);
            }
        }

        // Reset attack animation after a moment
        Invoke(nameof(ResetAttackAnim), 0.5f);
    }

    void ResetAttackAnim()
    {
        if (animator != null)
        {
            animator.SetBool(ATTACK_READY_PARAM, false);
            animator.SetInteger(ATTACK_TYPE_PARAM, 0);
        }
    }

    void ScanForEnemies()
    {
        // Don't scan if already attacking or chasing
        if (currentState == DogState.Attack || currentState == DogState.Chase) return;

        // Wait 3 seconds after spawn before hunting (let players initialize)
        if (Time.time - spawnTime < 3f) return;

        // Find nearest enemy - aggressive attack dog mode
        Transform nearestEnemy = null;
        float nearestDist = chaseRange;

        // Check AI enemies
        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        foreach (var ai in allAI)
        {
            if (ai.team == team) continue; // Skip teammates
            if (ai.currentState == AIController.AIState.Dead) continue;

            float dist = Vector3.Distance(transform.position, ai.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestEnemy = ai.transform;
            }
        }

        // Check players
        FPSControllerPhoton[] allPlayers = FindObjectsByType<FPSControllerPhoton>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player.playerTeam == Team.None) continue; // Skip uninitialized players
            if (player.playerTeam == team) continue; // Skip teammates

            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestEnemy = player.transform;
            }
        }

        // If found enemy very close, chase!
        if (nearestEnemy != null)
        {
            currentTarget = nearestEnemy;
            SetState(DogState.Chase);
        }
    }

    void SetState(DogState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        stateTimer = 0f;
    }

    bool IsTargetDead()
    {
        if (currentTarget == null) return true;

        // Check if AI is dead
        AIController targetAI = currentTarget.GetComponent<AIController>();
        if (targetAI != null && targetAI.currentState == AIController.AIState.Dead)
        {
            return true;
        }

        // Check if player is dead
        FPSControllerPhoton targetPlayer = currentTarget.GetComponent<FPSControllerPhoton>();
        if (targetPlayer != null && targetPlayer.isDead)
        {
            return true;
        }

        return false;
    }

    void FindNearbyHandler()
    {
        // Don't reassign player dogs to AI handlers
        if (playerHandler != null) return;

        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        DogController[] allDogs = FindObjectsByType<DogController>(FindObjectsSortMode.None);
        float nearestDist = 500f;

        Debug.Log($"[DogController] {name} FindNearbyHandler - Found {allAI.Length} AI, {allDogs.Length} dogs, my team: {team}");

        foreach (var ai in allAI)
        {
            if (ai.team != team) continue;
            if (ai.currentState == AIController.AIState.Dead) continue;

            // Skip AI in vehicles - only follow infantry
            if (ai.currentState == AIController.AIState.TankDriver ||
                ai.currentState == AIController.AIState.TankPassenger ||
                ai.currentState == AIController.AIState.HumveeDriver ||
                ai.currentState == AIController.AIState.HumveeGunner ||
                ai.currentState == AIController.AIState.HumveePassenger ||
                ai.currentState == AIController.AIState.HeliPilot ||
                ai.currentState == AIController.AIState.HeliGunner ||
                ai.currentState == AIController.AIState.HeliPassenger ||
                ai.currentState == AIController.AIState.JetPilot ||
                ai.currentState == AIController.AIState.BoardingHelicopter) continue;

            // Check if this AI already has a dog assigned
            bool alreadyTaken = false;
            foreach (var dog in allDogs)
            {
                if (dog != this && dog.handler == ai)
                {
                    alreadyTaken = true;
                    break;
                }
            }
            if (alreadyTaken) continue;

            float dist = Vector3.Distance(transform.position, ai.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                handler = ai;
            }
        }

        Debug.Log($"[DogController] {name} FindNearbyHandler result: {(handler != null ? handler.name : "NONE")}");
    }

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker = null)
    {
        if (currentState == DogState.Dead) return;

        currentHealth -= damage;

        // If attacker is enemy, target them
        if (attacker != null)
        {
            AIController attackerAI = attacker.GetComponent<AIController>();
            FPSControllerPhoton attackerPlayer = attacker.GetComponent<FPSControllerPhoton>();

            if ((attackerAI != null && attackerAI.team != team) ||
                (attackerPlayer != null && attackerPlayer.playerTeam != team))
            {
                currentTarget = attacker.transform;
                SetState(DogState.Chase);
            }
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        SetState(DogState.Dead);
        agent.enabled = false;

        if (animator != null)
        {
            animator.SetBool(DEATH_PARAM, true);
        }

        // Disable after delay
        Invoke(nameof(DisableDog), 5f);
    }

    void DisableDog()
    {
        gameObject.SetActive(false);
    }

    // Called by handler to command attack
    public void CommandAttack(Transform target)
    {
        if (currentState == DogState.Dead) return;
        currentTarget = target;
        SetState(DogState.Chase);
    }

    // Called by handler to command return
    public void CommandReturn()
    {
        if (currentState == DogState.Dead) return;
        currentTarget = null;
        SetState(DogState.Follow);
    }

    void OnDrawGizmosSelected()
    {
        // Draw chase range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        // Draw attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw follow distance
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, followDistance);
    }
}
