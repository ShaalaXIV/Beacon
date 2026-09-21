using Beacon.Server.Auth;
using Beacon.Server.Data;
using Beacon.Server.Data.Entities;
using Beacon.Server.Services;
using Beacon.Shared.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Beacon.Server.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profiles")
            .WithTags("Profiles")
            .RequireAuthorization();

        group.MapGet("/", SearchAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("Search the Chronicle.");

        group.MapGet("/me", MineAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("The calling account's own cards.");

        // Registered before the {id:guid} route purely for readability; the guid constraint already
        // keeps "me" and "activity" from matching it.
        group.MapPost("/activity", HeartbeatAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Report that a character is standing at a lit beacon.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("One profile by id.");

        group.MapGet("/code/{code}", GetByCodeAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("One profile by its share code.");

        group.MapGet("/character/{name}/{worldId:int}", GetByCharacterAsync)
            .RequireRateLimiting(RateLimits.Read)
            .WithSummary("The profile of a character you have just met.");

        group.MapPut("/", SaveAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Create or replace a character's profile.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Retire a profile.");

        group.MapPut("/{id:guid}/availability", SetAvailabilityAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Override the availability Beacon would otherwise derive.");

        group.MapPost("/{id:guid}/images", UploadImageAsync)
            .RequireRateLimiting(RateLimits.Upload)
            .DisableAntiforgery()
            .WithSummary("Add an image to the gallery.");

        group.MapDelete("/{id:guid}/images/{imageId:guid}", RemoveImageAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Remove an image from the gallery.");

        group.MapPut("/{id:guid}/gallery", UpdateGalleryAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Reorder, recaption, or choose the portrait.");

        group.MapPost("/{id:guid}/links", AddLinkAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Claim a tie to another character.");

        group.MapPost("/{id:guid}/links/{otherId:guid}/confirm", ConfirmLinkAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Agree to a tie somebody claimed.");

        group.MapDelete("/{id:guid}/links/{otherId:guid}", RemoveLinkAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Remove a tie, in both directions.");

        group.MapPost("/{id:guid}/report", ReportAsync)
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Flag a profile for a moderator.");
    }

    private static async Task<IResult> SearchAsync(
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] string? race = null,
        [FromQuery] string? world = null,
        [FromQuery] string? dataCenter = null,
        [FromQuery(Name = "personality")] string[]? personality = null,
        [FromQuery(Name = "tone")] string[]? tone = null,
        [FromQuery(Name = "activity")] string[]? activity = null,
        [FromQuery] string? length = null,
        [FromQuery] bool availableOnly = false,
        [FromQuery] int? activeWithinDays = null,
        [FromQuery] bool walkupsOnly = false,
        [FromQuery] bool includeMature = false,
        [FromQuery(Name = "mature")] string[]? mature = null,
        [FromQuery] Guid? ownerAccountId = null,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = ProfileLimits.DefaultPageSize)
    {
        // Parsed rather than bound so that camelCase, the spelling every JSON body uses, works here too.
        if (!QueryEnum.TryParseAll<PersonalityTrait>(personality, "personality", out var traits, out var error)
            || !QueryEnum.TryParseAll<RpTone>(tone, "tone", out var tones, out error)
            || !QueryEnum.TryParseAll<RpActivity>(activity, "activity", out var activities, out error)
            || !QueryEnum.TryParse<RpLength>(length, "length", out var rpLength, out error)
            || !QueryEnum.TryParseAll<MatureTheme>(mature, "mature", out var matureThemes, out error)
            || !QueryEnum.TryParseOr(sort, "sort", ProfileSort.Relevance, out var rpSort, out error))
        {
            return Results.BadRequest(new { error });
        }

        var query = new ProfileQuery
        {
            Search = search,
            Race = race,
            World = world,
            DataCenter = dataCenter,
            Personality = traits,
            Tones = tones,
            Activities = activities,
            Length = rpLength,
            AvailableOnly = availableOnly,
            ActiveWithinDays = activeWithinDays,
            WalkupsOnly = walkupsOnly,
            IncludeMature = includeMature,
            MatureThemes = matureThemes,
            OwnerAccountId = ownerAccountId,
            Sort = rpSort,
            Page = page,
            PageSize = pageSize,
        };

        return Results.Ok(await profiles.QueryAsync(query, http.RequireAccount(), ct));
    }

    private static async Task<IResult> MineAsync(HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var account = http.RequireAccount();

        var query = new ProfileQuery
        {
            OwnerAccountId = account.Id,
            IncludeMature = true,
            Sort = ProfileSort.Name,
            PageSize = ProfileLimits.MaxPageSize,
        };

        return Results.Ok(await profiles.QueryAsync(query, account, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, ProfileService profiles, CancellationToken ct) =>
        (await profiles.GetAsync(id, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> GetByCodeAsync(string code, HttpContext http, ProfileService profiles, CancellationToken ct) =>
        (await profiles.GetByShareCodeAsync(code, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> GetByCharacterAsync(
        string name,
        int worldId,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.GetByCharacterAsync(name, (uint)worldId, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> SaveAsync(
        SaveProfileRequest request,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.SaveAsync(request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var result = await profiles.DeleteAsync(id, http.RequireAccount(), ct);
        return result.Succeeded ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> SetAvailabilityAsync(
        Guid id,
        SetAvailabilityRequest request,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.SetAvailabilityAsync(id, request.Availability, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> HeartbeatAsync(
        ActivityHeartbeatRequest request,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct)
    {
        var result = await profiles.HeartbeatAsync(request, http.RequireAccount(), ct);

        // A throttled or rejected heartbeat is not an error the client should surface; it simply
        // means nothing was recorded this time.
        return result.Succeeded
            ? Results.Ok(new { recorded = result.Value })
            : result.ToHttpResult();
    }

    private static async Task<IResult> UploadImageAsync(
        Guid id,
        HttpContext http,
        ProfileService profiles,
        ImageService images,
        IOptions<BeaconOptions> options,
        CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected a multipart form upload." });

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("image") ?? form.Files.FirstOrDefault();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "No image was attached." });

        if (file.Length > options.Value.MaxImageUploadBytes)
        {
            return Results.BadRequest(new
            {
                error = $"That image is larger than {options.Value.MaxImageUploadBytes / (1024 * 1024)} MB.",
            });
        }

        await using var stream = file.OpenReadStream();
        var stored = await images.StoreAsync(stream, ct);

        if (stored is null)
            return Results.BadRequest(new { error = "That file could not be read as an image." });

        var result = await profiles.AddImageAsync(id, stored.Id, http.RequireAccount(), ct);

        // The bytes are on disk before the row exists, so a rejected add would otherwise orphan them.
        if (!result.Succeeded)
            images.Delete(stored.Id);

        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveImageAsync(
        Guid id,
        Guid imageId,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.RemoveImageAsync(id, imageId, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> UpdateGalleryAsync(
        Guid id,
        UpdateGalleryRequest request,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.UpdateGalleryAsync(id, request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> AddLinkAsync(
        Guid id,
        AddLinkRequest request,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.AddLinkAsync(id, request, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> ConfirmLinkAsync(
        Guid id,
        Guid otherId,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.ConfirmLinkAsync(id, otherId, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> RemoveLinkAsync(
        Guid id,
        Guid otherId,
        HttpContext http,
        ProfileService profiles,
        CancellationToken ct) =>
        (await profiles.RemoveLinkAsync(id, otherId, http.RequireAccount(), ct)).ToHttpResult();

    private static async Task<IResult> ReportAsync(
        Guid id,
        Beacon.Shared.Beacons.ReportBeaconRequest request,
        HttpContext http,
        BeaconDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        if (!await db.Profiles.AnyAsync(p => p.Id == id && !p.IsDeleted, ct))
            return Results.NotFound(new { error = "No such profile." });

        var reason = Validation.CleanBlock(request.Reason);
        if (reason.Length is 0 or > Beacon.Shared.Beacons.BeaconLimits.ReportReasonMaxLength)
            return Results.BadRequest(new { error = "Say briefly what is wrong with this profile." });

        var already = await db.Reports
            .AnyAsync(r => r.ProfileId == id && r.ReporterAccountId == account.Id && !r.Resolved, ct);

        if (already)
            return Results.Ok(new { message = "You have already reported this profile." });

        db.Reports.Add(new ReportEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = id,
            ReporterAccountId = account.Id,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "Reported. Thank you." });
    }
}
