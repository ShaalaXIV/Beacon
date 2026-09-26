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

            // A failed fetch is retried, not remembered for ever.
            //
            // The commonest failure by far is asking before the plugin has finished connecting, or a
            // blink of network at login. Giving up permanently turned a half-second hiccup into a
            // picture that never appeared again until the game was restarted -- which is exactly what
            // it looked like from the outside: a portrait that would not stay saved.
            if (existing.Failed && !existing.Loading && DateTime.UtcNow >= existing.RetryAt)
            {
                existing.Failed = false;
                existing.Loading = true;
                _ = LoadAsync(key, existing);
            }

            return existing.Texture;
        }

        var entry = new Entry { LastTouched = DateTime.UtcNow, Loading = true };
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
                entry.RecordFailure();
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
            entry.Attempts = 0;
        }
        catch (OperationCanceledException)
        {
            // The plugin is unloading. That is not a failure to remember, and marking it one would
            // poison the entry for a cache that is about to be thrown away anyway.
        }
        catch (Exception ex)
        {
            entry.RecordFailure();
            Svc.Log.Warning(ex, "Could not load beacon image {ImageId}, attempt {Attempt}.", key.Id, entry.Attempts);
        }
        finally
        {
            entry.Loading = false;
        }
    }

    /// <summary>
    /// True when an image has failed enough times to say so out loud rather than claim it is loading.
    /// </summary>
    public bool Struggling(Guid imageId, bool thumb = true) =>
        entries.TryGetValue((imageId, thumb), out var entry) && entry.Attempts >= 3;

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

        /// <summary>Set while a fetch is in flight, so a retry cannot start a second one.</summary>
        public bool Loading { get; set; }

        /// <summary>How many times this image has failed in a row. Reset by a success.</summary>
        public int Attempts { get; set; }

        /// <summary>The earliest a failed entry may be tried again.</summary>
        public DateTime RetryAt { get; set; }

        public bool Disposed { get; private set; }

        /// <summary>
        /// Records a failure and backs off, so a genuinely missing image is not re-requested every
        /// frame while a transient one still recovers within a few seconds.
        /// </summary>
        public void RecordFailure()
        {
            Failed = true;
            Attempts++;
            RetryAt = DateTime.UtcNow.AddSeconds(Math.Min(60d, Math.Pow(2d, Math.Min(Attempts, 6))));
        }

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
