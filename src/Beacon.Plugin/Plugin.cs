using Beacon.Services;
using Beacon.Shared.Beacons;
using Beacon.Shared.Profiles;
using Beacon.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace Beacon;

/// <summary>
/// Entry point. Builds every service by hand, in dependency order, and takes them all down again on
/// unload -- a plugin that leaks a socket or a texture survives an unload and breaks the next load.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/beacon";

    private readonly Configuration config;

    private readonly BeaconApi api;

    private readonly LocationService location;

    private readonly NotificationService notifications;

    private readonly ImageCache images;

    private readonly BeaconHubClient hub;

    private readonly AtlasService atlas;

    private readonly TravelService travel;

    private readonly ScreenshotService screenshots;

    private readonly DtrService dtr;

    private readonly ProfileService profiles;

    private readonly StagehandIpc stagehand;

    private readonly StageDressing stages;

    private readonly WindowSystem windows = new("Beacon");

    private readonly AtlasWindow atlasWindow;

    private readonly BeaconEditorWindow editorWindow;

    private readonly SettingsWindow settingsWindow;

    private readonly BeaconOverlay overlay;

    private readonly ChronicleWindow chronicleWindow;

    private readonly ProfileEditorWindow profileEditorWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Svc.Initialise(pluginInterface);

        config = Configuration.Load();

        api = new BeaconApi(config);
        location = new LocationService();
        notifications = new NotificationService(config, location);
        images = new ImageCache(api);
        hub = new BeaconHubClient(config);
        atlas = new AtlasService(config, api, location, hub, images, notifications);
        travel = new TravelService(config, location);
        screenshots = new ScreenshotService(config);
        dtr = new DtrService(config, atlas, travel);
        profiles = new ProfileService(config, api, location, atlas, notifications);
        stagehand = new StagehandIpc();
        stages = new StageDressing(config, api, stagehand, location);

        editorWindow = new BeaconEditorWindow(config, atlas, location, screenshots, images, notifications, stages);
        settingsWindow = new SettingsWindow(config, api, atlas, hub, travel, stages);
        atlasWindow = new AtlasWindow(
            config,
            atlas,
            travel,
            location,
            images,
            notifications,
            stages,
            editorWindow.Open,
            () => settingsWindow.IsOpen = true,
            OpenChronicle,
            OpenCharacterCard);

        profileEditorWindow = new ProfileEditorWindow(config, profiles, atlas, location, screenshots, images, notifications);
        chronicleWindow = new ChronicleWindow(
            config,
            profiles,
            atlas,
            travel,
            location,
            images,
            notifications,
            profileEditorWindow.Open);

        overlay = new BeaconOverlay(config, atlas, travel, location, () => atlasWindow.IsOpen);

        windows.AddWindow(atlasWindow);
        windows.AddWindow(editorWindow);
        windows.AddWindow(settingsWindow);
        windows.AddWindow(chronicleWindow);
        windows.AddWindow(profileEditorWindow);
        windows.AddWindow(overlay);

        // Restore the filters the player left the atlas on, so it opens where they left off.
        atlas.SetQuery(
            new BeaconQuery
            {
                DataCenter = config.LastDataCenterFilter,
                LitOnly = config.LastLitOnlyFilter,
            },
            immediate: false);

        Svc.Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Open the beacon atlas.\n"
                + "/beacon here - raise a beacon where you stand\n"
                + "/beacon light - light your nearest beacon\n"
                + "/beacon out - put out the beacon you lit\n"
                + "/beacon go <code> - open a beacon by its share code\n"
                + "/beacon currently <line> - say what your character is doing right now\n"
                + "/beacon ic | /beacon ooc - mark yourself in or out of character\n"
                + "/beacon settings - open the settings",
        });

        Svc.PluginInterface.UiBuilder.Draw += Draw;
        Svc.PluginInterface.UiBuilder.OpenMainUi += OpenAtlas;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Svc.Framework.Update += OnFrameworkUpdate;
        dtr.Clicked += OpenAtlas;

        dtr.Start();

        // Nothing to connect to until there is an account; the settings window handles that case.
        if (config.HasAccount)
        {
            atlas.Start();
            profiles.Start();
            stages.Start();
        }

        Svc.Log.Information("Beacon loaded.");
    }

    private void Draw()
    {
        // Recorded here because the main viewport's id can only be read inside a draw callback, and
        // a photograph can be triggered from a command or a background continuation.
        Beacon.UI.Viewport.Record();

        windows.Draw();

        // The file dialog lives outside the window system and must be drawn every frame, even when
        // the editor that opened it has since been closed.
        screenshots.Draw();
        stages.Draw();
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        try
        {
            atlas.Tick();
            profiles.Tick();
            stages.Tick(atlas.Beacons);
            travel.Tick();
            dtr.Tick();
        }
        catch (Exception ex)
        {
            // A throw here fires every frame and floods the log; log it once per occurrence and carry on.
            Svc.Log.Error(ex, "Beacon: framework update failed.");
        }
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();

        if (args.Length == 0)
        {
            OpenAtlas();
            return;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : string.Empty;

        switch (verb)
        {
            case "here" or "raise" or "new":
                editorWindow.Open(null);
                break;

            case "light":
                LightNearest();
                break;

            case "out" or "extinguish" or "snuff":
                ExtinguishMine();
                break;

            case "go" or "code":
                if (string.IsNullOrWhiteSpace(rest))
                    notifications.Error("Give a share code, like /beacon go WG9UHCSC");
                else
                    atlas.OpenShareCode(rest.Trim(), _ => OpenAtlas());

                break;

            case "me" or "card" or "profile":
                OpenMyCard();
                break;

            case "who" or "chronicle" or "souls":
                OpenChronicle();
                break;

            case "currently" or "doing":
                SetCurrently(rest);
                break;

            case "ic":
                SetStance(RpStance.InCharacter);
                break;

            case "ooc":
                SetStance(RpStance.OutOfCharacter);
                break;

            case "settings" or "config":
                OpenSettings();
                break;

            default:
                notifications.Error($"Beacon does not know \"{verb}\". Try /beacon on its own.");
                break;
        }
    }

    /// <summary>
    /// Rewrites the live line on the card of the character you are logged in as.
    ///
    /// A command rather than only a window, because the whole value of a "currently" is that changing
    /// it costs nothing. If it takes three clicks it is written once and never again, which is worse
    /// than not having the field.
    /// </summary>
    private void SetCurrently(string text)
    {
        if (profiles.MyProfile is not { } profile)
        {
            notifications.Error("Write a card for this character first, with /beacon me.");
            return;
        }

        var line = text.Trim();

        if (line.Length > ProfileLimits.CurrentlyMaxLength)
            line = line[..ProfileLimits.CurrentlyMaxLength];

        if (line.Length == 0)
        {
            profiles.SetCurrently(profile, null);
            notifications.Toast("Cleared what you are currently doing.", null);
            return;
        }

        profiles.SetCurrently(profile, line);
        notifications.Toast("Noted.", line);
    }

    private void SetStance(RpStance stance)
    {
        if (profiles.MyProfile is not { } profile)
        {
            notifications.Error("Write a card for this character first, with /beacon me.");
            return;
        }

        profiles.SetCurrently(profile, profile.Moment.Currently, stance);
        notifications.Toast(
            stance is RpStance.InCharacter ? "Marked in character." : "Marked out of character.",
            null);
    }

    /// <summary>
    /// Lights whichever of your beacons you are standing at.
    ///
    /// Exists because the whole point of a beacon is that you light it when you sit down to play, and
    /// making that a three-window job would mean nobody ever does it.
    /// </summary>
    private void LightNearest()
    {
        if (!Svc.InWorld)
        {
            notifications.Error("You need to be in the world.");
            return;
        }

        var candidates = atlas.MyBeacons
            .Concat(atlas.Beacons.Where(b => b.AllowPublicLighting))
            .DistinctBy(b => b.Id)
            .Where(b => !b.Flame.IsBurning)
            .Select(b => (Beacon: b, Distance: location.DistanceTo(b)))
            .Where(x => x.Distance is not null && x.Distance <= BeaconLimits.LightingRangeYalms)
            .OrderBy(x => x.Distance)
            .ToList();

        if (candidates.Count == 0)
        {
            notifications.Error("No beacon of yours within reach. Stand at one and try again.");
            return;
        }

        var target = candidates[0].Beacon;
        atlas.Select(target.Id);
        atlas.Light(target, config.DefaultLitMinutes, null);
    }

    private void ExtinguishMine()
    {
        var lit = atlas.MyBeacons
            .Concat(atlas.Beacons)
            .DistinctBy(b => b.Id)
            .FirstOrDefault(b => b.Flame.IsBurning && b.Flame.LitByAccountId == config.AccountId);

        if (lit is null)
        {
            notifications.Error("You have not lit anything.");
            return;
        }

        atlas.Extinguish(lit);
    }

    private void OpenAtlas()
    {
        atlasWindow.IsOpen = true;

        if (!config.HasAccount)
        {
            settingsWindow.IsOpen = true;
            return;
        }

        atlas.Refresh();
        atlas.RefreshMine();
    }

    private void OpenSettings() => settingsWindow.IsOpen = true;

    /// <summary>
    /// Opens your own card, or the editor when you have not written one yet.
    ///
    /// The whole point of /beacon me is that it does the right thing either way; making somebody
    /// find a "create" button first is the friction that leaves a directory empty.
    /// </summary>
    private void OpenMyCard()
    {
        if (!config.HasAccount)
        {
            settingsWindow.IsOpen = true;
            notifications.Error("Create a Beacon account first.");
            return;
        }

        profiles.RefreshMine();

        if (profiles.MyProfile is { } mine)
        {
            profiles.Select(mine.Id);
            chronicleWindow.IsOpen = true;
            return;
        }

        profileEditorWindow.Open(null);
    }

    /// <summary>
    /// Opens the card of whoever lit a beacon.
    ///
    /// This is the link that makes the two halves one plugin: a flame tells you roleplay is happening
    /// somewhere, and this tells you whether it is the kind you are looking for before you travel.
    /// </summary>
    private void OpenCharacterCard(string characterName, uint worldId)
    {
        if (string.IsNullOrWhiteSpace(characterName) || worldId == 0)
            return;

        chronicleWindow.IsOpen = true;
        profiles.OpenCharacter(characterName, worldId, _ => chronicleWindow.IsOpen = true);
    }

    private void OpenChronicle()
    {
        if (!config.HasAccount)
        {
            settingsWindow.IsOpen = true;
            return;
        }

        chronicleWindow.IsOpen = true;
        profiles.RefreshMine();
        profiles.Refresh();
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= OpenAtlas;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        dtr.Clicked -= OpenAtlas;

        Svc.Commands.RemoveHandler(Command);

        windows.RemoveAllWindows();

        dtr.Dispose();
        stages.Dispose();
        profiles.Dispose();
        profileEditorWindow.Dispose();
        screenshots.Dispose();
        travel.Dispose();
        atlas.Dispose();
        hub.Dispose();
        images.Dispose();
        api.Dispose();

        Svc.Log.Information("Beacon unloaded.");
    }
}
