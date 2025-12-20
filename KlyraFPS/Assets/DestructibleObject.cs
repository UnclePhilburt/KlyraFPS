using UnityEngine;

/// <summary>
/// Destructible prop/obstacle that can be destroyed by damage or vehicle collision
/// Attach this to barricades, fences, crates, etc.
/// </summary>
public class DestructibleObject : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 50f;
    public float currentHealth;

    [Header("Destruction")]
    [Tooltip("Prefab to spawn when destroyed (broken version, debris, etc.)")]
    public GameObject destroyedPrefab;
    [Tooltip("Effect to spawn on destruction (explosion, dust, etc.)")]
    public GameObject destructionEffect;
    [Tooltip("Sound to play on destruction")]
    public AudioClip destructionSound;
    [Tooltip("Volume of destruction sound")]
    public float destructionVolume = 1f;

    [Header("Debris")]
    [Tooltip("Apply force to destroyed prefab pieces")]
    public bool applyDebrisForce = true;
    [Tooltip("Force applied to debris pieces")]
    public float debrisForce = 500f;
    [Tooltip("Upward force on debris")]
    public float debrisUpwardForce = 200f;

    [Header("Options")]
    [Tooltip("Can be damaged by bullets/explosions")]
    public bool damageableByWeapons = true;
    [Tooltip("Can be destroyed by vehicle collision")]
    public bool crushableByVehicles = true;
    [Tooltip("Minimum vehicle speed to crush this object")]
    public float minCrushSpeed = 2f;

    private bool isDestroyed = false;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDestroyed;

    public event System.Action OnDestroyed;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker)
    {
        if (isDestroyed) return;
        if (!damageableByWeapons && attacker != null && attacker.GetComponent<TankController>() == null)
        {
            return;
        }

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Destroy(hitPoint, attacker);
        }
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position, null);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isDestroyed) return;
        if (!crushableByVehicles) return;

        // Check if hit by a vehicle
        TankController tank = collision.gameObject.GetComponent<TankController>();
        if (tank != null)
        {
            // Check speed
            Rigidbody tankRb = collision.rigidbody;
            if (tankRb != null && tankRb.linearVelocity.magnitude >= minCrushSpeed)
            {
                // Crushed by tank!
                Vector3 hitPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
                Destroy(hitPoint, tank.gameObject);
            }
        }
    }

    void Destroy(Vector3 hitPoint, GameObject attacker)
    {
        if (isDestroyed) return;
        isDestroyed = true;

        // Spawn destroyed version
        if (destroyedPrefab != null)
        {
            GameObject destroyed = Instantiate(destroyedPrefab, transform.position, transform.rotation);

            // Apply force to debris pieces
            if (applyDebrisForce)
            {
                Vector3 forceDir = Vector3.up;
                if (attacker != null)
                {
                    forceDir = (transform.position - attacker.transform.position).normalized;
                    forceDir.y = 0.5f;
                    forceDir.Normalize();
                }

                Rigidbody[] debrisRbs = destroyed.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in debrisRbs)
                {
                    Vector3 randomDir = forceDir + Random.insideUnitSphere * 0.5f;
                    rb.AddForce(randomDir * debrisForce + Vector3.up * debrisUpwardForce, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * 100f, ForceMode.Impulse);
                }
            }

            // Auto-destroy debris after some time
            Destroy(destroyed, 10f);
        }

        // Spawn effect
        if (destructionEffect != null)
        {
            GameObject effect = Instantiate(destructionEffect, hitPoint, Quaternion.identity);
            Destroy(effect, 5f);
        }

        // Play sound
        if (destructionSound != null)
        {
            AudioSource.PlayClipAtPoint(destructionSound, transform.position, destructionVolume);
        }

        OnDestroyed?.Invoke();

        Debug.Log($"[Destructible] {gameObject.name} destroyed!");

        // Destroy this object
        Destroy(gameObject);
    }

    /// <summary>
    /// Force immediate destruction
    /// </summary>
    public void ForceDestroy()
    {
        Destroy(transform.position, null);
    }
}
