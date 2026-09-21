using System.Text.RegularExpressions;
using Beacon.Shared.Beacons;

namespace Beacon.Server;

/// <summary>
/// Server-side input checks. These mirror <see cref="BeaconLimits"/>, which the plugin also enforces,
/// but they are the ones that actually matter: the client is not a trust boundary.
/// </summary>
public static partial class Validation
{
    /// <summary>
    /// Control characters that have no business in a display string. Newlines are allowed in
    /// descriptions and handled separately; everything else here is either invisible, a bidirectional
    /// override, or a zero-width character usable to spoof another person's name.
    /// </summary>
    [GeneratedRegex(@"[\p{Cc}\p{Cf}]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"[\p{Cc}-[\r\n]]|[\p{Cf}]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharactersAllowingNewlines();

    /// <summary>Collapses runs of whitespace and strips invisible characters from a single-line field.</summary>
    public static string CleanLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var stripped = ControlCharacters().Replace(value, string.Empty);
        return WhitespaceRuns().Replace(stripped, " ").Trim();
    }

    /// <summary>Cleans a multi-line field, preserving paragraph breaks but collapsing runaway blank lines.</summary>
    public static string CleanBlock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var stripped = ControlCharactersAllowingNewlines().Replace(value.Replace("\r\n", "\n"), string.Empty);
        return ExcessiveNewlines().Replace(stripped, "\n\n").Trim();
    }

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRuns();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessiveNewlines();

    /// <summary>Returns an error message, or null when the name is acceptable.</summary>
    public static string? ValidateBeaconName(string name) => name.Length switch
    {
        < BeaconLimits.NameMinLength => $"A beacon name needs at least {BeaconLimits.NameMinLength} characters.",
        > BeaconLimits.NameMaxLength => $"A beacon name cannot exceed {BeaconLimits.NameMaxLength} characters.",
        _ => null,
    };

    public static string? ValidateDisplayName(string name) => name.Length switch
    {
        < BeaconLimits.DisplayNameMinLength => $"A display name needs at least {BeaconLimits.DisplayNameMinLength} characters.",
        > BeaconLimits.DisplayNameMaxLength => $"A display name cannot exceed {BeaconLimits.DisplayNameMaxLength} characters.",
        _ => null,
    };

    public static string? ValidateDescription(string description) =>
        description.Length > BeaconLimits.DescriptionMaxLength
            ? $"A description cannot exceed {BeaconLimits.DescriptionMaxLength} characters."
            : null;

    public static string? ValidateNote(string? note) =>
        note is not null && note.Length > BeaconLimits.NoteMaxLength
            ? $"A note cannot exceed {BeaconLimits.NoteMaxLength} characters."
            : null;

    /// <summary>
    /// Checks a captured location is self-consistent. A zero territory means the plugin captured while
    /// the player was between zones, which would produce a beacon nobody can travel to.
    /// </summary>
    public static string? ValidateLocation(BeaconLocation? location)
    {
        if (location is null)
            return "A beacon needs a location.";

        if (location.TerritoryId == 0)
            return "That location has no zone. Try again once you have finished loading in.";

        if (!float.IsFinite(location.X) || !float.IsFinite(location.Y) || !float.IsFinite(location.Z))
            return "That location has invalid coordinates.";

        return null;
    }

    public static string? ValidateRealm(BeaconRealm? realm)
    {
        if (realm is null || realm.WorldId == 0)
            return "A beacon needs a world.";

        if (string.IsNullOrWhiteSpace(realm.WorldName))
            return "A beacon needs a world name.";

        return null;
    }

    /// <summary>Clamps a requested burn length into the allowed range rather than rejecting it.</summary>
    public static int ClampMinutes(int minutes) =>
        Math.Clamp(minutes, BeaconLimits.MinLitMinutes, BeaconLimits.MaxLitMinutes);
}
