using System.Numerics;
using Compass.Shared.Beacons;

namespace Compass.Services;

/// <summary>Where a journey has got to.</summary>
public enum JourneyStage
{
    Idle,
    ChangingWorld,
    Teleporting,
    Walking,
    Arrived,
    Failed,
}

/// <summary>
/// What getting to a beacon would involve, worked out before anything happens.
///
/// Computed up front and shown to the traveller so that a journey which will cross a data centre,
/// or cannot happen at all, says so before they commit rather than halfway through.
/// </summary>
public sealed record JourneyPlan
{
    public bool Possible { get; init; }

    /// <summary>Why the journey cannot be made, when it cannot.</summary>
    public string? Blocker { get; init; }

    public bool NeedsWorldChange { get; init; }

    public bool NeedsDataCenterTransfer { get; init; }

    public bool NeedsTeleport { get; init; }

    public uint AetheryteId { get; init; }

    public string? AetheryteName { get; init; }

    /// <summary>Distance left on foot once the teleport lands, when it can be worked out.</summary>
    public float? WalkDistance { get; init; }

    /// <summary>True when the traveller is already standing at the beacon.</summary>
    public bool AlreadyThere { get; init; }

    /// <summary>One line describing the route, for the detail pane.</summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Carries a player from wherever they are to a beacon.
///
/// The work is delegated to Lifestream and vnavmesh; what lives here is the sequencing, the waiting,
/// and the timeouts. Each leg has a deadline because every one of them can silently fail to finish:
/// a world transfer queue, a teleport interrupted by combat, a mesh that never finishes building.
/// Without deadlines the plugin would sit forever claiming to be travelling.
/// </summary>
public sealed class TravelService(Configuration config, LocationService location) : IDisposable
{
    /// <summary>Close enough to call it arrived, comfortably inside the range needed to light a beacon.</summary>
    private const float ArrivalRadius = 12f;

    private static readonly TimeSpan WorldChangeTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan TeleportTimeout = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan WalkTimeout = TimeSpan.FromMinutes(4);

    private readonly LifestreamIpc lifestream = new();

    private readonly NavmeshIpc navmesh = new();

    private DateTime legDeadline = DateTime.MaxValue;

    private bool teleportIssued;

    private bool walkIssued;

    public JourneyStage Stage { get; private set; } = JourneyStage.Idle;

    /// <summary>The beacon currently being travelled to, if any.</summary>
    public BeaconDto? Destination { get; private set; }

    /// <summary>What went wrong, when <see cref="Stage"/> is <see cref="JourneyStage.Failed"/>.</summary>
    public string? FailureReason { get; private set; }

    public bool IsTravelling => Stage is JourneyStage.ChangingWorld or JourneyStage.Teleporting or JourneyStage.Walking;

    public bool LifestreamAvailable => lifestream.Available;

    public bool NavmeshAvailable => navmesh.Available;

    /// <summary>A short line describing what is happening right now, for the status bar.</summary>
    public string StatusText => Stage switch
    {
        JourneyStage.ChangingWorld => $"Travelling to {Destination?.Realm.WorldName}...",
        JourneyStage.Teleporting => $"Teleporting to {Destination?.Location.NearestAetheryteName ?? "the aetheryte"}...",
        JourneyStage.Walking => WalkStatus(),
        JourneyStage.Arrived => $"Arrived at {Destination?.Name}.",
        JourneyStage.Failed => FailureReason ?? "The journey failed.",
        _ => string.Empty,
    };

    // --- Planning -------------------------------------------------------

    /// <summary>Works out what reaching this beacon would take. Pure; safe to call every frame.</summary>
    public JourneyPlan Plan(BeaconDto beacon)
    {
        if (!Svc.InWorld)
            return Impossible("You need to be logged in to travel.");

        if (!lifestream.Available)
            return Impossible("Lifestream is not installed. Compass uses it to travel.");

        var currentWorld = location.CurrentWorldId;
        var currentRegion = CurrentRegion();
        var sameWorld = currentWorld == beacon.Realm.WorldId;
        var sameDataCenter = string.Equals(location.CurrentDataCenter, beacon.Realm.DataCenter, StringComparison.OrdinalIgnoreCase);

        // Data centre travel exists only within a physical region. Nothing can bridge NA and EU.
        if (!sameDataCenter
            && !string.IsNullOrEmpty(currentRegion)
            && !string.IsNullOrEmpty(beacon.Realm.Region)
            && !string.Equals(currentRegion, beacon.Realm.Region, StringComparison.OrdinalIgnoreCase))
        {
            return Impossible($"{beacon.Realm.WorldName} is in {beacon.Realm.Region}. You cannot travel there from {currentRegion}.");
        }

        var distance = location.DistanceTo(beacon);
        if (distance is { } d && d <= ArrivalRadius)
        {
            return new JourneyPlan
            {
                Possible = true,
                AlreadyThere = true,
                Summary = "You are already here.",
            };
        }

        var sameZone = sameWorld && (ushort)Svc.ClientState.TerritoryType == beacon.Location.TerritoryId;
        var needsTeleport = !sameZone;
        var aetheryteId = beacon.Location.NearestAetheryteId;

        if (needsTeleport && aetheryteId == 0)
        {
            return Impossible(
                $"Compass does not know an aetheryte for {beacon.Location.ZoneName}, so it cannot teleport you there.");
        }

        var steps = new List<string>();
        if (!sameWorld)
            steps.Add($"{location.CurrentWorldName} to {beacon.Realm.WorldName}");

        if (needsTeleport)
            steps.Add($"teleport to {beacon.Location.NearestAetheryteName ?? "the nearest aetheryte"}");

        if (sameZone && distance is { } walk)
            steps.Add($"{walk:0} yalms afoot");
        else if (navmesh.Available && config.UseNavmeshForFinalApproach)
            steps.Add("then the last stretch afoot");

        return new JourneyPlan
        {
            Possible = true,
            NeedsWorldChange = !sameWorld,
            NeedsDataCenterTransfer = !sameDataCenter,
            NeedsTeleport = needsTeleport,
            AetheryteId = aetheryteId,
            AetheryteName = beacon.Location.NearestAetheryteName,
            WalkDistance = sameZone ? distance : null,
            Summary = steps.Count == 0 ? "You are already here." : string.Join("  ·  ", steps),
        };

        static JourneyPlan Impossible(string reason) => new() { Possible = false, Blocker = reason, Summary = reason };
    }

    // --- Execution ------------------------------------------------------

    /// <summary>Starts a journey. Returns false and explains why when it cannot begin.</summary>
    public bool Begin(BeaconDto beacon, out string error)
    {
        var plan = Plan(beacon);
        if (!plan.Possible)
        {
            error = plan.Blocker ?? "That journey is not possible.";
            return false;
        }

        if (plan.AlreadyThere)
        {
            error = "You are already there.";
            return false;
        }

        if (lifestream.IsBusy)
        {
            error = "Lifestream is busy with something else.";
            return false;
        }

        Destination = beacon;
        FailureReason = null;
        teleportIssued = false;
        walkIssued = false;

        if (plan.NeedsWorldChange)
        {
            if (!lifestream.ChangeWorld(beacon.Realm.WorldName, plan.NeedsDataCenterTransfer))
            {
                error = "Lifestream would not start the world transfer.";
                Reset();
                return false;
            }

            Enter(JourneyStage.ChangingWorld, WorldChangeTimeout);
        }
        else if (plan.NeedsTeleport)
        {
            Enter(JourneyStage.Teleporting, TeleportTimeout);
        }
        else
        {
            Enter(JourneyStage.Walking, WalkTimeout);
        }

        error = string.Empty;
        Svc.Log.Information("Compass: journey to {Beacon} begun at stage {Stage}.", beacon.Name, Stage);
        return true;
    }

    /// <summary>Stops everything and returns to idle. Safe to call at any time.</summary>
    public void Cancel()
    {
        if (Stage is JourneyStage.ChangingWorld && lifestream.IsBusy)
            lifestream.Abort();

        if (Stage is JourneyStage.Walking)
            navmesh.Stop();

        Reset();
    }

    /// <summary>Clears a finished journey so the UI stops showing its outcome.</summary>
    public void Acknowledge()
    {
        if (Stage is JourneyStage.Arrived or JourneyStage.Failed)
            Reset();
    }

    /// <summary>Advances the journey. Called once per frame.</summary>
    public void Tick()
    {
        if (!IsTravelling || Destination is null)
            return;

        if (DateTime.UtcNow > legDeadline)
        {
            Fail(Stage switch
            {
                JourneyStage.ChangingWorld => "The world transfer did not finish in time.",
                JourneyStage.Teleporting => "The teleport did not finish in time.",
                _ => "The walk did not finish in time.",
            });
            return;
        }

        // Nothing can be driven mid-zone-change; wait for the world to settle.
        if (!Svc.InWorld)
            return;

        switch (Stage)
        {
            case JourneyStage.ChangingWorld:
                TickWorldChange();
                break;
            case JourneyStage.Teleporting:
                TickTeleport();
                break;
            case JourneyStage.Walking:
                TickWalk();
                break;
        }
    }

    private void TickWorldChange()
    {
        if (lifestream.IsBusy)
            return;

        if (location.CurrentWorldId != Destination!.Realm.WorldId)
        {
            Fail($"You did not arrive on {Destination.Realm.WorldName}.");
            return;
        }

        var sameZone = (ushort)Svc.ClientState.TerritoryType == Destination.Location.TerritoryId;
        Enter(sameZone ? JourneyStage.Walking : JourneyStage.Teleporting,
              sameZone ? WalkTimeout : TeleportTimeout);
    }

    private void TickTeleport()
    {
        if ((ushort)Svc.ClientState.TerritoryType == Destination!.Location.TerritoryId)
        {
            Enter(JourneyStage.Walking, WalkTimeout);
            return;
        }

        if (lifestream.IsBusy)
            return;

        if (teleportIssued)
        {
            // Lifestream finished but we are not in the target zone. Nothing left to wait for.
            Fail($"The teleport to {Destination.Location.NearestAetheryteName ?? "the aetheryte"} did not go through.");
            return;
        }

        teleportIssued = true;
        if (!lifestream.Teleport(Destination.Location.NearestAetheryteId))
            Fail("Lifestream would not start the teleport.");
    }

    private void TickWalk()
    {
        var beacon = Destination!;
        var distance = location.DistanceTo(beacon);

        if (distance is { } d && d <= ArrivalRadius)
        {
            navmesh.Stop();
            Enter(JourneyStage.Arrived, TimeSpan.MaxValue);
            Svc.Log.Information("Compass: arrived at {Beacon}.", beacon.Name);
            return;
        }

        // Walking is a convenience. Without it, arriving in the zone is the end of the journey and
        // the overlay points the rest of the way.
        if (!config.UseNavmeshForFinalApproach || !navmesh.Available)
        {
            Enter(JourneyStage.Arrived, TimeSpan.MaxValue);
            return;
        }

        if (!navmesh.IsReady)
            return;

        if (walkIssued)
        {
            if (!navmesh.IsMoving)
            {
                // The path ended without reaching the beacon, which usually means the mesh has no route.
                // Stop here rather than retrying forever; the traveller is in the right zone.
                Enter(JourneyStage.Arrived, TimeSpan.MaxValue);
            }

            return;
        }

        walkIssued = true;

        var target = new Vector3(beacon.Location.X, beacon.Location.Y, beacon.Location.Z);
        var grounded = navmesh.SnapToFloor(target) ?? target;

        if (!navmesh.MoveTo(grounded))
            Enter(JourneyStage.Arrived, TimeSpan.MaxValue);
    }

    private string WalkStatus()
    {
        if (Destination is null)
            return "Walking...";

        var distance = location.DistanceTo(Destination);
        return distance is { } d ? $"{d:0} yalms to {Destination.Name}." : $"Walking to {Destination.Name}...";
    }

    private string CurrentRegion() =>
        location.CaptureRealm()?.Region ?? string.Empty;

    private void Enter(JourneyStage stage, TimeSpan timeout)
    {
        Stage = stage;
        legDeadline = timeout == TimeSpan.MaxValue ? DateTime.MaxValue : DateTime.UtcNow + timeout;

        // Each leg issues its own commands, so clear the guards on every transition.
        if (stage != JourneyStage.Teleporting)
            teleportIssued = false;

        if (stage != JourneyStage.Walking)
            walkIssued = false;
    }

    private void Fail(string reason)
    {
        FailureReason = reason;
        Stage = JourneyStage.Failed;
        legDeadline = DateTime.MaxValue;
        navmesh.Stop();
        Svc.Log.Warning("Compass: journey failed - {Reason}", reason);
    }

    private void Reset()
    {
        Stage = JourneyStage.Idle;
        Destination = null;
        FailureReason = null;
        legDeadline = DateTime.MaxValue;
        teleportIssued = false;
        walkIssued = false;
    }

    public void Dispose()
    {
        if (Stage == JourneyStage.Walking)
            navmesh.Stop();
    }
}
