namespace Beacon.Shared.Beacons;

/// <summary>Publish a new beacon at a spot the caller is standing on.</summary>
public sealed record CreateBeaconRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public BeaconKind Kind { get; init; } = BeaconKind.Camp;
    public BeaconLocation Location { get; init; } = new();
    public BeaconRealm Realm { get; init; } = new();
    public IReadOnlyList<string> Tags { get; init; } = [];
    public BeaconVisibility Visibility { get; init; } = BeaconVisibility.Public;
    public bool AllowPublicLighting { get; init; }
}

/// <summary>
/// Edit an existing beacon. Every field is optional: null means "leave this alone", which lets the
/// editor send only what actually changed and avoids two clients clobbering each other's edits.
/// </summary>
public sealed record UpdateBeaconRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public BeaconKind? Kind { get; init; }

    /// <summary>Supply to move the beacon to wherever the caller is now standing.</summary>
    public BeaconLocation? Location { get; init; }

    /// <summary>Supply alongside <see cref="Location"/> when the move crosses worlds.</summary>
    public BeaconRealm? Realm { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
    public BeaconVisibility? Visibility { get; init; }
    public bool? AllowPublicLighting { get; init; }

    /// <summary>Set to true to drop the current screenshot without uploading a replacement.</summary>
    public bool? ClearImage { get; init; }
}

/// <summary>
/// Light a beacon: announce that someone is there now and open to roleplay.
/// The caller's position is included so the server can enforce the proximity rule itself --
/// a client-side check alone would be trivially bypassed.
/// </summary>
public sealed record LightBeaconRequest
{
    /// <summary>How long to burn for, clamped server-side to the configured range.</summary>
    public int Minutes { get; init; } = BeaconLimits.DefaultLitMinutes;

    /// <summary>Optional invitation line shown in the atlas.</summary>
    public string? Note { get; init; }

    /// <summary>Character doing the lighting, shown as "lit by".</summary>
    public string CharacterName { get; init; } = string.Empty;

    /// <summary>Caller's world, checked against the beacon's -- you cannot light a camp from another world.</summary>
    public uint WorldId { get; init; }

    /// <summary>Caller's zone, checked against the beacon's.</summary>
    public ushort TerritoryId { get; init; }

    /// <summary>Caller's world-space position, checked against the beacon's.</summary>
    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}

/// <summary>Extend an already-burning flame without relighting it, so the "lit at" time is preserved.</summary>
public sealed record StokeBeaconRequest
{
    /// <summary>Additional minutes, clamped so the total never exceeds the maximum burn.</summary>
    public int Minutes { get; init; } = 60;

    /// <summary>Optional replacement note. Null leaves the existing one.</summary>
    public string? Note { get; init; }
}

/// <summary>Flag a beacon for a moderator's attention.</summary>
public sealed record ReportBeaconRequest
{
    public string Reason { get; init; } = string.Empty;
}
