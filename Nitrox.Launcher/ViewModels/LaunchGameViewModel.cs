using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nitrox.Launcher.Models.Design;
using Nitrox.Launcher.Models.Services;
using Nitrox.Launcher.Models.Utils;
using Nitrox.Launcher.ViewModels.Abstract;
using Nitrox.Model.Core;
using Nitrox.Model.Helper;
using Nitrox.Model.Logger;
using Nitrox.Model.Platforms.Discovery.Models;
using Nitrox.Model.Platforms.OS.Shared;
using Nitrox.Model.Platforms.Store;

namespace Nitrox.Launcher.ViewModels;

internal partial class LaunchGameViewModel(
    DialogService dialogService,
    ServerService serverService,
    OptionsViewModel optionsViewModel,
    IKeyValueStore keyValueStore)
    : RoutableViewModelBase
{
    public static Task<string>? LastFindSubnauticaTask;
    private static bool hasInstantLaunched;

    private readonly DialogService dialogService = dialogService;
    private readonly ServerService serverService = serverService;
    private readonly IKeyValueStore keyValueStore = keyValueStore;

    [ObservableProperty]
    private Platform gamePlatform;

    [ObservableProperty]
    private string? platformToolTip;

    public Bitmap[] GalleryImageSources { get; } =
    [
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-1.png", s => new Bitmap(s)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-2.png", s => new Bitmap(s)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-3.png", s => new Bitmap(s)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-4.png", s => new Bitmap(s))
    ];

    public string Version => $"{NitroxEnvironment.ReleasePhase} {NitroxEnvironment.Version}";

    internal override async Task ViewContentLoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            GamePlatform = NitroxUser.GamePlatform?.Platform ?? Platform.NONE;
            PlatformToolTip = GamePlatform.GetAttribute<DescriptionAttribute>()?.Description ?? "";
            HandleInstantLaunchForDevelopment();
        }, cancellationToken);
    }

    internal override Task ViewContentUnloadAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task StartSingleplayerAsync()
    {
        if (GameInspect.WarnIfGameProcessExists(GameInfo.Subnautica) &&
            !keyValueStore.GetIsMultipleGameInstancesAllowed())
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(NitroxUser.GamePath) ||
                !Directory.Exists(NitroxUser.GamePath))
            {
                ChangeView(optionsViewModel);
                LauncherNotifier.Warning("Location of Subnautica is unknown.");
                return;
            }

            NitroxEntryPatch.Remove(NitroxUser.GamePath);
            await StartSubnauticaAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while starting singleplayer");
            await dialogService.ShowErrorAsync(ex, "Error while starting singleplayer");
        }
    }

    [RelayCommand]
    private async Task StartMultiplayerAsync(string[]? args = null)
    {
        try
        {
            bool setupResult = await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(NitroxUser.GamePath) ||
                    !Directory.Exists(NitroxUser.GamePath))
                {
                    ChangeView(optionsViewModel);
                    LauncherNotifier.Warning("Location of Subnautica is unknown.");
                    return false;
                }

                if (PirateDetection.HasTriggered)
                {
                    LauncherNotifier.Error("Pirated copy detected.");
                    return false;
                }

                if (GameInspect.WarnIfGameProcessExists(GameInfo.Subnautica) &&
                    !keyValueStore.GetIsMultipleGameInstancesAllowed())
                {
                    return false;
                }

                if (await GameInspect.IsOutdatedGameAndNotify(
                        NitroxUser.GamePath, dialogService))
                {
                    return false;
                }

                if (LastFindSubnauticaTask != null)
                {
                    await LastFindSubnauticaTask;
                }

                await NitroxEntryPatch.Apply(NitroxUser.GamePath);
                GameInspect.WarnIfBepInExMods(NitroxUser.GamePath);

                return true;
            });

            if (!setupResult)
            {
                return;
            }

            await StartSubnauticaAsync(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while starting multiplayer");
            await dialogService.ShowErrorAsync(ex, "Error while starting multiplayer");
        }
    }

    private async Task StartSubnauticaAsync(string[]? args = null)
        => await StartGameAsync(GameInfo.Subnautica, args);

    private async Task StartGameAsync(GameInfo gameInfo, string[]? args)
    {
        string exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "MacOS" : string.Empty;
        string exePath = Path.Combine(NitroxUser.GamePath, exeSuffix, gameInfo.ExeName);

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Unable to find {gameInfo.ExeName}");
        }

        string launchArgs =
            $"{keyValueStore.GetLaunchArguments(gameInfo)} " +
            $"{string.Join(" ", args ?? NitroxEnvironment.CommandLineArgs)}";

        // 🔧 FIX #1: BepInEx bootstrap replacement
        if (BepInExBootstrap.TryCreateStartInfo(
                NitroxUser.GamePath,
                exePath,
                launchArgs,
                out ProcessStartInfo bepinexStartInfo))
        {
            ProcessEx bepinexGame = ProcessEx.From(bepinexStartInfo);
            if (bepinexGame is null)
            {
                throw new Exception("Failed to start game via BepInEx bootstrapper");
            }
            return;
        }

        // 🔧 FIX #2: No variable shadowing
        ProcessEx game = NitroxUser.GamePlatform switch
        {
            Steam => await Steam.StartGameAsync(
                exePath, launchArgs, gameInfo.SteamAppId,
                ShouldSkipSteam(launchArgs),
                keyValueStore.GetUseBigPictureMode()),

            EpicGames => await EpicGames.StartGameAsync(exePath, launchArgs),
            HeroicGames => await HeroicGames.StartGameAsync(gameInfo.EgsNamespace, launchArgs),
            MSStore => await MSStore.StartGameAsync(exePath, launchArgs),
            Discord => await Discord.StartGameAsync(exePath, launchArgs),

            _ => throw new Exception("Unsupported game platform")
        };

        if (game is null)
        {
            throw new Exception("Game failed to start");
        }
    }

    private bool ShouldSkipSteam(string args)
    {
        if (GameInspect.HasBepInExInstallation(NitroxUser.GamePath))
        {
            return true;
        }

        if (keyValueStore.GetUseBigPictureMode())
        {
            return false;
        }

        if (App.InstantLaunch is { PlayerNames.Length: > 1 })
        {
            return false;
        }

        if (args.Contains("-vrmode", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    [Conditional("DEBUG")]
    private void HandleInstantLaunchForDevelopment()
    {
        if (hasInstantLaunched || App.InstantLaunch == null)
        {
            return;
        }

        hasInstantLaunched = true;

        Task.Run(async () =>
        {
            ServerEntry? server =
                await serverService.GetOrCreateServerAsync(App.InstantLaunch.SaveName);

            if (server == null)
            {
                throw new Exception("Failed to create server");
            }

            await Dispatcher.UIThread.InvokeAsync(
                () => serverService.StartServerAsync(server));

            foreach (string player in App.InstantLaunch.PlayerNames)
            {
                await StartMultiplayerAsync(["--instantlaunch", player]);
            }
        });
    }
}
