using Beacon.Shared.Beacons;

namespace Beacon.Server.Data.Entities;

/// <summary>
/// One burn of one beacon, from lighting to going out.
///
/// Kept as history rather than folded into a counter so an owner can see when their place actually
/// drew people, and so abuse (someone relighting a beacon they do not own, repeatedly) is visible.
/// </summary>
public class BeaconLightEntity
{
    public Guid Id { get; set; }

    public Guid BeaconId { get; set; }

    public Guid AccountId { get; set; }

    public string CharacterName { get; set; } = string.Empty;

    public DateTimeOffset LitAt { get; set; }

    public DateTimeOffset LitUntil { get; set; }

    public DateTimeOffset? ExtinguishedAt { get; set; }

    public ExtinguishReason? Reason { get; set; }

    public string? Note { get; set; }
}
