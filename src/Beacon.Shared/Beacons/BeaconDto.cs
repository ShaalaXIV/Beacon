using System.Text.Json.Serialization;

namespace Beacon.Shared.Beacons;

/// <summary>
/// A place in the overworld somebody has claimed for roleplay, as the atlas sees it.
///
/// A beacon is deliberately more than a coordinate: the screenshot and description are what make a
/// stranger decide to make the trip, and the flame is what tells them it is worth making it <em>now</em>.
/// </summary>
public sealed record BeaconDto
{
    /// <summary>Server-assigned identity.</summary>
    public Guid Id { get; init; }

    /// <summary>Short display name. "The Bramblewood Camp".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// A paragraph or two of flavour: what the place is, what happens there, who is welcome.
    /// Plain text; rendered wrapped, never as markup.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>What kind of place this is, driving icon and tint.</summary>
    public BeaconKind Kind { get; init; }

    /// <summary>Where it is, and how to get there.</summary>
    public BeaconLocation Location { get; init; } = new();

    /// <summary>Which world it sits on.</summary>
    public BeaconRealm Realm { get; init; } = new();

    /// <summary>Account that created it and may edit or delete it.</summary>
    public Guid OwnerAccountId { get; init; }

    /// <summary>Display name of the owner, as they chose to be known.</summary>
    public string OwnerName { get; init; } = string.Empty;

    /// <summary>Live presence state.</summary>
    public BeaconFlame Flame { get; init; } = BeaconFlame.Dark;

    /// <summary>
    /// When true, anyone standing close enough may light this beacon, not just the owner.
    /// This is how a place declares itself a public commons rather than one person's camp.
    /// </summary>
    public bool AllowPublicLighting { get; init; }

    /// <summary>Uploaded screenshot, if any. Resolve through the image endpoints.</summary>
    public Guid? ImageId { get; init; }

    /// <summary>Free-form tags for filtering: "tavern", "combat-rp", "18+", "drop-in".</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Who can see it in the atlas.</summary>
    public BeaconVisibility Visibility { get; init; }

    /// <summary>
    /// Short human-typable code for sharing a beacon outside the plugin -- in a Discord post, say.
    /// Works for unlisted beacons too, which is the whole point of them.
    /// </summary>
    public string ShareCode { get; init; } = string.Empty;

    /// <summary>How many people have starred it.</summary>
    public int FavoriteCount { get; init; }

    /// <summary>Whether the account making this request has starred it. Per-caller; not cached across accounts.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>Total number of times this beacon has ever been lit. A rough proxy for "is this place alive".</summary>
    public int TimesLit { get; init; }

    /// <summary>When it was published.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was last edited.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>True when this beacon has a screenshot to show.</summary>
    [JsonIgnore]
    public bool HasImage => ImageId is not null && ImageId != Guid.Empty;

    /// <summary>"Il Mheg  -  Balmung (Crystal)", the one-line answer to "where is this".</summary>
    [JsonIgnore]
    public string WhereText => $"{Location.ZoneName}  -  {Realm}";
}
