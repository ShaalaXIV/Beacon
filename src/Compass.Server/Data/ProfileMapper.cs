using Compass.Server.Data.Entities;
using Compass.Shared.Profiles;

namespace Compass.Server.Data;

/// <summary>Translation between the profile tables and the wire shape.</summary>
public static class ProfileMapper
{
    public static string[] SplitArchetype(string? csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Normalises the three archetype words: trimmed, de-duplicated, length- and count-capped.</summary>
    public static string JoinArchetype(IEnumerable<string>? words)
    {
        if (words is null)
            return string.Empty;

        var cleaned = words
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().Replace('|', ' '))
            .Select(w => w.Length > ProfileLimits.ArchetypeWordMaxLength
                ? w[..ProfileLimits.ArchetypeWordMaxLength]
                : w)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ProfileLimits.MaxArchetypeWords)
            .ToArray();

        return cleaned.Length == 0 ? string.Empty : string.Join('|', cleaned);
    }

    /// <summary>Pulls the values of one tag kind off a profile's tag rows.</summary>
    public static List<T> TagsOf<T>(IEnumerable<ProfileTagEntity> tags, ProfileTagKind kind)
        where T : struct, Enum =>
        tags.Where(t => t.Kind == kind)
            .Select(t => (T)Enum.ToObject(typeof(T), t.Value))
            .Distinct()
            .ToList();

    /// <summary>
    /// Works out what the Chronicle should say about somebody right now.
    ///
    /// A manual override always wins, because somebody who has said they are busy means it. Otherwise
    /// availability comes from whether one of their beacons is burning, which is the point of the whole
    /// design: a status nobody has to remember to change cannot go stale.
    /// </summary>
    public static ProfilePresence BuildPresence(ProfileEntity profile, BeaconEntity? litBeacon)
    {
        var state = profile.Availability switch
        {
            AvailabilityOverride.Busy => AvailabilityState.Busy,
            AvailabilityOverride.StorySession => AvailabilityState.StorySession,
            AvailabilityOverride.Closed => AvailabilityState.Closed,
            _ when litBeacon is not null => profile.WalkupsWelcome
                ? AvailabilityState.OpenToWalkups
                : AvailabilityState.Busy,
            _ => AvailabilityState.Unknown,
        };

        return new ProfilePresence
        {
            State = state,
            LastActiveAt = profile.LastActiveAt,
            BeaconId = litBeacon?.Id,
            BeaconName = litBeacon?.Name,
            ZoneName = litBeacon?.ZoneName,
            WorldName = litBeacon?.WorldName ?? profile.WorldName,
            LitUntil = litBeacon?.LitUntil,
        };
    }

    public static ProfileDto ToDto(
        this ProfileEntity e,
        BeaconEntity? litBeacon = null,
        IReadOnlyList<ProfileLink>? links = null,
        bool includeProse = true) =>
        new()
        {
            Id = e.Id,
            OwnerAccountId = e.OwnerAccountId,
            CharacterName = e.CharacterName,
            WorldId = e.WorldId,
            WorldName = e.WorldName,
            DataCenter = e.DataCenter,
            Identity = new ProfileIdentity
            {
                Name = e.Name,
                Title = e.Title,
                Race = e.Race,
                Clan = e.Clan,
                Age = e.Age,
                Gender = e.Gender,
                Pronouns = e.Pronouns,
                Archetype = SplitArchetype(e.ArchetypeCsv),
                Quote = e.Quote,
            },
            Style = new ProfileStyle
            {
                Length = e.Length,
                Tones = TagsOf<RpTone>(e.Tags, ProfileTagKind.Tone),
                Activities = TagsOf<RpActivity>(e.Tags, ProfileTagKind.Activity),
                Boundaries = e.Boundaries,
                WalkupsWelcome = e.WalkupsWelcome,
                MatureThemes = TagsOf<MatureTheme>(e.Tags, ProfileTagKind.Mature),
                IsMature = e.IsMature,
            },
            Player = new PlayerNotes
            {
                Timezone = e.PlayerTimezone,
                Availability = e.PlayerAvailability,
                Contact = e.PlayerContact,
            },
            Presence = BuildPresence(e, litBeacon),
            Personality = TagsOf<PersonalityTrait>(e.Tags, ProfileTagKind.Personality),
            Hooks = e.Hooks
                .OrderBy(h => h.Order)
                .Select(h => new ProfileHook { Text = h.Text, Order = h.Order })
                .ToList(),
            Gallery = e.Images
                .OrderBy(i => i.Order)
                .Select(i => new ProfileImage
                {
                    ImageId = i.ImageId,
                    Category = i.Category,
                    Caption = i.Caption,
                    Order = i.Order,
                })
                .ToList(),
            Links = links ?? [],
            Overview = e.Overview,

            // Search results omit the long prose. A page of thirty cards would otherwise carry a
            // hundred kilobytes of backstory that nothing on screen displays.
            History = includeProse ? e.History : null,
            Goals = includeProse ? e.Goals : null,
            PortraitImageId = e.PortraitImageId,
            Visibility = e.Visibility,
            Availability = e.Availability,
            ShareCode = e.ShareCode,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

    /// <summary>Builds the tag rows for a profile from the typed selections on a save request.</summary>
    public static List<ProfileTagEntity> BuildTags(Guid profileId, SaveProfileRequest request)
    {
        var tags = new List<ProfileTagEntity>();

        void Add<T>(ProfileTagKind kind, IEnumerable<T>? values, int max)
            where T : struct, Enum
        {
            if (values is null)
                return;

            foreach (var value in values.Distinct().Take(max))
            {
                tags.Add(new ProfileTagEntity
                {
                    Id = Guid.NewGuid(),
                    ProfileId = profileId,
                    Kind = kind,
                    Value = Convert.ToInt32(value),
                });
            }
        }

        Add(ProfileTagKind.Personality, request.Personality, ProfileLimits.MaxPersonalityTraits);
        Add(ProfileTagKind.Tone, request.Style.Tones, ProfileLimits.MaxTones);
        Add(ProfileTagKind.Activity, request.Style.Activities, ProfileLimits.MaxActivities);
        Add(ProfileTagKind.Mature, request.Style.MatureThemes, ProfileLimits.MaxMatureThemes);

        // Length is single-valued, but lives in the same table so that a search can filter on it
        // with the same join as everything else.
        tags.Add(new ProfileTagEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Kind = ProfileTagKind.Length,
            Value = (int)request.Style.Length,
        });

        return tags;
    }
}
