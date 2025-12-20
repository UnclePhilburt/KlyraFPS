using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Vehicle-specific navigation system for tanks.
/// Uses a properly configured NavMeshAgent with tank dimensions.
/// Attach this to the tank prefab.
/// </summary>
[RequireComponent(typeof(TankController))]
public class TankNavigation : MonoBehaviour
{
    [Header("Vehicle Dimensions")]
    public float tankWidth = 4f;          // Width of the tank
    public float tankLength = 6f;         // Length of the tank
    public float tankHeight = 2.5f;       // Height of the tank
    public float turnRadius = 8f;         // Minimum turning radius

    [Header("Navigation Settings")]
    public float stoppingDistance = 5f;   // Distance to stop from target
    public float slowdownDistance = 15f;  // Distance to start slowing down
    public float maxSpeed = 8f;           // Maximum movement speed
    public float acceleration = 3f;       // Acceleration rate
    public float turnSpeed = 45f;         // Degrees per second

    [Header("Path Settings")]
    public float pathUpdateInterval = 0.5f;  // How often to update path
    public float waypointReachDistance = 3f; // Distance to consider waypoint reached - tight for strict paths
    public int pathSmoothingIterations = 2;  // How much to smooth the path

    [Header("Path Variation")]
    [Tooltip("Random offset added to paths so tanks don't follow identical routes")]
    public float pathRandomization = 5f;
    [Tooltip("Random offset for destination")]
    public float destinationRandomization = 3f;
    private Vector3 pathOffset; // Unique offset for this tank

    // Stuck detection
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 1f;
    private float lastStuckCheck = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;
    private float reverseTurnDir = 1f;

    [Header("Waypoint Navigation")]
    [Tooltip("Use waypoint system instead of NavMesh")]
    public bool useWaypoints = true;
    private TankWaypoint currentWaypoint;
    private TankWaypoint targetWaypoint;

    [Header("Obstacle Avoidance")]
    public float obstacleDetectionRange = 15f;
    public float avoidanceStrength = 1f;
    public LayerMask obstacleLayer = -1;

    [Header("Obstacle Crushing")]
    [Tooltip("Obstacles shorter than this will be ignored (tank can run over them)")]
    public float crushableObstacleHeight = 1.5f;

    [Header("Debug")]
    public bool showDebugPath = true;
    public bool showDebugInfo = false;

    // Components
    private TankController tankController;
    private NavMeshAgent agent;

    // Path state
    private List<Vector3> currentPath = new List<Vector3>();
    private int currentWaypointIndex = 0;
    private Vector3 currentDestination;
    private bool hasDestination = false;
    private float pathUpdateTimer = 0f;

    // Movement state
    private float currentSpeed = 0f;
    private Vector3 currentVelocity = Vector3.zero;

    // Callbacks
    public System.Action OnDestinationReached;
    public System.Action OnPathFailed;

    // Public properties
    public bool HasPath => currentPath.Count > 0 && currentWaypointIndex < currentPath.Count;
    public bool IsMoving => hasDestination && currentSpeed > 0.1f;
    public Vector3 Destination => currentDestination;
    public float RemainingDistance => hasDestination ? Vector3.Distance(transform.position, currentDestination) : 0f;

    void Awake()
    {
        tankController = GetComponent<TankController>();
        SetupNavMeshAgent();

        // Sync crushable height with TankController
        if (tankController != null)
        {
            crushableObstacleHeight = tankController.crushableMaxHeight;
        }

        // Generate unique path offset for this tank (so tanks don't all follow same path)
        pathOffset = new Vector3(
            Random.Range(-pathRandomization, pathRandomization),
            0f,
            Random.Range(-pathRandomization, pathRandomization)
        );

        // Randomize update interval slightly so tanks don't all update at same time
        pathUpdateInterval += Random.Range(-0.2f, 0.2f);

        // Fix obstacle layer - exclude ground, terrain, and vehicles
        // If set to -1 (everything), configure properly
        if (obstacleLayer.value == -1 || obstacleLayer.value == 0)
        {
            // Detect everything except common ground/vehicle layers
            int groundLayer = LayerMask.NameToLayer("Ground");
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            int waterLayer = LayerMask.NameToLayer("Water");
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

            // Start with all layers
            int mask = ~0;

            // Exclude layers that exist
            if (groundLayer >= 0) mask &= ~(1 << groundLayer);
            if (terrainLayer >= 0) mask &= ~(1 << terrainLayer);
            if (vehicleLayer >= 0) mask &= ~(1 << vehicleLayer);
            if (waterLayer >= 0) mask &= ~(1 << waterLayer);
            if (ignoreRaycastLayer >= 0) mask &= ~(1 << ignoreRaycastLayer);

            // Also exclude own layer
            mask &= ~(1 << gameObject.layer);

            obstacleLayer = mask;
            Debug.Log($"[TankNav] Auto-configured obstacleLayer mask: {obstacleLayer.value}");
        }
    }

    void SetupNavMeshAgent()
    {
        // Get or create NavMeshAgent
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        // Configure for vehicle dimensions
        agent.radius = tankWidth / 2f;
        agent.height = tankHeight;
        agent.baseOffset = 0f;

        // We'll handle movement ourselves - agent is just for pathfinding
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        // Path settings
        agent.stoppingDistance = stoppingDistance;
        agent.autoBraking = true;
        agent.autoRepath = true;

        // Avoidance (for pathfinding, not runtime)
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = 50;

        // Speed settings (for path calculation)
        agent.speed = maxSpeed;
        agent.angularSpeed = turnSpeed;
        agent.acceleration = acceleration;

        Debug.Log($"[TankNav] NavMeshAgent configured: radius={agent.radius}, height={agent.height}");
    }

    void Update()
    {
        // Use waypoint system if enabled and waypoints exist
        if (useWaypoints && TankWaypoint.AllWaypoints.Count > 0)
        {
            UpdateWaypointNavigation();
            return;
        }

        // Log state every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"[TankNav] {gameObject.name}: hasDestination={hasDestination}, HasPath={HasPath}, pathPoints={currentPath.Count}, waypointIdx={currentWaypointIndex}");
        }

        if (!hasDestination) return;

        // Sync agent position with tank (for path recalculation)
        agent.nextPosition = transform.position;

        // Update path periodically
        pathUpdateTimer -= Time.deltaTime;
        if (pathUpdateTimer <= 0f)
        {
            pathUpdateTimer = pathUpdateInterval;
            UpdatePath();
        }

        // Follow path
        if (HasPath)
        {
            FollowPath();
        }
    }

    void UpdateWaypointNavigation()
    {
        Vector3 currentPos = transform.position;

        // Find nearest waypoint if we don't have one
        if (currentWaypoint == null)
        {
            currentWaypoint = TankWaypoint.FindNearest(currentPos, tankController?.tankTeam ?? Team.None);
            if (currentWaypoint == null)
            {
                Debug.LogWarning("[TankNav] No waypoints found!");
                return;
            }
            Debug.Log($"[TankNav] Found nearest waypoint: {currentWaypoint.name}");
        }

        // Check if we reached current waypoint (use minimum of 8m so tanks don't get stuck)
        float distToWaypoint = Vector3.Distance(currentPos, currentWaypoint.transform.position);
        float reachDist = Mathf.Max(currentWaypoint.reachRadius, 8f);
        if (distToWaypoint < reachDist)
        {
            // Pick next waypoint
            TankWaypoint next = null;

            // If we have a destination, head toward it
            if (hasDestination)
            {
                next = currentWaypoint.GetConnectionToward(currentDestination);

                // Check if we're close enough to destination
                float distToDest = Vector3.Distance(currentPos, currentDestination);
                if (distToDest < stoppingDistance)
                {
                    hasDestination = false;
                    currentSpeed = 0f;
                    tankController?.SetAIInput(0f, 0f, 0f, false);
                    OnDestinationReached?.Invoke();
                    return;
                }
            }
            else
            {
                // No destination - pick random connected waypoint
                next = currentWaypoint.GetRandomConnection();
            }

            if (next != null)
            {
                currentWaypoint = next;
                Debug.Log($"[TankNav] Moving to waypoint: {currentWaypoint.name}");
            }
        }

        // Drive toward current waypoint
        DriveTowardWaypoint();
    }

    void DriveTowardWaypoint()
    {
        if (currentWaypoint == null || tankController == null) return;

        Vector3 currentPos = transform.position;

        // Stuck detection - check if tank hasn't moved
        if (Time.time - lastStuckCheck > stuckCheckInterval)
        {
            float movedDist = Vector3.Distance(currentPos, lastPosition);
            if (movedDist < 0.5f && currentSpeed > 0.1f) // Should be moving but isn't
            {
                stuckTimer += stuckCheckInterval;
                if (stuckTimer > 2f && !isReversing)
                {
                    // Tank is stuck - start reversing
                    isReversing = true;
                    reverseTimer = 2f;
                    reverseTurnDir = Random.value > 0.5f ? 1f : -1f;
                    Debug.Log("[TankNav] Stuck! Reversing...");
                }
            }
            else
            {
                stuckTimer = 0f;
            }
            lastPosition = currentPos;
            lastStuckCheck = Time.time;
        }

        // If reversing, back up and turn
        if (isReversing)
        {
            reverseTimer -= Time.deltaTime;
            if (reverseTimer <= 0f)
            {
                isReversing = false;
                stuckTimer = 0f;
            }
            else
            {
                // Reverse and turn
                tankController.SetAIInput(-0.4f, reverseTurnDir * 0.8f, 0f, false);
                return;
            }
        }

        Vector3 targetPos = currentWaypoint.transform.position;
        Vector3 toTarget = targetPos - currentPos;
        toTarget.y = 0;

        float distToTarget = toTarget.magnitude;
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        float angleToTarget = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);

        // Calculate speed
        float desiredSpeed = maxSpeed;

        // Apply speed limit from waypoint
        if (currentWaypoint.speedLimit > 0)
        {
            desiredSpeed = Mathf.Min(desiredSpeed, currentWaypoint.speedLimit);
        }

        // Slow down for sharp turns only
        if (Mathf.Abs(angleToTarget) > 45f)
        {
            float turnFactor = 1f - Mathf.Clamp01((Mathf.Abs(angleToTarget) - 45f) / 90f) * 0.5f;
            desiredSpeed *= turnFactor;
        }

        // Only slow down when very close to final destination, not intermediate waypoints
        // Tanks should keep moving through waypoints

        // Multi-ray obstacle detection
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        float detectRange = 12f;
        float tankHalfWidth = tankWidth / 2f;

        // Cast rays: center, left, right, and angled
        bool blockedCenter = false;
        bool blockedLeft = false;
        bool blockedRight = false;
        float steerAdjust = 0f;

        // Center ray
        if (Physics.Raycast(origin, forward, out RaycastHit hitCenter, detectRange, obstacleLayer))
        {
            if (!IsGroundCollider(hitCenter.collider) && !IsCrushableObstacle(hitCenter.collider))
            {
                blockedCenter = true;
                // Steer based on obstacle position
                float side = Vector3.Dot(hitCenter.normal, transform.right);
                steerAdjust = side > 0 ? 45f : -45f;
            }
        }

        // Left ray
        Vector3 leftOrigin = origin + transform.right * -tankHalfWidth;
        if (Physics.Raycast(leftOrigin, forward, out RaycastHit hitLeft, detectRange, obstacleLayer))
        {
            if (!IsGroundCollider(hitLeft.collider) && !IsCrushableObstacle(hitLeft.collider))
            {
                blockedLeft = true;
                steerAdjust += 30f; // Steer right
            }
        }

        // Right ray
        Vector3 rightOrigin = origin + transform.right * tankHalfWidth;
        if (Physics.Raycast(rightOrigin, forward, out RaycastHit hitRight, detectRange, obstacleLayer))
        {
            if (!IsGroundCollider(hitRight.collider) && !IsCrushableObstacle(hitRight.collider))
            {
                blockedRight = true;
                steerAdjust -= 30f; // Steer left
            }
        }

        // Apply obstacle avoidance
        if (blockedCenter || blockedLeft || blockedRight)
        {
            angleToTarget += steerAdjust;

            // Slow down if blocked ahead
            if (blockedCenter)
            {
                desiredSpeed *= 0.4f;
            }
            else
            {
                desiredSpeed *= 0.7f;
            }

            // If completely blocked, try to reverse
            if (blockedCenter && blockedLeft && blockedRight)
            {
                desiredSpeed = -maxSpeed * 0.3f; // Reverse slowly
                angleToTarget = steerAdjust > 0 ? 90f : -90f; // Turn while reversing
            }
        }

        // Smooth acceleration
        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);

        // Calculate inputs
        float moveInput = currentSpeed / maxSpeed;
        float turnInput = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);

        // Sharp turns - slow down more
        if (Mathf.Abs(angleToTarget) > 60f)
        {
            moveInput = 0.1f;
            turnInput = Mathf.Sign(angleToTarget);
        }

        tankController.SetAIInput(moveInput, turnInput, 0f, false);
    }

    /// <summary>
    /// Set a new destination for the tank
    /// </summary>
    public bool SetDestination(Vector3 destination)
    {
        // Add slight randomization to destination so tanks don't all go to exact same spot
        Vector3 randomizedDest = destination + new Vector3(
            Random.Range(-destinationRandomization, destinationRandomization),
            0f,
            Random.Range(-destinationRandomization, destinationRandomization)
        );

        currentDestination = randomizedDest;
        hasDestination = true;
        currentWaypointIndex = 0;
        pathUpdateTimer = 0f; // Force immediate path update

        return CalculatePath(randomizedDest);
    }

    /// <summary>
    /// Stop the tank and clear the current path
    /// </summary>
    public void Stop()
    {
        hasDestination = false;
        currentPath.Clear();
        currentWaypointIndex = 0;
        currentSpeed = 0f;

        if (tankController != null)
        {
            tankController.SetAIInput(0f, 0f, 0f, false);
        }
    }

    /// <summary>
    /// Calculate path to destination
    /// </summary>
    bool CalculatePath(Vector3 destination)
    {
        if (agent == null) return false;

        // Sample positions on NavMesh
        NavMeshHit startHit, endHit;
        Vector3 startPos = transform.position;

        if (!NavMesh.SamplePosition(startPos, out startHit, tankWidth * 2f, NavMesh.AllAreas))
        {
            Debug.LogWarning("[TankNav] Start position not on NavMesh");
            return false;
        }

        if (!NavMesh.SamplePosition(destination, out endHit, tankWidth * 2f, NavMesh.AllAreas))
        {
            Debug.LogWarning("[TankNav] Destination not on NavMesh");
            return false;
        }

        // Calculate path
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path))
        {
            Debug.LogWarning("[TankNav] Failed to calculate path");
            OnPathFailed?.Invoke();
            return false;
        }

        if (path.status == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning("[TankNav] Path invalid");
            OnPathFailed?.Invoke();
            return false;
        }

        // Convert to list and smooth
        currentPath.Clear();
        currentPath.AddRange(path.corners);

        // Add unique offset to intermediate waypoints (not start/end)
        AddPathVariation();

        // Smooth the path for vehicle movement
        SmoothPath();

        // Widen path check - verify tank can fit through each segment
        if (!VerifyPathWidth())
        {
            Debug.LogWarning("[TankNav] Path too narrow for tank");
            // Still use the path, but be cautious
        }

        currentWaypointIndex = 0;

        if (showDebugInfo)
        {
            Debug.Log($"[TankNav] Path calculated: {currentPath.Count} waypoints, status: {path.status}");
        }

        return true;
    }

    /// <summary>
    /// Update the current path (for moving targets or obstacle changes)
    /// </summary>
    void UpdatePath()
    {
        if (!hasDestination) return;

        // Check if we need to recalculate
        float distToEnd = Vector3.Distance(transform.position, currentDestination);
        if (distToEnd < stoppingDistance)
        {
            // Close enough, don't recalculate
            return;
        }

        // Recalculate path
        CalculatePath(currentDestination);
    }

    /// <summary>
    /// Add random variation to path waypoints so tanks don't all follow identical routes
    /// </summary>
    void AddPathVariation()
    {
        if (currentPath.Count < 3) return;

        // Apply this tank's unique offset to intermediate waypoints
        for (int i = 1; i < currentPath.Count - 1; i++)
        {
            Vector3 point = currentPath[i];

            // Add the tank's unique offset plus some per-waypoint randomness
            Vector3 variation = pathOffset + new Vector3(
                Random.Range(-pathRandomization * 0.3f, pathRandomization * 0.3f),
                0f,
                Random.Range(-pathRandomization * 0.3f, pathRandomization * 0.3f)
            );

            point += variation;

            // Make sure the varied point is still on NavMesh
            if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, pathRandomization * 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                currentPath[i] = hit.position;
            }
        }
    }

    /// <summary>
    /// Smooth the path using Chaikin's algorithm
    /// </summary>
    void SmoothPath()
    {
        if (currentPath.Count < 3) return;

        for (int iteration = 0; iteration < pathSmoothingIterations; iteration++)
        {
            List<Vector3> smoothed = new List<Vector3>();
            smoothed.Add(currentPath[0]); // Keep start

            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Vector3 p0 = currentPath[i];
                Vector3 p1 = currentPath[i + 1];

                // Chaikin's corner cutting
                Vector3 q = Vector3.Lerp(p0, p1, 0.25f);
                Vector3 r = Vector3.Lerp(p0, p1, 0.75f);

                smoothed.Add(q);
                smoothed.Add(r);
            }

            smoothed.Add(currentPath[currentPath.Count - 1]); // Keep end
            currentPath = smoothed;
        }

        // Ensure points are on NavMesh
        for (int i = 0; i < currentPath.Count; i++)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(currentPath[i], out hit, 5f, NavMesh.AllAreas))
            {
                currentPath[i] = hit.position;
            }
        }
    }

    /// <summary>
    /// Verify the tank can fit through the path
    /// </summary>
    bool VerifyPathWidth()
    {
        float checkRadius = tankWidth / 2f + 0.5f;

        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 from = currentPath[i];
            Vector3 to = currentPath[i + 1];
            Vector3 dir = (to - from).normalized;
            float dist = Vector3.Distance(from, to);

            // Check with sphere cast
            if (Physics.SphereCast(from + Vector3.up * 1.5f, checkRadius, dir, out RaycastHit hit, dist, obstacleLayer))
            {
                // Check if it's a real obstacle (not ground/terrain/crushable)
                if (IsRealObstacle(hit))
                {
                    if (showDebugInfo)
                    {
                        Debug.Log($"[TankNav] Path blocked by: {hit.collider.name}, height: {GetObstacleHeight(hit.collider):F1}m");
                    }
                    return false;
                }
            }
        }

        return true;
    }

    bool IsGroundCollider(Collider col)
    {
        if (col == null) return true;
        if (col is TerrainCollider) return true;
        if (col.GetComponent<Terrain>() != null) return true;

        string name = col.gameObject.name.ToLower();
        if (name.Contains("ground") || name.Contains("terrain") || name.Contains("floor"))
            return true;

        // Large flat colliders are probably ground
        if (col is BoxCollider box)
        {
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            if (size.x > 20f && size.z > 20f && size.y < 3f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if an obstacle is small enough for the tank to crush/run over
    /// </summary>
    bool IsCrushableObstacle(Collider col)
    {
        if (col == null) return true;
        if (col is TerrainCollider) return true;
        if (col.GetComponent<TankController>() != null) return false; // Never crush other tanks
        if (col.GetComponent<HelicopterController>() != null) return false; // Never crush helicopters

        // If it has DestructibleObject component, it's always crushable
        if (col.GetComponent<DestructibleObject>() != null) return true;
        if (col.GetComponentInParent<DestructibleObject>() != null) return true;

        // If it has Health component (damageable), it's crushable
        if (col.GetComponent<Health>() != null) return true;
        if (col.GetComponentInParent<Health>() != null) return true;

        // Get obstacle height
        float obstacleHeight = GetObstacleHeight(col);

        // If obstacle is shorter than crush threshold, we can run over it
        if (obstacleHeight <= crushableObstacleHeight)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the height of an obstacle from its collider
    /// </summary>
    float GetObstacleHeight(Collider col)
    {
        if (col == null) return 0f;

        // Get bounds in world space
        Bounds bounds = col.bounds;

        // For compound colliders, get combined bounds
        Collider[] allColliders = col.gameObject.GetComponentsInChildren<Collider>();
        if (allColliders.Length > 1)
        {
            bounds = allColliders[0].bounds;
            for (int i = 1; i < allColliders.Length; i++)
            {
                if (allColliders[i] != null)
                    bounds.Encapsulate(allColliders[i].bounds);
            }
        }

        return bounds.size.y;
    }

    /// <summary>
    /// Check if a raycast hit is something the tank should avoid
    /// Returns true for real obstacles, false for crushable/ignorable objects
    /// </summary>
    bool IsRealObstacle(RaycastHit hit)
    {
        if (IsGroundCollider(hit.collider)) return false;
        if (IsCrushableObstacle(hit.collider)) return false;
        return true;
    }

    /// <summary>
    /// Follow the current path
    /// </summary>
    void FollowPath()
    {
        if (currentPath.Count == 0 || currentWaypointIndex >= currentPath.Count)
        {
            // Path complete
            if (hasDestination)
            {
                hasDestination = false;
                currentSpeed = 0f;
                tankController?.SetAIInput(0f, 0f, 0f, false);
                OnDestinationReached?.Invoke();
            }
            return;
        }

        Vector3 currentPos = transform.position;
        Vector3 targetWaypoint = currentPath[currentWaypointIndex];

        // Check if we've reached current waypoint
        float distToWaypoint = Vector3.Distance(currentPos, targetWaypoint);
        if (distToWaypoint < waypointReachDistance)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= currentPath.Count)
            {
                // Destination reached
                hasDestination = false;
                currentSpeed = 0f;
                tankController?.SetAIInput(0f, 0f, 0f, false);
                OnDestinationReached?.Invoke();
                return;
            }
            targetWaypoint = currentPath[currentWaypointIndex];
        }

        // Calculate steering
        Vector3 toTarget = targetWaypoint - currentPos;
        toTarget.y = 0;
        float distToTarget = toTarget.magnitude;

        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        float angleToTarget = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);

        // Calculate desired speed based on distance and angle
        float distToFinal = Vector3.Distance(currentPos, currentDestination);
        float desiredSpeed = maxSpeed;

        // Slow down for turns
        float turnFactor = 1f - Mathf.Clamp01(Mathf.Abs(angleToTarget) / 90f) * 0.7f;
        desiredSpeed *= turnFactor;

        // Slow down approaching destination
        if (distToFinal < slowdownDistance)
        {
            float slowFactor = Mathf.Clamp(distToFinal / slowdownDistance, 0.2f, 1f);
            desiredSpeed *= slowFactor;
        }

        // Apply obstacle avoidance
        Vector3 avoidance = CalculateObstacleAvoidance();
        if (avoidance.magnitude > 0.1f)
        {
            // Blend avoidance into steering
            Vector3 desiredDir = toTarget.normalized + avoidance * avoidanceStrength;
            desiredDir.Normalize();
            float avoidAngle = Vector3.SignedAngle(forward, desiredDir, Vector3.up);
            angleToTarget = Mathf.Lerp(angleToTarget, avoidAngle, 0.5f);

            // Slow down when avoiding
            desiredSpeed *= 0.6f;

            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"[TankNav] AVOIDING obstacle! avoidance={avoidance.magnitude:F2}");
            }
        }

        // Smooth acceleration
        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);

        // Calculate inputs
        float moveInput = currentSpeed / maxSpeed;
        float turnInput = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);

        // For very sharp turns, stop and turn in place
        if (Mathf.Abs(angleToTarget) > 60f)
        {
            moveInput = 0.1f;
            turnInput = Mathf.Sign(angleToTarget);
        }

        // Send to tank controller
        if (tankController != null)
        {
            tankController.SetAIInput(moveInput, turnInput, 0f, false);
        }
    }

    /// <summary>
    /// Calculate obstacle avoidance vector
    /// </summary>
    Vector3 CalculateObstacleAvoidance()
    {
        Vector3 avoidance = Vector3.zero;
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 origin = transform.position + Vector3.up * 1.5f;

        // Cast rays in a fan pattern
        float[] angles = { -45f, -25f, 0f, 25f, 45f };
        float[] weights = { 0.5f, 0.8f, 1f, 0.8f, 0.5f };

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 dir = Quaternion.Euler(0, angles[i], 0) * forward;
            Vector3 endPoint = origin + dir * obstacleDetectionRange;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, obstacleDetectionRange, obstacleLayer))
            {
                // Only avoid real obstacles - ignore crushable ones
                if (IsRealObstacle(hit))
                {
                    // Obstacle detected - add avoidance away from it
                    float strength = 1f - (hit.distance / obstacleDetectionRange);
                    strength *= weights[i];

                    // Avoid to the opposite side
                    if (angles[i] <= 0)
                        avoidance += right * strength;
                    else
                        avoidance -= right * strength;

                    if (showDebugPath)
                    {
                        Debug.DrawLine(origin, hit.point, Color.red, 0.1f);
                    }

                    if (showDebugInfo && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[TankNav] REAL obstacle: {hit.collider.name} at {hit.distance:F1}m, height={GetObstacleHeight(hit.collider):F1}m");
                    }
                }
                else
                {
                    if (showDebugPath)
                    {
                        // Draw crushable obstacles in green
                        Debug.DrawLine(origin, hit.point, Color.green, 0.1f);
                    }

                    if (showDebugInfo && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[TankNav] Crushable: {hit.collider.name} at {hit.distance:F1}m");
                    }
                }
            }
            else if (showDebugPath)
            {
                // Draw ray with no hit in yellow
                Debug.DrawLine(origin, endPoint, Color.yellow, 0.1f);
            }
        }

        return avoidance;
    }

    /// <summary>
    /// Get the current waypoint we're heading to
    /// </summary>
    public Vector3 GetCurrentWaypoint()
    {
        if (currentPath.Count == 0 || currentWaypointIndex >= currentPath.Count)
            return transform.position;
        return currentPath[currentWaypointIndex];
    }

    /// <summary>
    /// Get a point ahead on the path (for look-ahead steering)
    /// </summary>
    public Vector3 GetLookAheadPoint(float distance)
    {
        if (currentPath.Count == 0) return transform.position + transform.forward * distance;

        float accumulated = 0f;
        for (int i = currentWaypointIndex; i < currentPath.Count - 1; i++)
        {
            Vector3 segStart = (i == currentWaypointIndex) ? transform.position : currentPath[i];
            Vector3 segEnd = currentPath[i + 1];
            float segLength = Vector3.Distance(segStart, segEnd);

            if (accumulated + segLength >= distance)
            {
                float remaining = distance - accumulated;
                float t = remaining / segLength;
                return Vector3.Lerp(segStart, segEnd, t);
            }

            accumulated += segLength;
        }

        return currentPath[currentPath.Count - 1];
    }

    void OnDrawGizmos()
    {
        if (!showDebugPath || currentPath.Count == 0) return;

        // Draw path
        Gizmos.color = Color.cyan;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.DrawLine(currentPath[i] + Vector3.up * 0.5f, currentPath[i + 1] + Vector3.up * 0.5f);
        }

        // Draw waypoints
        Gizmos.color = Color.yellow;
        foreach (var point in currentPath)
        {
            Gizmos.DrawWireSphere(point + Vector3.up * 0.5f, 1f);
        }

        // Draw current target
        if (currentWaypointIndex < currentPath.Count)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(currentPath[currentWaypointIndex] + Vector3.up * 0.5f, 1.5f);
        }

        // Draw tank dimensions
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.up * tankHeight / 2f, new Vector3(tankWidth, tankHeight, tankLength));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
