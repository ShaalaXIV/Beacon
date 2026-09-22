using Beacon.Server.Auth;
using Beacon.Server.Data;
using Beacon.Server.Data.Entities;
using Beacon.Server.Realtime;
using Beacon.Server.Services;
using Beacon.Shared.Beacons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Beacon.Server.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/beacons/{id:guid}/image", UploadAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Upload)
            .DisableAntiforgery()
            .WithTags("Images")
            .WithSummary("Attach a screenshot to a beacon.");

        app.MapDelete("/api/beacons/{id:guid}/image", RemoveAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithTags("Images")
            .WithSummary("Remove a beacon's screenshot.");

        var images = app.MapGroup("/images")
            .WithTags("Images")
            .RequireAuthorization();

        images.MapGet("/{imageId:guid}",
            (Guid imageId, ImageService service, BeaconDbContext db, CancellationToken ct, string? format = null) =>
                ServeAsync(imageId, thumb: false, format, service, db, ct));

        images.MapGet("/{imageId:guid}/thumb",
            (Guid imageId, ImageService service, BeaconDbContext db, CancellationToken ct, string? format = null) =>
                ServeAsync(imageId, thumb: true, format, service, db, ct));
    }

    private static async Task<IResult> UploadAsync(
        Guid id,
        HttpContext http,
        BeaconDbContext db,
        ImageService images,
        BeaconHub hub,
        IOptions<BeaconOptions> options,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected a multipart form upload." });

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return Results.NotFound(new { error = "That beacon no longer exists." });

        if (beacon.OwnerAccountId != account.Id && !account.IsModerator)
            return Results.Json(new { error = "That is not your beacon." }, statusCode: StatusCodes.Status403Forbidden);

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("image") ?? form.Files.FirstOrDefault();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "No image was attached." });

        if (file.Length > options.Value.MaxImageUploadBytes)
        {
            return Results.BadRequest(new
            {
                error = $"That screenshot is larger than {options.Value.MaxImageUploadBytes / (1024 * 1024)} MB.",
            });
        }

        await using var stream = file.OpenReadStream();
        var stored = await images.StoreAsync(stream, ct);

        if (stored is null)
            return Results.BadRequest(new { error = "That file could not be read as an image." });

        var previous = beacon.ImageId;

        db.Images.Add(new BeaconImageEntity
        {
            Id = stored.Id,
            BeaconId = beacon.Id,
            UploadedByAccountId = account.Id,
            ContentType = stored.ContentType,
            Width = stored.Width,
            Height = stored.Height,
            SizeBytes = stored.SizeBytes,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        beacon.ImageId = stored.Id;
        beacon.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Only after the new image is committed: if the save failed we would have deleted the old
        // screenshot and kept nothing in its place.
        if (previous is { } old)
            await DiscardAsync(old, db, images, ct);

        var ownerName = await db.Accounts
            .Where(a => a.Id == beacon.OwnerAccountId)
            .Select(a => a.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Unknown";

        var dto = beacon.ToDto(
            ownerName,
            stage: await db.Stages.FirstOrDefaultAsync(s => s.BeaconId == beacon.Id, ct));
        hub.Broadcast(new BeaconEvent { Kind = BeaconEventKind.Updated, BeaconId = beacon.Id, Beacon = dto });

        return Results.Ok(dto);
    }

    private static async Task<IResult> RemoveAsync(
        Guid id,
        HttpContext http,
        BeaconDbContext db,
        ImageService images,
        BeaconHub hub,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return Results.NotFound(new { error = "That beacon no longer exists." });

        if (beacon.OwnerAccountId != account.Id && !account.IsModerator)
            return Results.Json(new { error = "That is not your beacon." }, statusCode: StatusCodes.Status403Forbidden);

        if (beacon.ImageId is not { } imageId)
            return Results.NoContent();

        beacon.ImageId = null;
        beacon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await DiscardAsync(imageId, db, images, ct);

        hub.Broadcast(new BeaconEvent { Kind = BeaconEventKind.Updated, BeaconId = beacon.Id });
        return Results.NoContent();
    }

    private static async Task<IResult> ServeAsync(
        Guid imageId,
        bool thumb,
        string? format,
        ImageService images,
        BeaconDbContext db,
        CancellationToken ct)
    {
        var record = await db.Images.FirstOrDefaultAsync(i => i.Id == imageId, ct);
        if (record is null)
            return Results.NotFound();

        var wantsPng = string.Equals(format, "png", StringComparison.OrdinalIgnoreCase);

        var path = wantsPng
            ? await images.EnsurePngAsync(imageId, thumb, ct)
            : images.PathFor(imageId, thumb);

        if (path is null || !File.Exists(path))
            return Results.NotFound();

        var contentType = wantsPng ? "image/png" : record.ContentType;
        var variant = $"{(thumb ? "t" : "f")}{(wantsPng ? "p" : "w")}";

        // Image ids are never reused and the bytes never change, so this can be cached hard.
        // The tag includes the variant so a PNG and a WebP of the same image never collide in a cache.
        return Results.File(
            path,
            contentType,
            enableRangeProcessing: true,
            lastModified: record.CreatedAt,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{imageId:N}{variant}\""));
    }

    private static async Task DiscardAsync(Guid imageId, BeaconDbContext db, ImageService images, CancellationToken ct)
    {
        var record = await db.Images.FirstOrDefaultAsync(i => i.Id == imageId, ct);
        if (record is not null)
        {
            db.Images.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        images.Delete(imageId);
    }
}
