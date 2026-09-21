using System.Security.Claims;
using System.Text.Encodings.Web;
using Beacon.Server.Data;
using Beacon.Server.Data.Entities;
using Beacon.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Beacon.Server.Auth;

/// <summary>
/// Authenticates a request from the account secret key in the <c>X-Beacon-Key</c> header.
/// </summary>
public sealed class BeaconAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BeaconDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BeaconKey";

    /// <summary>Claim carrying the account id.</summary>
    public const string AccountIdClaim = "beacon:account";

    /// <summary>Claim present when the account is banned from writing.</summary>
    public const string BannedClaim = "beacon:banned";

    public const string ModeratorRole = "Moderator";

    /// <summary>
    /// How stale a last-seen timestamp may get before we bother writing it. Without this, every read
    /// becomes a write, which on SQLite means every read takes the write lock.
    /// </summary>
    private static readonly TimeSpan LastSeenPrecision = TimeSpan.FromMinutes(10);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = ExtractKey();
        if (key is null)
            return AuthenticateResult.NoResult();

        if (!ApiKeys.LooksValid(key))
            return AuthenticateResult.Fail("Malformed key.");

        var hash = ApiKeys.Hash(key);
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.KeyHash == hash, Context.RequestAborted);

        if (account is null)
            return AuthenticateResult.Fail("Unknown key.");

        await TouchLastSeenAsync(account);

        Context.Items[typeof(AccountEntity)] = account;

        var claims = new List<Claim>
        {
            new(AccountIdClaim, account.Id.ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.DisplayName),
        };

        if (account.IsModerator)
            claims.Add(new Claim(ClaimTypes.Role, ModeratorRole));

        if (account.IsBanned)
            claims.Add(new Claim(BannedClaim, "true"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private string? ExtractKey()
    {
        if (Request.Headers.TryGetValue(ApiRoutes.ApiKeyHeader, out var header))
        {
            var value = header.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        // WebSocket upgrades from contexts that cannot set headers fall back to a query parameter.
        if (Request.Query.TryGetValue("key", out var query))
        {
            var value = query.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private async Task TouchLastSeenAsync(AccountEntity account)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - account.LastSeenAt < LastSeenPrecision)
            return;

        account.LastSeenAt = now;
        try
        {
            await db.SaveChangesAsync(Context.RequestAborted);
        }
        catch (Exception ex)
        {
            // A missed last-seen update must never fail the request it rode in on.
            Logger.LogDebug(ex, "Could not update last-seen for account {AccountId}.", account.Id);
        }
    }
}
