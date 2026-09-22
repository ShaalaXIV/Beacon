using Dalamud.Plugin.Ipc;

namespace Beacon.Services;

/// <summary>
/// Stagehand's public IPC, as Beacon uses it.
///
/// Stagehand ships an API library for this, and Beacon deliberately does not link it. Every call it
/// needs takes and returns primitives, so the call gates can be subscribed to by name exactly as
/// Lifestream's and vnavmesh's are. That keeps Stagehand a genuinely optional dependency -- missing,
/// disabled, or a version with a changed signature all degrade into a disabled button -- and it keeps
/// a licence boundary between the two projects rather than pulling one into the other.
///
/// Every stage Beacon loads is a *temporary* stage. Nothing is ever written to the guest's own
/// library, so accepting a stage is entirely undoable and leaves nothing behind.
/// </summary>
public sealed class StagehandIpc
{
    private const string PluginName = "Stagehand";

    private readonly ICallGateSubscriber<string, string, string, bool> createOrUpdate =
        Svc.PluginInterface.GetIpcSubscriber<string, string, string, bool>("Stagehand.TryCreateOrUpdateTemporaryStage");

    private readonly ICallGateSubscriber<string, bool, bool> setVisible =
        Svc.PluginInterface.GetIpcSubscriber<string, bool, bool>("Stagehand.TrySetTemporaryStageVisible");

    private readonly ICallGateSubscriber<string, bool> destroy =
        Svc.PluginInterface.GetIpcSubscriber<string, bool>("Stagehand.TryDestroyTemporaryStage");

    /// <summary>True when Stagehand is installed and loaded.</summary>
    public bool Available => LifestreamIpc.IsPluginLoaded(PluginName);

    /// <summary>
    /// Hands a stage definition to Stagehand under an id of Beacon's own. The id is prefixed so it can
    /// never collide with one of the guest's own stages, which Stagehand refuses to let us touch anyway.
    /// </summary>
    public bool CreateOrUpdate(Guid beaconId, string definition, string debugName) =>
        Try(() => createOrUpdate.InvokeFunc(definition, StageId(beaconId), debugName), false);

    /// <summary>
    /// Shows or hides a stage Beacon created. Stagehand hides every temporary stage when the player
    /// changes location, so arriving somewhere means showing it again rather than assuming it is up.
    /// </summary>
    public bool SetVisible(Guid beaconId, bool visible) =>
        Try(() => setVisible.InvokeFunc(StageId(beaconId), visible), false);

    /// <summary>Removes a stage entirely. Called when a guest leaves, refuses it, or Beacon unloads.</summary>
    public bool Destroy(Guid beaconId) =>
        Try(() => destroy.InvokeFunc(StageId(beaconId)), false);

    /// <summary>Beacon's own namespace inside Stagehand's temporary stage ids.</summary>
    private static string StageId(Guid beaconId) => $"Beacon.{beaconId:N}";

    private static T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Stagehand IPC call failed.");
            return fallback;
        }
    }
}
