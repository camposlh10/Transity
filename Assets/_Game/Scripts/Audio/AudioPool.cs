using System.Collections.Generic;
using UnityEngine;

namespace Transity.Audio
{
    /// <summary>
    /// Fire-and-forget playback of <see cref="ProceduralAudio"/> sounds, 2D or at a point
    /// in the world, from a small reusable pool of AudioSources. Exists so gameplay code
    /// never creates an AudioSource of its own.
    /// </summary>
    public static class AudioPool
    {
        const int PoolSize = 24;

        static readonly List<AudioSource> Sources = new();
        static GameObject s_Root;

        static void EnsurePool()
        {
            if (s_Root != null)
            {
                return;
            }

            s_Root = new GameObject("~AudioPool");
            Object.DontDestroyOnLoad(s_Root);

            for (var i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"Voice_{i}");
                go.transform.SetParent(s_Root.transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.dopplerLevel = 0f;
                source.minDistance = 1.5f;
                Sources.Add(source);
            }
        }

        static AudioSource Next()
        {
            EnsurePool();

            foreach (var source in Sources)
            {
                if (!source.isPlaying)
                {
                    return source;
                }
            }

            // All busy: steal the one that started earliest. Quiet ambience losing a voice
            // to a gunshot is the right trade.
            var oldest = Sources[0];
            var oldestTime = float.MaxValue;
            foreach (var source in Sources)
            {
                if (source.time < oldestTime)
                {
                    oldestTime = source.time;
                    oldest = source;
                }
            }

            oldest.Stop();
            return oldest;
        }

        /// <summary>Plays in the world.</summary>
        public static AudioSource PlayAt(SoundKind kind, Vector3 position, float volume = 1f,
            float pitch = 1f, float maxDistance = 40f)
        {
            var source = Next();
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.maxDistance = maxDistance;
            source.pitch = pitch;
            source.volume = volume;
            source.clip = ProceduralAudio.Get(kind);
            source.Play();
            return source;
        }

        /// <summary>Plays flat, for UI and the local player's own body.</summary>
        public static AudioSource Play2D(SoundKind kind, float volume = 1f, float pitch = 1f)
        {
            var source = Next();
            source.spatialBlend = 0f;
            source.pitch = pitch;
            source.volume = volume;
            source.clip = ProceduralAudio.Get(kind);
            source.Play();
            return source;
        }

        /// <summary>A little pitch variation so repeated sounds do not machine-gun.</summary>
        public static float Vary(float amount = 0.08f) => 1f + Random.Range(-amount, amount);
    }
}
