using System;
using UnityEngine;

namespace CoopGame.Player
{
    /// <summary>
    /// One place that knows the player's model. Finds the bones of the CharacterVisual child
    /// through its Humanoid avatar (any Mixamo or Humanoid FBX), with a name fallback for the
    /// old hand-made rig, plus the body and face renderers. Arms, legs, appearance and the
    /// animator driver ask this component instead of searching bone names themselves, so a
    /// new model only needs a Humanoid avatar.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class CharacterRig : MonoBehaviour
    {
        public const string VisualName = "CharacterVisual";

        /// <summary>One finger: up to three joints from knuckle to tip, with the local axis that curls it.</summary>
        public sealed class Finger
        {
            public string Name;
            public bool IsThumb;
            public Transform[] Joints;
            public Vector3[] CurlAxes;
            public Quaternion[] RestRotations;
        }

        [SerializeField] private Transform _visual;

        private Animator _animator;
        private Transform _hips, _spine, _chest, _neck, _head;
        private Transform _upperArmL, _lowerArmL, _handL, _upperArmR, _lowerArmR, _handR;
        private Transform _upperLegL, _lowerLegL, _footL, _upperLegR, _lowerLegR, _footR;
        private Finger[] _fingersL = Array.Empty<Finger>();
        private Finger[] _fingersR = Array.Empty<Finger>();
        private Renderer _bodyRenderer;
        private Renderer _faceRenderer;
        private bool _resolved;

        public Transform Visual { get { Resolve(); return _visual; } }
        public Animator Animator { get { Resolve(); return _animator; } }
        public bool IsHumanoid { get { Resolve(); return _animator != null && _animator.isHuman; } }

        /// <summary>True once the Animator plays a controller that has at least one clip.
        /// Procedural idle and walk motion step aside for real animation.</summary>
        public bool HasAnimation
        {
            get
            {
                Resolve();
                return _animator != null && _animator.enabled && _animator.runtimeAnimatorController != null &&
                       _animator.runtimeAnimatorController.animationClips.Length > 0;
            }
        }

        public Transform Hips { get { Resolve(); return _hips; } }
        public Transform Spine { get { Resolve(); return _spine; } }
        public Transform Chest { get { Resolve(); return _chest; } }
        public Transform Neck { get { Resolve(); return _neck; } }
        public Transform Head { get { Resolve(); return _head; } }
        public Transform UpperArmL { get { Resolve(); return _upperArmL; } }
        public Transform LowerArmL { get { Resolve(); return _lowerArmL; } }
        public Transform HandL { get { Resolve(); return _handL; } }
        public Transform UpperArmR { get { Resolve(); return _upperArmR; } }
        public Transform LowerArmR { get { Resolve(); return _lowerArmR; } }
        public Transform HandR { get { Resolve(); return _handR; } }
        public Transform UpperLegL { get { Resolve(); return _upperLegL; } }
        public Transform LowerLegL { get { Resolve(); return _lowerLegL; } }
        public Transform FootL { get { Resolve(); return _footL; } }
        public Transform UpperLegR { get { Resolve(); return _upperLegR; } }
        public Transform LowerLegR { get { Resolve(); return _lowerLegR; } }
        public Transform FootR { get { Resolve(); return _footR; } }
        public Finger[] FingersL { get { Resolve(); return _fingersL; } }
        public Finger[] FingersR { get { Resolve(); return _fingersR; } }
        public Renderer BodyRenderer { get { Resolve(); return _bodyRenderer; } }
        public Renderer FaceRenderer { get { Resolve(); return _faceRenderer; } }

        private void Awake() => Resolve();

        /// <summary>Points the rig at a (new) visual and finds its bones again.</summary>
        public void SetVisual(Transform visual)
        {
            _visual = visual;
            _resolved = false;
            Resolve();
        }

        public void Resolve()
        {
            if (_resolved && _visual != null) return;
            if (_visual == null) _visual = transform.Find(VisualName);
            if (_visual == null) return;

            _animator = _visual.GetComponent<Animator>();
            if (_animator != null && _animator.isHuman) ResolveHumanoid();
            else ResolveByName();

            // The model's left side is the player's -X. Swap if a rig names its sides the other way.
            if (_upperArmL != null && _upperArmR != null &&
                transform.InverseTransformPoint(_upperArmL.position).x > transform.InverseTransformPoint(_upperArmR.position).x)
            {
                (_upperArmL, _upperArmR) = (_upperArmR, _upperArmL);
                (_lowerArmL, _lowerArmR) = (_lowerArmR, _lowerArmL);
                (_handL, _handR) = (_handR, _handL);
                (_fingersL, _fingersR) = (_fingersR, _fingersL);
            }

            ResolveRenderers();
            _resolved = _upperArmL != null && _upperArmR != null;
        }

        private void ResolveHumanoid()
        {
            Transform B(HumanBodyBones b) => _animator.GetBoneTransform(b);
            _hips = B(HumanBodyBones.Hips);
            _spine = B(HumanBodyBones.Spine);
            _chest = B(HumanBodyBones.UpperChest);
            if (_chest == null) _chest = B(HumanBodyBones.Chest);
            if (_chest == null) _chest = _spine;
            _neck = B(HumanBodyBones.Neck);
            _head = B(HumanBodyBones.Head);
            _upperArmL = B(HumanBodyBones.LeftUpperArm);
            _lowerArmL = B(HumanBodyBones.LeftLowerArm);
            _handL = B(HumanBodyBones.LeftHand);
            _upperArmR = B(HumanBodyBones.RightUpperArm);
            _lowerArmR = B(HumanBodyBones.RightLowerArm);
            _handR = B(HumanBodyBones.RightHand);
            _upperLegL = B(HumanBodyBones.LeftUpperLeg);
            _lowerLegL = B(HumanBodyBones.LeftLowerLeg);
            _footL = B(HumanBodyBones.LeftFoot);
            _upperLegR = B(HumanBodyBones.RightUpperLeg);
            _lowerLegR = B(HumanBodyBones.RightLowerLeg);
            _footR = B(HumanBodyBones.RightFoot);

            _fingersL = new[]
            {
                HumanFinger("Thumb", true, HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal),
                HumanFinger("Index", false, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal),
                HumanFinger("Middle", false, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal),
                HumanFinger("Ring", false, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal),
                HumanFinger("Little", false, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal),
            };
            _fingersR = new[]
            {
                HumanFinger("Thumb", true, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal),
                HumanFinger("Index", false, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal),
                HumanFinger("Middle", false, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal),
                HumanFinger("Ring", false, HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal),
                HumanFinger("Little", false, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal),
            };
            _fingersL = Array.FindAll(_fingersL, f => f != null);
            _fingersR = Array.FindAll(_fingersR, f => f != null);
            MeasureFingerCurlAxes();
        }

        private Finger HumanFinger(string name, bool thumb, params HumanBodyBones[] bones)
        {
            var joints = Array.FindAll(Array.ConvertAll(bones, b => _animator.GetBoneTransform(b)), t => t != null);
            if (joints.Length == 0) return null;
            return new Finger
            {
                Name = name, IsThumb = thumb, Joints = joints,
                CurlAxes = new Vector3[joints.Length],
                RestRotations = Array.ConvertAll(joints, j => j.localRotation),
            };
        }

        /// <summary>
        /// Asks the avatar which way each finger joint closes: pose a fist through the Humanoid
        /// muscles, read each joint's change, then put the bind pose back.
        /// </summary>
        private void MeasureFingerCurlAxes()
        {
            Transform[] all = _visual.GetComponentsInChildren<Transform>(true);
            var savedRot = new Quaternion[all.Length];
            var savedPos = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) { savedRot[i] = all[i].localRotation; savedPos[i] = all[i].localPosition; }

            try
            {
                var handler = new HumanPoseHandler(_animator.avatar, _visual);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                string[] muscles = HumanTrait.MuscleName;
                for (int m = 0; m < muscles.Length; m++)
                    if (muscles[m].Contains("Stretched")) pose.muscles[m] = -1f;
                handler.SetHumanPose(ref pose);
                Measure(_fingersL);
                Measure(_fingersR);
                handler.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CharacterRig] Could not measure finger curl from the avatar, using +X: " + e.Message);
                foreach (var f in _fingersL) for (int j = 0; j < f.CurlAxes.Length; j++) f.CurlAxes[j] = Vector3.right;
                foreach (var f in _fingersR) for (int j = 0; j < f.CurlAxes.Length; j++) f.CurlAxes[j] = Vector3.right;
            }
            finally
            {
                for (int i = 0; i < all.Length; i++) { all[i].localRotation = savedRot[i]; all[i].localPosition = savedPos[i]; }
            }
        }

        private static void Measure(Finger[] fingers)
        {
            foreach (var f in fingers)
            {
                for (int j = 0; j < f.Joints.Length; j++)
                {
                    Quaternion delta = Quaternion.Inverse(f.RestRotations[j]) * f.Joints[j].localRotation;
                    delta.ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f) { angle = 360f - angle; axis = -axis; }
                    f.CurlAxes[j] = angle > 1f && axis.sqrMagnitude > 0.5f ? axis.normalized : Vector3.right;
                }
            }
        }

        /// <summary>
        /// Hand targets from carry and climbing are built as LookRotation(back of hand, fingers).
        /// Returns the local rotation that turns that frame into this hand bone's own axes:
        /// bone rotation = target rotation * fix. Measured from the current pose, so call it at bind pose.
        /// </summary>
        public Quaternion MeasureHandTargetFix(bool left)
        {
            Resolve();
            Transform hand = left ? _handL : _handR;
            Finger[] fingers = left ? _fingersL : _fingersR;
            if (hand == null) return Quaternion.identity;

            Finger reference = null;
            foreach (var f in fingers)
                if (!f.IsThumb && f.Joints.Length >= 2 && (reference == null || f.Name == "Middle")) reference = f;
            if (reference == null) return Quaternion.identity;

            Vector3 fingerDir = reference.Joints[0].position - hand.position;
            Vector3 curlAxis = reference.Joints[0].rotation * reference.CurlAxes[0];
            // A joint turning about curlAxis carries its finger toward Cross(axis, finger): the palm side.
            Vector3 palm = Vector3.Cross(curlAxis, (reference.Joints[1].position - reference.Joints[0].position).normalized);
            if (fingerDir.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return Quaternion.identity;
            Vector3 back = Vector3.ProjectOnPlane(-palm, fingerDir);
            if (back.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.Inverse(Quaternion.LookRotation(back.normalized, fingerDir.normalized)) * hand.rotation;
        }

        private void ResolveByName()
        {
            // Old hand-made rig (Rigged_Hand_character_). Its "L"/"R" are mirrored; the side swap in Resolve fixes that.
            _hips = Find("Hip");
            _spine = Find("Spine");
            _chest = Find("Chest");
            _neck = Find("Neck");
            _head = Find("Head");
            _upperArmL = Find("UpperArmL");
            _upperArmR = Find("UpperArmR");
            _lowerArmL = _upperArmL != null && _upperArmL.childCount > 0 ? _upperArmL.GetChild(0) : Find("LowerArm.L");
            _lowerArmR = _upperArmR != null && _upperArmR.childCount > 0 ? _upperArmR.GetChild(0) : Find("LowerArm.R");
            _handL = _lowerArmL != null && _lowerArmL.childCount > 0 ? _lowerArmL.GetChild(0) : Find("Wrist.L");
            _handR = _lowerArmR != null && _lowerArmR.childCount > 0 ? _lowerArmR.GetChild(0) : Find("Wrist.R");
            _upperLegL = Find("UpperLeg.L");
            _upperLegR = Find("UpperLeg.R");
            _lowerLegL = Find("Foot.L");
            _lowerLegR = Find("Foot.R");
            _footL = Find("Toe.L");
            _footR = Find("Toe.R");
            _fingersL = NamedFingers(_handL);
            _fingersR = NamedFingers(_handR);
        }

        private static Finger[] NamedFingers(Transform hand)
        {
            if (hand == null) return Array.Empty<Finger>();
            var list = new System.Collections.Generic.List<Finger>();
            foreach (var (key, thumb) in new[] { ("thumb", true), ("Index", false), ("Middle", false), ("Pinky", false) })
            {
                Transform j1 = FindContaining(hand, key + "1");
                Transform j2 = FindContaining(hand, key + "2");
                var joints = Array.FindAll(new[] { j1, j2 }, t => t != null);
                if (joints.Length == 0) continue;
                var axes = new Vector3[joints.Length];
                // The old rig closes its fingers around local -X.
                for (int i = 0; i < axes.Length; i++) axes[i] = thumb ? new Vector3(-1f, 0f, 0f) : Vector3.left;
                list.Add(new Finger
                {
                    Name = key, IsThumb = thumb, Joints = joints, CurlAxes = axes,
                    RestRotations = Array.ConvertAll(joints, j => j.localRotation),
                });
            }
            return list.ToArray();
        }

        private void ResolveRenderers()
        {
            _bodyRenderer = null;
            _faceRenderer = null;
            Renderer largest = null;
            float largestSize = -1f;
            foreach (var r in _visual.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("face", StringComparison.OrdinalIgnoreCase)) { _faceRenderer ??= r; continue; }
                if (_bodyRenderer == null && r.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0) _bodyRenderer = r;
                float size = r.bounds.size.sqrMagnitude;
                if (size > largestSize) { largestSize = size; largest = r; }
            }
            _bodyRenderer ??= largest;
        }

        private Transform Find(string name) => FindExact(_visual, name) ?? FindContaining(_visual, name);

        private static Transform FindExact(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindExact(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindContaining(Transform parent, string keyword)
        {
            if (parent == null) return null;
            if (parent.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindContaining(child, keyword);
                if (found != null) return found;
            }
            return null;
        }
    }
}
