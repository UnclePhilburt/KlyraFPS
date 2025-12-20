using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class HelicopterSpawner : MonoBehaviourPunCallbacks
{
    [Header("Helicopter Prefabs")]
    public GameObject transportHelicopterPrefab;
    public GameObject attackHelicopterPrefab;

    [Header("Pilot Settings")]
    public GameObject aiPrefab;  // AI soldier prefab to spawn as pilot
    public bool spawnWithPilot = true;  // Auto-spawn a dedicated pilot

    [Header("Spawn Points")]
    public Transform[] phantomHelipads;
    public Transform[] havocHelipads;

    [Header("Spawn Settings")]
    public float respawnDelay = 60f;
    public int helicoptersPerTeam = 2;
    public bool spawnOnStart = true;

    // Track spawned helicopters
    private List<SpawnedHelicopter> spawnedHelicopters = new List<SpawnedHelicopter>();

    private class SpawnedHelicopter
    {
        public HelicopterController helicopter;
        public Transform spawnPoint;
        public Team team;
        public float respawnTimer;
        public bool needsRespawn;
    }

    void Start()
    {
        // Only the master client spawns helicopters
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
            return;

        if (spawnOnStart)
        {
            SpawnInitialHelicopters();
        }
    }

    void SpawnInitialHelicopters()
    {
        // Spawn for Phantom team
        for (int i = 0; i < Mathf.Min(helicoptersPerTeam, phantomHelipads.Length); i++)
        {
            SpawnHelicopter(phantomHelipads[i], Team.Phantom);
        }

        // Spawn for Havoc team
        for (int i = 0; i < Mathf.Min(helicoptersPerTeam, havocHelipads.Length); i++)
        {
            SpawnHelicopter(havocHelipads[i], Team.Havoc);
        }
    }

    void SpawnHelicopter(Transform spawnPoint, Team team)
    {
        if (spawnPoint == null) return;
        if (transportHelicopterPrefab == null)
        {
            Debug.LogError("Transport helicopter prefab not assigned!");
            return;
        }

        GameObject heliObj;

        if (PhotonNetwork.IsConnected)
        {
            // Networked spawn
            object[] instantiationData = new object[] { (int)team };
            heliObj = PhotonNetwork.Instantiate(
                transportHelicopterPrefab.name,
                spawnPoint.position,
                spawnPoint.rotation,
                0,
                instantiationData
            );
        }
        else
        {
            // Offline spawn
            heliObj = Instantiate(transportHelicopterPrefab, spawnPoint.position, spawnPoint.rotation);
        }

        HelicopterController heli = heliObj.GetComponent<HelicopterController>();
        if (heli != null)
        {
            heli.helicopterTeam = team;

            // Track for respawning
            SpawnedHelicopter tracked = new SpawnedHelicopter
            {
                helicopter = heli,
                spawnPoint = spawnPoint,
                team = team,
                respawnTimer = 0f,
                needsRespawn = false
            };
            spawnedHelicopters.Add(tracked);

            // Spawn a dedicated pilot for this helicopter
            if (spawnWithPilot)
            {
                if (aiPrefab != null)
                {
                    SpawnPilotForHelicopter(heli, spawnPoint, team);
                    Debug.Log($"[HELI SPAWNER] Spawning pilot for {team} helicopter");
                }
                else
                {
                    Debug.LogWarning("[HELI SPAWNER] spawnWithPilot is true but aiPrefab is not assigned! Assign AI prefab in Inspector.");
                }
            }
            else
            {
                Debug.Log("[HELI SPAWNER] spawnWithPilot is false, no pilot spawned");
            }
        }

        Debug.Log($"Spawned {team} helicopter at {spawnPoint.name}");
    }

    void SpawnPilotForHelicopter(HelicopterController helicopter, Transform spawnPoint, Team team)
    {
        StartCoroutine(SpawnPilotDelayed(helicopter, spawnPoint, team));
    }

    System.Collections.IEnumerator SpawnPilotDelayed(HelicopterController helicopter, Transform spawnPoint, Team team)
    {
        // Wait a moment for helicopter to fully initialize
        yield return new WaitForSeconds(0.5f);

        if (helicopter == null || helicopter.isDestroyed) yield break;

        // Spawn pilot next to the helicopter
        Vector3 pilotSpawnPos = spawnPoint.position + spawnPoint.right * 3f;
        Quaternion spawnRot = Quaternion.LookRotation(helicopter.transform.position - pilotSpawnPos);

        // Also set the static prefab reference in case AISpawner hasn't
        if (AIController.aiPrefabReference == null)
        {
            AIController.aiPrefabReference = aiPrefab;
        }

        GameObject pilotObj = Instantiate(aiPrefab, pilotSpawnPos, spawnRot);
        AIController pilot = pilotObj.GetComponent<AIController>();

        if (pilot != null)
        {
            pilot.team = team;
            pilot.InitializeTeam();
            pilotObj.name = $"AI_Pilot_{team}_{helicopter.name}";

            // Wait one more frame for AI to fully initialize (Start() to run)
            yield return null;

            // Make this AI become the helicopter's pilot
            pilot.AssignAsHelicopterPilot(helicopter);

            Debug.Log($"Spawned dedicated {team} pilot for {helicopter.name}");
        }
    }

    void Update()
    {
        // Only master client handles respawning
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
            return;

        // Check for destroyed helicopters
        for (int i = spawnedHelicopters.Count - 1; i >= 0; i--)
        {
            SpawnedHelicopter tracked = spawnedHelicopters[i];

            if (tracked.needsRespawn)
            {
                tracked.respawnTimer -= Time.deltaTime;
                if (tracked.respawnTimer <= 0)
                {
                    // Respawn
                    SpawnHelicopter(tracked.spawnPoint, tracked.team);
                    spawnedHelicopters.RemoveAt(i);
                }
            }
            else if (tracked.helicopter == null || tracked.helicopter.isDestroyed)
            {
                // Mark for respawn
                tracked.needsRespawn = true;
                tracked.respawnTimer = respawnDelay;
                Debug.Log($"Helicopter destroyed. Respawning in {respawnDelay} seconds.");
            }
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // If we're the new master, we might need to handle respawns
        if (newMasterClient.IsLocal)
        {
            Debug.Log("Became master client - taking over helicopter spawning");
        }
    }

    // Called from editor or other scripts to manually spawn
    public void SpawnHelicopterAtPoint(int helipadIndex, Team team)
    {
        Transform[] helipads = team == Team.Phantom ? phantomHelipads : havocHelipads;

        if (helipadIndex >= 0 && helipadIndex < helipads.Length)
        {
            SpawnHelicopter(helipads[helipadIndex], team);
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw helipad locations
        if (phantomHelipads != null)
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            foreach (var pad in phantomHelipads)
            {
                if (pad != null)
                {
                    Gizmos.DrawWireCube(pad.position, new Vector3(10f, 0.5f, 10f));
                    Gizmos.DrawLine(pad.position, pad.position + Vector3.up * 5f);
                }
            }
        }

        if (havocHelipads != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            foreach (var pad in havocHelipads)
            {
                if (pad != null)
                {
                    Gizmos.DrawWireCube(pad.position, new Vector3(10f, 0.5f, 10f));
                    Gizmos.DrawLine(pad.position, pad.position + Vector3.up * 5f);
                }
            }
        }
    }
}
