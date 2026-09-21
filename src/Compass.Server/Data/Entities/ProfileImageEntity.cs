using Compass.Shared.Profiles;

namespace Compass.Server.Data.Entities;

/// <summary>
/// One image in a profile's reference gallery.
///
/// The bytes live in the same store beacon screenshots use, so the gallery inherits the resizing,
/// format normalisation, metadata stripping and decompression-bomb guard already built for those.
/// </summary>
public class ProfileImageEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public ProfileEntity? Profile { get; set; }

    /// <summary>Key into the shared image store.</summary>
    public Guid ImageId { get; set; }

    public GalleryCategory Category { get; set; }

    public string? Caption { get; set; }

    public int Order { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
