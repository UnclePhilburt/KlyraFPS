using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Waypoint-based navigation for Humvees using the TankWaypoint system.
/// Attach this to the Humvee prefab.
/// </summary>
[RequireComponent(typeof(HumveeController))]
public class HumveeNavigation : MonoBehaviour
{
    [Header("Vehicle Dimensions")]
    public float vehicleWidth = 2.5f;
    public float vehicleLength = 5f;

    [Header("Navigation Settings")]
    public float stoppingDistance = 5f;
    public float maxSpeed = 12f;
    public float acceleration = 8f;

    [Header("Waypoint Navigation")]
    public bool useWaypoints = true;
    public float waypointReachDistance = 8f;
    public bool fallbackToTankWaypoints = true;  // Use tank waypoints if no humvee waypoints

    [Header("Obstacle Avoidance")]
    public float obstacleDetectionRange = 10f;
    public float crushableObstacleHeight = 0.8f;
    public LayerMask obstacleLayer = -1;

    [Header("Debug")]
    public bool showDebugPath = false;

    // Components
    private HumveeController humveeController;
    private NavMeshAgent agent;

    // Waypoint state - Humvee waypoints
    private HumveeWaypoint currentHumveeWaypoint;
    private HumveeWaypoint previousHumveeWaypoint;  // Track where we came from
    // Fallback - Tank waypoints
    private TankWaypoint currentTankWaypoint;
    private TankWaypoint previousTankWaypoint;

    private Vector3 currentDestination;
    private bool hasDestination = false;

    // Movement state
    private float currentSpeed = 0f;

    // Stuck detection
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float lastStuckCheck = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;
    private float reverseTurnDir = 1f;

    // Public properties
    public bool HasNavigation => true;
    public bool IsMoving => currentSpeed > 0.5f;
    public float RemainingDistance => hasDestination ? Vector3.Distance(transform.position, currentDestination) : 0f;

    void Awake()
    {
        humveeController = GetComponent<HumveeController>();
        SetupNavMeshAgent();

        // Configure obstacle layer
        if (obstacleLayer.value == -1 || obstacleLayer.value == 0)
        {
            int mask = ~0;
            int groundLayer = LayerMask.NameToLayer("Ground");
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            int waterLayer = LayerMask.NameToLayer("Water");

            if (groundLayer >= 0) mask &= ~(1 << groundLayer);
            if (terrainLayer >= 0) mask &= ~(1 << terrainLayer);
            if (vehicleLayer >= 0) mask &= ~(1 << vehicleLayer);
            if (waterLayer >= 0) mask &= ~(1 << waterLayer);
            mask &= ~(1 << gameObject.layer);

            obstacleLayer = mask;
        }
    }

    void SetupNavMeshAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        agent.radius = vehicleWidth / 2f;
        agent.height = 2f;
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.stoppingDistance = stoppingDistance;
        agent.speed = maxSpeed;
        agent.angularSpeed = 120f;
        agent.acceleration = acceleration;
    }

    void Update()
    {
        if (humveeController == null || !humveeController.HasDriver) return;

        if (!useWaypoints) return;

        // Prefer HumveeWaypoints, fallback to TankWaypoints
        if (HumveeWaypoint.AllWaypoints.Count > 0)
        {
            UpdateHumveeWaypointNavigation();
        }
        else if (fallbackToTankWaypoints && TankWaypoint.AllWaypoints.Count > 0)
        {
            UpdateTankWaypointNavigation();
        }
    }

    void UpdateHumveeWaypointNavigation()
    {
        Vector3 currentPos = transform.position;

        // Find nearest waypoint if we don't have one
        if (currentHumveeWaypoint == null)
        {
            currentHumveeWaypoint = HumveeWaypoint.FindNearest(currentPos, humveeController?.HumveeTeam ?? Team.None);
            if (currentHumveeWaypoint == null) return;

            // Set a "virtual" previous waypoint behind us based on spawn direction
            // This prevents the Humvee from immediately turning around
            Vector3 behindUs = currentPos - transform.forward * 50f;
            previousHumveeWaypoint = FindWaypointNear(behindUs);

            Debug.Log($"[HumveeNav] Starting at waypoint: {currentHumveeWaypoint.name}, " +
                $"connections: {currentHumveeWaypoint.connections.Count}, " +
                $"virtual previous: {(previousHumveeWaypoint != null ? previousHumveeWaypoint.name : "none")}");
        }

        // Check if we reached current waypoint
        float distToWaypoint = Vector3.Distance(currentPos, currentHumveeWaypoint.transform.position);
        float reachDist = Mathf.Max(currentHumveeWaypoint.reachRadius, waypointReachDistance);

        if (distToWaypoint < reachDist)
        {
            // Pick next waypoint - avoid going back to previous
            HumveeWaypoint next = null;

            if (hasDestination)
            {
                next = currentHumveeWaypoint.GetConnectionToward(currentDestination);

                float distToDest = Vector3.Distance(currentPos, currentDestination);
                if (distToDest < stoppingDistance)
                {
                    hasDestination = false;
                    currentSpeed = 0f;
                    return;
                }
            }
            else
            {
                // Get all connections except the one we came from
                var connections = currentHumveeWaypoint.connections;
                if (connections.Count > 1 && previousHumveeWaypoint != null)
                {
                    // Pick random connection that isn't where we came from
                    var forwardConnections = connections.FindAll(c => c != previousHumveeWaypoint);
                    if (forwardConnections.Count > 0)
                    {
                        next = forwardConnections[Random.Range(0, forwardConnections.Count)];
                    }
                    else
                    {
                        next = connections[Random.Range(0, connections.Count)];
                    }
                }
                else if (connections.Count > 0)
                {
                    next = connections[Random.Range(0, connections.Count)];
                }
            }

            if (next != null && next != currentHumveeWaypoint)
            {
                Debug.Log($"[HumveeNav] Reached {currentHumveeWaypoint.name}, " +
                    $"connections: {currentHumveeWaypoint.connections.Count}, " +
                    $"previous: {(previousHumveeWaypoint != null ? previousHumveeWaypoint.name : "none")}, " +
                    $"next: {next.name}");
                previousHumveeWaypoint = currentHumveeWaypoint;
                currentHumveeWaypoint = next;
            }
        }

        // Drive toward current waypoint
        DriveTowardWaypoint(currentHumveeWaypoint.transform.position, currentHumveeWaypoint.speedLimit);
    }

    void UpdateTankWaypointNavigation()
    {
        Vector3 currentPos = transform.position;

        // Find nearest waypoint if we don't have one
        if (currentTankWaypoint == null)
        {
            currentTankWaypoint = TankWaypoint.FindNearest(currentPos, humveeController?.HumveeTeam ?? Team.None);
            if (currentTankWaypoint == null) return;

            // Set virtual previous based on spawn direction to prevent U-turns
            Vector3 behindUs = currentPos - transform.forward * 50f;
            previousTankWaypoint = FindTankWaypointNear(behindUs);

            Debug.Log($"[HumveeNav] Starting at tank waypoint: {currentTankWaypoint.name}");
        }

        // Check if we reached current waypoint
        float distToWaypoint = Vector3.Distance(currentPos, currentTankWaypoint.transform.position);
        float reachDist = Mathf.Max(currentTankWaypoint.reachRadius, waypointReachDistance);

        if (distToWaypoint < reachDist)
        {
            // Pick next waypoint - avoid going back
            TankWaypoint next = null;

            if (hasDestination)
            {
                next = currentTankWaypoint.GetConnectionToward(currentDestination);

                float distToDest = Vector3.Distance(currentPos, currentDestination);
                if (distToDest < stoppingDistance)
                {
                    hasDestination = false;
                    currentSpeed = 0f;
                    return;
                }
            }
            else
            {
                // Get all connections except the one we came from
                var connections = currentTankWaypoint.connections;
                if (connections.Count > 1 && previousTankWaypoint != null)
                {
                    var forwardConnections = connections.FindAll(c => c != previousTankWaypoint);
                    if (forwardConnections.Count > 0)
                    {
                        next = forwardConnections[Random.Range(0, forwardConnections.Count)];
                    }
                    else
                    {
                        next = connections[Random.Range(0, connections.Count)];
                    }
                }
                else if (connections.Count > 0)
                {
                    next = connections[Random.Range(0, connections.Count)];
                }
            }

            if (next != null && next != currentTankWaypoint)
            {
                previousTankWaypoint = currentTankWaypoint;
                currentTankWaypoint = next;
            }
        }

        // Drive toward current waypoint
        DriveTowardWaypoint(currentTankWaypoint.transform.position, currentTankWaypoint.speedLimit);
    }

    void DriveTowardWaypoint(Vector3 targetPos, float speedLimit)
    {
        if (humveeController == null) return;

        Vector3 currentPos = transform.position;

        // Stuck detection - only reverse if REALLY stuck
        if (Time.time - lastStuckCheck > 1f)
        {
            float movedDist = Vector3.Distance(currentPos, lastPosition);
            if (movedDist < 0.3f && !isReversing)
            {
                stuckTimer += 1f;
                if (stuckTimer > 4f)
                {
                    isReversing = true;
                    reverseTimer = 1.5f;
                    reverseTurnDir = Random.value > 0.5f ? 1f : -1f;
                }
            }
            else
            {
                stuckTimer = 0f;
            }
            lastPosition = currentPos;
            lastStuckCheck = Time.time;
        }

        // If reversing, back up briefly
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
                humveeController.SetAIDriveInput(-0.8f, reverseTurnDir);
                return;
            }
        }

        Vector3 toTarget = targetPos - currentPos;
        toTarget.y = 0;

        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        float angleToTarget = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);

        // Drive fast but steer smoothly
        float moveInput = 1f;

        // Apply speed limit if set
        if (speedLimit > 0)
        {
            moveInput = Mathf.Min(moveInput, speedLimit / maxSpeed);
        }

        float turnInput = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);

        // Slow down for sharper turns
        if (Mathf.Abs(angleToTarget) > 90f)
        {
            moveInput *= 0.4f;
            turnInput = Mathf.Clamp(angleToTarget / 30f, -1f, 1f);
        }
        else if (Mathf.Abs(angleToTarget) > 45f)
        {
            moveInput *= 0.7f;
        }

        humveeController.SetAIDriveInput(moveInput, turnInput);
    }

    public bool SetDestination(Vector3 destination)
    {
        currentDestination = destination;
        hasDestination = true;
        return true;
    }

    public void Stop()
    {
        hasDestination = false;
        currentSpeed = 0f;
        if (humveeController != null)
        {
            humveeController.SetAIDriveInput(0f, 0f);
        }
    }

    bool IsGroundCollider(Collider col)
    {
        if (col == null) return true;
        if (col is TerrainCollider) return true;
        string name = col.gameObject.name.ToLower();
        if (name.Contains("ground") || name.Contains("terrain") || name.Contains("floor"))
            return true;
        return false;
    }

    bool IsCrushable(Collider col)
    {
        if (col == null) return true;
        if (col is TerrainCollider) return true;
        if (col.GetComponent<TankController>() != null) return false;
        if (col.GetComponent<HumveeController>() != null) return false;
        if (col.GetComponent<DestructibleObject>() != null) return true;

        float height = col.bounds.size.y;
        return height <= crushableObstacleHeight;
    }

    /// <summary>
    /// Find waypoint nearest to a position (used to set virtual previous waypoint)
    /// </summary>
    HumveeWaypoint FindWaypointNear(Vector3 position)
    {
        HumveeWaypoint nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var wp in HumveeWaypoint.AllWaypoints)
        {
            if (wp == null || wp == currentHumveeWaypoint) continue;

            float dist = Vector3.Distance(position, wp.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = wp;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Find tank waypoint nearest to a position (used to set virtual previous waypoint)
    /// </summary>
    TankWaypoint FindTankWaypointNear(Vector3 position)
    {
        TankWaypoint nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var wp in TankWaypoint.AllWaypoints)
        {
            if (wp == null || wp == currentTankWaypoint) continue;

            float dist = Vector3.Distance(position, wp.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = wp;
            }
        }

        return nearest;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugPath) return;

        // Draw line to current waypoint
        if (currentHumveeWaypoint != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);  // Orange for humvee waypoint
            Gizmos.DrawLine(transform.position + Vector3.up, currentHumveeWaypoint.transform.position + Vector3.up);
            Gizmos.DrawWireSphere(currentHumveeWaypoint.transform.position + Vector3.up, 2f);
        }
        else if (currentTankWaypoint != null)
        {
            Gizmos.color = Color.cyan;  // Cyan for tank waypoint fallback
            Gizmos.DrawLine(transform.position + Vector3.up, currentTankWaypoint.transform.position + Vector3.up);
            Gizmos.DrawWireSphere(currentTankWaypoint.transform.position + Vector3.up, 2f);
        }
    }
}
