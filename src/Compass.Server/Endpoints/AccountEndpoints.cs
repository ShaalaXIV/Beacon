using Compass.Server.Auth;
using Compass.Server.Data;
using Compass.Server.Data.Entities;
using Compass.Shared;
using Compass.Shared.Accounts;
using Compass.Shared.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Compass.Server.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Registration)
            .WithSummary("Create an account and mint its one and only secret key.");

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithSummary("The calling account.");

        group.MapPatch("/me", UpdateMeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Change account settings.");

        group.MapPost("/me/characters", LinkCharacterAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Claim a character for this account.");

        group.MapPut("/me/adult", ConfirmAdultAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Confirm the account holder is an adult, or withdraw that confirmation.");

        group.MapDelete("/me/characters/{name}/{worldId:int}", UnlinkCharacterAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Write)
            .WithSummary("Release a character from this account.");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterAccountRequest request,
        CompassDbContext db,
        IOptions<CompassOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.RegistrationOpen)
            return Results.Problem("This instance is not accepting new accounts.", statusCode: StatusCodes.Status403Forbidden);

        var displayName = Validation.CleanLine(request.DisplayName);
        if (Validation.ValidateDisplayName(displayName) is { } error)
            return Results.BadRequest(new { error });

        var key = ApiKeys.Generate();
        var now = DateTimeOffset.UtcNow;

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            KeyHash = ApiKeys.Hash(key),
            CreatedAt = now,
            LastSeenAt = now,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new RegisterAccountResponse
        {
            Account = account.ToDto(beaconCount: 0),

            // The only time this value ever leaves the server.
            SecretKey = key,
        });
    }

    private static async Task<IResult> GetMeAsync(HttpContext http, CompassDbContext db, CancellationToken ct)
    {
        var account = http.RequireAccount();
        return Results.Ok(await LoadDtoAsync(db, account, ct));
    }

    private static async Task<IResult> UpdateMeAsync(
        UpdateAccountRequest request,
        HttpContext http,
        CompassDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        if (request.DisplayName is not null)
        {
            var displayName = Validation.CleanLine(request.DisplayName);
            if (Validation.ValidateDisplayName(displayName) is { } error)
                return Results.BadRequest(new { error });

            account.DisplayName = displayName;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDtoAsync(db, account, ct));
    }

    private static async Task<IResult> LinkCharacterAsync(
        LinkCharacterRequest request,
        HttpContext http,
        CompassDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();

        var name = Validation.CleanLine(request.Name);
        if (name.Length is < 2 or > 48 || request.WorldId == 0)
            return Results.BadRequest(new { error = "That character name or world is not valid." });

        var existing = await db.Characters
            .FirstOrDefaultAsync(c => c.Name == name && c.WorldId == request.WorldId, ct);

        if (existing is not null)
        {
            // Already ours: refresh the denormalised world data and move on. Re-linking after a world
            // transfer is routine, and should not be an error.
            if (existing.AccountId == account.Id)
            {
                existing.WorldName = Validation.CleanLine(request.WorldName);
                existing.DataCenter = Validation.CleanLine(request.DataCenter);
                await db.SaveChangesAsync(ct);
                return Results.Ok(await LoadDtoAsync(db, account, ct));
            }

            return Results.Conflict(new
            {
                error = "That character is already claimed by another account.",
            });
        }

        db.Characters.Add(new CharacterEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Name = name,
            WorldId = request.WorldId,
            WorldName = Validation.CleanLine(request.WorldName),
            DataCenter = Validation.CleanLine(request.DataCenter),
            LinkedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDtoAsync(db, account, ct));
    }

    /// <summary>
    /// Records, or withdraws, the confirmation that the account holder is an adult.
    ///
    /// Withdrawing takes effect immediately but does not retroactively strip themes from cards already
    /// written; the account simply cannot set new ones until it is confirmed again.
    /// </summary>
    private static async Task<IResult> ConfirmAdultAsync(
        ConfirmAdultRequest request,
        HttpContext http,
        CompassDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();
        account.AdultConfirmedAt = request.Confirmed ? DateTimeOffset.UtcNow : null;

        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDtoAsync(db, account, ct));
    }

    private static async Task<IResult> UnlinkCharacterAsync(
        string name,
        int worldId,
        HttpContext http,
        CompassDbContext db,
        CancellationToken ct)
    {
        var account = http.RequireAccount();
        var cleaned = Validation.CleanLine(name);

        var character = await db.Characters.FirstOrDefaultAsync(
            c => c.AccountId == account.Id && c.Name == cleaned && c.WorldId == (uint)worldId,
            ct);

        if (character is null)
            return Results.NotFound(new { error = "That character is not linked to this account." });

        db.Characters.Remove(character);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadDtoAsync(db, account, ct));
    }

    private static async Task<AccountDto> LoadDtoAsync(CompassDbContext db, AccountEntity account, CancellationToken ct)
    {
        var characters = await db.Characters
            .Where(c => c.AccountId == account.Id)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var beaconCount = await db.Beacons
            .CountAsync(b => b.OwnerAccountId == account.Id && !b.IsDeleted, ct);

        return account.ToDto(beaconCount, characters);
    }

    private static AccountDto ToDto(
        this AccountEntity account,
        int beaconCount,
        IReadOnlyList<CharacterEntity>? characters = null) =>
        new()
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            CreatedAt = account.CreatedAt,
            IsModerator = account.IsModerator,
            AdultConfirmed = account.AdultConfirmedAt is not null,
            BeaconCount = beaconCount,
            Characters = (characters ?? [])
                .Select(c => new CharacterDto
                {
                    Name = c.Name,
                    WorldId = c.WorldId,
                    WorldName = c.WorldName,
                    DataCenter = c.DataCenter,
                    LinkedAt = c.LinkedAt,
                })
                .ToList(),
        };
}
