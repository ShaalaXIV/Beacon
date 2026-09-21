using System.Security.Cryptography;
using Beacon.Server.Data.Entities;
using Beacon.Shared.Beacons;

namespace Beacon.Server.Data;

/// <summary>Translation between the storage shape and the wire shape.</summary>
public static class BeaconMapper
{
    /// <summary>
    /// Alphabet for share codes, with 0/O/1/I/L removed. These codes get read aloud in Discord voice
    /// and retyped by hand, so the characters people confuse are simply not in the set.
    /// </summary>
    private const string ShareAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int ShareCodeLength = 8;

    public static string NewShareCode()
    {
        Span<char> buffer = stackalloc char[ShareCodeLength];
        for (var i = 0; i < ShareCodeLength; i++)
            buffer[i] = ShareAlphabet[RandomNumberGenerator.GetInt32(ShareAlphabet.Length)];

        return new string(buffer);
    }

    /// <summary>
    /// Normalises a tag list: lowercased, trimmed, de-duplicated, length-capped, and stripped of the
    /// pipe used as the storage delimiter.
    /// </summary>
    public static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
            return [];

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant().Replace('|', '-'))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > BeaconLimits.TagMaxLength ? t[..BeaconLimits.TagMaxLength] : t)
            .Distinct(StringComparer.Ordinal)
            .Take(BeaconLimits.MaxTags)
            .ToArray();
    }

    /// <summary>Wraps and delimits tags with pipes so a LIKE search can match whole tags only.</summary>
    public static string JoinTags(IEnumerable<string>? tags)
    {
        var normalized = NormalizeTags(tags);
        return normalized.Length == 0 ? string.Empty : $"|{string.Join('|', normalized)}|";
    }

    public static string[] SplitTags(string? csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static BeaconFlame ToFlame(this BeaconEntity e) =>
        !e.IsLit
            ? BeaconFlame.Dark
            : new BeaconFlame
            {
                IsLit = true,
                LitAt = e.LitAt,
                LitUntil = e.LitUntil,
                LitByName = e.LitByName,
                LitByAccountId = e.LitByAccountId,
                Note = e.LitNote,
            };

    public static BeaconDto ToDto(this BeaconEntity e, string ownerName, bool isFavorite = false) =>
        new()
        {
            Id = e.Id,
            Name = e.Name,
            Description = e.Description,
            Kind = e.Kind,
            Location = new BeaconLocation
            {
                TerritoryId = e.TerritoryId,
                MapId = e.MapId,
                ZoneName = e.ZoneName,
                SubAreaName = e.SubAreaName,
                X = e.X,
                Y = e.Y,
                Z = e.Z,
                MapX = e.MapX,
                MapY = e.MapY,
                NearestAetheryteId = e.NearestAetheryteId,
                NearestAetheryteName = e.NearestAetheryteName,
                AethernetShard = e.AethernetShard,
            },
            Realm = new BeaconRealm
            {
                WorldId = e.WorldId,
                WorldName = e.WorldName,
                DataCenter = e.DataCenter,
                Region = e.Region,
            },
            OwnerAccountId = e.OwnerAccountId,
            OwnerName = ownerName,
            Flame = e.ToFlame(),
            AllowPublicLighting = e.AllowPublicLighting,
            ImageId = e.ImageId,
            Tags = SplitTags(e.TagsCsv),
            Visibility = e.Visibility,
            ShareCode = e.ShareCode,
            FavoriteCount = e.FavoriteCount,
            IsFavorite = isFavorite,
            TimesLit = e.TimesLit,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

    /// <summary>Copies a captured location onto an entity. Used by both create and "move beacon here".</summary>
    public static void ApplyLocation(this BeaconEntity e, BeaconLocation location)
    {
        e.TerritoryId = location.TerritoryId;
        e.MapId = location.MapId;
        e.ZoneName = Truncate(location.ZoneName, 128) ?? string.Empty;
        e.SubAreaName = Truncate(location.SubAreaName, 128);
        e.X = location.X;
        e.Y = location.Y;
        e.Z = location.Z;
        e.MapX = location.MapX;
        e.MapY = location.MapY;
        e.NearestAetheryteId = location.NearestAetheryteId;
        e.NearestAetheryteName = Truncate(location.NearestAetheryteName, 128);
        e.AethernetShard = Truncate(location.AethernetShard, 128);
    }

    public static void ApplyRealm(this BeaconEntity e, BeaconRealm realm)
    {
        e.WorldId = realm.WorldId;
        e.WorldName = Truncate(realm.WorldName, 48) ?? string.Empty;
        e.DataCenter = Truncate(realm.DataCenter, 48) ?? string.Empty;
        e.Region = Truncate(realm.Region, 48) ?? string.Empty;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
