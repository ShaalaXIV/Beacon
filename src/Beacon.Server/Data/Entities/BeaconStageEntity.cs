namespace Beacon.Server.Data.Entities;

/// <summary>
/// The Stagehand stage attached to a beacon.
///
/// Only the description of it lives in the database. The definition itself is a JSON document that
/// can run to megabytes when it embeds its own models, and it follows the same rule as a screenshot:
/// bytes on disk, facts in SQLite. A beacon has at most one, so the beacon's own id is the key.
/// </summary>
public class BeaconStageEntity
{
    public Guid BeaconId { get; set; }

    public Guid UploadedByAccountId { get; set; }

    /// <summary>The stage's own name, as written by whoever built it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The credit the author gave themselves, which need not match the beacon's keeper.</summary>
    public string AuthorName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The zone it was built for, so a guest standing somewhere else can be warned.</summary>
    public uint IntendedTerritoryType { get; set; }

    public int ObjectCount { get; set; }

    public int ModpackCount { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
