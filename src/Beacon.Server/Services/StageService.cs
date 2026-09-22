using System.Text;
using System.Text.Json;
using Beacon.Shared.Beacons;
using Microsoft.Extensions.Options;

namespace Beacon.Server.Services;

/// <summary>Why a stage was refused, in words a keeper can act on.</summary>
public sealed record StageRejection(string Reason);

/// <summary>
/// Stores the Stagehand stage a keeper has dressed their place with.
///
/// The server deliberately does not link Stagehand's own libraries. It treats a stage as an opaque
/// document, reads only the few facts a guest needs in order to decide whether to load it, and
/// refuses the shapes that cannot possibly work for anyone but their author. Understanding the format
/// more deeply than that would tie the server's release cycle to a game plugin's.
/// </summary>
public sealed class StageService(
    IOptions<BeaconOptions> options,
    IHostEnvironment env,
    ILogger<StageService> logger)
{
    private string Root => options.Value.StageDirectory(env.ContentRootPath);

    /// <summary>
    /// Validates an uploaded definition and writes it to disk, replacing whatever was there.
    /// Returns the facts to record, or a rejection to show the keeper.
    /// </summary>
    public async Task<(BeaconStageInfo? Info, StageRejection? Rejected)> StoreAsync(
        Guid beaconId,
        Stream source,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);

        if (buffer.Length == 0)
            return (null, new StageRejection("That stage file was empty."));

        if (buffer.Length > BeaconLimits.MaxStageBytes)
            return (null, new StageRejection(
                $"That stage is {buffer.Length / (1024 * 1024)} MB. The limit is {BeaconLimits.MaxStageBytes / (1024 * 1024)} MB, " +
                "so a guest is not made to download a mod collection in order to visit."));

        var text = Encoding.UTF8.GetString(buffer.ToArray());

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return (null, new StageRejection("That file is not a Stagehand stage."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("Info", out var info))
                return (null, new StageRejection("That file is not a Stagehand stage."));

            var objects = root.TryGetProperty("Objects", out var objectsElement) && objectsElement.ValueKind is JsonValueKind.Object
                ? objectsElement.EnumerateObject().Count()
                : 0;

            if (objects == 0)
                return (null, new StageRejection("That stage does not place anything."));

            if (objects > BeaconLimits.MaxStageObjects)
                return (null, new StageRejection(
                    $"That stage places {objects} objects. The limit for a shared stage is {BeaconLimits.MaxStageObjects}, " +
                    "because every guest who loads it pays for all of them."));

            var modpacks = root.TryGetProperty("EmbeddedModpacks", out var modpacksElement) && modpacksElement.ValueKind is JsonValueKind.Object
                ? modpacksElement.EnumerateObject().Count()
                : 0;

            if (modpacks > 0 && ReferencesLocalFiles(modpacksElement))
                return (null, new StageRejection(
                    "That stage loads files from your own disk, so it would arrive at a guest with holes in it. " +
                    "Embed the modpack in the stage before sharing it."));

            var stage = new BeaconStageInfo
            {
                Name = ReadString(info, "Name", 128),
                AuthorName = ReadString(info, "AuthorName", 64),
                Description = ReadString(info, "Description", 512),
                IntendedTerritoryType = info.TryGetProperty("IntendedTerritoryType", out var territory)
                                        && territory.TryGetUInt32(out var value)
                    ? value
                    : 0,
                ObjectCount = objects,
                ModpackCount = modpacks,
                SizeBytes = buffer.Length,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await WriteAsync(beaconId, buffer, ct);
            return (stage, null);
        }
    }

    /// <summary>Opens the stored definition, or null when there is none.</summary>
    public Stream? Open(Guid beaconId)
    {
        var path = PathFor(beaconId);

        try
        {
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open the stage for beacon {BeaconId}.", beaconId);
            return null;
        }
    }

    /// <summary>Deletes the stored definition. Missing is success: the caller wanted it gone.</summary>
    public void Delete(Guid beaconId)
    {
        try
        {
            var path = PathFor(beaconId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the stage for beacon {BeaconId}.", beaconId);
        }
    }

    private async Task WriteAsync(Guid beaconId, MemoryStream buffer, CancellationToken ct)
    {
        var path = PathFor(beaconId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written beside the target and moved into place, so a failed upload cannot leave a guest
        // downloading half a stage.
        var partial = path + ".partial";
        buffer.Position = 0;

        await using (var file = File.Create(partial))
            await buffer.CopyToAsync(file, ct);

        File.Move(partial, path, overwrite: true);
    }

    /// <summary>
    /// True when any embedded resource is backed by a path on the author's machine. Such a stage
    /// works perfectly for its author and is broken for everyone else, which is the worst failure to
    /// discover after travelling to somebody's camp.
    /// </summary>
    private static bool ReferencesLocalFiles(JsonElement modpacks)
    {
        foreach (var modpack in modpacks.EnumerateObject())
        {
            if (!modpack.Value.TryGetProperty("ModdedResources", out var resources)
                || resources.ValueKind is not JsonValueKind.Object)
                continue;

            foreach (var resource in resources.EnumerateObject())
            {
                if (resource.Value.ValueKind is not JsonValueKind.Object)
                    continue;

                if (resource.Value.TryGetProperty("SourceDiskPath", out _))
                    return true;

                if (resource.Value.TryGetProperty("Type", out var type)
                    && type.ValueKind is JsonValueKind.String
                    && type.GetString()?.Contains("Disk", StringComparison.OrdinalIgnoreCase) is true)
                    return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement info, string property, int maxLength)
    {
        if (!info.TryGetProperty(property, out var element) || element.ValueKind is not JsonValueKind.String)
            return string.Empty;

        var value = element.GetString() ?? string.Empty;
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    /// <summary>Sharded the same way screenshots are, for the same reason: flat directories get slow.</summary>
    private string PathFor(Guid beaconId) =>
        Path.Combine(Root, beaconId.ToString("N")[..2], $"{beaconId:N}.json");
}
