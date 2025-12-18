using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class CapturePoint : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Point Settings")]
    public string pointName = "A";
    public float captureRadius = 10f;
    public float captureTime = 10f; // Seconds to fully capture

    [Header("Visual")]
    public Color neutralColor = Color.white;
    public Color phantomColor = Color.blue;
    public Color havocColor = Color.red;

    // Current state
    public Team owningTeam = Team.None;
    public float captureProgress = 0f; // -1 to 1 (-1 = Havoc, 0 = Neutral, 1 = Phantom)
    public bool isContested = false;

    private List<FPSControllerPhoton> playersInZone = new List<FPSControllerPhoton>();
    private MeshRenderer flagRenderer;
    private MeshRenderer zoneRenderer;
    private Light pointLight;

    // Cached for performance
    private static FPSControllerPhoton[] cachedPlayers;
    private static AIController[] cachedAIs;
    private static float playerCacheTimer = 0f;
    private float updateTimer = 0f;

    // Count of each team in zone (players + AI)
    private int phantomInZone = 0;
    private int havocInZone = 0;

    void Start()
    {
        // Create visual indicator
        SetupVisuals();
    }

    void SetupVisuals()
    {
        // Create a flag pole / visual marker
        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        flag.name = "FlagPole";
        flag.transform.SetParent(transform);
        flag.transform.localPosition = Vector3.up * 5f;
        flag.transform.localScale = new Vector3(0.5f, 5f, 0.5f);

        flagRenderer = flag.GetComponent<MeshRenderer>();
        flagRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        flagRenderer.material.color = neutralColor;

        // Remove collider from flag
        Destroy(flag.GetComponent<Collider>());

        // Add point light (no shadows for performance)
        GameObject lightObj = new GameObject("PointLight");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = Vector3.up * 12f;
        pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = neutralColor;
        pointLight.intensity = 5f;
        pointLight.range = 20f;
        pointLight.shadows = LightShadows.None;

        // Create capture zone indicator (flat cylinder on ground)
        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        zone.name = "CaptureZone";
        zone.transform.SetParent(transform);
        zone.transform.localPosition = Vector3.up * 0.1f;
        zone.transform.localScale = new Vector3(captureRadius * 2f, 0.1f, captureRadius * 2f);

        zoneRenderer = zone.GetComponent<MeshRenderer>();
        Material zoneMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        // Enable transparency
        zoneMat.SetFloat("_Surface", 1); // 0 = Opaque, 1 = Transparent
        zoneMat.SetFloat("_Blend", 0); // Alpha blend
        zoneMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        zoneMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        zoneMat.SetInt("_ZWrite", 0);
        zoneMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        zoneMat.renderQueue = 3000;
        Color zoneColor = neutralColor;
        zoneColor.a = 0.15f;
        zoneMat.SetColor("_BaseColor", zoneColor);
        zoneRenderer.material = zoneMat;

        // Remove collider from zone visual
        Destroy(zone.GetComponent<Collider>());
    }

    void Update()
    {
        // Only update 2 times per second
        updateTimer -= Time.deltaTime;
        if (updateTimer > 0f) return;
        updateTimer = 0.5f;

        // Find players and AI in zone
        UpdatePlayersInZone();

        // Determine capture state (now includes AI bots)
        isContested = (phantomInZone > 0 && havocInZone > 0);

        if (!isContested)
        {
            // captureSpeed = how much progress per second per person
            // We update every 0.5 seconds, so multiply by 0.5
            float captureSpeed = (1f / captureTime) * 0.5f;

            if (phantomInZone > havocInZone)
            {
                // Phantom capturing - progress goes toward 1
                captureProgress += captureSpeed * phantomInZone;
                captureProgress = Mathf.Clamp(captureProgress, -1f, 1f);
            }
            else if (havocInZone > phantomInZone)
            {
                // Havoc capturing - progress goes toward -1
                captureProgress -= captureSpeed * havocInZone;
                captureProgress = Mathf.Clamp(captureProgress, -1f, 1f);
            }
            else if (phantomInZone == 0 && havocInZone == 0)
            {
                // Nobody in zone - decay toward neutral slowly if not owned
                // But if owned, stay owned
            }
        }

        // Update ownership based on progress
        if (captureProgress >= 1f)
        {
            owningTeam = Team.Phantom;
        }
        else if (captureProgress <= -1f)
        {
            owningTeam = Team.Havoc;
        }
        else if (Mathf.Abs(captureProgress) < 0.01f)
        {
            owningTeam = Team.None;
        }
    }

    void LateUpdate()
    {
        // Update visuals (runs on all clients)
        UpdateVisuals();
    }

    void UpdatePlayersInZone()
    {
        playersInZone.Clear();
        phantomInZone = 0;
        havocInZone = 0;

        float sqrRadius = captureRadius * captureRadius;

        // Cache players and AI - shared across all capture points
        playerCacheTimer -= Time.deltaTime;
        if (playerCacheTimer <= 0f || cachedPlayers == null)
        {
            cachedPlayers = FindObjectsOfType<FPSControllerPhoton>();
            cachedAIs = FindObjectsOfType<AIController>();
            playerCacheTimer = 1f;
        }

        // Count players in zone
        if (cachedPlayers != null)
        {
            foreach (var player in cachedPlayers)
            {
                if (player == null) continue;

                // Use squared distance (faster - no sqrt)
                float dx = transform.position.x - player.transform.position.x;
                float dz = transform.position.z - player.transform.position.z;
                float sqrDist = dx * dx + dz * dz;

                if (sqrDist <= sqrRadius)
                {
                    playersInZone.Add(player);
                    if (player.playerTeam == Team.Phantom) phantomInZone++;
                    else if (player.playerTeam == Team.Havoc) havocInZone++;
                }
            }
        }

        // Count AI bots in zone
        if (cachedAIs != null)
        {
            foreach (var ai in cachedAIs)
            {
                if (ai == null) continue;
                if (ai.currentState == AIController.AIState.Dead) continue;

                float dx = transform.position.x - ai.transform.position.x;
                float dz = transform.position.z - ai.transform.position.z;
                float sqrDist = dx * dx + dz * dz;

                if (sqrDist <= sqrRadius)
                {
                    if (ai.team == Team.Phantom) phantomInZone++;
                    else if (ai.team == Team.Havoc) havocInZone++;
                }
            }
        }
    }

    void UpdateVisuals()
    {
        Color targetColor;

        if (owningTeam == Team.Phantom)
        {
            targetColor = phantomColor;
        }
        else if (owningTeam == Team.Havoc)
        {
            targetColor = havocColor;
        }
        else
        {
            // Lerp between colors based on progress
            if (captureProgress > 0)
            {
                targetColor = Color.Lerp(neutralColor, phantomColor, captureProgress);
            }
            else
            {
                targetColor = Color.Lerp(neutralColor, havocColor, -captureProgress);
            }
        }

        if (isContested)
        {
            // Flash when contested
            float flash = Mathf.PingPong(Time.time * 4f, 1f);
            targetColor = Color.Lerp(targetColor, Color.yellow, flash * 0.5f);
        }

        if (flagRenderer != null)
        {
            flagRenderer.material.color = targetColor;
        }

        if (pointLight != null)
        {
            pointLight.color = targetColor;
        }

        if (zoneRenderer != null)
        {
            Color zoneColor = targetColor;
            zoneColor.a = 0.15f;
            zoneRenderer.material.SetColor("_BaseColor", zoneColor);
        }
    }

    // Network sync
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext((int)owningTeam);
            stream.SendNext(captureProgress);
            stream.SendNext(isContested);
        }
        else
        {
            owningTeam = (Team)(int)stream.ReceiveNext();
            captureProgress = (float)stream.ReceiveNext();
            isContested = (bool)stream.ReceiveNext();
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw capture radius in editor
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, captureRadius);
    }
}
