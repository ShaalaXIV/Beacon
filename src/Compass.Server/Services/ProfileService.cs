using Compass.Server.Data;
using Compass.Server.Data.Entities;
using Compass.Shared.Beacons;
using Compass.Shared.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Compass.Server.Services;

/// <summary>
/// Everything the Chronicle can do to a profile.
///
/// The rule that shapes the rest: availability is never stored as something a player maintains. It is
/// derived from whether one of their beacons is burning, so a card cannot claim somebody is around
/// when they logged off three weeks ago.
/// </summary>
public sealed class ProfileService(
    CompassDbContext db,
    ImageService images,
    ILogger<ProfileService> logger)
{
    // --- Visibility ------------------------------------------------------

    /// <summary>Profiles that may appear in a search: public ones, plus the caller's own.</summary>
    private IQueryable<ProfileEntity> Listable(AccountEntity caller) =>
        db.Profiles.Where(p =>
            !p.IsDeleted
            && (p.Visibility == ProfileVisibility.Public || p.OwnerAccountId == caller.Id));

    /// <summary>Profiles the caller may open directly, which adds unlisted ones.</summary>
    private IQueryable<ProfileEntity> Fetchable(AccountEntity caller)
    {
        var isModerator = caller.IsModerator;
        return db.Profiles.Where(p =>
            !p.IsDeleted
            && (p.Visibility != ProfileVisibility.Private || p.OwnerAccountId == caller.Id || isModerator));
    }

    private IQueryable<ProfileEntity> WithDetail(IQueryable<ProfileEntity> q) =>
        q.Include(p => p.Tags).Include(p => p.Hooks).Include(p => p.Images);

    // --- Reads -----------------------------------------------------------

    public async Task<PagedResult<ProfileDto>> QueryAsync(
        ProfileQuery query,
        AccountEntity caller,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var q = Listable(caller);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(p =>
                EF.Functions.Like(p.Name, term, "\\")
                || EF.Functions.Like(p.Title ?? string.Empty, term, "\\")
                || EF.Functions.Like(p.Overview ?? string.Empty, term, "\\")
                || p.Hooks.Any(h => EF.Functions.Like(h.Text, term, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(query.Race))
            q = q.Where(p => p.Race == query.Race);

        if (!string.IsNullOrWhiteSpace(query.World))
            q = q.Where(p => p.WorldName == query.World);

        if (!string.IsNullOrWhiteSpace(query.DataCenter))
            q = q.Where(p => p.DataCenter == query.DataCenter);

        // Each selected tag adds its own EXISTS, which is what makes these combine with AND.
        // Building one predicate per tag rather than a single IN is the difference between
        // "has all of these" and "has any of these".
        foreach (var trait in query.Personality.Distinct())
        {
            var value = (int)trait;
            q = q.Where(p => p.Tags.Any(t => t.Kind == ProfileTagKind.Personality && t.Value == value));
        }

        foreach (var tone in query.Tones.Distinct())
        {
            var value = (int)tone;
            q = q.Where(p => p.Tags.Any(t => t.Kind == ProfileTagKind.Tone && t.Value == value));
        }

        foreach (var activity in query.Activities.Distinct())
        {
            var value = (int)activity;
            q = q.Where(p => p.Tags.Any(t => t.Kind == ProfileTagKind.Activity && t.Value == value));
        }

        if (query.Length is { } length)
        {
            var value = (int)length;
            q = q.Where(p => p.Tags.Any(t => t.Kind == ProfileTagKind.Length && t.Value == value));
        }

        if (query.WalkupsOnly)
            q = q.Where(p => p.WalkupsWelcome);

        if (query.IncludeMature)
        {
            // Narrowing to particular themes only applies once adult content has been asked for at
            // all, so this can never widen a search past what the caller opted into.
            foreach (var theme in query.MatureThemes.Distinct())
            {
                var value = (int)theme;
                q = q.Where(p => p.Tags.Any(t => t.Kind == ProfileTagKind.Mature && t.Value == value));
            }
        }
        else
        {
            q = q.Where(p => !p.IsMature);
        }

        if (query.OwnerAccountId is { } owner)
            q = q.Where(p => p.OwnerAccountId == owner);

        if (query.ActiveWithinDays is { } days and > 0)
        {
            var since = now.AddDays(-days);
            q = q.Where(p => p.LastActiveAt != null && p.LastActiveAt >= since);
        }

        if (query.AvailableOnly)
        {
            // Open means: a beacon of theirs is burning, they take walk-ups, and they have not
            // overridden themselves to busy.
            q = q.Where(p =>
                p.WalkupsWelcome
                && p.Availability == AvailabilityOverride.Derived
                && db.Beacons.Any(b =>
                    !b.IsDeleted
                    && b.IsLit
                    && b.LitUntil != null
                    && b.LitUntil > now
                    && b.LitByName == p.CharacterName
                    && b.WorldId == p.WorldId));
        }

        var total = await q.CountAsync(ct);

        q = query.Sort switch
        {
            ProfileSort.Newest => q.OrderByDescending(p => p.CreatedAt),
            ProfileSort.Name => q.OrderBy(p => p.Name),
            ProfileSort.RecentlyActive => q.OrderByDescending(p => p.LastActiveAt),

            // Relevance: who is out there now, then who was recently, then who is new.
            _ => q.OrderByDescending(p => p.LastActiveAt).ThenByDescending(p => p.CreatedAt),
        };

        var pageSize = Math.Clamp(query.PageSize, 1, ProfileLimits.MaxPageSize);
        var page = Math.Max(0, query.Page);

        var rows = await WithDetail(q).Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
        var beacons = await LitBeaconsForAsync(rows, ct);

        return new PagedResult<ProfileDto>
        {
            Items = rows.Select(p => p.ToDto(Match(beacons, p), links: null, includeProse: false)).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
        };
    }

    public async Task<OperationResult<ProfileDto>> GetAsync(Guid id, AccountEntity caller, CancellationToken ct)
    {
        var profile = await WithDetail(Fetchable(caller)).FirstOrDefaultAsync(p => p.Id == id, ct);
        return profile is null
            ? OperationResult<ProfileDto>.NotFound("No such profile.")
            : OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    public async Task<OperationResult<ProfileDto>> GetByShareCodeAsync(string code, AccountEntity caller, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var profile = await WithDetail(Fetchable(caller)).FirstOrDefaultAsync(p => p.ShareCode == normalized, ct);

        return profile is null
            ? OperationResult<ProfileDto>.NotFound("No profile carries that code.")
            : OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    /// <summary>The lookup behind "who is this person standing in front of me".</summary>
    public async Task<OperationResult<ProfileDto>> GetByCharacterAsync(
        string name,
        uint worldId,
        AccountEntity caller,
        CancellationToken ct)
    {
        var cleaned = Validation.CleanLine(name);
        var profile = await WithDetail(Fetchable(caller))
            .FirstOrDefaultAsync(p => p.CharacterName == cleaned && p.WorldId == worldId, ct);

        return profile is null
            ? OperationResult<ProfileDto>.NotFound("That character has no profile.")
            : OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    // --- Writes ----------------------------------------------------------

    /// <summary>
    /// Creates or replaces the caller's profile for one character.
    ///
    /// The character must already be claimed by this account, which is what stops anyone writing a
    /// card for a character they do not play.
    /// </summary>
    public async Task<OperationResult<ProfileDto>> SaveAsync(
        SaveProfileRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        if (caller.IsBanned)
            return OperationResult<ProfileDto>.Forbidden("This account cannot publish profiles.");

        var characterName = Validation.CleanLine(request.CharacterName);
        if (characterName.Length < 2 || request.WorldId == 0)
            return OperationResult<ProfileDto>.Invalid("That character name or world is not valid.");

        var owns = await db.Characters.AnyAsync(
            c => c.AccountId == caller.Id && c.Name == characterName && c.WorldId == request.WorldId,
            ct);

        if (!owns)
            return OperationResult<ProfileDto>.Forbidden("Claim that character before writing its profile.");

        if (Validate(request) is { } error)
            return OperationResult<ProfileDto>.Invalid(error);

        // A card cannot declare adult content from an account that has not confirmed its holder is an
        // adult. Checked here rather than only in the editor, because the editor is a program running
        // on somebody else's computer.
        if (request.Style.MatureThemes.Count > 0 && caller.AdultConfirmedAt is null)
        {
            return OperationResult<ProfileDto>.Forbidden(
                "Confirm you are an adult in the settings before marking a card with mature themes.");
        }

        var profile = await WithDetail(db.Profiles)
            .FirstOrDefaultAsync(p => p.CharacterName == characterName && p.WorldId == request.WorldId, ct);

        var now = DateTimeOffset.UtcNow;
        var isNew = profile is null;

        if (profile is null)
        {
            profile = new ProfileEntity
            {
                Id = Guid.NewGuid(),
                OwnerAccountId = caller.Id,
                CharacterName = characterName,
                WorldId = request.WorldId,
                ShareCode = await MintShareCodeAsync(ct),
                CreatedAt = now,
            };

            db.Profiles.Add(profile);
        }
        else if (profile.OwnerAccountId != caller.Id && !caller.IsModerator)
        {
            return OperationResult<ProfileDto>.Forbidden("That character's profile belongs to another account.");
        }
        else if (profile.IsDeleted)
        {
            // Re-publishing a retired card revives it rather than colliding with the unique index.
            profile.IsDeleted = false;
        }

        Apply(profile, request);
        profile.UpdatedAt = now;

        // Tags and hooks are replaced wholesale: the editor always sends the complete set, and
        // diffing them would be more code for an identical result.
        db.ProfileTags.RemoveRange(profile.Tags);
        db.ProfileHooks.RemoveRange(profile.Hooks);
        await db.SaveChangesAsync(ct);

        profile.Tags = ProfileMapper.BuildTags(profile.Id, request);
        profile.Hooks = CleanHooks(profile.Id, request.Hooks);

        db.ProfileTags.AddRange(profile.Tags);
        db.ProfileHooks.AddRange(profile.Hooks);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Profile {Id} {Action} for {Character} by {Account}.",
            profile.Id,
            isNew ? "created" : "updated",
            characterName,
            caller.Id);

        return OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    public async Task<OperationResult<ProfileDto>> SetAvailabilityAsync(
        Guid id,
        AvailabilityOverride availability,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await WithDetail(db.Profiles).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<ProfileDto>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<ProfileDto>.Forbidden("That is not your profile.");

        profile.Availability = availability;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, AccountEntity caller, CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<bool>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id && !caller.IsModerator)
            return OperationResult<bool>.Forbidden("That is not your profile.");

        profile.IsDeleted = true;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return OperationResult<bool>.Ok(true);
    }

    // --- Last active -----------------------------------------------------

    /// <summary>
    /// Records that a character was seen standing at a lit beacon.
    ///
    /// Verified rather than trusted: the beacon must actually be burning, and the reported position
    /// must be within the same range that lighting one requires. Without that check this is a field
    /// any client could set to "just now" forever, and the signal people are using to judge whether a
    /// community is alive would mean nothing.
    ///
    /// Returns false when the heartbeat was throttled or rejected, which callers treat as a no-op.
    /// </summary>
    public async Task<OperationResult<bool>> HeartbeatAsync(
        ActivityHeartbeatRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        var characterName = Validation.CleanLine(request.CharacterName);

        var profile = await db.Profiles.FirstOrDefaultAsync(
            p => p.CharacterName == characterName && p.WorldId == request.WorldId && !p.IsDeleted,
            ct);

        if (profile is null)
            return OperationResult<bool>.NotFound("That character has no profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<bool>.Forbidden("That is not your character.");

        var now = DateTimeOffset.UtcNow;

        // Throttle server-side as well as in the client. Accepting every heartbeat would turn a
        // cosmetic timestamp into a write on every tick of every player's framework loop.
        if (profile.LastActiveAt is { } last && now - last < ProfileLimits.ActivityHeartbeatInterval)
            return OperationResult<bool>.Ok(false);

        var beacon = await db.Beacons.FirstOrDefaultAsync(b => b.Id == request.BeaconId && !b.IsDeleted, ct);
        if (beacon is null)
            return OperationResult<bool>.NotFound("That beacon no longer exists.");

        if (!beacon.IsLit || beacon.LitUntil is null || beacon.LitUntil <= now)
            return OperationResult<bool>.Invalid("That beacon is not lit.");

        if (beacon.WorldId != request.WorldId || beacon.TerritoryId != request.TerritoryId)
            return OperationResult<bool>.Invalid("You are not where that beacon is.");

        var dx = beacon.X - request.X;
        var dz = beacon.Z - request.Z;
        if (MathF.Sqrt((dx * dx) + (dz * dz)) > BeaconLimits.LightingRangeYalms)
            return OperationResult<bool>.Invalid("You are too far from that beacon.");

        profile.LastActiveAt = now;
        await db.SaveChangesAsync(ct);

        return OperationResult<bool>.Ok(true);
    }

    // --- Relationships ---------------------------------------------------

    public async Task<OperationResult<bool>> AddLinkAsync(
        Guid id,
        AddLinkRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<bool>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<bool>.Forbidden("That is not your profile.");

        if (request.OtherProfileId == id)
            return OperationResult<bool>.Invalid("A character cannot be tied to themselves.");

        if (!await db.Profiles.AnyAsync(p => p.Id == request.OtherProfileId && !p.IsDeleted, ct))
            return OperationResult<bool>.NotFound("No such character.");

        var count = await db.ProfileLinks.CountAsync(l => l.ProfileId == id, ct);
        if (count >= ProfileLimits.MaxRelationships)
            return OperationResult<bool>.Forbidden($"A profile may hold {ProfileLimits.MaxRelationships} ties.");

        var existing = await db.ProfileLinks
            .FirstOrDefaultAsync(l => l.ProfileId == id && l.OtherProfileId == request.OtherProfileId, ct);

        var note = Validation.CleanLine(request.Note);
        if (note.Length > ProfileLimits.RelationshipNoteMaxLength)
            note = note[..ProfileLimits.RelationshipNoteMaxLength];

        if (existing is not null)
        {
            existing.Kind = request.Kind;
            existing.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        }
        else
        {
            db.ProfileLinks.Add(new ProfileLinkEntity
            {
                Id = Guid.NewGuid(),
                ProfileId = id,
                OtherProfileId = request.OtherProfileId,
                Kind = request.Kind,
                Note = string.IsNullOrWhiteSpace(note) ? null : note,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return OperationResult<bool>.Ok(true);
    }

    /// <summary>
    /// Agrees to a tie somebody claimed. Confirming records the matching claim in the other direction,
    /// so both cards show it and either party can undo it later.
    /// </summary>
    public async Task<OperationResult<bool>> ConfirmLinkAsync(
        Guid id,
        Guid otherProfileId,
        AccountEntity caller,
        CancellationToken ct)
    {
        var mine = await db.Profiles.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (mine is null)
            return OperationResult<bool>.NotFound("No such profile.");

        if (mine.OwnerAccountId != caller.Id)
            return OperationResult<bool>.Forbidden("That is not your profile.");

        var claim = await db.ProfileLinks
            .FirstOrDefaultAsync(l => l.ProfileId == otherProfileId && l.OtherProfileId == id, ct);

        if (claim is null)
            return OperationResult<bool>.NotFound("Nobody has claimed a tie to you there.");

        var now = DateTimeOffset.UtcNow;
        claim.ConfirmedAt = now;

        var mirror = await db.ProfileLinks
            .FirstOrDefaultAsync(l => l.ProfileId == id && l.OtherProfileId == otherProfileId, ct);

        if (mirror is null)
        {
            db.ProfileLinks.Add(new ProfileLinkEntity
            {
                Id = Guid.NewGuid(),
                ProfileId = id,
                OtherProfileId = otherProfileId,
                Kind = claim.Kind,
                ConfirmedAt = now,
                CreatedAt = now,
            });
        }
        else
        {
            mirror.ConfirmedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return OperationResult<bool>.Ok(true);
    }

    public async Task<OperationResult<bool>> RemoveLinkAsync(
        Guid id,
        Guid otherProfileId,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
            return OperationResult<bool>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<bool>.Forbidden("That is not your profile.");

        var links = await db.ProfileLinks
            .Where(l => (l.ProfileId == id && l.OtherProfileId == otherProfileId)
                        || (l.ProfileId == otherProfileId && l.OtherProfileId == id))
            .ToListAsync(ct);

        // Removes both directions: a confirmed tie one party has withdrawn from is not a tie.
        db.ProfileLinks.RemoveRange(links);
        await db.SaveChangesAsync(ct);

        return OperationResult<bool>.Ok(true);
    }

    // --- Gallery ---------------------------------------------------------

    public async Task<OperationResult<ProfileDto>> UpdateGalleryAsync(
        Guid id,
        UpdateGalleryRequest request,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await WithDetail(db.Profiles).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<ProfileDto>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<ProfileDto>.Forbidden("That is not your profile.");

        var known = profile.Images.ToDictionary(i => i.ImageId);

        foreach (var entry in request.Images)
        {
            if (!known.TryGetValue(entry.ImageId, out var image))
                continue;

            image.Category = entry.Category;
            image.Order = entry.Order;

            var caption = Validation.CleanLine(entry.Caption);
            image.Caption = string.IsNullOrWhiteSpace(caption)
                ? null
                : caption.Length > ProfileLimits.CaptionMaxLength
                    ? caption[..ProfileLimits.CaptionMaxLength]
                    : caption;
        }

        // The portrait must be one of this profile's own images, or nothing.
        profile.PortraitImageId = request.PortraitImageId is { } portrait && known.ContainsKey(portrait)
            ? portrait
            : null;

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return OperationResult<ProfileDto>.Ok(await HydrateAsync(profile, ct));
    }

    public async Task<OperationResult<ProfileDto>> AddImageAsync(
        Guid id,
        Guid imageId,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await WithDetail(db.Profiles).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<ProfileDto>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<ProfileDto>.Forbidden("That is not your profile.");

        if (profile.Images.Count >= ProfileLimits.MaxGalleryImages)
        {
            return OperationResult<ProfileDto>.Forbidden(
                $"A gallery holds {ProfileLimits.MaxGalleryImages} images. Remove one first.");
        }

        var now = DateTimeOffset.UtcNow;
        var image = new ProfileImageEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            ImageId = imageId,
            Category = profile.Images.Count == 0 ? GalleryCategory.Portrait : GalleryCategory.Other,
            Order = profile.Images.Count,
            CreatedAt = now,
        };

        // Added through the DbSet, which states the intent plainly. Adding it to the tracked navigation
        // collection as well is what produced a duplicate in the response; adding it only there left
        // EF treating it as a modification of a row that does not exist yet. The gallery is re-read
        // after saving instead, so the response reflects what was actually written.
        db.ProfileImages.Add(image);

        // The first image uploaded becomes the portrait, because a card with a gallery and no face
        // on it is the most common way this ends up looking broken.
        profile.PortraitImageId ??= imageId;
        profile.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return await ReloadAsync(profile.Id, ct);
    }

    public async Task<OperationResult<ProfileDto>> RemoveImageAsync(
        Guid id,
        Guid imageId,
        AccountEntity caller,
        CancellationToken ct)
    {
        var profile = await WithDetail(db.Profiles).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (profile is null)
            return OperationResult<ProfileDto>.NotFound("No such profile.");

        if (profile.OwnerAccountId != caller.Id)
            return OperationResult<ProfileDto>.Forbidden("That is not your profile.");

        var image = profile.Images.FirstOrDefault(i => i.ImageId == imageId);
        if (image is null)
            return OperationResult<ProfileDto>.NotFound("That image is not in this gallery.");

        db.ProfileImages.Remove(image);
        profile.Images.Remove(image);

        if (profile.PortraitImageId == imageId)
            profile.PortraitImageId = profile.Images.OrderBy(i => i.Order).FirstOrDefault()?.ImageId;

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        images.Delete(imageId);
        return await ReloadAsync(profile.Id, ct);
    }

    // --- Plumbing --------------------------------------------------------

    /// <summary>
    /// Re-reads a profile after a write that changed its child collections.
    ///
    /// Used instead of mapping the tracked instance, because whether an added child appears in a
    /// tracked navigation collection depends on change-tracker fixup, and getting that wrong produces
    /// either a duplicate or a missing entry in the response. Reading it back cannot be wrong.
    /// </summary>
    private async Task<OperationResult<ProfileDto>> ReloadAsync(Guid id, CancellationToken ct)
    {
        var fresh = await WithDetail(db.Profiles.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, ct);
        return fresh is null
            ? OperationResult<ProfileDto>.NotFound("No such profile.")
            : OperationResult<ProfileDto>.Ok(await HydrateAsync(fresh, ct));
    }

    /// <summary>Fills in the parts of a profile that live outside its own rows: presence and ties.</summary>
    private async Task<ProfileDto> HydrateAsync(ProfileEntity profile, CancellationToken ct)
    {
        var beacons = await LitBeaconsForAsync([profile], ct);

        var links = await db.ProfileLinks
            .Where(l => l.ProfileId == profile.Id)
            .Join(db.Profiles, l => l.OtherProfileId, p => p.Id, (l, p) => new { l, p.Name })
            .ToListAsync(ct);

        var mapped = links
            .Select(x => new ProfileLink
            {
                OtherProfileId = x.l.OtherProfileId,
                OtherName = x.Name,
                Kind = x.l.Kind,
                Note = x.l.Note,
                Confirmed = x.l.ConfirmedAt is not null,
            })
            .ToList();

        return profile.ToDto(Match(beacons, profile), mapped);
    }

    /// <summary>
    /// Finds the burning beacon, if any, for each of these characters.
    ///
    /// One query for the whole page rather than one per row: a thirty-card search would otherwise be
    /// thirty round trips to answer a question that is a single IN clause.
    /// </summary>
    private async Task<List<BeaconEntity>> LitBeaconsForAsync(
        IReadOnlyCollection<ProfileEntity> profiles,
        CancellationToken ct)
    {
        if (profiles.Count == 0)
            return [];

        var now = DateTimeOffset.UtcNow;
        var names = profiles.Select(p => p.CharacterName).Distinct().ToList();
        var worlds = profiles.Select(p => p.WorldId).Distinct().ToList();

        return await db.Beacons
            .Where(b => !b.IsDeleted
                        && b.IsLit
                        && b.LitUntil != null
                        && b.LitUntil > now
                        && b.LitByName != null
                        && names.Contains(b.LitByName)
                        && worlds.Contains(b.WorldId))
            .ToListAsync(ct);
    }

    private static BeaconEntity? Match(List<BeaconEntity> beacons, ProfileEntity profile) =>
        beacons.FirstOrDefault(b => b.LitByName == profile.CharacterName && b.WorldId == profile.WorldId);

    private static void Apply(ProfileEntity profile, SaveProfileRequest request)
    {
        profile.WorldName = Validation.CleanLine(request.WorldName);
        profile.DataCenter = Validation.CleanLine(request.DataCenter);

        var identity = request.Identity;
        profile.Name = Validation.CleanLine(identity.Name);
        profile.Title = NullIfEmpty(Validation.CleanLine(identity.Title));
        profile.Race = NullIfEmpty(Validation.CleanLine(identity.Race));
        profile.Clan = NullIfEmpty(Validation.CleanLine(identity.Clan));
        profile.Age = identity.Age;
        profile.Gender = NullIfEmpty(Validation.CleanLine(identity.Gender));
        profile.Pronouns = NullIfEmpty(Validation.CleanLine(identity.Pronouns));
        profile.ArchetypeCsv = ProfileMapper.JoinArchetype(identity.Archetype);
        profile.Quote = NullIfEmpty(Validation.CleanLine(identity.Quote));

        var style = request.Style;
        profile.Length = style.Length;
        profile.Boundaries = NullIfEmpty(Validation.CleanLine(style.Boundaries));
        profile.WalkupsWelcome = style.WalkupsWelcome;

        // Derived, never taken from the request. Were it a field of its own, a card could declare
        // itself open to explicit scenes and still turn up in a search made by somebody who asked not
        // to see any adult content at all, which is the one failure this must not permit.
        profile.IsMature = style.MatureThemes.Count > 0;

        profile.PlayerTimezone = NullIfEmpty(Validation.CleanLine(request.Player.Timezone));
        profile.PlayerAvailability = NullIfEmpty(Validation.CleanLine(request.Player.Availability));
        profile.PlayerContact = NullIfEmpty(Validation.CleanLine(request.Player.Contact));

        profile.Overview = NullIfEmpty(Validation.CleanBlock(request.Overview));
        profile.History = NullIfEmpty(Validation.CleanBlock(request.History));
        profile.Goals = NullIfEmpty(Validation.CleanBlock(request.Goals));

        profile.Visibility = request.Visibility;
        profile.Availability = request.Availability;
    }

    private static List<ProfileHookEntity> CleanHooks(Guid profileId, IReadOnlyList<string> hooks)
    {
        var order = 0;
        return hooks
            .Select(h => Validation.CleanLine(h))
            .Where(h => h.Length > 0)
            .Select(h => h.Length > ProfileLimits.HookMaxLength ? h[..ProfileLimits.HookMaxLength] : h)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ProfileLimits.MaxHooks)
            .Select(h => new ProfileHookEntity
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                Text = h,
                Order = order++,
            })
            .ToList();
    }

    private static string? Validate(SaveProfileRequest request)
    {
        var name = Validation.CleanLine(request.Identity.Name);

        if (name.Length < ProfileLimits.NameMinLength)
            return $"A character name needs at least {ProfileLimits.NameMinLength} characters.";

        if (name.Length > ProfileLimits.NameMaxLength)
            return $"A character name cannot exceed {ProfileLimits.NameMaxLength} characters.";

        if ((request.Overview?.Length ?? 0) > ProfileLimits.OverviewMaxLength)
            return $"The overview cannot exceed {ProfileLimits.OverviewMaxLength} characters.";

        if ((request.History?.Length ?? 0) > ProfileLimits.HistoryMaxLength)
            return $"The history cannot exceed {ProfileLimits.HistoryMaxLength} characters.";

        if ((request.Identity.Quote?.Length ?? 0) > ProfileLimits.QuoteMaxLength)
            return $"The quote cannot exceed {ProfileLimits.QuoteMaxLength} characters.";

        if ((request.Style.Boundaries?.Length ?? 0) > ProfileLimits.BoundariesMaxLength)
            return $"Boundaries cannot exceed {ProfileLimits.BoundariesMaxLength} characters.";

        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<string> MintShareCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = BeaconMapper.NewShareCode();
            if (!await db.Profiles.AnyAsync(p => p.ShareCode == code, ct))
                return code;
        }

        throw new InvalidOperationException("Could not mint a unique share code.");
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
