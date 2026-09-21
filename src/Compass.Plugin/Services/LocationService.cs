using System.Numerics;
using Compass.Shared.Beacons;
using Dalamud.Game.ClientState.Objects.Enums;
using Lumina.Excel.Sheets;

namespace Compass.Services;

/// <summary>
/// Turns "where I am standing right now" into something the atlas can store, and back again.
///
/// This is the bridge between game data and the server, which has none. Zone names, map coordinates
/// and the nearest aetheryte are all resolved here at capture time and travel with the beacon,
/// because the server cannot look any of them up later.
/// </summary>
public sealed class LocationService
{
    /// <summary>
    /// The map image every marker coordinate is expressed against. Used to compare aetheryte markers
    /// with a beacon's position in one common space, independent of each map's own scale factor.
    /// </summary>
    private const float MapImageSize = 2048f;

    /// <summary>Map coordinates run 1 to 42 regardless of the zone's real size.</summary>
    private const float MapCoordinateSpan = 41f;

    /// <summary>Captures the player's current position, or null when they are not safely in the world.</summary>
    public BeaconLocation? CaptureHere()
    {
        if (!Svc.InWorld)
            return null;

        var player = Svc.Objects.LocalPlayer;
        if (player is null)
            return null;

        // TerritoryType widened to uint in API 15; ids comfortably fit a ushort, which is what
        // the wire contract uses, so narrow once here at the boundary.
        var territoryId = (ushort)Svc.ClientState.TerritoryType;
        if (territoryId == 0)
            return null;

        var position = player.Position;
        var territory = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        var map = territory?.Map.ValueNullable;

        var sizeFactor = map?.SizeFactor ?? 100;
        var offsetX = map?.OffsetX ?? 0;
        var offsetY = map?.OffsetY ?? 0;

        var mapX = WorldToMap(position.X, offsetX, sizeFactor);
        var mapY = WorldToMap(position.Z, offsetY, sizeFactor);

        var (aetheryteId, aetheryteName) = FindNearestAetheryte(territoryId, mapX, mapY);

        return new BeaconLocation
        {
            TerritoryId = territoryId,
            MapId = map?.RowId ?? 0,
            ZoneName = ZoneName(territoryId),
            SubAreaName = null,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            MapX = mapX,
            MapY = mapY,
            NearestAetheryteId = aetheryteId,
            NearestAetheryteName = aetheryteName,
        };
    }

    /// <summary>Captures the world the player is currently on.</summary>
    public BeaconRealm? CaptureRealm()
    {
        if (!Svc.InWorld)
            return null;

        var world = Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable;
        if (world is null)
            return null;

        var dataCenter = world.Value.DataCenter.ValueNullable;

        return new BeaconRealm
        {
            WorldId = world.Value.RowId,
            WorldName = world.Value.Name.ExtractText(),
            DataCenter = dataCenter?.Name.ExtractText() ?? string.Empty,
            Region = RegionName(dataCenter?.Region.RowId ?? 0),
        };
    }

    /// <summary>
    /// Every data centre name, for the atlas filter. Read from game data once and cached, since the
    /// list only changes when the game itself does.
    /// </summary>
    public IReadOnlyList<string> DataCenters => dataCenters ??= LoadDataCenters();

    private IReadOnlyList<string>? dataCenters;

    private static IReadOnlyList<string> LoadDataCenters()
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<WorldDCGroupType>();
            if (sheet is null)
                return [];

            return sheet
                .Select(dc => dc.Name.ExtractText())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            // Losing the filter list is survivable; the atlas simply shows everywhere.
            Svc.Log.Warning(ex, "Could not read the data centre list.");
            return [];
        }
    }

    /// <summary>Display name for a zone, falling back to the id so the UI never shows a blank.</summary>
    public string ZoneName(ushort territoryId)
    {
        var territory = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        if (territory is null)
            return $"Zone {territoryId}";

        var name = territory.Value.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? $"Zone {territoryId}" : name;
    }

    /// <summary>The player's current world id, or 0 when not in the world.</summary>
    public uint CurrentWorldId => Svc.Objects.LocalPlayer?.CurrentWorld.RowId ?? 0;

    /// <summary>The player's current world name, or empty when not in the world.</summary>
    public string CurrentWorldName =>
        Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

    /// <summary>The player's current data centre name, or empty when not in the world.</summary>
    public string CurrentDataCenter =>
        Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText()
        ?? string.Empty;

    public string CurrentCharacterName => Svc.Objects.LocalPlayer?.Name.TextValue ?? string.Empty;

    /// <summary>
    /// The logged-in character's race, clan and gender, read from their appearance data.
    ///
    /// Filled in automatically when somebody writes their card. The game already knows all three, and
    /// a form that asks for what it could have worked out is a form fewer people finish.
    /// </summary>
    public (string? Race, string? Clan, string? Gender) CurrentAppearance
    {
        get
        {
            try
            {
                var player = Svc.Objects.LocalPlayer;
                if (player is null)
                    return (null, null, null);

                var customize = player.Customize;
                if (customize.Length <= (int)CustomizeIndex.Tribe)
                    return (null, null, null);

                var raceId = customize[(int)CustomizeIndex.Race];
                var tribeId = customize[(int)CustomizeIndex.Tribe];
                var isFemale = customize[(int)CustomizeIndex.Gender] == 1;

                var race = Svc.Data.GetExcelSheet<Race>()?.GetRowOrDefault(raceId);
                var tribe = Svc.Data.GetExcelSheet<Tribe>()?.GetRowOrDefault(tribeId);

                // The sheets carry a masculine and a feminine spelling of every name.
                var raceName = isFemale
                    ? race?.Feminine.ExtractText()
                    : race?.Masculine.ExtractText();

                var clanName = isFemale
                    ? tribe?.Feminine.ExtractText()
                    : tribe?.Masculine.ExtractText();

                return (
                    string.IsNullOrWhiteSpace(raceName) ? null : raceName,
                    string.IsNullOrWhiteSpace(clanName) ? null : clanName,
                    isFemale ? "Female" : "Male");
            }
            catch (Exception ex)
            {
                // Losing the auto-fill costs a little typing; it must never stop the editor opening.
                Svc.Log.Warning(ex, "Could not read the character's appearance.");
                return (null, null, null);
            }
        }
    }

    /// <summary>The player's world-space position, or null when not safely in the world.</summary>
    public Vector3? CurrentPosition => Svc.InWorld ? Svc.Objects.LocalPlayer?.Position : null;

    /// <summary>
    /// Horizontal distance from the player to a beacon, or null when the player is elsewhere entirely.
    /// Returns null rather than a huge number so callers must handle "not comparable" explicitly.
    /// </summary>
    public float? DistanceTo(BeaconDto beacon)
    {
        if (!Svc.InWorld)
            return null;

        if ((ushort)Svc.ClientState.TerritoryType != beacon.Location.TerritoryId)
            return null;

        if (CurrentWorldId != beacon.Realm.WorldId)
            return null;

        var position = CurrentPosition;
        return position is null
            ? null
            : beacon.Location.HorizontalDistanceTo(position.Value.X, position.Value.Z);
    }

    /// <summary>True when the player is close enough, and in the right place, to light this beacon.</summary>
    public bool IsWithinLightingRange(BeaconDto beacon) =>
        DistanceTo(beacon) is { } distance && distance <= BeaconLimits.LightingRangeYalms;

    /// <summary>
    /// Converts a world coordinate into the 1-to-42 map coordinate players read off the map and paste
    /// into chat. The constants come from the game's own map projection.
    /// </summary>
    public static float WorldToMap(float value, short offset, ushort sizeFactor)
    {
        var scale = sizeFactor / 100f;
        return (MapCoordinateSpan / scale * (((value + offset) * scale + 1024f) / MapImageSize)) + 1f;
    }

    /// <summary>
    /// Picks the aetheryte to teleport to for a given spot.
    ///
    /// Aetheryte positions live in the map marker table, in the map image's own pixel space. Rather
    /// than convert markers into world coordinates, the beacon is converted into marker space, which
    /// needs no per-map scale factor and so cannot drift between zones of different sizes.
    /// </summary>
    private (uint Id, string? Name) FindNearestAetheryte(ushort territoryId, float mapX, float mapY)
    {
        try
        {
            var aetherytes = Svc.Data.GetExcelSheet<Aetheryte>();
            if (aetherytes is null)
                return (0, null);

            var candidates = aetherytes
                .Where(a => a.RowId != 0 && a.IsAetheryte && a.Territory.RowId == territoryId)
                .ToList();

            if (candidates.Count == 0)
                return (0, null);

            // One aetheryte in the zone is the common case; no need to resolve any markers.
            if (candidates.Count == 1)
                return (candidates[0].RowId, AetheryteName(candidates[0]));

            var markers = BuildMarkerLookup(territoryId);
            if (markers.Count == 0)
                return (candidates[0].RowId, AetheryteName(candidates[0]));

            var targetX = (mapX - 1f) * MapImageSize / MapCoordinateSpan;
            var targetY = (mapY - 1f) * MapImageSize / MapCoordinateSpan;

            var best = candidates[0];
            var bestDistance = float.MaxValue;

            foreach (var aetheryte in candidates)
            {
                if (!markers.TryGetValue(aetheryte.RowId, out var marker))
                    continue;

                var dx = marker.X - targetX;
                var dy = marker.Y - targetY;
                var distance = (dx * dx) + (dy * dy);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = aetheryte;
            }

            return (best.RowId, AetheryteName(best));
        }
        catch (Exception ex)
        {
            // Travel still works from the zone alone, so a lookup failure must not block a capture.
            Svc.Log.Warning(ex, "Could not resolve the nearest aetheryte for territory {Territory}.", territoryId);
            return (0, null);
        }
    }

    /// <summary>Marker positions for the aetherytes on a zone's map, keyed by aetheryte row id.</summary>
    private static Dictionary<uint, (float X, float Y)> BuildMarkerLookup(ushort territoryId)
    {
        var result = new Dictionary<uint, (float X, float Y)>();

        var territory = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        var map = territory?.Map.ValueNullable;
        if (map is null)
            return result;

        var markerSheet = Svc.Data.GetSubrowExcelSheet<MapMarker>();
        if (markerSheet is null)
            return result;

        if (!markerSheet.TryGetRow(map.Value.MapMarkerRange, out var group))
            return result;

        foreach (var marker in group)
        {
            // DataType 3 is the aetheryte marker; DataKey then holds the Aetheryte row id.
            if (marker.DataType != 3)
                continue;

            result[marker.DataKey.RowId] = (marker.X, marker.Y);
        }

        return result;
    }

    private static string? AetheryteName(Aetheryte aetheryte)
    {
        var name = aetheryte.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Region names, used to tell the player whether a beacon is even reachable: data centre travel
    /// works within a region, never across one.
    /// </summary>
    private static string RegionName(uint region) => region switch
    {
        1 => "Japan",
        2 => "North America",
        3 => "Europe",
        4 => "Oceania",
        5 => "China",
        6 => "Korea",
        _ => "Unknown",
    };
}
