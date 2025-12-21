using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Waypoint for Humvee navigation. Place these along roads/paths.
/// Humvees will follow these waypoints for patrol routes.
/// </summary>
public class HumveeWaypoint : MonoBehaviour
{
    [Header("Connections")]
    [Tooltip("Waypoints this one connects to")]
    public List<HumveeWaypoint> connections = new List<HumveeWaypoint>();

    [Header("Settings")]
    [Tooltip("Radius to consider waypoint reached")]
    public float reachRadius = 8f;

    [Tooltip("Speed limit at this waypoint (0 = no limit)")]
    public float speedLimit = 0f;

    [Tooltip("Is this a spawn point for Humvees?")]
    public bool isSpawnPoint = false;

    [Tooltip("Team that owns this waypoint (None = neutral)")]
    public Team ownerTeam = Team.None;

    // Static list of all waypoints
    private static List<HumveeWaypoint> allWaypoints = new List<HumveeWaypoint>();
    public static List<HumveeWaypoint> AllWaypoints => allWaypoints;

    void OnEnable()
    {
        if (!allWaypoints.Contains(this))
            allWaypoints.Add(this);
    }

    void OnDisable()
    {
        allWaypoints.Remove(this);
    }

    /// <summary>
    /// Find nearest waypoint to a position
    /// </summary>
    public static HumveeWaypoint FindNearest(Vector3 position, Team team = Team.None)
    {
        HumveeWaypoint nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var wp in allWaypoints)
        {
            if (wp == null) continue;

            // Skip enemy waypoints if team specified
            if (team != Team.None && wp.ownerTeam != Team.None && wp.ownerTeam != team)
                continue;

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
    /// Find a random connected waypoint
    /// </summary>
    public HumveeWaypoint GetRandomConnection()
    {
        if (connections.Count == 0) return null;
        return connections[Random.Range(0, connections.Count)];
    }

    /// <summary>
    /// Find the connection closest to a target position
    /// </summary>
    public HumveeWaypoint GetConnectionToward(Vector3 target)
    {
        if (connections.Count == 0) return null;
        if (connections.Count == 1) return connections[0];

        HumveeWaypoint best = null;
        float bestDist = float.MaxValue;

        foreach (var conn in connections)
        {
            if (conn == null) continue;
            float dist = Vector3.Distance(conn.transform.position, target);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = conn;
            }
        }

        return best;
    }

    /// <summary>
    /// Auto-connect to nearby waypoints
    /// </summary>
    [ContextMenu("Auto Connect Nearby")]
    public void AutoConnectNearby()
    {
        float maxDist = 150f;  // Humvees can travel further between waypoints
        connections.Clear();

        foreach (var wp in allWaypoints)
        {
            if (wp == null || wp == this) continue;

            float dist = Vector3.Distance(transform.position, wp.transform.position);
            if (dist < maxDist)
            {
                // Check line of sight
                Vector3 dir = (wp.transform.position - transform.position).normalized;
                if (!Physics.Raycast(transform.position + Vector3.up, dir, dist - 1f))
                {
                    connections.Add(wp);
                }
            }
        }

        Debug.Log($"[HumveeWaypoint] {name} connected to {connections.Count} waypoints");
    }

    void OnDrawGizmos()
    {
        // Draw waypoint - orange for humvee waypoints
        Gizmos.color = isSpawnPoint ? Color.green :
            (ownerTeam == Team.Phantom ? new Color(0.3f, 0.5f, 1f) :
            (ownerTeam == Team.Havoc ? new Color(1f, 0.3f, 0.3f) :
            new Color(1f, 0.6f, 0f)));  // Orange for neutral

        Gizmos.DrawWireSphere(transform.position, reachRadius);

        // Draw connections
        Gizmos.color = new Color(1f, 0.5f, 0f);  // Orange lines
        foreach (var conn in connections)
        {
            if (conn != null)
            {
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, conn.transform.position + Vector3.up * 0.5f);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw larger when selected
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, reachRadius * 1.2f);
    }
}
