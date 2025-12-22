using UnityEngine;

public class BulletTracer : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private float fadeSpeed = 10f;
    private float currentAlpha = 1f;

    // Static shared material to prevent memory leak (one material for all tracers)
    private static Material sharedTracerMaterial;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        // Setup line renderer
        lineRenderer.startWidth = 0.02f;
        lineRenderer.endWidth = 0.01f;
        lineRenderer.positionCount = 2;

        // Use shared material to prevent memory leak
        if (sharedTracerMaterial == null)
        {
            // Try multiple shaders for WebGL compatibility
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");

            if (shader != null)
            {
                sharedTracerMaterial = new Material(shader);
            }
            else
            {
                // Ultimate fallback - create basic material
                sharedTracerMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                Debug.LogWarning("[BulletTracer] Could not find suitable shader, using fallback");
            }
        }
        lineRenderer.material = sharedTracerMaterial;
        lineRenderer.startColor = new Color(1f, 0.8f, 0.2f, 1f); // Yellow/orange
        lineRenderer.endColor = new Color(1f, 0.5f, 0.1f, 0.5f);
    }

    public void SetPositions(Vector3 start, Vector3 end)
    {
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
        currentAlpha = 1f;
    }

    void Update()
    {
        // Fade out
        currentAlpha -= Time.deltaTime * fadeSpeed;

        if (currentAlpha <= 0)
        {
            Destroy(gameObject);
            return;
        }

        // Update colors with fade
        Color startCol = new Color(1f, 0.8f, 0.2f, currentAlpha);
        Color endCol = new Color(1f, 0.5f, 0.1f, currentAlpha * 0.5f);
        lineRenderer.startColor = startCol;
        lineRenderer.endColor = endCol;
    }
}
