using UnityEngine;
using System.Collections.Generic;

public class Runway : MonoBehaviour
{
    [Header("Runway Settings")]
    public Team assignedTeam = Team.None;
    public float runwayLength = 200f;
    public float runwayWidth = 20f;

    [Header("Spawn/Landing Points")]
    public Transform spawnPoint;        // Where jets spawn (start of runway)
    public Transform takeoffPoint;      // Where jets should lift off
    public Transform approachPoint;     // Where jets line up for landing (in the air)
    public Transform touchdownPoint;    // Where jets touch down

    [Header("State")]
    public bool isOccupied = false;
    public float occupiedCooldown = 30f;

    private float occupiedTimer = 0f;
    private static List<Runway> allRunways = new List<Runway>();

    // Direction the runway points (takeoff direction)
    public Vector3 RunwayDirection => spawnPoint != null && takeoffPoint != null
        ? (takeoffPoint.position - spawnPoint.position).normalized
        : transform.forward;

    public Vector3 SpawnPosition
    {
        get
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            Debug.Log($"[RUNWAY] SpawnPosition called. spawnPoint: {spawnPoint}, returning: {pos}");
            return pos;
        }
    }
    public Quaternion SpawnRotation => Quaternion.LookRotation(RunwayDirection, Vector3.up);

    void Awake()
    {
        if (!allRunways.Contains(this))
        {
            allRunways.Add(this);
        }

        // Auto-create points if not assigned
        if (spawnPoint == null)
        {
            GameObject spawn = new GameObject("SpawnPoint");
            spawn.transform.parent = transform;
            spawn.transform.localPosition = Vector3.zero;
            spawnPoint = spawn.transform;
        }

        if (takeoffPoint == null)
        {
            GameObject takeoff = new GameObject("TakeoffPoint");
            takeoff.transform.parent = transform;
            takeoff.transform.localPosition = transform.forward * runwayLength;
            takeoffPoint = takeoff.transform;
        }

        if (approachPoint == null)
        {
            GameObject approach = new GameObject("ApproachPoint");
            approach.transform.parent = transform;
            approach.transform.localPosition = -transform.forward * 300f + Vector3.up * 50f;
            approachPoint = approach.transform;
        }

        if (touchdownPoint == null)
        {
            GameObject touchdown = new GameObject("TouchdownPoint");
            touchdown.transform.parent = transform;
            touchdown.transform.localPosition = transform.forward * (runwayLength * 0.3f);
            touchdownPoint = touchdown.transform;
        }
    }

    void OnDestroy()
    {
        allRunways.Remove(this);
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

    // Check if a position is on this runway
    public bool IsOnRunway(Vector3 position)
    {
        Vector3 localPos = transform.InverseTransformPoint(position);

        // Check if within runway bounds
        bool withinLength = localPos.z >= -10f && localPos.z <= runwayLength + 10f;
        bool withinWidth = Mathf.Abs(localPos.x) <= runwayWidth / 2f + 5f;
        bool nearGround = Mathf.Abs(localPos.y) <= 5f;

        return withinLength && withinWidth && nearGround;
    }

    // Check if jet is aligned for landing
    public bool IsAlignedForLanding(Transform jet, float maxAngle = 15f)
    {
        Vector3 toRunway = (touchdownPoint.position - jet.position).normalized;
        float angle = Vector3.Angle(jet.forward, toRunway);
        return angle < maxAngle;
    }

    // Get landing approach waypoints
    public Vector3[] GetLandingApproachPath()
    {
        return new Vector3[]
        {
            approachPoint.position,
            touchdownPoint.position,
            spawnPoint.position  // End of rollout
        };
    }

    // === STATIC METHODS ===

    public static Runway FindNearestAvailable(Vector3 position, Team team, float maxDistance = 2000f)
    {
        Runway best = null;
        float bestDist = maxDistance;

        foreach (var runway in allRunways)
        {
            if (runway == null) continue;
            if (runway.isOccupied) continue;
            if (runway.assignedTeam != Team.None && runway.assignedTeam != team) continue;

            float dist = Vector3.Distance(position, runway.SpawnPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = runway;
            }
        }

        return best;
    }

    public static Runway FindBestForLanding(Vector3 jetPosition, Vector3 jetForward, Team team, float maxDistance = 3000f)
    {
        Runway best = null;
        float bestScore = float.MinValue;

        foreach (var runway in allRunways)
        {
            if (runway == null) continue;
            if (runway.isOccupied) continue;
            if (runway.assignedTeam != Team.None && runway.assignedTeam != team) continue;

            float dist = Vector3.Distance(jetPosition, runway.approachPoint.position);
            if (dist > maxDistance) continue;

            // Score based on distance and alignment
            float distScore = 1000f - dist;

            // Prefer runways we're already heading toward
            Vector3 toRunway = (runway.approachPoint.position - jetPosition).normalized;
            float alignment = Vector3.Dot(jetForward, toRunway);
            float alignScore = alignment * 500f;

            float score = distScore + alignScore;

            if (score > bestScore)
            {
                bestScore = score;
                best = runway;
            }
        }

        return best;
    }

    public static List<Runway> GetAllForTeam(Team team)
    {
        List<Runway> result = new List<Runway>();

        foreach (var runway in allRunways)
        {
            if (runway == null) continue;
            if (runway.assignedTeam == Team.None || runway.assignedTeam == team)
            {
                result.Add(runway);
            }
        }

        return result;
    }

    public static int GetTotalCount()
    {
        return allRunways.Count;
    }

    void OnDrawGizmos()
    {
        // Draw runway surface
        Gizmos.color = isOccupied ? Color.red : (assignedTeam == Team.Phantom ? Color.blue : (assignedTeam == Team.Havoc ? new Color(1f, 0.5f, 0f) : Color.gray));

        Vector3 center = transform.position + transform.forward * (runwayLength / 2f);
        Vector3 size = new Vector3(runwayWidth, 0.1f, runwayLength);

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = oldMatrix;

        // Draw direction arrow
        Gizmos.color = Color.green;
        Vector3 arrowStart = transform.position + transform.forward * (runwayLength * 0.8f);
        Vector3 arrowEnd = transform.position + transform.forward * (runwayLength + 20f);
        Gizmos.DrawLine(arrowStart, arrowEnd);
        Gizmos.DrawLine(arrowEnd, arrowEnd - transform.forward * 5f + transform.right * 3f);
        Gizmos.DrawLine(arrowEnd, arrowEnd - transform.forward * 5f - transform.right * 3f);

        // Draw centerline
        Gizmos.color = Color.white;
        for (float z = 0; z < runwayLength; z += 20f)
        {
            Vector3 start = transform.position + transform.forward * z;
            Vector3 end = transform.position + transform.forward * (z + 10f);
            Gizmos.DrawLine(start, end);
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw waypoints
        if (spawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(spawnPoint.position, 3f);
            Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + Vector3.up * 10f);
        }

        if (takeoffPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(takeoffPoint.position, 3f);
        }

        if (approachPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(approachPoint.position, 5f);

            // Draw approach path
            if (touchdownPoint != null)
            {
                Gizmos.DrawLine(approachPoint.position, touchdownPoint.position);
            }
        }

        if (touchdownPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(touchdownPoint.position, 3f);

            if (spawnPoint != null)
            {
                Gizmos.DrawLine(touchdownPoint.position, spawnPoint.position);
            }
        }
    }
}
