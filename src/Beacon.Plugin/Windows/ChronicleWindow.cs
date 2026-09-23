using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Beacons;
using Beacon.Shared.Profiles;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>
/// The Chronicle: who is out there, and whether they are the kind of company you are looking for.
///
/// Laid out like the atlas on purpose. The two halves answer different halves of the same question --
/// the atlas says where roleplay is happening, this says whose it is -- and a player moving between
/// them should not have to learn a second interface.
/// </summary>
public sealed class ChronicleWindow : Window
{
    private readonly Configuration config;

    private readonly ProfileService profiles;

    private readonly AtlasService atlas;

    private readonly TravelService travel;

    private readonly LocationService location;

    private readonly ImageCache images;

    private readonly NotificationService notifications;

    private readonly Action<ProfileDto?> openEditor;

    private string searchText = string.Empty;

    /// <summary>The card face, shared with the editor's preview so the two cannot drift.</summary>
    private readonly ProfileCard card;

    public ChronicleWindow(
        Configuration config,
        ProfileService profiles,
        AtlasService atlas,
        TravelService travel,
        LocationService location,
        ImageCache images,
        NotificationService notifications,
        Action<ProfileDto?> openEditor)
        : base("The Chronicle###BeaconChronicle")
    {
        this.config = config;
        this.profiles = profiles;
        this.atlas = atlas;
        this.travel = travel;
        this.location = location;
        this.images = images;
        this.notifications = notifications;
        card = new ProfileCard(images);
        this.openEditor = openEditor;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 480),
            MaximumSize = new Vector2(2000, 1600),
        };

        Size = new Vector2(940, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseBeaconTheme);

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        DrawFilterBar();

        var footer = (ImGui.GetFrameHeightWithSpacing() * 1.1f) + (6f * ImGuiHelpers.GlobalScale);
        var body = ImGui.GetContentRegionAvail().Y - footer;

        if (ImGui.BeginChild("##chronicleBody", new Vector2(0, body), false))
        {
            var scale = ImGuiHelpers.GlobalScale;
            var listWidth = Math.Max(250f * scale, ImGui.GetContentRegionAvail().X * 0.34f);

            if (ImGui.BeginChild("##souls", new Vector2(listWidth, 0), true))
                DrawList();

            ImGui.EndChild();
            ImGui.SameLine();

            if (ImGui.BeginChild("##card", new Vector2(0, 0), false))
                DrawCard(profiles.Selected);

            ImGui.EndChild();
        }

        ImGui.EndChild();

        DrawStatusBar();
        card.DrawGalleryPopup();
    }

    // --- Filters ---------------------------------------------------------

    private void DrawFilterBar()
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(180f * scale);
        if (ImGui.InputTextWithHint("##soulSearch", "Seek a soul...", ref searchText, 64))
            profiles.SetQuery(profiles.Query with { Search = string.IsNullOrWhiteSpace(searchText) ? null : searchText });

        ImGui.SameLine();

        var available = profiles.Query.AvailableOnly;
        if (Chip("Available now", available))
        {
            config.LastAvailableOnlyFilter = !available;
            config.Save();
            profiles.SetQuery(profiles.Query with { AvailableOnly = !available }, immediate: true);
        }

        ImGui.SameLine();

        if (Chip("Walk-ups", profiles.Query.WalkupsOnly))
            profiles.SetQuery(profiles.Query with { WalkupsOnly = !profiles.Query.WalkupsOnly }, immediate: true);

        ImGui.SameLine();
        DrawMatureFilter();

        ImGui.SameLine();
        DrawToneFilter(scale);

        ImGui.SameLine();
        DrawTraitFilter(scale);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(4f * scale, 0));
        ImGui.SameLine();

        var mine = profiles.MyProfile;
        if (ImGui.Button(mine is null ? "Write my card" : "Edit my card"))
            openEditor(mine);

        Ornament.FleuronDivider(Theme.BrassDim);
    }

    private void DrawToneFilter(float scale)
    {
        ImGui.SetNextItemWidth(120f * scale);

        var label = profiles.Query.Tones.Count switch
        {
            0 => "Any tone",
            1 => ProfileLabels.Describe(profiles.Query.Tones[0]),
            var n => $"{n} tones",
        };

        if (!ImGui.BeginCombo("##tone", label))
            return;

        foreach (var tone in Enum.GetValues<RpTone>())
        {
            var on = profiles.Query.Tones.Contains(tone);
            if (!ImGui.Selectable(ProfileLabels.Describe(tone), on, ImGuiSelectableFlags.DontClosePopups))
                continue;

            var next = profiles.Query.Tones.ToList();
            if (on)
                next.Remove(tone);
            else
                next.Add(tone);

            profiles.SetQuery(profiles.Query with { Tones = next }, immediate: true);
        }

        if (profiles.Query.Tones.Count > 0 && ImGui.Selectable("Clear", false))
            profiles.SetQuery(profiles.Query with { Tones = [] }, immediate: true);

        ImGui.EndCombo();
    }

    private void DrawTraitFilter(float scale)
    {
        ImGui.SetNextItemWidth(130f * scale);

        var label = profiles.Query.Personality.Count switch
        {
            0 => "Any nature",
            1 => ProfileLabels.Describe(profiles.Query.Personality[0]),
            var n => $"{n} traits",
        };

        if (!ImGui.BeginCombo("##trait", label))
            return;

        // Every selected trait narrows the search rather than widening it, which is worth saying
        // once here rather than leaving somebody to wonder why three picks found nobody.
        Ornament.Text(Theme.MutedDeep, "Cards must have all of these.");
        ImGui.Separator();

        foreach (var trait in Enum.GetValues<PersonalityTrait>())
        {
            var on = profiles.Query.Personality.Contains(trait);
            if (!ImGui.Selectable(ProfileLabels.Describe(trait), on, ImGuiSelectableFlags.DontClosePopups))
                continue;

            var next = profiles.Query.Personality.ToList();
            if (on)
                next.Remove(trait);
            else
                next.Add(trait);

            profiles.SetQuery(profiles.Query with { Personality = next }, immediate: true);
        }

        if (profiles.Query.Personality.Count > 0 && ImGui.Selectable("Clear", false))
            profiles.SetQuery(profiles.Query with { Personality = [] }, immediate: true);

        ImGui.EndCombo();
    }

    /// <summary>
    /// The one control that lets adult cards into a search.
    ///
    /// Off every time the window opens, never remembered, and unavailable until the account has
    /// confirmed its holder is an adult. Somebody who has not asked for this should never encounter
    /// it by scrolling.
    /// </summary>
    private void DrawMatureFilter()
    {
        var confirmed = atlas.Account?.AdultConfirmed ?? false;
        var on = profiles.Query.IncludeMature;

        if (!confirmed)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Panel);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Panel);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.MutedDeep);
            ImGui.Button("Adult content");
            ImGui.PopStyleColor(4);

            Ornament.Tooltip("Confirm you are an adult, in Settings, to include adult cards in a search.");
            return;
        }

        if (Chip("Adult content", on))
        {
            profiles.SetQuery(
                profiles.Query with { IncludeMature = !on, MatureThemes = on ? [] : profiles.Query.MatureThemes },
                immediate: true);
        }

        Ornament.Tooltip(on
            ? "Cards marked with adult themes are included in this search."
            : "Include cards marked with adult themes.");
    }

    private static bool Chip(string label, bool active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active ? Theme.BrassBright : Theme.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? Theme.Gold : Theme.BrassDim);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active ? Theme.GoldBright : Theme.Brass);
        ImGui.PushStyleColor(ImGuiCol.Text, active ? Theme.Leather : Theme.Muted);

        var clicked = ImGui.Button(label);

        ImGui.PopStyleColor(4);
        return clicked;
    }

    // --- The list --------------------------------------------------------

    private void DrawList()
    {
        var rows = profiles.Results;

        if (rows.Count == 0)
        {
            ImGui.Spacing();
            Ornament.TextWrapped(
                Theme.Muted,
                profiles.Loading
                    ? "Reading the Chronicle..."
                    : "Nobody matches. Try fewer traits, or turn off \"Available now\".");

            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var line = ImGui.GetTextLineHeight();
        var pad = 9f * scale;
        var portrait = 46f * scale;
        var rowHeight = Math.Max(portrait + (pad * 2f), (pad * 2f) + (line * 3f));
        var draw = ImGui.GetWindowDrawList();

        foreach (var profile in rows)
        {
            var selected = profiles.SelectedId == profile.Id;
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;

            ImGui.InvisibleButton("##soul" + profile.Id, new Vector2(width, rowHeight));
            var hovered = ImGui.IsItemHovered();

            if (ImGui.IsItemClicked())
                profiles.Select(profile.Id);

            var min = origin;
            var max = new Vector2(origin.X + width, origin.Y + rowHeight);

            if (selected || hovered)
                draw.AddRectFilled(min, max, Theme.PanelRaised.Fade(selected ? 0.50f : 0.26f).Packed(), 2f);

            if (selected)
                draw.AddRect(min, max, Theme.Gold.Packed(), 2f, ImDrawFlags.None, 1.8f * scale);
            else if (hovered)
                draw.AddRect(min, max, Theme.Brass.Fade(0.85f).Packed(), 2f, ImDrawFlags.None, 1f);

            DrawThumbnail(draw, profile, new Vector2(min.X + pad, min.Y + pad), portrait);

            var textLeft = min.X + pad + portrait + (9f * scale);
            var available = MathF.Max(24f, max.X - pad - textLeft);
            var y = min.Y + pad;

            draw.AddText(
                new Vector2(textLeft, y),
                Theme.Cream.Packed(),
                Ornament.TruncateToWidth(profile.Identity.Name, available));

            y += line;
            draw.AddText(
                new Vector2(textLeft, y),
                Theme.Muted.Packed(),
                Ornament.TruncateToWidth(profile.Identity.Lineage, available));

            y += line;
            var presence = profile.Presence;
            var dot = presence.IsOpen ? Theme.Verdigris : Theme.MutedDeep;

            draw.AddCircleFilled(new Vector2(textLeft + (4f * scale), y + (line / 2f)), 3.5f * scale, dot.Packed(), 10);
            draw.AddText(
                new Vector2(textLeft + (13f * scale), y),
                dot.Packed(),
                Ornament.TruncateToWidth(
                    presence.IsOpen ? ProfileLabels.Describe(presence.State) : ProfileLabels.LastSeen(presence.LastActiveAt),
                    available - (13f * scale)));

            ImGui.Dummy(new Vector2(width, 7f * scale));
        }
    }

    private void DrawThumbnail(ImDrawListPtr draw, ProfileDto profile, Vector2 at, float size)
    {
        var box = new Vector2(at.X + size, at.Y + size);

        if (profile.HasPortrait && images.Get(profile.PortraitImageId!.Value) is { } texture)
        {
            draw.AddImage(texture.Handle, at, box);
        }
        else
        {
            // A placeholder rather than a gap, so a list of cards without portraits still scans.
            draw.AddRectFilled(at, box, Theme.Well.Packed());

            var initial = string.IsNullOrWhiteSpace(profile.Identity.Name) ? "?" : profile.Identity.Name[..1];
            var textSize = ImGui.CalcTextSize(initial);
            draw.AddText(
                new Vector2(at.X + ((size - textSize.X) / 2f), at.Y + ((size - textSize.Y) / 2f)),
                Theme.BrassText.Packed(),
                initial);
        }

        draw.AddRect(at, box, Theme.Brass.Fade(0.8f).Packed());
    }

    // --- The card --------------------------------------------------------

    private void DrawCard(ProfileDto? profile)
    {
        if (profile is null)
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.Muted, "Choose somebody to read their card.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;

        Theme.PushPage();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f) * scale);

        if (ImGui.BeginChild("##cardPage", new Vector2(0, 0), true))
        {
            var origin = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();

            Ornament.CardinalRose(
                ImGui.GetWindowDrawList(),
                new Vector2(origin.X + size.X - (54f * scale), origin.Y + size.Y - (54f * scale)),
                42f * scale,
                Theme.Ink.Fade(0.09f));

            card.Draw(profile, scale);

            ImGui.Spacing();
            DrawActions(profile, scale);
        }

        ImGui.EndChild();

        ImGui.PopStyleVar();
        Theme.PopPage();
    }


    private void DrawActions(ProfileDto profile, float scale)
    {
        var mine = profile.OwnerAccountId == config.AccountId;
        var size = new Vector2(130f * scale, 26f * scale);

        // Travelling to somebody only means anything while they are standing at a lit beacon.
        if (profile.Presence.BeaconId is { } beaconId)
        {
            var beacon = atlas.Beacons.Concat(atlas.MyBeacons).FirstOrDefault(b => b.Id == beaconId);

            if (beacon is not null)
            {
                if (Ornament.AccentButton("Travel to them", size, travel.Plan(beacon).Possible))
                {
                    if (!travel.Begin(beacon, out var error))
                        notifications.Error(error);
                }
            }
            else if (Ornament.AccentButton("Find their fire", size))
            {
                atlas.Select(beaconId);
                notifications.Toast("Their beacon is selected in the atlas.", profile.Presence.BeaconName);
            }

            ImGui.SameLine();
        }

        if (mine && Ornament.AccentButton("Edit my card", size))
            openEditor(profile);

        if (mine)
            ImGui.SameLine();

        Ornament.ShareCode(profile.ShareCode, 22f * scale);
    }

    // --- Gallery ---------------------------------------------------------

    private void DrawStatusBar()
    {
        Ornament.FleuronDivider(Theme.BrassDim, 2f);

        var open = profiles.Results.Count(p => p.Presence.IsOpen);
        Ornament.Text(Theme.Muted, $"{open} of {profiles.TotalMatches} open to walk-ups");

        var mine = profiles.MyProfile;
        if (mine is not null)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - (230f * ImGuiHelpers.GlobalScale));

            var percent = (int)Math.Round(mine.Completeness * 100);
            Ornament.Text(percent >= 100 ? Theme.Verdigris : Theme.MutedDeep, $"Your card is {percent}% filled");
        }

        if (profiles.LastError is { } error)
        {
            Ornament.Text(Theme.WaxText, error);
            ImGui.SameLine();
            if (ImGui.SmallButton("x##profileErr"))
                profiles.ClearError();
        }
    }
}
