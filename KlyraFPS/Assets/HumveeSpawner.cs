using UnityEngine;
using Photon.Pun;

/// <summary>
/// Spawns Humvees with AI driver and gunner.
/// </summary>
public class HumveeSpawner : MonoBehaviour
{
    [Header("Humvee Prefab")]
    public GameObject humveePrefab;  // SM_Veh_Light_Armored_Car_01 or similar

    [Header("AI Prefab")]
    public GameObject aiPrefab;

    [Header("Spawn Settings")]
    public Team spawnTeam = Team.Phantom;  // USA team
    public int humveesToSpawn = 1;
    public float spawnRadius = 5f;
    public bool spawnOnStart = true;
    public float spawnRotationOffset = 0f;

    [Header("AI Crew")]
    public bool spawnDriver = true;
    public bool spawnGunner = true;
    public int passengersToSpawn = 0;  // Additional infantry passengers

    [Header("Respawn")]
    public bool respawnOnDestroy = true;
    public float respawnDelay = 45f;  // Faster respawn than tanks

    private int humveesSpawned = 0;

    void Start()
    {
        if (spawnOnStart)
        {
            SpawnHumvees();
        }
    }

    public void SpawnHumvees()
    {
        for (int i = 0; i < humveesToSpawn; i++)
        {
            SpawnHumvee(i);
        }
    }

    void SpawnHumvee(int index)
    {
        if (humveePrefab == null)
        {
            Debug.LogError($"[HumveeSpawner] No humvee prefab assigned!");
            return;
        }

        // Calculate spawn position
        Vector3 spawnPos = transform.position;
        if (humveesToSpawn > 1)
        {
            float angle = (360f / humveesToSpawn) * index;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRadius;
            spawnPos += offset;
        }

        // Spawn the humvee
        Quaternion spawnRot = transform.rotation * Quaternion.Euler(0f, spawnRotationOffset, 0f);
        GameObject humveeObj = Instantiate(humveePrefab, spawnPos, spawnRot);
        humveeObj.name = $"Humvee_{spawnTeam}_{humveesSpawned}";

        // Setup humvee controller
        HumveeController humvee = humveeObj.GetComponent<HumveeController>();
        if (humvee == null)
        {
            humvee = humveeObj.AddComponent<HumveeController>();
        }
        humvee.humveeTeam = spawnTeam;

        // Add Photon components
        if (PhotonNetwork.IsConnected)
        {
            PhotonView pv = humveeObj.GetComponent<PhotonView>();
            if (pv == null)
            {
                pv = humveeObj.AddComponent<PhotonView>();
            }
        }

        // Spawn AI crew
        if (spawnDriver)
            SpawnAIDriver(humvee);
        if (spawnGunner)
            SpawnAIGunner(humvee);

        // Spawn passengers
        for (int p = 0; p < passengersToSpawn; p++)
        {
            SpawnAIPassenger(humvee);
        }

        // Setup respawn
        if (respawnOnDestroy)
        {
            HumveeRespawnTracker tracker = humveeObj.AddComponent<HumveeRespawnTracker>();
            tracker.spawner = this;
            tracker.index = index;
        }

        humveesSpawned++;
        Debug.Log($"[HumveeSpawner] Spawned {humveeObj.name} at {spawnPos}");
    }

    void SpawnAIDriver(HumveeController humvee)
    {
        AIController ai = SpawnAI(humvee.transform.position, "HumveeDriver");
        if (ai != null)
        {
            ai.AssignAsHumveeDriver(humvee);
        }
    }

    void SpawnAIGunner(HumveeController humvee)
    {
        AIController ai = SpawnAI(humvee.transform.position, "HumveeGunner");
        if (ai != null)
        {
            ai.AssignAsHumveeGunner(humvee);
        }
    }

    void SpawnAIPassenger(HumveeController humvee)
    {
        AIController ai = SpawnAI(humvee.transform.position, "HumveePassenger");
        if (ai != null)
        {
            humvee.AddPassenger(ai);
            // Set AI state to passenger
            ai.SetAsVehiclePassenger(humvee.gameObject);
        }
    }

    AIController SpawnAI(Vector3 basePos, string namePrefix)
    {
        GameObject prefab = aiPrefab;
        if (prefab == null && AIController.aiPrefabReference != null)
        {
            prefab = AIController.aiPrefabReference;
            Debug.Log("[HumveeSpawner] Using AIController.aiPrefabReference");
        }

        if (prefab == null)
        {
            Debug.LogError("[HumveeSpawner] No AI prefab assigned! Assign aiPrefab in inspector.");
            return null;
        }

        Debug.Log($"[HumveeSpawner] Spawning AI: {namePrefix} using prefab {prefab.name}");

        Vector3 aiSpawnPos = basePos + Vector3.up * 2f;
        GameObject aiObj = Instantiate(prefab, aiSpawnPos, Quaternion.identity);
        aiObj.name = $"{namePrefix}_{humveesSpawned}";

        AIController ai = aiObj.GetComponent<AIController>();
        if (ai != null)
        {
            ai.team = spawnTeam;
            ai.InitializeTeam();
        }
        else
        {
            Debug.LogError("[HumveeSpawner] AI prefab missing AIController!");
            Destroy(aiObj);
            return null;
        }

        return ai;
    }

    public void OnHumveeDestroyed(int index)
    {
        if (respawnOnDestroy)
        {
            StartCoroutine(RespawnHumveeCoroutine(index));
        }
    }

    System.Collections.IEnumerator RespawnHumveeCoroutine(int index)
    {
        yield return new WaitForSeconds(respawnDelay);
        SpawnHumvee(index);
    }
}

public class HumveeRespawnTracker : MonoBehaviour
{
    public HumveeSpawner spawner;
    public int index;

    void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnHumveeDestroyed(index);
        }
    }
}
