using System.Text.Json.Serialization;

namespace Compass.Shared.Beacons;

/// <summary>
/// Everything needed to both <em>find</em> a spot in the overworld and <em>travel</em> to it.
///
/// The travel hints (<see cref="NearestAetheryteId"/>, <see cref="AethernetShard"/>) are captured by the
/// plugin at publish time rather than resolved on read, because resolving them requires game data the
/// server does not have. The server treats them as opaque routing breadcrumbs.
/// </summary>
public sealed record BeaconLocation
{
    /// <summary>TerritoryType row id. The authoritative "which zone" key.</summary>
    public ushort TerritoryId { get; init; }

    /// <summary>Map row id, used to turn world coordinates into the 1..42 map coordinates players quote.</summary>
    public uint MapId { get; init; }

    /// <summary>Denormalised zone name so the server can search and display without game data. e.g. "Il Mheg".</summary>
    public string ZoneName { get; init; } = string.Empty;

    /// <summary>Optional finer-grained place name from the map's sub-area markers. e.g. "The Bramble Patch".</summary>
    public string? SubAreaName { get; init; }

    /// <summary>World-space X. This is what we actually navigate to.</summary>
    public float X { get; init; }

    /// <summary>World-space Y (height).</summary>
    public float Y { get; init; }

    /// <summary>World-space Z.</summary>
    public float Z { get; init; }

    /// <summary>Human-facing map X, the value shown in the in-game map and chat coordinate links.</summary>
    public float MapX { get; init; }

    /// <summary>Human-facing map Y.</summary>
    public float MapY { get; init; }

    /// <summary>Aetheryte row id nearest to this point, used as the first hop of a journey. 0 when unknown.</summary>
    public uint NearestAetheryteId { get; init; }

    /// <summary>Display name of <see cref="NearestAetheryteId"/>, so the server can render a route without game data.</summary>
    public string? NearestAetheryteName { get; init; }

    /// <summary>
    /// Optional aethernet shard name to hop to after the main aetheryte, passed straight through to Lifestream.
    /// Only meaningful inside cities and the few zones with aethernet networks.
    /// </summary>
    public string? AethernetShard { get; init; }

    /// <summary>Straight-line distance in yalms to another point, including height.</summary>
    public float DistanceTo(float x, float y, float z)
    {
        var dx = X - x;
        var dy = Y - y;
        var dz = Z - z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// Distance ignoring height. Preferred for proximity checks: standing on the balcony above a camp
    /// should still count as "at" the camp, and a 3D check would wrongly reject it.
    /// </summary>
    public float HorizontalDistanceTo(float x, float z)
    {
        var dx = X - x;
        var dz = Z - z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>The coordinate string players expect to see, e.g. "X: 12.4  Y: 30.1".</summary>
    [JsonIgnore]
    public string MapCoordinateText => $"X: {MapX:0.0}  Y: {MapY:0.0}";
}
