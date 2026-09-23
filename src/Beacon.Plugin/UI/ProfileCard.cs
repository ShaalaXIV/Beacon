using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Profiles;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;

namespace Beacon.UI;

/// <summary>
/// One roleplay card, drawn the way everyone else sees it.
///
/// This lives apart from the Chronicle because the editor needs to draw the same thing. A preview
/// that reimplements the card is a preview that lies the moment either copy changes -- which is
/// exactly what happened to the completeness meter, which kept its own duplicate of the rules and
/// silently stopped agreeing with them.
///
/// It draws the card and nothing else. The buttons underneath -- travel, edit, report -- belong to
/// whoever is showing the card, not to the card.
/// </summary>
public sealed class ProfileCard(ImageCache images)
{
    /// <summary>Set while the reader has asked for the long prose.</summary>
    private bool showLongProse;

    /// <summary>
    /// Called when the reader clicks a likeness, with the image to show full size.
    ///
    /// A callback rather than state of its own: the card draws inside a child window, and anything
    /// opened from in there has to be drawn by whoever owns the window, not by the card.
    /// </summary>
    public Action<Guid>? OpenImage { get; set; }

    /// <summary>Called when the reader asks for the whole gallery.</summary>
    public Action<ProfileDto>? OpenGallery { get; set; }

    /// <summary>
    /// A portrait chosen but not yet uploaded, so the editor's preview shows the picture being
    /// considered rather than the one it is about to replace.
    /// </summary>
    public IDalamudTextureWrap? PendingPortrait { get; set; }

    /// <summary>Draws the card face.</summary>
    public void Draw(ProfileDto profile, float scale) => DrawCardContents(profile, scale);

    /// <summary>Indents the cursor so something of this width sits in the middle of the card.</summary>
    private static void Centre(float itemWidth, float available)
    {
        var indent = (available - itemWidth) / 2f;
        if (indent > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
    }

    private static void CentreText(string text, float available) => Centre(ImGui.CalcTextSize(text).X, available);

    private void DrawCardContents(ProfileDto profile, float scale)
    {
        var width = ImGui.GetContentRegionAvail().X;

        // The likeness leads, centred, at a size worth looking at. Everything else sits beneath it and
        // scrolls -- a face is what somebody decides on, and it was previously a thumbnail beside a
        // column of text with half the card left empty.
        var portraitSize = Math.Clamp(width * 0.52f, 220f * scale, 460f * scale);

        Centre(portraitSize, width);
        DrawPortrait(profile, portraitSize, scale);

        ImGui.Spacing();

        CentreText(profile.Identity.Name, width);
        Ornament.Text(Theme.Ink, profile.Identity.Name);

        if (!string.IsNullOrWhiteSpace(profile.Identity.Title))
        {
            var title = $"“{profile.Identity.Title}”";
            CentreText(title, width);
            Ornament.Text(Theme.Wax, title);
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.Identity.Lineage))
            details.Add(profile.Identity.Lineage);

        if (profile.Identity.Age is { } age)
            details.Add($"Age {age}");

        if (details.Count > 0)
        {
            var line = string.Join("  ·  ", details);
            CentreText(line, width);
            Ornament.Text(Theme.InkSoft, line);
        }

        if (profile.Identity.Archetype.Count > 0)
        {
            ImGui.Spacing();

            // Measure the row of chips so it can be centred as a block rather than left-hung.
            var chipsWidth = 0f;
            foreach (var word in profile.Identity.Archetype)
                chipsWidth += ImGui.CalcTextSize(word).X + (18f * scale);

            Centre(chipsWidth, width);

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
            var quote = $"“{profile.Identity.Quote}”";

            // Only centre a quote that fits on one line; a wrapped one reads better ranged left.
            if (ImGui.CalcTextSize(quote).X < width)
                CentreText(quote, width);

            Ornament.TextWrapped(Theme.InkSoft, quote);
        }

        Ornament.FleuronDivider(Theme.ParchmentRule, 4f);

        DrawPresence(profile, scale);

        DrawMoment(profile, scale);

        DrawAtFirstGlance(profile, scale);

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
    }

    /// <summary>
    /// The part of the card that is true today: the live line, the IC/OOC flag, and the player's own
    /// note.
    ///
    /// The age of the live line is shown rather than hidden. A "currently" written three weeks ago is
    /// not current, and presenting it as though it were is the exact failure that makes people stop
    /// trusting the field at all.
    /// </summary>
    private void DrawMoment(ProfileDto profile, float scale)
    {
        var moment = profile.Moment;
        if (!moment.HasAnything)
            return;

        if (moment.Stance is not RpStance.Unstated)
        {
            var inCharacter = moment.Stance is RpStance.InCharacter;
            Ornament.Tag(
                inCharacter ? "In character" : "Out of character",
                inCharacter ? Theme.Wax : Theme.Brass,
                Theme.Ink);

            ImGui.Spacing();
        }

        if (!string.IsNullOrWhiteSpace(moment.Currently))
        {
            Ornament.PageLabel("CURRENTLY");
            Ornament.TextWrapped(Theme.Ink, moment.Currently!);

            if (moment.UpdatedAt is { } when)
                Ornament.Text(moment.IsFresh ? Theme.InkFaint : Theme.Wax, Ornament.Ago(when));

            ImGui.Spacing();
        }

        if (!string.IsNullOrWhiteSpace(moment.OutOfCharacter))
        {
            Ornament.PageLabel("OUT OF CHARACTER");
            Ornament.TextWrapped(Theme.InkSoft, moment.OutOfCharacter!);
            ImGui.Spacing();
        }
    }

    /// <summary>What a stranger notices before a word is exchanged.</summary>
    private void DrawAtFirstGlance(ProfileDto profile, float scale)
    {
        if (profile.AtFirstGlance.Count == 0)
            return;

        Ornament.PageLabel("AT FIRST GLANCE");

        foreach (var note in profile.AtFirstGlance)
        {
            if (note.Label.Length > 0)
            {
                Ornament.Text(Theme.InkFaint, note.Label);
                ImGui.SameLine(0, 6f * scale);
            }

            Ornament.TextWrapped(Theme.InkSoft, note.Text);
        }

        ImGui.Spacing();
    }

    private void DrawPortrait(ProfileDto profile, float size, float scale)
    {
        ImGui.BeginGroup();

        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var box = new Vector2(origin.X + size, origin.Y + (size * 1.25f));

        var portrait = PendingPortrait
                       ?? (profile.HasPortrait ? images.Get(profile.PortraitImageId!.Value, thumb: false) : null);

        if (portrait is not null)
        {
            draw.AddImage(portrait.Handle, origin, box);
        }
        else if (profile.HasPortrait)
        {
            // Fetching. Say so, rather than showing an empty well that reads as "the picture is gone".
            draw.AddRectFilled(origin, box, Theme.ParchmentShade.Packed());
            const string Fetching = "Loading...";
            var fetchingSize = ImGui.CalcTextSize(Fetching);
            draw.AddText(
                new Vector2(origin.X + ((size - fetchingSize.X) / 2f), origin.Y + ((size * 1.25f - fetchingSize.Y) / 2f)),
                Theme.InkFaint.Packed(),
                Fetching);
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

        // Clicking the likeness opens it at a size you can actually look at.
        if (portrait is not null && profile.HasPortrait)
        {
            ImGui.InvisibleButton($"##portrait{profile.Id}", new Vector2(size, size * 1.25f));

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                draw.AddRect(origin, box, Theme.Gold.Packed(), 0f, ImDrawFlags.None, 2f * scale);
                Ornament.Tooltip("Click to see it full size.");
            }

            if (ImGui.IsItemClicked())
                OpenImage?.Invoke(profile.PortraitImageId!.Value);
        }
        else
            ImGui.Dummy(new Vector2(size, size * 1.25f));

        if (profile.Gallery.Count > 1)
        {
            if (ImGui.SmallButton($"Gallery ({profile.Gallery.Count})##gal{profile.Id}"))
                OpenGallery?.Invoke(profile);
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

}
