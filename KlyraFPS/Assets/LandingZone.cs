using UnityEngine;
using System.Collections.Generic;

public class LandingZone : MonoBehaviour
{
    [Header("Landing Zone Settings")]
    public Team assignedTeam = Team.None;  // None = any team can use
    public float radius = 15f;  // Size of landing area
    public bool isOccupied = false;  // Is a helicopter currently here?
    public float occupiedCooldown = 30f;  // How long before another heli can land here

    [Header("Optional References")]
    public Transform landingPoint;  // Specific point to land at (optional)

    private float occupiedTimer = 0f;
    private static List<LandingZone> allLandingZones = new List<LandingZone>();

    public Vector3 LandingPosition => landingPoint != null ? landingPoint.position : transform.position;

    void Awake()
    {
        if (!allLandingZones.Contains(this))
        {
            allLandingZones.Add(this);
        }
    }

    void OnDestroy()
    {
        allLandingZones.Remove(this);
    }

    void Update()
    {
        if (isOccupied)
        {
            occupiedTimer -= Time.deltaTime;
            if (occupiedTimer <= 0f)
            {
                isOccupied = false;
            }
        }
    }

    public void SetOccupied()
    {
        isOccupied = true;
        occupiedTimer = occupiedCooldown;
    }

    public void SetFree()
    {
        isOccupied = false;
        occupiedTimer = 0f;
    }

    // Static methods to find landing zones
    public static LandingZone FindNearestAvailable(Vector3 position, Team team, float maxDistance = 500f)
    {
        LandingZone best = null;
        float bestDist = maxDistance;

        foreach (var zone in allLandingZones)
        {
            if (zone == null) continue;
            if (zone.isOccupied) continue;
            if (zone.assignedTeam != Team.None && zone.assignedTeam != team) continue;

            float dist = Vector3.Distance(position, zone.LandingPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = zone;
            }
        }

        return best;
    }

    public static LandingZone FindNearestToObjective(Vector3 objectivePos, Team team, float minDist = 20f, float maxDist = 100f)
    {
        LandingZone best = null;
        float bestScore = float.MinValue;

        foreach (var zone in allLandingZones)
        {
            if (zone == null) continue;
            if (zone.isOccupied) continue;
            if (zone.assignedTeam != Team.None && zone.assignedTeam != team) continue;

            float dist = Vector3.Distance(objectivePos, zone.LandingPosition);

            // Must be within range
            if (dist < minDist || dist > maxDist) continue;

            // Score: prefer closer to objective but not too close
            float score = 100f - (dist * 0.5f);

            if (score > bestScore)
            {
                bestScore = score;
                best = zone;
            }
        }

        return best;
    }

    public static LandingZone FindNearestToPlayer(Vector3 playerPos, Team team, float maxDist = 200f)
    {
        LandingZone best = null;
        float bestDist = maxDist;

        foreach (var zone in allLandingZones)
        {
            if (zone == null) continue;
            if (zone.isOccupied) continue;
            if (zone.assignedTeam != Team.None && zone.assignedTeam != team) continue;

            float dist = Vector3.Distance(playerPos, zone.LandingPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = zone;
            }
        }

        return best;
    }

    public static List<LandingZone> GetAllAvailable(Team team)
    {
        List<LandingZone> available = new List<LandingZone>();

        foreach (var zone in allLandingZones)
        {
            if (zone == null) continue;
            if (zone.isOccupied) continue;
            if (zone.assignedTeam != Team.None && zone.assignedTeam != team) continue;

            available.Add(zone);
        }

        return available;
    }

    public static int GetTotalCount()
    {
        return allLandingZones.Count;
    }

    void OnDrawGizmos()
    {
        // Draw landing zone in editor
        Gizmos.color = isOccupied ? Color.red : (assignedTeam == Team.Phantom ? Color.blue : (assignedTeam == Team.Havoc ? new Color(1f, 0.5f, 0f) : Color.green));
        Gizmos.DrawWireSphere(LandingPosition, radius);

        // Draw H for helipad
        Gizmos.color = Color.white;
        Vector3 pos = LandingPosition + Vector3.up * 0.1f;
        float size = radius * 0.3f;

        // Draw H shape
        Gizmos.DrawLine(pos + new Vector3(-size, 0, -size), pos + new Vector3(-size, 0, size));
        Gizmos.DrawLine(pos + new Vector3(size, 0, -size), pos + new Vector3(size, 0, size));
        Gizmos.DrawLine(pos + new Vector3(-size, 0, 0), pos + new Vector3(size, 0, 0));
    }

    void OnDrawGizmosSelected()
    {
        // Draw more detailed when selected
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawSphere(LandingPosition, radius);
    }
}
