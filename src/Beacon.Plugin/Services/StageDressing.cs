using System.Text.Json;
using Beacon.Shared.Beacons;
using Dalamud.Interface.ImGuiFileDialog;

namespace Beacon.Services;

/// <summary>What Beacon is currently doing about a beacon's stage.</summary>
public enum StageState
{
    /// <summary>No stage, or Stagehand is not installed.</summary>
    None,

    /// <summary>There is a stage and nobody has said yes or no to it yet.</summary>
    Offered,

    Loading,

    Showing,

    /// <summary>Loaded once and since hidden, so showing it again costs nothing.</summary>
    Hidden,

    Failed,
}

/// <summary>
/// Loads the stage a keeper dressed their place with, for guests who run Stagehand.
///
/// The rule this service exists to enforce is that the guest decides. A stage is somebody else's
/// content arriving on your screen, so Beacon never loads one silently the first time: it offers,
/// remembers the answer, and skips the question only for beacons you have already said yes to, or if
/// you have turned auto-loading on for everything. Every stage is created as a Stagehand temporary
/// stage, so nothing is written into the guest's own library and refusing later leaves no trace.
/// </summary>
public sealed class StageDressing(
    Configuration config,
    BeaconApi api,
    StagehandIpc stagehand,
    LocationService location) : IDisposable
{
    private readonly FileDialogManager dialogs = new();

    /// <summary>Definitions already fetched this session, so walking in and out is not a download each time.</summary>
    private readonly Dictionary<Guid, string> cache = [];

    private readonly HashSet<Guid> loading = [];

    /// <summary>The beacon whose stage is currently created inside Stagehand. At most one at a time.</summary>
    private Guid? dressed;

    private bool visible;

    private Guid? failed;

    private string? lastError;

    /// <summary>True when a guest could load stages at all.</summary>
    public bool Available => stagehand.Available;

    /// <summary>The last failure, for the beacon page to show inline.</summary>
    public string? LastError => lastError;

    public void Start() => Svc.ClientState.TerritoryChanged += OnTerritoryChanged;

    /// <summary>Draws the stage file picker, if one is open. Must be called every frame from the UI callback.</summary>
    public void Draw() => dialogs.Draw();

    /// <summary>
    /// Opens a picker over the keeper's own Stagehand library so they can choose the stage this place
    /// is dressed with. The callback runs on the UI thread with the file's bytes and name.
    /// </summary>
    public void PickStage(Action<byte[], string> onPicked)
    {
        lastError = null;

        dialogs.OpenFileDialog(
            "Choose a stage",
            "Stagehand stage{.json}",
            (confirmed, paths) =>
            {
                if (!confirmed || paths.Count == 0)
                    return;

                var path = paths[0];

                try
                {
                    var info = new FileInfo(path);

                    if (!info.Exists)
                    {
                        lastError = "That file is no longer there.";
                        return;
                    }

                    if (info.Length > BeaconLimits.MaxStageBytes)
                    {
                        lastError = $"That stage is larger than {BeaconLimits.MaxStageBytes / (1024 * 1024)} MB.";
                        return;
                    }

                    onPicked(File.ReadAllBytes(path), Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning(ex, "Beacon: could not read the chosen stage.");
                    lastError = "Could not read that file.";
                }
            },
            selectionCountMax: 1,
            startPath: LibraryFolder(),
            isModal: true);
    }

    /// <summary>
    /// Where the keeper keeps their stages, read from Stagehand's own settings so the picker opens
    /// somewhere useful. Best effort: a missing or changed config just means starting in Documents.
    /// </summary>
    public static string? LibraryFolder()
    {
        try
        {
            var config = Path.Combine(
                Svc.PluginInterface.ConfigDirectory.Parent?.FullName ?? string.Empty,
                "Stagehand.json");

            if (File.Exists(config))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(config));
                if (document.RootElement.TryGetProperty("DefinitionLibraryPath", out var path)
                    && path.GetString() is { Length: > 0 } folder
                    && Directory.Exists(folder))
                    return folder;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Could not read Stagehand's library path.");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : null;
    }

    public StageState StateFor(BeaconDto beacon)
    {
        if (!beacon.HasStage || !stagehand.Available)
            return StageState.None;

        if (loading.Contains(beacon.Id))
            return StageState.Loading;

        if (dressed == beacon.Id)
            return visible ? StageState.Showing : StageState.Hidden;

        return failed == beacon.Id ? StageState.Failed : StageState.Offered;
    }

    /// <summary>
    /// Shows a beacon's stage, fetching the definition if this is the first time. Safe to call again
    /// while it is already showing.
    /// </summary>
    public void Show(BeaconDto beacon, bool remember)
    {
        if (!beacon.HasStage || !stagehand.Available || loading.Contains(beacon.Id))
            return;

        if (remember)
        {
            config.TrustedStageBeacons.Add(beacon.Id);
            config.RefusedStageBeacons.Remove(beacon.Id);
            config.Save();
        }

        if (dressed == beacon.Id && visible)
            return;

        lastError = null;
        failed = null;

        if (cache.TryGetValue(beacon.Id, out var cached))
        {
            Apply(beacon, cached);
            return;
        }

        loading.Add(beacon.Id);

        _ = Task.Run(async () =>
        {
            var definition = await api.DownloadStageAsync(beacon.Id, CancellationToken.None);

            await Svc.Framework.RunOnFrameworkThread(() =>
            {
                loading.Remove(beacon.Id);

                if (definition is null)
                {
                    failed = beacon.Id;
                    lastError = "Could not fetch that stage from the server.";
                    return;
                }

                cache[beacon.Id] = definition;
                Apply(beacon, definition);
            });
        });
    }

    /// <summary>Hides the stage without forgetting it, so showing it again is instant.</summary>
    public void Hide()
    {
        if (dressed is not { } id || !visible)
            return;

        stagehand.SetVisible(id, false);
        visible = false;
    }

    /// <summary>Tears the stage down entirely and records that this beacon's stage is not wanted.</summary>
    public void Refuse(BeaconDto beacon)
    {
        config.RefusedStageBeacons.Add(beacon.Id);
        config.TrustedStageBeacons.Remove(beacon.Id);
        config.Save();

        if (dressed == beacon.Id)
            Clear();
    }

    /// <summary>
    /// Called every tick. Loads the stage of a beacon the player is standing at, when they have
    /// already agreed to that beacon or turned auto-loading on, and takes it down again when they walk
    /// away. Nothing here loads a stage that has not been consented to.
    /// </summary>
    public void Tick(IReadOnlyList<BeaconDto> known)
    {
        if (!stagehand.Available)
            return;

        var here = Nearest(known);

        if (here is null)
        {
            // Out of range of anything dressed: put it away rather than leave a camp standing in a
            // field the player has already walked out of.
            if (dressed is not null)
                Clear();

            return;
        }

        if (dressed == here.Id)
        {
            // Stagehand hides every temporary stage on a location change, so re-show rather than
            // assume it is still up.
            if (!visible)
                visible = stagehand.SetVisible(here.Id, true);

            return;
        }

        if (config.RefusedStageBeacons.Contains(here.Id))
            return;

        if (config.AutoLoadStages || config.TrustedStageBeacons.Contains(here.Id))
            Show(here, remember: false);
    }

    /// <summary>The nearest beacon in range that actually has a stage to load.</summary>
    private BeaconDto? Nearest(IReadOnlyList<BeaconDto> known)
    {
        BeaconDto? best = null;
        var bestDistance = float.MaxValue;

        foreach (var beacon in known)
        {
            if (!beacon.HasStage)
                continue;

            if (location.DistanceTo(beacon) is not { } distance || distance > BeaconLimits.LightingRangeYalms)
                continue;

            if (distance >= bestDistance)
                continue;

            best = beacon;
            bestDistance = distance;
        }

        return best;
    }

    private void Apply(BeaconDto beacon, string definition)
    {
        // Only one at a time: two camps' worth of scenery in one field is nobody's intent.
        if (dressed is { } previous && previous != beacon.Id)
            Clear();

        var name = beacon.Stage?.Name is { Length: > 0 } stageName ? stageName : beacon.Name;

        if (!stagehand.CreateOrUpdate(beacon.Id, definition, $"Beacon: {name}"))
        {
            failed = beacon.Id;
            lastError = "Stagehand could not read that stage. It may have been built by a newer version.";
            return;
        }

        dressed = beacon.Id;
        visible = stagehand.SetVisible(beacon.Id, true);

        if (!visible)
        {
            failed = beacon.Id;
            lastError = "Stagehand loaded that stage but would not show it.";
        }
    }

    private void Clear()
    {
        if (dressed is { } id)
            stagehand.Destroy(id);

        dressed = null;
        visible = false;
    }

    /// <summary>
    /// Stagehand hides temporary stages when the player's location changes. Beacon drops its stage
    /// outright instead: the zone it was built for is the only place it belongs.
    /// </summary>
    private void OnTerritoryChanged(uint territory)
    {
        visible = false;

        if (dressed is not null)
            Clear();
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Clear();
    }
}
