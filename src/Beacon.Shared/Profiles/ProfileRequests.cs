namespace Beacon.Shared.Profiles;

/// <summary>
/// Creates or replaces the calling account's profile for one character.
///
/// An upsert rather than separate create and update calls: there is exactly one profile per character,
/// so "make my card look like this" is the only operation the editor ever needs, and it removes the
/// class of bug where the client has to know whether it is creating or editing.
/// </summary>
public sealed record SaveProfileRequest
{
    /// <summary>The character this card belongs to. Must be claimed by the calling account.</summary>
    public string CharacterName { get; init; } = string.Empty;

    public uint WorldId { get; init; }

    public string WorldName { get; init; } = string.Empty;

    public string DataCenter { get; init; } = string.Empty;

    public ProfileIdentity Identity { get; init; } = new();

    public ProfileStyle Style { get; init; } = new();

    public PlayerNotes Player { get; init; } = new();

    public IReadOnlyList<PersonalityTrait> Personality { get; init; } = [];

    /// <summary>Replaces the hook list wholesale. Order is the order given.</summary>
    public IReadOnlyList<string> Hooks { get; init; } = [];

    /// <summary>What a stranger notices first. Ordered as given.</summary>
    public IReadOnlyList<GlanceNote> AtFirstGlance { get; init; } = [];

    /// <summary>
    /// The out-of-character note and the IC/OOC flag. The live "currently" line is deliberately not
    /// here: it has its own route so it can be rewritten from a chat command without resubmitting a
    /// whole profile.
    /// </summary>
    public string? OutOfCharacter { get; init; }

    public RpStance Stance { get; init; }

    public string? Overview { get; init; }

    public string? History { get; init; }

    public string? Goals { get; init; }

    public ProfileVisibility Visibility { get; init; } = ProfileVisibility.Public;

    public AvailabilityOverride Availability { get; init; } = AvailabilityOverride.Derived;
}

/// <summary>
/// Confirms the account holder is an adult.
///
/// Required once before a card may declare any mature theme, or before a search may ask to see them.
/// Self-declared, as it must be -- the point is that it is a deliberate, recorded act rather than a
/// checkbox somebody can pass without noticing.
/// </summary>
public sealed record ConfirmAdultRequest
{
    public bool Confirmed { get; init; }
}

/// <summary>Sets just the availability override, for the one-click "I am busy" toggle.</summary>
/// <summary>
/// Rewrites the live line, and nothing else.
///
/// Its own route on purpose. The whole value of a "currently" is that updating it is a ten-second act
/// between scenes; making it a profile save means it is set once when the card is written and never
/// again, which is how this field dies in every tool that buries it in an editor.
/// </summary>
public sealed record SetCurrentlyRequest
{
    public string? Currently { get; init; }

    /// <summary>Optional: set the IC/OOC flag in the same breath.</summary>
    public RpStance? Stance { get; init; }
}

public sealed record SetAvailabilityRequest
{
    public AvailabilityOverride Availability { get; init; }
}

/// <summary>Reorders, recaptions or recategorises the gallery. The image bytes are uploaded separately.</summary>
public sealed record UpdateGalleryRequest
{
    public IReadOnlyList<GalleryEntry> Images { get; init; } = [];

    /// <summary>Which image is the portrait. Must be one of the gallery images, or null to clear it.</summary>
    public Guid? PortraitImageId { get; init; }

    public sealed record GalleryEntry
    {
        public Guid ImageId { get; init; }

        public GalleryCategory Category { get; init; }

        public string? Caption { get; init; }

        public int Order { get; init; }
    }
}

/// <summary>Claims a tie to another character. Takes effect one-sided until they confirm it.</summary>
public sealed record AddLinkRequest
{
    public Guid OtherProfileId { get; init; }

    public RelationshipKind Kind { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Reports that a character is standing at a lit beacon.
///
/// The position travels with it so the server can check it, exactly as it does for lighting. A
/// self-reported "I am active" that nobody verifies is a number people would quietly inflate, and the
/// whole value of the signal is that it means somebody was genuinely out there.
/// </summary>
public sealed record ActivityHeartbeatRequest
{
    /// <summary>The lit beacon the character is standing at.</summary>
    public Guid BeaconId { get; init; }

    public string CharacterName { get; init; } = string.Empty;

    public uint WorldId { get; init; }

    public ushort TerritoryId { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}
