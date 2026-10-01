using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>Shared presentation cues; audio assets are cached and loaded only on first use.</summary>
    public static class GameplayFeedback
    {
        public enum Cue { Lift, Impact, Delivered, Portal, Broken, Throw, Step, Click }
        private static readonly string[,] Paths = {
            { "Audio/SFX/Grab", "Audio/SFX/Grab2" },
            { "Audio/SFX/HeavyDrop", null },
            { "Audio/Delivered", null },
            { "Audio/Portal", null },
            { "Audio/Broken", null },
            { "Audio/SFX/Throw", "Audio/SFX/Throw2" },
            { "Audio/SFX/Walk", "Audio/SFX/Walk2" },
            { "Audio/SFX/Click", "Audio/SFX/Click2" }
        };
        private static readonly AudioClip[,] Clips = new AudioClip[8, 2];
        private static readonly bool[] Loaded = new bool[8];
        private static readonly int[] Variants = new int[8];
        private static readonly AudioSource[] Sources = new AudioSource[16];
        private static int _nextSource;
        private static float _nextClickAt;
        private static Material _dustMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            System.Array.Clear(Clips, 0, Clips.Length);
            System.Array.Clear(Loaded, 0, Loaded.Length);
            System.Array.Clear(Variants, 0, Variants.Length);
            System.Array.Clear(Sources, 0, Sources.Length);
            _nextSource = 0;
            _nextClickAt = 0f;
            _dustMaterial = null;
        }

        public static void Play(Cue cue, Vector3 position)
        {
            int index = (int)cue;
            if (index < 0 || index >= Loaded.Length) return;
            if (cue == Cue.Click)
            {
                if (Time.unscaledTime < _nextClickAt) return;
                _nextClickAt = Time.unscaledTime + .05f;
            }
            if (!Loaded[index])
            {
                for (int variant = 0; variant < 2; variant++)
                    if (Paths[index, variant] != null) Clips[index, variant] = Resources.Load<AudioClip>(Paths[index, variant]);
                Loaded[index] = true;
            }
            int choice = Variants[index]++ & 1;
            AudioClip clip = Clips[index, choice] != null ? Clips[index, choice] : Clips[index, 0];
            if (clip == null) return;
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
            audio.volume = cue == Cue.Step ? .25f : cue == Cue.Click ? .45f : cue == Cue.Impact ? .4f : .65f;
            audio.spatialBlend = cue == Cue.Delivered || cue == Cue.Portal || cue == Cue.Click ? 0f : 1f;
            audio.minDistance = cue == Cue.Step ? 1.5f : 3f;
            audio.maxDistance = cue == Cue.Step ? 18f : 30f;
            audio.Play();
        }

        public static void Burst(Vector3 point, Color color, int count = 16)
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
            main.startSpeed = 1.8f;
            main.startSize = .14f;
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
    }
}
