using Beacon.Server.Auth;
using Beacon.Server.Data;
using Beacon.Server.Data.Entities;
using Beacon.Server.Services;
using Beacon.Shared.Beacons;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Server.Endpoints;

/// <summary>
/// The stage a keeper has dressed their place with, for guests who run Stagehand.
///
/// It is deliberately a separate route from the beacon itself. The atlas carries a description of the
/// stage -- its name, its weight, who built it -- and nothing more, so browsing stays cheap and the
/// definition is transferred only to somebody who has chosen to load it.
/// </summary>
public static class StageEndpoints
{
    public static void MapStageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/beacons/{id:guid}/stage", UploadAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Upload)
            .DisableAntiforgery()
            .WithTags("Stages")
            .WithSummary("Attach a Stagehand stage to a beacon.");

        app.MapGet("/api/beacons/{id:guid}/stage", DownloadAsync)
            .RequireAuthorization()
            .WithTags("Stages")
            .WithSummary("Fetch a beacon's stage definition.");

        app.MapDelete("/api/beacons/{id:guid}/stage", RemoveAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithTags("Stages")
            .WithSummary("Remove a beacon's stage.");
    }

    private static async Task<IResult> UploadAsync(
        Guid id,
        HttpContext http,
        BeaconDbContext db,
        StageService stages,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return Results.NotFound(new { error = "That beacon no longer exists." });

        if (beacon.OwnerAccountId != account.Id && !account.IsModerator)
            return Results.Json(new { error = "That is not your beacon." }, statusCode: StatusCodes.Status403Forbidden);

        // Refuse on the declared length before reading, so an oversized upload is not streamed in full
        // only to be thrown away.
        if (http.Request.ContentLength > BeaconLimits.MaxStageBytes)
            return Results.Json(
                new { error = $"That stage is larger than {BeaconLimits.MaxStageBytes / (1024 * 1024)} MB." },
                statusCode: StatusCodes.Status413PayloadTooLarge);

        var (info, rejected) = await stages.StoreAsync(
            id,
            http.Request.Body,
            ct);

        if (info is null)
            return Results.BadRequest(new { error = rejected?.Reason ?? "That stage could not be read." });

        var entity = await db.Stages.FirstOrDefaultAsync(s => s.BeaconId == id, ct);
        if (entity is null)
        {
            entity = new BeaconStageEntity { BeaconId = id };
            db.Stages.Add(entity);
        }

        entity.UploadedByAccountId = account.Id;
        entity.Name = info.Name;
        entity.AuthorName = info.AuthorName;
        entity.Description = info.Description;
        entity.IntendedTerritoryType = info.IntendedTerritoryType;
        entity.ObjectCount = info.ObjectCount;
        entity.ModpackCount = info.ModpackCount;
        entity.SizeBytes = info.SizeBytes;
        entity.UpdatedAt = info.UpdatedAt;

        beacon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(info);
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        BeaconDbContext db,
        StageService stages,
        CancellationToken ct)
    {
        var exists = await db.Beacons.AnyAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (!exists)
            return Results.NotFound(new { error = "That beacon no longer exists." });

        var stream = stages.Open(id);
        if (stream is null)
            return Results.NotFound(new { error = "That beacon has no stage." });

        return Results.Stream(stream, "application/json");
    }

    private static async Task<IResult> RemoveAsync(
        Guid id,
        HttpContext http,
        BeaconDbContext db,
        StageService stages,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return Results.NotFound(new { error = "That beacon no longer exists." });

        if (beacon.OwnerAccountId != account.Id && !account.IsModerator)
            return Results.Json(new { error = "That is not your beacon." }, statusCode: StatusCodes.Status403Forbidden);

        var entity = await db.Stages.FirstOrDefaultAsync(s => s.BeaconId == id, ct);
        if (entity is not null)
            db.Stages.Remove(entity);

        stages.Delete(id);

        beacon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
