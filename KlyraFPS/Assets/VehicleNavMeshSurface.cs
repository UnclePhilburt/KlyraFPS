using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Bakes a NavMesh specifically for vehicles (tanks) at runtime or in editor.
/// Attach this to an empty GameObject in your scene.
///
/// SETUP:
/// 1. Create empty GameObject named "VehicleNavMesh"
/// 2. Add this component
/// 3. Click "Bake Vehicle NavMesh" button in Inspector
/// </summary>
public class VehicleNavMeshSurface : MonoBehaviour
{
    [Header("Vehicle Dimensions")]
    [Tooltip("Half the width of the vehicle")]
    public float agentRadius = 2.5f;

    [Tooltip("Height of the vehicle")]
    public float agentHeight = 3f;

    [Tooltip("Maximum slope the vehicle can climb")]
    public float maxSlope = 30f;

    [Tooltip("Maximum step height")]
    public float stepHeight = 0.5f;

    [Header("Build Settings")]
    [Tooltip("Smaller = more accurate, slower bake")]
    public float voxelSize = 0.25f;

    [Tooltip("Minimum region area to keep")]
    public float minRegionArea = 4f;

    [Header("Collection")]
    [Tooltip("What geometry to include")]
    public CollectObjects collectObjects = CollectObjects.Volume;

    [Tooltip("Size of the bake volume (if using Volume)")]
    public Vector3 volumeSize = new Vector3(500f, 50f, 500f);

    [Tooltip("Layers to include in NavMesh")]
    public LayerMask includeLayers = -1;

    public enum CollectObjects { All, Volume, Children }

    // Runtime data
    private NavMeshData navMeshData;
    private NavMeshDataInstance navMeshInstance;

    [Header("Status")]
    [SerializeField] private bool isBaked = false;
    [SerializeField] private int triangleCount = 0;

    void OnEnable()
    {
        // If we have baked data, add it to the NavMesh
        if (navMeshData != null)
        {
            navMeshInstance = NavMesh.AddNavMeshData(navMeshData, transform.position, transform.rotation);
        }
    }

    void OnDisable()
    {
        // Remove our NavMesh data
        if (navMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(navMeshInstance);
        }
    }

    /// <summary>
    /// Bake the NavMesh for vehicles
    /// </summary>
    public void BakeNavMesh()
    {
        // Create build settings for vehicles
        NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByID(0);
        buildSettings.agentRadius = agentRadius;
        buildSettings.agentHeight = agentHeight;
        buildSettings.agentSlope = maxSlope;
        buildSettings.agentClimb = stepHeight;
        buildSettings.voxelSize = voxelSize;
        buildSettings.minRegionArea = minRegionArea;

        // Collect sources (geometry to bake)
        List<NavMeshBuildSource> sources = CollectSources();

        if (sources.Count == 0)
        {
            Debug.LogError("[VehicleNavMesh] No geometry found to bake!");
            return;
        }

        Debug.Log($"[VehicleNavMesh] Baking with {sources.Count} sources...");

        // Calculate bounds
        Bounds bounds = CalculateBounds();

        // Remove old data
        if (navMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(navMeshInstance);
        }

        // Bake!
        navMeshData = NavMeshBuilder.BuildNavMeshData(
            buildSettings,
            sources,
            bounds,
            transform.position,
            transform.rotation
        );

        if (navMeshData != null)
        {
            // Add to NavMesh system
            navMeshInstance = NavMesh.AddNavMeshData(navMeshData, transform.position, transform.rotation);

            // Update status
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            triangleCount = tri.indices.Length / 3;
            isBaked = true;

            Debug.Log($"[VehicleNavMesh] Bake complete! {triangleCount} triangles");
        }
        else
        {
            Debug.LogError("[VehicleNavMesh] Bake failed!");
            isBaked = false;
        }
    }

    /// <summary>
    /// Clear the baked NavMesh
    /// </summary>
    public void ClearNavMesh()
    {
        if (navMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(navMeshInstance);
        }
        navMeshData = null;
        isBaked = false;
        triangleCount = 0;
        Debug.Log("[VehicleNavMesh] Cleared");
    }

    List<NavMeshBuildSource> CollectSources()
    {
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

        // Find all MeshFilters and Terrains
        switch (collectObjects)
        {
            case CollectObjects.All:
                CollectFromAll(sources);
                break;
            case CollectObjects.Volume:
                CollectFromVolume(sources);
                break;
            case CollectObjects.Children:
                CollectFromChildren(sources);
                break;
        }

        return sources;
    }

    void CollectFromAll(List<NavMeshBuildSource> sources)
    {
        // Collect all meshes
        MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
        foreach (MeshFilter mf in meshFilters)
        {
            if (!IsInLayerMask(mf.gameObject, includeLayers)) continue;
            if (mf.sharedMesh == null) continue;

            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Mesh;
            source.sourceObject = mf.sharedMesh;
            source.transform = mf.transform.localToWorldMatrix;
            source.area = 0; // Walkable
            sources.Add(source);
        }

        // Collect all terrains
        Terrain[] terrains = FindObjectsOfType<Terrain>();
        foreach (Terrain t in terrains)
        {
            if (!IsInLayerMask(t.gameObject, includeLayers)) continue;

            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Terrain;
            source.sourceObject = t.terrainData;
            source.transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one);
            source.area = 0; // Walkable
            sources.Add(source);
        }
    }

    void CollectFromVolume(List<NavMeshBuildSource> sources)
    {
        Bounds bounds = new Bounds(transform.position, volumeSize);

        // Collect meshes in volume
        MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
        foreach (MeshFilter mf in meshFilters)
        {
            if (!IsInLayerMask(mf.gameObject, includeLayers)) continue;
            if (mf.sharedMesh == null) continue;
            if (!bounds.Intersects(mf.GetComponent<Renderer>()?.bounds ?? new Bounds())) continue;

            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Mesh;
            source.sourceObject = mf.sharedMesh;
            source.transform = mf.transform.localToWorldMatrix;
            source.area = 0;
            sources.Add(source);
        }

        // Collect terrains in volume
        Terrain[] terrains = FindObjectsOfType<Terrain>();
        foreach (Terrain t in terrains)
        {
            if (!IsInLayerMask(t.gameObject, includeLayers)) continue;

            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Terrain;
            source.sourceObject = t.terrainData;
            source.transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one);
            source.area = 0;
            sources.Add(source);
        }
    }

    void CollectFromChildren(List<NavMeshBuildSource> sources)
    {
        // Collect from child objects only
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;

            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Mesh;
            source.sourceObject = mf.sharedMesh;
            source.transform = mf.transform.localToWorldMatrix;
            source.area = 0;
            sources.Add(source);
        }

        Terrain[] terrains = GetComponentsInChildren<Terrain>();
        foreach (Terrain t in terrains)
        {
            NavMeshBuildSource source = new NavMeshBuildSource();
            source.shape = NavMeshBuildSourceShape.Terrain;
            source.sourceObject = t.terrainData;
            source.transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one);
            source.area = 0;
            sources.Add(source);
        }
    }

    Bounds CalculateBounds()
    {
        if (collectObjects == CollectObjects.Volume)
        {
            return new Bounds(transform.position, volumeSize);
        }

        // Calculate bounds from all geometry
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool first = true;

        MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
        foreach (MeshFilter mf in meshFilters)
        {
            Renderer r = mf.GetComponent<Renderer>();
            if (r == null) continue;

            if (first)
            {
                bounds = r.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        Terrain[] terrains = FindObjectsOfType<Terrain>();
        foreach (Terrain t in terrains)
        {
            Bounds tBounds = new Bounds(
                t.transform.position + t.terrainData.size / 2f,
                t.terrainData.size
            );

            if (first)
            {
                bounds = tBounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(tBounds);
            }
        }

        // Expand slightly
        bounds.Expand(10f);
        return bounds;
    }

    bool IsInLayerMask(GameObject go, LayerMask mask)
    {
        return (mask.value & (1 << go.layer)) != 0;
    }

    void OnDrawGizmosSelected()
    {
        // Draw volume bounds
        if (collectObjects == CollectObjects.Volume)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawCube(transform.position, volumeSize);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, volumeSize);
        }

        // Draw agent size preview
        Gizmos.color = Color.cyan;
        Vector3 agentSize = new Vector3(agentRadius * 2f, agentHeight, agentRadius * 2f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * agentHeight / 2f, agentSize);
    }
}
