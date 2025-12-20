using UnityEngine;
using Photon.Pun;

public class TankShell : MonoBehaviour
{
    [Header("Damage")]
    public float damage = 150f;
    public float explosionRadius = 5f;
    public float explosionForce = 1000f;

    [Header("Team")]
    public Team ownerTeam = Team.None;

    [Header("Effects")]
    public GameObject explosionPrefab;
    public AudioClip explosionSound;

    private Rigidbody rb;
    private bool hasExploded = false;
    private float lifetime = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity = true;
        rb.mass = 10f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Add collider if missing
        if (GetComponent<Collider>() == null)
        {
            SphereCollider col = gameObject.AddComponent<SphereCollider>();
            col.radius = 0.1f;
        }

        // Create a simple visual if no mesh
        if (GetComponent<MeshRenderer>() == null)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.3f;
            Destroy(visual.GetComponent<Collider>());

            // Make it look like a shell
            MeshRenderer mr = visual.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material = new Material(Shader.Find("Standard"));
                mr.material.color = new Color(0.3f, 0.3f, 0.2f);
            }
        }
    }

    void Update()
    {
        lifetime += Time.deltaTime;

        // Auto-destroy after 10 seconds
        if (lifetime > 10f && !hasExploded)
        {
            Explode();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;

        // Don't explode on friendly units
        AIController hitAI = collision.gameObject.GetComponentInParent<AIController>();
        if (hitAI != null && hitAI.team == ownerTeam) return;

        FPSControllerPhoton hitPlayer = collision.gameObject.GetComponentInParent<FPSControllerPhoton>();
        if (hitPlayer != null && hitPlayer.playerTeam == ownerTeam) return;

        TankController hitTank = collision.gameObject.GetComponentInParent<TankController>();
        if (hitTank != null && hitTank.TankTeam == ownerTeam) return;

        Explode();
    }

    void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Vector3 explosionPos = transform.position;

        // Spawn explosion effect
        SpawnExplosionEffect(explosionPos);

        // Play explosion sound
        if (explosionSound != null)
        {
            AudioSource.PlayClipAtPoint(explosionSound, explosionPos, 1f);
        }

        // Deal damage to everything in radius
        Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
        foreach (Collider hit in hits)
        {
            // Calculate damage falloff based on distance
            float distance = Vector3.Distance(explosionPos, hit.transform.position);
            float damageMultiplier = 1f - (distance / explosionRadius);
            damageMultiplier = Mathf.Clamp01(damageMultiplier);
            float finalDamage = damage * damageMultiplier;

            // Check for AI
            AIController ai = hit.GetComponentInParent<AIController>();
            if (ai != null && ai.team != ownerTeam && !ai.isDead)
            {
                ai.TakeDamage(finalDamage, explosionPos, hit.transform.position, gameObject);
                continue;
            }

            // Check for player
            FPSControllerPhoton player = hit.GetComponentInParent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != ownerTeam && !player.isDead)
            {
                player.TakeDamage(finalDamage, -1); // -1 for non-player attacker
                continue;
            }

            // Check for tank
            TankController tank = hit.GetComponentInParent<TankController>();
            if (tank != null && tank.TankTeam != ownerTeam && !tank.isDestroyed)
            {
                tank.TakeDamage(finalDamage, explosionPos, gameObject);
                continue;
            }

            // Check for helicopter
            HelicopterController heli = hit.GetComponentInParent<HelicopterController>();
            if (heli != null && heli.helicopterTeam != ownerTeam && !heli.isDestroyed)
            {
                heli.TakeDamage(finalDamage, -1); // -1 for non-player attacker
                continue;
            }

            // Apply explosion force to rigidbodies
            Rigidbody hitRb = hit.GetComponent<Rigidbody>();
            if (hitRb != null)
            {
                hitRb.AddExplosionForce(explosionForce, explosionPos, explosionRadius);
            }
        }

        // Destroy the shell
        Destroy(gameObject);
    }

    void SpawnExplosionEffect(Vector3 position)
    {
        if (explosionPrefab != null)
        {
            GameObject explosion = Instantiate(explosionPrefab, position, Quaternion.identity);
            Destroy(explosion, 3f);
        }
        else
        {
            // Create simple explosion effect
            GameObject explosion = new GameObject("TankExplosion");
            explosion.transform.position = position;

            // Add light
            Light explosionLight = explosion.AddComponent<Light>();
            explosionLight.type = LightType.Point;
            explosionLight.color = new Color(1f, 0.6f, 0.2f);
            explosionLight.intensity = 5f;
            explosionLight.range = 15f;

            // Add particle system for smoke
            ParticleSystem ps = explosion.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = 3f;
            main.startLifetime = 2f;
            main.startSpeed = 5f;
            main.startColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 50;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] {
                new ParticleSystem.Burst(0f, 30)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, 2f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1f, 0.5f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 0.3f),
                    new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            // Fade out the light
            explosion.AddComponent<ExplosionLightFade>();

            Destroy(explosion, 3f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}

// Helper component to fade explosion light
public class ExplosionLightFade : MonoBehaviour
{
    private Light explosionLight;
    private float startIntensity;
    private float fadeTime = 0.5f;
    private float elapsed = 0f;

    void Start()
    {
        explosionLight = GetComponent<Light>();
        if (explosionLight != null)
            startIntensity = explosionLight.intensity;
    }

    void Update()
    {
        if (explosionLight == null) return;

        elapsed += Time.deltaTime;
        explosionLight.intensity = Mathf.Lerp(startIntensity, 0f, elapsed / fadeTime);

        if (elapsed >= fadeTime)
        {
            Destroy(explosionLight);
            Destroy(this);
        }
    }
}
