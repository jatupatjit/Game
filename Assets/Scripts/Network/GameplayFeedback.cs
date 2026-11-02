using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Shared presentation cues; audio assets are cached and loaded only on first use. A cue whose own clip is not in
    /// the project yet (drop one in at its path) uses a stand-in: another cue's clip, or for Checkpoint a short
    /// generated chime.
    /// </summary>
    public static class GameplayFeedback
    {
        public enum Cue { Lift, Impact, Delivered, Portal, Broken, Throw, Step, Click, Place, ClimbGrab, Knockdown, Checkpoint }
        private const int CueCount = 12;
        private static readonly string[,] Paths = {
            { "Audio/SFX/Grab", "Audio/SFX/Grab2" },
            { "Audio/SFX/HeavyDrop", null },
            { "Audio/Delivered", null },
            { "Audio/Portal", null },
            { "Audio/Broken", null },
            { "Audio/SFX/Throw", "Audio/SFX/Throw2" },
            { "Audio/SFX/Walk", "Audio/SFX/Walk2" },
            { "Audio/SFX/Click", "Audio/SFX/Click2" },
            // Slots for dedicated clips (Assets/Resources/<path>.wav); until then the stand-in below plays.
            { "Audio/SFX/Place", null },
            { "Audio/SFX/ClimbGrab", null },
            { "Audio/SFX/Knockdown", null },
            { "Audio/SFX/Checkpoint", null }
        };
        // Stand-in cue for a missing clip (-1 = none; Checkpoint falls back to the generated chime).
        private static readonly int[] StandIn = { -1, -1, -1, -1, -1, -1, -1, -1, (int)Cue.Impact, (int)Cue.Lift, (int)Cue.Impact, -1 };
        // Per cue: volume, pitch, spatial (0 = heard the same everywhere), min/max distance.
        private static readonly float[] Volume = { .65f, .4f, .65f, .65f, .65f, .65f, .25f, .45f, .4f, .5f, .7f, .6f };
        private static readonly float[] Pitch = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, .85f, 1.25f, .75f, 1f };
        private static readonly bool[] Global = { false, false, true, true, false, false, false, true, false, false, false, true };
        private static readonly AudioClip[,] Clips = new AudioClip[CueCount, 2];
        private static readonly bool[] Loaded = new bool[CueCount];
        private static readonly int[] Variants = new int[CueCount];
        private static readonly AudioSource[] Sources = new AudioSource[16];
        private static int _nextSource;
        private static float _nextClickAt;
        private static Material _dustMaterial;
        private static AudioClip _chime;
        private static readonly int[] Played = new int[CueCount];

        /// <summary>For tests: how often each cue has played on this machine ("Lift=2 Impact=5 ...").</summary>
        public static string PlayCounts()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < CueCount; i++)
                if (Played[i] > 0) text.Append((Cue)i).Append('=').Append(Played[i]).Append(' ');
            return text.ToString().TrimEnd();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            System.Array.Clear(Clips, 0, Clips.Length);
            System.Array.Clear(Loaded, 0, Loaded.Length);
            System.Array.Clear(Variants, 0, Variants.Length);
            System.Array.Clear(Played, 0, Played.Length);
            System.Array.Clear(Sources, 0, Sources.Length);
            _nextSource = 0;
            _nextClickAt = 0f;
            _dustMaterial = null;
            _chime = null;
        }

        /// <summary>Plays a cue at a point. <paramref name="strength"/> (0..1) scales the volume, e.g. by impact force.</summary>
        public static void Play(Cue cue, Vector3 position, float strength = 1f)
        {
            int index = (int)cue;
            if (index < 0 || index >= CueCount) return;
            if (cue == Cue.Click)
            {
                if (Time.unscaledTime < _nextClickAt) return;
                _nextClickAt = Time.unscaledTime + .05f;
            }
            AudioClip clip = ClipFor(index);
            if (clip == null && StandIn[index] >= 0) clip = ClipFor(StandIn[index]);
            if (clip == null && cue == Cue.Checkpoint) clip = Chime();
            if (clip == null) return;
            Played[index]++;
            int slot = _nextSource;
            for (int i = 0; i < Sources.Length; i++)
            {
                slot = (_nextSource + i) % Sources.Length;
                if (Sources[slot] == null || !Sources[slot].isPlaying) break;
            }
            _nextSource = (slot + 1) % Sources.Length;
            AudioSource audio = Sources[slot];
            if (audio == null)
            {
                var go = new GameObject("Gameplay Audio " + slot);
                Object.DontDestroyOnLoad(go);
                audio = go.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.dopplerLevel = 0f;
                Sources[slot] = audio;
            }
            audio.Stop();
            audio.transform.position = position;
            audio.clip = clip;
            audio.volume = Volume[index] * Mathf.Lerp(.35f, 1f, Mathf.Clamp01(strength));
            audio.pitch = Pitch[index] * Random.Range(.96f, 1.04f);
            audio.spatialBlend = Global[index] ? 0f : 1f;
            audio.minDistance = cue == Cue.Step ? 1.5f : 3f;
            audio.maxDistance = cue == Cue.Step ? 18f : 30f;
            audio.Play();
        }

        private static AudioClip ClipFor(int index)
        {
            if (!Loaded[index])
            {
                for (int variant = 0; variant < 2; variant++)
                    if (Paths[index, variant] != null) Clips[index, variant] = Resources.Load<AudioClip>(Paths[index, variant]);
                Loaded[index] = true;
            }
            int choice = Variants[index]++ & 1;
            return Clips[index, choice] != null ? Clips[index, choice] : Clips[index, 0];
        }

        /// <summary>Two rising notes (placeholder for Audio/SFX/Checkpoint).</summary>
        private static AudioClip Chime()
        {
            if (_chime != null) return _chime;
            const int rate = 44100;
            int length = (int)(rate * .5f);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float frequency = t < .16f ? 784f : 1175f; // G5 then D6
                float local = t < .16f ? t : t - .16f;
                float envelope = Mathf.Exp(-local * 9f) * Mathf.Clamp01(local * 200f);
                data[i] = .35f * envelope * (Mathf.Sin(2f * Mathf.PI * frequency * t) + .3f * Mathf.Sin(4f * Mathf.PI * frequency * t));
            }
            _chime = AudioClip.Create("CheckpointChime (generated)", length, 1, rate, false);
            _chime.SetData(data, 0);
            return _chime;
        }

        public static void Burst(Vector3 point, Color color, int count = 16, float speed = 1.8f, float size = .14f)
        {
            if (_dustMaterial == null) _dustMaterial = Resources.Load<Material>("FeedbackDust");
            if (_dustMaterial == null) return;
            var go = new GameObject("Cargo_Dust");
            go.transform.position = point;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = .15f;
            main.startLifetime = .65f;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = .4f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = .25f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _dustMaterial;
            ps.Play();
            Object.Destroy(go, 2f);
        }

        /// <summary>Colourful upward burst for celebrations (checkpoint saved, delivery).</summary>
        public static void Celebrate(Vector3 point, Color color, int count = 40) => Burst(point, color, count, 4.5f, .18f);
    }
}
