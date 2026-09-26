using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoopGame.CarrySystem;
using CoopGame.Network;

namespace CoopGame.EditorTools
{
    public static class DeliveryZoneSetupUtility
    {
        [MenuItem("CoopGame/Open SampleScene")]
        public static void OpenSampleScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        }

        [MenuItem("CoopGame/Setup Delivery Zone & Place in Level01")]
        public static void SetupAndPlaceInLevel01()
        {
            Debug.Log("<color=cyan>[DeliveryZoneSetup] Starting setup...</color>");

            // 1. Ensure Materials folder & DeliveryZone_Mat.mat
            string matPath = "Assets/Materials/DeliveryZone_Mat.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader shader = Shader.Find("DontDropIt/DeliveryZoneRing");
            if (shader == null)
            {
                Debug.LogError("[DeliveryZoneSetup] Shader 'DontDropIt/DeliveryZoneRing' not found!");
                return;
            }

            if (mat == null)
            {
                mat = new Material(shader);
                mat.SetColor("_Color", new Color(1.0f, 0.15f, 0.15f, 1.0f));
                mat.SetFloat("_InnerAlpha", 0.18f);
                mat.SetFloat("_RingThickness", 0.08f);
                mat.SetFloat("_EdgeGlow", 2.5f);
                mat.SetFloat("_PulseSpeed", 2.5f);
                mat.SetFloat("_RotationSpeed", 1.0f);
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[DeliveryZoneSetup] Created Material at {matPath}");
            }
            else
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            // 2. Build Prefab
            string prefabDir = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabDir))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            string prefabPath = "Assets/Prefabs/DeliveryZone.prefab";
            GameObject tempRoot = new GameObject("DeliveryZone");
            DeliveryZone zoneComp = tempRoot.AddComponent<DeliveryZone>();

            // Setup Quad Visual
            GameObject ringQuad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringQuad.name = "Zone_Circle_Visual";
            ringQuad.transform.SetParent(tempRoot.transform, false);
            ringQuad.transform.localRotation = Quaternion.identity;
            ringQuad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            float radius = 3.5f;
            ringQuad.transform.localScale = new Vector3(radius, 0.01f, radius); // flat disc
            var quadCol = ringQuad.GetComponent<Collider>();
            if (quadCol != null) Object.DestroyImmediate(quadCol);
            var quadRenderer = ringQuad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = mat;

            // Setup Particle System with dedicated URP particle material (never pink)
            string particleMatPath = "Assets/Materials/ZoneParticle_Mat.mat";
            Material particleMat = AssetDatabase.LoadAssetAtPath<Material>(particleMatPath);

            GameObject psObj = new GameObject("Perimeter_Particles");
            psObj.transform.SetParent(tempRoot.transform, false);
            psObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ParticleSystem ps = psObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.2f;
            main.startSpeed = 0.5f;
            main.startSize = 0.15f;
            main.startColor = new Color(1.0f, 0.15f, 0.15f, 1.0f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            var emission = ps.emission;
            emission.rateOverTime = 25f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3.5f * 0.98f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            colorOverLife.color = grad;

            ParticleSystemRenderer psRenderer = psObj.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null && particleMat != null)
            {
                psRenderer.sharedMaterial = particleMat;
            }

            // Save as Prefab
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, prefabPath);
            Object.DestroyImmediate(tempRoot);
            Debug.Log($"<color=lime>[DeliveryZoneSetup] Prefab saved at {prefabPath}</color>");

            // 3. Open Level01 scene and configure Delivery Zone
            string level01ScenePath = "Assets/Scenes/Level01.unity";
            Scene currentScene = SceneManager.GetActiveScene();
            bool needRestore = false;
            string originalScenePath = currentScene.path;

            if (currentScene.path != level01ScenePath)
            {
                needRestore = true;
                EditorSceneManager.OpenScene(level01ScenePath, OpenSceneMode.Single);
            }

            // Find or instantiate DeliveryZone in Level01
            DeliveryZone existingZone = Object.FindFirstObjectByType<DeliveryZone>();
            GameObject zoneInstance;
            if (existingZone != null)
            {
                zoneInstance = existingZone.gameObject;
            }
            else
            {
                zoneInstance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab);
                zoneInstance.name = "DeliveryZone";
                Undo.RegisterCreatedObjectUndo(zoneInstance, "Instantiate DeliveryZone");
            }

            // Position at final platform "floating Ground (13)"
            // floating Ground (13) is at x: -15.2, y: -12.3, z: 161.5, scale y is 1.066 -> top surface y = -11.767
            GameObject goalGround = GameObject.Find("floating Ground (13)");
            if (goalGround != null)
            {
                float surfaceY = goalGround.transform.position.y + (goalGround.transform.localScale.y * 0.5f);
                zoneInstance.transform.position = new Vector3(goalGround.transform.position.x, surfaceY, goalGround.transform.position.z);
            }
            else
            {
                zoneInstance.transform.position = new Vector3(-15.2f, -11.75f, 161.5f);
            }

            // Check if Level01 has a CarryablePackage
            CarryableObject existingPackage = Object.FindFirstObjectByType<CarryableObject>();
            GameObject packageInstance = null;
            if (existingPackage != null)
            {
                packageInstance = existingPackage.gameObject;
            }
            else
            {
                // Instantiate CarryablePackage prefab at start ground
                string packagePrefabPath = "Assets/Prefabs/Items/CarryablePackage.prefab";
                GameObject packagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(packagePrefabPath);
                if (packagePrefab != null)
                {
                    packageInstance = (GameObject)PrefabUtility.InstantiatePrefab(packagePrefab);
                    packageInstance.name = "CarryablePackage";
                    GameObject startGround = GameObject.Find("Level01_Ground");
                    if (startGround != null)
                    {
                        float startY = startGround.transform.position.y + (startGround.transform.localScale.y * 0.5f) + 0.5f;
                        packageInstance.transform.position = new Vector3(startGround.transform.position.x, startY, startGround.transform.position.z);
                    }
                    else
                    {
                        packageInstance.transform.position = new Vector3(0f, 1f, 0f);
                    }
                    Undo.RegisterCreatedObjectUndo(packageInstance, "Instantiate CarryablePackage");
                }
            }

            // Hook up targetDeliveryObject on DeliveryZone
            DeliveryZone zoneInScene = zoneInstance.GetComponent<DeliveryZone>();
            if (zoneInScene != null && packageInstance != null)
            {
                SerializedObject soZone = new SerializedObject(zoneInScene);
                soZone.FindProperty("_targetDeliveryObject").objectReferenceValue = packageInstance;
                soZone.ApplyModifiedProperties();
                EditorUtility.SetDirty(zoneInScene);
                Debug.Log($"<color=lime>[DeliveryZoneSetup] DeliveryZone targetDeliveryObject successfully assigned to: {packageInstance.name}</color>");
            }

            // 4. Remove any accidental LobbyUI from Level01 (Lobby & hosting are strictly in SampleScene)
            LobbyUI lobbyInLevel = Object.FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (lobbyInLevel != null)
            {
                Object.DestroyImmediate(lobbyInLevel.gameObject);
                Debug.Log("<color=yellow>[DeliveryZoneSetup] Cleaned up LobbyUI from Level01. Lobby is only in SampleScene.</color>");
            }

            // Save scene
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("<color=lime>[DeliveryZoneSetup] Level01 saved with configured DeliveryZone!</color>");

            if (needRestore && !string.IsNullOrEmpty(originalScenePath) && originalScenePath != level01ScenePath)
            {
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }
        }
    }
}
