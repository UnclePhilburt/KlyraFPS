#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(MainMenuUI))]
public class MainMenuUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MainMenuUI menu = (MainMenuUI)target;

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Auto-Find Synty Assets", GUILayout.Height(35)))
        {
            AutoPopulateAssets(menu);
        }

        EditorGUILayout.Space(5);

        bool hasSoldiers = menu.soldierPrefabs != null && menu.soldierPrefabs.Length > 0;
        bool hasVehicles = menu.vehiclePrefabs != null && menu.vehiclePrefabs.Length > 0;
        bool hasAnimator = menu.soldierAnimator != null;
        bool hasIdleClip = menu.idleAnimationClip != null;
        bool hasDust = menu.dustPrefab != null;

        if (!hasSoldiers || !hasVehicles)
        {
            EditorGUILayout.HelpBox(
                "Click 'Auto-Find Synty Assets' to populate:\n" +
                "• 5 Soldiers (squad formation)\n" +
                "• 5 Vehicles (tanks, helicopters, APCs)\n" +
                "• 15 Props (crates, barrels, equipment)\n" +
                "• Dust effects (FX_Dust_Blowing_Soft)\n" +
                "• Idle animation (Mixamo Rifle Aiming Idle)",
                MessageType.Info);
        }
        else
        {
            string animStatus = hasIdleClip ? "idle anim" : (hasAnimator ? "animator" : "NO ANIM");
            string dustStatus = hasDust ? "dust fx" : "no dust";
            EditorGUILayout.HelpBox(
                $"Loaded: {menu.soldierPrefabs.Length} soldiers, {menu.vehiclePrefabs.Length} vehicles, " +
                $"{(menu.propPrefabs?.Length ?? 0)} props, {dustStatus}, {animStatus}",
                MessageType.None);
        }
    }

    void AutoPopulateAssets(MainMenuUI menu)
    {
        Undo.RecordObject(menu, "Auto-populate MainMenuUI assets");

        // Find soldiers - variety of types
        List<GameObject> soldiers = new List<GameObject>();
        string[] soldierPaths = new string[]
        {
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Female_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Soldier_Male_02.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Contractor_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Contractor_Female_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Ghillie_Male_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Characters/SM_Chr_Pilot_Male_01.prefab",
        };

        foreach (var path in soldierPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && soldiers.Count < 5)
            {
                soldiers.Add(prefab);
            }
        }

        if (soldiers.Count > 0)
        {
            menu.soldierPrefabs = soldiers.ToArray();
            Debug.Log($"[MainMenuUI] Found {soldiers.Count} soldier prefabs");
        }

        // Find vehicles - variety of military vehicles
        List<GameObject> vehicles = new List<GameObject>();
        string[] vehiclePaths = new string[]
        {
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Tank_USA_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Helicopter_Attack_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_APC_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Light_Armored_Car_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Helicopter_Transport_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Humvee_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Tank_RUS_01.prefab",
            "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Jet_01.prefab",
        };

        foreach (var path in vehiclePaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && vehicles.Count < 5)
            {
                vehicles.Add(prefab);
            }
        }

        if (vehicles.Count > 0)
        {
            menu.vehiclePrefabs = vehicles.ToArray();
            Debug.Log($"[MainMenuUI] Found {vehicles.Count} vehicle prefabs");
        }

        // Find props - crates, barrels, sandbags, weapons, equipment, etc.
        List<GameObject> props = new List<GameObject>();
        string[] propSearchPaths = new string[]
        {
            "Assets/Synty/PolygonMilitary/Prefabs/Props",
            "Assets/Synty/PolygonMilitary/Prefabs/Structures",
            "Assets/Synty/PolygonMilitary/Prefabs/Equipment"
        };

        string[] propPatterns = new string[]
        {
            "Crate", "Barrel", "Sandbag", "Box", "Ammo", "Pallet",
            "Tent", "Flag", "Bag", "Radio", "Gear", "Table", "Weapon_Rack",
            "Barricade", "Fence", "Sign", "Canister"
        };

        foreach (var searchPath in propSearchPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });
            foreach (var guid in guids)
            {
                if (props.Count >= 15) break;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                string filename = System.IO.Path.GetFileNameWithoutExtension(path);

                foreach (var pattern in propPatterns)
                {
                    if (filename.Contains(pattern))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null && !props.Contains(prefab))
                        {
                            props.Add(prefab);
                            break;
                        }
                    }
                }
            }
        }

        if (props.Count > 0)
        {
            menu.propPrefabs = props.ToArray();
            Debug.Log($"[MainMenuUI] Found {props.Count} prop prefabs");
        }

        // Find dust effect prefab
        if (menu.dustPrefab == null)
        {
            string[] dustPaths = new string[]
            {
                "Assets/Synty/PolygonMilitary/Prefabs/FX/FX_Dust_Blowing_Soft_Large_01.prefab",
                "Assets/Synty/PolygonMilitary/Prefabs/FX/FX_Dust_Blowing_Soft_01.prefab",
                "Assets/PolygonParticleFX/Prefabs/FX_Dust_Blowing_Soft_01.prefab"
            };

            foreach (var path in dustPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    menu.dustPrefab = prefab;
                    Debug.Log($"[MainMenuUI] Found dust prefab: {path}");
                    break;
                }
            }
        }

        // Find idle animation clip (Mixamo)
        if (menu.idleAnimationClip == null)
        {
            string[] idleClipPaths = new string[]
            {
                "Assets/Rifle Aiming Idle.fbx",
                "Assets/Animations/Rifle Aiming Idle.fbx",
                "Assets/Mixamo/Rifle Aiming Idle.fbx"
            };

            foreach (var path in idleClipPaths)
            {
                // Load all assets from the FBX
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        menu.idleAnimationClip = clip;
                        Debug.Log($"[MainMenuUI] Found idle animation clip: {clip.name} from {path}");
                        break;
                    }
                }
                if (menu.idleAnimationClip != null) break;
            }

            // Fallback - search for any idle animation
            if (menu.idleAnimationClip == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip Idle");
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Aiming") || path.Contains("Standing"))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        if (clip != null && !clip.name.Contains("__preview__"))
                        {
                            menu.idleAnimationClip = clip;
                            Debug.Log($"[MainMenuUI] Found fallback idle clip: {clip.name}");
                            break;
                        }
                    }
                }
            }
        }

        // Also find animator controller as backup
        if (menu.soldierAnimator == null)
        {
            string[] animatorPaths = new string[]
            {
                "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/AC_Polygon_Masculine.controller",
                "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/AC_Polygon_Feminine.controller"
            };

            foreach (var path in animatorPaths)
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (controller != null)
                {
                    menu.soldierAnimator = controller;
                    Debug.Log($"[MainMenuUI] Found animator controller: {path}");
                    break;
                }
            }
        }

        EditorUtility.SetDirty(menu);

        string animInfo = menu.idleAnimationClip != null ? $", Idle: {menu.idleAnimationClip.name}" : "";
        string dustInfo = menu.dustPrefab != null ? $", Dust: {menu.dustPrefab.name}" : "";
        Debug.Log($"[MainMenuUI] Asset population complete! Soldiers: {soldiers.Count}, Vehicles: {vehicles.Count}, Props: {props.Count}{dustInfo}{animInfo}");
    }

    [MenuItem("CONTEXT/MainMenuUI/Auto-Find Synty Assets")]
    static void AutoFindFromContextMenu(MenuCommand command)
    {
        MainMenuUI menu = (MainMenuUI)command.context;
        var editor = CreateEditor(menu) as MainMenuUIEditor;
        editor.AutoPopulateAssets(menu);
        DestroyImmediate(editor);
    }
}
#endif
