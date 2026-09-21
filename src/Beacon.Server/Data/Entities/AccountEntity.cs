namespace Beacon.Server.Data.Entities;

/// <summary>
/// A person. Characters and beacons hang off this so that changing character, world or data centre
/// never orphans what you have published -- and so roleplay profiles have somewhere to live later.
/// </summary>
public class AccountEntity
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Base64 SHA-256 of the account's secret key. A plain hash is correct here: the key is 256 bits of
    /// CSPRNG output, not a human-chosen password, so there is no dictionary to stretch against.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>May resolve reports and remove other people's beacons.</summary>
    public bool IsModerator { get; set; }

    /// <summary>Blocked from writing. Reads still work, so a ban is not silently confusing.</summary>
    public bool IsBanned { get; set; }

    /// <summary>
    /// When the account holder confirmed they are an adult.
    ///
    /// Null until they do. Required before a card may declare any mature theme, and before a search
    /// may ask to see them. Checked on the server, because a confirmation the client could skip would
    /// be decoration rather than a gate.
    /// </summary>
    public DateTimeOffset? AdultConfirmedAt { get; set; }

    public List<CharacterEntity> Characters { get; set; } = [];

    public List<BeaconEntity> Beacons { get; set; } = [];
}
