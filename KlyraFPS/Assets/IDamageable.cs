using UnityEngine;

/// <summary>
/// Interface for any object that can take damage
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker);
    float CurrentHealth { get; }
    float MaxHealth { get; }
    bool IsDead { get; }
}

/// <summary>
/// Simple health component that implements IDamageable
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Effects")]
    public GameObject deathEffect;
    public AudioClip hitSound;
    public AudioClip deathSound;

    [Header("Options")]
    public bool destroyOnDeath = true;
    public float destroyDelay = 0f;

    private AudioSource audioSource;
    private bool isDead = false;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;

    public event System.Action<float, GameObject> OnDamaged;
    public event System.Action OnDeath;

    void Awake()
    {
        currentHealth = maxHealth;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, GameObject attacker)
    {
        if (isDead) return;

        currentHealth -= damage;
        OnDamaged?.Invoke(damage, attacker);

        if (hitSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(hitSound);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position, null);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        OnDeath?.Invoke();

        if (deathEffect != null)
        {
            Instantiate(deathEffect, transform.position, transform.rotation);
        }

        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        if (destroyOnDeath)
        {
            Destroy(gameObject, destroyDelay);
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
    }

    public void SetHealth(float health)
    {
        currentHealth = Mathf.Clamp(health, 0, maxHealth);
        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
    }
}
