using System.Numerics;
using Compass.Services;
using Compass.Shared.Beacons;
using Compass.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Compass.Windows;

/// <summary>
/// The atlas: a list of beacons on the left, and the selected one written out on a parchment page
/// on the right.
/// </summary>
public sealed class AtlasWindow : Window
{
    private readonly Configuration config;

    private readonly AtlasService atlas;

    private readonly TravelService travel;

    private readonly LocationService location;

    private readonly ImageCache images;

    private readonly NotificationService notifications;

    private readonly Action<BeaconDto?> openEditor;

    private readonly Action openSettings;

    private readonly Action openChronicle;

    private readonly Action<string, uint> openCharacterCard;

    private string searchText = string.Empty;

    private string shareCodeText = string.Empty;

    private int lightMinutes;

    private string lightNote = string.Empty;

    private bool lightPopupQueued;

    private Guid? confirmingDelete;

    public AtlasWindow(
        Configuration config,
        AtlasService atlas,
        TravelService travel,
        LocationService location,
        ImageCache images,
        NotificationService notifications,
        Action<BeaconDto?> openEditor,
        Action openSettings,
        Action openChronicle,
        Action<string, uint> openCharacterCard)
        : base("The Compass###CompassAtlas")
    {
        this.config = config;
        this.atlas = atlas;
        this.travel = travel;
        this.location = location;
        this.images = images;
        this.notifications = notifications;
        this.openEditor = openEditor;
        this.openSettings = openSettings;
        this.openChronicle = openChronicle;
        this.openCharacterCard = openCharacterCard;

        lightMinutes = config.DefaultLitMinutes;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 460),
            MaximumSize = new Vector2(2000, 1600),
        };

        Size = new Vector2(920, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseCompassTheme);

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        DrawFilterBar();

        var footerHeight = (ImGui.GetFrameHeightWithSpacing() * 1.1f) + (6f * ImGuiHelpers.GlobalScale);
        var bodyHeight = ImGui.GetContentRegionAvail().Y - footerHeight;

        if (ImGui.BeginChild("##body", new Vector2(0, bodyHeight), false))
        {
            if (ImGui.BeginTabBar("##compassTabs"))
            {
                if (ImGui.BeginTabItem("Atlas"))
                {
                    DrawTwoPane(atlas.Beacons, showEmptyHint: true);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("My beacons"))
                {
                    DrawMine();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        ImGui.EndChild();

        DrawStatusBar();
        DrawLightPopup();
    }

    // --- Filters ---------------------------------------------------------

    private void DrawFilterBar()
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(190f * scale);
        if (ImGui.InputTextWithHint("##search", "Seek a place...", ref searchText, 64))
            atlas.SetQuery(atlas.Query with { Search = string.IsNullOrWhiteSpace(searchText) ? null : searchText });

        ImGui.SameLine();

        ImGui.SetNextItemWidth(130f * scale);
        var dataCenter = atlas.Query.DataCenter ?? "Everywhere";
        if (ImGui.BeginCombo("##dc", dataCenter))
        {
            if (ImGui.Selectable("Everywhere", atlas.Query.DataCenter is null))
                ApplyDataCenter(null);

            var current = location.CurrentDataCenter;
            if (!string.IsNullOrEmpty(current) && ImGui.Selectable($"{current} (yours)", atlas.Query.DataCenter == current))
                ApplyDataCenter(current);

            ImGui.Separator();

            foreach (var name in location.DataCenters)
            {
                if (ImGui.Selectable(name, atlas.Query.DataCenter == name))
                    ApplyDataCenter(name);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(120f * scale);
        var kindLabel = atlas.Query.Kind?.ToString() ?? "Any kind";
        if (ImGui.BeginCombo("##kind", kindLabel))
        {
            if (ImGui.Selectable("Any kind", atlas.Query.Kind is null))
                atlas.SetQuery(atlas.Query with { Kind = null }, immediate: true);

            foreach (var kind in Enum.GetValues<BeaconKind>())
            {
                if (ImGui.Selectable(BeaconLabels.Describe(kind), atlas.Query.Kind == kind))
                    atlas.SetQuery(atlas.Query with { Kind = kind }, immediate: true);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        var litOnly = atlas.Query.LitOnly;
        if (ToggleChip("Burning now", litOnly))
        {
            config.LastLitOnlyFilter = !litOnly;
            config.Save();
            atlas.SetQuery(atlas.Query with { LitOnly = !litOnly }, immediate: true);
        }

        ImGui.SameLine();

        if (ToggleChip("Starred", atlas.Query.FavoritesOnly))
            atlas.SetQuery(atlas.Query with { FavoritesOnly = !atlas.Query.FavoritesOnly }, immediate: true);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(4f * scale, 0));
        ImGui.SameLine();

        if (ImGui.Button("Raise a beacon"))
            openEditor(null);

        ImGui.SameLine();
        if (ImGui.Button("Chronicle"))
            openChronicle();

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            openSettings();

        Ornament.FleuronDivider(Theme.BrassDim);
    }

    private void ApplyDataCenter(string? name)
    {
        config.LastDataCenterFilter = name;
        config.Save();
        atlas.SetQuery(atlas.Query with { DataCenter = name }, immediate: true);
    }

    /// <summary>A filter chip that reads as lit or unlit rather than as a checkbox.</summary>
    private static bool ToggleChip(string label, bool active)
    {
        var background = active ? Theme.Brass : Theme.PanelRaised;
        var text = active ? Theme.Cream : Theme.Muted;

        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? Theme.BrassBright : Theme.BrassDim);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Brass);
        ImGui.PushStyleColor(ImGuiCol.Text, text);

        var clicked = ImGui.Button(label);

        ImGui.PopStyleColor(4);
        return clicked;
    }

    // --- Panes -----------------------------------------------------------

    private void DrawTwoPane(List<BeaconDto> beacons, bool showEmptyHint)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var listWidth = Math.Max(240f * scale, ImGui.GetContentRegionAvail().X * 0.34f);

        if (ImGui.BeginChild("##list", new Vector2(listWidth, 0), true))
            DrawList(beacons, showEmptyHint);

        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##detail", new Vector2(0, 0), false))
            DrawDetail(atlas.Selected);

        ImGui.EndChild();
    }

    private void DrawMine()
    {
        if (atlas.MyBeacons.Count == 0)
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.Muted,
                "You have not raised any beacons yet. Stand somewhere worth gathering and raise one.");

            ImGui.Spacing();
            if (ImGui.Button("Raise a beacon here"))
                openEditor(null);

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
                atlas.RefreshMine();

            return;
        }

        DrawTwoPane(atlas.MyBeacons, showEmptyHint: false);
    }

    private void DrawList(List<BeaconDto> beacons, bool showEmptyHint)
    {
        if (beacons.Count == 0)
        {
            ImGui.Spacing();

            Ornament.TextWrapped(
                Theme.Muted,
                atlas.Loading
                    ? "Reading the atlas..."
                    : showEmptyHint
                        ? "No beacons match. Try widening the data centre, or turn off \"Burning now\"."
                        : "Nothing here.");

            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();

        var pad = 9f * scale;
        var gap = 7f * scale;
        var border = 1.8f * scale;

        // Derived from the real line height rather than a magic constant, so a row is always exactly
        // tall enough for what goes in it. The old fixed 52px was shorter than three lines plus a
        // burn bar at most UI scales, which is why rows bled into each other.
        var rowHeight = (pad * 2f) + (lineHeight * 3f) + (7f * scale);

        var draw = ImGui.GetWindowDrawList();

        foreach (var beacon in beacons)
        {
            var selected = atlas.SelectedId == beacon.Id;
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;

            // Deliberately not a Selectable. Its Header fill paints behind the text, which is what
            // made labels wash out on hover; an invisible hit area leaves every label at full
            // contrast in every state, and the selection reads from the outline instead.
            ImGui.InvisibleButton("##row" + beacon.Id, new Vector2(width, rowHeight));

            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked())
                atlas.Select(beacon.Id);

            var min = origin;
            var max = new Vector2(origin.X + width, origin.Y + rowHeight);

            if (selected || hovered)
                draw.AddRectFilled(min, max, Theme.PanelRaised.Fade(selected ? 0.50f : 0.26f).Packed(), 2f);

            if (selected)
                draw.AddRect(min, max, Theme.Gold.Packed(), 2f, ImDrawFlags.None, border);
            else if (hovered)
                draw.AddRect(min, max, Theme.Brass.Fade(0.85f).Packed(), 2f, ImDrawFlags.None, 1f);

            // Inset so the gold outline never sits on top of the ember edge.
            var inset = border + (1f * scale);
            draw.AddRectFilled(
                new Vector2(min.X + inset, min.Y + inset),
                new Vector2(min.X + inset + (3f * scale), max.Y - inset),
                Theme.FlameColour(beacon.Flame).Packed());

            var flameSize = 13f * scale;
            var flameCentreX = min.X + pad + (5f * scale) + (flameSize / 2f);
            var textLeft = flameCentreX + (flameSize / 2f) + (7f * scale);
            var available = MathF.Max(24f, max.X - pad - textLeft);

            Ornament.FlameAt(
                draw,
                new Vector2(flameCentreX, min.Y + pad + (lineHeight / 2f)),
                flameSize,
                beacon.Flame);

            var y = min.Y + pad;

            draw.AddText(
                new Vector2(textLeft, y),
                (beacon.Flame.IsBurning ? Theme.Cream : Theme.CreamDim).Packed(),
                Ornament.TruncateToWidth(beacon.Name, available));

            y += lineHeight;
            draw.AddText(
                new Vector2(textLeft, y),
                Theme.Muted.Packed(),
                Ornament.TruncateToWidth(beacon.Location.ZoneName + "  ·  " + beacon.Realm.WorldName, available));

            y += lineHeight;
            var (third, colour) = ThirdLine(beacon);
            draw.AddText(new Vector2(textLeft, y), colour.Packed(), Ornament.TruncateToWidth(third, available));

            if (beacon.Flame.IsBurning)
            {
                Ornament.BurnBarAt(
                    draw,
                    new Vector2(textLeft, min.Y + pad + (lineHeight * 3f) + (2f * scale)),
                    available,
                    beacon.Flame);
            }

            // Real space between boxes, so two outlines can never touch or appear to merge.
            ImGui.Dummy(new Vector2(width, gap));
        }

        DrawPager();
    }

    /// <summary>
    /// The third line of a list row. Always returns something: a row that sometimes has two lines and
    /// sometimes three would make the list jump as beacons light and go out.
    /// </summary>
    private static (string Text, Vector4 Colour) ThirdLine(BeaconDto beacon)
    {
        if (beacon.Flame.IsBurning)
        {
            if (!string.IsNullOrWhiteSpace(beacon.Flame.Note))
                return ("“" + beacon.Flame.Note + "”", Theme.BrassBright);

            var who = string.IsNullOrWhiteSpace(beacon.Flame.LitByName) ? "someone" : beacon.Flame.LitByName!;
            var left = beacon.Flame.Remaining is { } remaining ? "  ·  " + Ornament.Remaining(remaining) : string.Empty;
            return ("lit by " + who + left, Theme.BrassText);
        }

        return ("kept by " + beacon.OwnerName, Theme.MutedDeep);
    }

    private void DrawPager()
    {
        if (atlas.TotalMatches <= atlas.Query.PageSize)
            return;

        var pages = (int)Math.Ceiling(atlas.TotalMatches / (double)atlas.Query.PageSize);
        var page = atlas.Query.Page;

        ImGui.Spacing();

        if (page > 0 && ImGui.SmallButton("< Back"))
            atlas.GoToPage(page - 1);

        if (page > 0)
            ImGui.SameLine();

        Ornament.Text(Theme.MutedDeep, $"Page {page + 1} of {pages}");

        if (page + 1 < pages)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("On >"))
                atlas.GoToPage(page + 1);
        }
    }

    // --- The parchment page ----------------------------------------------

    private void DrawDetail(BeaconDto? beacon)
    {
        if (beacon is null)
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.Muted, "Choose a beacon to read its page.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;

        // The detail pane is the atlas page: parchment, dark ink, and a compass rose watermark.
        Theme.PushPage();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * scale);

        if (ImGui.BeginChild("##page", new Vector2(0, 0), true))
        {
            var origin = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();

            Ornament.CompassRose(
                ImGui.GetWindowDrawList(),
                new Vector2(origin.X + size.X - (52f * scale), origin.Y + size.Y - (52f * scale)),
                40f * scale,
                Theme.Ink.Fade(0.10f));

            DrawPageContents(beacon, scale);
        }

        ImGui.EndChild();

        ImGui.PopStyleVar();
        Theme.PopPage();
    }

    private void DrawPageContents(BeaconDto beacon, float scale)
    {
        DrawScreenshot(beacon, scale);

        ImGui.Spacing();

        Ornament.Text(Theme.Ink, beacon.Name);
        ImGui.SameLine();
        Ornament.Tag(BeaconLabels.Describe(beacon.Kind), Theme.ParchmentRule, Theme.InkFaint);

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - (44f * scale));

        var star = beacon.IsFavorite ? "Starred" : "Star";
        ImGui.PushStyleColor(ImGuiCol.Button, beacon.IsFavorite ? Theme.ParchmentShade : Theme.Parchment);
        ImGui.PushStyleColor(ImGuiCol.Text, beacon.IsFavorite ? Theme.Wax : Theme.InkFaint);
        if (ImGui.SmallButton($"{star}##fav"))
            atlas.ToggleFavorite(beacon);

        ImGui.PopStyleColor(2);

        Ornament.FleuronDivider(Theme.ParchmentRule, 3f);

        Ornament.PageLabel($"Kept by {beacon.OwnerName}  ·  {beacon.WhereText}  ·  {beacon.Location.MapCoordinateText}");

        if (beacon.Flame.IsBurning)
        {
            ImGui.Spacing();
            Ornament.Flame(beacon.Flame, 14f * scale);
            ImGui.SameLine(0, 6f * scale);

            var remaining = beacon.Flame.Remaining is { } left ? Ornament.Remaining(left) : "burning";
            var who = beacon.Flame.LitByName;

            if (string.IsNullOrWhiteSpace(who))
            {
                Ornament.Text(Theme.Wax, $"Lit by someone  ·  {remaining}");
            }
            else
            {
                // The whole reason both halves are one plugin: a flame says roleplay is happening, and
                // the card behind the name says whether it is the kind you are looking for.
                Ornament.Text(Theme.Wax, "Lit by");
                ImGui.SameLine(0, 4f * scale);

                if (Ornament.LinkText(who!, Theme.Wax, Theme.WaxBright))
                    openCharacterCard(who!, beacon.Realm.WorldId);

                Ornament.Tooltip($"Read {who}'s card.");

                ImGui.SameLine(0, 4f * scale);
                Ornament.Text(Theme.Wax, $"·  {remaining}");
            }

            if (!string.IsNullOrWhiteSpace(beacon.Flame.Note))
                Ornament.TextWrapped(Theme.InkSoft, $"“{beacon.Flame.Note}”");
        }

        if (!string.IsNullOrWhiteSpace(beacon.Description))
        {
            ImGui.Spacing();
            Ornament.TextWrapped(Theme.InkSoft, beacon.Description);
        }

        if (beacon.Tags.Count > 0)
        {
            ImGui.Spacing();
            foreach (var tag in beacon.Tags)
            {
                Ornament.Tag(tag, Theme.ParchmentRule, Theme.InkFaint);
                ImGui.SameLine(0, 4f * scale);
            }

            ImGui.NewLine();
        }

        ImGui.Spacing();
        DrawRoute(beacon, scale);

        ImGui.Spacing();
        DrawActions(beacon, scale);
    }

    private void DrawScreenshot(BeaconDto beacon, float scale)
    {
        var width = ImGui.GetContentRegionAvail().X;

        if (!beacon.HasImage)
        {
            // Keep the space: a page that reflows when a picture loads is worse than one with a gap.
            var height = width * 0.34f;
            var origin = ImGui.GetCursorScreenPos();

            ImGui.GetWindowDrawList().AddRect(
                origin,
                new Vector2(origin.X + width, origin.Y + height),
                Theme.ParchmentRule.Packed());

            var label = "No likeness recorded";
            var textSize = ImGui.CalcTextSize(label);
            ImGui.GetWindowDrawList().AddText(
                new Vector2(origin.X + ((width - textSize.X) / 2f), origin.Y + ((height - textSize.Y) / 2f)),
                Theme.InkFaint.Fade(0.7f).Packed(),
                label);

            ImGui.Dummy(new Vector2(width, height));
            return;
        }

        var texture = images.Get(beacon.ImageId!.Value, thumb: false);

        if (texture is null)
        {
            var height = width * 0.34f;
            ImGui.Dummy(new Vector2(width, height));
            return;
        }

        var aspect = texture.Height / (float)Math.Max(1, texture.Width);
        var drawHeight = width * aspect * config.ThumbnailScale;

        var imageOrigin = ImGui.GetCursorScreenPos();
        ImGui.Image(texture.Handle, new Vector2(width, drawHeight));

        ImGui.GetWindowDrawList().AddRect(
            imageOrigin,
            new Vector2(imageOrigin.X + width, imageOrigin.Y + drawHeight),
            Theme.Brass.Fade(0.65f).Packed());
    }

    private void DrawRoute(BeaconDto beacon, float scale)
    {
        var plan = travel.Plan(beacon);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ParchmentShade);
        if (ImGui.BeginChild("##route", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2.1f), false))
        {
            Ornament.PageLabel("Your journey");
            Ornament.TextWrapped(plan.Possible ? Theme.InkSoft : Theme.Wax, plan.Summary);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawActions(BeaconDto beacon, float scale)
    {
        var plan = travel.Plan(beacon);
        var buttonSize = new Vector2(120f * scale, 26f * scale);

        if (travel.IsTravelling && travel.Destination?.Id == beacon.Id)
        {
            if (Ornament.AccentButton("Stop", buttonSize))
                travel.Cancel();
        }
        else if (Ornament.AccentButton("Set out", buttonSize, plan.Possible && !plan.AlreadyThere))
        {
            if (!travel.Begin(beacon, out var error))
                notifications.Error(error);
        }

        ImGui.SameLine();

        DrawLightButton(beacon, buttonSize);

        ImGui.Spacing();
        Ornament.ShareCode(beacon.ShareCode, 22f * scale);

        // Owner controls live below the fold, so the page reads as a place first and a record second.
        if (beacon.OwnerAccountId != config.AccountId)
            return;

        ImGui.Spacing();

        if (ImGui.SmallButton("Edit"))
            openEditor(beacon);

        ImGui.SameLine();

        if (confirmingDelete == beacon.Id)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Wax);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Cream);

            if (ImGui.SmallButton("Really retire it?"))
            {
                atlas.Delete(beacon);
                confirmingDelete = null;
            }

            ImGui.PopStyleColor(2);

            ImGui.SameLine();
            if (ImGui.SmallButton("Keep it"))
                confirmingDelete = null;
        }
        else if (ImGui.SmallButton("Retire"))
        {
            confirmingDelete = beacon.Id;
        }
    }

    private void DrawLightButton(BeaconDto beacon, Vector2 size)
    {
        var mine = beacon.OwnerAccountId == config.AccountId;
        var mayLight = mine || beacon.AllowPublicLighting;
        var litByMe = beacon.Flame.IsBurning && beacon.Flame.LitByAccountId == config.AccountId;

        if (litByMe || (mine && beacon.Flame.IsBurning))
        {
            if (Ornament.AccentButton("Put it out", size))
                atlas.Extinguish(beacon);

            return;
        }

        if (!mayLight)
        {
            Ornament.AccentButton("Keeper only", size, enabled: false);
            Tooltip("Only this beacon's keeper may light it.");
            return;
        }

        var distance = location.DistanceTo(beacon);

        if (distance is null)
        {
            Ornament.AccentButton("Not here", size, enabled: false);
            Tooltip($"Stand in {beacon.Location.ZoneName} on {beacon.Realm.WorldName} to light this beacon.");
            return;
        }

        if (distance > BeaconLimits.LightingRangeYalms)
        {
            Ornament.AccentButton("Too far", size, enabled: false);
            Tooltip($"You are {distance:0} yalms away. Get within {BeaconLimits.LightingRangeYalms:0}.");
            return;
        }

        if (beacon.Flame.IsBurning)
        {
            Ornament.AccentButton("Already lit", size, enabled: false);
            Tooltip($"{beacon.Flame.LitByName} has this one burning.");
            return;
        }

        if (Ornament.AccentButton("Light it", size))
        {
            lightMinutes = config.DefaultLitMinutes;
            lightNote = string.Empty;
            lightPopupQueued = true;
        }
    }

    private static void Tooltip(string text) => Ornament.Tooltip(text);

    // --- Lighting --------------------------------------------------------

    private void DrawLightPopup()
    {
        if (lightPopupQueued)
        {
            ImGui.OpenPopup("Light the beacon###CompassLight");
            lightPopupQueued = false;
        }

        var open = true;
        if (!ImGui.BeginPopupModal("Light the beacon###CompassLight", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var beacon = atlas.Selected;
        if (beacon is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        Ornament.TextWrapped(Theme.CreamDim, $"Announce that you are at {beacon.Name} and open to roleplay.");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        ImGui.SliderInt(
            "##minutes",
            ref lightMinutes,
            BeaconLimits.MinLitMinutes,
            BeaconLimits.MaxLitMinutes,
            FormatMinutes(lightMinutes));

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##note", "A line for the atlas, if you like", ref lightNote, BeaconLimits.NoteMaxLength);

        ImGui.Spacing();

        if (ImGui.Button("Light it", new Vector2(110f * ImGuiHelpers.GlobalScale, 0)))
        {
            atlas.Light(beacon, lightMinutes, string.IsNullOrWhiteSpace(lightNote) ? null : lightNote);

            config.DefaultLitMinutes = lightMinutes;
            config.Save();

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button("Never mind"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static string FormatMinutes(int minutes) => minutes < 60
        ? $"{minutes} minutes"
        : minutes % 60 == 0
            ? $"{minutes / 60} hours"
            : $"{minutes / 60}h {minutes % 60}m";

    // --- Footer ----------------------------------------------------------

    private void DrawStatusBar()
    {
        Ornament.FleuronDivider(Theme.BrassDim, 2f);

        if (travel.IsTravelling || travel.Stage is JourneyStage.Arrived or JourneyStage.Failed)
        {
            var colour = travel.Stage switch
            {
                JourneyStage.Failed => Theme.Wax,
                JourneyStage.Arrived => Theme.Verdigris,
                _ => Theme.BrassBright,
            };

            Ornament.Flame(BeaconFlame.Dark, 12f * ImGuiHelpers.GlobalScale, animate: false);
            ImGui.SameLine(0, 6f);
            Ornament.Text(colour, travel.StatusText);

            if (travel.Stage is JourneyStage.Arrived or JourneyStage.Failed)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Dismiss"))
                    travel.Acknowledge();
            }
            else
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Stop"))
                    travel.Cancel();
            }

            return;
        }

        var lit = atlas.LitCount;
        Ornament.Text(Theme.Muted, lit switch
        {
            0 => "No fires burning in this view.",
            1 => "One fire burning.",
            _ => $"{lit} fires burning.",
        });

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - (210f * ImGuiHelpers.GlobalScale));

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##code", "Share code", ref shareCodeText, 16, ImGuiInputTextFlags.EnterReturnsTrue)
            && !string.IsNullOrWhiteSpace(shareCodeText))
        {
            atlas.OpenShareCode(shareCodeText.Trim());
            shareCodeText = string.Empty;
        }

        ImGui.SameLine();
        Ornament.Text(atlas.Connected ? Theme.Verdigris : Theme.Wax, atlas.Connected ? "Connected" : "Offline");

        if (atlas.LastError is { } error)
        {
            Ornament.Text(Theme.WaxText, error);
            ImGui.SameLine();
            if (ImGui.SmallButton("x##err"))
                atlas.ClearError();
        }
    }

}
