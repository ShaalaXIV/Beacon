namespace Compass.Shared.Beacons;

/// <summary>
/// The single source of truth for field limits, shared by the plugin's editor and the server's validator.
///
/// These live in shared code on purpose: when the client and server disagree about limits, the user
/// composes a perfectly good beacon, hits publish, and gets a rejection they cannot act on.
/// </summary>
public static class BeaconLimits
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 60;

    public const int DescriptionMaxLength = 1200;

    /// <summary>The line attached when lighting a flame. Short enough to read at a glance in the atlas.</summary>
    public const int NoteMaxLength = 140;

    public const int MaxTags = 8;
    public const int TagMaxLength = 24;

    /// <summary>Owner display name, which is usually a character name but need not be.</summary>
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 32;

    /// <summary>How close you must stand to light a beacon, in yalms. Generous enough to cover a whole camp.</summary>
    public const float LightingRangeYalms = 50f;

    /// <summary>Shortest flame you can light.</summary>
    public const int MinLitMinutes = 15;

    /// <summary>
    /// Longest flame you can light in one go. Capped so that forgetting to snuff a beacon cannot leave
    /// the atlas advertising an empty field for a week.
    /// </summary>
    public const int MaxLitMinutes = 480;

    public const int DefaultLitMinutes = 120;

    /// <summary>Largest screenshot accepted before re-encoding, in bytes.</summary>
    public const int MaxImageUploadBytes = 12 * 1024 * 1024;

    /// <summary>Longest edge of a stored screenshot, in pixels. Anything larger is downscaled.</summary>
    public const int ImageMaxEdge = 1280;

    /// <summary>Longest edge of the atlas thumbnail.</summary>
    public const int ThumbnailMaxEdge = 320;

    /// <summary>How many beacons a single account may own. A limit, not a target.</summary>
    public const int MaxBeaconsPerAccount = 50;

    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    public const int ReportReasonMaxLength = 500;
}
