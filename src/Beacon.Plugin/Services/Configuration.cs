using Beacon.Shared.Beacons;
using Dalamud.Configuration;

namespace Beacon.Services;

/// <summary>
/// Everything Beacon remembers between sessions.
///
/// The secret key lives here, in Dalamud's per-plugin config file. That is the same protection every
/// other plugin's credentials get, but it is worth being honest about what it is: obfuscation-free
/// plaintext on the player's own machine. The key grants nothing but the ability to manage that
/// account's beacons, which is why it is a throwaway credential rather than anything reused.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- Account --------------------------------------------------------

    /// <summary>Base URL of the Beacon server, without a trailing slash.</summary>
    public string ServerUrl { get; set; } = "https://plugins.aethercast.org:61249";

    /// <summary>The account secret. Null until registered or pasted in.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Cached account id, so the UI can tell "mine" from "theirs" before the first fetch.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Cached display name for the same reason.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Claim the logged-in character automatically. On by default because the alternative is a
    /// settings chore before the plugin does anything useful.
    /// </summary>
    public bool AutoLinkCharacter { get; set; } = true;

    // --- Presence -------------------------------------------------------

    /// <summary>Show the lit-beacon count in the server info bar.</summary>
    public bool ShowDtrEntry { get; set; } = true;

    /// <summary>Draw the on-screen pointer towards the beacon you are travelling to.</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Tell me when a beacon lights up in the zone I am standing in.</summary>
    public bool NotifyOnNearbyLit { get; set; } = true;

    /// <summary>Tell me when a beacon I have starred lights up, wherever it is.</summary>
    public bool NotifyOnFavoriteLit { get; set; } = true;

    /// <summary>Also print notifications to the chat log, for people who miss toasts.</summary>
    public bool EchoToChat { get; set; }

    /// <summary>Beacons to stay quiet about, however they would otherwise qualify.</summary>
    public HashSet<Guid> MutedBeacons { get; set; } = [];

    // --- Travel ---------------------------------------------------------

    /// <summary>
    /// Walk the last stretch with vnavmesh after Lifestream has done the teleporting.
    /// Only has an effect when vnavmesh is installed.
    /// </summary>
    public bool UseNavmeshForFinalApproach { get; set; } = true;

    /// <summary>Ask before starting a journey that needs a data centre transfer.</summary>
    public bool ConfirmDataCenterTravel { get; set; } = true;

    // --- Lighting -------------------------------------------------------

    /// <summary>Default burn length offered in the light dialog.</summary>
    public int DefaultLitMinutes { get; set; } = BeaconLimits.DefaultLitMinutes;

    /// <summary>
    /// Remind me to put my beacon out when I stop being near it, rather than letting it burn down
    /// while I am elsewhere and someone makes a wasted trip.
    /// </summary>
    public bool RemindToExtinguish { get; set; } = true;

    // --- The Chronicle --------------------------------------------------

    /// <summary>
    /// Let Beacon record that you were at a lit beacon, so your card shows when you were last out.
    ///
    /// On by default, because the signal is what lets somebody tell a living community from an
    /// abandoned one -- but it is presence information, so it is a single switch to turn off.
    /// </summary>
    public bool ShareActivity { get; set; } = true;

    /// <summary>Last filters used in the Chronicle, restored on open.</summary>
    public bool LastAvailableOnlyFilter { get; set; }

    // --- Appearance -----------------------------------------------------

    /// <summary>Scale applied to the atlas screenshot thumbnails.</summary>
    public float ThumbnailScale { get; set; } = 1f;

    /// <summary>Use the warm parchment styling rather than inheriting Dalamud's theme.</summary>
    public bool UseBeaconTheme { get; set; } = true;

    /// <summary>Last filters used in the atlas, restored on open so the plugin picks up where you left off.</summary>
    public string? LastDataCenterFilter { get; set; }

    public bool LastLitOnlyFilter { get; set; }

    /// <summary>True once the first-run flow has been completed or dismissed.</summary>
    public bool OnboardingComplete { get; set; }

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);

    public static Configuration Load() =>
        Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    /// <summary>True when there is an account to authenticate with.</summary>
    public bool HasAccount => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Normalised base URL, with any trailing slash removed so route concatenation is safe.</summary>
    public string NormalisedServerUrl => ServerUrl.TrimEnd('/');
}
