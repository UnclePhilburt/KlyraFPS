using UnityEngine;
using System.Collections.Generic;

public static class RagdollHelper
{
    /// <summary>
    /// Enables ragdoll physics on a character.
    /// Works with Synty characters and standard Mixamo/Humanoid rigs.
    /// </summary>
    public static void EnableRagdoll(GameObject character, Vector3 forceDirection = default, float forceMagnitude = 5f)
    {
        // Disable animator
        Animator animator = character.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.enabled = false;
        }

        // Disable CharacterController
        CharacterController cc = character.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
        }

        // Disable NavMeshAgent
        UnityEngine.AI.NavMeshAgent agent = character.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
        }

        // Find bones and set up ragdoll
        Transform[] allTransforms = character.GetComponentsInChildren<Transform>();
        Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        // Map transforms to bone types
        foreach (Transform t in allTransforms)
        {
            string lower = t.name.ToLower();

            // Root/Hips/Pelvis
            if ((lower.Contains("root") || lower.Contains("hips") || lower.Contains("pelvis"))
                && !lower.Contains("hair") && !bones.ContainsKey("hips"))
            {
                bones["hips"] = t;
            }
            // Spine
            else if (lower.Contains("spine"))
            {
                if (lower.Contains("02") || lower.Contains("2") || lower.Contains("upper"))
                {
                    if (!bones.ContainsKey("spine2")) bones["spine2"] = t;
                }
                else if (lower.Contains("01") || lower.Contains("1") || !bones.ContainsKey("spine1"))
                {
                    if (!bones.ContainsKey("spine1")) bones["spine1"] = t;
                }
            }
            // Neck
            else if (lower.Contains("neck") && !bones.ContainsKey("neck"))
            {
                bones["neck"] = t;
            }
            // Head
            else if (lower.Contains("head") && !lower.Contains("headpiece") && !bones.ContainsKey("head"))
            {
                bones["head"] = t;
            }
            // Left Leg
            else if ((lower.Contains("upperleg_l") || lower.Contains("leftupleg") || lower.Contains("thigh_l") || lower.Contains("upleg_l"))
                     && !bones.ContainsKey("leftupleg"))
            {
                bones["leftupleg"] = t;
            }
            else if ((lower.Contains("lowerleg_l") || lower.Contains("leftleg") || lower.Contains("calf_l") || lower.Contains("leg_l"))
                     && !lower.Contains("upper") && !bones.ContainsKey("leftleg"))
            {
                bones["leftleg"] = t;
            }
            else if ((lower.Contains("foot_l") || lower.Contains("leftfoot")) && !bones.ContainsKey("leftfoot"))
            {
                bones["leftfoot"] = t;
            }
            // Right Leg
            else if ((lower.Contains("upperleg_r") || lower.Contains("rightupleg") || lower.Contains("thigh_r") || lower.Contains("upleg_r"))
                     && !bones.ContainsKey("rightupleg"))
            {
                bones["rightupleg"] = t;
            }
            else if ((lower.Contains("lowerleg_r") || lower.Contains("rightleg") || lower.Contains("calf_r") || lower.Contains("leg_r"))
                     && !lower.Contains("upper") && !bones.ContainsKey("rightleg"))
            {
                bones["rightleg"] = t;
            }
            else if ((lower.Contains("foot_r") || lower.Contains("rightfoot")) && !bones.ContainsKey("rightfoot"))
            {
                bones["rightfoot"] = t;
            }
            // Left Arm
            else if ((lower.Contains("shoulder_l") || lower.Contains("leftarm") || lower.Contains("upperarm_l") || lower.Contains("arm_l"))
                     && !lower.Contains("fore") && !bones.ContainsKey("leftarm"))
            {
                bones["leftarm"] = t;
            }
            else if ((lower.Contains("elbow_l") || lower.Contains("leftforearm") || lower.Contains("forearm_l") || lower.Contains("lowerarm_l"))
                     && !bones.ContainsKey("leftforearm"))
            {
                bones["leftforearm"] = t;
            }
            else if ((lower.Contains("hand_l") || lower.Contains("lefthand")) && !lower.Contains("finger") && !bones.ContainsKey("lefthand"))
            {
                bones["lefthand"] = t;
            }
            // Right Arm
            else if ((lower.Contains("shoulder_r") || lower.Contains("rightarm") || lower.Contains("upperarm_r") || lower.Contains("arm_r"))
                     && !lower.Contains("fore") && !bones.ContainsKey("rightarm"))
            {
                bones["rightarm"] = t;
            }
            else if ((lower.Contains("elbow_r") || lower.Contains("rightforearm") || lower.Contains("forearm_r") || lower.Contains("lowerarm_r"))
                     && !bones.ContainsKey("rightforearm"))
            {
                bones["rightforearm"] = t;
            }
            else if ((lower.Contains("hand_r") || lower.Contains("righthand")) && !lower.Contains("finger") && !bones.ContainsKey("righthand"))
            {
                bones["righthand"] = t;
            }
            // Clavicles (for arm attachment)
            else if ((lower.Contains("clavicle_l") || lower.Contains("leftshoulder")) && !bones.ContainsKey("leftclavicle"))
            {
                bones["leftclavicle"] = t;
            }
            else if ((lower.Contains("clavicle_r") || lower.Contains("rightshoulder")) && !bones.ContainsKey("rightclavicle"))
            {
                bones["rightclavicle"] = t;
            }
        }

        // Debug: log found bones
        Debug.Log($"Ragdoll found {bones.Count} bones: {string.Join(", ", bones.Keys)}");

        // Collect all colliders we create so we can make them ignore each other
        List<Collider> boneColliders = new List<Collider>();

        // Add rigidbodies and colliders to bones
        Rigidbody hipsRb = null;
        foreach (var kvp in bones)
        {
            Transform bone = kvp.Value;
            string boneType = kvp.Key;

            // Add Rigidbody
            Rigidbody rb = bone.gameObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = bone.gameObject.AddComponent<Rigidbody>();
            }
            rb.mass = GetBoneMass(boneType);
            rb.linearDamping = 2f;  // Increased damping for stability
            rb.angularDamping = 2f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxAngularVelocity = 10f;  // Limit spin speed

            if (boneType == "hips")
            {
                hipsRb = rb;
            }

            // Add Collider
            Collider col = bone.gameObject.GetComponent<Collider>();
            if (col == null)
            {
                CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                SetupBoneCollider(capsule, boneType);
                col = capsule;
            }
            boneColliders.Add(col);
        }

        // CRITICAL: Make all bone colliders ignore each other to prevent internal explosions
        for (int i = 0; i < boneColliders.Count; i++)
        {
            for (int j = i + 1; j < boneColliders.Count; j++)
            {
                Physics.IgnoreCollision(boneColliders[i], boneColliders[j], true);
            }
        }

        // Also ignore collision with the main character collider if it exists
        Collider mainCollider = character.GetComponent<Collider>();
        if (mainCollider != null)
        {
            foreach (var boneCol in boneColliders)
            {
                Physics.IgnoreCollision(mainCollider, boneCol, true);
            }
        }

        // Connect bones with joints
        ConnectBones(bones);

        // Apply death force to hips
        if (hipsRb != null)
        {
            Vector3 force = forceDirection != default ? forceDirection : Random.insideUnitSphere;
            force.y = Mathf.Abs(force.y) * 0.5f; // Slight upward component
            hipsRb.AddForce(force.normalized * forceMagnitude, ForceMode.Impulse);
            hipsRb.AddTorque(Random.insideUnitSphere * forceMagnitude * 0.5f, ForceMode.Impulse);
        }
    }

    private static float GetBoneMass(string boneType)
    {
        switch (boneType)
        {
            case "hips": return 15f;
            case "spine1":
            case "spine2": return 10f;
            case "neck": return 3f;
            case "head": return 5f;
            case "leftupleg":
            case "rightupleg": return 8f;
            case "leftleg":
            case "rightleg": return 5f;
            case "leftfoot":
            case "rightfoot": return 2f;
            case "leftclavicle":
            case "rightclavicle": return 2f;
            case "leftarm":
            case "rightarm": return 4f;
            case "leftforearm":
            case "rightforearm": return 3f;
            case "lefthand":
            case "righthand": return 1f;
            default: return 3f;
        }
    }

    private static void SetupBoneCollider(CapsuleCollider capsule, string boneType)
    {
        switch (boneType)
        {
            case "hips":
                capsule.center = Vector3.up * 0.05f;
                capsule.radius = 0.12f;
                capsule.height = 0.25f;
                capsule.direction = 0; // X
                break;
            case "spine1":
            case "spine2":
                capsule.center = Vector3.up * 0.08f;
                capsule.radius = 0.1f;
                capsule.height = 0.2f;
                capsule.direction = 1; // Y
                break;
            case "neck":
                capsule.center = Vector3.up * 0.05f;
                capsule.radius = 0.04f;
                capsule.height = 0.1f;
                capsule.direction = 1;
                break;
            case "head":
                capsule.center = Vector3.up * 0.1f;
                capsule.radius = 0.1f;
                capsule.height = 0.2f;
                capsule.direction = 1;
                break;
            case "leftupleg":
            case "rightupleg":
                capsule.center = Vector3.down * 0.15f;
                capsule.radius = 0.07f;
                capsule.height = 0.35f;
                capsule.direction = 1;
                break;
            case "leftleg":
            case "rightleg":
                capsule.center = Vector3.down * 0.15f;
                capsule.radius = 0.05f;
                capsule.height = 0.35f;
                capsule.direction = 1;
                break;
            case "leftfoot":
            case "rightfoot":
                capsule.center = Vector3.forward * 0.05f;
                capsule.radius = 0.04f;
                capsule.height = 0.12f;
                capsule.direction = 2; // Z
                break;
            case "leftclavicle":
            case "rightclavicle":
                capsule.center = Vector3.zero;
                capsule.radius = 0.04f;
                capsule.height = 0.12f;
                capsule.direction = 0;
                break;
            case "leftarm":
            case "rightarm":
                capsule.center = Vector3.down * 0.1f;
                capsule.radius = 0.04f;
                capsule.height = 0.22f;
                capsule.direction = 1;
                break;
            case "leftforearm":
            case "rightforearm":
                capsule.center = Vector3.down * 0.1f;
                capsule.radius = 0.035f;
                capsule.height = 0.2f;
                capsule.direction = 1;
                break;
            case "lefthand":
            case "righthand":
                capsule.center = Vector3.zero;
                capsule.radius = 0.03f;
                capsule.height = 0.08f;
                capsule.direction = 1;
                break;
            default:
                capsule.radius = 0.05f;
                capsule.height = 0.1f;
                break;
        }
    }

    private static void ConnectBones(Dictionary<string, Transform> bones)
    {
        // Spine chain
        if (bones.ContainsKey("spine1") && bones.ContainsKey("hips"))
            CreateJoint(bones["spine1"], bones["hips"], 25f, 25f);

        if (bones.ContainsKey("spine2") && bones.ContainsKey("spine1"))
            CreateJoint(bones["spine2"], bones["spine1"], 20f, 20f);
        else if (bones.ContainsKey("spine2") && bones.ContainsKey("hips"))
            CreateJoint(bones["spine2"], bones["hips"], 25f, 25f);

        // Neck and head
        Transform spineTop = bones.ContainsKey("spine2") ? bones["spine2"] :
                            (bones.ContainsKey("spine1") ? bones["spine1"] : null);

        if (bones.ContainsKey("neck") && spineTop != null)
            CreateJoint(bones["neck"], spineTop, 40f, 30f);

        if (bones.ContainsKey("head"))
        {
            Transform headParent = bones.ContainsKey("neck") ? bones["neck"] : spineTop;
            if (headParent != null)
                CreateJoint(bones["head"], headParent, 40f, 30f);
        }

        // Legs - connect to hips
        if (bones.ContainsKey("hips"))
        {
            if (bones.ContainsKey("leftupleg"))
                CreateJoint(bones["leftupleg"], bones["hips"], 70f, 20f);
            if (bones.ContainsKey("rightupleg"))
                CreateJoint(bones["rightupleg"], bones["hips"], 70f, 20f);
        }

        // Lower legs
        if (bones.ContainsKey("leftleg") && bones.ContainsKey("leftupleg"))
            CreateJoint(bones["leftleg"], bones["leftupleg"], 90f, 5f);
        if (bones.ContainsKey("rightleg") && bones.ContainsKey("rightupleg"))
            CreateJoint(bones["rightleg"], bones["rightupleg"], 90f, 5f);

        // Feet
        if (bones.ContainsKey("leftfoot") && bones.ContainsKey("leftleg"))
            CreateJoint(bones["leftfoot"], bones["leftleg"], 30f, 15f);
        if (bones.ContainsKey("rightfoot") && bones.ContainsKey("rightleg"))
            CreateJoint(bones["rightfoot"], bones["rightleg"], 30f, 15f);

        // Arms - connect to spine top or clavicles
        Transform armParent = spineTop;

        if (bones.ContainsKey("leftarm"))
        {
            Transform leftArmParent = bones.ContainsKey("leftclavicle") ? bones["leftclavicle"] : armParent;
            if (leftArmParent != null)
                CreateJoint(bones["leftarm"], leftArmParent, 80f, 40f);
        }
        if (bones.ContainsKey("rightarm"))
        {
            Transform rightArmParent = bones.ContainsKey("rightclavicle") ? bones["rightclavicle"] : armParent;
            if (rightArmParent != null)
                CreateJoint(bones["rightarm"], rightArmParent, 80f, 40f);
        }

        // Clavicles to spine
        if (bones.ContainsKey("leftclavicle") && spineTop != null)
            CreateJoint(bones["leftclavicle"], spineTop, 15f, 10f);
        if (bones.ContainsKey("rightclavicle") && spineTop != null)
            CreateJoint(bones["rightclavicle"], spineTop, 15f, 10f);

        // Forearms
        if (bones.ContainsKey("leftforearm") && bones.ContainsKey("leftarm"))
            CreateJoint(bones["leftforearm"], bones["leftarm"], 120f, 10f);
        if (bones.ContainsKey("rightforearm") && bones.ContainsKey("rightarm"))
            CreateJoint(bones["rightforearm"], bones["rightarm"], 120f, 10f);

        // Hands
        if (bones.ContainsKey("lefthand") && bones.ContainsKey("leftforearm"))
            CreateJoint(bones["lefthand"], bones["leftforearm"], 50f, 30f);
        if (bones.ContainsKey("righthand") && bones.ContainsKey("rightforearm"))
            CreateJoint(bones["righthand"], bones["rightforearm"], 50f, 30f);
    }

    private static void CreateJoint(Transform child, Transform parent, float swing, float twist)
    {
        Rigidbody childRb = child.GetComponent<Rigidbody>();
        Rigidbody parentRb = parent.GetComponent<Rigidbody>();

        if (childRb == null || parentRb == null) return;

        // Remove existing joint if any
        CharacterJoint existingJoint = child.GetComponent<CharacterJoint>();
        if (existingJoint != null)
        {
            Object.Destroy(existingJoint);
        }

        CharacterJoint joint = child.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parentRb;
        joint.enablePreprocessing = false;

        // CRITICAL: Set anchor at the connection point (where child meets parent)
        // The anchor should be at the start of the child bone (near parent)
        joint.anchor = Vector3.zero;  // Local origin of child

        // Connected anchor is where on the parent this joint connects
        // Convert child's position to parent's local space
        Vector3 connectionPoint = parent.InverseTransformPoint(child.position);
        joint.connectedAnchor = connectionPoint;

        // Set axis to point along the bone
        joint.axis = Vector3.right;
        joint.swingAxis = Vector3.forward;

        // Configure limits - reduced for more stability
        SoftJointLimit lowTwist = new SoftJointLimit();
        lowTwist.limit = -twist;
        lowTwist.bounciness = 0f;
        lowTwist.contactDistance = 0f;
        joint.lowTwistLimit = lowTwist;

        SoftJointLimit highTwist = new SoftJointLimit();
        highTwist.limit = twist;
        highTwist.bounciness = 0f;
        highTwist.contactDistance = 0f;
        joint.highTwistLimit = highTwist;

        SoftJointLimit swing1 = new SoftJointLimit();
        swing1.limit = swing;
        swing1.bounciness = 0f;
        swing1.contactDistance = 0f;
        joint.swing1Limit = swing1;

        SoftJointLimit swing2 = new SoftJointLimit();
        swing2.limit = swing * 0.5f;
        swing2.bounciness = 0f;
        swing2.contactDistance = 0f;
        joint.swing2Limit = swing2;

        // Strong spring/damping for stability - keeps bones together
        SoftJointLimitSpring spring = new SoftJointLimitSpring();
        spring.spring = 500f;
        spring.damper = 50f;
        joint.swingLimitSpring = spring;
        joint.twistLimitSpring = spring;

        // Enable projection to prevent bones from separating
        joint.enableProjection = true;
        joint.projectionDistance = 0.01f;
        joint.projectionAngle = 5f;
    }
}
