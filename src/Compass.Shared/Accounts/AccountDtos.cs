namespace Compass.Shared.Accounts;

/// <summary>
/// A person, not a character. Beacons and (later) roleplay profiles hang off this, so that rerolling
/// an alt or moving worlds does not orphan everything you have published.
/// </summary>
public sealed record AccountDto
{
    public Guid Id { get; init; }

    /// <summary>How the owner wants to be credited on their beacons.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Characters claimed by this account.</summary>
    public IReadOnlyList<CharacterDto> Characters { get; init; } = [];

    /// <summary>How many beacons this account currently owns, for the quota display.</summary>
    public int BeaconCount { get; init; }

    /// <summary>True for accounts that may act on reports and remove other people's beacons.</summary>
    public bool IsModerator { get; init; }

    /// <summary>True once the account holder has confirmed they are an adult.</summary>
    public bool AdultConfirmed { get; init; }
}

/// <summary>A character claimed by an account.</summary>
public sealed record CharacterDto
{
    public string Name { get; init; } = string.Empty;

    public uint WorldId { get; init; }

    public string WorldName { get; init; } = string.Empty;

    public string DataCenter { get; init; } = string.Empty;

    public DateTimeOffset LinkedAt { get; init; }

    /// <summary>"Shaala Xiv @ Balmung", the form used everywhere a character is named.</summary>
    public override string ToString() => $"{Name} @ {WorldName}";
}

/// <summary>Create a brand new account. The only unauthenticated write the server accepts.</summary>
public sealed record RegisterAccountRequest
{
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// The one and only time the server hands back a secret key in plaintext. It is stored hashed,
/// so a lost key cannot be recovered -- only the holder can ever authenticate again.
/// </summary>
public sealed record RegisterAccountResponse
{
    public AccountDto Account { get; init; } = new();

    /// <summary>Save this. It is the password, it is shown once, and it cannot be reissued.</summary>
    public string SecretKey { get; init; } = string.Empty;
}

/// <summary>Change account-level settings.</summary>
public sealed record UpdateAccountRequest
{
    public string? DisplayName { get; init; }
}

/// <summary>
/// Attach a character to the calling account, so beacons can show who published them and so a future
/// roleplay profile has a character to hang off.
/// </summary>
public sealed record LinkCharacterRequest
{
    public string Name { get; init; } = string.Empty;

    public uint WorldId { get; init; }

    public string WorldName { get; init; } = string.Empty;

    public string DataCenter { get; init; } = string.Empty;
}
