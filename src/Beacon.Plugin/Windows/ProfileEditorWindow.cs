using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Profiles;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>
/// Writes a character's card.
///
/// Built around one idea: a card is publishable in about a minute. Race, clan, gender and name come
/// from the game, nothing below the first section is required, and the meter at the foot fills as you
/// go. A form that demands ten sections before it will accept anything is a form people abandon, and
/// an empty Chronicle helps nobody.
/// </summary>
public sealed class ProfileEditorWindow : Window, IDisposable
{
    private const float PortraitAspect = 5f / 6f;
    private const int PortraitOutputWidth = 600;
    private const int PortraitOutputHeight = 720;
    private const int PortraitSourceMaxBytes = 25 * 1024 * 1024;
    private const long PortraitSourceMaxPixels = 50_000_000;

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
    private string quote = string.Empty;
    private readonly string[] archetype = ["", "", ""];
    private string age = string.Empty;

    private readonly HashSet<PersonalityTrait> traits = [];
    private readonly HashSet<RpTone> tones = [];
    private readonly HashSet<RpActivity> activities = [];
    private RpLength length = RpLength.Casual;

    private string boundaries = string.Empty;
    private bool walkups = true;
    private readonly HashSet<MatureTheme> matureThemes = [];

    private readonly List<string> hooks = [];

    /// <summary>The "at first glance" slots, as label and line pairs.</summary>
    private readonly List<(string Label, string Text)> glances = [];

    private string outOfCharacter = string.Empty;
    private string currently = string.Empty;
    private RpStance stance = RpStance.Unstated;

    private string overview = string.Empty;
    private string history = string.Empty;
    private string goals = string.Empty;

    private string timezone = string.Empty;
    private string playtimes = string.Empty;
    private string contact = string.Empty;

    private ProfileVisibility visibility = ProfileVisibility.Public;
    private AvailabilityOverride availability = AvailabilityOverride.Derived;

    private IDalamudTextureWrap? pendingPortraitTexture;
    private string? pendingPortraitName;
    private float portraitZoom = 1f;
    private float portraitPanX = 0.5f;
    private float portraitPanY = 0.5f;
    private bool portraitLoading;

    /// <summary>The card face, the same one the Chronicle draws, so a preview cannot flatter the truth.</summary>
    private readonly ProfileCard preview;

    private bool previewing;

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
        : base("My card###BeaconProfileEditor")
    {
        this.config = config;
        this.profiles = profiles;
        this.atlas = atlas;
        this.location = location;
        this.screenshots = screenshots;
        this.images = images;
        this.notifications = notifications;
        preview = new ProfileCard(images);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 560),
            MaximumSize = new Vector2(1000, 1400),
        };

        Size = new Vector2(600, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseBeaconTheme);

    public override void PostDraw() => Theme.Pop();

    /// <summary>Opens the editor, either on an existing card or a new one primed from the game.</summary>
    public void Open(ProfileDto? profile)
    {
        validationError = null;
        saving = false;
        DisposePendingPortrait();
        pendingPortraitName = null;
        ResetPortraitCrop();
        editing = profile;

        traits.Clear();
        tones.Clear();
        activities.Clear();
        matureThemes.Clear();
        hooks.Clear();
        glances.Clear();

        if (profile is null)
        {
            var appearance = location.CurrentAppearance;

            name = location.CurrentCharacterName;
            title = string.Empty;
            race = appearance.Race ?? string.Empty;
            clan = appearance.Clan ?? string.Empty;
            gender = appearance.Gender ?? string.Empty;
            quote = string.Empty;
            Array.Fill(archetype, string.Empty);
            age = string.Empty;

            length = RpLength.Casual;
            boundaries = string.Empty;
            walkups = true;

            overview = history = goals = string.Empty;
            outOfCharacter = string.Empty;
            currently = string.Empty;
            stance = RpStance.Unstated;
            timezone = playtimes = contact = string.Empty;
            visibility = ProfileVisibility.Public;
            availability = AvailabilityOverride.Derived;

            WindowName = "Write my card###BeaconProfileEditor";
        }
        else
        {
            var identity = profile.Identity;

            name = identity.Name;
            title = identity.Title ?? string.Empty;
            race = identity.Race ?? string.Empty;
            clan = identity.Clan ?? string.Empty;
            gender = identity.Gender ?? string.Empty;
            quote = identity.Quote ?? string.Empty;
            age = identity.Age?.ToString() ?? string.Empty;

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
            glances.AddRange(profile.AtFirstGlance.Select(g => (g.Label, g.Text)));
            outOfCharacter = profile.Moment.OutOfCharacter ?? string.Empty;
            currently = profile.Moment.Currently ?? string.Empty;
            stance = profile.Moment.Stance;
            overview = profile.Overview ?? string.Empty;
            history = profile.History ?? string.Empty;
            goals = profile.Goals ?? string.Empty;

            timezone = profile.Player.Timezone ?? string.Empty;
            playtimes = profile.Player.Availability ?? string.Empty;
            contact = profile.Player.Contact ?? string.Empty;

            visibility = profile.Visibility;
            availability = profile.Availability;

            WindowName = $"Editing {identity.Name}###BeaconProfileEditor";
        }

        previewing = false;
        IsOpen = true;
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (!Svc.InWorld && editing is null)
        {
            Ornament.TextWrapped(Theme.Wax, "Log in to a character before writing their card.");
            return;
        }

        if (previewing)
        {
            DrawPreview(scale);
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

            if (ImGui.BeginTabItem("Right now"))
            {
                DrawRightNow(scale);
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
        ImGui.InputTextWithHint("##name", "Their name", ref name, ProfileLimits.NameMaxLength);

        Ornament.PageLabel("An epithet, if they have one");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##title", "An epithet", ref title, ProfileLimits.TitleMaxLength);

        Ornament.PageLabel("Three words");
        var third = (ImGui.GetContentRegionAvail().X - (8f * scale)) / 3f;

        for (var i = 0; i < archetype.Length; i++)
        {
            ImGui.SetNextItemWidth(third);
            ImGui.InputTextWithHint($"##arch{i}", i switch
            {
                0 => "First word",
                1 => "Second",
                _ => "Third",
            }, ref archetype[i], ProfileLimits.ArchetypeWordMaxLength);

            if (i < archetype.Length - 1)
                ImGui.SameLine(0, 4f * scale);
        }

        ImGui.EndGroup();
    }

    private void DrawPortraitSlot(float scale)
    {
        var size = 180f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var box = new Vector2(origin.X + size, origin.Y + (size / PortraitAspect));

        var texture = pendingPortraitTexture
                      ?? (editing?.PortraitImageId is { } id ? images.Get(id, thumb: false) : null);

        if (texture is not null)
        {
            var (uv0, uv1) = CropUvs(texture);
            draw.AddImage(texture.Handle, origin, box, uv0, uv1);
        }
        else
            draw.AddRectFilled(origin, box, Theme.Well.Packed());

        draw.AddRect(origin, box, Theme.Brass.Packed(), 0f, ImDrawFlags.None, 1.4f * scale);

        if (portraitLoading)
        {
            const string Loading = "Loading...";
            var textSize = ImGui.CalcTextSize(Loading);
            draw.AddText(
                new Vector2(origin.X + ((size - textSize.X) / 2f), origin.Y + (((size / PortraitAspect) - textSize.Y) / 2f)),
                Theme.Verdigris.Packed(),
                Loading);
        }

        ImGui.Dummy(new Vector2(size, size / PortraitAspect));

        if (!saving && !portraitLoading && ImGui.Button("Choose picture", new Vector2(size, 0)))
        {
            screenshots.PickFile((bytes, fileName) =>
            {
                _ = LoadPortraitAsync(bytes, fileName);
            }, PortraitSourceMaxBytes);
        }

        if (pendingPortraitTexture is not null)
        {
            Ornament.PageLabel("Crop portrait");

            ImGui.SetNextItemWidth(size);
            ImGui.SliderFloat("##portraitZoom", ref portraitZoom, 1f, 4f, "Zoom %.1fx");

            Ornament.Text(Theme.MutedDeep, "Left / right");
            ImGui.SetNextItemWidth(size);
            ImGui.SliderFloat("##portraitPanX", ref portraitPanX, 0f, 1f, string.Empty);

            Ornament.Text(Theme.MutedDeep, "Up / down");
            ImGui.SetNextItemWidth(size);
            ImGui.SliderFloat("##portraitPanY", ref portraitPanY, 0f, 1f, string.Empty);

            if (ImGui.SmallButton("Center crop"))
                ResetPortraitCrop();
        }

        if (screenshots.LastError is { } error)
            Ornament.TextWrapped(Theme.Wax, error);
    }

    private async Task LoadPortraitAsync(byte[] bytes, string fileName)
    {
        portraitLoading = true;
        validationError = null;

        try
        {
            var texture = await Svc.Textures.CreateFromImageAsync(
                bytes,
                debugName: "Beacon portrait preview");

            if ((long)texture.Width * texture.Height > PortraitSourceMaxPixels)
            {
                texture.Dispose();
                validationError = "That picture has too many pixels. Resize it below 50 megapixels first.";
                return;
            }

            DisposePendingPortrait();
            pendingPortraitTexture = texture;
            pendingPortraitName = Path.ChangeExtension(fileName, ".png");
            ResetPortraitCrop();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Beacon: could not decode the chosen portrait.");
            validationError = "Beacon could not open that picture. Try a PNG or JPEG instead.";
        }
        finally
        {
            portraitLoading = false;
        }
    }

    private (Vector2 Uv0, Vector2 Uv1) CropUvs(IDalamudTextureWrap texture)
    {
        var imageAspect = texture.Width / (float)texture.Height;
        var cropWidth = imageAspect > PortraitAspect ? PortraitAspect / imageAspect : 1f;
        var cropHeight = imageAspect > PortraitAspect ? 1f : imageAspect / PortraitAspect;

        cropWidth /= portraitZoom;
        cropHeight /= portraitZoom;

        var halfWidth = cropWidth / 2f;
        var halfHeight = cropHeight / 2f;
        var centerX = halfWidth + (portraitPanX * (1f - cropWidth));
        var centerY = halfHeight + (portraitPanY * (1f - cropHeight));

        return (
            new Vector2(centerX - halfWidth, centerY - halfHeight),
            new Vector2(centerX + halfWidth, centerY + halfHeight));
    }

    private void ResetPortraitCrop()
    {
        portraitZoom = 1f;
        portraitPanX = 0.5f;
        portraitPanY = 0.5f;
    }

    private void DisposePendingPortrait()
    {
        pendingPortraitTexture?.Dispose();
        pendingPortraitTexture = null;
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

        Ornament.PageLabel("Age");
        ImGui.SetNextItemWidth(half);
        ImGui.InputTextWithHint("##age", "Age", ref age, 5, ImGuiInputTextFlags.CharsDecimal);

        ImGui.Spacing();
        Ornament.PageLabel("A line they might say");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##quote", "A line in their own voice", ref quote, ProfileLimits.QuoteMaxLength);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, $"At first glance  ({glances.Count} of {ProfileLimits.MaxGlanceNotes})");
        Ornament.TextWrapped(Theme.MutedDeep,
            "What somebody notices before a word is exchanged. A label and a line: \"Hands\" / \"Ink to the\n"
            + "knuckles, badly done\". Leave the label blank if the line speaks for itself.");

        var labelWidth = 130f * scale;

        for (var i = 0; i < glances.Count; i++)
        {
            var (label, text) = glances[i];

            ImGui.SetNextItemWidth(labelWidth);
            if (ImGui.InputTextWithHint($"##glanceLabel{i}", "Label", ref label, ProfileLimits.GlanceLabelMaxLength))
                glances[i] = (label, text);

            ImGui.SameLine(0, 6f * scale);

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (32f * scale));
            if (ImGui.InputTextWithHint($"##glanceText{i}", "What they notice", ref text, ProfileLimits.GlanceTextMaxLength))
                glances[i] = (label, text);

            ImGui.SameLine();
            if (ImGui.SmallButton($"x##delglance{i}"))
            {
                glances.RemoveAt(i);
                break;
            }
        }

        if (glances.Count < ProfileLimits.MaxGlanceNotes && ImGui.Button("Add an impression"))
            glances.Add((string.Empty, string.Empty));

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
        ImGui.InputTextWithHint("##boundaries", "What you would rather not play", ref boundaries, ProfileLimits.BoundariesMaxLength);
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

    // --- Right now --------------------------------------------------------

    /// <summary>
    /// The part of a card that is true today rather than true in general.
    ///
    /// Borrowed wholesale from the addon culture that has had it for years, including the thing that
    /// makes it work: the live line is saved the moment you write it, never on the card's save button,
    /// because a field you have to remember to publish is a field that goes stale in a week.
    /// </summary>
    private void DrawRightNow(float scale)
    {
        Ornament.Text(Theme.BrassBright, "Are you in character?");
        Ornament.TextWrapped(Theme.MutedDeep,
            "The single most useful thing a card can say, and the one thing a fixed profile cannot.");

        DrawStanceOption(RpStance.Unstated, "Not saying", "The default, and what every card starts as.");
        ImGui.SameLine(0, 6f * scale);
        DrawStanceOption(RpStance.InCharacter, "In character", "Approach me as my character.");
        ImGui.SameLine(0, 6f * scale);
        DrawStanceOption(RpStance.OutOfCharacter, "Out of character", "Here, but not playing right now.");

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Currently");

        if (editing is { } profile)
        {
            Ornament.TextWrapped(Theme.MutedDeep,
                "What they are doing, in their own voice. Saved the moment you write it, not when you save\n"
                + "the card. You can also set it without opening this window, with /beacon currently.");

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##currently", "Camped above the falls, drying out", ref currently, ProfileLimits.CurrentlyMaxLength);

            if (ImGui.Button("Set it"))
                profiles.SetCurrently(profile, currently, stance);

            ImGui.SameLine();

            if (ImGui.Button("Clear it"))
            {
                currently = string.Empty;
                profiles.SetCurrently(profile, null, stance);
            }

            if (profile.Moment.UpdatedAt is { } when)
            {
                ImGui.SameLine(0, 10f * scale);
                Ornament.Text(profile.Moment.IsFresh ? Theme.MutedDeep : Theme.Wax, $"written {Ornament.Ago(when)}");
            }
        }
        else
        {
            Ornament.TextWrapped(Theme.MutedDeep,
                "Publish the card first. The live line is saved on its own, so it needs somewhere to live.");
        }

        Ornament.FleuronDivider(Theme.BrassDim);

        Ornament.Text(Theme.BrassBright, "Out of character");
        Ornament.TextWrapped(Theme.MutedDeep,
            "You speaking as yourself rather than as your character: a hiatus, a content warning, an\n"
            + "invitation to ask. Saved with the rest of the card.");

        ImGui.InputTextMultiline(
            "##ooc",
            ref outOfCharacter,
            ProfileLimits.OutOfCharacterMaxLength,
            new Vector2(-1, 80f * scale));
    }

    private void DrawStanceOption(RpStance option, string label, string tooltip)
    {
        var selected = stance == option;

        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Theme.BrassBright : Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? Theme.Leather : Theme.CreamDim);

        if (ImGui.Button($"{label}##stance{option}"))
            stance = option;

        ImGui.PopStyleColor(2);
        Ornament.Tooltip(tooltip);
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
            _ = SubmitAsync();

        ImGui.SameLine();
        if (ImGui.Button("Preview", new Vector2(90f * scale, 28f * scale)))
            previewing = true;

        Ornament.Tooltip("See the card the way everybody else will, before you publish it.");

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
    /// The card exactly as everybody else will read it, drawn by the same code the Chronicle uses.
    ///
    /// Not a mock-up of one. The point of a preview is to be trustworthy, and a second implementation
    /// is only trustworthy until the first one changes.
    /// </summary>
    private void DrawPreview(float scale)
    {
        Ornament.Text(Theme.BrassBright, "How others will read your card");
        Ornament.Text(Theme.MutedDeep, "Nothing here is saved yet.");

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - (110f * scale));
        if (ImGui.Button("Back to editing", new Vector2(110f * scale, 0)))
        {
            previewing = false;
            preview.PendingPortrait = null;
        }

        Ornament.FleuronDivider(Theme.BrassDim);

        var height = ImGui.GetContentRegionAvail().Y - (40f * scale);

        Theme.PushPage();
        if (ImGui.BeginChild("##previewPage", new Vector2(0, height), true))
        {
            // The picture being considered, not the one it is about to replace.
            preview.PendingPortrait = pendingPortraitTexture;
            preview.Draw(BuildDraft(), scale);
        }

        ImGui.EndChild();
        Theme.PopPage();

        ImGui.Spacing();
        DrawCompletionMeter(scale);
    }

    /// <summary>
    /// A meter showing how filled the card is.
    ///
    /// Never a gate on publishing. It is here because watching a card fill in is what persuades
    /// somebody to keep going; refusing an incomplete one just means they publish nothing at all.
    /// </summary>
    private void DrawCompletionMeter(float scale)
    {
        // Read from the draft, not from a second copy of the rules. The duplicate that used to live
        // here had already stopped agreeing with ProfileDto.Completeness.
        var draft = BuildDraft();
        var fraction = draft.Completeness;

        // A picture chosen but not yet uploaded still counts; the draft only knows about saved ones.
        if (pendingPortraitTexture is not null && !draft.HasPortrait)
            fraction = Math.Min(1f, fraction + (1f / ProfileDto.CompletenessCriteria));

        var total = ProfileDto.CompletenessCriteria;
        var earned = (int)MathF.Round(fraction * total);
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
            fraction >= 1f ? "A full card" : $"{earned} of {total} filled  ·  publish whenever you like");
    }

    /// <summary>
    /// The card as it stands in the editor right now, shaped exactly like one the server would send.
    ///
    /// Built rather than approximated, so the preview and the completeness meter both read the same
    /// thing every other player will. The meter used to keep its own copy of the rules and had already
    /// drifted out of agreement with them, which is the whole argument for doing it this way.
    /// </summary>
    private ProfileDto BuildDraft()
    {
        _ = int.TryParse(age, out var parsedAge);

        return new ProfileDto
        {
            Id = editing?.Id ?? Guid.Empty,
            OwnerAccountId = editing?.OwnerAccountId ?? config.AccountId,
            CharacterName = editing?.CharacterName ?? location.CurrentCharacterName,
            WorldId = editing?.WorldId ?? location.CurrentWorldId,
            WorldName = editing?.WorldName ?? location.CurrentWorldName,
            DataCenter = editing?.DataCenter ?? location.CurrentDataCenter,
            Identity = new ProfileIdentity
            {
                Name = name.Trim(),
                Title = Blank(title),
                Race = Blank(race),
                Clan = Blank(clan),
                Age = parsedAge > 0 ? parsedAge : null,
                Gender = Blank(gender),
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
                IsMature = matureThemes.Count > 0,
            },
            Player = new PlayerNotes
            {
                Timezone = Blank(timezone),
                Availability = Blank(playtimes),
                Contact = Blank(contact),
            },

            // Presence is the server's to derive from lit beacons, so the preview shows whatever the
            // card currently has rather than inventing a state.
            Presence = editing?.Presence ?? ProfilePresence.Unknown,
            Moment = new ProfileMoment
            {
                Currently = Blank(currently),
                OutOfCharacter = Blank(outOfCharacter),
                Stance = stance,
                UpdatedAt = editing?.Moment.UpdatedAt,
            },
            AtFirstGlance = glances
                .Where(g => !string.IsNullOrWhiteSpace(g.Text))
                .Select((g, i) => new GlanceNote { Label = g.Label.Trim(), Text = g.Text.Trim(), Order = i })
                .ToList(),
            Personality = [.. traits],
            Hooks = hooks
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select((h, i) => new ProfileHook { Text = h.Trim(), Order = i })
                .ToList(),
            Gallery = editing?.Gallery ?? [],
            Links = editing?.Links ?? [],
            Overview = Blank(overview),
            History = Blank(history),
            Goals = Blank(goals),
            PortraitImageId = editing?.PortraitImageId,
            Visibility = visibility,
            Availability = availability,
            ShareCode = editing?.ShareCode ?? string.Empty,
        };
    }

    private async Task SubmitAsync()
    {
        var trimmed = name.Trim();

        if (trimmed.Length < ProfileLimits.NameMinLength)
        {
            validationError = "Give them a name first. Everything else can wait.";
            return;
        }

        var characterName = editing?.CharacterName ?? location.CurrentCharacterName;
        var worldId = editing?.WorldId ?? location.CurrentWorldId;

        int? ageYears = null;
        if (!string.IsNullOrWhiteSpace(age))
        {
            if (!int.TryParse(age, out var parsedAge)
                || parsedAge < ProfileLimits.AgeMin
                || parsedAge > ProfileLimits.AgeMax)
            {
                validationError = $"Age must be between {ProfileLimits.AgeMin} and {ProfileLimits.AgeMax}.";
                return;
            }

            ageYears = parsedAge;
        }

        if (string.IsNullOrWhiteSpace(characterName) || worldId == 0)
        {
            validationError = "Beacon cannot tell which character this is. Wait until you have loaded in.";
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
                Age = ageYears,
                Gender = Blank(gender),
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
            AtFirstGlance = glances
                .Where(g => !string.IsNullOrWhiteSpace(g.Text))
                .Select((g, i) => new GlanceNote { Label = g.Label.Trim(), Text = g.Text.Trim(), Order = i })
                .ToList(),
            OutOfCharacter = Blank(outOfCharacter),
            Stance = stance,
            Overview = Blank(overview),
            History = Blank(history),
            Goals = Blank(goals),
            Visibility = visibility,
            Availability = availability,
        };

        byte[]? portrait = null;
        if (pendingPortraitTexture is not null)
        {
            var (uv0, uv1) = CropUvs(pendingPortraitTexture);
            portrait = await screenshots.CropToPngAsync(
                pendingPortraitTexture,
                uv0,
                uv1,
                PortraitOutputWidth,
                PortraitOutputHeight);

            if (portrait is null)
            {
                validationError = screenshots.LastError ?? "Could not prepare that portrait for upload.";
                saving = false;
                return;
            }
        }

        profiles.Save(request, portrait, pendingPortraitName, saved =>
        {
            saving = false;
            editing = saved;
            DisposePendingPortrait();
            pendingPortraitName = null;
            IsOpen = false;
        });
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => DisposePendingPortrait();
}
