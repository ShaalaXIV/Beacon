using Compass.Shared.Beacons;
using Dalamud.Interface.ImGuiNotification;

namespace Compass.Services;

/// <summary>
/// Tells the player when something worth knowing happens, and stays quiet otherwise.
///
/// The bar for interrupting somebody mid-roleplay is high. A beacon lighting up is only surfaced when
/// it is somewhere the player could actually walk to, or somewhere they have said they care about.
/// Everything else belongs in the atlas, not in a popup.
/// </summary>
public sealed class NotificationService(Configuration config, LocationService location)
{
    /// <summary>
    /// Beacons already announced, so that a re-light, a stoke, or a reconnect that replays events
    /// cannot announce the same fire twice.
    /// </summary>
    private readonly Dictionary<Guid, DateTime> announced = [];

    private static readonly TimeSpan AnnounceCooldown = TimeSpan.FromMinutes(30);

    /// <summary>A plain notification, for the results of things the player just did.</summary>
    public void Toast(string title, string? content, NotificationType type = NotificationType.Info)
    {
        Svc.Notifications.AddNotification(new Notification
        {
            Title = title,
            Content = content ?? string.Empty,
            Type = type,
            InitialDuration = TimeSpan.FromSeconds(6),
        });

        if (config.EchoToChat)
            Echo(content is null ? title : $"{title} {content}");
    }

    /// <summary>Something went wrong that the player asked for and should hear about.</summary>
    public void Error(string message) => Toast("Compass", message, NotificationType.Warning);

    /// <summary>
    /// A beacon somewhere lit up. Decides whether this particular player wants to know.
    /// </summary>
    public void BeaconLit(BeaconDto? beacon)
    {
        if (beacon is null || !beacon.Flame.IsBurning)
            return;

        if (config.MutedBeacons.Contains(beacon.Id))
            return;

        // Your own flame is not news to you.
        if (beacon.Flame.LitByAccountId == config.AccountId)
            return;

        if (!ShouldAnnounce(beacon))
            return;

        var now = DateTime.UtcNow;
        if (announced.TryGetValue(beacon.Id, out var last) && now - last < AnnounceCooldown)
            return;

        announced[beacon.Id] = now;
        PruneAnnounced(now);

        var where = beacon.Location.ZoneName;
        var who = beacon.Flame.LitByName;
        var note = beacon.Flame.Note;

        var content = string.IsNullOrWhiteSpace(note)
            ? $"{where}{(string.IsNullOrWhiteSpace(who) ? string.Empty : $" - lit by {who}")}"
            : $"{where} - \"{note}\"";

        Toast($"{beacon.Name} is lit", content);
    }

    private bool ShouldAnnounce(BeaconDto beacon)
    {
        if (config.NotifyOnFavoriteLit && beacon.IsFavorite)
            return true;

        if (!config.NotifyOnNearbyLit)
            return false;

        // "Nearby" means the same zone on the same world: somewhere the player could simply walk to.
        // A beacon lighting on another world is not nearby in any sense that matters.
        return Svc.InWorld
               && location.CurrentWorldId == beacon.Realm.WorldId
               && (ushort)Svc.ClientState.TerritoryType == beacon.Location.TerritoryId;
    }

    /// <summary>Forgets beacons announced long enough ago that the cooldown no longer applies.</summary>
    private void PruneAnnounced(DateTime now)
    {
        if (announced.Count < 64)
            return;

        var stale = announced
            .Where(e => now - e.Value > AnnounceCooldown)
            .Select(e => e.Key)
            .ToList();

        foreach (var id in stale)
            announced.Remove(id);
    }

    private static void Echo(string message)
    {
        try
        {
            Svc.Chat.Print($"[Compass] {message}");
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Compass: could not echo to chat.");
        }
    }
}
