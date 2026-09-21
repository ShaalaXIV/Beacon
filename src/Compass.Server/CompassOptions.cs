using Compass.Shared.Beacons;

namespace Compass.Server;

/// <summary>Server configuration, bound from the "Compass" section of appsettings.</summary>
public sealed class CompassOptions
{
    public const string SectionName = "Compass";

    /// <summary>
    /// Where the SQLite database and uploaded screenshots live. Relative paths resolve against the
    /// content root. Back this one directory up and you have backed up the whole service.
    ///
    /// Named "var" rather than "data" deliberately. Windows paths are case-insensitive, so a runtime
    /// "data" folder beside the source "Data" folder is not a near miss -- it is literally the same
    /// directory, and the database ends up inside the source tree where a clean or a glob delete will
    /// take it with them.
    /// </summary>
    public string DataDirectory { get; set; } = "var";

    /// <summary>
    /// Whether anyone may create an account. Turn off to freeze membership on a private instance:
    /// existing keys keep working, new ones cannot be minted.
    /// </summary>
    public bool RegistrationOpen { get; set; } = true;

    /// <summary>Beacons one account may own.</summary>
    public int MaxBeaconsPerAccount { get; set; } = BeaconLimits.MaxBeaconsPerAccount;

    /// <summary>How often the sweeper looks for flames that have burnt out.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Largest image accepted, before re-encoding. Also enforced as a request body size limit, so an
    /// oversized upload is rejected before it is buffered rather than after.
    /// </summary>
    public int MaxImageUploadBytes { get; set; } = BeaconLimits.MaxImageUploadBytes;

    /// <summary>
    /// Largest pixel count accepted. A 12 MB file can still decode to a hundred megapixels and exhaust
    /// the process, so this, not the byte limit, is the guard that stops a decompression bomb.
    /// </summary>
    public int MaxImagePixels { get; set; } = 50_000_000;

    /// <summary>WebP quality for stored screenshots. 80 is visually clean at a fraction of the size.</summary>
    public int ImageQuality { get; set; } = 80;

    /// <summary>
    /// Accounts promoted to moderator on startup, by account id. The only way in, since there is no
    /// admin UI: moderation is deliberately a config-file decision on a self-hosted instance.
    /// </summary>
    public List<string> ModeratorAccountIds { get; set; } = [];

    /// <summary>
    /// Addresses of reverse proxies whose X-Forwarded-For header may be trusted.
    ///
    /// Behind a proxy without this, every request appears to come from the proxy itself, so the
    /// per-IP limits collapse into one shared bucket: five account registrations per hour for the
    /// entire internet, and one anonymous rate limit for everybody at once. Left empty, forwarded
    /// headers are ignored entirely, which is the right default for a server reached directly.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>Requests per minute per account, or per IP when anonymous.</summary>
    public int RateLimitPerMinute { get; set; } = 120;

    /// <summary>Writes per minute per account. Lower, because writes are the expensive, abusable ones.</summary>
    public int WriteRateLimitPerMinute { get; set; } = 20;

    /// <summary>
    /// New accounts per hour from one address.
    ///
    /// Configurable like every other limit rather than hardcoded: a development instance creates
    /// accounts constantly, and a large shared household or a convention hall is a legitimate reason
    /// to raise it on a real one.
    /// </summary>
    public int RegistrationsPerHour { get; set; } = 5;

    public string ResolveDataDirectory(string contentRoot) =>
        Path.IsPathRooted(DataDirectory) ? DataDirectory : Path.Combine(contentRoot, DataDirectory);

    public string ImageDirectory(string contentRoot) =>
        Path.Combine(ResolveDataDirectory(contentRoot), "images");
}
