using UnityEngine;

public class BulletImpact : MonoBehaviour
{
    private static int maxImpacts = 50;
    private static System.Collections.Generic.Queue<GameObject> impactPool = new System.Collections.Generic.Queue<GameObject>();

    public float lifetime = 5f;

    public static void SpawnImpact(Vector3 position, Vector3 normal)
    {
        // Remove old impacts if at limit
        while (impactPool.Count >= maxImpacts)
        {
            GameObject old = impactPool.Dequeue();
            if (old != null) Destroy(old);
        }

        // Create impact decal
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Quad);
        impact.name = "BulletImpact";

        // Remove collider
        Destroy(impact.GetComponent<Collider>());

        // Position and rotate to face away from surface
        impact.transform.position = position + normal * 0.01f; // Slight offset to prevent z-fighting
        impact.transform.rotation = Quaternion.LookRotation(-normal);
        impact.transform.localScale = Vector3.one * 0.15f;

        // Dark material for bullet hole look
        Renderer renderer = impact.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        renderer.material = mat;

        // Add component and track
        BulletImpact impactScript = impact.AddComponent<BulletImpact>();
        impactPool.Enqueue(impact);
    }

    void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
