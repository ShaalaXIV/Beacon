using Beacon.Shared.Profiles;

namespace Beacon.UI;

/// <summary>Player-facing wording for the Chronicle's enums, kept out of the wire contracts.</summary>
public static class ProfileLabels
{
    public static string Describe(PersonalityTrait trait) => trait switch
    {
        PersonalityTrait.Honourable => "Honourable",
        PersonalityTrait.Intellectual => "Intellectual",
        PersonalityTrait.Manipulative => "Manipulative",
        PersonalityTrait.Flirtatious => "Flirtatious",
        PersonalityTrait.Traditional => "Traditional",
        PersonalityTrait.Rebellious => "Rebellious",
        PersonalityTrait.Mysterious => "Mysterious",
        PersonalityTrait.Protective => "Protective",
        PersonalityTrait.Optimistic => "Optimistic",
        PersonalityTrait.Aggressive => "Aggressive",
        PersonalityTrait.Ambitious => "Ambitious",
        PersonalityTrait.Sarcastic => "Sarcastic",
        PersonalityTrait.Reserved => "Reserved",
        PersonalityTrait.Cynical => "Cynical",
        PersonalityTrait.Curious => "Curious",
        PersonalityTrait.Chaotic => "Chaotic",
        PersonalityTrait.Playful => "Playful",
        PersonalityTrait.Gentle => "Gentle",
        PersonalityTrait.Stoic => "Stoic",
        PersonalityTrait.Loyal => "Loyal",
        PersonalityTrait.Wild => "Wild",
        _ => "Kind",
    };

    public static string Describe(RpLength length) => length switch
    {
        RpLength.QuickScenes => "Quick scenes",
        RpLength.LongForm => "Long form",
        RpLength.Campaign => "Campaign",
        _ => "Casual",
    };

    public static string Hint(RpLength length) => length switch
    {
        RpLength.QuickScenes => "A passing encounter. In and out in an evening.",
        RpLength.LongForm => "Threads that run for weeks and remember what happened.",
        RpLength.Campaign => "An ongoing story with a cast and a direction.",
        _ => "Somewhere in between. Most evenings, most people.",
    };

    public static string Describe(RpTone tone) => tone switch
    {
        RpTone.DarkFantasy => "Dark fantasy",
        RpTone.SliceOfLife => "Slice of life",
        RpTone.Lighthearted => "Lighthearted",
        RpTone.Political => "Political",
        RpTone.Adventure => "Adventure",
        RpTone.Mystery => "Mystery",
        RpTone.Romance => "Romance",
        RpTone.Comedy => "Comedy",
        RpTone.Horror => "Horror",
        _ => "Drama",
    };

    public static string Describe(RpActivity activity) => activity switch
    {
        RpActivity.CharacterDevelopment => "Character growth",
        RpActivity.Investigation => "Investigation",
        RpActivity.Relationship => "Relationships",
        RpActivity.Exploration => "Exploration",
        RpActivity.Crafting => "Crafting",
        RpActivity.Combat => "Combat",
        RpActivity.Social => "Social",
        RpActivity.Event => "Events",
        _ => "Tavern",
    };

    public static string Describe(MatureTheme theme) => theme switch
    {
        MatureTheme.Explicit => "Explicit scenes",
        MatureTheme.GraphicViolence => "Graphic violence",
        MatureTheme.HeavyThemes => "Heavy themes",
        _ => "Adult themes",
    };

    /// <summary>
    /// Wording matters more here than anywhere else on the card.
    ///
    /// Every one of these is phrased as willingness within a story, because that is what it is. A tag
    /// that reads as an advertisement turns a directory of characters into something else entirely.
    /// </summary>
    public static string Hint(MatureTheme theme) => theme switch
    {
        MatureTheme.Explicit =>
            "Willing to write explicit scenes where the story has led there and both players have\nagreed. "
            + "This is not an invitation, and nobody should read it as one.",
        MatureTheme.GraphicViolence =>
            "Injury, torture and the aftermath of a fight, described rather than glossed over.",
        MatureTheme.HeavyThemes =>
            "Grief, abuse, addiction, self-destruction. Handled with care, but not avoided.",
        _ =>
            "Sex work, drink, drugs, the seedier corners of the world, as subject matter rather\nthan as a scene. "
            + "Plenty of people want this and want nothing to do with the explicit tag.",
    };

    public static string Describe(AgeRange age) => age switch
    {
        AgeRange.YoungAdult => "Young adult",
        AgeRange.Adult => "Adult",
        AgeRange.MiddleAged => "Middle-aged",
        AgeRange.Elder => "Elder",
        AgeRange.Ageless => "Ageless",
        _ => "Unsaid",
    };

    public static string Describe(GalleryCategory category) => category switch
    {
        GalleryCategory.FullBody => "Full body",
        GalleryCategory.Outfit => "Outfit",
        GalleryCategory.Other => "Other",
        _ => "Portrait",
    };

    public static string Describe(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Organisation => "Organisation",
        RelationshipKind.Complicated => "Complicated",
        RelationshipKind.Partner => "Partner",
        RelationshipKind.Student => "Student",
        RelationshipKind.Mentor => "Mentor",
        RelationshipKind.Family => "Family",
        RelationshipKind.Enemy => "Enemy",
        RelationshipKind.Rival => "Rival",
        RelationshipKind.Ally => "Ally",
        _ => "Friend",
    };

    public static string Describe(ProfileVisibility visibility) => visibility switch
    {
        ProfileVisibility.Unlisted => "Unlisted",
        ProfileVisibility.Private => "Private",
        _ => "Public",
    };

    public static string Hint(ProfileVisibility visibility) => visibility switch
    {
        ProfileVisibility.Unlisted => "Not in the Chronicle. Anyone with the share code can still read it.",
        ProfileVisibility.Private => "Only you. Useful while the card is still a draft.",
        _ => "Anybody searching the Chronicle can find this card.",
    };

    public static string Describe(AvailabilityOverride availability) => availability switch
    {
        AvailabilityOverride.Busy => "Busy",
        AvailabilityOverride.StorySession => "In a story session",
        AvailabilityOverride.Closed => "Not looking",
        _ => "Follow my beacons",
    };

    public static string Hint(AvailabilityOverride availability) => availability switch
    {
        AvailabilityOverride.Busy => "In the world, but not taking walk-ups.",
        AvailabilityOverride.StorySession => "Already in a scene with somebody.",
        AvailabilityOverride.Closed => "Not looking for anything at the moment.",
        _ => "Open when one of your beacons is lit, quiet when none are. Nothing to remember.",
    };

    public static string Describe(AvailabilityState state) => state switch
    {
        AvailabilityState.OpenToWalkups => "Open to walk-ups",
        AvailabilityState.StorySession => "In a story session",
        AvailabilityState.Busy => "Busy",
        AvailabilityState.Closed => "Not looking",
        _ => "No beacon lit",
    };

    public static string Describe(ProfileSort sort) => sort switch
    {
        ProfileSort.RecentlyActive => "Recently seen",
        ProfileSort.Newest => "Newest",
        ProfileSort.Name => "By name",
        _ => "Who is about",
    };

    /// <summary>
    /// How long ago somebody was last seen at a fire, in the words a person would use.
    ///
    /// Deliberately coarse. The useful question is "is this community alive", not "where exactly was
    /// this person at 14:32", and a vaguer answer is also a kinder one to the person being described.
    /// </summary>
    public static string LastSeen(DateTimeOffset? lastActive)
    {
        if (lastActive is not { } seen)
            return "Not yet seen out";

        var span = DateTimeOffset.UtcNow - seen;

        return span.TotalMinutes switch
        {
            < 15 => "Out right now",
            < 90 => "Seen within the hour",
            < 60 * 12 => "Seen today",
            < 60 * 36 => "Seen yesterday",
            < 60 * 24 * 7 => $"Seen {(int)span.TotalDays} days ago",
            < 60 * 24 * 14 => "Seen last week",
            < 60 * 24 * 60 => $"Seen {(int)(span.TotalDays / 7)} weeks ago",
            _ => "Not seen in months",
        };
    }
}
