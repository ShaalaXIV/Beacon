using Compass.Shared.Beacons;

namespace Compass.UI;

/// <summary>
/// Player-facing wording for the enums.
///
/// Kept apart from the shared contracts so that renaming a label is a UI change, never a wire change,
/// and so the server never has to care what a kind is called this month.
/// </summary>
public static class BeaconLabels
{
    public static string Describe(BeaconKind kind) => kind switch
    {
        BeaconKind.Camp => "Camp",
        BeaconKind.Hearth => "Hearth",
        BeaconKind.Market => "Market",
        BeaconKind.Shrine => "Shrine",
        BeaconKind.Ruins => "Ruins",
        BeaconKind.Wilds => "Wilds",
        BeaconKind.Stage => "Stage",
        BeaconKind.Gathering => "Gathering",
        BeaconKind.Homestead => "Homestead",
        _ => "Waypoint",
    };

    /// <summary>A line of guidance shown beside the picker, so the choice means something.</summary>
    public static string Hint(BeaconKind kind) => kind switch
    {
        BeaconKind.Camp => "Tents, bedrolls, a fire. Somewhere pitched rather than built.",
        BeaconKind.Hearth => "A tavern, inn or hearth. Somewhere to drink and talk.",
        BeaconKind.Market => "A stall, caravan or travelling merchant.",
        BeaconKind.Shrine => "A shrine, altar or place of worship.",
        BeaconKind.Ruins => "Ruins, a dig site, or somewhere deliberately ominous.",
        BeaconKind.Wilds => "A hunt, a grove, a clearing far from any road.",
        BeaconKind.Stage => "A performance space, an arena, somewhere with an audience.",
        BeaconKind.Gathering => "A recurring muster: a court, a guild meet, a scheduled gathering.",
        BeaconKind.Homestead => "A settled residence out in the world.",
        _ => "Anything that does not fit the other shapes.",
    };

    public static string Describe(BeaconVisibility visibility) => visibility switch
    {
        BeaconVisibility.Unlisted => "Unlisted",
        BeaconVisibility.Private => "Private",
        _ => "Public",
    };

    public static string Hint(BeaconVisibility visibility) => visibility switch
    {
        BeaconVisibility.Unlisted => "Not in the atlas. Anyone with the share code can still find it.",
        BeaconVisibility.Private => "Only you can see it. Useful while you are still scouting the spot.",
        _ => "Listed in the atlas for everyone.",
    };

    public static string Describe(BeaconSort sort) => sort switch
    {
        BeaconSort.Newest => "Newest",
        BeaconSort.Popular => "Most starred",
        BeaconSort.Name => "By name",
        BeaconSort.RecentlyLit => "Recently lit",
        _ => "What is alive",
    };
}
