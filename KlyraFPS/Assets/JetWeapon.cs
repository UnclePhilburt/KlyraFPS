using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class JetWeapon : MonoBehaviourPunCallbacks
{
    [Header("Gun Settings")]
    public Transform[] gunMuzzles;
    public float gunFireRate = 0.05f;  // 20 rounds per second
    public float gunDamage = 15f;
    public float gunRange = 500f;
    public float gunSpread = 2f;  // Degrees of spread
    public AudioClip gunSound;
    public GameObject gunTracerPrefab;

    [Header("Missile Settings")]
    public Transform[] missilePylons;
    public GameObject missilePrefab;
    public int maxMissiles = 4;
    public float missileReloadTime = 30f;
    public float missileLockTime = 2f;
    public float missileRange = 800f;
    public AudioClip missileLaunchSound;
    public AudioClip missileLockSound;

    [Header("References")]
    public JetController jet;

    // Gun state
    private float gunCooldown = 0f;
    private int currentMuzzleIndex = 0;
    private AudioSource gunAudioSource;

    // Missile state
    private int currentMissiles;
    private float missileReloadTimer = 0f;
    private Transform lockTarget;
    private float lockProgress = 0f;
    private bool isLocking = false;
    private AudioSource missileAudioSource;

    // Tracers
    private List<TracerBullet> activeTracers = new List<TracerBullet>();

    private class TracerBullet
    {
        public LineRenderer line;
        public float lifetime;
        public Vector3 start;
        public Vector3 end;
    }

    void Awake()
    {
        if (jet == null)
        {
            jet = GetComponentInParent<JetController>();
        }

        currentMissiles = maxMissiles;

        // Set up audio sources
        gunAudioSource = gameObject.AddComponent<AudioSource>();
        gunAudioSource.spatialBlend = 1f;
        gunAudioSource.maxDistance = 200f;
        gunAudioSource.playOnAwake = false;

        missileAudioSource = gameObject.AddComponent<AudioSource>();
        missileAudioSource.spatialBlend = 1f;
        missileAudioSource.maxDistance = 300f;
        missileAudioSource.playOnAwake = false;
    }

    void Update()
    {
        // Cooldowns
        if (gunCooldown > 0f) gunCooldown -= Time.deltaTime;

        // Missile reload
        if (currentMissiles < maxMissiles)
        {
            missileReloadTimer -= Time.deltaTime;
            if (missileReloadTimer <= 0f)
            {
                currentMissiles++;
                missileReloadTimer = missileReloadTime;
                Debug.Log($"[JET WEAPON] Missile reloaded. {currentMissiles}/{maxMissiles}");
            }
        }

        // Update tracers
        UpdateTracers();
    }

    // === GUNS ===

    public void FireGuns()
    {
        if (gunCooldown > 0f) return;
        if (gunMuzzles == null || gunMuzzles.Length == 0) return;

        gunCooldown = gunFireRate;

        // Fire from current muzzle
        Transform muzzle = gunMuzzles[currentMuzzleIndex];
        currentMuzzleIndex = (currentMuzzleIndex + 1) % gunMuzzles.Length;

        // Add spread
        Vector3 spreadDir = muzzle.forward;
        spreadDir += muzzle.right * Random.Range(-gunSpread, gunSpread) * 0.01f;
        spreadDir += muzzle.up * Random.Range(-gunSpread, gunSpread) * 0.01f;
        spreadDir.Normalize();

        // Raycast
        RaycastHit hit;
        Vector3 endPoint = muzzle.position + spreadDir * gunRange;

        if (Physics.Raycast(muzzle.position, spreadDir, out hit, gunRange))
        {
            endPoint = hit.point;

            // Apply damage
            ApplyGunDamage(hit);
        }

        // Spawn tracer
        SpawnTracer(muzzle.position, endPoint);

        // Play sound
        if (gunSound != null && gunAudioSource != null)
        {
            gunAudioSource.pitch = Random.Range(0.95f, 1.05f);
            gunAudioSource.PlayOneShot(gunSound, 0.6f);
        }

        // Network sync
        if (PhotonNetwork.IsConnected && photonView.IsMine)
        {
            photonView.RPC("RPC_FireGuns", RpcTarget.Others, muzzle.position, endPoint);
        }
    }

    [PunRPC]
    void RPC_FireGuns(Vector3 start, Vector3 end)
    {
        SpawnTracer(start, end);
        if (gunSound != null && gunAudioSource != null)
        {
            gunAudioSource.PlayOneShot(gunSound, 0.4f);
        }
    }

    void ApplyGunDamage(RaycastHit hit)
    {
        // Check for AI
        AIController ai = hit.collider.GetComponentInParent<AIController>();
        if (ai != null && ai.team != jet.JetTeam)
        {
            ai.TakeDamage(gunDamage, hit.point, hit.point, jet.gameObject);
            return;
        }

        // Check for player
        FPSControllerPhoton player = hit.collider.GetComponentInParent<FPSControllerPhoton>();
        if (player != null && player.playerTeam != jet.JetTeam)
        {
            player.TakeDamageFromAI(gunDamage, hit.point);
            return;
        }

        // Check for helicopter
        HelicopterController heli = hit.collider.GetComponentInParent<HelicopterController>();
        if (heli != null && heli.helicopterTeam != jet.JetTeam)
        {
            heli.TakeDamage(gunDamage, -1);  // -1 for AI attacker
            return;
        }

        // Check for other jet
        JetController otherJet = hit.collider.GetComponentInParent<JetController>();
        if (otherJet != null && otherJet.JetTeam != jet.JetTeam)
        {
            otherJet.TakeDamage(gunDamage, hit.point, jet.gameObject);
            return;
        }
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("Tracer");
        LineRenderer line = tracerObj.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = 0.1f;
        line.endWidth = 0.05f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = new Color(1f, 0.8f, 0.2f);
        line.endColor = new Color(1f, 0.5f, 0.1f, 0.5f);

        TracerBullet tracer = new TracerBullet
        {
            line = line,
            lifetime = 0.1f,
            start = start,
            end = end
        };

        activeTracers.Add(tracer);
    }

    void UpdateTracers()
    {
        for (int i = activeTracers.Count - 1; i >= 0; i--)
        {
            TracerBullet tracer = activeTracers[i];
            tracer.lifetime -= Time.deltaTime;

            if (tracer.lifetime <= 0f)
            {
                if (tracer.line != null)
                {
                    Destroy(tracer.line.gameObject);
                }
                activeTracers.RemoveAt(i);
            }
        }
    }

    // === MISSILES ===

    public void StartLock(Transform target)
    {
        if (target == null) return;
        if (currentMissiles <= 0) return;

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > missileRange) return;

        lockTarget = target;
        isLocking = true;
        lockProgress = 0f;

        // Play lock tone
        if (missileLockSound != null && missileAudioSource != null)
        {
            missileAudioSource.clip = missileLockSound;
            missileAudioSource.loop = true;
            missileAudioSource.Play();
        }
    }

    public void UpdateLock()
    {
        if (!isLocking || lockTarget == null) return;

        // Check if target still valid
        float dist = Vector3.Distance(transform.position, lockTarget.position);
        if (dist > missileRange)
        {
            CancelLock();
            return;
        }

        // Check if target in front of jet
        Vector3 toTarget = (lockTarget.position - transform.position).normalized;
        float angle = Vector3.Angle(jet.transform.forward, toTarget);
        if (angle > 45f)
        {
            CancelLock();
            return;
        }

        lockProgress += Time.deltaTime / missileLockTime;

        // Increase lock tone pitch as we get closer to lock
        if (missileAudioSource != null)
        {
            missileAudioSource.pitch = 1f + lockProgress * 0.5f;
        }
    }

    public void CancelLock()
    {
        isLocking = false;
        lockProgress = 0f;
        lockTarget = null;

        if (missileAudioSource != null)
        {
            missileAudioSource.Stop();
        }
    }

    public bool IsLocked => isLocking && lockProgress >= 1f;
    public float LockProgress => lockProgress;
    public Transform LockTarget => lockTarget;
    public int CurrentMissiles => currentMissiles;
    public int MaxMissiles => maxMissiles;

    public void FireMissile()
    {
        if (currentMissiles <= 0) return;
        if (!IsLocked) return;

        currentMissiles--;

        // Find pylon to fire from
        Transform pylon = null;
        if (missilePylons != null && missilePylons.Length > 0)
        {
            pylon = missilePylons[currentMissiles % missilePylons.Length];
        }
        else
        {
            pylon = transform;
        }

        // Spawn missile
        if (missilePrefab != null)
        {
            GameObject missileObj = Instantiate(missilePrefab, pylon.position, pylon.rotation);
            JetMissile missile = missileObj.GetComponent<JetMissile>();
            if (missile != null)
            {
                missile.Initialize(lockTarget, jet.JetTeam, jet.gameObject);
            }
        }
        else
        {
            // Simple missile without prefab - just do instant damage
            ApplyMissileDamage(lockTarget);
        }

        // Play launch sound
        if (missileLaunchSound != null && missileAudioSource != null)
        {
            missileAudioSource.Stop();
            missileAudioSource.pitch = 1f;
            missileAudioSource.PlayOneShot(missileLaunchSound);
        }

        // Reset lock
        CancelLock();

        // Start reload timer
        if (currentMissiles < maxMissiles && missileReloadTimer <= 0f)
        {
            missileReloadTimer = missileReloadTime;
        }

        Debug.Log($"[JET WEAPON] Missile fired! {currentMissiles}/{maxMissiles} remaining");

        // Network sync
        if (PhotonNetwork.IsConnected && photonView.IsMine)
        {
            int targetViewId = lockTarget.GetComponent<PhotonView>()?.ViewID ?? -1;
            photonView.RPC("RPC_FireMissile", RpcTarget.Others, pylon.position, targetViewId);
        }
    }

    [PunRPC]
    void RPC_FireMissile(Vector3 launchPos, int targetViewId)
    {
        if (missileLaunchSound != null && missileAudioSource != null)
        {
            missileAudioSource.PlayOneShot(missileLaunchSound);
        }

        // Find target by view ID
        PhotonView targetView = PhotonView.Find(targetViewId);
        if (targetView != null && missilePrefab != null)
        {
            GameObject missileObj = Instantiate(missilePrefab, launchPos, transform.rotation);
            JetMissile missile = missileObj.GetComponent<JetMissile>();
            if (missile != null)
            {
                missile.Initialize(targetView.transform, jet.JetTeam, jet.gameObject);
            }
        }
    }

    void ApplyMissileDamage(Transform target)
    {
        if (target == null) return;

        float missileDamage = 150f;

        // Check for helicopter
        HelicopterController heli = target.GetComponentInParent<HelicopterController>();
        if (heli != null)
        {
            heli.TakeDamage(missileDamage, -1);  // -1 for AI attacker
            return;
        }

        // Check for other jet
        JetController otherJet = target.GetComponentInParent<JetController>();
        if (otherJet != null)
        {
            otherJet.TakeDamage(missileDamage, target.position, jet.gameObject);
            return;
        }

        // Area damage for ground targets
        Collider[] hits = Physics.OverlapSphere(target.position, 10f);
        foreach (var col in hits)
        {
            AIController ai = col.GetComponentInParent<AIController>();
            if (ai != null && ai.team != jet.JetTeam)
            {
                ai.TakeDamage(missileDamage * 0.5f, col.transform.position, col.transform.position, jet.gameObject);
            }

            FPSControllerPhoton player = col.GetComponentInParent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != jet.JetTeam)
            {
                player.TakeDamageFromAI(missileDamage * 0.5f, col.transform.position);
            }
        }
    }
}

// Simple missile component for guided missiles
public class JetMissile : MonoBehaviour
{
    public float speed = 80f;
    public float turnSpeed = 90f;
    public float lifetime = 10f;
    public float damage = 150f;
    public float blastRadius = 10f;
    public GameObject explosionPrefab;

    private Transform target;
    private Team ownerTeam;
    private GameObject owner;
    private float timer;

    public void Initialize(Transform target, Team team, GameObject owner)
    {
        this.target = target;
        this.ownerTeam = team;
        this.owner = owner;
        timer = lifetime;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Explode();
            return;
        }

        // Track target
        if (target != null && target.gameObject.activeInHierarchy)
        {
            Vector3 toTarget = target.position - transform.position;
            Quaternion targetRot = Quaternion.LookRotation(toTarget);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        // Move forward
        transform.position += transform.forward * speed * Time.deltaTime;

        // Check for impact
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, speed * Time.deltaTime * 2f))
        {
            Explode();
        }
    }

    void Explode()
    {
        // Spawn explosion effect
        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }

        // Area damage
        Collider[] hits = Physics.OverlapSphere(transform.position, blastRadius);
        foreach (var col in hits)
        {
            float dist = Vector3.Distance(transform.position, col.transform.position);
            float falloff = 1f - (dist / blastRadius);
            float actualDamage = damage * falloff;

            AIController ai = col.GetComponentInParent<AIController>();
            if (ai != null && ai.team != ownerTeam)
            {
                ai.TakeDamage(actualDamage, col.transform.position, col.transform.position, owner);
            }

            FPSControllerPhoton player = col.GetComponentInParent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != ownerTeam)
            {
                player.TakeDamageFromAI(actualDamage, col.transform.position);
            }

            HelicopterController heli = col.GetComponentInParent<HelicopterController>();
            if (heli != null && heli.helicopterTeam != ownerTeam)
            {
                heli.TakeDamage(actualDamage, -1);  // -1 for AI attacker
            }

            JetController jet = col.GetComponentInParent<JetController>();
            if (jet != null && jet.JetTeam != ownerTeam)
            {
                jet.TakeDamage(actualDamage, col.transform.position, owner);
            }
        }

        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, blastRadius);
    }
}
