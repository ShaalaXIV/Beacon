namespace Beacon.Shared.Profiles;

/// <summary>
/// Personality traits, as a fixed vocabulary.
///
/// Deliberately not free text. Let people type their own and you get "sarcastic", "Sarcastic",
/// "sarcasm" and "snarky" as four different values, and no filter ever matches anything. The whole
/// point of these is that somebody can search for them.
/// </summary>
public enum PersonalityTrait
{
    Wild = 0,
    Kind = 1,
    Sarcastic = 2,
    Protective = 3,
    Honourable = 4,
    Manipulative = 5,
    Playful = 6,
    Reserved = 7,
    Flirtatious = 8,
    Aggressive = 9,
    Curious = 10,
    Intellectual = 11,
    Chaotic = 12,
    Traditional = 13,
    Rebellious = 14,
    Mysterious = 15,
    Loyal = 16,
    Cynical = 17,
    Optimistic = 18,
    Stoic = 19,
    Ambitious = 20,
    Gentle = 21,
}

/// <summary>How long a scene someone wants.</summary>
public enum RpLength
{
    QuickScenes = 0,
    Casual = 1,
    LongForm = 2,
    Campaign = 3,
}

/// <summary>The flavour of story someone enjoys.</summary>
public enum RpTone
{
    Lighthearted = 0,
    Comedy = 1,
    Adventure = 2,
    Drama = 3,
    DarkFantasy = 4,
    Political = 5,
    Romance = 6,
    Mystery = 7,
    Horror = 8,
    SliceOfLife = 9,
}

/// <summary>What someone actually likes doing in a scene.</summary>
public enum RpActivity
{
    Tavern = 0,
    Combat = 1,
    Exploration = 2,
    Social = 3,
    Event = 4,
    CharacterDevelopment = 5,
    Relationship = 6,
    Crafting = 7,
    Investigation = 8,
}

/// <summary>
/// Adult content somebody is <em>willing</em> to play, when a story leads there.
///
/// Willingness, not solicitation. This exists because adult material genuinely arises in long-form
/// roleplay and people need to match expectations before it does, not because the Chronicle is a
/// place to look for it -- which is why there is no "seeking" counterpart and no way to search for
/// these without having opted into seeing them at all.
///
/// Split into several flags rather than one NSFW switch because people's limits differ along these
/// axes independently: plenty of players will write a character's addiction or a battlefield in
/// detail and want nothing to do with explicit scenes, and the reverse is just as common.
/// </summary>
public enum MatureTheme
{
    /// <summary>Adult subject matter handled in narrative: sex work, drink, drugs, the seedier world.</summary>
    AdultThemes = 0,

    /// <summary>Explicit scenes, where the story has led there and both players have agreed.</summary>
    Explicit = 1,

    /// <summary>Violence described in detail: injury, torture, the aftermath of a fight.</summary>
    GraphicViolence = 2,

    /// <summary>Heavy material: grief, abuse, addiction, self-destruction.</summary>
    HeavyThemes = 3,
}

/// <summary>
/// Which kind of tag a row in the tag table holds.
///
/// One table for every dimension, rather than four near-identical tables. The search that matters
/// crosses dimensions -- "dark fantasy AND long form AND protective" -- and that is one join here
/// instead of three.
/// </summary>
public enum ProfileTagKind
{
    Personality = 0,
    Length = 1,
    Tone = 2,
    Activity = 3,
    Mature = 4,
}

/// <summary>
/// A manual override on top of the availability Beacon works out for itself.
///
/// <see cref="Derived"/> is the default and the point of the design: availability comes from whether
/// your beacon is lit, so it cannot go stale. The others exist for being in-world but not open.
/// </summary>
public enum AvailabilityOverride
{
    /// <summary>Work it out from my beacons. The default.</summary>
    Derived = 0,

    /// <summary>In-world but not taking walk-ups.</summary>
    Busy = 1,

    /// <summary>In a planned scene with someone already.</summary>
    StorySession = 2,

    /// <summary>Not looking for anything at the moment.</summary>
    Closed = 3,
}

/// <summary>What the atlas should show about someone right now. Computed, never stored.</summary>
public enum AvailabilityState
{
    /// <summary>Nothing lit, nothing said.</summary>
    Unknown = 0,

    /// <summary>A beacon of theirs is burning and they are open to walk-ups.</summary>
    OpenToWalkups = 1,

    Busy = 2,
    StorySession = 3,
    Closed = 4,
}

/// <summary>
/// Gallery slots. Four, not nine.
///
/// A longer list reads as a checklist of chores; people fill two and leave the rest empty, which makes
/// the gallery look abandoned. Captions carry anything finer than this.
/// </summary>
public enum GalleryCategory
{
    Portrait = 0,
    FullBody = 1,
    Outfit = 2,
    Other = 3,
}

/// <summary>How two characters know each other.</summary>
public enum RelationshipKind
{
    Friend = 0,
    Partner = 1,
    Family = 2,
    Rival = 3,
    Enemy = 4,
    Ally = 5,
    Mentor = 6,
    Student = 7,
    Organisation = 8,
    Complicated = 9,
}

/// <summary>Who may see a profile. Mirrors beacon visibility so the two behave the same way.</summary>
public enum ProfileVisibility
{
    Public = 0,

    /// <summary>Not in search results, but readable by anyone given the link.</summary>
    Unlisted = 1,

    Private = 2,
}

/// <summary>Ordering for a profile search.</summary>
public enum ProfileSort
{
    /// <summary>Open to walk-ups first, then most recently out roleplaying. The default.</summary>
    Relevance = 0,

    RecentlyActive = 1,
    Newest = 2,
    Name = 3,
}

/// <summary>
/// Whether the player behind a character is playing them right now.
///
/// Borrowed from the addon culture that has had this for years: the difference between "I am in
/// character, approach me as one" and "I am here but out of character" is the single most useful
/// thing a card can say, and it is the one thing a static profile cannot express.
/// </summary>
public enum RpStance
{
    /// <summary>Not said either way. The honest default, and what every existing card has.</summary>
    Unstated = 0,

    InCharacter = 1,

    OutOfCharacter = 2,
}
