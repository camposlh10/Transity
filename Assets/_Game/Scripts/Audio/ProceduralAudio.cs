using System;
using System.Collections.Generic;
using UnityEngine;

namespace Transity.Audio
{
    /// <summary>Every sound the game makes, by name rather than by clip.</summary>
    public enum SoundKind : byte
    {
        GunshotLight = 0,
        GunshotHeavy = 1,
        GunshotShotgun = 2,
        CrossbowTwang = 3,
        TranqPuff = 4,
        MeleeSwing = 5,
        MeleeHit = 6,
        Reload = 7,
        DryFire = 8,
        Footstep = 9,
        FootstepSprint = 10,
        Heartbeat = 11,
        GrowlLow = 12,
        Screech = 13,
        Bark = 14,
        Whisper = 15,
        HitMarker = 16,
        WeakPointMarker = 17,
        Alarm = 18,
        Beep = 19,
        Click = 20,
        Hurt = 21,
        Death = 22,
        TrapSnap = 23,
        Lunge = 24,
        Breath = 25,
        CreatureStep = 26,
        Collapse = 27,
        Contain = 28,
        Chime = 29,
        Rustle = 30
    }

    /// <summary>
    /// Synthesises every sound at runtime from noise and oscillators, because the project
    /// has no audio assets yet and a creature that makes no sound is not frightening.
    ///
    /// Each generator is a few lines of DSP: an envelope, a source, a filter. The results
    /// are deliberately abstract rather than imitative -- a heavy thump reads as a rifle,
    /// a filtered sweep reads as a screech -- and every one is replaceable by a recorded
    /// clip through <see cref="Override"/> without touching a caller.
    /// </summary>
    public static class ProceduralAudio
    {
        const int SampleRate = 44100;

        static readonly Dictionary<SoundKind, AudioClip> Cache = new();
        static readonly Dictionary<SoundKind, AudioClip> Overrides = new();

        /// <summary>Swap a synthesised sound for a real recording.</summary>
        public static void Override(SoundKind kind, AudioClip clip) => Overrides[kind] = clip;

        public static AudioClip Get(SoundKind kind)
        {
            if (Overrides.TryGetValue(kind, out var overridden) && overridden != null)
            {
                return overridden;
            }

            if (Cache.TryGetValue(kind, out var cached) && cached != null)
            {
                return cached;
            }

            var samples = Generate(kind);
            var clip = AudioClip.Create(kind.ToString(), samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Cache[kind] = clip;
            return clip;
        }

        // ---------------------------------------------------------------- generators

        static float[] Generate(SoundKind kind)
        {
            switch (kind)
            {
                case SoundKind.GunshotLight: return Gunshot(0.22f, 0.55f, 1400f, 0.9f);
                case SoundKind.GunshotHeavy: return Gunshot(0.42f, 0.9f, 700f, 1.15f);
                case SoundKind.GunshotShotgun: return Gunshot(0.36f, 1.0f, 900f, 1.3f);
                case SoundKind.CrossbowTwang: return Twang();
                case SoundKind.TranqPuff: return Puff();
                case SoundKind.MeleeSwing: return Swing();
                case SoundKind.MeleeHit: return Thud(0.16f, 0.9f, 240f);
                case SoundKind.Reload: return Clicks(new[] { 0f, 0.28f, 0.55f }, 0.8f);
                case SoundKind.DryFire: return Clicks(new[] { 0f }, 0.5f);
                case SoundKind.Footstep: return Step(0.09f, 0.35f, 500f);
                case SoundKind.FootstepSprint: return Step(0.11f, 0.55f, 380f);
                case SoundKind.CreatureStep: return Step(0.14f, 0.6f, 160f);
                case SoundKind.Heartbeat: return Heartbeat();
                case SoundKind.GrowlLow: return Growl();
                case SoundKind.Screech: return Screech();
                case SoundKind.Bark: return Bark();
                case SoundKind.Whisper: return Whisper();
                case SoundKind.HitMarker: return Tone(0.06f, 1800f, 0.35f, 0.002f);
                case SoundKind.WeakPointMarker: return TwoTone(0.12f, 1800f, 2600f, 0.4f);
                case SoundKind.Alarm: return Alarm();
                case SoundKind.Beep: return Tone(0.09f, 1250f, 0.4f, 0.004f);
                case SoundKind.Click: return Clicks(new[] { 0f }, 0.35f);
                case SoundKind.Hurt: return Hurt();
                case SoundKind.Death: return Thud(0.6f, 1f, 90f);
                case SoundKind.TrapSnap: return Snap();
                case SoundKind.Lunge: return Lunge();
                case SoundKind.Breath: return Breath();
                case SoundKind.Collapse: return Thud(0.5f, 0.8f, 120f);
                case SoundKind.Contain: return TwoTone(0.4f, 520f, 780f, 0.5f);
                case SoundKind.Chime: return Chime();
                case SoundKind.Rustle: return Rustle();
                default: return Tone(0.1f, 440f, 0.3f, 0.005f);
            }
        }

        // ---------------------------------------------------------------- building blocks

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

        static float Rand(ref uint state)
        {
            // xorshift: deterministic across peers and runs, no UnityEngine.Random in a static.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 8388607.5f - 1f;
        }

        /// <summary>Exponential decay envelope with a short linear attack.</summary>
        static float Env(int i, int length, float attackSeconds, float decayRate)
        {
            var t = i / (float)SampleRate;
            var attack = attackSeconds > 0f ? Mathf.Clamp01(t / attackSeconds) : 1f;
            return attack * Mathf.Exp(-t * decayRate) * (1f - i / (float)length * 0.15f);
        }

        /// <summary>One-pole low-pass, in place.</summary>
        static void LowPass(float[] samples, float cutoffHz)
        {
            var rc = 1f / (2f * Mathf.PI * cutoffHz);
            var dt = 1f / SampleRate;
            var alpha = dt / (rc + dt);
            var last = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                last += alpha * (samples[i] - last);
                samples[i] = last;
            }
        }

        static void HighPass(float[] samples, float cutoffHz)
        {
            var rc = 1f / (2f * Mathf.PI * cutoffHz);
            var dt = 1f / SampleRate;
            var alpha = rc / (rc + dt);
            var lastIn = 0f;
            var lastOut = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var input = samples[i];
                lastOut = alpha * (lastOut + input - lastIn);
                lastIn = input;
                samples[i] = lastOut;
            }
        }

        static void Normalise(float[] samples, float peak)
        {
            var max = 0.0001f;
            foreach (var s in samples)
            {
                max = Mathf.Max(max, Mathf.Abs(s));
            }

            var gain = peak / max;
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }
        }

        static void SoftClip(float[] samples, float drive)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (float)Math.Tanh(samples[i] * drive);
            }
        }

        // ---------------------------------------------------------------- sounds

        static float[] Gunshot(float seconds, float bodyWeight, float crackCutoff, float peak)
        {
            var s = Buffer(seconds);
            uint seed = 0x9E3779B9;

            // Crack: bright noise burst. Body: low thump that gives it weight.
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var crack = Rand(ref seed) * Env(i, s.Length, 0.001f, 28f);
                var thump = Mathf.Sin(2f * Mathf.PI * (55f + 40f * Mathf.Exp(-t * 30f)) * t)
                            * Env(i, s.Length, 0.002f, 11f) * bodyWeight;
                s[i] = crack * 0.7f + thump;
            }

            LowPass(s, crackCutoff * 4f);
            SoftClip(s, 1.8f);
            Normalise(s, Mathf.Min(1f, peak));
            return s;
        }

        static float[] Twang()
        {
            var s = Buffer(0.3f);
            uint seed = 12345;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var pluck = Mathf.Sin(2f * Mathf.PI * (180f - 60f * t) * t) * Env(i, s.Length, 0.001f, 14f);
                var slap = Rand(ref seed) * Env(i, s.Length, 0.0005f, 60f) * 0.6f;
                s[i] = pluck + slap;
            }

            Normalise(s, 0.8f);
            return s;
        }

        static float[] Puff()
        {
            var s = Buffer(0.18f);
            uint seed = 777;
            for (var i = 0; i < s.Length; i++)
            {
                s[i] = Rand(ref seed) * Env(i, s.Length, 0.004f, 24f);
            }

            LowPass(s, 1800f);
            HighPass(s, 300f);
            Normalise(s, 0.55f);
            return s;
        }

        static float[] Swing()
        {
            var s = Buffer(0.28f);
            uint seed = 4242;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / s.Length;
                var window = Mathf.Sin(t * Mathf.PI);
                s[i] = Rand(ref seed) * window * window;
            }

            LowPass(s, 900f);
            HighPass(s, 200f);
            Normalise(s, 0.45f);
            return s;
        }

        static float[] Thud(float seconds, float peak, float baseHz)
        {
            var s = Buffer(seconds);
            uint seed = 99;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var pitch = baseHz * (0.5f + 0.5f * Mathf.Exp(-t * 20f));
                s[i] = Mathf.Sin(2f * Mathf.PI * pitch * t) * Env(i, s.Length, 0.002f, 9f)
                       + Rand(ref seed) * Env(i, s.Length, 0.001f, 40f) * 0.3f;
            }

            LowPass(s, 500f);
            Normalise(s, peak);
            return s;
        }

        static float[] Clicks(float[] offsets, float peak)
        {
            var s = Buffer(offsets[offsets.Length - 1] + 0.08f);
            uint seed = 31337;
            foreach (var offset in offsets)
            {
                var start = Mathf.RoundToInt(offset * SampleRate);
                for (var i = 0; i < 1200 && start + i < s.Length; i++)
                {
                    s[start + i] += Rand(ref seed) * Mathf.Exp(-i / 140f);
                }
            }

            HighPass(s, 1200f);
            Normalise(s, peak);
            return s;
        }

        static float[] Step(float seconds, float peak, float cutoff)
        {
            var s = Buffer(seconds);
            uint seed = 2024;
            for (var i = 0; i < s.Length; i++)
            {
                s[i] = Rand(ref seed) * Env(i, s.Length, 0.003f, 40f);
            }

            LowPass(s, cutoff);
            Normalise(s, peak);
            return s;
        }

        static float[] Heartbeat()
        {
            // Lub-dub: two low thumps, the second softer, in one 0.9 s cycle.
            var s = Buffer(0.9f);
            void Thump(int start, float gain)
            {
                for (var i = 0; i < SampleRate * 0.16f && start + i < s.Length; i++)
                {
                    var t = i / (float)SampleRate;
                    s[start + i] += Mathf.Sin(2f * Mathf.PI * 48f * t) * Mathf.Exp(-t * 22f) * gain;
                }
            }

            Thump(0, 1f);
            Thump(Mathf.RoundToInt(0.22f * SampleRate), 0.65f);
            LowPass(s, 160f);
            Normalise(s, 0.9f);
            return s;
        }

        static float[] Growl()
        {
            var s = Buffer(1.4f);
            uint seed = 555;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                // Pulsed noise at a throat-like rate, with slow pitch wander.
                var rate = 34f + 6f * Mathf.Sin(t * 3f);
                var pulse = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * rate * t));
                var window = Mathf.Sin(Mathf.Clamp01(t / 1.4f) * Mathf.PI);
                s[i] = Rand(ref seed) * pulse * pulse * window;
            }

            LowPass(s, 420f);
            SoftClip(s, 2.5f);
            Normalise(s, 0.85f);
            return s;
        }

        static float[] Screech()
        {
            var s = Buffer(0.9f);
            uint seed = 8080;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var sweep = 900f + 1400f * Mathf.Sin(t * 9f) + 600f * t;
                var vibrato = 1f + 0.03f * Mathf.Sin(t * 70f);
                var window = Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI);
                s[i] = (Mathf.Sin(2f * Mathf.PI * sweep * vibrato * t) * 0.7f + Rand(ref seed) * 0.3f) * window;
            }

            SoftClip(s, 2f);
            HighPass(s, 500f);
            Normalise(s, 0.8f);
            return s;
        }

        static float[] Bark()
        {
            var s = Buffer(0.22f);
            uint seed = 4321;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var pitch = 320f * (1f + 0.6f * Mathf.Exp(-t * 25f));
                s[i] = (Mathf.Sin(2f * Mathf.PI * pitch * t) * 0.6f + Rand(ref seed) * 0.4f) * Env(i, s.Length, 0.004f, 12f);
            }

            SoftClip(s, 3f);
            LowPass(s, 1400f);
            Normalise(s, 0.8f);
            return s;
        }

        static float[] Whisper()
        {
            var s = Buffer(2.4f);
            uint seed = 1010;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                // Syllable-like amplitude shape over breathy noise.
                var syllables = 0.5f + 0.5f * Mathf.Sin(t * 11f) * Mathf.Sin(t * 4.3f + 1f);
                var window = Mathf.Sin(Mathf.Clamp01(t / 2.4f) * Mathf.PI);
                s[i] = Rand(ref seed) * syllables * window;
            }

            HighPass(s, 900f);
            LowPass(s, 4200f);
            Normalise(s, 0.5f);
            return s;
        }

        static float[] Tone(float seconds, float hz, float peak, float attack)
        {
            var s = Buffer(seconds);
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                s[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Env(i, s.Length, attack, 18f);
            }

            Normalise(s, peak);
            return s;
        }

        static float[] TwoTone(float seconds, float hzA, float hzB, float peak)
        {
            var s = Buffer(seconds);
            var half = s.Length / 2;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var hz = i < half ? hzA : hzB;
                var local = i < half ? i : i - half;
                s[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Env(local, half, 0.003f, 16f);
            }

            Normalise(s, peak);
            return s;
        }

        static float[] Alarm()
        {
            var s = Buffer(0.6f);
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var hz = 1500f + 500f * Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 6f));
                s[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * 0.8f;
            }

            Normalise(s, 0.6f);
            return s;
        }

        static float[] Hurt()
        {
            var s = Buffer(0.3f);
            uint seed = 6060;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var pitch = 220f * (1f + 0.4f * Mathf.Exp(-t * 10f));
                s[i] = (Mathf.Sin(2f * Mathf.PI * pitch * t) + Rand(ref seed) * 0.5f) * Env(i, s.Length, 0.005f, 9f);
            }

            LowPass(s, 1200f);
            SoftClip(s, 2f);
            Normalise(s, 0.6f);
            return s;
        }

        static float[] Snap()
        {
            var s = Buffer(0.25f);
            uint seed = 909;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                s[i] = Rand(ref seed) * Env(i, s.Length, 0.0005f, 45f)
                       + Mathf.Sin(2f * Mathf.PI * 2400f * t) * Env(i, s.Length, 0.0005f, 70f) * 0.6f;
            }

            HighPass(s, 600f);
            Normalise(s, 0.95f);
            return s;
        }

        static float[] Lunge()
        {
            var s = Buffer(0.5f);
            uint seed = 1212;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var window = Mathf.Sin(Mathf.Clamp01(t / 0.5f) * Mathf.PI);
                var rise = 120f + 500f * t;
                s[i] = (Rand(ref seed) * 0.6f + Mathf.Sin(2f * Mathf.PI * rise * t) * 0.4f) * window;
            }

            LowPass(s, 1500f);
            Normalise(s, 0.85f);
            return s;
        }

        static float[] Breath()
        {
            var s = Buffer(1.6f);
            uint seed = 3131;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var window = Mathf.Sin(Mathf.Clamp01(t / 1.6f) * Mathf.PI);
                s[i] = Rand(ref seed) * window * window;
            }

            LowPass(s, 1100f);
            HighPass(s, 250f);
            Normalise(s, 0.4f);
            return s;
        }

        static float[] Chime()
        {
            var s = Buffer(1.2f);
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                s[i] = (Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1320f * t)
                        + 0.25f * Mathf.Sin(2f * Mathf.PI * 1760f * t)) * Env(i, s.Length, 0.004f, 3.5f);
            }

            Normalise(s, 0.5f);
            return s;
        }

        static float[] Rustle()
        {
            var s = Buffer(0.5f);
            uint seed = 7777;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)SampleRate;
                var crackle = Mathf.Abs(Rand(ref seed)) > 0.92f ? Rand(ref seed) : 0f;
                s[i] = (Rand(ref seed) * 0.3f + crackle) * Mathf.Sin(Mathf.Clamp01(t / 0.5f) * Mathf.PI);
            }

            HighPass(s, 1500f);
            Normalise(s, 0.45f);
            return s;
        }

        /// <summary>
        /// A seamless low drone for the tension bed: detuned sines that beat against each
        /// other. Looped by the music layer; intensity is done with volume, not content.
        /// </summary>
        public static AudioClip DroneLoop(float baseHz, float seconds, int seed)
        {
            var samples = Buffer(seconds);
            var n = samples.Length;

            // Frequencies chosen so an integer number of cycles fit the loop exactly.
            var cyclesA = Mathf.Max(1, Mathf.RoundToInt(baseHz * seconds));
            var cyclesB = cyclesA + 1;
            var cyclesC = cyclesA * 2 + 1;

            for (var i = 0; i < n; i++)
            {
                var phase = i / (float)n;
                samples[i] = Mathf.Sin(2f * Mathf.PI * cyclesA * phase)
                             + 0.6f * Mathf.Sin(2f * Mathf.PI * cyclesB * phase + seed)
                             + 0.25f * Mathf.Sin(2f * Mathf.PI * cyclesC * phase);
            }

            Normalise(samples, 0.6f);
            var clip = AudioClip.Create($"Drone_{baseHz}", n, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
