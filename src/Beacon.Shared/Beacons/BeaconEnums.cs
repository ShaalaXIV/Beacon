namespace Beacon.Shared.Beacons;

/// <summary>
/// The flavour of place a beacon marks. Purely cosmetic/filterable -- it drives the
/// icon and the tint used when the beacon is drawn, and gives the atlas something
/// meaningful to sort by beyond "everything is a dot on a map".
/// </summary>
public enum BeaconKind
{
    /// <summary>Anything that does not fit the other shapes.</summary>
    Waypoint = 0,

    /// <summary>A pitched camp: tents, bedrolls, a fire. The archetypal overworld beacon.</summary>
    Camp = 1,

    /// <summary>A tavern, inn or hearth -- somewhere to drink and talk.</summary>
    Hearth = 2,

    /// <summary>Trade: a market stall, caravan, or travelling merchant.</summary>
    Market = 3,

    /// <summary>A shrine, altar, or place of worship.</summary>
    Shrine = 4,

    /// <summary>Ruins, a dig site, or somewhere deliberately ominous.</summary>
    Ruins = 5,

    /// <summary>Wilderness: a hunt, a grove, a clearing far from any road.</summary>
    Wilds = 6,

    /// <summary>A performance space, arena, or somewhere with an audience.</summary>
    Stage = 7,

    /// <summary>A recurring meet: a guild muster, a court, a scheduled gathering.</summary>
    Gathering = 8,

    /// <summary>A homestead or settled residence out in the world.</summary>
    Homestead = 9,
}

/// <summary>Who is allowed to see a beacon in the atlas.</summary>
public enum BeaconVisibility
{
    /// <summary>Listed in the public atlas for everyone.</summary>
    Public = 0,

    /// <summary>Not listed, but reachable by anyone holding the share code.</summary>
    Unlisted = 1,

    /// <summary>Visible only to the owning account. Useful for scouting spots before you publish.</summary>
    Private = 2,
}

/// <summary>Ordering options for an atlas query.</summary>
public enum BeaconSort
{
    /// <summary>Lit beacons first, then most recently lit, then newest. The default "what is happening right now" view.</summary>
    Relevance = 0,

    /// <summary>Newest beacons first.</summary>
    Newest = 1,

    /// <summary>Most favourited first.</summary>
    Popular = 2,

    /// <summary>Alphabetical by name.</summary>
    Name = 3,

    /// <summary>Most recently lit first, regardless of current state.</summary>
    RecentlyLit = 4,
}

/// <summary>Why a beacon stopped being lit. Recorded so the owner can tell a manual snuff from a timeout.</summary>
public enum ExtinguishReason
{
    /// <summary>Somebody pressed the button.</summary>
    Manual = 0,

    /// <summary>The lit duration elapsed and the server swept it.</summary>
    Expired = 1,

    /// <summary>The person who lit it went offline / stopped sending heartbeats.</summary>
    Away = 2,
}
