namespace Compass.Server.Data.Entities;

/// <summary>
/// Metadata for a stored screenshot. The bytes themselves live on disk under the data directory,
/// not in the database: serving them straight from the filesystem keeps large reads out of SQLite
/// and lets the OS page cache do the work.
/// </summary>
public class BeaconImageEntity
{
    public Guid Id { get; set; }

    public Guid BeaconId { get; set; }

    public Guid UploadedByAccountId { get; set; }

    /// <summary>Always "image/webp" today; stored so a future format change does not need a migration.</summary>
    public string ContentType { get; set; } = "image/webp";

    public int Width { get; set; }

    public int Height { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
