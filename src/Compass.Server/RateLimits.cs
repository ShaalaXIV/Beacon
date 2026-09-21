using System.Globalization;
using System.Threading.RateLimiting;
using Compass.Server.Auth;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Compass.Server;

/// <summary>
/// Rate limiting, partitioned by account where we know one and by IP where we do not.
///
/// Partitioning by account rather than by IP alone matters for this audience: a linkshell behind one
/// household NAT, or a group at a convention, would otherwise throttle each other.
/// </summary>
public static class RateLimits
{
    /// <summary>General reads.</summary>
    public const string Read = "read";

    /// <summary>Anything that changes state. Tighter, because writes are the abusable ones.</summary>
    public const string Write = "write";

    /// <summary>Account creation. Tightest of all, and always by IP: there is no account yet.</summary>
    public const string Registration = "registration";

    /// <summary>Screenshot uploads, which cost real CPU and disk.</summary>
    public const string Upload = "upload";

    public static void AddCompassRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, ct) =>
            {
                // Tell the client how long to wait instead of leaving it to guess and hammer.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    """{"error":"Slow down a moment, then try again."}""",
                    ct);
            };

            limiter.AddPolicy(Read, http => Partition(http, ReadLimit(http)));
            limiter.AddPolicy(Write, http => Partition(http, WriteLimit(http)));
            limiter.AddPolicy(Upload, http => Partition(http, new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));

            limiter.AddPolicy(Registration, http => RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{ClientIp(http)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Options(http).RegistrationsPerHour,
                    Window = TimeSpan.FromHours(1),
                }));
        });
    }

    private static FixedWindowRateLimiterOptions ReadLimit(HttpContext http) => new()
    {
        PermitLimit = Options(http).RateLimitPerMinute,
        Window = TimeSpan.FromMinutes(1),
    };

    private static FixedWindowRateLimiterOptions WriteLimit(HttpContext http) => new()
    {
        PermitLimit = Options(http).WriteRateLimitPerMinute,
        Window = TimeSpan.FromMinutes(1),
    };

    private static CompassOptions Options(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<CompassOptions>>().Value;

    private static RateLimitPartition<string> Partition(HttpContext http, FixedWindowRateLimiterOptions options)
    {
        var account = http.GetAccount();
        var key = account is not null ? $"acct:{account.Id}" : $"ip:{ClientIp(http)}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => options);
    }

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
