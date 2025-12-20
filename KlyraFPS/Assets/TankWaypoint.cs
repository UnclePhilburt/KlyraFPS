using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Waypoint for tank navigation. Place these along roads/paths.
/// Tanks will follow waypoints instead of using NavMesh.
/// </summary>
public class TankWaypoint : MonoBehaviour
{
    [Header("Connections")]
    [Tooltip("Waypoints this one connects to")]
    public List<TankWaypoint> connections = new List<TankWaypoint>();

    [Header("Settings")]
    [Tooltip("Radius to consider waypoint reached - keep tight for strict paths")]
    public float reachRadius = 4f;

    [Tooltip("Speed limit at this waypoint (0 = no limit)")]
    public float speedLimit = 0f;

    [Tooltip("Is this a spawn point for tanks?")]
    public bool isSpawnPoint = false;

    [Tooltip("Team that owns this waypoint (None = neutral)")]
    public Team ownerTeam = Team.None;

    // Static list of all waypoints
    private static List<TankWaypoint> allWaypoints = new List<TankWaypoint>();
    public static List<TankWaypoint> AllWaypoints => allWaypoints;

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
    public static TankWaypoint FindNearest(Vector3 position, Team team = Team.None)
    {
        TankWaypoint nearest = null;
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
    public TankWaypoint GetRandomConnection()
    {
        if (connections.Count == 0) return null;
        return connections[Random.Range(0, connections.Count)];
    }

    /// <summary>
    /// Find the connection closest to a target position
    /// </summary>
    public TankWaypoint GetConnectionToward(Vector3 target)
    {
        if (connections.Count == 0) return null;
        if (connections.Count == 1) return connections[0];

        TankWaypoint best = null;
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
        float maxDist = 100f;
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

        Debug.Log($"[TankWaypoint] {name} connected to {connections.Count} waypoints");
    }

    void OnDrawGizmos()
    {
        // Draw waypoint
        Gizmos.color = isSpawnPoint ? Color.green : (ownerTeam == Team.Phantom ? Color.blue : (ownerTeam == Team.Havoc ? Color.red : Color.yellow));
        Gizmos.DrawWireSphere(transform.position, reachRadius);

        // Draw connections
        Gizmos.color = Color.cyan;
        foreach (var conn in connections)
        {
            if (conn != null)
            {
                Gizmos.DrawLine(transform.position + Vector3.up, conn.transform.position + Vector3.up);
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
