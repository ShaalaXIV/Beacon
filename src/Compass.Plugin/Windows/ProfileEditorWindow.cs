using System.Numerics;
using Compass.Services;
using Compass.Shared.Profiles;
using Compass.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Compass.Windows;

/// <summary>
/// Writes a character's card.
///
/// Built around one idea: a card is publishable in about a minute. Race, clan, gender and name come
/// from the game, nothing below the first section is required, and the meter at the foot fills as you
/// go. A form that demands ten sections before it will accept anything is a form people abandon, and
/// an empty Chronicle helps nobody.
/// </summary>
public sealed class ProfileEditorWindow : Window
{
    private readonly Configuration config;

    private readonly ProfileService profiles;

    private readonly AtlasService atlas;

    private readonly LocationService location;

    private readonly ScreenshotService screenshots;

    private readonly ImageCache images;

    private readonly NotificationService notifications;

    private ProfileDto? editing;

    private string name = string.Empty;
    private string title = string.Empty;
    private string race = string.Empty;
    private string clan = string.Empty;
    private string gender = string.Empty;
    private string pronouns = string.Empty;
    private string quote = string.Empty;
    private readonly string[] archetype = ["", "", ""];
    private AgeRange age = AgeRange.Unspecified;

    private readonly HashSet<PersonalityTrait> traits = [];
    private readonly HashSet<RpTone> tones = [];
    private readonly HashSet<RpActivity> activities = [];
    private RpLength length = RpLength.Casual;

    private string boundaries = string.Empty;
    private bool walkups = true;
    private readonly HashSet<MatureTheme> matureThemes = [];

    private readonly List<string> hooks = [];
    private string overview = string.Empty;
    private string history = string.Empty;
    private string goals = string.Empty;

    private string timezone = string.Empty;
    private string playtimes = string.Empty;
    private string contact = string.Empty;

    private ProfileVisibility visibility = ProfileVisibility.Public;
    private AvailabilityOverride availability = AvailabilityOverride.Derived;

    private byte[]? pendingPortrait;
    private string? pendingPortraitName;

    private string? validationError;
    private bool saving;

    public ProfileEditorWindow(
        Configuration config,
        ProfileService profiles,
        AtlasService atlas,
        LocationService location,
        ScreenshotService screenshots,
        ImageCache images,
        NotificationService notifications)
        : base("My card###CompassProfileEditor")
    {
        this.config = config;
        this.profiles = profiles;
        this.atlas = atlas;
        this.location = location;
        this.screenshots = screenshots;
        this.images = images;
        this.notifications = notifications;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 560),
            MaximumSize = new Vector2(1000, 1400),
        };

        Size = new Vector2(600, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseCompassTheme);

    public override void PostDraw() => Theme.Pop();

    /// <summary>Opens the editor, either on an existing card or a new one primed from the game.</summary>
    public void Open(ProfileDto? profile)
    {
        validationError = null;
        saving = false;
        pendingPortrait = null;
        pendingPortraitName = null;
        editing = profile;

        traits.Clear();
        tones.Clear();
        activities.Clear();
        matureThemes.Clear();
        hooks.Clear();

        if (profile is null)
        {
            var appearance = location.CurrentAppearance;

            name = location.CurrentCharacterName;
            title = string.Empty;
            race = appearance.Race ?? string.Empty;
            clan = appearance.Clan ?? string.Empty;
            gender = appearance.Gender ?? string.Empty;
            pronouns = string.Empty;
            quote = string.Empty;
            Array.Fill(archetype, string.Empty);
            age = AgeRange.Unspecified;

            length = RpLength.Casual;
            boundaries = string.Empty;
            walkups = true;

            overview = history = goals = string.Empty;
            timezone = playtimes = contact = string.Empty;
            visibility = ProfileVisibility.Public;
            availability = AvailabilityOverride.Derived;

            WindowName = "Write my card###CompassProfileEditor";
        }
        else
        {
            var identity = profile.Identity;

            name = identity.Name;
            title = identity.Title ?? string.Empty;
            race = identity.Race ?? string.Empty;
            clan = identity.Clan ?? string.Empty;
            gender = identity.Gender ?? string.Empty;
            pronouns = identity.Pronouns ?? string.Empty;
            quote = identity.Quote ?? string.Empty;
            age = identity.Age;

            for (var i = 0; i < archetype.Length; i++)
                archetype[i] = i < identity.Archetype.Count ? identity.Archetype[i] : string.Empty;

            foreach (var trait in profile.Personality)
                traits.Add(trait);

            foreach (var tone in profile.Style.Tones)
                tones.Add(tone);

            foreach (var activity in profile.Style.Activities)
                activities.Add(activity);

            length = profile.Style.Length;
            boundaries = profile.Style.Boundaries ?? string.Empty;
            walkups = profile.Style.WalkupsWelcome;

            foreach (var theme in profile.Style.MatureThemes)
                matureThemes.Add(theme);

            hooks.AddRange(profile.Hooks.Select(h => h.Text));
            overview = profile.Overview ?? string.Empty;
            history = profile.History ?? string.Empty;
            goals = profile.Goals ?? string.Empty;

            timezone = profile.Player.Timezone ?? string.Empty;
            playtimes = profile.Player.Availability ?? string.Empty;
            contact = profile.Player.Contact ?? string.Empty;

            visibility = profile.Visibility;
            availability = profile.Availability;

            WindowName = $"Editing {identity.Name}###CompassProfileEditor";
        }

        IsOpen = true;
    }

    public override void Draw()
    {
        screenshots.Draw();

        var scale = ImGuiHelpers.GlobalScale;

        if (!Svc.InWorld && editing is null)
        {
            Ornament.TextWrapped(Theme.Wax, "Log in to a character before writing their card.");
            return;
        }

        DrawEssentials(scale);
        Ornament.FleuronDivider(Theme.BrassDim);

        if (ImGui.BeginTabBar("##cardTabs"))
        {
            if (ImGui.BeginTabItem("Character"))
            {
                DrawCharacter(scale);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("How I play"))
            {
                DrawStyle(scale);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Story"))
            {
                DrawStory(scale);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("The player"))
            {
                DrawPlayer(scale);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawFooter(scale);
    }

    // --- The minute that matters -----------------------------------------

    private void DrawEssentials(float scale)
    {
        ImGui.BeginGroup();
        DrawPortraitSlot(scale);
        ImGui.EndGroup();

        ImGui.SameLine(0, 12f * scale);
        ImGui.BeginGroup();

        Ornament.PageLabel("Their name, as you want it written");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##name", "Shaala Avarr", ref name, ProfileLimits.NameMaxLength);

        Ornament.PageLabel("An epithet, if they have one");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##title", "The Wolf Without a Den", ref title, ProfileLimits.TitleMaxLength);

        Ornament.PageLabel("Three words");
        var third = (ImGui.GetContentRegionAvail().X - (8f * scale)) / 3f;

        for (var i = 0; i < archetype.Length; i++)
        {
            ImGui.SetNextItemWidth(third);
            ImGui.InputTextWithHint($"##arch{i}", i switch
            {
                0 => "Wild",
                1 => "Wanderer",
                _ => "Independent",
            }, ref archetype[i], ProfileLimits.ArchetypeWordMaxLength);

            if (i < archetype.Length - 1)
                ImGui.SameLine(0, 4f * scale);
        }

        ImGui.EndGroup();
    }

    private void DrawPortraitSlot(float scale)
    {
        var size = 96f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var box = new Vector2(origin.X + size, origin.Y + (size * 1.2f));

        var texture = pendingPortrait is null && editing?.PortraitImageId is { } id
            ? images.Get(id, thumb: false)
            : null;

        if (texture is not null)
            draw.AddImage(texture.Handle, origin, box);
        else
            draw.AddRectFilled(origin, box, Theme.Well.Packed());

        draw.AddRect(origin, box, Theme.Brass.Packed(), 0f, ImDrawFlags.None, 1.4f * scale);

        if (pendingPortrait is not null)
        {
            const string Ready = "New portrait";
            var textSize = ImGui.CalcTextSize(Ready);
            draw.AddText(
                new Vector2(origin.X + ((size - textSize.X) / 2f), origin.Y + ((size * 1.2f - textSize.Y) / 2f)),
                Theme.Verdigris.Packed(),
                Ready);
        }

        ImGui.Dummy(new Vector2(size, size * 1.2f));

        if (ImGui.Button(screenshots.Capturing ? "..." : "Capture", new Vector2(size, 0)))
            _ = CapturePortraitAsync();

        if (ImGui.Button("Choose file", new Vector2(size, 0)))
        {
            screenshots.PickFile((bytes, fileName) =>
            {
                pendingPortrait = bytes;
                pendingPortraitName = fileName;
            });
        }

        if (screenshots.LastError is { } error)
            Ornament.TextWrapped(Theme.Wax, error);
    }

    private async Task CapturePortraitAsync()
    {
        var bytes = await screenshots.CaptureAsync();
        if (bytes is null)
            return;

        pendingPortrait = bytes;
        pendingPortraitName = "portrait.png";
    }

    // --- Character -------------------------------------------------------

    private void DrawCharacter(float scale)
    {
        var half = (ImGui.GetContentRegionAvail().X - (8f * scale)) / 2f;

        ImGui.SetNextItemWidth(half);
        ImGui.InputTextWithHint("##race", "Race", ref race, 32);
        ImGui.SameLine(0, 8f * scale);
        ImGui.SetNextItemWidth(half);
        ImGui.InputTextWithHint("##clan", "Clan", ref clan, 32);

        ImGui.SetNextItemWidth(half);
        ImGui.InputTextWithHint("##gender", "Gender", ref gender, 32);
        ImGui.SameLine(0, 8f * scale);
        ImGui.SetNextItemWidth(half);
        ImGui.InputTextWithHint("##pronouns", "Pronouns", ref pronouns, 32);

        ImGui.SetNextItemWidth(half);
        if (ImGui.BeginCombo("##age", ProfileLabels.Describe(age)))
        {
            foreach (var option in Enum.GetValues<AgeRange>())
            {
                if (ImGui.Selectable(ProfileLabels.Describe(option), age == option))
                    age = option;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Ornament.Text(Theme.MutedDeep, "A bracket, not a number.");

        ImGui.Spacing();
        Ornament.PageLabel("A line they might say");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##quote", "Between instinct and civilisation.", ref quote, ProfileLimits.QuoteMaxLength);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, $"Nature  ({traits.Count} of {ProfileLimits.MaxPersonalityTraits})");
        Ornament.Text(Theme.MutedDeep, "Pick a few. These are what people search by.");

        DrawTagGrid(Enum.GetValues<PersonalityTrait>(), traits, ProfileLabels.Describe, ProfileLimits.MaxPersonalityTraits, scale);
    }

    /// <summary>A wrapping grid of toggles, which is faster to scan and pick from than a dropdown.</summary>
    private void DrawTagGrid<T>(T[] all, HashSet<T> selected, Func<T, string> label, int max, float scale)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var used = 0f;

        foreach (var option in all)
        {
            var text = label(option);
            var width = ImGui.CalcTextSize(text).X + (18f * scale);

            if (used > 0f && used + width > available)
            {
                ImGui.NewLine();
                used = 0f;
            }
            else if (used > 0f)
            {
                ImGui.SameLine(0, 4f * scale);
            }

            var on = selected.Contains(option);
            var full = !on && selected.Count >= max;

            ImGui.PushStyleColor(ImGuiCol.Button, on ? Theme.BrassBright : Theme.PanelRaised);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, on ? Theme.Gold : Theme.BrassDim);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, on ? Theme.GoldBright : Theme.Brass);
            ImGui.PushStyleColor(ImGuiCol.Text, on ? Theme.Leather : full ? Theme.MutedDeep : Theme.CreamDim);

            if (ImGui.Button(text) && !full)
            {
                if (on)
                    selected.Remove(option);
                else
                    selected.Add(option);
            }

            ImGui.PopStyleColor(4);
            used += width + (4f * scale);
        }

        ImGui.NewLine();
    }

    // --- Style -----------------------------------------------------------

    private void DrawStyle(float scale)
    {
        Ornament.Text(Theme.BrassBright, "How long a scene");

        foreach (var option in Enum.GetValues<RpLength>())
        {
            if (ImGui.RadioButton(ProfileLabels.Describe(option), length == option))
                length = option;

            Ornament.Tooltip(ProfileLabels.Hint(option));
            ImGui.SameLine(0, 10f * scale);
        }

        ImGui.NewLine();
        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, $"Tone  ({tones.Count} of {ProfileLimits.MaxTones})");
        DrawTagGrid(Enum.GetValues<RpTone>(), tones, ProfileLabels.Describe, ProfileLimits.MaxTones, scale);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, $"What you enjoy  ({activities.Count} of {ProfileLimits.MaxActivities})");
        DrawTagGrid(Enum.GetValues<RpActivity>(), activities, ProfileLabels.Describe, ProfileLimits.MaxActivities, scale);

        Ornament.FleuronDivider(Theme.BrassDim);

        ImGui.Checkbox("Strangers may walk up", ref walkups);
        Ornament.Tooltip("Turn this off if you would rather be asked first.");

        Ornament.FleuronDivider(Theme.BrassDim);
        DrawMatureThemes(scale);

        ImGui.Spacing();
        Ornament.PageLabel("What you will not play");
        Ornament.Text(Theme.MutedDeep, "Shown on the face of your card, where it prevents trouble.");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##boundaries", "No romance. Ask before injury.", ref boundaries, ProfileLimits.BoundariesMaxLength);
    }

    /// <summary>
    /// What adult content this player is willing to write, when a story leads there.
    ///
    /// Deliberately framed as willingness rather than as something on offer, and deliberately several
    /// flags rather than one NSFW switch: somebody happy to write a character's addiction in detail may
    /// want nothing to do with explicit scenes, and that distinction is the entire point of having it.
    ///
    /// Nothing here is required, nothing is on by default, and a card carrying any of it is hidden from
    /// anybody who has not asked to see adult content at all.
    /// </summary>
    private void DrawMatureThemes(float scale)
    {
        Ornament.Text(Theme.BrassBright, "Adult content you are willing to play");

        var confirmed = atlas.Account?.AdultConfirmed ?? false;

        if (!confirmed)
        {
            Ornament.TextWrapped(Theme.MutedDeep,
                "Confirm you are an adult before marking a card with any of this. One time, per account.");

            var agree = false;
            if (ImGui.Checkbox("I am 18 or over", ref agree) && agree)
                atlas.ConfirmAdult(true);

            return;
        }

        Ornament.TextWrapped(Theme.MutedDeep,
            "Willingness, not an advertisement. Leave it all unticked if none of it applies, which is the default.");

        DrawTagGrid(Enum.GetValues<MatureTheme>(), matureThemes, ProfileLabels.Describe, ProfileLimits.MaxMatureThemes, scale);

        foreach (var theme in matureThemes.OrderBy(t => (int)t))
            Ornament.TextWrapped(Theme.MutedDeep, ProfileLabels.Hint(theme));

        if (matureThemes.Count > 0)
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.WaxText,
                "This card will only be shown to people who have asked to see adult content.");
        }
    }

    // --- Story -----------------------------------------------------------

    private void DrawStory(float scale)
    {
        Ornament.Text(Theme.BrassBright, $"Hooks  ({hooks.Count} of {ProfileLimits.MaxHooks})");
        Ornament.TextWrapped(Theme.MutedDeep,
            "One sentence each, giving a stranger a reason to speak to you. This is the part people actually read.");

        for (var i = 0; i < hooks.Count; i++)
        {
            var hook = hooks[i];

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (32f * scale));
            if (ImGui.InputText($"##hook{i}", ref hook, ProfileLimits.HookMaxLength))
                hooks[i] = hook;

            ImGui.SameLine();
            if (ImGui.SmallButton($"x##delhook{i}"))
            {
                hooks.RemoveAt(i);
                break;
            }
        }

        if (hooks.Count < ProfileLimits.MaxHooks && ImGui.Button("Add a hook"))
            hooks.Add(string.Empty);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.PageLabel($"In brief  ({ProfileLimits.OverviewMaxLength - overview.Length} left)");
        Ornament.Text(Theme.MutedDeep, "The only prose that appears on the card itself.");
        ImGui.InputTextMultiline("##overview", ref overview, ProfileLimits.OverviewMaxLength, new Vector2(-1, 80f * scale));

        Ornament.PageLabel("What they want");
        ImGui.InputTextMultiline("##goals", ref goals, ProfileLimits.GoalsMaxLength, new Vector2(-1, 60f * scale));

        Ornament.PageLabel("History, for anyone who asks");
        ImGui.InputTextMultiline("##history", ref history, ProfileLimits.HistoryMaxLength, new Vector2(-1, 110f * scale));
    }

    // --- Player ----------------------------------------------------------

    private void DrawPlayer(float scale)
    {
        Ornament.TextWrapped(Theme.MutedDeep,
            "About you rather than your character. All optional, and none of it is required to publish.");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(200f * scale);
        ImGui.InputTextWithHint("##tz", "Timezone, e.g. GMT", ref timezone, ProfileLimits.TimezoneMaxLength);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##playtimes", "When you usually play", ref playtimes, 200);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##contact", "How to reach you, if you want that known", ref contact, ProfileLimits.ContactMaxLength);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Who may see this card");

        ImGui.SetNextItemWidth(160f * scale);
        if (ImGui.BeginCombo("##visibility", ProfileLabels.Describe(visibility)))
        {
            foreach (var option in Enum.GetValues<ProfileVisibility>())
            {
                if (ImGui.Selectable(ProfileLabels.Describe(option), visibility == option))
                    visibility = option;

                Ornament.Tooltip(ProfileLabels.Hint(option));
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        Ornament.Text(Theme.BrassBright, "Availability");

        ImGui.SetNextItemWidth(200f * scale);
        if (ImGui.BeginCombo("##availability", ProfileLabels.Describe(availability)))
        {
            foreach (var option in Enum.GetValues<AvailabilityOverride>())
            {
                if (ImGui.Selectable(ProfileLabels.Describe(option), availability == option))
                    availability = option;

                Ornament.Tooltip(ProfileLabels.Hint(option));
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Ornament.Text(Theme.MutedDeep, ProfileLabels.Hint(availability));
    }

    // --- Footer ----------------------------------------------------------

    private void DrawFooter(float scale)
    {
        Ornament.FleuronDivider(Theme.BrassDim, 2f);

        if (validationError is { } error)
            Ornament.TextWrapped(Theme.WaxText, error);

        DrawCompletionMeter(scale);

        if (Ornament.AccentButton(saving ? "Saving..." : "Save my card", new Vector2(150f * scale, 28f * scale), !saving))
            Submit();

        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(90f * scale, 28f * scale)))
            IsOpen = false;

        if (editing is null)
            return;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - (110f * scale));

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Wax);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Cream);

        if (ImGui.Button("Retire card", new Vector2(96f * scale, 28f * scale)))
        {
            profiles.Delete(editing);
            IsOpen = false;
        }

        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// A meter showing how filled the card is.
    ///
    /// Never a gate on publishing. It is here because watching a card fill in is what persuades
    /// somebody to keep going; refusing an incomplete one just means they publish nothing at all.
    /// </summary>
    private void DrawCompletionMeter(float scale)
    {
        var earned = 0;
        const int Total = 8;

        if (pendingPortrait is not null || editing?.HasPortrait == true) earned++;
        if (!string.IsNullOrWhiteSpace(name)) earned++;
        if (traits.Count > 0) earned++;
        if (hooks.Any(h => !string.IsNullOrWhiteSpace(h))) earned++;
        if (!string.IsNullOrWhiteSpace(overview)) earned++;
        if (tones.Count > 0 || activities.Count > 0) earned++;
        if (archetype.Any(a => !string.IsNullOrWhiteSpace(a)) || !string.IsNullOrWhiteSpace(quote)) earned++;
        if (!string.IsNullOrWhiteSpace(goals) || !string.IsNullOrWhiteSpace(history)) earned++;

        var fraction = earned / (float)Total;
        var width = 160f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var height = 6f * scale;

        draw.AddRectFilled(origin, new Vector2(origin.X + width, origin.Y + height), Theme.BrassDim.Packed(), 3f);
        draw.AddRectFilled(
            origin,
            new Vector2(origin.X + (width * fraction), origin.Y + height),
            (fraction >= 1f ? Theme.Verdigris : Theme.Gold).Packed(),
            3f);

        ImGui.Dummy(new Vector2(width, height));
        ImGui.SameLine();

        Ornament.Text(
            fraction >= 1f ? Theme.Verdigris : Theme.MutedDeep,
            fraction >= 1f ? "A full card" : $"{earned} of {Total} filled  ·  publish whenever you like");
    }

    private void Submit()
    {
        var trimmed = name.Trim();

        if (trimmed.Length < ProfileLimits.NameMinLength)
        {
            validationError = "Give them a name first. Everything else can wait.";
            return;
        }

        var characterName = editing?.CharacterName ?? location.CurrentCharacterName;
        var worldId = editing?.WorldId ?? location.CurrentWorldId;

        if (string.IsNullOrWhiteSpace(characterName) || worldId == 0)
        {
            validationError = "Compass cannot tell which character this is. Wait until you have loaded in.";
            return;
        }

        validationError = null;
        saving = true;

        var request = new SaveProfileRequest
        {
            CharacterName = characterName,
            WorldId = worldId,
            WorldName = editing?.WorldName ?? location.CurrentWorldName,
            DataCenter = editing?.DataCenter ?? location.CurrentDataCenter,
            Identity = new ProfileIdentity
            {
                Name = trimmed,
                Title = Blank(title),
                Race = Blank(race),
                Clan = Blank(clan),
                Age = age,
                Gender = Blank(gender),
                Pronouns = Blank(pronouns),
                Archetype = archetype.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
                Quote = Blank(quote),
            },
            Style = new ProfileStyle
            {
                Length = length,
                Tones = [.. tones],
                Activities = [.. activities],
                Boundaries = Blank(boundaries),
                WalkupsWelcome = walkups,
                MatureThemes = [.. matureThemes],
            },
            Player = new PlayerNotes
            {
                Timezone = Blank(timezone),
                Availability = Blank(playtimes),
                Contact = Blank(contact),
            },
            Personality = [.. traits],
            Hooks = hooks.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList(),
            Overview = Blank(overview),
            History = Blank(history),
            Goals = Blank(goals),
            Visibility = visibility,
            Availability = availability,
        };

        profiles.Save(request, pendingPortrait, pendingPortraitName, saved =>
        {
            saving = false;
            editing = saved;
            pendingPortrait = null;
            pendingPortraitName = null;
            IsOpen = false;
        });
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
