namespace Beacon.Server.Data.Entities;

/// <summary>
/// One of the things a stranger notices before a word is exchanged.
///
/// A label and a line rather than a fixed set of physical fields. Height, build and eye colour make a
/// census entry; "Hands" / "Ink to the knuckles, badly done" makes somebody walk over.
/// </summary>
public class ProfileGlanceEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public ProfileEntity? Profile { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public int Order { get; set; }
}
