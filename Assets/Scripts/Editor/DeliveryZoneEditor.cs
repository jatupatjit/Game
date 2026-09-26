using UnityEditor;
using UnityEngine;
using CoopGame.CarrySystem;

namespace CoopGame.EditorTools
{
    [CustomEditor(typeof(DeliveryZone))]
    public class DeliveryZoneEditor : Editor
    {
        private SerializedProperty _targetDeliveryObjectProp;
        private SerializedProperty _detectionModeProp;
        private SerializedProperty _targetTagProp;
        private SerializedProperty _radiusProp;
        private SerializedProperty _triggerHeightProp;
        private SerializedProperty _waitingRedColorProp;
        private SerializedProperty _deliveredGreenColorProp;
        private SerializedProperty _colorTransitionSpeedProp;
        private SerializedProperty _lockDeliveryOnceEnteredProp;
        private SerializedProperty _freezeObjectOnDeliveryProp;

        private void OnEnable()
        {
            _targetDeliveryObjectProp = serializedObject.FindProperty("_targetDeliveryObject");
            _detectionModeProp = serializedObject.FindProperty("_detectionMode");
            _targetTagProp = serializedObject.FindProperty("_targetTag");
            _radiusProp = serializedObject.FindProperty("_radius");
            _triggerHeightProp = serializedObject.FindProperty("_triggerHeight");
            _waitingRedColorProp = serializedObject.FindProperty("_waitingRedColor");
            _deliveredGreenColorProp = serializedObject.FindProperty("_deliveredGreenColor");
            _colorTransitionSpeedProp = serializedObject.FindProperty("_colorTransitionSpeed");
            _lockDeliveryOnceEnteredProp = serializedObject.FindProperty("_lockDeliveryOnceEntered");
            _freezeObjectOnDeliveryProp = serializedObject.FindProperty("_freezeObjectOnDelivery");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DeliveryZone zone = (DeliveryZone)target;

            // Header Status Box
            EditorGUILayout.Space(6);
            if (Application.isPlaying)
            {
                if (zone.IsDelivered || zone.IsTargetInside)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.9f, 0.4f, 1f);
                    EditorGUILayout.HelpBox("🟢 TARGET INSIDE ZONE - EFFECT IS GREEN", MessageType.Info);
                }
                else
                {
                    GUI.backgroundColor = new Color(1.0f, 0.3f, 0.3f, 1f);
                    EditorGUILayout.HelpBox("🔴 WAITING FOR DELIVERY OBJECT - EFFECT IS RED", MessageType.Warning);
                }
                GUI.backgroundColor = Color.white;
            }

            // Section 1: Target Object
            EditorGUILayout.LabelField("🎯 Delivery Target", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_targetDeliveryObjectProp, new GUIContent("Target Delivery Object", "Drag the GameObject you want to deliver here (e.g. CarryablePackage, box, or item)."));

            EditorGUILayout.PropertyField(_detectionModeProp, new GUIContent("Detection Mode"));
            if (_detectionModeProp.enumValueIndex == (int)DeliveryDetectionMode.ByTag)
            {
                EditorGUILayout.PropertyField(_targetTagProp, new GUIContent("Target Tag"));
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            // Section 2: Zone Size
            EditorGUILayout.LabelField("⭕ Zone Dimensions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_radiusProp, new GUIContent("Circle Radius"));
            EditorGUILayout.PropertyField(_triggerHeightProp, new GUIContent("Trigger Height"));
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            // Section 3: Visual Colors
            EditorGUILayout.LabelField("✨ Effect Colors (Red -> Green)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_waitingRedColorProp, new GUIContent("Waiting Effect (Red)"));
            EditorGUILayout.PropertyField(_deliveredGreenColorProp, new GUIContent("Delivered Effect (Green)"));
            EditorGUILayout.PropertyField(_colorTransitionSpeedProp, new GUIContent("Transition Speed"));
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            // Section 4: Rules
            EditorGUILayout.LabelField("⚙️ Delivery Behavior", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_lockDeliveryOnceEnteredProp, new GUIContent("Lock On Delivered", "Keep green permanently once target enters."));
            EditorGUILayout.PropertyField(_freezeObjectOnDeliveryProp, new GUIContent("Freeze On Delivery", "Stop object movement when delivered."));
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            if (GUILayout.Button("🔄 Setup / Refresh Visuals", GUILayout.Height(28)))
            {
                zone.EnsureVisuals();
                EditorUtility.SetDirty(zone);
            }

            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem("GameObject/3D Object/CoopGame/Delivery Zone", false, 10)]
        [MenuItem("CoopGame/Create Delivery Zone In Scene")]
        public static void CreateDeliveryZoneInScene()
        {
            GameObject zoneObj = new GameObject("DeliveryZone");
            DeliveryZone zone = zoneObj.AddComponent<DeliveryZone>();
            zone.EnsureVisuals();

            // Try positioning near scene view camera focus
            if (SceneView.lastActiveSceneView != null)
            {
                Vector3 camPos = SceneView.lastActiveSceneView.pivot;
                zoneObj.transform.position = new Vector3(camPos.x, camPos.y, camPos.z);
            }

            Selection.activeGameObject = zoneObj;
            Undo.RegisterCreatedObjectUndo(zoneObj, "Create Delivery Zone");
            Debug.Log("<color=cyan>[DeliveryZoneEditor] Created Delivery Zone in current scene.</color>");
        }
    }
}
