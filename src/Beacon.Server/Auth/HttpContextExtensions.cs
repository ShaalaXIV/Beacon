using System.Security.Claims;
using Beacon.Server.Data.Entities;

namespace Beacon.Server.Auth;

public static class HttpContextExtensions
{
    /// <summary>
    /// The account behind this request, or null when unauthenticated.
    /// Resolved once by the auth handler and cached, so endpoints do not re-query it.
    /// </summary>
    public static AccountEntity? GetAccount(this HttpContext context) =>
        context.Items.TryGetValue(typeof(AccountEntity), out var value) ? value as AccountEntity : null;

    /// <summary>The account behind this request. Only call from endpoints that require authorisation.</summary>
    public static AccountEntity RequireAccount(this HttpContext context) =>
        context.GetAccount()
        ?? throw new InvalidOperationException("Endpoint requires authentication but no account was resolved.");

    public static Guid? GetAccountId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(BeaconAuthHandler.AccountIdClaim);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static bool IsModerator(this HttpContext context) =>
        context.GetAccount()?.IsModerator ?? false;
}
