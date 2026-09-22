using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Beacons;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>Server, account and behaviour settings.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Configuration config;

    private readonly BeaconApi api;

    private readonly AtlasService atlas;

    private readonly BeaconHubClient hub;

    private readonly TravelService travel;

    private readonly StageDressing stages;

    private string serverUrl;

    private string displayName;

    private string keyToImport = string.Empty;

    private bool revealKey;

    private string? connectionStatus;

    private bool testing;

    private bool registering;

    public SettingsWindow(
        Configuration config,
        BeaconApi api,
        AtlasService atlas,
        BeaconHubClient hub,
        TravelService travel,
        StageDressing stages)
        : base("Beacon settings###BeaconSettings")
    {
        this.config = config;
        this.api = api;
        this.atlas = atlas;
        this.hub = hub;
        this.travel = travel;
        this.stages = stages;

        serverUrl = config.ServerUrl;
        displayName = config.DisplayName;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 460),
            MaximumSize = new Vector2(900, 1200),
        };

        Size = new Vector2(540, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseBeaconTheme);

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (ImGui.BeginTabBar("##settingsTabs"))
        {
            if (ImGui.BeginTabItem("Connection"))
            {
                DrawConnection(scale);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Behaviour"))
            {
                DrawBehaviour();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Travel"))
            {
                DrawTravel();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    // --- Connection -------------------------------------------------------

    private void DrawConnection(float scale)
    {
        Ornament.Text(Theme.BrassBright, "The atlas server");

        Ornament.TextWrapped(Theme.MutedDeep,
            "Beacon keeps beacons on a server you or your community runs. Point it at yours.");

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##url", ref serverUrl, 256))
        {
            config.ServerUrl = serverUrl.Trim();
            config.Save();
        }

        if (ImGui.Button(testing ? "Testing..." : "Test connection"))
            _ = TestAsync();

        ImGui.SameLine();
        if (ImGui.Button("Reconnect"))
        {
            hub.Restart();
            atlas.Refresh();
        }

        ImGui.SameLine();
        Ornament.Text(hub.Connected ? Theme.Verdigris : Theme.Wax, hub.Connected ? "Listening" : "Not listening");

        if (connectionStatus is { } status)
            Ornament.TextWrapped(status.StartsWith("Connected", StringComparison.Ordinal) ? Theme.Verdigris : Theme.Wax, status);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Your account");

        if (!config.HasAccount)
        {
            DrawRegistration(scale);
            return;
        }

        DrawAccount(scale);
    }

    private void DrawRegistration(float scale)
    {
        Ornament.TextWrapped(Theme.CreamDim,
            "You need an account before you can raise or light beacons. One account covers all your characters.");

        ImGui.Spacing();
        Ornament.PageLabel("How you want to be credited on your beacons");

        ImGui.SetNextItemWidth(220f * scale);
        ImGui.InputTextWithHint("##name", "account name", ref displayName, BeaconLimits.DisplayNameMaxLength);

        if (Ornament.AccentButton(registering ? "Creating..." : "Create an account", new Vector2(180f * scale, 26f * scale), !registering))
            _ = RegisterAsync();

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.TextWrapped(Theme.MutedDeep,
            "Already have a key from another PC? Paste it here instead.");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##import", "cmps_...", ref keyToImport, 128, ImGuiInputTextFlags.Password);

        if (ImGui.Button("Use this key"))
        {
            var trimmed = keyToImport.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                connectionStatus = "Paste a key first.";
                return;
            }

            config.SecretKey = trimmed;
            config.Save();
            keyToImport = string.Empty;

            hub.Restart();
            atlas.RefreshAccount();
            atlas.Refresh();
        }
    }

    private void DrawAccount(float scale)
    {
        var account = atlas.Account;

        Ornament.Text(Theme.CreamDim, account?.DisplayName ?? config.DisplayName);

        if (account is not null)
        {
            Ornament.Text(Theme.MutedDeep,
                $"{account.BeaconCount} of {BeaconLimits.MaxBeaconsPerAccount} beacons raised");

            if (account.Characters.Count > 0)
            {
                ImGui.Spacing();
                Ornament.PageLabel("Characters claimed");

                foreach (var character in account.Characters)
                    Ornament.Text(Theme.Muted, $"  {character.Name} @ {character.WorldName}");
            }
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(220f * scale);
        ImGui.InputText("##displayName", ref displayName, BeaconLimits.DisplayNameMaxLength);

        ImGui.SameLine();
        if (ImGui.Button("Rename"))
            _ = RenameAsync();

        ImGui.Spacing();
        Toggle("Claim my characters automatically", () => config.AutoLinkCharacter, v => config.AutoLinkCharacter = v);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Your key");

        Ornament.TextWrapped(Theme.MutedDeep,
            "This is the only thing that proves a beacon is yours. Keep a copy somewhere safe: "
            + "it cannot be reissued, and anyone holding it can manage your beacons.");

        ImGui.Checkbox("Show it", ref revealKey);

        if (revealKey)
        {
            var key = config.SecretKey ?? string.Empty;
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##key", ref key, 128, ImGuiInputTextFlags.ReadOnly);
        }

        if (ImGui.Button("Copy key"))
        {
            ImGui.SetClipboardText(config.SecretKey ?? string.Empty);
            connectionStatus = "Key copied to the clipboard.";
        }

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Wax);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Cream);

        if (ImGui.Button("Forget this account"))
        {
            config.SecretKey = null;
            config.AccountId = Guid.Empty;
            config.Save();
            hub.Restart();
            connectionStatus = "Account forgotten on this PC. Your beacons are still on the server.";
        }

        ImGui.PopStyleColor(2);
    }

    private async Task TestAsync()
    {
        testing = true;
        connectionStatus = null;

        try
        {
            var result = await api.HealthAsync(CancellationToken.None);

            connectionStatus = result.Ok && result.Value is { } health
                ? $"Connected. Protocol {health.Protocol}, {health.Listeners} listening."
                : result.Error;
        }
        finally
        {
            testing = false;
        }
    }

    private async Task RegisterAsync()
    {
        var chosen = displayName.Trim();

        if (chosen.Length < BeaconLimits.DisplayNameMinLength)
        {
            connectionStatus = $"Choose a name of at least {BeaconLimits.DisplayNameMinLength} characters.";
            return;
        }

        registering = true;

        try
        {
            var result = await api.RegisterAsync(chosen, CancellationToken.None);

            if (!result.Ok || result.Value is not { } response)
            {
                connectionStatus = result.Error;
                return;
            }

            config.SecretKey = response.SecretKey;
            config.AccountId = response.Account.Id;
            config.DisplayName = response.Account.DisplayName;
            config.OnboardingComplete = true;
            config.Save();

            // Show it straight away: this is the only moment the key exists outside the config file,
            // and somebody who loses it loses every beacon they have raised.
            revealKey = true;
            connectionStatus = "Account created. Copy your key and keep it somewhere safe.";

            hub.Restart();
            atlas.RefreshAccount();
            atlas.Refresh();
        }
        finally
        {
            registering = false;
        }
    }

    private async Task RenameAsync()
    {
        var chosen = displayName.Trim();
        var result = await api.UpdateAccountAsync(chosen, CancellationToken.None);

        if (result.Ok)
        {
            config.DisplayName = chosen;
            config.Save();
            atlas.RefreshAccount();
            connectionStatus = "Renamed.";
        }
        else
        {
            connectionStatus = result.Error;
        }
    }

    // --- Behaviour --------------------------------------------------------

    private void DrawBehaviour()
    {
        Ornament.Text(Theme.BrassBright, "Being told things");

        Toggle("When a beacon lights in the zone I am standing in",
            () => config.NotifyOnNearbyLit, v => config.NotifyOnNearbyLit = v);

        Toggle("When a beacon I have starred lights, wherever it is",
            () => config.NotifyOnFavoriteLit, v => config.NotifyOnFavoriteLit = v);

        Toggle("Also print notifications to the chat log",
            () => config.EchoToChat, v => config.EchoToChat = v);

        if (config.MutedBeacons.Count > 0)
        {
            ImGui.Spacing();
            Ornament.Text(Theme.MutedDeep, $"{config.MutedBeacons.Count} beacon(s) muted.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Unmute all"))
            {
                config.MutedBeacons.Clear();
                config.Save();
            }
        }

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "The Chronicle");

        Toggle("Record when I am out at a lit beacon",
            () => config.ShareActivity, v => config.ShareActivity = v,
            "Your card shows when you were last seen out roleplaying, so people can tell a living\n"
            + "community from an abandoned one. Beacon only records this while you are actually\n"
            + "standing at a beacon somebody has lit, and never where you are the rest of the time.");

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Stages");

        Toggle("Load every stage automatically when I arrive",
            () => config.AutoLoadStages, v => config.AutoLoadStages = v,
            "Off by default. A stage is another player's scenery appearing on your screen, so Beacon\n"
            + "asks the first time and remembers your answer for that beacon. Turn this on if you would\n"
            + "rather see every place as its keeper built it.\n"
            + "Nothing is ever written into your own Stagehand library either way.");

        if (config.TrustedStageBeacons.Count > 0 || config.RefusedStageBeacons.Count > 0)
        {
            Ornament.Text(
                Theme.MutedDeep,
                $"{config.TrustedStageBeacons.Count} agreed to, {config.RefusedStageBeacons.Count} declined.");

            if (ImGui.Button("Ask me about all of them again"))
            {
                config.TrustedStageBeacons.Clear();
                config.RefusedStageBeacons.Clear();
                config.Save();
            }
        }

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Appearance");

        Toggle("Use the Beacon parchment styling",
            () => config.UseBeaconTheme, v => config.UseBeaconTheme = v,
            "Turn this off to inherit whatever Dalamud theme you use everywhere else.");

        Toggle("Show the lit count in the server info bar",
            () => config.ShowDtrEntry, v => config.ShowDtrEntry = v);

        Toggle("Point the way to a beacon I am travelling to",
            () => config.ShowOverlay, v => config.ShowOverlay = v);

        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        var thumbnailScale = config.ThumbnailScale;
        if (ImGui.SliderFloat("Screenshot size", ref thumbnailScale, 0.5f, 1.5f, "%.2fx"))
        {
            config.ThumbnailScale = thumbnailScale;
            config.Save();
        }
    }

    // --- Travel -----------------------------------------------------------

    private void DrawTravel()
    {
        Ornament.Text(Theme.BrassBright, "What Beacon travels with");

        DrawDependency("Lifestream", travel.LifestreamAvailable,
            "Required. Beacon uses it for world visits, data centre transfers and aetheryte teleports.");

        DrawDependency("vnavmesh", travel.NavmeshAvailable,
            "Optional. Walks the last stretch from the aetheryte to the beacon itself.");

        DrawDependency("Stagehand", stages.Available,
            "Optional. Lets you see a place dressed the way its keeper built it, and share your own.");

        Ornament.FleuronDivider(Theme.BrassDim);

        Toggle("Walk the last stretch automatically",
            () => config.UseNavmeshForFinalApproach, v => config.UseNavmeshForFinalApproach = v);

        if (!travel.NavmeshAvailable)
        {
            ImGui.SameLine();
            Ornament.Text(Theme.MutedDeep, "(needs vnavmesh)");
        }

        Toggle("Ask before a data centre transfer",
            () => config.ConfirmDataCenterTravel, v => config.ConfirmDataCenterTravel = v);

        Toggle("Remind me to put my beacon out when I leave it",
            () => config.RemindToExtinguish, v => config.RemindToExtinguish = v);

        ImGui.Spacing();
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);

        var minutes = config.DefaultLitMinutes;
        if (ImGui.SliderInt("Default burn", ref minutes, BeaconLimits.MinLitMinutes, BeaconLimits.MaxLitMinutes, "%d minutes"))
        {
            config.DefaultLitMinutes = minutes;
            config.Save();
        }
    }

    private static void DrawDependency(string plugin, bool installed, string why)
    {
        Ornament.Text(installed ? Theme.Verdigris : Theme.Wax, $"{plugin}: {(installed ? "found" : "not found")}");
        Ornament.TextWrapped(Theme.MutedDeep, why);
        ImGui.Spacing();
    }

    /// <summary>
    /// A checkbox bound to a configuration property, saved the moment it changes.
    ///
    /// Config values are properties, which cannot be passed by ref to ImGui, and a settings screen
    /// that needs an explicit save button is a settings screen people lose changes in.
    /// </summary>
    private void Toggle(string label, Func<bool> get, Action<bool> set, string? tooltip = null)
    {
        var value = get();

        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            config.Save();
        }

        if (tooltip is not null)
            Ornament.Tooltip(tooltip);
    }
}
