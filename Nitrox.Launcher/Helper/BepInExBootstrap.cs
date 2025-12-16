using System.Diagnostics;
using Nitrox.Model.Platforms.Store;

namespace Nitrox.Launcher.Helper;

public static class BepInExEnvironment
{
    public static bool TryCreateStartInfo(
        string gamePath,
        string exePath,
        string arguments,
        out ProcessStartInfo startInfo)
    {
        startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = gamePath,
            UseShellExecute = false
        };

        BepInExEnvironment.Apply(startInfo, exePath);

        return startInfo.EnvironmentVariables.Count > 0;
    }
}
