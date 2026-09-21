namespace Beacon.Server.Data.Entities;

/// <summary>
/// Something flagged for a moderator.
///
/// One table for both beacons and profiles, with exactly one of the two target columns set. A second
/// near-identical table would mean a second moderation queue, and the whole point of a queue is that
/// there is one of them.
/// </summary>
public class ReportEntity
{
    public Guid Id { get; set; }

    /// <summary>Set when the report is about a beacon.</summary>
    public Guid? BeaconId { get; set; }

    /// <summary>Set when the report is about a profile.</summary>
    public Guid? ProfileId { get; set; }

    public Guid ReporterAccountId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool Resolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedByAccountId { get; set; }
}
