using UnityEngine;
using Photon.Pun;

public class TankSpawner : MonoBehaviour
{
    [Header("Tank Prefabs")]
    public GameObject usaTankPrefab;      // SM_Veh_Tank_USA_01
    public GameObject russianTankPrefab;  // SM_Veh_Tank_Russian_01

    [Header("AI Prefab")]
    public GameObject aiPrefab;  // The AI soldier prefab

    [Header("Spawn Settings")]
    public Team spawnTeam = Team.Phantom;
    public int tanksToSpawn = 1;
    public float spawnRadius = 5f;
    public bool spawnOnStart = true;
    [Tooltip("Extra rotation applied to tank on spawn (Y axis)")]
    public float spawnRotationOffset = 0f;

    [Header("Respawn")]
    public bool respawnOnDestroy = true;
    public float respawnDelay = 60f;

    private int tanksSpawned = 0;

    void Start()
    {
        if (spawnOnStart)
        {
            SpawnTanks();
        }
    }

    public void SpawnTanks()
    {
        for (int i = 0; i < tanksToSpawn; i++)
        {
            SpawnTank(i);
        }
    }

    void SpawnTank(int index)
    {
        // Choose tank prefab based on team
        GameObject tankPrefab = spawnTeam == Team.Phantom ? usaTankPrefab : russianTankPrefab;

        if (tankPrefab == null)
        {
            Debug.LogError($"[TankSpawner] No tank prefab assigned for team {spawnTeam}!");
            return;
        }

        // Calculate spawn position
        Vector3 spawnPos = transform.position;
        if (tanksToSpawn > 1)
        {
            // Offset for multiple tanks
            float angle = (360f / tanksToSpawn) * index;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRadius;
            spawnPos += offset;
        }

        // Spawn the tank with rotation offset
        Quaternion spawnRot = transform.rotation * Quaternion.Euler(0f, spawnRotationOffset, 0f);
        GameObject tankObj = Instantiate(tankPrefab, spawnPos, spawnRot);
        tankObj.name = $"Tank_{spawnTeam}_{tanksSpawned}";

        // Setup tank controller
        TankController tank = tankObj.GetComponent<TankController>();
        if (tank == null)
        {
            tank = tankObj.AddComponent<TankController>();
        }
        tank.tankTeam = spawnTeam;

        // Add Photon components if in a multiplayer room
        if (PhotonNetwork.InRoom)
        {
            PhotonView pv = tankObj.GetComponent<PhotonView>();
            if (pv == null)
            {
                pv = tankObj.AddComponent<PhotonView>();
            }
        }

        // Spawn AI driver
        SpawnAIDriver(tank);

        // Setup respawn callback
        if (respawnOnDestroy)
        {
            TankRespawnTracker tracker = tankObj.AddComponent<TankRespawnTracker>();
            tracker.spawner = this;
            tracker.index = index;
        }

        tanksSpawned++;
        Debug.Log($"[TankSpawner] Spawned {tankObj.name} at {spawnPos}");
    }

    void SpawnAIDriver(TankController tank)
    {
        if (aiPrefab == null)
        {
            // Try to find AI prefab reference
            if (AIController.aiPrefabReference != null)
            {
                aiPrefab = AIController.aiPrefabReference;
            }
            else
            {
                Debug.LogWarning("[TankSpawner] No AI prefab assigned!");
                return;
            }
        }

        // Spawn AI near tank
        Vector3 aiSpawnPos = tank.transform.position + Vector3.up * 2f;
        GameObject aiObj = Instantiate(aiPrefab, aiSpawnPos, tank.transform.rotation);
        aiObj.name = $"TankDriver_{tank.name}";

        AIController ai = aiObj.GetComponent<AIController>();
        if (ai != null)
        {
            ai.team = spawnTeam;
            ai.InitializeTeam();  // This sets isAIControlled = true
            ai.AssignAsTankDriver(tank);
        }
        else
        {
            Debug.LogError("[TankSpawner] AI prefab missing AIController component!");
            Destroy(aiObj);
        }
    }

    public void OnTankDestroyed(int index)
    {
        if (respawnOnDestroy)
        {
            StartCoroutine(RespawnTankCoroutine(index));
        }
    }

    System.Collections.IEnumerator RespawnTankCoroutine(int index)
    {
        yield return new WaitForSeconds(respawnDelay);
        SpawnTank(index);
    }
}

// Helper component to track tank destruction
public class TankRespawnTracker : MonoBehaviour
{
    public TankSpawner spawner;
    public int index;

    void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnTankDestroyed(index);
        }
    }
}
