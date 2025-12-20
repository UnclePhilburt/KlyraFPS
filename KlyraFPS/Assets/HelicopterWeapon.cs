using UnityEngine;
using Photon.Pun;

public enum HeliWeaponType
{
    Minigun,
    RocketPod
}

public class HelicopterWeapon : MonoBehaviourPunCallbacks
{
    [Header("Weapon Type")]
    public HeliWeaponType weaponType = HeliWeaponType.Minigun;

    [Header("Minigun Settings")]
    public float fireRate = 0.05f; // 1200 RPM
    public float damage = 15f;
    public float range = 300f;
    public float spread = 0.02f;

    [Header("Rocket Settings")]
    public int maxRockets = 14;
    public float rocketDamage = 150f;
    public float rocketSpeed = 80f;
    public float rocketReloadTime = 0.5f;
    public GameObject rocketPrefab;
    private int currentRockets;

    [Header("Overheat System")]
    public float maxHeat = 100f;
    public float heatPerShot = 2f;
    public float cooldownRate = 20f;
    public float overheatCooldownRate = 40f;
    private float currentHeat = 0f;
    private bool isOverheated = false;

    [Header("Visual")]
    public Transform muzzlePoint;
    public Transform[] barrels; // For minigun spinning
    public float barrelSpinSpeed = 2000f;
    private float currentBarrelRotation = 0f;
    private bool isFiring = false;

    [Header("Effects")]
    public GameObject muzzleFlashPrefab;
    public GameObject tracerPrefab;
    public float tracerSpeed = 200f;

    [Header("Audio")]
    public AudioClip fireSound;
    public AudioClip spinUpSound;
    public AudioClip overheatSound;
    public AudioClip rocketFireSound;
    private AudioSource audioSource;
    private AudioSource spinAudio;

    // State
    private FPSControllerPhoton currentOperator;
    private Team aiGunnerTeam = Team.None;  // For AI gunners
    private bool isAIControlled = false;
    private float nextFireTime = 0f;
    private bool isSpunUp = false;
    private float spinUpTimer = 0f;
    private float spinUpTime = 0.5f;

    void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.maxDistance = 100f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.playOnAwake = false;

        spinAudio = gameObject.AddComponent<AudioSource>();
        spinAudio.spatialBlend = 1f;
        spinAudio.maxDistance = 80f;
        spinAudio.loop = true;
        spinAudio.playOnAwake = false;

        if (muzzlePoint == null)
            muzzlePoint = transform;

        currentRockets = maxRockets;
    }

    public void SetOperator(FPSControllerPhoton player)
    {
        currentOperator = player;
        isAIControlled = false;

        if (player == null)
        {
            isFiring = false;
            isSpunUp = false;
            spinUpTimer = 0f;
        }
    }

    public void SetAIGunner(Team team)
    {
        currentOperator = null;
        aiGunnerTeam = team;
        isAIControlled = true;
    }

    public void ClearAIGunner()
    {
        isAIControlled = false;
        aiGunnerTeam = Team.None;
        isFiring = false;
        isSpunUp = false;
        spinUpTimer = 0f;
    }

    void Update()
    {
        // Update barrel spin
        UpdateBarrelSpin();

        // Update heat/cooldown
        UpdateHeat();

        // Update spin audio
        UpdateSpinAudio();
    }

    void UpdateBarrelSpin()
    {
        if (weaponType != HeliWeaponType.Minigun) return;

        float targetSpeed = isFiring ? barrelSpinSpeed : 0f;

        if (isFiring && !isSpunUp)
        {
            spinUpTimer += Time.deltaTime;
            if (spinUpTimer >= spinUpTime)
            {
                isSpunUp = true;
            }
            targetSpeed = barrelSpinSpeed * (spinUpTimer / spinUpTime);
        }
        else if (!isFiring)
        {
            spinUpTimer = Mathf.Max(0, spinUpTimer - Time.deltaTime * 2f);
            isSpunUp = false;
            targetSpeed = barrelSpinSpeed * (spinUpTimer / spinUpTime);
        }

        currentBarrelRotation += targetSpeed * Time.deltaTime;

        // Rotate barrels
        if (barrels != null)
        {
            foreach (var barrel in barrels)
            {
                if (barrel != null)
                {
                    barrel.localRotation = Quaternion.Euler(0, 0, currentBarrelRotation);
                }
            }
        }
    }

    void UpdateHeat()
    {
        if (isOverheated)
        {
            currentHeat -= overheatCooldownRate * Time.deltaTime;
            if (currentHeat <= 0)
            {
                currentHeat = 0;
                isOverheated = false;
            }
        }
        else if (!isFiring)
        {
            currentHeat -= cooldownRate * Time.deltaTime;
            currentHeat = Mathf.Max(0, currentHeat);
        }
    }

    void UpdateSpinAudio()
    {
        if (weaponType != HeliWeaponType.Minigun) return;

        float spinRatio = spinUpTimer / spinUpTime;

        if (spinRatio > 0.1f)
        {
            if (!spinAudio.isPlaying && spinUpSound != null)
            {
                spinAudio.clip = spinUpSound;
                spinAudio.Play();
            }
            spinAudio.volume = spinRatio * 0.6f;
            spinAudio.pitch = 0.5f + spinRatio * 0.5f;
        }
        else
        {
            if (spinAudio.isPlaying)
            {
                spinAudio.Stop();
            }
        }
    }

    public void Fire()
    {
        // Check if player or AI can fire
        if (!isAIControlled)
        {
            if (currentOperator == null) return;
            if (!currentOperator.photonView.IsMine) return;
        }

        isFiring = true;

        switch (weaponType)
        {
            case HeliWeaponType.Minigun:
                FireMinigun();
                break;
            case HeliWeaponType.RocketPod:
                FireRocket();
                break;
        }
    }

    public void StopFiring()
    {
        isFiring = false;
    }

    Team GetGunnerTeam()
    {
        if (isAIControlled) return aiGunnerTeam;
        if (currentOperator != null) return currentOperator.playerTeam;
        return Team.None;
    }

    int GetGunnerViewID()
    {
        if (currentOperator != null) return currentOperator.photonView.ViewID;
        return -1;
    }

    void FireMinigun()
    {
        if (isOverheated) return;
        if (!isSpunUp) return;
        if (Time.time < nextFireTime) return;

        nextFireTime = Time.time + fireRate;

        // Add heat
        currentHeat += heatPerShot;
        if (currentHeat >= maxHeat)
        {
            isOverheated = true;
            if (overheatSound != null)
            {
                audioSource.PlayOneShot(overheatSound);
            }
            return;
        }

        Team gunnerTeam = GetGunnerTeam();
        int gunnerViewID = GetGunnerViewID();

        // Calculate aim direction with spread
        Vector3 aimDir = muzzlePoint.forward;
        aimDir += new Vector3(
            Random.Range(-spread, spread),
            Random.Range(-spread, spread),
            Random.Range(-spread, spread)
        );
        aimDir.Normalize();

        // Raycast for hit
        RaycastHit hit;
        Vector3 hitPoint = muzzlePoint.position + aimDir * range;
        bool didHit = Physics.Raycast(muzzlePoint.position, aimDir, out hit, range);

        if (didHit)
        {
            hitPoint = hit.point;

            // Damage
            int targetViewID = -1;

            // Check for player
            FPSControllerPhoton player = hit.collider.GetComponentInParent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != gunnerTeam)
            {
                targetViewID = player.photonView.ViewID;
            }

            // Check for AI
            AIController ai = hit.collider.GetComponentInParent<AIController>();
            if (ai != null && ai.team != gunnerTeam)
            {
                // AI doesn't have photonView, damage directly
                ai.TakeDamage(damage);
            }

            // Check for helicopter
            HelicopterController heli = hit.collider.GetComponentInParent<HelicopterController>();
            if (heli != null && heli.helicopterTeam != gunnerTeam)
            {
                heli.TakeDamage(damage, gunnerViewID);
            }

            // Apply damage via RPC (only if we have a valid attacker view ID for network sync)
            if (targetViewID != -1 && gunnerViewID != -1)
            {
                photonView.RPC("RPC_ApplyDamage", RpcTarget.All, targetViewID, damage, gunnerViewID);
            }
            else if (targetViewID != -1 && player != null)
            {
                // AI gunner - apply damage directly
                player.TakeDamage(damage, -1);
            }

            // Spawn impact effect
            SpawnImpactEffect(hitPoint, hit.normal);
        }

        // Network sync the shot (only if we have photonView)
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_MinigunFired", RpcTarget.Others, muzzlePoint.position, hitPoint);
        }

        // Local effects
        SpawnMuzzleFlash();
        SpawnTracer(muzzlePoint.position, hitPoint);

        // Play sound
        if (fireSound != null)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(fireSound, 0.7f);
        }
    }

    void FireRocket()
    {
        if (currentRockets <= 0) return;
        if (Time.time < nextFireTime) return;

        nextFireTime = Time.time + rocketReloadTime;
        currentRockets--;

        // Spawn rocket
        if (rocketPrefab != null)
        {
            photonView.RPC("RPC_FireRocket", RpcTarget.All, muzzlePoint.position, muzzlePoint.rotation);
        }

        // Play sound
        if (rocketFireSound != null)
        {
            audioSource.PlayOneShot(rocketFireSound);
        }
    }

    [PunRPC]
    void RPC_MinigunFired(Vector3 muzzlePos, Vector3 hitPos)
    {
        // Remote effects
        SpawnTracer(muzzlePos, hitPos);
    }

    [PunRPC]
    void RPC_FireRocket(Vector3 pos, Quaternion rot)
    {
        if (rocketPrefab != null)
        {
            GameObject rocket = Instantiate(rocketPrefab, pos, rot);
            Rigidbody rocketRb = rocket.GetComponent<Rigidbody>();
            if (rocketRb != null)
            {
                rocketRb.linearVelocity = rot * Vector3.forward * rocketSpeed;
            }

            // Set damage values
            HeliRocket rocketScript = rocket.GetComponent<HeliRocket>();
            if (rocketScript != null)
            {
                rocketScript.damage = rocketDamage;
                rocketScript.ownerTeam = currentOperator != null ? currentOperator.playerTeam : Team.None;
            }
        }
    }

    [PunRPC]
    void RPC_ApplyDamage(int targetViewID, float damage, int attackerViewID)
    {
        PhotonView targetView = PhotonView.Find(targetViewID);
        if (targetView == null) return;

        // Apply to player
        FPSControllerPhoton player = targetView.GetComponent<FPSControllerPhoton>();
        if (player != null)
        {
            player.TakeDamage(damage, attackerViewID);
            return;
        }

        // Apply to AI
        AIController ai = targetView.GetComponent<AIController>();
        if (ai != null)
        {
            ai.TakeDamage(damage);
        }
    }

    void SpawnMuzzleFlash()
    {
        if (muzzlePoint == null) return;

        if (muzzleFlashPrefab != null)
        {
            // Use Synty prefab (FX_Gunshot_Repeating for minigun)
            GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation);
            flash.transform.SetParent(muzzlePoint);
            Destroy(flash, 0.1f);
        }
        else
        {
            // Fallback: Create procedural muzzle flash
            GameObject flashObj = new GameObject("MinigunFlash");
            flashObj.transform.position = muzzlePoint.position;
            flashObj.transform.rotation = muzzlePoint.rotation;
            flashObj.transform.SetParent(muzzlePoint);

            Light flashLight = flashObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.9f, 0.4f);
            flashLight.intensity = 4f;
            flashLight.range = 8f;

            Destroy(flashObj, 0.05f);
        }
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        if (tracerPrefab != null)
        {
            // Use Synty tracer prefab (FX_Bullet_Trail_Mesh)
            Vector3 direction = (end - start).normalized;
            GameObject tracer = Instantiate(tracerPrefab, start, Quaternion.LookRotation(direction));

            // Move tracer toward end point
            Rigidbody tracerRb = tracer.GetComponent<Rigidbody>();
            if (tracerRb != null)
            {
                tracerRb.linearVelocity = direction * tracerSpeed;
            }

            Destroy(tracer, 0.5f);
            return;
        }

        // Fallback: Create procedural tracer
        GameObject tracerObj = new GameObject("HeliMinigunTracer");
        tracerObj.transform.position = start;

        // Add LineRenderer manually for minigun (thicker tracers)
        LineRenderer lr = tracerObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        // Minigun tracer style - thick bright yellow
        lr.startWidth = 0.1f;
        lr.endWidth = 0.05f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = new Color(1f, 0.95f, 0.4f, 1f);
        lr.endColor = new Color(1f, 0.7f, 0.2f, 0.6f);

        // Simple fade component
        tracerObj.AddComponent<TracerFade>();

        Destroy(tracerObj, 0.12f);
    }

    void SpawnImpactEffect(Vector3 pos, Vector3 normal)
    {
        // Use existing bullet impact system
        GameObject impactPrefab = Resources.Load<GameObject>("BulletImpact");
        if (impactPrefab != null)
        {
            GameObject impact = Instantiate(impactPrefab, pos, Quaternion.LookRotation(normal));
            Destroy(impact, 2f);
        }
    }

    // UI for heat display
    public float GetHeatPercentage()
    {
        return currentHeat / maxHeat;
    }

    public bool IsOverheated()
    {
        return isOverheated;
    }

    public int GetRocketCount()
    {
        return currentRockets;
    }

    void OnDrawGizmosSelected()
    {
        // Draw fire direction
        if (muzzlePoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(muzzlePoint.position, muzzlePoint.position + muzzlePoint.forward * 20f);

            // Draw spread cone
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            float spreadAngle = Mathf.Atan(spread) * Mathf.Rad2Deg;
            // Draw cone outline
        }
    }
}

// Simple rocket projectile
public class HeliRocket : MonoBehaviour
{
    public float damage = 150f;
    public float explosionRadius = 5f;
    public Team ownerTeam = Team.None;
    public GameObject explosionEffectPrefab;

    private bool hasExploded = false;

    void Start()
    {
        // Self-destruct after 10 seconds
        Destroy(gameObject, 10f);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;
        hasExploded = true;

        Explode();
    }

    void Explode()
    {
        // Spawn explosion effect
        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        // Damage in radius
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (var hit in hits)
        {
            float distance = Vector3.Distance(transform.position, hit.transform.position);
            float falloff = 1f - (distance / explosionRadius);
            float actualDamage = damage * falloff;

            // Damage players
            FPSControllerPhoton player = hit.GetComponentInParent<FPSControllerPhoton>();
            if (player != null && player.playerTeam != ownerTeam)
            {
                player.TakeDamage(actualDamage, -1);
            }

            // Damage AI
            AIController ai = hit.GetComponentInParent<AIController>();
            if (ai != null && ai.team != ownerTeam)
            {
                ai.TakeDamage(actualDamage);
            }

            // Damage helicopters
            HelicopterController heli = hit.GetComponentInParent<HelicopterController>();
            if (heli != null && heli.helicopterTeam != ownerTeam)
            {
                heli.TakeDamage(actualDamage, -1);
            }
        }

        Destroy(gameObject);
    }
}

// Simple fade component for minigun tracers
public class TracerFade : MonoBehaviour
{
    private LineRenderer lr;
    private float alpha = 1f;
    private Color startColor;
    private Color endColor;

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        if (lr != null)
        {
            startColor = lr.startColor;
            endColor = lr.endColor;
        }
    }

    void Update()
    {
        if (lr == null) return;

        alpha -= Time.deltaTime * 12f;
        if (alpha < 0) alpha = 0;

        lr.startColor = new Color(startColor.r, startColor.g, startColor.b, alpha);
        lr.endColor = new Color(endColor.r, endColor.g, endColor.b, alpha * 0.6f);
    }
}
