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

    /// <summary>The gallery the reader has opened, if any.</summary>
    public Guid? ViewingGallery { get; set; }

    /// <summary>
    /// A portrait chosen but not yet uploaded, so the editor's preview shows the picture being
    /// considered rather than the one it is about to replace.
    /// </summary>
    public IDalamudTextureWrap? PendingPortrait { get; set; }

    /// <summary>The card most recently drawn, so the gallery popup has something to show.</summary>
    private ProfileDto? shown;

    /// <summary>Draws the card face.</summary>
    public void Draw(ProfileDto profile, float scale)
    {
        shown = profile;
        DrawCardContents(profile, scale);
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

        if (profile.Identity.Age is { } age)
            details.Add($"Age {age}");

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

        ImGui.Dummy(new Vector2(size, size * 1.25f));

        if (profile.Gallery.Count > 1)
        {
            if (ImGui.SmallButton($"Gallery ({profile.Gallery.Count})##gal{profile.Id}"))
                ViewingGallery = profile.Id;
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

    /// <summary>The gallery, shown over the card when the reader opens it.</summary>
    public void DrawGalleryPopup()
    {
        if (ViewingGallery is not { } id)
            return;

        var profile = shown;
        if (profile is null || profile.Id != id)
        {
            ViewingGallery = null;
            return;
        }

        ImGui.OpenPopup("Gallery###BeaconGallery");

        var open = true;
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 300), new Vector2(1400, 1000));

        if (!ImGui.BeginPopupModal("Gallery###BeaconGallery", ref open, ImGuiWindowFlags.None))
        {
            ViewingGallery = null;
            return;
        }

        if (!open)
        {
            ViewingGallery = null;
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
            ViewingGallery = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
