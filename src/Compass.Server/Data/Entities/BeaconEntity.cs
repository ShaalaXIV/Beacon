using Compass.Shared.Beacons;

namespace Compass.Server.Data.Entities;

/// <summary>
/// A published beacon.
///
/// Location, realm and flame are stored as flat columns rather than reusing the shared DTO records as
/// owned types: keeping the table shape independent of the wire contract means a protocol change does
/// not force a migration, and vice versa.
/// </summary>
public class BeaconEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public BeaconKind Kind { get; set; }

    // --- Location -------------------------------------------------------

    public ushort TerritoryId { get; set; }

    public uint MapId { get; set; }

    public string ZoneName { get; set; } = string.Empty;

    public string? SubAreaName { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float MapX { get; set; }

    public float MapY { get; set; }

    public uint NearestAetheryteId { get; set; }

    public string? NearestAetheryteName { get; set; }

    public string? AethernetShard { get; set; }

    // --- Realm ----------------------------------------------------------

    public uint WorldId { get; set; }

    public string WorldName { get; set; } = string.Empty;

    public string DataCenter { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    // --- Ownership ------------------------------------------------------

    public Guid OwnerAccountId { get; set; }

    public AccountEntity? Owner { get; set; }

    // --- Flame ----------------------------------------------------------

    public bool IsLit { get; set; }

    public DateTimeOffset? LitAt { get; set; }

    public DateTimeOffset? LitUntil { get; set; }

    public string? LitByName { get; set; }

    public Guid? LitByAccountId { get; set; }

    public string? LitNote { get; set; }

    public bool AllowPublicLighting { get; set; }

    /// <summary>Kept for sorting by "recently active" even after the flame goes out.</summary>
    public DateTimeOffset? LastLitAt { get; set; }

    public int TimesLit { get; set; }

    // --- Presentation ---------------------------------------------------

    public Guid? ImageId { get; set; }

    /// <summary>
    /// Tags stored pipe-delimited and pipe-wrapped ("|tavern|drop-in|") so that a LIKE '%|tag|%'
    /// matches whole tags only, and never a tag that merely contains another as a substring.
    /// </summary>
    public string TagsCsv { get; set; } = string.Empty;

    public BeaconVisibility Visibility { get; set; }

    public string ShareCode { get; set; } = string.Empty;

    public int FavoriteCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete, so that a removed beacon can still be traced from a report.</summary>
    public bool IsDeleted { get; set; }

    public List<FavoriteEntity> Favorites { get; set; } = [];
}
