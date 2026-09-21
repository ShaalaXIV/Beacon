using Beacon.Shared.Profiles;

namespace Beacon.Server.Data.Entities;

/// <summary>
/// One searchable tag on a profile: a personality trait, a tone, an activity or a length.
///
/// A join table rather than the pipe-delimited column beacons use. Beacon tags are few and queried one
/// at a time; profile search is an intersection -- "dark fantasy AND long form AND protective" -- and
/// chaining LIKE '%|x|%' for each term scans the whole table every time. Indexed on (Kind, Value),
/// this is an ordinary join.
/// </summary>
public class ProfileTagEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public ProfileEntity? Profile { get; set; }

    public ProfileTagKind Kind { get; set; }

    /// <summary>The enum value for <see cref="Kind"/>, stored as an int.</summary>
    public int Value { get; set; }
}
