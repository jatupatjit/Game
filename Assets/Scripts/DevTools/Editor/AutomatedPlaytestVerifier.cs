using System;
using System.Collections.Generic;
using System.IO;
using CoopGame.CarrySystem;
using CoopGame.Network;
using CoopGame.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace CoopGame.EditorTools
{
    /// <summary>Read-only Edit Mode inspection. No gameplay methods or network sessions are executed.</summary>
    public static class AutomatedPlaytestVerifier
    {
        private static readonly string[] RequiredScenes = { "Lobby", "Level01", "Level02" };

        [Serializable]
        public sealed class CheckResult
        {
            public string status;
            public string scope;
            public string message;
        }

        [Serializable]
        public sealed class Report
        {
            public string verificationType = "Read-only structural audit";
            public string projectPath;
            public string unityVersion;
            public string utcTime;
            public string structuralStatus = "UNVERIFIED";
            public string gameplayStatus = "UNVERIFIED";
            public string gameplayLimit = "Movement, climbing, carry physics, IK, stamina, HP, delivery, " +
                "UI interaction, Host/Client replication, late join and scene transitions were not exercised.";
            public int scenesChecked, prefabsChecked, gameObjectsChecked, componentsChecked, serializedReferencesChecked;
            public int checksPerformed, passedChecks, failedChecks, warnings, unverifiedChecks;
            public List<CheckResult> checks = new List<CheckResult>();
            public List<CheckResult> issues = new List<CheckResult>();
            public string ToJson() => JsonUtility.ToJson(this, true);

            internal void Check(bool passed, string scope, string message) => Add(passed ? "PASS" : "FAIL", scope, message);
            internal void Add(string status, string scope, string message)
            {
                var result = new CheckResult { status = status, scope = scope, message = message };
                checks.Add(result);
                if (status == "PASS") { checksPerformed++; passedChecks++; }
                else if (status == "FAIL") { checksPerformed++; failedChecks++; issues.Add(result); }
                else if (status == "WARNING") { warnings++; issues.Add(result); }
                else if (status == "UNVERIFIED") { unverifiedChecks++; issues.Add(result); }
            }
            internal void Finish() => structuralStatus = failedChecks > 0 ? "FAIL" : unverifiedChecks > 0 ? "UNVERIFIED" : "PASS";
        }

        // Keep the original entrypoint for callers, with an accurate menu label.
        [MenuItem("CoopGame/Run Structural Verification (Gameplay Unverified)")]
        public static void RunVerification() => Debug.Log("[StructuralVerifier] " + RunStructuralVerificationJson());
        public static string RunStructuralVerificationJson() => RunStructuralVerification().ToJson();

        public static Report RunStructuralVerification()
        {
            var report = new Report
            {
                projectPath = Path.GetDirectoryName(Application.dataPath),
                unityVersion = Application.unityVersion,
                utcTime = DateTime.UtcNow.ToString("O")
            };
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                report.Add("UNVERIFIED", "Editor", "Run in stable Edit Mode after compilation/import finish. The existing session was left untouched.");
                report.Finish();
                return report;
            }

            Scene originalActive = SceneManager.GetActiveScene();
            var originalHandles = new HashSet<SceneHandle>();
            for (int i = 0; i < SceneManager.sceneCount; i++) originalHandles.Add(SceneManager.GetSceneAt(i).handle);
            var openedScenes = new List<Scene>();
            var auditedPrefabs = new HashSet<string>(StringComparer.Ordinal);
            var enabledPaths = new HashSet<string>(StringComparer.Ordinal);
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene entry in buildScenes) if (entry.enabled) enabledPaths.Add(entry.path);
            try
            {
                foreach (string name in RequiredScenes)
                {
                    string path = FindBuildScene(name, buildScenes, report);
                    if (path == null) continue;
                    bool exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;
                    report.Check(exists, path, "Build scene asset exists.");
                    if (!exists) continue;
                    Scene scene = SceneManager.GetSceneByPath(path);
                    try
                    {
                        if (!scene.isLoaded)
                        {
                            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                            if (scene.IsValid() && scene.isLoaded && !originalHandles.Contains(scene.handle)) openedScenes.Add(scene);
                        }
                        if (!scene.IsValid() || !scene.isLoaded)
                        {
                            report.Add("UNVERIFIED", path, "Scene could not be loaded for inspection.");
                            continue;
                        }
                        if (originalHandles.Contains(scene.handle) && scene.isDirty)
                            report.Add("WARNING", path, "Inspected the existing unsaved scene state. The saved build version may differ; nothing was saved or discarded.");
                        report.scenesChecked++;
                        InspectHierarchy(scene.GetRootGameObjects(), path, report);
                        InspectManagers(scene, auditedPrefabs, report);
                        InspectUi(scene, report);
                        if (name == "Lobby") InspectLobbyPortal(scene, enabledPaths, report);
                        else InspectMission(scene, enabledPaths, report);
                    }
                    catch (Exception error) { report.Add("UNVERIFIED", path, "Inspection stopped for this scene: " + error.Message); }
                }
                report.Check(auditedPrefabs.Count > 0, "Player configuration",
                    "Inspected " + auditedPrefabs.Count + " player prefab(s) referenced by scene NetworkManager configuration.");
            }
            catch (Exception error) { report.Add("UNVERIFIED", "Audit", "Inspection could not finish: " + error.Message); }
            finally
            {
                // Restoring a whole scene setup can discard preexisting unsaved work. Restore only the active scene.
                try
                {
                    if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
                }
                catch (Exception error) { report.Add("UNVERIFIED", "Editor", "Could not restore the active scene: " + error.Message); }
                for (int i = openedScenes.Count - 1; i >= 0; i--)
                {
                    Scene scene = openedScenes[i];
                    if (!scene.IsValid() || !scene.isLoaded || originalHandles.Contains(scene.handle)) continue;
                    if (scene.isDirty)
                    {
                        report.Add("UNVERIFIED", scene.path, "A scene opened for inspection became dirty, possibly through an Editor callback. It was kept open to preserve changes.");
                        continue;
                    }
                    try
                    {
                        if (!EditorSceneManager.CloseScene(scene, true)) report.Add("UNVERIFIED", scene.path, "Could not close the clean scene opened for inspection.");
                    }
                    catch (Exception error) { report.Add("UNVERIFIED", scene.path, "Scene cleanup failed: " + error.Message); }
                }
            }
            report.Finish();
            return report;
        }

        private static string FindBuildScene(string name, EditorBuildSettingsScene[] scenes, Report report)
        {
            string path = null;
            int count = 0;
            foreach (EditorBuildSettingsScene scene in scenes)
                if (scene.enabled && Path.GetFileNameWithoutExtension(scene.path) == name) { count++; path = scene.path; }
            report.Check(count == 1, "Build Settings", name + " requires exactly one enabled build scene; found " + count + ".");
            return count == 1 ? path : null;
        }

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static T RequireSingle<T>(Scene scene, Report report) where T : Behaviour
        {
            List<T> found = FindInScene<T>(scene);
            report.Check(found.Count == 1, scene.path, "Expected one " + typeof(T).Name + "; found " + found.Count + ".");
            if (found.Count != 1) return null;
            RequireEnabled(found[0], scene.path, report);
            return found[0];
        }

        private static void RequireEnabled(Behaviour target, string scope, Report report) => report.Check(
            target.enabled && target.gameObject.activeInHierarchy, scope, ObjectPath(target) + " is enabled on an active object.");

        private static void InspectManagers(Scene scene, HashSet<string> audited, Report report)
        {
            List<NetworkManager> managers = FindInScene<NetworkManager>(scene);
            if (scene.name == "Lobby") report.Check(managers.Count == 1, scene.path, "Lobby requires one NetworkManager; found " + managers.Count + ".");
            else if (managers.Count == 0) report.Add("INFO", scene.path, "No scene-local NetworkManager; this level relies on the persistent Lobby session manager.");
            else report.Check(managers.Count == 1, scene.path, "At most one scene-local NetworkManager; found " + managers.Count + ".");
            foreach (NetworkManager manager in managers)
            {
                RequireEnabled(manager, scene.path, report);
                if (manager.NetworkConfig == null) { report.Check(false, scene.path, ObjectPath(manager) + " has no NetworkConfig."); continue; }
                report.Check(manager.NetworkConfig.EnableSceneManagement, scene.path, ObjectPath(manager) + " enables NGO scene management.");
                GameObject prefab = manager.NetworkConfig.PlayerPrefab;
                string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : string.Empty;
                bool isAsset = prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab) && !string.IsNullOrEmpty(path);
                report.Check(isAsset, scene.path, ObjectPath(manager) + " references a player prefab asset via NetworkConfig.PlayerPrefab.");
                if (isAsset && audited.Add(path)) InspectPlayer(prefab, path, report);
            }
        }

        private static void InspectPlayer(GameObject prefab, string path, Report report)
        {
            report.prefabsChecked++;
            InspectHierarchy(new[] { prefab }, path, report);
            report.Check(prefab.activeSelf, path, "Configured player prefab root is active.");
            Type[] required = {
                typeof(NetworkObject), typeof(NetworkPlayer), typeof(CharacterController), typeof(PlayerMovement),
                typeof(PlayerInputReader), typeof(PlayerCameraController), typeof(PlayerStamina), typeof(PlayerStaminaUI),
                typeof(Wallclimb), typeof(PlayerCarry), typeof(ProceduralPlayerArms), typeof(ProceduralPlayerLegs),
                typeof(PlayerExpeditionState), typeof(PlayerAppearance), typeof(NetworkTransform)
            };
            foreach (Type type in required)
            {
                Component[] components = prefab.GetComponents(type);
                report.Check(components.Length == 1, path, "Player root requires one " + type.Name + "; found " + components.Length + ".");
                if (components.Length == 1 && components[0] is Behaviour behaviour) report.Check(behaviour.enabled, path, type.Name + " is enabled on the player prefab.");
            }
            report.Add("INFO", path, "PlayerStaminaUI generates the owner HUD at runtime; no scene-level stamina UI is required. Bone resolution and rendered IK remain gameplay UNVERIFIED.");
        }

        private static void InspectHierarchy(GameObject[] roots, string scope, Report report)
        {
            int missingScripts = 0, missingReferences = 0;
            foreach (GameObject root in roots)
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                report.gameObjectsChecked++;
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                missingScripts += missing;
                if (missing > 0) report.Check(false, scope, ObjectPath(transform) + " contains " + missing + " missing script(s).");
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null) continue;
                    report.componentsChecked++;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                            report.serializedReferencesChecked++;
                            if (property.objectReferenceValue != null || property.objectReferenceEntityIdValue == EntityId.None) continue;
                            missingReferences++;
                            report.Check(false, scope, ObjectPath(component) + "." + property.propertyPath + " contains a missing serialized object reference.");
                        }
                    }
                }
            }
            if (missingScripts == 0) report.Check(true, scope, "No missing scripts in the inspected hierarchy.");
            if (missingReferences == 0) report.Check(true, scope, "No dangling serialized object references. Optional nulls are allowed; required wiring is checked separately.");
        }

        private static void InspectUi(Scene scene, Report report)
        {
            RequireSingle<EventSystem>(scene, report);
            PauseMenu pause = RequireSingle<PauseMenu>(scene, report);
            if (pause != null) InspectBuiltUi(pause, "_pauseRoot", scene.path, report,
                "_pausePanel", "_settingsPanel", "_continueButton", "_settingsButton", "_quitToMenuButton",
                "_backFromSettingsButton", "_confirmDialog", "_confirmQuitButton", "_cancelQuitButton");
            if (scene.name == "Lobby")
            {
                LobbyUI lobby = RequireSingle<LobbyUI>(scene, report);
                if (lobby != null) InspectBuiltUi(lobby, "_lobbyRoot", scene.path, report, "_mainPanel", "_hostRoomPanel",
                    "_joinPanel", "_settingsPanel", "_connectingPanel", "_hostRoomMenuButton", "_settingsMenuButton",
                    "_quitGameButton", "_hostButton", "_joinButton", "_backFromHostRoomButton", "_roomCodeInput",
                    "_confirmJoinButton", "_pasteCodeButton", "_backFromJoinButton", "_backFromSettingsButton",
                    "_connectingLabel", "_cancelConnectingButton", "_statusMessageLabel");
                RoomCodeHUD room = RequireSingle<RoomCodeHUD>(scene, report);
                if (room != null) InspectBuiltUi(room, "_hudGroup", scene.path, report,
                    "_roomCodeText", "_modeLabel", "_playerCountText", "_playerNamesText", "_copyButton", "_copyFeedbackText");
            }
            else
            {
                LevelSessionHUD hud = RequireSingle<LevelSessionHUD>(scene, report);
                if (hud != null)
                {
                    RequireComponent<CanvasGroup>(hud.gameObject, scene.path, report);
                    RequireReferences(hud, scene.path, report, "_modeLabel", "_playerCountText", "_playerNamesText");
                }
                RequireSingle<ExpeditionHUD>(scene, report);
                report.Check(FindInScene<LobbyUI>(scene).Count == 0 && FindInScene<RoomCodeHUD>(scene).Count == 0,
                    scene.path, "Gameplay uses LevelSessionHUD; LobbyUI and RoomCodeHUD are absent.");
            }
        }

        private static void InspectBuiltUi(Component target, string rootField, string scope, Report report, params string[] fields)
        {
            RequireComponent<Canvas>(target.gameObject, scope, report);
            using (var serialized = new SerializedObject(target))
            {
                SerializedProperty root = serialized.FindProperty(rootField);
                if (root == null) { report.Check(false, scope, ObjectPath(target) + " has no serialized " + rootField + " field."); return; }
                if (root.objectReferenceValue == null)
                {
                    report.Add("INFO", scope, ObjectPath(target) + " uses its Awake self-builder because the UI root is unset. Runtime creation and interaction remain gameplay UNVERIFIED.");
                    return;
                }
            }
            RequireReferences(target, scope, report, fields);
        }

        private static void InspectLobbyPortal(Scene scene, HashSet<string> enabled, Report report)
        {
            LobbyPortalGate portal = RequireSingle<LobbyPortalGate>(scene, report);
            if (portal == null) return;
            RequireComponent<NetworkObject>(portal.gameObject, scene.path, report);
            RequireReferences(portal, scene.path, report, "_portalVisualRoot", "_portalEntryZone");
            InspectDestination(portal, scene.path, enabled, report, false);
        }

        private static void InspectMission(Scene scene, HashSet<string> enabled, Report report)
        {
            LevelMission mission = RequireSingle<LevelMission>(scene, report);
            if (mission == null) return;
            RequireComponent<NetworkObject>(mission.gameObject, scene.path, report);
            RequireReferences(mission, scene.path, report, "_cargo", "_delivery");
            if (scene.name == "Level01") RequireReferences(mission, scene.path, report, "_portal");
            FragileCargo cargo = Reference<FragileCargo>(mission, "_cargo");
            DeliveryPoint delivery = Reference<DeliveryPoint>(mission, "_delivery");
            DeliveryExitPortal portal = Reference<DeliveryExitPortal>(mission, "_portal");
            if (scene.name == "Level02" && portal == null)
                report.Add("N/A", scene.path, "Level02 is the final supported stage; no next-stage portal is configured or required.");
            if (cargo != null)
            {
                RequireEnabled(cargo, scene.path, report);
                RequireComponent<NetworkObject>(cargo.gameObject, scene.path, report);
                RequireComponent<Rigidbody>(cargo.gameObject, scene.path, report);
                RequireComponent<NetworkTransform>(cargo.gameObject, scene.path, report);
                bool hasCollider = false;
                foreach (Collider collider in cargo.GetComponentsInChildren<Collider>(true))
                    hasCollider |= collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
                report.Check(hasCollider, scene.path, ObjectPath(cargo) + " has an enabled non-trigger collider.");
                report.Check(cargo.gameObject.scene == scene, scene.path, "Mission cargo belongs to this scene.");
            }
            if (delivery != null)
            {
                RequireEnabled(delivery, scene.path, report);
                RequireComponent<NetworkObject>(delivery.gameObject, scene.path, report);
                Collider trigger = RequireComponent<Collider>(delivery.gameObject, scene.path, report);
                if (trigger != null) report.Check(trigger.enabled && trigger.isTrigger, scene.path, ObjectPath(delivery) + " has an enabled trigger collider.");
                DeliveryZone zone = RequireComponent<DeliveryZone>(delivery.gameObject, scene.path, report);
                if (zone != null) { RequireEnabled(zone, scene.path, report); InspectDeliveryTarget(zone, cargo, scene.path, report); }
                report.Check(delivery.gameObject.scene == scene, scene.path, "Mission delivery point belongs to this scene.");
            }
            if (portal != null)
            {
                RequireEnabled(portal, scene.path, report);
                RequireComponent<NetworkObject>(portal.gameObject, scene.path, report);
                RequireReferences(portal, scene.path, report, "_deliveryPoint", "_entryVolume", "_portalVisual", "_closedBarrier", "_statusText");
                report.Check(delivery != null && Reference<DeliveryPoint>(portal, "_deliveryPoint") == delivery, scene.path, "Exit portal references the mission's delivery point.");
                BoxCollider entry = Reference<BoxCollider>(portal, "_entryVolume");
                if (entry != null) report.Check(entry.enabled && entry.gameObject.activeInHierarchy && entry.size.x > 0 && entry.size.y > 0 && entry.size.z > 0,
                    scene.path, "Exit portal entry volume is enabled, active and has positive dimensions.");
                report.Check(portal.gameObject.scene == scene, scene.path, "Mission exit portal belongs to this scene.");
                InspectDestination(portal, scene.path, enabled, report, scene.name == "Level02");
            }
        }

        private static void InspectDeliveryTarget(DeliveryZone zone, FragileCargo cargo, string scope, Report report)
        {
            using (var serialized = new SerializedObject(zone))
            {
                SerializedProperty modeField = serialized.FindProperty("_detectionMode");
                SerializedProperty targetField = serialized.FindProperty("_targetDeliveryObject");
                if (modeField == null || targetField == null) { report.Check(false, scope, "DeliveryZone detection fields could not be inspected."); return; }
                var mode = (DeliveryDetectionMode)modeField.intValue;
                GameObject target = targetField.objectReferenceValue as GameObject;
                if (mode == DeliveryDetectionMode.SpecificObjectOnly || (mode == DeliveryDetectionMode.SpecificObjectOrCarryable && target != null))
                {
                    bool matches = false;
                    if (cargo != null && target != null)
                        foreach (Collider collider in cargo.GetComponentsInChildren<Collider>(true))
                            matches |= collider.gameObject == target || collider.transform.IsChildOf(target.transform) ||
                                target.transform.IsChildOf(collider.transform);
                    report.Check(matches, scope, "DeliveryZone's specific target identifies a mission cargo collider.");
                }
                else if (mode == DeliveryDetectionMode.ByTag)
                {
                    SerializedProperty tag = serialized.FindProperty("_targetTag");
                    bool matches = false;
                    if (cargo != null && tag != null)
                        foreach (Collider collider in cargo.GetComponentsInChildren<Collider>(true))
                            matches |= collider.gameObject.tag == tag.stringValue ||
                                (collider.transform.parent != null && collider.transform.parent.gameObject.tag == tag.stringValue);
                    report.Check(matches, scope, "DeliveryZone target tag matches a mission cargo collider or its immediate parent.");
                }
                else if (mode == DeliveryDetectionMode.AnyCarryable || mode == DeliveryDetectionMode.SpecificObjectOrCarryable)
                    report.Add("INFO", scope, "DeliveryZone accepts carryable cargo via " + mode + "; no specific target reference is required.");
                else report.Check(false, scope, "DeliveryZone has an unsupported detection mode: " + modeField.intValue + ".");
            }
        }

        private static void InspectDestination(Component portal, string scope, HashSet<string> enabled, Report report, bool allowUnconfiguredNextStage)
        {
            using (var serialized = new SerializedObject(portal))
            {
                SerializedProperty destination = serialized.FindProperty("_destinationSceneName");
                if (destination == null) { report.Check(false, scope, ObjectPath(portal) + " has no serialized destination field."); return; }
                string name = destination.stringValue;
                if (string.IsNullOrWhiteSpace(name) && allowUnconfiguredNextStage)
                {
                    report.Add("WARNING", scope, "The destination after Level02 is unset; that next-stage transition is unavailable and remains UNVERIFIED.");
                    return;
                }
                int matches = 0;
                foreach (string path in enabled) if (Path.GetFileNameWithoutExtension(path) == name) matches++;
                report.Check(matches == 1 && !string.IsNullOrWhiteSpace(name), scope, ObjectPath(portal) + " destination '" + name + "' resolves to one enabled build scene; found " + matches + ".");
            }
        }

        private static T RequireComponent<T>(GameObject target, string scope, Report report) where T : Component
        {
            T component = target.GetComponent<T>();
            report.Check(component != null, scope, ObjectPath(target.transform) + " requires " + typeof(T).Name + ".");
            return component;
        }

        private static void RequireReferences(Component target, string scope, Report report, params string[] fields)
        {
            using (var serialized = new SerializedObject(target))
            foreach (string field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                report.Check(property != null && property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null,
                    scope, ObjectPath(target) + "." + field + " is wired.");
            }
        }

        private static T Reference<T>(Component target, string field) where T : UnityEngine.Object
        {
            using (var serialized = new SerializedObject(target)) return serialized.FindProperty(field)?.objectReferenceValue as T;
        }

        private static string ObjectPath(Component component)
        {
            string path = component.gameObject.name;
            for (Transform parent = component.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path + " (" + component.GetType().Name + ")";
        }
    }
}
