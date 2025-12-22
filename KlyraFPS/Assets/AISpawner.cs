using UnityEngine;
using System.Collections.Generic;

public class AISpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public GameObject aiPrefab;
    public int botsPerTeam = 4;
    public float spawnDelay = 3f; // Delay between spawns (staggered for performance)

    [Header("Spawn Points")]
    public Transform phantomSpawnArea;
    public Transform havocSpawnArea;
    public float spawnRadius = 5f;

    [Header("Respawn")]
    public bool respawnBots = true;
    public float respawnDelay = 10f;

    // Tracking
    private List<AIController> phantomBots = new List<AIController>();
    private List<AIController> havocBots = new List<AIController>();
    private float spawnTimer = 0f;
    private int phantomSpawned = 0;
    private int havocSpawned = 0;
    private bool initialSpawnComplete = false;

    void Start()
    {
        // Set the static prefab reference so AI can respawn themselves
        if (aiPrefab != null)
        {
            AIController.aiPrefabReference = aiPrefab;
        }

        // Start spawning after a short delay
        spawnTimer = 1f;
    }

    void Update()
    {
        // Initial spawn
        if (!initialSpawnComplete)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                SpawnNextBot();
                spawnTimer = spawnDelay;

                if (phantomSpawned >= botsPerTeam && havocSpawned >= botsPerTeam)
                {
                    initialSpawnComplete = true;
                    Debug.Log($"Initial spawn complete: {phantomSpawned} Phantom bots, {havocSpawned} Havoc bots");
                }
            }
        }

        // Respawn dead bots
        if (respawnBots && initialSpawnComplete)
        {
            CleanupDeadBots();
        }
    }

    void SpawnNextBot()
    {
        if (aiPrefab == null)
        {
            Debug.LogError("AI Prefab not assigned!");
            return;
        }

        // Alternate between teams
        Team teamToSpawn;
        Transform spawnArea;

        if (phantomSpawned <= havocSpawned && phantomSpawned < botsPerTeam)
        {
            teamToSpawn = Team.Phantom;
            spawnArea = phantomSpawnArea;
            phantomSpawned++;
        }
        else if (havocSpawned < botsPerTeam)
        {
            teamToSpawn = Team.Havoc;
            spawnArea = havocSpawnArea;
            havocSpawned++;
        }
        else
        {
            return; // All bots spawned
        }

        SpawnBot(teamToSpawn, spawnArea);
    }

    void SpawnBot(Team team, Transform spawnArea)
    {
        Vector3 spawnPos = transform.position;

        if (spawnArea != null)
        {
            // Random position within spawn radius
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            spawnPos = spawnArea.position + new Vector3(randomCircle.x, 0, randomCircle.y);
        }

        // Rotate Phantom bots 180 degrees so they face the right direction
        Quaternion spawnRot = team == Team.Phantom ? Quaternion.Euler(0, 180f, 0) : Quaternion.identity;

        GameObject bot = Instantiate(aiPrefab, spawnPos, spawnRot);
        AIController ai = bot.GetComponent<AIController>();

        if (ai != null)
        {
            ai.team = team;
            ai.InitializeTeam(); // Call this AFTER setting team

            int botIndex = team == Team.Phantom ? phantomBots.Count : havocBots.Count;
            bot.name = $"AI_{team}_{botIndex}";

            if (team == Team.Phantom)
                phantomBots.Add(ai);
            else
                havocBots.Add(ai);

            Debug.Log($"Spawned {team} bot at {spawnPos}");
        }
    }

    void CleanupDeadBots()
    {
        // Remove dead bots from lists and respawn
        for (int i = phantomBots.Count - 1; i >= 0; i--)
        {
            if (phantomBots[i] == null || phantomBots[i].currentState == AIController.AIState.Dead)
            {
                phantomBots.RemoveAt(i);
                // Queue respawn
                StartCoroutine(RespawnBot(Team.Phantom, respawnDelay));
            }
        }

        for (int i = havocBots.Count - 1; i >= 0; i--)
        {
            if (havocBots[i] == null || havocBots[i].currentState == AIController.AIState.Dead)
            {
                havocBots.RemoveAt(i);
                // Queue respawn
                StartCoroutine(RespawnBot(Team.Havoc, respawnDelay));
            }
        }
    }

    System.Collections.IEnumerator RespawnBot(Team team, float delay)
    {
        yield return new WaitForSeconds(delay);

        Transform spawnArea = team == Team.Phantom ? phantomSpawnArea : havocSpawnArea;
        SpawnBot(team, spawnArea);
    }

    void OnDrawGizmosSelected()
    {
        // Draw spawn areas
        if (phantomSpawnArea != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(phantomSpawnArea.position, spawnRadius);
        }

        if (havocSpawnArea != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(havocSpawnArea.position, spawnRadius);
        }
    }
}
