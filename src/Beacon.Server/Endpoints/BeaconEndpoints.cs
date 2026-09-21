using Beacon.Server.Auth;
using Beacon.Server.Data;
using Beacon.Server.Data.Entities;
using Beacon.Server.Services;
using Beacon.Shared.Beacons;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Server.Endpoints;

public static class BeaconEndpoints
{
    public static void MapBeaconEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/beacons")
            .WithTags("Beacons")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("Browse the atlas.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("One beacon by id.");

        group.MapGet("/code/{code}", GetByCodeAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("One beacon by its share code.");

        group.MapPost("/", CreateAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Raise a new beacon.");

        group.MapPatch("/{id:guid}", UpdateAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Edit a beacon.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Retire a beacon.");

        group.MapPost("/{id:guid}/light", LightAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Light a beacon you are standing at.");

        group.MapPost("/{id:guid}/stoke", StokeAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Extend a burning flame.");

        group.MapPost("/{id:guid}/extinguish", ExtinguishAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Put a flame out.");

        group.MapPut("/{id:guid}/favorite", FavoriteAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Star a beacon.");

        group.MapDelete("/{id:guid}/favorite", UnfavoriteAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Unstar a beacon.");

        group.MapPost("/{id:guid}/report", ReportAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Flag a beacon for a moderator.");
    }

    private static async Task<IResult> BrowseAsync(
        HttpContext http,
        BeaconService beacons,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] int? territoryId = null,
        [FromQuery] string? dataCenter = null,
        [FromQuery] string? world = null,
        [FromQuery] string? region = null,
        [FromQuery] string? kind = null,
        [FromQuery] bool litOnly = false,
        [FromQuery] string? tag = null,
        [FromQuery] Guid? ownerAccountId = null,
        [FromQuery] bool favoritesOnly = false,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = BeaconLimits.DefaultPageSize)
    {
        // See QueryEnum: minimal APIs bind these case-sensitively and throw on a mismatch, so
        // ?kind=camp would 500 while ?kind=Camp worked.
        if (!QueryEnum.TryParse<BeaconKind>(kind, "kind", out var beaconKind, out var error)
            || !QueryEnum.TryParseOr(sort, "sort", BeaconSort.Relevance, out var beaconSort, out error))
        {
            return Results.BadRequest(new { error });
        }

        var query = new BeaconQuery
        {
            Search = search,
            TerritoryId = territoryId is > 0 and <= ushort.MaxValue ? (ushort)territoryId.Value : null,
            DataCenter = dataCenter,
            World = world,
            Region = region,
            Kind = beaconKind,
            LitOnly = litOnly,
            Tag = tag,
            OwnerAccountId = ownerAccountId,
            FavoritesOnly = favoritesOnly,
            Sort = beaconSort,
            Page = page,
            PageSize = pageSize,
        };

        return Results.Ok(await beacons.QueryAsync(query, http.RequireAccount(), ct));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, BeaconService beacons, CancellationToken ct) =>
        (await beacons.GetAsync(id, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> GetByCodeAsync(string code, HttpContext http, BeaconService beacons, CancellationToken ct) =>
        (await beacons.GetByShareCodeAsync(code, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        CreateBeaconRequest request,
        HttpContext http,
        BeaconService beacons,
        CancellationToken ct)
    {
        var result = await beacons.CreateAsync(request, http.RequireAccount(), ct);
        return result.Succeeded
            ? Results.Created(Shared.ApiRoutes.Beacons.ById(result.Value!.Id), result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateBeaconRequest request,
        HttpContext http,
        BeaconService beacons,
        CancellationToken ct) =>
        (await beacons.UpdateAsync(id, request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, BeaconService beacons, CancellationToken ct)
    {
        var result = await beacons.DeleteAsync(id, http.RequireAccount(), ct);
        return result.Succeeded ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> LightAsync(
        Guid id,
        LightBeaconRequest request,
        HttpContext http,
        BeaconService beacons,
        CancellationToken ct) =>
        (await beacons.LightAsync(id, request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> StokeAsync(
        Guid id,
        StokeBeaconRequest request,
        HttpContext http,
        BeaconService beacons,
        CancellationToken ct) =>
        (await beacons.StokeAsync(id, request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> ExtinguishAsync(Guid id, HttpContext http, BeaconService beacons, CancellationToken ct) =>
        (await beacons.ExtinguishAsync(id, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> FavoriteAsync(Guid id, HttpContext http, BeaconService beacons, CancellationToken ct) =>
        (await beacons.SetFavoriteAsync(id, favorite: true, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> UnfavoriteAsync(Guid id, HttpContext http, BeaconService beacons, CancellationToken ct) =>
        (await beacons.SetFavoriteAsync(id, favorite: false, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> ReportAsync(
        Guid id,
        ReportBeaconRequest request,
        HttpContext http,
        BeaconDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        if (!await db.Beacons.AnyAsync(b => b.Id == id && !b.IsDeleted, ct))
            return Results.NotFound(new { error = "That beacon no longer exists." });

        var reason = Validation.CleanBlock(request.Reason);
        if (reason.Length is 0 or > BeaconLimits.ReportReasonMaxLength)
            return Results.BadRequest(new { error = "Say briefly what is wrong with this beacon." });

        // One open report per person per beacon. Without this, a report button is a spam button.
        var alreadyReported = await db.Reports
            .AnyAsync(r => r.BeaconId == id && r.ReporterAccountId == account.Id && !r.Resolved, ct);

        if (alreadyReported)
            return Results.Ok(new { message = "You have already reported this beacon." });

        db.Reports.Add(new ReportEntity
        {
            Id = Guid.NewGuid(),
            BeaconId = id,
            ReporterAccountId = account.Id,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "Reported. Thank you." });
    }
}
