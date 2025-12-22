using UnityEngine;

public class DogSpawner : MonoBehaviour
{
    [Header("Dog Prefab")]
    public GameObject dogPrefab; // Assign German Shepherd or Doberman prefab

    [Header("Spawn Settings")]
    public Team spawnTeam = Team.Phantom;
    public int dogsToSpawn = 2;
    public float spawnRadius = 3f;
    public bool spawnOnStart = true;

    [Header("Handler Settings")]
    public bool assignToNearbyHandler = true;
    public AIController specificHandler; // Optional: assign specific handler

    private int dogsSpawned = 0;

    void Start()
    {
        Debug.Log($"[DogSpawner] Start - dogPrefab assigned: {dogPrefab != null}, dogsToSpawn: {dogsToSpawn}, team: {spawnTeam}");

        if (spawnOnStart)
        {
            // Delay dog spawning to ensure AI soldiers exist first
            StartCoroutine(DelayedSpawn());
        }
    }

    System.Collections.IEnumerator DelayedSpawn()
    {
        // Wait for AI to spawn first (AISpawner has a delay)
        yield return new WaitForSeconds(5f);
        SpawnDogs();
    }

    public void SpawnDogs()
    {
        Debug.Log($"[DogSpawner] SpawnDogs called, spawning {dogsToSpawn} dogs");
        for (int i = 0; i < dogsToSpawn; i++)
        {
            SpawnDog(i);
        }
        Debug.Log($"[DogSpawner] Finished spawning, total spawned: {dogsSpawned}");
    }

    void SpawnDog(int index)
    {
        Debug.Log($"[DogSpawner] SpawnDog({index}) called, dogPrefab: {dogPrefab}");
        if (dogPrefab == null)
        {
            Debug.LogError("[DogSpawner] No dog prefab assigned!");
            return;
        }

        // Calculate spawn position
        Vector3 spawnPos = transform.position;
        if (dogsToSpawn > 1)
        {
            float angle = (360f / dogsToSpawn) * index;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRadius;
            spawnPos += offset;
        }

        // Spawn the dog
        GameObject dogObj = Instantiate(dogPrefab, spawnPos, transform.rotation);
        dogObj.name = $"K9_{spawnTeam}_{dogsSpawned}";

        Debug.Log($"[DogSpawner] Spawned dog {dogsSpawned} at {spawnPos}");

        // Setup DogController
        DogController dog = dogObj.GetComponent<DogController>();
        if (dog == null)
        {
            dog = dogObj.AddComponent<DogController>();
        }
        dog.team = spawnTeam;

        // Assign handler
        if (specificHandler != null)
        {
            dog.handler = specificHandler;
            Debug.Log($"[DogSpawner] Assigned specific handler to dog {dogsSpawned}");
        }
        else if (assignToNearbyHandler)
        {
            dog.handler = FindAvailableHandler();
            Debug.Log($"[DogSpawner] Dog {dogsSpawned} handler: {(dog.handler != null ? dog.handler.name : "NONE")}");
        }

        dogsSpawned++;
    }

    AIController FindAvailableHandler()
    {
        AIController[] allAI = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        DogController[] allDogs = FindObjectsByType<DogController>(FindObjectsSortMode.None);

        int phantomCount = 0, havocCount = 0;
        foreach (var ai in allAI)
        {
            if (ai.team == Team.Phantom) phantomCount++;
            else if (ai.team == Team.Havoc) havocCount++;
        }
        Debug.Log($"[DogSpawner] FindAvailableHandler: {allAI.Length} AI total (Phantom:{phantomCount}, Havoc:{havocCount}), looking for {spawnTeam}");

        AIController nearestAvailable = null;
        float nearestDist = 500f;

        foreach (var ai in allAI)
        {
            Debug.Log($"[DogSpawner] Checking AI: {ai.name}, team={ai.team}, state={ai.currentState}, wantTeam={spawnTeam}, match={ai.team == spawnTeam}");

            if (ai.team != spawnTeam)
            {
                continue;
            }
            if (ai.currentState == AIController.AIState.Dead) continue;

            // Skip AI in vehicles - only follow infantry
            if (ai.currentState == AIController.AIState.TankDriver ||
                ai.currentState == AIController.AIState.TankPassenger ||
                ai.currentState == AIController.AIState.HumveeDriver ||
                ai.currentState == AIController.AIState.HumveeGunner ||
                ai.currentState == AIController.AIState.HumveePassenger ||
                ai.currentState == AIController.AIState.HeliPilot ||
                ai.currentState == AIController.AIState.HeliGunner ||
                ai.currentState == AIController.AIState.HeliPassenger ||
                ai.currentState == AIController.AIState.JetPilot ||
                ai.currentState == AIController.AIState.BoardingHelicopter) continue;

            // Check if this AI already has a dog assigned
            bool alreadyTaken = false;
            foreach (var dog in allDogs)
            {
                if (dog.handler == ai)
                {
                    alreadyTaken = true;
                    break;
                }
            }
            if (alreadyTaken) continue;

            float dist = Vector3.Distance(transform.position, ai.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestAvailable = ai;
            }
        }

        return nearestAvailable;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = spawnTeam == Team.Phantom ? Color.blue : Color.red;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // Draw dog icon
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(1f, 0.5f, 2f));
    }
}
