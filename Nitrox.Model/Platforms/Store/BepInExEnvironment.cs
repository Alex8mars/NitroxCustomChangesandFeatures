using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Nitrox.Model.Platforms.Store;

public static class BepInExEnvironment
{
    // ✅ NEW OVERLOAD — THIS IS WHAT WAS MISSING
    public static void Apply(ProcessStartInfo startInfo, string gameFilePath)
    {
        if (startInfo == null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        // ProcessStartInfo.EnvironmentVariables is a StringDictionary
        var env = startInfo.EnvironmentVariables;

        // Convert to Dictionary<string, string> for logic reuse
        Dictionary<string, string> temp = env
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value);

        foreach ((string key, string value) in BuildVariables(gameFilePath, temp))
        {
            env[key] = value;
        }
    }

    // Existing API (keep)
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

    public static IEnumerable<(string, string)> MergeWith(
        IEnumerable<(string, string)>? environment,
        string gameFilePath)
    {
        Dictionary<string, string> merged =
            environment?.ToDictionary(p => p.Item1, p => p.Item2)
            ?? new Dictionary<string, string>();

        Apply(merged, gameFilePath);
        return merged.Select(kvp => (kvp.Key, kvp.Value));
    }

    private static IEnumerable<(string, string)> BuildVariables(
        string gameFilePath,
        IDictionary<string, string> environment)
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

        string libExt = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";
        string doorstopName = $"libdoorstop.{libExt}";
        string doorstopPath = Path.Combine(gameDir, doorstopName);

        if (!File.Exists(doorstopPath))
        {
            return Enumerable.Empty<(string, string)>();
        }

        string targetAssembly =
            Path.Combine(bepInExDir, "core", "BepInEx.Preloader.dll");

        if (!File.Exists(targetAssembly))
        {
            return Enumerable.Empty<(string, string)>();
        }

        List<(string, string)> vars =
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
            vars.Add(("DYLD_LIBRARY_PATH",
                Prepend(gameDir, GetEnvironmentValue(environment, "DYLD_LIBRARY_PATH"))));
            vars.Add(("DYLD_INSERT_LIBRARIES",
                Prepend(doorstopPath, GetEnvironmentValue(environment, "DYLD_INSERT_LIBRARIES"))));
        }
        else
        {
            vars.Add(("LD_LIBRARY_PATH",
                Prepend(gameDir, GetEnvironmentValue(environment, "LD_LIBRARY_PATH"))));
            vars.Add(("LD_PRELOAD",
                Prepend(doorstopPath, GetEnvironmentValue(environment, "LD_PRELOAD"))));
        }

        return vars;
    }

    private static string GetEnvironmentValue(
        IDictionary<string, string> environment,
        string key)
    {
        return environment.TryGetValue(key, out string? value)
            ? value
            : Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    private static string Prepend(string value, string? existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return existing;
        }

        return $"{value}:{existing}";
    }
}
