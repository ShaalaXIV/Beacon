namespace Compass.Shared.Beacons;

/// <summary>What happened to a beacon.</summary>
public enum BeaconEventKind
{
    /// <summary>A flame was lit. Carries the flame, and the beacon when the client may not know it yet.</summary>
    Lit = 0,

    /// <summary>A flame went out, by hand or by timeout.</summary>
    Extinguished = 1,

    /// <summary>A burning flame was extended.</summary>
    Stoked = 2,

    /// <summary>A new beacon was published.</summary>
    Published = 3,

    /// <summary>An existing beacon was edited.</summary>
    Updated = 4,

    /// <summary>A beacon was deleted or hidden and should be dropped from the atlas.</summary>
    Removed = 5,
}

/// <summary>
/// A push message from the server's beacon hub.
///
/// Flame changes carry only the flame so that a lit/extinguish storm stays cheap; structural changes
/// carry the whole beacon because the client has no way to patch fields it cannot see.
/// </summary>
public sealed record BeaconEvent
{
    public BeaconEventKind Kind { get; init; }

    public Guid BeaconId { get; init; }

    /// <summary>Present for <see cref="BeaconEventKind.Lit"/>, <see cref="BeaconEventKind.Extinguished"/> and <see cref="BeaconEventKind.Stoked"/>.</summary>
    public BeaconFlame? Flame { get; init; }

    /// <summary>Present for <see cref="BeaconEventKind.Published"/> and <see cref="BeaconEventKind.Updated"/>, and for lights on beacons the client may not have.</summary>
    public BeaconDto? Beacon { get; init; }

    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
