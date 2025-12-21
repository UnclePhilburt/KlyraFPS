using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class JetSpawner : MonoBehaviourPunCallbacks
{
    [Header("Jet Prefabs")]
    public GameObject jetPrefab;

    [Header("AI Pilot")]
    public GameObject aiPrefab;
    public bool spawnWithPilot = true;

    [Header("Runways")]
    public Runway[] phantomRunways;
    public Runway[] havocRunways;

    [Header("Spawn Settings")]
    public float respawnDelay = 90f;
    public int jetsPerTeam = 1;
    public bool spawnOnStart = true;

    // Track spawned jets
    private List<SpawnedJet> spawnedJets = new List<SpawnedJet>();

    private class SpawnedJet
    {
        public JetController jet;
        public Runway runway;
        public Team team;
        public float respawnTimer;
        public bool needsRespawn;
    }

    void Start()
    {
        Debug.Log($"[JET SPAWNER] Start called. Connected: {PhotonNetwork.IsConnected}, InRoom: {PhotonNetwork.InRoom}, IsMaster: {PhotonNetwork.IsMasterClient}");

        // Only spawn if: not in a room, OR in a room and master client
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[JET SPAWNER] Not master client in room, skipping spawn");
            return;
        }

        Debug.Log($"[JET SPAWNER] spawnOnStart: {spawnOnStart}, jetPrefab: {jetPrefab}, phantomRunways: {phantomRunways?.Length ?? 0}, havocRunways: {havocRunways?.Length ?? 0}");

        if (spawnOnStart)
        {
            SpawnInitialJets();
        }
    }

    void SpawnInitialJets()
    {
        Debug.Log($"[JET SPAWNER] SpawnInitialJets called. jetsPerTeam: {jetsPerTeam}");

        // Spawn for Phantom team
        Debug.Log($"[JET SPAWNER] Phantom runways count: {phantomRunways?.Length ?? 0}");
        for (int i = 0; i < Mathf.Min(jetsPerTeam, phantomRunways != null ? phantomRunways.Length : 0); i++)
        {
            Debug.Log($"[JET SPAWNER] Trying Phantom runway {i}: {phantomRunways[i]}");
            if (phantomRunways[i] != null)
            {
                SpawnJet(phantomRunways[i], Team.Phantom);
            }
        }

        // Spawn for Havoc team
        Debug.Log($"[JET SPAWNER] Havoc runways count: {havocRunways?.Length ?? 0}");
        for (int i = 0; i < Mathf.Min(jetsPerTeam, havocRunways != null ? havocRunways.Length : 0); i++)
        {
            Debug.Log($"[JET SPAWNER] Trying Havoc runway {i}: {havocRunways[i]}");
            if (havocRunways[i] != null)
            {
                SpawnJet(havocRunways[i], Team.Havoc);
            }
        }
    }

    void SpawnJet(Runway runway, Team team)
    {
        Debug.Log($"[JET SPAWNER] SpawnJet called for {team} at runway {runway?.name}");

        if (runway == null)
        {
            Debug.LogError("[JET SPAWNER] Runway is null!");
            return;
        }
        if (jetPrefab == null)
        {
            Debug.LogError("[JET SPAWNER] Jet prefab not assigned!");
            return;
        }

        Vector3 spawnPos = runway.SpawnPosition + Vector3.up * 3f;
        Quaternion spawnRot = runway.SpawnRotation;
        Debug.Log($"[JET SPAWNER] Spawn position: {spawnPos}, rotation: {spawnRot}");

        GameObject jetObj;

        if (PhotonNetwork.InRoom)
        {
            // Networked spawn (only works when in a room)
            object[] instantiationData = new object[] { (int)team };
            jetObj = PhotonNetwork.Instantiate(
                jetPrefab.name,
                spawnPos,
                spawnRot,
                0,
                instantiationData
            );
        }
        else
        {
            // Offline/solo spawn
            jetObj = Instantiate(jetPrefab, spawnPos, spawnRot);
        }

        Debug.Log($"[JET SPAWNER] Jet instantiated at: {jetObj.transform.position}");

        JetController jet = jetObj.GetComponent<JetController>();
        if (jet != null)
        {
            jet.jetTeam = team;

            // Track for respawning
            SpawnedJet tracked = new SpawnedJet
            {
                jet = jet,
                runway = runway,
                team = team,
                respawnTimer = 0f,
                needsRespawn = false
            };
            spawnedJets.Add(tracked);

            // Spawn AI pilot
            if (spawnWithPilot && aiPrefab != null)
            {
                StartCoroutine(SpawnPilotDelayed(jet, runway, team));
            }

            runway.SetOccupied();
            Debug.Log($"[JET SPAWNER] Spawned {team} jet at {runway.name}");
        }
    }

    System.Collections.IEnumerator SpawnPilotDelayed(JetController jet, Runway runway, Team team)
    {
        // Wait for jet to initialize
        yield return new WaitForSeconds(0.5f);

        if (jet == null || jet.isDestroyed) yield break;

        // Spawn pilot near the jet
        Vector3 pilotSpawnPos = runway.SpawnPosition + Vector3.right * 5f;
        Quaternion pilotRot = Quaternion.LookRotation(jet.transform.position - pilotSpawnPos);

        // Set static prefab reference if needed
        if (AIController.aiPrefabReference == null)
        {
            AIController.aiPrefabReference = aiPrefab;
        }

        GameObject pilotObj = Instantiate(aiPrefab, pilotSpawnPos, pilotRot);
        AIController pilot = pilotObj.GetComponent<AIController>();

        if (pilot != null)
        {
            pilot.team = team;
            pilot.InitializeTeam();
            pilotObj.name = $"AI_JetPilot_{team}_{jet.name}";

            // Wait one frame for AI to initialize
            yield return null;

            // Assign as jet pilot with runway reference
            pilot.AssignAsJetPilot(jet, runway);

            Debug.Log($"[JET SPAWNER] Spawned {team} jet pilot for {jet.name} at runway {runway.name}");
        }
    }

    void Update()
    {
        // Only master client handles respawning (or offline mode)
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            return;

        // Check for destroyed jets
        for (int i = spawnedJets.Count - 1; i >= 0; i--)
        {
            SpawnedJet tracked = spawnedJets[i];

            if (tracked.needsRespawn)
            {
                tracked.respawnTimer -= Time.deltaTime;
                if (tracked.respawnTimer <= 0)
                {
                    // Respawn
                    tracked.runway.SetFree();
                    SpawnJet(tracked.runway, tracked.team);
                    spawnedJets.RemoveAt(i);
                }
            }
            else if (tracked.jet == null || tracked.jet.isDestroyed)
            {
                // Mark for respawn
                tracked.needsRespawn = true;
                tracked.respawnTimer = respawnDelay;
                Debug.Log($"[JET SPAWNER] Jet destroyed. Respawning in {respawnDelay} seconds.");
            }
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (newMasterClient.IsLocal)
        {
            Debug.Log("[JET SPAWNER] Became master client - taking over jet spawning");
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw runway connections
        Gizmos.color = Color.blue;
        if (phantomRunways != null)
        {
            foreach (var runway in phantomRunways)
            {
                if (runway != null)
                {
                    Gizmos.DrawLine(transform.position, runway.transform.position);
                    Gizmos.DrawWireSphere(runway.transform.position, 5f);
                }
            }
        }

        Gizmos.color = new Color(1f, 0.5f, 0f);
        if (havocRunways != null)
        {
            foreach (var runway in havocRunways)
            {
                if (runway != null)
                {
                    Gizmos.DrawLine(transform.position, runway.transform.position);
                    Gizmos.DrawWireSphere(runway.transform.position, 5f);
                }
            }
        }
    }
}
