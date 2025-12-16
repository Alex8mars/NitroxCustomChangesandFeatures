using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Nitrox.Model.Helper;

namespace Nitrox.Model.Platforms.Store;

internal static class BepInExEnvironment
{
    public static void Apply(ProcessStartInfo startInfo, string gameFilePath)
    {
        if (startInfo == null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        IDictionary<string, string> environmentSnapshot = ExtractEnvironment(startInfo);

        foreach ((string key, string value) in BuildVariables(gameFilePath, environmentSnapshot))
        {
            SetEnvironmentVariable(startInfo, key, value);
        }
    }

    public static void Apply(IDictionary<string, string> environment, string gameFilePath)
    {
        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        foreach ((string key, string value) in BuildVariables(gameFilePath, environment))
        {
            environment[key] = value;
        }
    }

    public static IEnumerable<(string, string)> MergeWith(IEnumerable<(string, string)>? environment, string gameFilePath)
    {
        Dictionary<string, string> merged = environment?.ToDictionary(pair => pair.Item1, pair => pair.Item2)
                                           ?? new Dictionary<string, string>();
        Apply(merged, gameFilePath);
        return merged.Select(kvp => (kvp.Key, kvp.Value));
    }

    private static IDictionary<string, string> ExtractEnvironment(ProcessStartInfo startInfo)
    {
#if NET472
        return startInfo.EnvironmentVariables
                         .Cast<DictionaryEntry>()
                         .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? string.Empty);
#else
        return startInfo.Environment;
#endif
    }

    private static void SetEnvironmentVariable(ProcessStartInfo startInfo, string key, string value)
    {
#if NET472
        startInfo.EnvironmentVariables[key] = value;
#else
        startInfo.Environment[key] = value;
#endif
    }

    private static IEnumerable<(string, string)> BuildVariables(string gameFilePath, IDictionary<string, string> environment)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Enumerable.Empty<(string, string)>();
        }

        string? gameDir = Path.GetDirectoryName(gameFilePath);
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return Enumerable.Empty<(string, string)>();
        }

        string bepInExDir = Path.Combine(gameDir, "BepInEx");
        if (!Directory.Exists(bepInExDir))
        {
            return Enumerable.Empty<(string, string)>();
        }

        string libExtension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";
        string doorstopName = $"libdoorstop.{libExtension}";
        string doorstopPath = Path.Combine(gameDir, doorstopName);
        if (!File.Exists(doorstopPath))
        {
            return Enumerable.Empty<(string, string)>();
        }

        string targetAssembly = Path.Combine(bepInExDir, "core", "BepInEx.Preloader.dll");
        if (!File.Exists(targetAssembly))
        {
            return Enumerable.Empty<(string, string)>();
        }

        List<(string, string)> variables =
        [
            ("DOORSTOP_ENABLED", "1"),
            ("DOORSTOP_TARGET_ASSEMBLY", targetAssembly),
            ("DOORSTOP_IGNORE_DISABLED_ENV", "0"),
            ("DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE", string.Empty),
            ("DOORSTOP_MONO_DEBUG_ENABLED", "0"),
            ("DOORSTOP_MONO_DEBUG_ADDRESS", "127.0.0.1:10000"),
            ("DOORSTOP_MONO_DEBUG_SUSPEND", "0"),
        ];

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            variables.Add(("DYLD_LIBRARY_PATH", Prepend(gameDir, GetEnvironmentValue(environment, "DYLD_LIBRARY_PATH"))));
            variables.Add(("DYLD_INSERT_LIBRARIES", Prepend(doorstopPath, GetEnvironmentValue(environment, "DYLD_INSERT_LIBRARIES"))));
        }
        else
        {
            variables.Add(("LD_LIBRARY_PATH", Prepend(gameDir, GetEnvironmentValue(environment, "LD_LIBRARY_PATH"))));
            variables.Add(("LD_PRELOAD", Prepend(doorstopPath, GetEnvironmentValue(environment, "LD_PRELOAD"))));
        }

        return variables;
    }

    private static string GetEnvironmentValue(IDictionary<string, string> environment, string key)
    {
        if (environment.TryGetValue(key, out string? value))
        {
            return value;
        }

        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    private static string Prepend(string value, string? existing)
    {
        StringBuilder builder = new();
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(value);
        }

        if (!string.IsNullOrWhiteSpace(existing))
        {
            if (builder.Length > 0)
            {
                builder.Append(':');
            }
            builder.Append(existing);
        }

        return builder.ToString();
    }
}
