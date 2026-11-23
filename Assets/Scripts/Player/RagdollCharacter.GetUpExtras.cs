using UnityEngine;
using Unity.Netcode;

namespace CoopGame.Player
{
    /// <summary>
    /// Extras for the fall-respawn get-up:
    /// 1. dust ring + thud when the dropped body hits the pad (and a small puff as the hands push off), 2. dizzy stars from the moment the body lands until the get-up ends, plus a short head shake once standing,
    /// 3. moving after the halfway point skips the rest of the get-up, 4. traps cannot hit you while you lie,
    /// get up, and for a moment after. Dust/thud and stars go to every screen through <see cref="GetUpFxRpc"/>;
    /// the head shake is part of the owner's pose, which every screen already receives.
    /// </summary>
    public sealed partial class RagdollCharacter
    {
        [Header("Get-up extras")]
        [Tooltip("Seconds of dizzy head shake after standing, before control comes back.")]
        [SerializeField, Min(0f)] private float _dizzySeconds = 1.1f;
        [Tooltip("Head shake size in degrees.")]
        [SerializeField, Min(0f)] private float _dizzyHeadDegrees = 22f;
        [Tooltip("Share of the get-up after which moving skips the rest.")]
        [SerializeField, Range(0f, 1f)] private float _getUpSkipAfter = .5f;
        [Tooltip("Seconds of trap immunity after the get-up ends.")]
        [SerializeField, Min(0f)] private float _trapImmuneAfterGetUp = 1f;

        [Tooltip("Size of the dust ring when the body lands after a fall respawn (1 = normal).")]
        [SerializeField, Min(0f)] private float _landingDustScale = 1f;

        private float _thudAt = -1f, _trapImmuneUntil = -1f, _thudSince, _thudMaxFall;
        private bool _dizzyFxSent, _pushDustSent;

        /// <summary>Owner: lying after a fall respawn, getting up, or just up. Traps leave this player alone.</summary>
        public bool TrapImmune => _getUpPending || _gettingUp || Time.time < _trapImmuneUntil;

        private float DizzySeconds => _dizzySeconds;

        /// <summary>Called from ApplyRespawnSlump: the body drops from the respawn height; thud when it lands.</summary>
        private void OnRespawnSlumpStarted()
        {
            // Fallback time (free fall + a little); normally the thud fires on the real touch-down below.
            _thudAt = Time.time + Mathf.Sqrt(2f * Mathf.Max(.05f, _respawnDropHeight) / 9.81f) + .3f;
            _thudSince = Time.time;
            _thudMaxFall = 0f;
            _dizzyFxSent = false;
            _pushDustSent = false;
        }

        /// <summary>Called every owner physics step from UpdateGetUp (before the pose).</summary>
        private void GetUpExtrasTick()
        {
            if (_thudAt <= 0f) return;
            // Touch-down: the hips were falling and have suddenly slowed (measured: ~3.5 m/s down to ~1.4 m/s in 2 steps).
            float fall = Mathf.Max(0f, -_bodies[Hips].linearVelocity.y);
            _thudMaxFall = Mathf.Max(_thudMaxFall, fall);
            bool landed = Time.time - _thudSince > .08f && _thudMaxFall > 1.2f && fall < _thudMaxFall * .5f;
            if (!landed && Time.time < _thudAt) return;
            _thudAt = -1f;
            float strength = Mathf.Clamp01(Mathf.Max(_thudMaxFall, 2f) / 4f);
            SendGetUpFx(0, GroundBelow(_bodies[Hips].position), strength);
            // Dizzy stars from the moment the body hits the ground, through the get-up, until it ends.
            _dizzyFxSent = true;
            SendGetUpFx(1, _bodies[Head].position, 1f);
        }

        /// <summary>Owner, while posing the get-up: head shake once standing, and the skip on move.
        /// <paramref name="poseTime"/> = seconds since the first key; <paramref name="keysTime"/> = length of the keys.
        /// Returns true when the get-up was ended early by moving.</summary>
        private bool GetUpExtrasPose(float poseTime, float keysTime)
        {
            Vector2 raw = _input != null ? _input.MoveInput : Vector2.zero;
            if (!Blocked && raw.sqrMagnitude > .04f && poseTime >= keysTime * _getUpSkipAfter)
            {
                FinishGetUp();
                // Soft hand-over from a half-up pose: strength ramps in instead of snapping upright.
                _activeStandUpSeconds = .4f;
                _standUpUntil = Time.time + _activeStandUpSeconds;
                return true;
            }

            // Small puff where the hands push off the ground (key 2, "push up on the hands").
            if (!_pushDustSent && poseTime >= _getUpStepSeconds * .85f)
            {
                _pushDustSent = true;
                SendGetUpFx(2, GroundBelow((_bodies[HandL].position + _bodies[HandR].position) * .5f), .5f);
            }

            float dizzyT = poseTime - keysTime;
            if (dizzyT < 0f || _dizzySeconds <= 0f) return false;
            float fade = Mathf.Clamp01(dizzyT / .2f) * Mathf.Clamp01((_dizzySeconds - dizzyT) / .3f);
            float wave = Mathf.Sin(dizzyT * Mathf.PI * 2f * 1.6f);
            float roll = Mathf.Sin(dizzyT * Mathf.PI * 2f * 1.1f + 1f);
            Vector3 up = Vector3.up, forward = _getUpFacing * Vector3.forward;
            Quaternion head = Quaternion.AngleAxis(wave * _dizzyHeadDegrees * fade, up) * Quaternion.AngleAxis(roll * _dizzyHeadDegrees * .5f * fade, forward);
            Quaternion chest = Quaternion.AngleAxis(roll * _dizzyHeadDegrees * .25f * fade, forward);
            _target[Chest] = chest * _target[Chest];
            _target[Head] = chest * head * _target[Head];
            return false;
        }

        /// <summary>Called from FinishGetUp.</summary>
        private void OnGetUpFinished()
        {
            _trapImmuneUntil = Time.time + _trapImmuneAfterGetUp;
            _thudAt = -1f;
            if (_dizzyFxSent) { _dizzyFxSent = false; SendGetUpFx(3, Vector3.zero, 0f); } // stars fade out
        }

        /// <summary>Ground point under <paramref name="from"/> (the player's own limbs ignored).</summary>
        private Vector3 GroundBelow(Vector3 from)
        {
            if (Physics.Raycast(from + Vector3.up * .3f, Vector3.down, out RaycastHit hit, 2.5f,
                    Physics.DefaultRaycastLayers & ~(1 << _limbLayer), QueryTriggerInteraction.Ignore))
                return hit.point;
            return from + Vector3.down * .25f;
        }

        private void SendGetUpFx(byte kind, Vector3 at, float strength)
        {
            if (IsSpawned) GetUpFxRpc(kind, at, strength);
            else PlayGetUpFx(kind, at, strength);
        }

        /// <summary>Every screen: 0 = dust ring + thud where the body lands, 1 = dizzy stars over this player's head
        /// (start), 2 = small dust puff where the hands push off, 3 = dizzy stars end (get-up finished or skipped).</summary>
        [Rpc(SendTo.Everyone)]
        private void GetUpFxRpc(byte kind, Vector3 at, float strength) => PlayGetUpFx(kind, at, strength);

        private void PlayGetUpFx(byte kind, Vector3 at, float strength)
        {
            if (kind == 0)
            {
                CoopGame.Network.GameplayFeedback.Play(CoopGame.Network.GameplayFeedback.Cue.BodyThud, at, strength);
                GroundDust.Ring(at, _landingDustScale * Mathf.Lerp(.75f, 1.15f, strength));
                CoopGame.Network.GameplayFeedback.Burst(at + Vector3.up * .05f, new Color(.62f, .52f, .40f, .9f), 10, 1.6f, .07f); // grit
                CoopGame.Network.CameraShake.Add(at, .35f * strength);
                return;
            }
            if (kind == 2)
            {
                GroundDust.Ring(at, _landingDustScale * .65f);
                return;
            }
            if (kind == 3)
            {
                DizzyStars.StopFor(transform);
                return;
            }
            // Safety limit only: the stars normally end with kind 3 when the get-up finishes.
            DizzyStars.Spawn(this, MaxStarsSeconds);
        }

        private const float MaxStarsSeconds = 12f;
    }

    /// <summary>A flat ring of soft dust that rolls out along the ground, swells and fades (drawn on every screen, no physics).</summary>
    public static class GroundDust
    {
        private static Material _material;

        public static void Ring(Vector3 ground, float scale = 1f)
        {
            if (scale <= 0f) return;
            // Soft round puff (transparent); the plain square feedback material only if it is missing.
            if (_material == null) _material = Resources.Load<Material>("FX/GetUpDust");
            if (_material == null) _material = Resources.Load<Material>("FeedbackDust");
            if (_material == null) return;
            var go = new GameObject("GetUp_Dust");
            go.transform.position = ground + Vector3.up * .06f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = .1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.9f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * scale, 3.4f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(.4f * scale, .7f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.80f, .71f, .57f, 1f), new Color(.70f, .62f, .50f, .9f));
            main.gravityModifier = -.04f; // drifts up a touch
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(Mathf.RoundToInt(26 * scale), 6, 40)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; // flat ring, radial along the ground
            shape.radius = .2f * scale;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = .25f;
            limit.dampen = .12f; // fast out, then it hangs in the air
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.8f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .08f), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            Object.Destroy(go, 2.5f);
        }
    }

    /// <summary>Three cartoon stars (5-point, Resources/FX/DizzyStar) circling above a player's head for a moment.
    /// Drawn on every screen, no physics; each star faces the camera, spins a little and leaves a short glowing tail.</summary>
    public sealed class DizzyStars : MonoBehaviour
    {
        private const float StarSize = .24f;
        private Transform _head;
        private Transform _follow;
        private float _until, _born;
        private readonly Transform[] _stars = new Transform[3];
        private readonly TrailRenderer[] _trails = new TrailRenderer[3];
        private static Mesh _quad;
        private static readonly System.Collections.Generic.List<DizzyStars> Active = new();

        /// <summary>Ends the stars over this player (they shrink away in 0.2 s).</summary>
        public static void StopFor(Transform player)
        {
            foreach (var stars in Active)
                if (stars != null && stars._follow == player) stars._until = Mathf.Min(stars._until, Time.time + .2f);
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        public static void Spawn(Component player, float seconds)
        {
            if (player == null || seconds <= 0f) return;
            StopFor(player.transform); // never two sets on one head
            var material = Resources.Load<Material>("FX/DizzyStar");
            if (material == null) return;
            var trailMaterial = Resources.Load<Material>("FX/StarTrail");
            if (_quad == null) _quad = BuildQuad();
            var go = new GameObject("DizzyStars");
            var stars = go.AddComponent<DizzyStars>();
            var animator = player.GetComponentInChildren<Animator>();
            stars._head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            stars._follow = player.transform;
            stars._born = Time.time;
            stars._until = Time.time + seconds;
            for (int i = 0; i < 3; i++)
            {
                var star = new GameObject("Star" + i);
                star.transform.SetParent(go.transform, false);
                star.AddComponent<MeshFilter>().sharedMesh = _quad;
                var renderer = star.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                stars._stars[i] = star.transform;
                if (trailMaterial != null) stars._trails[i] = AddTrail(star, trailMaterial);
            }
            stars.LateUpdate();
            // Start the tails at the stars' first spot (not from the world origin).
            foreach (var trail in stars._trails) if (trail != null) { trail.Clear(); trail.emitting = true; }
        }

        /// <summary>A short glowing tail behind a star: yellow, tapering and fading out.</summary>
        private static TrailRenderer AddTrail(GameObject star, Material material)
        {
            var trail = star.AddComponent<TrailRenderer>();
            trail.emitting = false;
            trail.sharedMaterial = material;
            trail.time = .45f;
            trail.minVertexDistance = .015f;
            trail.widthMultiplier = .14f;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var colors = new Gradient();
            colors.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .45f), 0f), new GradientColorKey(new Color(1f, .7f, .15f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.6f, .4f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = colors;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.View;
            return trail;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "DizzyStarQuad" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void LateUpdate()
        {
            if (Time.time >= _until || _follow == null) { Destroy(gameObject); return; }
            Vector3 center = _head != null ? _head.position + Vector3.up * .55f : _follow.position + Vector3.up * 1.9f;
            transform.position = center;
            Camera cam = Camera.main;
            Quaternion face = cam != null ? Quaternion.LookRotation(center - cam.transform.position, Vector3.up) : Quaternion.identity;
            // Pop in, shrink away at the end.
            float age = Time.time - _born, left = _until - Time.time;
            float scale = StarSize * Mathf.Clamp01(age / .15f) * Mathf.Clamp01(left / .2f);
            float a = Time.time * 4.5f;
            for (int i = 0; i < _stars.Length; i++)
            {
                float angle = a + i * Mathf.PI * 2f / 3f;
                _stars[i].position = center + new Vector3(Mathf.Cos(angle) * .32f, Mathf.Sin(angle * 2f) * .05f, Mathf.Sin(angle) * .32f);
                _stars[i].rotation = face * Quaternion.Euler(0f, 0f, Time.time * 160f + i * 40f);
                _stars[i].localScale = Vector3.one * scale;
                if (_trails[i] != null) _trails[i].widthMultiplier = .14f * scale / StarSize;
            }
        }
    }
}
