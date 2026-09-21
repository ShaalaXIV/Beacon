using Compass.Shared.Beacons;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Compass.Server.Services;

/// <summary>Result of storing a screenshot.</summary>
public sealed record StoredImage(Guid Id, int Width, int Height, long SizeBytes, string ContentType);

/// <summary>
/// Turns whatever the player uploaded into two predictable WebP files: a full-size view and an atlas
/// thumbnail. Re-encoding rather than passing the bytes through is deliberate. It normalises the
/// format, bounds the size, and strips metadata, which on a screenshot can carry more than intended.
/// </summary>
public sealed class ImageService(
    IOptions<CompassOptions> options,
    IHostEnvironment env,
    ILogger<ImageService> logger)
{
    private readonly CompassOptions config = options.Value;

    private string Root => config.ImageDirectory(env.ContentRootPath);

    /// <summary>
    /// Decodes, downscales and stores an upload.
    /// Returns null when the payload is not a readable image, or is implausibly large.
    /// </summary>
    public async Task<StoredImage?> StoreAsync(Stream source, CancellationToken ct)
    {
        // Buffer to a seekable stream so the header can be identified before committing to a full decode.
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (buffer.Length == 0 || buffer.Length > config.MaxImageUploadBytes)
            return null;

        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(buffer, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Upload rejected: not a readable image.");
            return null;
        }

        // Guard before decoding. File size says nothing about decoded pixel count, and a small file
        // that expands to hundreds of megapixels is the classic way to take a server down.
        var pixels = (long)info.Width * info.Height;
        if (pixels <= 0 || pixels > config.MaxImagePixels)
        {
            logger.LogWarning("Upload rejected: {Width}x{Height} exceeds the pixel budget.", info.Width, info.Height);
            return null;
        }

        buffer.Position = 0;

        Image image;
        try
        {
            image = await Image.LoadAsync(buffer, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Upload rejected: decode failed.");
            return null;
        }

        using (image)
        {
            // Honour the orientation tag, then drop metadata entirely.
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var id = Guid.NewGuid();
            var encoder = new WebpEncoder { Quality = config.ImageQuality };

            Directory.CreateDirectory(DirectoryFor(id));

            using (var full = image.Clone(x => Fit(x, image.Width, image.Height, BeaconLimits.ImageMaxEdge)))
            {
                await using var stream = File.Create(PathFor(id, thumb: false));
                await full.SaveAsync(stream, encoder, ct);
            }

            using (var thumb = image.Clone(x => Fit(x, image.Width, image.Height, BeaconLimits.ThumbnailMaxEdge)))
            {
                await using var stream = File.Create(PathFor(id, thumb: true));
                await thumb.SaveAsync(stream, encoder, ct);
            }

            var size = new FileInfo(PathFor(id, thumb: false)).Length;

            // Report the stored dimensions, not the uploaded ones, so clients can lay out before download.
            var (width, height) = FitDimensions(image.Width, image.Height, BeaconLimits.ImageMaxEdge);
            return new StoredImage(id, width, height, size, "image/webp");
        }
    }

    /// <summary>Full path to a stored variant. Callers must check existence; files can vanish out of band.</summary>
    public string PathFor(Guid id, bool thumb) =>
        Path.Combine(DirectoryFor(id), thumb ? $"{id:N}.thumb.webp" : $"{id:N}.webp");

    /// <summary>
    /// Returns a PNG rendering of a stored variant, transcoding and caching it on first request.
    ///
    /// Screenshots are stored as WebP because it is a third the size, but WebP decoding on Windows
    /// comes from an optional OS component that is not always present. Rather than show those players
    /// broken thumbnails, transcode once on demand and serve the PNG from then on.
    /// </summary>
    public async Task<string?> EnsurePngAsync(Guid id, bool thumb, CancellationToken ct)
    {
        var source = PathFor(id, thumb);
        if (!File.Exists(source))
            return null;

        var target = PngPathFor(id, thumb);
        if (File.Exists(target))
            return target;

        try
        {
            using var image = await Image.LoadAsync(source, ct);
            await using var stream = File.Create(target);
            await image.SaveAsPngAsync(stream, ct);
            return target;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not transcode image {ImageId} to PNG.", id);

            // A half-written file would be served forever; remove it so the next request retries.
            TryDelete(target);
            return null;
        }
    }

    /// <summary>Path of the transcoded PNG for a variant.</summary>
    public string PngPathFor(Guid id, bool thumb) =>
        Path.Combine(DirectoryFor(id), thumb ? $"{id:N}.thumb.png" : $"{id:N}.png");

    /// <summary>Removes both variants. Safe to call for an id that was never stored.</summary>
    public void Delete(Guid id)
    {
        foreach (var thumb in new[] { false, true })
        {
            TryDelete(PathFor(id, thumb));
            TryDelete(PngPathFor(id, thumb));
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // Losing the file is a disk-space problem, not a correctness one; the row is already gone.
            logger.LogWarning(ex, "Could not delete {Path}.", path);
        }
    }

    /// <summary>
    /// Shards by the first two hex characters of the id. Flat directories holding tens of thousands of
    /// files get slow to enumerate on most filesystems, and unpleasant to back up.
    /// </summary>
    private string DirectoryFor(Guid id) => Path.Combine(Root, id.ToString("N")[..2]);

    private static void Fit(IImageProcessingContext context, int width, int height, int maxEdge)
    {
        var (targetWidth, targetHeight) = FitDimensions(width, height, maxEdge);
        if (targetWidth != width || targetHeight != height)
            context.Resize(targetWidth, targetHeight);
    }

    /// <summary>Scales to fit inside a square of <paramref name="maxEdge"/>, never enlarging.</summary>
    private static (int Width, int Height) FitDimensions(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxEdge)
            return (width, height);

        var scale = (double)maxEdge / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
