using Beacon.Shared.Profiles;

namespace Beacon.Server.Data.Entities;

/// <summary>
/// A claimed tie between two characters.
///
/// Stored one-directional and unconfirmed until the other party agrees. Anybody can otherwise write
/// "Rival" onto a stranger's card, which is a way to harass someone rather than a feature; an
/// unconfirmed tie is shown only to the person who claimed it, and marked as one-sided.
/// </summary>
public class ProfileLinkEntity
{
    public Guid Id { get; set; }

    /// <summary>The profile that claimed the tie.</summary>
    public Guid ProfileId { get; set; }

    public ProfileEntity? Profile { get; set; }

    public Guid OtherProfileId { get; set; }

    public RelationshipKind Kind { get; set; }

    public string? Note { get; set; }

    /// <summary>Set when the other party agreed. Null means the claim is one-sided.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
