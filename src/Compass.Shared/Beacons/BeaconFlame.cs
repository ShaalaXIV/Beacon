using System.Text.Json.Serialization;

namespace Compass.Shared.Beacons;

/// <summary>
/// The live state of a beacon: whether somebody is standing at it right now, ready to play.
///
/// This is the part of a beacon that changes minute to minute, so it is modelled separately from the
/// beacon itself -- realtime updates carry a flame, not a whole beacon, and the atlas can patch a row
/// in place without refetching.
/// </summary>
public sealed record BeaconFlame
{
    /// <summary>A flame that is not lit. Shared instance; the type is immutable.</summary>
    public static readonly BeaconFlame Dark = new();

    /// <summary>True when someone has declared themselves present and open to roleplay.</summary>
    public bool IsLit { get; init; }

    /// <summary>When the flame was lit.</summary>
    public DateTimeOffset? LitAt { get; init; }

    /// <summary>
    /// When the flame will go out on its own. Always set while lit -- a beacon that can never expire
    /// becomes a lie the moment its owner logs off, and a stale atlas is worse than an empty one.
    /// </summary>
    public DateTimeOffset? LitUntil { get; init; }

    /// <summary>Character name of whoever lit it. Not necessarily the owner, if public lighting is on.</summary>
    public string? LitByName { get; init; }

    /// <summary>Account that lit it, so we know who is allowed to snuff it early.</summary>
    public Guid? LitByAccountId { get; init; }

    /// <summary>
    /// A short line from whoever lit it, shown in the atlas: "tavern night, walk-ins welcome",
    /// "wounded traveller needs a healer", and so on. The difference between a pin and an invitation.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>How much longer the flame burns, or null when dark. Clamped at zero.</summary>
    [JsonIgnore]
    public TimeSpan? Remaining
    {
        get
        {
            if (!IsLit || LitUntil is null)
                return null;

            var left = LitUntil.Value - DateTimeOffset.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// True when the flame is nominally lit but its time has already run out. The sweeper will clear it
    /// shortly; readers should treat it as dark immediately rather than showing a burnt-out flame.
    /// </summary>
    [JsonIgnore]
    public bool IsStale => IsLit && LitUntil is not null && LitUntil.Value <= DateTimeOffset.UtcNow;

    /// <summary>True when the flame should be drawn as burning: lit, and not past its time.</summary>
    [JsonIgnore]
    public bool IsBurning => IsLit && !IsStale;
}
