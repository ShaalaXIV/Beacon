using System.Text.Json.Serialization;

namespace Compass.Shared.Profiles;

/// <summary>Who the character is. The top half of the card.</summary>
public sealed record ProfileIdentity
{
    /// <summary>The character's name as they want it written, which need not be their login name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The epithet under the name.</summary>
    public string? Title { get; init; }

    public string? Race { get; init; }

    public string? Clan { get; init; }

    public AgeRange Age { get; init; }

    public string? Gender { get; init; }

    public string? Pronouns { get; init; }

    /// <summary>The three words that set the tone before anybody reads a word of prose.</summary>
    public IReadOnlyList<string> Archetype { get; init; } = [];

    public string? Quote { get; init; }

    /// <summary>"Miqo'te - Seeker of the Sun", or just the race when no clan is set.</summary>
    [JsonIgnore]
    public string Lineage => string.IsNullOrWhiteSpace(Clan) ? Race ?? string.Empty : $"{Race} · {Clan}";
}

/// <summary>How the player likes to roleplay, as opposed to who the character is.</summary>
public sealed record ProfileStyle
{
    public RpLength Length { get; init; } = RpLength.Casual;

    public IReadOnlyList<RpTone> Tones { get; init; } = [];

    public IReadOnlyList<RpActivity> Activities { get; init; } = [];

    /// <summary>
    /// What this player will not do, shown on the card face rather than buried.
    ///
    /// On the face because it prevents the interactions that make people stop using a plugin. Somebody
    /// who finds out three messages in is already having a bad time.
    /// </summary>
    public string? Boundaries { get; init; }

    /// <summary>Whether strangers may simply walk up, or should ask first.</summary>
    public bool WalkupsWelcome { get; init; } = true;

    /// <summary>
    /// Adult content this player is willing to write, when a story leads there.
    ///
    /// Empty for most cards, and empty by default.
    /// </summary>
    public IReadOnlyList<MatureTheme> MatureThemes { get; init; } = [];

    /// <summary>
    /// Whether this card is gated behind an explicit request to see adult content.
    ///
    /// Derived by the server from <see cref="MatureThemes"/> rather than set independently. A separate
    /// switch would let somebody mark themselves open to explicit scenes and still appear in a search
    /// made by a person who asked not to see any, which is the one outcome this must not allow.
    /// </summary>
    public bool IsMature { get; init; }
}

/// <summary>Player-level information, kept apart from the character's.</summary>
public sealed record PlayerNotes
{
    public string? Timezone { get; init; }

    /// <summary>Roughly when this person plays, in their own words.</summary>
    public string? Availability { get; init; }

    /// <summary>How to reach them out of character, if they want that known.</summary>
    public string? Contact { get; init; }
}

/// <summary>A reason to walk over and say something.</summary>
public sealed record ProfileHook
{
    public string Text { get; init; } = string.Empty;

    public int Order { get; init; }
}

/// <summary>One image in the reference gallery.</summary>
public sealed record ProfileImage
{
    public Guid ImageId { get; init; }

    public GalleryCategory Category { get; init; }

    public string? Caption { get; init; }

    public int Order { get; init; }
}

/// <summary>
/// A tie to another character.
///
/// <see cref="Confirmed"/> matters: anybody can claim a connection to anybody, so an unconfirmed tie
/// is shown only on the card of the person who claimed it, and marked as one-sided. Without that, the
/// relationships list is a way to write things on a stranger's profile.
/// </summary>
public sealed record ProfileLink
{
    public Guid OtherProfileId { get; init; }

    public string OtherName { get; init; } = string.Empty;

    public RelationshipKind Kind { get; init; }

    public string? Note { get; init; }

    public bool Confirmed { get; init; }
}

/// <summary>
/// Whether this person is out there right now, and when they last were.
///
/// Computed on read, never stored as a state anybody has to maintain. A status field that a player
/// sets by hand is stale the moment they forget it, and a directory full of people who say they are
/// available but are not is worse than one that says nothing at all.
/// </summary>
public sealed record ProfilePresence
{
    public static readonly ProfilePresence Unknown = new();

    public AvailabilityState State { get; init; }

    /// <summary>
    /// The last time this character was seen standing at a lit beacon.
    ///
    /// Presence rather than login: "had the game open" says nothing, whereas "was out at a fire" is
    /// exactly the thing somebody is trying to judge when they wonder whether a community is alive.
    /// </summary>
    public DateTimeOffset? LastActiveAt { get; init; }

    /// <summary>The beacon they are at, when one of theirs is burning.</summary>
    public Guid? BeaconId { get; init; }

    public string? BeaconName { get; init; }

    public string? ZoneName { get; init; }

    public string? WorldName { get; init; }

    /// <summary>When that flame goes out.</summary>
    public DateTimeOffset? LitUntil { get; init; }

    [JsonIgnore]
    public bool IsOpen => State == AvailabilityState.OpenToWalkups;

    /// <summary>True when they have been seen recently enough to count as part of the living community.</summary>
    [JsonIgnore]
    public bool IsRecentlyActive =>
        LastActiveAt is { } seen && DateTimeOffset.UtcNow - seen <= ProfileLimits.ActiveWindow;
}

/// <summary>A character's roleplay profile, as the Chronicle shows it.</summary>
public sealed record ProfileDto
{
    public Guid Id { get; init; }

    public Guid OwnerAccountId { get; init; }

    /// <summary>The character this profile belongs to, which is not necessarily the display name.</summary>
    public string CharacterName { get; init; } = string.Empty;

    public uint WorldId { get; init; }

    public string WorldName { get; init; } = string.Empty;

    public string DataCenter { get; init; } = string.Empty;

    public ProfileIdentity Identity { get; init; } = new();

    public ProfileStyle Style { get; init; } = new();

    public PlayerNotes Player { get; init; } = new();

    public ProfilePresence Presence { get; init; } = ProfilePresence.Unknown;

    public IReadOnlyList<PersonalityTrait> Personality { get; init; } = [];

    public IReadOnlyList<ProfileHook> Hooks { get; init; } = [];

    public IReadOnlyList<ProfileImage> Gallery { get; init; } = [];

    public IReadOnlyList<ProfileLink> Links { get; init; } = [];

    /// <summary>The 500 characters that appear on the card.</summary>
    public string? Overview { get; init; }

    /// <summary>Everything below the fold. Absent from search results.</summary>
    public string? History { get; init; }

    public string? Goals { get; init; }

    public Guid? PortraitImageId { get; init; }

    public ProfileVisibility Visibility { get; init; }

    public AvailabilityOverride Availability { get; init; }

    /// <summary>Short code for sharing a profile outside the plugin, as beacons have.</summary>
    public string ShareCode { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    [JsonIgnore]
    public bool HasPortrait => PortraitImageId is not null && PortraitImageId != Guid.Empty;

    /// <summary>"Wild - Wanderer - Independent".</summary>
    [JsonIgnore]
    public string ArchetypeLine => string.Join("  ·  ", Identity.Archetype);

    /// <summary>
    /// How complete the card is, 0 to 1.
    ///
    /// Shown as a meter while editing, and never as a gate. It exists because watching a card fill in
    /// is what actually persuades somebody to finish one; refusing to publish an incomplete profile
    /// just means they publish nothing.
    /// </summary>
    [JsonIgnore]
    public float Completeness
    {
        get
        {
            var earned = 0;
            const int Total = 8;

            if (HasPortrait) earned++;
            if (!string.IsNullOrWhiteSpace(Identity.Name)) earned++;
            if (Personality.Count > 0) earned++;
            if (Hooks.Count > 0) earned++;
            if (!string.IsNullOrWhiteSpace(Overview)) earned++;
            if (Style.Tones.Count > 0 || Style.Activities.Count > 0) earned++;
            if (Identity.Archetype.Count > 0 || !string.IsNullOrWhiteSpace(Identity.Quote)) earned++;
            if (Gallery.Count > 1) earned++;

            return earned / (float)Total;
        }
    }
}
