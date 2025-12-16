using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nitrox.Model.Platforms.Store;

public static class BepInExEnvironment
{
    // Needed by launcher: BepInExEnvironment.Apply(startInfo, exePath)
    public static void Apply(ProcessStartInfo startInfo, string gameFilePath)
    {
        if (startInfo == null)
            throw new ArgumentNullException(nameof(startInfo));

        // EnvironmentVariables is a StringDictionary
        var envDict = startInfo.EnvironmentVariables
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value);

        Apply(envDict, gameFilePath);

        startInfo.EnvironmentVariables.Clear();
        foreach (var kv in envDict)
        {
            startInfo.EnvironmentVariables[kv.Key] = kv.Value;
        }
    }

    // Existing API (keep)
    public static void Apply(IDictionary<string, string> environment, string gameFilePath)
    {
        if (environment == null)
            throw new ArgumentNullException(nameof(environment));

        foreach (var pair in BuildVariables(gameFilePath, environment))
        {
            environment[pair.Item1] = pair.Item2;
        }
    }

    // Your other call sites need this too
    public static IEnumerable<(string, string)> MergeWith(
        IEnumerable<(string, string)>? environment,
        string gameFilePath)
    {
        var merged = environment != null
            ? environment.ToDictionary(p => p.Item1, p => p.Item2)
            : new Dictionary<string, string>();

        Apply(merged, gameFilePath);

        foreach (var kvp in merged)
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    private static IEnumerable<(string, string)> BuildVariables(
        string gameFilePath,
        IDictionary<string, string> environment)
    {
        // Windows uses doorstop.dll autoload; no env vars needed
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Enumerable.Empty<(string, string)>();

        string? gameDir = Path.GetDirectoryName(gameFilePath);
        if (string.IsNullOrWhiteSpace(gameDir))
            return Enumerable.Empty<(string, string)>();

        string bepInExDir = Path.Combine(gameDir, "BepInEx");
        if (!Directory.Exists(bepInExDir))
            return Enumerable.Empty<(string, string)>();

        string libExt = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";
        string doorstopPath = Path.Combine(gameDir, $"libdoorstop.{libExt}");
        if (!File.Exists(doorstopPath))
            return Enumerable.Empty<(string, string)>();

        string targetAssembly = Path.Combine(bepInExDir, "core", "BepInEx.Preloader.dll");
        if (!File.Exists(targetAssembly))
            return Enumerable.Empty<(string, string)>();

        var vars = new List<(string, string)>
        {
            ("DOORSTOP_ENABLED", "1"),
            ("DOORSTOP_TARGET_ASSEMBLY", targetAssembly),
            ("DOORSTOP_IGNORE_DISABLED_ENV", "0"),
            ("DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE", string.Empty),
            ("DOORSTOP_MONO_DEBUG_ENABLED", "0"),
            ("DOORSTOP_MONO_DEBUG_ADDRESS", "127.0.0.1:10000"),
            ("DOORSTOP_MONO_DEBUG_SUSPEND", "0"),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            vars.Add(("DYLD_LIBRARY_PATH", Prepend(gameDir, GetEnvironmentValue(environment, "DYLD_LIBRARY_PATH"))));
            vars.Add(("DYLD_INSERT_LIBRARIES", Prepend(doorstopPath, GetEnvironmentValue(environment, "DYLD_INSERT_LIBRARIES"))));
        }
        else
        {
            vars.Add(("LD_LIBRARY_PATH", Prepend(gameDir, GetEnvironmentValue(environment, "LD_LIBRARY_PATH"))));
            vars.Add(("LD_PRELOAD", Prepend(doorstopPath, GetEnvironmentValue(environment, "LD_PRELOAD"))));
        }

        return vars;
    }

    private static string GetEnvironmentValue(IDictionary<string, string> environment, string key)
    {
        if (environment.TryGetValue(key, out string? value))
            return value ?? string.Empty;

        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    private static string Prepend(string value, string existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return value;
        if (string.IsNullOrWhiteSpace(value))
            return existing;
        return $"{value}:{existing}";
    }
}
