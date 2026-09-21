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

    private Guid? viewingGallery;

    private bool showLongProse;

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
        DrawGalleryPopup();
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

            DrawCardContents(profile, scale);
        }

        ImGui.EndChild();

        ImGui.PopStyleVar();
        Theme.PopPage();
    }

    private void DrawCardContents(ProfileDto profile, float scale)
    {
        var portraitSize = 104f * scale;

        DrawPortrait(profile, portraitSize, scale);

        ImGui.SameLine(0, 14f * scale);
        ImGui.BeginGroup();

        Ornament.Text(Theme.Ink, profile.Identity.Name);

        if (!string.IsNullOrWhiteSpace(profile.Identity.Title))
            Ornament.Text(Theme.Wax, $"“{profile.Identity.Title}”");

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.Identity.Lineage))
            details.Add(profile.Identity.Lineage);

        if (profile.Identity.Age != AgeRange.Unspecified)
            details.Add(ProfileLabels.Describe(profile.Identity.Age));

        if (!string.IsNullOrWhiteSpace(profile.Identity.Pronouns))
            details.Add(profile.Identity.Pronouns!);

        if (details.Count > 0)
            Ornament.Text(Theme.InkSoft, string.Join("  ·  ", details));

        if (profile.Identity.Archetype.Count > 0)
        {
            ImGui.Spacing();
            foreach (var word in profile.Identity.Archetype)
            {
                Ornament.Tag(word, Theme.ParchmentRule, Theme.Ink);
                ImGui.SameLine(0, 4f * scale);
            }

            ImGui.NewLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.Identity.Quote))
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.InkSoft, $"“{profile.Identity.Quote}”");
        }

        ImGui.EndGroup();

        Ornament.FleuronDivider(Theme.ParchmentRule, 4f);

        DrawPresence(profile, scale);

        if (profile.Personality.Count > 0)
        {
            Ornament.PageLabel("PERSONALITY");
            foreach (var trait in profile.Personality)
            {
                Ornament.Tag(ProfileLabels.Describe(trait), Theme.Brass, Theme.Ink);
                ImGui.SameLine(0, 4f * scale);
            }

            ImGui.NewLine();
            ImGui.Spacing();
        }

        if (profile.Hooks.Count > 0)
        {
            Ornament.PageLabel("WHY YOU MIGHT APPROACH THEM");

            foreach (var hook in profile.Hooks)
            {
                var cursor = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddTriangleFilled(
                    new Vector2(cursor.X + (2f * scale), cursor.Y + (4f * scale)),
                    new Vector2(cursor.X + (7f * scale), cursor.Y + (8f * scale)),
                    new Vector2(cursor.X + (2f * scale), cursor.Y + (12f * scale)),
                    Theme.Wax.Packed());

                ImGui.Indent(14f * scale);
                Ornament.TextWrapped(Theme.InkSoft, hook.Text);
                ImGui.Unindent(14f * scale);
            }

            ImGui.Spacing();
        }

        DrawStyle(profile);

        if (!string.IsNullOrWhiteSpace(profile.Overview))
        {
            Ornament.PageLabel("IN BRIEF");
            Ornament.TextWrapped(Theme.InkSoft, profile.Overview!);
            ImGui.Spacing();
        }

        DrawLongProse(profile);
        DrawLinks(profile);

        ImGui.Spacing();
        DrawActions(profile, scale);
    }

    private void DrawPortrait(ProfileDto profile, float size, float scale)
    {
        ImGui.BeginGroup();

        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var box = new Vector2(origin.X + size, origin.Y + (size * 1.25f));

        if (profile.HasPortrait && images.Get(profile.PortraitImageId!.Value, thumb: false) is { } texture)
        {
            draw.AddImage(texture.Handle, origin, box);
        }
        else
        {
            draw.AddRectFilled(origin, box, Theme.ParchmentShade.Packed());
            const string Label = "No likeness";
            var textSize = ImGui.CalcTextSize(Label);
            draw.AddText(
                new Vector2(origin.X + ((size - textSize.X) / 2f), origin.Y + ((size * 1.25f - textSize.Y) / 2f)),
                Theme.InkFaint.Packed(),
                Label);
        }

        draw.AddRect(origin, box, Theme.Brass.Packed(), 0f, ImDrawFlags.None, 1.4f * scale);

        ImGui.Dummy(new Vector2(size, size * 1.25f));

        if (profile.Gallery.Count > 1)
        {
            if (ImGui.SmallButton($"Gallery ({profile.Gallery.Count})##gal{profile.Id}"))
                viewingGallery = profile.Id;
        }

        ImGui.EndGroup();
    }

    private void DrawPresence(ProfileDto profile, float scale)
    {
        var presence = profile.Presence;
        var open = presence.IsOpen;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ParchmentShade);

        if (ImGui.BeginChild("##presence", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2.3f), false))
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(
                new Vector2(cursor.X + (5f * scale), cursor.Y + (ImGui.GetTextLineHeight() / 2f)),
                4.5f * scale,
                (open ? Theme.VerdigrisInk : Theme.InkFaint).Packed(),
                12);

            ImGui.Indent(16f * scale);
            Ornament.Text(open ? Theme.VerdigrisInk : Theme.InkFaint, ProfileLabels.Describe(presence.State));

            if (open && !string.IsNullOrWhiteSpace(presence.BeaconName))
            {
                var left = presence.LitUntil is { } until
                    ? $"  ·  {Ornament.Remaining(until - DateTimeOffset.UtcNow)}"
                    : string.Empty;

                Ornament.Text(Theme.InkSoft, $"At {presence.BeaconName}  ·  {presence.ZoneName}{left}");
            }
            else
            {
                Ornament.Text(Theme.InkFaint, ProfileLabels.LastSeen(presence.LastActiveAt));
            }

            ImGui.Unindent(16f * scale);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawStyle(ProfileDto profile)
    {
        var style = profile.Style;

        Ornament.PageLabel("HOW THEY PLAY");

        var parts = new List<string> { ProfileLabels.Describe(style.Length) };

        if (style.Tones.Count > 0)
            parts.Add(string.Join(", ", style.Tones.Select(ProfileLabels.Describe)));

        if (style.Activities.Count > 0)
            parts.Add(string.Join(", ", style.Activities.Select(ProfileLabels.Describe)));

        Ornament.TextWrapped(Theme.InkSoft, string.Join("  ·  ", parts));

        // Boundaries sit on the face of the card, not three clicks down. Somebody who finds out after
        // the fact has already had the interaction that makes people stop using a plugin.
        var notes = new List<string> { style.WalkupsWelcome ? "Walk-ups welcome" : "Ask before approaching" };

        if (!string.IsNullOrWhiteSpace(style.Boundaries))
            notes.Add(style.Boundaries!);

        Ornament.TextWrapped(Theme.Wax, string.Join("  ·  ", notes));

        if (style.MatureThemes.Count > 0)
        {
            // Phrased as willingness, matching how it was offered in the editor. "Open to" reads very
            // differently from "looking for", and only one of those is what this field means.
            Ornament.TextWrapped(
                Theme.Wax,
                "Open to: " + string.Join(", ", style.MatureThemes.Select(ProfileLabels.Describe)));
        }

        ImGui.Spacing();
    }

    private void DrawLongProse(ProfileDto profile)
    {
        var hasProse = !string.IsNullOrWhiteSpace(profile.History) || !string.IsNullOrWhiteSpace(profile.Goals);
        if (!hasProse)
            return;

        if (ImGui.SmallButton(showLongProse ? "Less" : "Read their chronicle"))
            showLongProse = !showLongProse;

        if (!showLongProse)
            return;

        if (!string.IsNullOrWhiteSpace(profile.Goals))
        {
            Ornament.PageLabel("WHAT THEY WANT");
            Ornament.TextWrapped(Theme.InkSoft, profile.Goals!);
        }

        if (!string.IsNullOrWhiteSpace(profile.History))
        {
            Ornament.PageLabel("HISTORY");
            Ornament.TextWrapped(Theme.InkSoft, profile.History!);
        }

        ImGui.Spacing();
    }

    private void DrawLinks(ProfileDto profile)
    {
        if (profile.Links.Count == 0)
            return;

        Ornament.PageLabel("TIES");

        foreach (var link in profile.Links)
        {
            var note = string.IsNullOrWhiteSpace(link.Note) ? string.Empty : $"  —  {link.Note}";
            var claim = link.Confirmed ? string.Empty : "  (unconfirmed)";

            Ornament.Text(
                link.Confirmed ? Theme.InkSoft : Theme.InkFaint,
                $"{ProfileLabels.Describe(link.Kind)}: {link.OtherName}{note}{claim}");
        }

        ImGui.Spacing();
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

    private void DrawGalleryPopup()
    {
        if (viewingGallery is not { } id)
            return;

        var profile = profiles.Selected;
        if (profile is null || profile.Id != id)
        {
            viewingGallery = null;
            return;
        }

        ImGui.OpenPopup("Gallery###BeaconGallery");

        var open = true;
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 300), new Vector2(1400, 1000));

        if (!ImGui.BeginPopupModal("Gallery###BeaconGallery", ref open, ImGuiWindowFlags.None))
        {
            viewingGallery = null;
            return;
        }

        if (!open)
        {
            viewingGallery = null;
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;

        foreach (var image in profile.Gallery)
        {
            Ornament.Text(Theme.BrassBright, ProfileLabels.Describe(image.Category));

            if (images.Get(image.ImageId, thumb: false) is { } texture)
            {
                var drawWidth = Math.Min(width, 520f * scale);
                var drawHeight = drawWidth * texture.Height / Math.Max(1f, texture.Width);
                ImGui.Image(texture.Handle, new Vector2(drawWidth, drawHeight));
            }
            else
            {
                Ornament.Text(Theme.MutedDeep, "Loading...");
            }

            if (!string.IsNullOrWhiteSpace(image.Caption))
                Ornament.TextWrapped(Theme.Muted, image.Caption!);

            Ornament.FleuronDivider(Theme.BrassDim);
        }

        if (ImGui.Button("Close"))
        {
            viewingGallery = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    // --- Footer ----------------------------------------------------------

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
