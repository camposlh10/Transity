using System.Diagnostics;
using UnityEngine;

namespace Transity.Core
{
    /// <summary>
    /// Thin logging front-end. Info/Net calls are compiled out of release players so
    /// per-frame networking chatter costs nothing in a shipped build.
    /// </summary>
    public static class GameLog
    {
        const string Prefix = "<color=#7fd1b9>[Transity]</color> ";

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Net(string message) => UnityEngine.Debug.Log(Prefix + "<color=#9db8ff>[net]</color> " + message);

        public static void Warn(string message) => UnityEngine.Debug.LogWarning(Prefix + message);

        public static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);
    }
}
