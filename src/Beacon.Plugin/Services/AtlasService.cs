using System.Collections.Concurrent;
using Beacon.Shared.Accounts;
using Beacon.Shared.Beacons;

namespace Beacon.Services;

/// <summary>
/// The atlas as the plugin sees it: what has been fetched, what is being filtered for, and every
/// action a player can take on a beacon.
///
/// Everything that mutates visible state does so on the framework thread. API calls and hub events
/// arrive on threadpool threads and post their results to a queue that <see cref="Tick"/> drains,
/// so the UI never reads a list that is being rebuilt underneath it.
/// </summary>
public sealed class AtlasService(
    Configuration config,
    BeaconApi api,
    LocationService location,
    BeaconHubClient hub,
    ImageCache images,
    NotificationService notifications) : IDisposable
{
    /// <summary>Wait after the last keystroke before searching, so typing does not spam the server.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(350);

    /// <summary>How often the atlas refreshes itself when left open.</summary>
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(2);

    /// <summary>How often to check whether you have wandered away from a beacon you lit.</summary>
    private static readonly TimeSpan AbandonCheckInterval = TimeSpan.FromMinutes(3);

    private readonly ConcurrentQueue<Action> pending = new();

    private readonly CancellationTokenSource lifetime = new();

    private DateTime searchDueAt = DateTime.MaxValue;

    private DateTime nextAutoRefresh = DateTime.MinValue;

    private CancellationTokenSource? inFlight;

    private DateTime nextAbandonCheck = DateTime.MinValue;

    /// <summary>Beacons already warned about, so the reminder does not repeat every minute.</summary>
    private readonly HashSet<Guid> abandonWarned = [];

    // --- State ----------------------------------------------------------

    /// <summary>The current page of results.</summary>
    public List<BeaconDto> Beacons { get; private set; } = [];

    /// <summary>Total matches across all pages, for the pager.</summary>
    public int TotalMatches { get; private set; }

    public BeaconQuery Query { get; private set; } = new();

    public bool Loading { get; private set; }

    /// <summary>The last error worth showing the player, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>The account, once fetched.</summary>
    public AccountDto? Account { get; private set; }

    /// <summary>Id of the beacon selected in the atlas.</summary>
    public Guid? SelectedId { get; private set; }

    /// <summary>The selected beacon, if it is still in the current page.</summary>
    public BeaconDto? Selected => SelectedId is { } id ? Beacons.FirstOrDefault(b => b.Id == id) : null;

    /// <summary>Beacons owned by this account, kept separately so the "mine" tab does not disturb browsing.</summary>
    public List<BeaconDto> MyBeacons { get; private set; } = [];

    public bool Connected => hub.Connected;

    /// <summary>How many beacons in the current page are burning, for the status line.</summary>
    public int LitCount => Beacons.Count(b => b.Flame.IsBurning);

    // --- Lifecycle ------------------------------------------------------

    public void Start()
    {
        hub.Start();
        RefreshAccount();
        Refresh();
    }

    /// <summary>Drains queued work and runs the debounce timers. Called once per frame.</summary>
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
                Svc.Log.Error(ex, "Beacon: a queued atlas update failed.");
            }
        }

        foreach (var evt in hub.Drain())
            Apply(evt);

        var now = DateTime.UtcNow;

        if (now >= searchDueAt)
        {
            searchDueAt = DateTime.MaxValue;
            Refresh();
        }

        if (now >= nextAutoRefresh)
        {
            nextAutoRefresh = now + AutoRefreshInterval;

            // Skip the very first tick, which Start already covered.
            if (Beacons.Count > 0 || Account is not null)
                Refresh(quiet: true);
        }

        if (now >= nextAbandonCheck)
        {
            nextAbandonCheck = now + AbandonCheckInterval;
            CheckAbandonedFlames();
        }
    }

    /// <summary>
    /// Warns when you have left a beacon burning and walked away from it.
    ///
    /// This is the failure mode that quietly ruins an atlas: somebody lights their camp, logs off or
    /// wanders to a dungeon, and the beacon keeps advertising an empty field until it expires hours
    /// later. Travellers make the trip, find nobody, and stop trusting the flame. One nudge fixes it.
    /// </summary>
    private void CheckAbandonedFlames()
    {
        if (!config.RemindToExtinguish || !Svc.InWorld || config.AccountId == Guid.Empty)
            return;

        foreach (var beacon in MyBeacons.Concat(Beacons).DistinctBy(b => b.Id))
        {
            if (!beacon.Flame.IsBurning || beacon.Flame.LitByAccountId != config.AccountId)
            {
                abandonWarned.Remove(beacon.Id);
                continue;
            }

            var distance = location.DistanceTo(beacon);
            var away = distance is null || distance > BeaconLimits.LightingRangeYalms;

            if (!away)
            {
                // Back at the beacon: forget the warning so leaving again warns afresh.
                abandonWarned.Remove(beacon.Id);
                continue;
            }

            if (!abandonWarned.Add(beacon.Id))
                continue;

            notifications.Toast(
                $"{beacon.Name} is still lit",
                "You have wandered off. Put it out with /beacon out if you are done.");
        }
    }

    // --- Filtering ------------------------------------------------------

    /// <summary>Replaces the query and schedules a debounced refresh.</summary>
    public void SetQuery(BeaconQuery query, bool immediate = false)
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

    // --- Fetching -------------------------------------------------------

    public void Refresh(bool quiet = false)
    {
        // A new search supersedes one already running; without this, a slow early request can land
        // after a fast later one and repopulate the list with stale results.
        inFlight?.Cancel();
        inFlight?.Dispose();
        inFlight = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = inFlight.Token;

        if (!quiet)
            Loading = true;

        var query = Query;

        _ = Task.Run(async () =>
        {
            var result = await api.BrowseAsync(query, token);
            if (token.IsCancellationRequested)
                return;

            Post(() =>
            {
                Loading = false;

                if (result.Ok && result.Value is { } page)
                {
                    Beacons = [.. page.Items];
                    TotalMatches = page.Total;
                    LastError = null;
                }
                else if (!quiet)
                {
                    LastError = result.Error;
                }
            });
        }, token);
    }

    public void RefreshAccount()
    {
        if (!config.HasAccount)
            return;

        _ = Task.Run(async () =>
        {
            var result = await api.GetAccountAsync(lifetime.Token);

            Post(() =>
            {
                if (!result.Ok || result.Value is not { } account)
                {
                    LastError = result.Error;
                    return;
                }

                Account = account;
                config.AccountId = account.Id;
                config.DisplayName = account.DisplayName;
                config.Save();

                LinkCurrentCharacterIfNeeded(account);
            });
        }, lifetime.Token);
    }

    public void RefreshMine()
    {
        if (config.AccountId == Guid.Empty)
            return;

        var query = new BeaconQuery
        {
            OwnerAccountId = config.AccountId,
            Sort = BeaconSort.Newest,
            PageSize = BeaconLimits.MaxBeaconsPerAccount,
        };

        _ = Task.Run(async () =>
        {
            var result = await api.BrowseAsync(query, lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } page)
                    MyBeacons = [.. page.Items];
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);
    }

    /// <summary>
    /// Claims the logged-in character for this account, once, when it is not already claimed.
    /// Silent on failure: someone else may legitimately hold that character, and nagging about it
    /// every login would be worse than the plugin simply not crediting that alt.
    /// </summary>
    private void LinkCurrentCharacterIfNeeded(AccountDto account)
    {
        if (!config.AutoLinkCharacter || !Svc.InWorld)
            return;

        var name = location.CurrentCharacterName;
        var worldId = location.CurrentWorldId;

        if (string.IsNullOrWhiteSpace(name) || worldId == 0)
            return;

        if (account.Characters.Any(c => c.WorldId == worldId && c.Name == name))
            return;

        var request = new LinkCharacterRequest
        {
            Name = name,
            WorldId = worldId,
            WorldName = location.CurrentWorldName,
            DataCenter = location.CurrentDataCenter,
        };

        _ = Task.Run(async () =>
        {
            var result = await api.LinkCharacterAsync(request, lifetime.Token);
            if (result.Ok && result.Value is { } updated)
                Post(() => Account = updated);
            else
                Svc.Log.Debug("Beacon: could not claim {Character} - {Error}", name, result.Error ?? "unknown");
        }, lifetime.Token);
    }

    // --- Actions --------------------------------------------------------

    /// <summary>Lights a beacon the player is standing at.</summary>
    public void Light(BeaconDto beacon, int minutes, string? note)
    {
        var position = location.CurrentPosition;
        if (position is null)
        {
            LastError = "You need to be in the world to light a beacon.";
            return;
        }

        var request = new LightBeaconRequest
        {
            Minutes = minutes,
            Note = note,
            CharacterName = location.CurrentCharacterName,
            WorldId = location.CurrentWorldId,
            TerritoryId = (ushort)Svc.ClientState.TerritoryType,
            X = position.Value.X,
            Y = position.Value.Y,
            Z = position.Value.Z,
        };

        Mutate(
            () => api.LightAsync(beacon.Id, request, lifetime.Token),
            updated => notifications.Toast($"{updated.Name} is lit.", "Your beacon is burning."));
    }

    public void Stoke(BeaconDto beacon, int minutes, string? note) =>
        Mutate(
            () => api.StokeAsync(beacon.Id, new StokeBeaconRequest { Minutes = minutes, Note = note }, lifetime.Token),
            updated => notifications.Toast($"{updated.Name} will burn longer.", null));

    public void Extinguish(BeaconDto beacon) =>
        Mutate(
            () => api.ExtinguishAsync(beacon.Id, lifetime.Token),
            updated => notifications.Toast($"{updated.Name} is dark.", null));

    public void ToggleFavorite(BeaconDto beacon) =>
        Mutate(() => api.SetFavoriteAsync(beacon.Id, !beacon.IsFavorite, lifetime.Token), null);

    public void Create(CreateBeaconRequest request, byte[]? screenshot, string? fileName, Action<BeaconDto>? onCreated = null)
    {
        _ = Task.Run(async () =>
        {
            var created = await api.CreateBeaconAsync(request, lifetime.Token);

            if (!created.Ok || created.Value is not { } beacon)
            {
                Post(() => LastError = created.Error);
                return;
            }

            // Upload the screenshot second: a beacon without its picture is still useful, whereas a
            // picture without a beacon to attach it to is nothing at all.
            if (screenshot is { Length: > 0 })
            {
                var uploaded = await api.UploadImageAsync(beacon.Id, screenshot, fileName ?? "beacon.png", lifetime.Token);
                if (uploaded.Ok && uploaded.Value is { } withImage)
                    beacon = withImage;
                else
                    Post(() => LastError = $"The beacon was raised, but the screenshot did not upload: {uploaded.Error}");
            }

            Post(() =>
            {
                notifications.Toast($"{beacon.Name} raised.", beacon.Location.ZoneName);
                Upsert(beacon);
                RefreshMine();
                onCreated?.Invoke(beacon);
            });
        }, lifetime.Token);
    }

    public void Update(Guid id, UpdateBeaconRequest request, byte[]? screenshot, string? fileName)
    {
        _ = Task.Run(async () =>
        {
            var updated = await api.UpdateBeaconAsync(id, request, lifetime.Token);

            if (!updated.Ok || updated.Value is not { } beacon)
            {
                Post(() => LastError = updated.Error);
                return;
            }

            if (screenshot is { Length: > 0 })
            {
                var uploaded = await api.UploadImageAsync(id, screenshot, fileName ?? "beacon.png", lifetime.Token);
                if (uploaded.Ok && uploaded.Value is { } withImage)
                {
                    beacon = withImage;

                    if (withImage.ImageId is { } imageId)
                        images.Invalidate(imageId);
                }
            }

            Post(() =>
            {
                Upsert(beacon);
                RefreshMine();
            });
        }, lifetime.Token);
    }

    public void Delete(BeaconDto beacon)
    {
        _ = Task.Run(async () =>
        {
            var result = await api.DeleteBeaconAsync(beacon.Id, lifetime.Token);

            Post(() =>
            {
                if (!result.Ok)
                {
                    LastError = result.Error;
                    return;
                }

                Beacons.RemoveAll(b => b.Id == beacon.Id);
                MyBeacons.RemoveAll(b => b.Id == beacon.Id);

                if (SelectedId == beacon.Id)
                    SelectedId = null;

                notifications.Toast($"{beacon.Name} retired.", null);
            });
        }, lifetime.Token);
    }

    public void Report(BeaconDto beacon, string reason) =>
        _ = Task.Run(async () =>
        {
            var result = await api.ReportAsync(beacon.Id, reason, lifetime.Token);
            Post(() => notifications.Toast(
                result.Ok ? "Reported. Thank you." : result.Error ?? "The report did not send.",
                null));
        }, lifetime.Token);

    /// <summary>Looks a beacon up by share code and selects it.</summary>
    public void OpenShareCode(string code, Action<BeaconDto>? onFound = null) =>
        _ = Task.Run(async () =>
        {
            var result = await api.GetByShareCodeAsync(code, lifetime.Token);

            Post(() =>
            {
                if (!result.Ok || result.Value is not { } beacon)
                {
                    LastError = result.Error ?? "No beacon carries that code.";
                    return;
                }

                Upsert(beacon);
                SelectedId = beacon.Id;
                onFound?.Invoke(beacon);
            });
        }, lifetime.Token);

    public void ClearError() => LastError = null;

    /// <summary>Records or withdraws the adult confirmation, then refreshes the cached account.</summary>
    public void ConfirmAdult(bool confirmed) =>
        _ = Task.Run(async () =>
        {
            var result = await api.ConfirmAdultAsync(confirmed, lifetime.Token);

            Post(() =>
            {
                if (result.Ok && result.Value is { } account)
                    Account = account;
                else
                    LastError = result.Error;
            });
        }, lifetime.Token);

    // --- Plumbing -------------------------------------------------------

    private void Mutate(Func<Task<ApiResult<BeaconDto>>> call, Action<BeaconDto>? onSuccess) =>
        _ = Task.Run(async () =>
        {
            var result = await call();

            Post(() =>
            {
                if (!result.Ok || result.Value is not { } beacon)
                {
                    LastError = result.Error;
                    return;
                }

                Upsert(beacon);
                onSuccess?.Invoke(beacon);
            });
        }, lifetime.Token);

    /// <summary>Replaces a beacon in every list that holds it, or inserts it into the page if new.</summary>
    private void Upsert(BeaconDto beacon)
    {
        Replace(Beacons, beacon);
        Replace(MyBeacons, beacon);

        if (Beacons.All(b => b.Id != beacon.Id) && Matches(beacon))
            Beacons.Insert(0, beacon);

        static void Replace(List<BeaconDto> list, BeaconDto beacon)
        {
            var index = list.FindIndex(b => b.Id == beacon.Id);
            if (index >= 0)
                list[index] = beacon;
        }
    }

    /// <summary>
    /// Whether a beacon belongs in the current view.
    ///
    /// Only an approximation of the server's filter, and deliberately so: it exists to decide whether
    /// a beacon that just lit up should appear without a round trip. A false positive shows one extra
    /// row until the next refresh, which is a far better failure than missing the event entirely.
    /// </summary>
    private bool Matches(BeaconDto beacon)
    {
        if (Query.LitOnly && !beacon.Flame.IsBurning)
            return false;

        if (Query.TerritoryId is { } territory && beacon.Location.TerritoryId != territory)
            return false;

        if (!string.IsNullOrWhiteSpace(Query.DataCenter)
            && !string.Equals(beacon.Realm.DataCenter, Query.DataCenter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Query.World)
            && !string.Equals(beacon.Realm.WorldName, Query.World, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Query.Kind is { } kind && beacon.Kind != kind)
            return false;

        if (Query.OwnerAccountId is { } owner && beacon.OwnerAccountId != owner)
            return false;

        if (!string.IsNullOrWhiteSpace(Query.Search))
        {
            var term = Query.Search.Trim();
            var hit = beacon.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || beacon.Location.ZoneName.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || beacon.Description.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (!hit)
                return false;
        }

        return true;
    }

    /// <summary>Applies a hub event to local state and raises a notification when it is worth one.</summary>
    private void Apply(BeaconEvent evt)
    {
        switch (evt.Kind)
        {
            case BeaconEventKind.Removed:
                Beacons.RemoveAll(b => b.Id == evt.BeaconId);
                MyBeacons.RemoveAll(b => b.Id == evt.BeaconId);

                if (SelectedId == evt.BeaconId)
                    SelectedId = null;

                return;

            case BeaconEventKind.Published or BeaconEventKind.Updated:
                if (evt.Beacon is { } published)
                    Upsert(published);

                return;
        }

        // A flame change. Patch the flame in place when the beacon is known, and take the whole
        // beacon when the event carries one we have not seen.
        if (evt.Beacon is { } carried)
            Upsert(carried);

        if (evt.Flame is { } flame)
        {
            PatchFlame(Beacons, evt.BeaconId, flame);
            PatchFlame(MyBeacons, evt.BeaconId, flame);
        }

        if (evt.Kind == BeaconEventKind.Lit)
            notifications.BeaconLit(evt.Beacon ?? Beacons.FirstOrDefault(b => b.Id == evt.BeaconId));

        static void PatchFlame(List<BeaconDto> list, Guid id, BeaconFlame flame)
        {
            var index = list.FindIndex(b => b.Id == id);
            if (index >= 0)
                list[index] = list[index] with { Flame = flame };
        }
    }

    /// <summary>Queues work to run on the framework thread.</summary>
    private void Post(Action action) => pending.Enqueue(action);

    public void Dispose()
    {
        lifetime.Cancel();
        inFlight?.Cancel();
        inFlight?.Dispose();
        lifetime.Dispose();
    }
}
