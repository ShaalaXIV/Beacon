namespace Beacon.Shared.Beacons;

/// <summary>
/// What a keeper has built on top of a place, described the way Stagehand describes it.
///
/// This is metadata only. The definition itself can be hundreds of kilobytes, so it is never carried
/// in the atlas listing -- it is fetched from its own route, once, by someone who has decided they
/// want to see the place dressed.
/// </summary>
public sealed record BeaconStageInfo
{
    /// <summary>The stage's own name, as its author wrote it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whoever the author credited, which need not be the beacon's keeper.</summary>
    public string AuthorName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>The zone the stage was built for. Shown so an obvious mismatch can be warned about.</summary>
    public uint IntendedTerritoryType { get; init; }

    /// <summary>How many objects it places. The honest measure of "how heavy is this going to be".</summary>
    public int ObjectCount { get; init; }

    /// <summary>How many embedded modpacks it carries. Non-zero means it brings its own textures and models.</summary>
    public int ModpackCount { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>A short line for the beacon page: "41 objects, 2 modpacks".</summary>
    public string Weight =>
        ModpackCount > 0
            ? $"{ObjectCount} object{(ObjectCount == 1 ? "" : "s")}, {ModpackCount} modpack{(ModpackCount == 1 ? "" : "s")}"
            : $"{ObjectCount} object{(ObjectCount == 1 ? "" : "s")}";
}
