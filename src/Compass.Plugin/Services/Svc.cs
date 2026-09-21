using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Compass.Services;

/// <summary>
/// The Dalamud services Compass uses, injected once at load.
///
/// Static by design: these are process-wide singletons owned by Dalamud, and threading them through
/// constructors would add a parameter to every type without making anything more testable, since none
/// of them can be substituted anyway.
/// </summary>
internal sealed class Svc
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;

    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    [PluginService] internal static IDataManager Data { get; private set; } = null!;

    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    [PluginService] internal static INotificationManager Notifications { get; private set; } = null!;

    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    /// <summary>Owns <c>LocalPlayer</c>, which moved here from IClientState in API 15.</summary>
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;

    /// <summary>Reads rendered textures back to bytes, which is how a screenshot becomes an upload.</summary>
    [PluginService] internal static ITextureReadbackProvider TextureReadback { get; private set; } = null!;

    /// <summary>Initialises every property above. Call once, before anything else touches them.</summary>
    internal static void Initialise(IDalamudPluginInterface pluginInterface) =>
        pluginInterface.Create<Svc>();

    /// <summary>
    /// True when the player is loaded into the world and safe to read position from.
    /// Guards every capture and proximity check: reading a position during a zone change gives
    /// coordinates from nowhere, and a beacon raised on those is one nobody can ever reach.
    /// </summary>
    internal static bool InWorld =>
        ClientState.IsLoggedIn
        && Objects.LocalPlayer is not null
        && !Condition[ConditionFlag.BetweenAreas]
        && !Condition[ConditionFlag.BetweenAreas51]
        && !Condition[ConditionFlag.LoggingOut];
}
