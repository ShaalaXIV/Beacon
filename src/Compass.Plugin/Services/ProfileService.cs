using System.Collections.Concurrent;
using Compass.Shared.Beacons;
using Compass.Shared.Profiles;

namespace Compass.Services;

/// <summary>
/// The Chronicle as the plugin sees it: your own cards, whatever search is open, and the quiet
/// heartbeat that records you were out at a fire.
///
/// Follows the same threading rule as <see cref="AtlasService"/>: API results land on a queue and are
/// applied on the framework thread, so the UI never reads a list while it is being replaced.
/// </summary>
public sealed class ProfileService(
    Configuration config,
    CompassApi api,
    LocationService location,
    AtlasService atlas,
    NotificationService notifications) : IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// How often to look for a lit beacon to report standing at.
    ///
    /// Well under the server's five-minute throttle, because the check itself is local and cheap and
    /// a player may only pass through a fire briefly.
    /// </summary>
    private static readonly TimeSpan HeartbeatCheck = TimeSpan.FromSeconds(60);

    private readonly ConcurrentQueue<Action> pending = new();

    private readonly CancellationTokenSource lifetime = new();

    private DateTime searchDueAt = DateTime.MaxValue;

    private DateTime nextHeartbeatCheck = DateTime.MinValue;

    private DateTime lastHeartbeatSent = DateTime.MinValue;

    private CancellationTokenSource? inFlight;

    // --- State ----------------------------------------------------------

    /// <summary>Every card this account owns.</summary>
    public List<ProfileDto> MyProfiles { get; private set; } = [];

    /// <summary>The card for the character currently logged in, if there is one.</summary>
    public ProfileDto? MyProfile =>
        MyProfiles.FirstOrDefault(p =>
            p.CharacterName == location.CurrentCharacterName && p.WorldId == location.CurrentWorldId);

    public List<ProfileDto> Results { get; private set; } = [];

    public int TotalMatches { get; private set; }

    public ProfileQuery Query { get; private set; } = new();

    public bool Loading { get; private set; }

    public string? LastError { get; private set; }

    public Guid? SelectedId { get; private set; }

    /// <summary>The card being read, whether it came from a search or a direct lookup.</summary>
    public ProfileDto? Selected =>
        SelectedId is { } id
            ? Results.FirstOrDefault(p => p.Id == id)
              ?? MyProfiles.FirstOrDefault(p => p.Id == id)
              ?? fetched.GetValueOrDefault(id)
            : null;

    /// <summary>Cards pulled in individually, which are not in any current result list.</summary>
    private readonly Dictionary<Guid, ProfileDto> fetched = [];

    // --- Lifecycle ------------------------------------------------------

    public void Start() => RefreshMine();

    public void Tick()
    {
        while (pending.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "Compass: a queued profile update failed.");
            }
        }

        var now = DateTime.UtcNow;

        if (now >= searchDueAt)
        {
            searchDueAt = DateTime.MaxValue;
            Refresh();
        }

        if (now >= nextHeartbeatCheck)
        {
            nextHeartbeatCheck = now + HeartbeatCheck;
            ReportPresenceIfAtAFire();
        }
    }

    // --- Last active ----------------------------------------------------

    /// <summary>
    /// Records that this character is standing at a lit beacon.
    ///
    /// Only fires when there is genuinely a burning beacon within reach, so the signal means somebody
    /// was out roleplaying rather than merely logged in. The server verifies the position again and
    /// throttles regardless, so being wrong here is harmless.
    /// </summary>
    private void ReportPresenceIfAtAFire()
    {
        if (!config.ShareActivity || !Svc.InWorld || !config.HasAccount)
            return;

        // The server throttles to five minutes; do not bother it more often than that.
        if (DateTime.UtcNow - lastHeartbeatSent < ProfileLimits.ActivityHeartbeatInterval)
            return;

        if (MyProfile is null)
            return;

        var beacon = atlas.Beacons
            .Concat(atlas.MyBeacons)
            .DistinctBy(b => b.Id)
            .Where(b => b.Flame.IsBurning)
            .Select(b => (Beacon: b, Distance: location.DistanceTo(b)))
            .Where(x => x.Distance is not null && x.Distance <= BeaconLimits.LightingRangeYalms)
            .OrderBy(x => x.Distance)
            .Select(x => x.Beacon)
            .FirstOrDefault();

        if (beacon is null)
            return;

        var position = location.CurrentPosition;
        if (position is null)
            return;

        lastHeartbeatSent = DateTime.UtcNow;

        var request = new ActivityHeartbeatRequest
        {
            BeaconId = beacon.Id,
            CharacterName = location.CurrentCharacterName,
            WorldId = location.CurrentWorldId,
            TerritoryId = (ushort)Svc.ClientState.TerritoryType,
            X = position.Value.X,
            Y = position.Value.Y,
            Z = position.Value.Z,
        };

        _ = Task.Run(async () =>
        {
            var recorded = await api.HeartbeatAsync(request, lifetime.Token);
            if (recorded)
                Svc.Log.Debug("Compass: presence recorded at {Beacon}.", beacon.Name);
        }, lifetime.Token);
    }

    // --- Searching ------------------------------------------------------

    public void SetQuery(ProfileQuery query, bool immediate = false)
    {
        Query = query with { Page = 0 };

        if (immediate)
            Refresh();
        else
            searchDueAt = DateTime.UtcNow + SearchDebounce;
    }

    public void GoToPage(int page)
    {
        Query = Query with { Page = Math.Max(0, page) };
        Refresh();
    }

    public void Select(Guid? id) => SelectedId = id;

    public void Refresh()
    {
        inFlight?.Cancel();
        inFlight?.Dispose();
        inFlight = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = inFlight.Token;

        Loading = true;
        var query = Query;

        _ = Task.Run(async () =>
        {
            var result = await api.SearchProfilesAsync(query, token);
            if (token.IsCancellationRequested)
                return;

            Post(() =>
            {
                Loading = false;

                if (result.Ok && result.Value is { } page)
                {
                    Results = [.. page.Items];
                    TotalMatches = page.Total;
                    LastError = null;
                }
                else
                {
                    LastError = result.Error;
                }
            });
        }, token);
    }

    public void RefreshMine()
    {
        if (!config.HasAccount)
            return;

        _ = Task.Run(async () =>
        {
            var result = await api.MyProfilesAsync(lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } page)
                    MyProfiles = [.. page.Items];
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);
    }

    /// <summary>Opens the card of a character you have just met.</summary>
    public void OpenCharacter(string name, uint worldId, Action<ProfileDto>? onFound = null) =>
        _ = Task.Run(async () =>
        {
            var result = await api.GetProfileByCharacterAsync(name, worldId, lifetime.Token);

            Post(() =>
            {
                if (!result.Ok || result.Value is not { } profile)
                {
                    LastError = result.Error ?? $"{name} has no profile.";
                    return;
                }

                fetched[profile.Id] = profile;
                SelectedId = profile.Id;
                onFound?.Invoke(profile);
            });
        }, lifetime.Token);

    public void OpenShareCode(string code, Action<ProfileDto>? onFound = null) =>
        _ = Task.Run(async () =>
        {
            var result = await api.GetProfileByShareCodeAsync(code, lifetime.Token);

            Post(() =>
            {
                if (!result.Ok || result.Value is not { } profile)
                {
                    LastError = result.Error ?? "No profile carries that code.";
                    return;
                }

                fetched[profile.Id] = profile;
                SelectedId = profile.Id;
                onFound?.Invoke(profile);
            });
        }, lifetime.Token);

    // --- Writing --------------------------------------------------------

    public void Save(SaveProfileRequest request, byte[]? portrait, string? fileName, Action<ProfileDto>? onSaved = null)
    {
        _ = Task.Run(async () =>
        {
            var saved = await api.SaveProfileAsync(request, lifetime.Token);

            if (!saved.Ok || saved.Value is not { } profile)
            {
                Post(() => LastError = saved.Error);
                return;
            }

            // The portrait goes second: a card without its picture is still a card, whereas a picture
            // with nothing to attach it to is nothing.
            if (portrait is { Length: > 0 })
            {
                var uploaded = await api.UploadPortraitAsync(profile.Id, portrait, fileName ?? "portrait.png", lifetime.Token);
                if (uploaded.Ok && uploaded.Value is { } withImage)
                    profile = withImage;
                else
                    Post(() => LastError = $"The card was saved, but the portrait did not upload: {uploaded.Error}");
            }

            Post(() =>
            {
                Upsert(profile);
                notifications.Toast($"{profile.Identity.Name} recorded.", "Your card is in the Chronicle.");
                onSaved?.Invoke(profile);
            });
        }, lifetime.Token);
    }

    public void SetAvailability(ProfileDto profile, AvailabilityOverride availability) =>
        _ = Task.Run(async () =>
        {
            var result = await api.SetAvailabilityAsync(profile.Id, availability, lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } updated)
                    Upsert(updated);
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);

    public void RemoveImage(ProfileDto profile, Guid imageId) =>
        _ = Task.Run(async () =>
        {
            var result = await api.RemoveGalleryImageAsync(profile.Id, imageId, lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } updated)
                    Upsert(updated);
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);

    public void AddImage(ProfileDto profile, byte[] bytes, string fileName) =>
        _ = Task.Run(async () =>
        {
            var result = await api.UploadPortraitAsync(profile.Id, bytes, fileName, lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } updated)
                    Upsert(updated);
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);

    public void Delete(ProfileDto profile) =>
        _ = Task.Run(async () =>
        {
            var result = await api.DeleteProfileAsync(profile.Id, lifetime.Token);

            Post(() =>
            {
                if (!result.Ok)
                {
                    LastError = result.Error;
                    return;
                }

                MyProfiles.RemoveAll(p => p.Id == profile.Id);
                Results.RemoveAll(p => p.Id == profile.Id);
                fetched.Remove(profile.Id);

                if (SelectedId == profile.Id)
                    SelectedId = null;
            });
        }, lifetime.Token);

    public void ClearError() => LastError = null;

    // --- Plumbing -------------------------------------------------------

    private void Upsert(ProfileDto profile)
    {
        Replace(MyProfiles, profile);
        Replace(Results, profile);
        fetched[profile.Id] = profile;

        if (profile.OwnerAccountId == config.AccountId && MyProfiles.All(p => p.Id != profile.Id))
            MyProfiles.Add(profile);

        static void Replace(List<ProfileDto> list, ProfileDto profile)
        {
            var index = list.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
                list[index] = profile;
        }
    }

    private void Post(Action action) => pending.Enqueue(action);

    public void Dispose()
    {
        lifetime.Cancel();
        inFlight?.Cancel();
        inFlight?.Dispose();
        lifetime.Dispose();
    }
}
