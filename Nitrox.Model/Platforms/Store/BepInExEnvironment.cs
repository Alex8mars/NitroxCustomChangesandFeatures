using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nitrox.Model.Platforms.Store
{
    public static class BepInExEnvironment
    {
        public static void Apply(ProcessStartInfo startInfo, string gameFilePath)
        {
            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            if (string.IsNullOrWhiteSpace(gameFilePath))
            {
                throw new ArgumentNullException(nameof(gameFilePath));
            }

            Dictionary<string, string> env =
                startInfo.EnvironmentVariables
                    .Cast<DictionaryEntry>()
                    .ToDictionary(e => (string)e.Key, e => (string)e.Value);

            Apply(env, gameFilePath);

            startInfo.EnvironmentVariables.Clear();
            foreach (var kv in env)
            {
                startInfo.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        public static void Apply(
            IDictionary<string, string> environment,
            string gameFilePath)
        {
            foreach (var pair in BuildVariables(gameFilePath, environment))
            {
                environment[pair.Item1] = pair.Item2;
            }
        }

        private static IEnumerable<(string, string)> BuildVariables(
            string gameFilePath,
            IDictionary<string, string> environment)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield break;
            }

            string? gameDir = Path.GetDirectoryName(gameFilePath);
            if (string.IsNullOrWhiteSpace(gameDir))
            {
                yield break;
            }

            string bepInExDir = Path.Combine(gameDir, "BepInEx");
            if (!Directory.Exists(bepInExDir))
            {
                yield break;
            }

            string ext = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";
            string doorstopPath = Path.Combine(gameDir, $"libdoorstop.{ext}");
            if (!File.Exists(doorstopPath))
            {
                yield break;
            }

            string preloader =
                Path.Combine(bepInExDir, "core", "BepInEx.Preloader.dll");
            if (!File.Exists(preloader))
            {
                yield break;
            }

            yield return ("DOORSTOP_ENABLED", "1");
            yield return ("DOORSTOP_TARGET_ASSEMBLY", preloader);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return ("DYLD_INSERT_LIBRARIES", doorstopPath);
            }
            else
            {
                yield return ("LD_PRELOAD", doorstopPath);
            }
        }
    }
}
