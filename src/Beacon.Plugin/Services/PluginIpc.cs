using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace Beacon.Services;

/// <summary>
/// Lifestream's public IPC, as Beacon uses it.
///
/// Beacon does not reimplement travel. Lifestream already solves world visits, data centre transfers
/// and aetheryte teleports, and it solves them against a moving target of game updates. Every call
/// here is wrapped, because an optional dependency that is missing, unloaded, or a version with a
/// changed signature must degrade into a disabled button, never an exception in the middle of a journey.
/// </summary>
public sealed class LifestreamIpc
{
    private const string PluginName = "Lifestream";

    private readonly ICallGateSubscriber<bool> isBusy =
        Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");

    private readonly ICallGateSubscriber<object> abort =
        Svc.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");

    private readonly ICallGateSubscriber<string, bool> canVisitSameDc =
        Svc.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");

    private readonly ICallGateSubscriber<string, bool> canVisitCrossDc =
        Svc.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC");

    private readonly ICallGateSubscriber<string, bool, string?, bool, int?, bool?, bool?, object>
        tpAndChangeWorld = Svc.PluginInterface
            .GetIpcSubscriber<string, bool, string?, bool, int?, bool?, bool?, object>("Lifestream.TPAndChangeWorld");

    private readonly ICallGateSubscriber<uint, byte, bool> teleport =
        Svc.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");

    private readonly ICallGateSubscriber<string, bool> aethernetTeleport =
        Svc.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");

    /// <summary>True when Lifestream is installed and loaded.</summary>
    public bool Available => IsPluginLoaded(PluginName);

    /// <summary>True when Lifestream is in the middle of something and must not be interrupted.</summary>
    public bool IsBusy => Try(() => isBusy.InvokeFunc(), false);

    public void Abort() => Try(() => abort.InvokeAction());

    /// <summary>Whether a world visit to this world is currently possible without leaving the data centre.</summary>
    public bool CanVisitSameDataCenter(string world) => Try(() => canVisitSameDc.InvokeFunc(world), false);

    /// <summary>Whether a data centre transfer to this world is currently possible.</summary>
    public bool CanVisitCrossDataCenter(string world) => Try(() => canVisitCrossDc.InvokeFunc(world), false);

    /// <summary>Starts a world visit, optionally as a data centre transfer. Lifestream drives it to completion.</summary>
    public bool ChangeWorld(string world, bool dataCenterTransfer) =>
        Try(() =>
        {
            tpAndChangeWorld.InvokeAction(world, dataCenterTransfer, null, false, null, null, null);
            return true;
        }, false);

    /// <summary>Teleports to an aetheryte by row id.</summary>
    public bool Teleport(uint aetheryteId) => Try(() => teleport.InvokeFunc(aetheryteId, (byte)0), false);

    /// <summary>Hops to an aethernet shard by name, for the handful of zones that have one.</summary>
    public bool AethernetTeleport(string destination) => Try(() => aethernetTeleport.InvokeFunc(destination), false);

    internal static bool IsPluginLoaded(string internalName)
    {
        try
        {
            return Svc.PluginInterface.InstalledPlugins
                .Any(p => p.IsLoaded && string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Could not enumerate installed plugins.");
            return false;
        }
    }

    private static T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Lifestream IPC call failed.");
            return fallback;
        }
    }

    private static void Try(Action call)
    {
        try
        {
            call();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Lifestream IPC call failed.");
        }
    }
}

/// <summary>
/// vnavmesh's public IPC, used only for the last stretch on foot.
///
/// Optional throughout: without it a traveller is teleported to the nearest aetheryte and pointed in
/// the right direction, which is exactly what they would do by hand.
/// </summary>
public sealed class NavmeshIpc
{
    private const string PluginName = "vnavmesh";

    private readonly ICallGateSubscriber<bool> isReady =
        Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");

    private readonly ICallGateSubscriber<float> buildProgress =
        Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");

    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo =
        Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");

    private readonly ICallGateSubscriber<bool> pathfindInProgress =
        Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    private readonly ICallGateSubscriber<object> stop =
        Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

    private readonly ICallGateSubscriber<bool> isRunning =
        Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");

    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor =
        Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");

    public bool Available => LifestreamIpc.IsPluginLoaded(PluginName);

    /// <summary>True when the navigation mesh for the current zone has finished building.</summary>
    public bool IsReady => Try(() => isReady.InvokeFunc(), false);

    /// <summary>Mesh build progress from 0 to 1, or a negative value when nothing is building.</summary>
    public float BuildProgress => Try(() => buildProgress.InvokeFunc(), -1f);

    /// <summary>True while a path is being computed or walked.</summary>
    public bool IsMoving => Try(() => isRunning.InvokeFunc(), false) || Try(() => pathfindInProgress.InvokeFunc(), false);

    /// <summary>Walks to a point, pathing around the terrain. False when the request could not be started.</summary>
    public bool MoveTo(Vector3 destination) => Try(() => pathfindAndMoveTo.InvokeFunc(destination, false), false);

    public void Stop() => Try(() => stop.InvokeAction());

    /// <summary>
    /// Drops a point onto the walkable floor beneath it.
    ///
    /// Worth doing before walking anywhere: a beacon's stored Y is wherever its author was standing,
    /// which may be a rock or a rooftop, and asking the mesh to path to a point floating in the air
    /// either fails outright or walks the traveller somewhere strange.
    /// </summary>
    public Vector3? SnapToFloor(Vector3 point, float searchRange = 5f) =>
        Try(() => pointOnFloor.InvokeFunc(point, false, searchRange), null);

    private static T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "vnavmesh IPC call failed.");
            return fallback;
        }
    }

    private static void Try(Action call)
    {
        try
        {
            call();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "vnavmesh IPC call failed.");
        }
    }
}
