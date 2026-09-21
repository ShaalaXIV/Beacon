using System.Collections.Concurrent;
using Dalamud.Interface.Textures.TextureWraps;

namespace Beacon.Services;

/// <summary>
/// Downloads beacon screenshots and turns them into textures the atlas can draw, once each.
///
/// Bounded on purpose: browsing a large atlas would otherwise pull every screenshot ever uploaded
/// into video memory and never let go. Least-recently-drawn entries are evicted, which for a list the
/// player is scrolling is exactly the right thing to drop.
/// </summary>
public sealed class ImageCache(BeaconApi api) : IDisposable
{
    /// <summary>How many decoded textures to hold. Thumbnails are small; full views are the expensive ones.</summary>
    private const int Capacity = 96;

    private readonly ConcurrentDictionary<(Guid Id, bool Thumb), Entry> entries = new();

    private readonly CancellationTokenSource lifetime = new();

    /// <summary>
    /// Whether this machine can decode WebP.
    ///
    /// Dalamud decodes through Windows Imaging Component, and WebP support there comes from an optional
    /// OS component that is usually, but not always, present. Rather than show broken thumbnails on the
    /// machines that lack it, probe once and ask the server for PNG instead.
    /// </summary>
    private bool? webpSupported;

    /// <summary>
    /// The texture for an image, or null while it loads or if it failed.
    /// Safe to call every frame; the work happens once.
    /// </summary>
    public IDalamudTextureWrap? Get(Guid imageId, bool thumb = true)
    {
        if (imageId == Guid.Empty)
            return null;

        var key = (imageId, thumb);

        if (entries.TryGetValue(key, out var existing))
        {
            existing.LastTouched = DateTime.UtcNow;
            return existing.Texture;
        }

        var entry = new Entry { LastTouched = DateTime.UtcNow };
        if (!entries.TryAdd(key, entry))
            return entries.TryGetValue(key, out var raced) ? raced.Texture : null;

        _ = LoadAsync(key, entry);
        Trim();
        return null;
    }

    /// <summary>Drops a cached image so the next draw re-fetches it, after a screenshot is replaced.</summary>
    public void Invalidate(Guid imageId)
    {
        foreach (var thumb in new[] { true, false })
        {
            if (entries.TryRemove((imageId, thumb), out var entry))
                entry.Dispose();
        }
    }

    private async Task LoadAsync((Guid Id, bool Thumb) key, Entry entry)
    {
        try
        {
            var preferPng = !SupportsWebp();
            var bytes = await api.DownloadImageAsync(key.Id, key.Thumb, preferPng, lifetime.Token);

            if (bytes is null || bytes.Length == 0)
            {
                entry.Failed = true;
                return;
            }

            var texture = await Svc.Textures.CreateFromImageAsync(bytes, cancellationToken: lifetime.Token);

            // The entry may have been evicted or disposed while the download was in flight.
            if (entry.Disposed)
            {
                texture.Dispose();
                return;
            }

            entry.Texture = texture;
        }
        catch (OperationCanceledException)
        {
            entry.Failed = true;
        }
        catch (Exception ex)
        {
            entry.Failed = true;
            Svc.Log.Warning(ex, "Could not load beacon image {ImageId}.", key.Id);
        }
    }

    private bool SupportsWebp()
    {
        if (webpSupported is { } known)
            return known;

        try
        {
            var decoders = Svc.Textures.GetSupportedImageDecoderInfos();
            var supported = decoders.Any(d =>
                d.MimeTypes.Any(m => m.Contains("webp", StringComparison.OrdinalIgnoreCase))
                || d.Extensions.Any(e => e.Contains("webp", StringComparison.OrdinalIgnoreCase)));

            webpSupported = supported;

            if (!supported)
                Svc.Log.Information("Beacon: this machine cannot decode WebP; requesting PNG screenshots instead.");

            return supported;
        }
        catch (Exception ex)
        {
            // If the probe itself fails, assume the worst and ask for PNG, which always decodes.
            Svc.Log.Debug(ex, "Could not probe image decoder support.");
            webpSupported = false;
            return false;
        }
    }

    /// <summary>Evicts the least recently drawn entries once the cache is over capacity.</summary>
    private void Trim()
    {
        if (entries.Count <= Capacity)
            return;

        var stale = entries
            .OrderBy(e => e.Value.LastTouched)
            .Take(entries.Count - Capacity)
            .ToList();

        foreach (var (key, entry) in stale)
        {
            if (entries.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();

        foreach (var entry in entries.Values)
            entry.Dispose();

        entries.Clear();
        lifetime.Dispose();
    }

    private sealed class Entry : IDisposable
    {
        private IDalamudTextureWrap? texture;

        public DateTime LastTouched { get; set; }

        public bool Failed { get; set; }

        public bool Disposed { get; private set; }

        public IDalamudTextureWrap? Texture
        {
            get => texture;
            set
            {
                texture?.Dispose();
                texture = value;
            }
        }

        public void Dispose()
        {
            Disposed = true;
            texture?.Dispose();
            texture = null;
        }
    }
}
