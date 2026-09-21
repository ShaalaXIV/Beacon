using Compass.Shared.Profiles;

namespace Compass.Server.Data.Entities;

/// <summary>
/// A character's roleplay profile.
///
/// One per character, owned by an account, so that rerolling or transferring never orphans a card and
/// one person can keep several characters under a single key.
/// </summary>
public class ProfileEntity
{
    public Guid Id { get; set; }

    public Guid OwnerAccountId { get; set; }

    public AccountEntity? Owner { get; set; }

    // --- Which character this is ----------------------------------------

    public string CharacterName { get; set; } = string.Empty;

    public uint WorldId { get; set; }

    public string WorldName { get; set; } = string.Empty;

    public string DataCenter { get; set; } = string.Empty;

    // --- Identity --------------------------------------------------------

    /// <summary>The name on the card, which need not be the login name.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Race { get; set; }

    public string? Clan { get; set; }

    public AgeRange Age { get; set; }

    public string? Gender { get; set; }

    public string? Pronouns { get; set; }

    /// <summary>The three archetype words, pipe-delimited. Not searched, so no join table needed.</summary>
    public string ArchetypeCsv { get; set; } = string.Empty;

    public string? Quote { get; set; }

    // --- Style -----------------------------------------------------------

    public RpLength Length { get; set; }

    public string? Boundaries { get; set; }

    public bool WalkupsWelcome { get; set; } = true;

    public bool IsMature { get; set; }

    // --- The player behind the character ---------------------------------

    public string? PlayerTimezone { get; set; }

    public string? PlayerAvailability { get; set; }

    public string? PlayerContact { get; set; }

    // --- Prose -----------------------------------------------------------

    public string? Overview { get; set; }

    public string? History { get; set; }

    public string? Goals { get; set; }

    // --- Presentation ----------------------------------------------------

    public Guid? PortraitImageId { get; set; }

    public ProfileVisibility Visibility { get; set; }

    public AvailabilityOverride Availability { get; set; }

    public string ShareCode { get; set; } = string.Empty;

    /// <summary>
    /// The last time this character was seen standing at a lit beacon.
    ///
    /// Written by the heartbeat endpoint after it verifies the position, so it means "was genuinely
    /// out roleplaying" rather than "had the launcher open". Indexed, because sorting the Chronicle
    /// by who is actually around is the default view.
    /// </summary>
    public DateTimeOffset? LastActiveAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public List<ProfileTagEntity> Tags { get; set; } = [];

    public List<ProfileHookEntity> Hooks { get; set; } = [];

    public List<ProfileImageEntity> Images { get; set; } = [];
}
