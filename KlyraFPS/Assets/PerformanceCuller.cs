using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Performance culling manager - handles distance culling, AI throttling, and effect culling.
/// Attach to a GameObject in the scene (or it will create itself).
/// </summary>
public class PerformanceCuller : MonoBehaviour
{
    public static PerformanceCuller Instance { get; private set; }

    [Header("Player Reference")]
    [Tooltip("Auto-finds player camera if not set")]
    public Transform playerTransform;

    [Header("AI Culling")]
    [Tooltip("Distance at which AI updates less frequently")]
    public float aiSlowUpdateDistance = 100f;
    [Tooltip("Distance at which AI updates very rarely")]
    public float aiMinimalUpdateDistance = 200f;
    [Tooltip("Distance at which AI is completely paused")]
    public float aiPauseDistance = 400f;

    [Header("Effect Culling")]
    [Tooltip("Distance at which particle effects are hidden")]
    public float particleCullDistance = 150f;
    [Tooltip("Distance at which audio sources are muted")]
    public float audioCullDistance = 200f;

    [Header("Object Culling")]
    [Tooltip("Distance at which small props are hidden")]
    public float smallPropCullDistance = 100f;
    [Tooltip("Max size to be considered a small prop")]
    public float smallPropMaxSize = 2f;

    [Header("Shadow Culling")]
    [Tooltip("Distance at which shadows are disabled on lights")]
    public float shadowCullDistance = 80f;

    [Header("Update Settings")]
    [Tooltip("How often to update culling (seconds)")]
    public float cullUpdateInterval = 0.5f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // Cached references
    private Camera mainCamera;
    private float lastCullUpdate = 0f;

    // Tracked objects
    private List<CulledAI> culledAIs = new List<CulledAI>();
    private List<CulledEffect> culledEffects = new List<CulledEffect>();
    private List<CulledLight> culledLights = new List<CulledLight>();

    // Stats
    private int aisPaused = 0;
    private int aisSlowed = 0;
    private int effectsCulled = 0;
    private int shadowsCulled = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Enable occlusion culling if available
        if (Camera.main != null)
        {
            Camera.main.useOcclusionCulling = true;
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
        FindPlayerTransform();

        // Initial registration of all cullable objects
        RegisterAllCullables();
    }

    void FindPlayerTransform()
    {
        if (playerTransform != null) return;

        // Try to find local player
        var players = FindObjectsByType<FPSControllerPhoton>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            if (player.photonView != null && player.photonView.IsMine)
            {
                playerTransform = player.transform;
                break;
            }
        }

        // Fallback to camera
        if (playerTransform == null && mainCamera != null)
        {
            playerTransform = mainCamera.transform;
        }
    }

    void RegisterAllCullables()
    {
        // Register AI controllers
        var ais = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        foreach (var ai in ais)
        {
            RegisterAI(ai);
        }

        // Register particle systems
        var particles = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        foreach (var ps in particles)
        {
            RegisterEffect(ps);
        }

        // Register lights with shadows
        var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var light in lights)
        {
            if (light.shadows != LightShadows.None && light.type != LightType.Directional)
            {
                RegisterLight(light);
            }
        }

        Debug.Log($"[PerformanceCuller] Registered {culledAIs.Count} AIs, {culledEffects.Count} effects, {culledLights.Count} shadow lights");
    }

    public void RegisterAI(AIController ai)
    {
        if (ai == null) return;

        // Check if already registered
        foreach (var c in culledAIs)
        {
            if (c.ai == ai) return;
        }

        culledAIs.Add(new CulledAI { ai = ai, updateRate = AIUpdateRate.Full });
    }

    public void RegisterEffect(ParticleSystem ps)
    {
        if (ps == null) return;

        // Skip UI particles
        if (ps.GetComponentInParent<Canvas>() != null) return;

        foreach (var c in culledEffects)
        {
            if (c.particleSystem == ps) return;
        }

        culledEffects.Add(new CulledEffect
        {
            particleSystem = ps,
            originalEmission = ps.emission.enabled,
            isCulled = false
        });
    }

    public void RegisterLight(Light light)
    {
        if (light == null) return;

        foreach (var c in culledLights)
        {
            if (c.light == light) return;
        }

        culledLights.Add(new CulledLight
        {
            light = light,
            originalShadows = light.shadows,
            shadowsCulled = false
        });
    }

    void Update()
    {
        if (playerTransform == null)
        {
            FindPlayerTransform();
            return;
        }

        // Throttle culling updates
        if (Time.time - lastCullUpdate < cullUpdateInterval) return;
        lastCullUpdate = Time.time;

        UpdateCulling();
    }

    void UpdateCulling()
    {
        Vector3 playerPos = playerTransform.position;

        // Reset stats
        aisPaused = 0;
        aisSlowed = 0;
        effectsCulled = 0;
        shadowsCulled = 0;

        // Update AI culling
        UpdateAICulling(playerPos);

        // Update effect culling
        UpdateEffectCulling(playerPos);

        // Update shadow culling
        UpdateShadowCulling(playerPos);

        // Clean up destroyed objects
        CleanupDestroyedObjects();
    }

    void UpdateAICulling(Vector3 playerPos)
    {
        foreach (var culled in culledAIs)
        {
            if (culled.ai == null) continue;

            float dist = Vector3.Distance(playerPos, culled.ai.transform.position);

            AIUpdateRate newRate;
            if (dist > aiPauseDistance)
            {
                newRate = AIUpdateRate.Paused;
                aisPaused++;
            }
            else if (dist > aiMinimalUpdateDistance)
            {
                newRate = AIUpdateRate.Minimal;
                aisSlowed++;
            }
            else if (dist > aiSlowUpdateDistance)
            {
                newRate = AIUpdateRate.Slow;
                aisSlowed++;
            }
            else
            {
                newRate = AIUpdateRate.Full;
            }

            if (newRate != culled.updateRate)
            {
                culled.updateRate = newRate;
                ApplyAIUpdateRate(culled.ai, newRate);
            }
        }
    }

    void ApplyAIUpdateRate(AIController ai, AIUpdateRate rate)
    {
        switch (rate)
        {
            case AIUpdateRate.Full:
                ai.enabled = true;
                ai.SetCullUpdateInterval(0f);  // Full rate
                break;
            case AIUpdateRate.Slow:
                ai.enabled = true;
                ai.SetCullUpdateInterval(0.2f);  // 5 updates/sec
                break;
            case AIUpdateRate.Minimal:
                ai.enabled = true;
                ai.SetCullUpdateInterval(0.5f);  // 2 updates/sec
                break;
            case AIUpdateRate.Paused:
                ai.enabled = false;
                break;
        }
    }

    void UpdateEffectCulling(Vector3 playerPos)
    {
        foreach (var culled in culledEffects)
        {
            if (culled.particleSystem == null) continue;

            float dist = Vector3.Distance(playerPos, culled.particleSystem.transform.position);
            bool shouldCull = dist > particleCullDistance;

            if (shouldCull != culled.isCulled)
            {
                culled.isCulled = shouldCull;

                var emission = culled.particleSystem.emission;
                emission.enabled = shouldCull ? false : culled.originalEmission;

                if (shouldCull)
                {
                    culled.particleSystem.Clear();
                }
            }

            if (culled.isCulled) effectsCulled++;
        }
    }

    void UpdateShadowCulling(Vector3 playerPos)
    {
        foreach (var culled in culledLights)
        {
            if (culled.light == null) continue;

            float dist = Vector3.Distance(playerPos, culled.light.transform.position);
            bool shouldCull = dist > shadowCullDistance;

            if (shouldCull != culled.shadowsCulled)
            {
                culled.shadowsCulled = shouldCull;
                culled.light.shadows = shouldCull ? LightShadows.None : culled.originalShadows;
            }

            if (culled.shadowsCulled) shadowsCulled++;
        }
    }

    void CleanupDestroyedObjects()
    {
        culledAIs.RemoveAll(c => c.ai == null);
        culledEffects.RemoveAll(c => c.particleSystem == null);
        culledLights.RemoveAll(c => c.light == null);
    }

    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(10, 200, 250, 150));
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Performance Culler</b>");
        GUILayout.Label($"AIs: {culledAIs.Count} ({aisPaused} paused, {aisSlowed} slowed)");
        GUILayout.Label($"Effects: {culledEffects.Count} ({effectsCulled} culled)");
        GUILayout.Label($"Lights: {culledLights.Count} ({shadowsCulled} shadows culled)");
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // Classes for tracking culled objects
    private class CulledAI
    {
        public AIController ai;
        public AIUpdateRate updateRate;
    }

    private class CulledEffect
    {
        public ParticleSystem particleSystem;
        public bool originalEmission;
        public bool isCulled;
    }

    private class CulledLight
    {
        public Light light;
        public LightShadows originalShadows;
        public bool shadowsCulled;
    }

    private enum AIUpdateRate
    {
        Full,
        Slow,
        Minimal,
        Paused
    }
}
