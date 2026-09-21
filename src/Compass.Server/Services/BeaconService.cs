using Compass.Server.Data;
using Compass.Server.Data.Entities;
using Compass.Server.Realtime;
using Compass.Shared.Beacons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Compass.Server.Services;

/// <summary>
/// Everything the atlas can do to a beacon. The rules about who may light what, and from where,
/// live here rather than in the endpoints, because they are the part that must not drift.
/// </summary>
public sealed class BeaconService(
    CompassDbContext db,
    BeaconHub hub,
    IOptions<CompassOptions> options,
    ILogger<BeaconService> logger)
{
    private readonly CompassOptions config = options.Value;

    // --- Reads ----------------------------------------------------------

    /// <summary>
    /// Beacons that may appear in a browse listing: public ones, plus everything the caller owns.
    /// Unlisted beacons are deliberately absent -- they are reachable by id or share code only.
    /// </summary>
    private IQueryable<BeaconEntity> Listable(AccountEntity caller) =>
        db.Beacons.Where(b =>
            !b.IsDeleted
            && (b.Visibility == BeaconVisibility.Public || b.OwnerAccountId == caller.Id));

    /// <summary>
    /// Beacons the caller may fetch directly. Adds unlisted ones, which is the entire point of an
    /// unlisted beacon: findable if you were given the link, invisible if you were not.
    /// </summary>
    private IQueryable<BeaconEntity> Fetchable(AccountEntity caller)
    {
        var isModerator = caller.IsModerator;
        return db.Beacons.Where(b =>
            !b.IsDeleted
            && (b.Visibility != BeaconVisibility.Private || b.OwnerAccountId == caller.Id || isModerator));
    }

    public async Task<PagedResult<BeaconDto>> QueryAsync(BeaconQuery query, AccountEntity caller, CancellationToken ct)
    {
        var q = Listable(caller);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(b =>
                EF.Functions.Like(b.Name, term, "\\")
                || EF.Functions.Like(b.Description, term, "\\")
                || EF.Functions.Like(b.ZoneName, term, "\\"));
        }

        if (query.TerritoryId is { } territory and > 0)
            q = q.Where(b => b.TerritoryId == territory);

        if (!string.IsNullOrWhiteSpace(query.DataCenter))
            q = q.Where(b => b.DataCenter == query.DataCenter);

        if (!string.IsNullOrWhiteSpace(query.World))
            q = q.Where(b => b.WorldName == query.World);

        if (!string.IsNullOrWhiteSpace(query.Region))
            q = q.Where(b => b.Region == query.Region);

        if (query.Kind is { } kind)
            q = q.Where(b => b.Kind == kind);

        if (query.LitOnly)
        {
            // Filter on the clock as well as the flag, so a beacon the sweeper has not reached yet is
            // never shown as burning. The sweeper is eventual; this query is not allowed to be.
            var now = DateTimeOffset.UtcNow;
            q = q.Where(b => b.IsLit && b.LitUntil != null && b.LitUntil > now);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = $"%|{EscapeLike(query.Tag.Trim().ToLowerInvariant())}|%";
            q = q.Where(b => EF.Functions.Like(b.TagsCsv, tag, "\\"));
        }

        if (query.OwnerAccountId is { } owner)
            q = q.Where(b => b.OwnerAccountId == owner);

        if (query.FavoritesOnly)
            q = q.Where(b => db.Favorites.Any(f => f.BeaconId == b.Id && f.AccountId == caller.Id));

        var total = await q.CountAsync(ct);

        q = ApplySort(q, query.Sort);

        var pageSize = Math.Clamp(query.PageSize, 1, BeaconLimits.MaxPageSize);
        var page = Math.Max(0, query.Page);

        var rows = await q
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<BeaconDto>
        {
            Items = await HydrateAsync(rows, caller, ct),
            Page = page,
            PageSize = pageSize,
            Total = total,
        };
    }

    private static IQueryable<BeaconEntity> ApplySort(IQueryable<BeaconEntity> q, BeaconSort sort) => sort switch
    {
        BeaconSort.Newest => q.OrderByDescending(b => b.CreatedAt),
        BeaconSort.Popular => q.OrderByDescending(b => b.FavoriteCount).ThenByDescending(b => b.CreatedAt),
        BeaconSort.Name => q.OrderBy(b => b.Name),
        BeaconSort.RecentlyLit => q.OrderByDescending(b => b.LastLitAt).ThenByDescending(b => b.CreatedAt),

        // Relevance: what is happening right now, then what happened recently, then what is new.
        _ => q.OrderByDescending(b => b.IsLit)
              .ThenByDescending(b => b.LastLitAt)
              .ThenByDescending(b => b.CreatedAt),
    };

    public async Task<OperationResult<BeaconDto>> GetAsync(Guid id, AccountEntity caller, CancellationToken ct)
    {
        var beacon = await Fetchable(caller).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        var dto = (await HydrateAsync([beacon], caller, ct))[0];
        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<BeaconDto>> GetByShareCodeAsync(string code, AccountEntity caller, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var beacon = await Fetchable(caller).FirstOrDefaultAsync(b => b.ShareCode == normalized, ct);

        return beacon is null
            ? OperationResult<BeaconDto>.NotFound("No beacon carries that code.")
            : OperationResult<BeaconDto>.Ok((await HydrateAsync([beacon], caller, ct))[0]);
    }

    /// <summary>
    /// Fills in the parts of a beacon that do not live on its row: the owner's display name and
    /// whether this caller has starred it. Done in two batch queries rather than per row.
    /// </summary>
    private async Task<List<BeaconDto>> HydrateAsync(
        List<BeaconEntity> rows,
        AccountEntity caller,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var ownerIds = rows.Select(b => b.OwnerAccountId).Distinct().ToList();
        var owners = await db.Accounts
            .Where(a => ownerIds.Contains(a.Id))
            .Select(a => new { a.Id, a.DisplayName })
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);

        var ids = rows.Select(b => b.Id).ToList();
        var favorites = await db.Favorites
            .Where(f => f.AccountId == caller.Id && ids.Contains(f.BeaconId))
            .Select(f => f.BeaconId)
            .ToListAsync(ct);

        var favoriteSet = favorites.ToHashSet();

        return rows
            .Select(b => b.ToDto(
                owners.GetValueOrDefault(b.OwnerAccountId, "Unknown"),
                favoriteSet.Contains(b.Id)))
            .ToList();
    }

    // --- Writes ---------------------------------------------------------

    public async Task<OperationResult<BeaconDto>> CreateAsync(
        CreateBeaconRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        if (caller.IsBanned)
            return OperationResult<BeaconDto>.Forbidden("This account cannot publish beacons.");

        var name = Validation.CleanLine(request.Name);
        if (Validation.ValidateBeaconName(name) is { } nameError)
            return OperationResult<BeaconDto>.Invalid(nameError);

        var description = Validation.CleanBlock(request.Description);
        if (Validation.ValidateDescription(description) is { } descriptionError)
            return OperationResult<BeaconDto>.Invalid(descriptionError);

        if (Validation.ValidateLocation(request.Location) is { } locationError)
            return OperationResult<BeaconDto>.Invalid(locationError);

        if (Validation.ValidateRealm(request.Realm) is { } realmError)
            return OperationResult<BeaconDto>.Invalid(realmError);

        var owned = await db.Beacons.CountAsync(b => b.OwnerAccountId == caller.Id && !b.IsDeleted, ct);
        if (owned >= config.MaxBeaconsPerAccount)
        {
            return OperationResult<BeaconDto>.Forbidden(
                $"You already own {config.MaxBeaconsPerAccount} beacons. Retire one before raising another.");
        }

        var now = DateTimeOffset.UtcNow;
        var beacon = new BeaconEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Kind = request.Kind,
            OwnerAccountId = caller.Id,
            TagsCsv = BeaconMapper.JoinTags(request.Tags),
            Visibility = request.Visibility,
            AllowPublicLighting = request.AllowPublicLighting,
            ShareCode = await MintShareCodeAsync(ct),
            CreatedAt = now,
            UpdatedAt = now,
        };

        beacon.ApplyLocation(request.Location);
        beacon.ApplyRealm(request.Realm);

        db.Beacons.Add(beacon);
        await db.SaveChangesAsync(ct);

        var dto = beacon.ToDto(caller.DisplayName);

        if (beacon.Visibility == BeaconVisibility.Public)
            hub.Broadcast(new BeaconEvent { Kind = BeaconEventKind.Published, BeaconId = beacon.Id, Beacon = dto });

        logger.LogInformation("Beacon {Id} raised by {Account} in {Zone}.", beacon.Id, caller.Id, beacon.ZoneName);
        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<BeaconDto>> UpdateAsync(
        Guid id,
        UpdateBeaconRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        if (!CanManage(beacon, caller))
            return OperationResult<BeaconDto>.Forbidden("That is not your beacon.");

        if (request.Name is not null)
        {
            var name = Validation.CleanLine(request.Name);
            if (Validation.ValidateBeaconName(name) is { } error)
                return OperationResult<BeaconDto>.Invalid(error);

            beacon.Name = name;
        }

        if (request.Description is not null)
        {
            var description = Validation.CleanBlock(request.Description);
            if (Validation.ValidateDescription(description) is { } error)
                return OperationResult<BeaconDto>.Invalid(error);

            beacon.Description = description;
        }

        if (request.Kind is { } kind)
            beacon.Kind = kind;

        if (request.Location is { } location)
        {
            if (Validation.ValidateLocation(location) is { } error)
                return OperationResult<BeaconDto>.Invalid(error);

            beacon.ApplyLocation(location);
        }

        if (request.Realm is { } realm)
        {
            if (Validation.ValidateRealm(realm) is { } error)
                return OperationResult<BeaconDto>.Invalid(error);

            beacon.ApplyRealm(realm);
        }

        if (request.Tags is not null)
            beacon.TagsCsv = BeaconMapper.JoinTags(request.Tags);

        if (request.Visibility is { } visibility)
            beacon.Visibility = visibility;

        if (request.AllowPublicLighting is { } allow)
            beacon.AllowPublicLighting = allow;

        if (request.ClearImage == true)
            beacon.ImageId = null;

        beacon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dto = (await HydrateAsync([beacon], caller, ct))[0];
        hub.Broadcast(new BeaconEvent { Kind = BeaconEventKind.Updated, BeaconId = beacon.Id, Beacon = dto });

        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, AccountEntity caller, CancellationToken ct)
    {
        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<bool>.NotFound("That beacon no longer exists.");

        if (!CanManage(beacon, caller))
            return OperationResult<bool>.Forbidden("That is not your beacon.");

        // Soft delete: a beacon referenced by an open report must still be inspectable by a moderator.
        beacon.IsDeleted = true;
        beacon.IsLit = false;
        beacon.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        hub.Broadcast(new BeaconEvent { Kind = BeaconEventKind.Removed, BeaconId = beacon.Id });
        return OperationResult<bool>.Ok(true);
    }

    // --- The flame ------------------------------------------------------

    public async Task<OperationResult<BeaconDto>> LightAsync(
        Guid id,
        LightBeaconRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        if (caller.IsBanned)
            return OperationResult<BeaconDto>.Forbidden("This account cannot light beacons.");

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        var isOwner = beacon.OwnerAccountId == caller.Id;
        if (!isOwner && !beacon.AllowPublicLighting)
            return OperationResult<BeaconDto>.Forbidden("Only the keeper of this beacon may light it.");

        if (Proximity(beacon, request) is { } proximityError)
            return OperationResult<BeaconDto>.Invalid(proximityError);

        var now = DateTimeOffset.UtcNow;
        var burning = beacon.IsLit && beacon.LitUntil > now;

        // Someone else is already standing there. Let the owner take over -- it is their place -- but
        // do not let two strangers fight over whose name is on the flame.
        if (burning && beacon.LitByAccountId != caller.Id && !isOwner)
        {
            return OperationResult<BeaconDto>.Conflict(
                $"{beacon.LitByName} already has this beacon lit.");
        }

        var minutes = Validation.ClampMinutes(request.Minutes);
        var note = Validation.CleanLine(request.Note);
        if (Validation.ValidateNote(note) is { } noteError)
            return OperationResult<BeaconDto>.Invalid(noteError);

        // Close any history row still open, so a takeover does not leave two burns overlapping.
        await CloseOpenLightsAsync(beacon.Id, now, ExtinguishReason.Manual, ct);

        beacon.IsLit = true;
        beacon.LitAt = now;
        beacon.LitUntil = now.AddMinutes(minutes);
        beacon.LitByName = Validation.CleanLine(request.CharacterName);
        beacon.LitByAccountId = caller.Id;
        beacon.LitNote = string.IsNullOrWhiteSpace(note) ? null : note;
        beacon.LastLitAt = now;
        beacon.TimesLit++;

        db.Lights.Add(new BeaconLightEntity
        {
            Id = Guid.NewGuid(),
            BeaconId = beacon.Id,
            AccountId = caller.Id,
            CharacterName = beacon.LitByName ?? string.Empty,
            LitAt = now,
            LitUntil = beacon.LitUntil.Value,
            Note = beacon.LitNote,
        });

        await db.SaveChangesAsync(ct);

        var dto = (await HydrateAsync([beacon], caller, ct))[0];

        // Carries the whole beacon: a listener filtering by zone may never have seen this one before,
        // and a flame with no beacon attached is not something they can show.
        hub.Broadcast(new BeaconEvent
        {
            Kind = BeaconEventKind.Lit,
            BeaconId = beacon.Id,
            Flame = beacon.ToFlame(),
            Beacon = dto,
            At = now,
        });

        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<BeaconDto>> StokeAsync(
        Guid id,
        StokeBeaconRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        var now = DateTimeOffset.UtcNow;
        if (!beacon.IsLit || beacon.LitUntil is null || beacon.LitUntil <= now)
            return OperationResult<BeaconDto>.Invalid("That beacon is not lit.");

        if (!CanTendFlame(beacon, caller))
            return OperationResult<BeaconDto>.Forbidden("Only whoever lit this beacon, or its keeper, may tend it.");

        // Extend from the current expiry, but never let a chain of stokes exceed one maximum burn from
        // when it was first lit: that is the guard that stops a beacon becoming permanent by accident.
        var ceiling = (beacon.LitAt ?? now).AddMinutes(BeaconLimits.MaxLitMinutes);
        var extended = beacon.LitUntil.Value.AddMinutes(Validation.ClampMinutes(request.Minutes));

        beacon.LitUntil = extended > ceiling ? ceiling : extended;

        if (request.Note is not null)
        {
            var note = Validation.CleanLine(request.Note);
            if (Validation.ValidateNote(note) is { } noteError)
                return OperationResult<BeaconDto>.Invalid(noteError);

            beacon.LitNote = string.IsNullOrWhiteSpace(note) ? null : note;
        }

        var openLight = await db.Lights
            .FirstOrDefaultAsync(l => l.BeaconId == beacon.Id && l.ExtinguishedAt == null, ct);

        if (openLight is not null)
        {
            openLight.LitUntil = beacon.LitUntil.Value;
            openLight.Note = beacon.LitNote;
        }

        await db.SaveChangesAsync(ct);

        var dto = (await HydrateAsync([beacon], caller, ct))[0];
        hub.Broadcast(new BeaconEvent
        {
            Kind = BeaconEventKind.Stoked,
            BeaconId = beacon.Id,
            Flame = beacon.ToFlame(),
            At = now,
        });

        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<BeaconDto>> ExtinguishAsync(Guid id, AccountEntity caller, CancellationToken ct)
    {
        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        if (!CanTendFlame(beacon, caller))
            return OperationResult<BeaconDto>.Forbidden("Only whoever lit this beacon, or its keeper, may put it out.");

        var now = DateTimeOffset.UtcNow;
        await CloseOpenLightsAsync(beacon.Id, now, ExtinguishReason.Manual, ct);

        beacon.IsLit = false;
        beacon.LitAt = null;
        beacon.LitUntil = null;
        beacon.LitByName = null;
        beacon.LitByAccountId = null;
        beacon.LitNote = null;

        await db.SaveChangesAsync(ct);

        var dto = (await HydrateAsync([beacon], caller, ct))[0];
        hub.Broadcast(new BeaconEvent
        {
            Kind = BeaconEventKind.Extinguished,
            BeaconId = beacon.Id,
            Flame = BeaconFlame.Dark,
            At = now,
        });

        return OperationResult<BeaconDto>.Ok(dto);
    }

    public async Task<OperationResult<BeaconDto>> SetFavoriteAsync(
        Guid id,
        bool favorite,
        AccountEntity caller,
        CancellationToken ct)
    {
        var beacon = await Fetchable(caller).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (beacon is null)
            return OperationResult<BeaconDto>.NotFound("That beacon no longer exists.");

        var existing = await db.Favorites
            .FirstOrDefaultAsync(f => f.AccountId == caller.Id && f.BeaconId == id, ct);

        if (favorite && existing is null)
        {
            db.Favorites.Add(new FavoriteEntity
            {
                AccountId = caller.Id,
                BeaconId = id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            beacon.FavoriteCount++;
        }
        else if (!favorite && existing is not null)
        {
            db.Favorites.Remove(existing);
            beacon.FavoriteCount = Math.Max(0, beacon.FavoriteCount - 1);
        }

        await db.SaveChangesAsync(ct);

        var dto = beacon.ToDto(
            await db.Accounts.Where(a => a.Id == beacon.OwnerAccountId)
                .Select(a => a.DisplayName).FirstOrDefaultAsync(ct) ?? "Unknown",
            favorite);

        return OperationResult<BeaconDto>.Ok(dto);
    }

    // --- Rules ----------------------------------------------------------

    /// <summary>Owners and moderators may edit or remove a beacon.</summary>
    private static bool CanManage(BeaconEntity beacon, AccountEntity caller) =>
        beacon.OwnerAccountId == caller.Id || caller.IsModerator;

    /// <summary>Whoever lit a flame may tend it, and so may the beacon's keeper and a moderator.</summary>
    private static bool CanTendFlame(BeaconEntity beacon, AccountEntity caller) =>
        beacon.OwnerAccountId == caller.Id
        || beacon.LitByAccountId == caller.Id
        || caller.IsModerator;

    /// <summary>
    /// Enforces that you are actually standing at the beacon.
    ///
    /// The plugin checks this too, for a responsive button, but the check that counts is this one:
    /// the client is just a program on someone else's computer, and "lit" is only meaningful if it
    /// cannot be claimed from the other side of the world.
    /// </summary>
    private static string? Proximity(BeaconEntity beacon, LightBeaconRequest request)
    {
        if (beacon.WorldId != request.WorldId)
            return $"You must be on {beacon.WorldName} to light this beacon.";

        if (beacon.TerritoryId != request.TerritoryId)
            return $"You must be in {beacon.ZoneName} to light this beacon.";

        var dx = beacon.X - request.X;
        var dz = beacon.Z - request.Z;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));

        // Height is ignored on purpose: standing on the balcony above a camp is still being at it.
        return distance > BeaconLimits.LightingRangeYalms
            ? $"You are {distance:0} yalms away. Get within {BeaconLimits.LightingRangeYalms:0} to light it."
            : null;
    }

    private async Task CloseOpenLightsAsync(Guid beaconId, DateTimeOffset now, ExtinguishReason reason, CancellationToken ct)
    {
        var open = await db.Lights
            .Where(l => l.BeaconId == beaconId && l.ExtinguishedAt == null)
            .ToListAsync(ct);

        foreach (var light in open)
        {
            light.ExtinguishedAt = now;
            light.Reason = reason;
        }
    }

    /// <summary>
    /// Mints a share code, retrying on the astronomically unlikely collision rather than trusting
    /// randomness and letting a unique-index violation surface as a 500.
    /// </summary>
    private async Task<string> MintShareCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = BeaconMapper.NewShareCode();
            if (!await db.Beacons.AnyAsync(b => b.ShareCode == code, ct))
                return code;
        }

        // 31^8 of space; reaching here means something is badly wrong with the RNG, not bad luck.
        throw new InvalidOperationException("Could not mint a unique share code.");
    }

    /// <summary>Escapes the LIKE wildcards so a search for "100%" does not match everything.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
