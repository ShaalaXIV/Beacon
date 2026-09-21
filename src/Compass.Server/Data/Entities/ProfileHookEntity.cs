namespace Compass.Server.Data.Entities;

/// <summary>
/// A reason to walk over and speak to somebody.
///
/// Free prose rather than a fixed vocabulary, because hooks are the part of a card that actually
/// distinguishes one character from another. Searched as text, not faceted.
/// </summary>
public class ProfileHookEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public ProfileEntity? Profile { get; set; }

    public string Text { get; set; } = string.Empty;

    public int Order { get; set; }
}
