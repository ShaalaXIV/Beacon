using System.Numerics;
using Beacon.Shared.Beacons;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Beacon.UI;

/// <summary>
/// The small drawn flourishes that make Beacon look like an atlas rather than a form: rules with a
/// fleuron, a cardinal rose watermark, a wax seal, and the flame that carries the whole idea.
///
/// All of it is draw-list work rather than images, so it scales with the player's UI scale and costs
/// nothing to ship.
/// </summary>
public static class Ornament
{
    /// <summary>A hairline rule broken by a small diamond, used between sections of a page.</summary>
    public static void FleuronDivider(Vector4 colour, float padding = 4f)
    {
        var draw = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + padding;
        var packed = colour.Packed();

        var centre = origin.X + (width / 2f);
        var gap = 7f * ImGuiHelpers.GlobalScale;

        draw.AddLine(new Vector2(origin.X, y), new Vector2(centre - gap, y), packed, 1f);
        draw.AddLine(new Vector2(centre + gap, y), new Vector2(origin.X + width, y), packed, 1f);

        // The fleuron: a small diamond standing on its point.
        var r = 3f * ImGuiHelpers.GlobalScale;
        draw.AddQuadFilled(
            new Vector2(centre, y - r),
            new Vector2(centre + r, y),
            new Vector2(centre, y + r),
            new Vector2(centre - r, y),
            packed);

        ImGui.Dummy(new Vector2(width, (padding * 2f) + 1f));
    }

    /// <summary>
    /// A cardinal rose, drawn faintly behind a panel as a watermark. Purely decorative, so it is drawn
    /// to the background draw list and never intercepts a click.
    /// </summary>
    public static void CardinalRose(ImDrawListPtr draw, Vector2 centre, float radius, Vector4 colour)
    {
        var packed = colour.Packed();

        draw.AddCircle(centre, radius, packed, 48, 1f);
        draw.AddCircle(centre, radius * 0.68f, packed, 48, 0.7f);

        // Four cardinal points, each a narrow kite from the centre.
        DrawPoint(draw, centre, radius, packed, 0f);
        DrawPoint(draw, centre, radius, packed, MathF.PI / 2f);
        DrawPoint(draw, centre, radius, packed, MathF.PI);
        DrawPoint(draw, centre, radius, packed, 3f * MathF.PI / 2f);
    }

    private static void DrawPoint(ImDrawListPtr draw, Vector2 centre, float radius, uint packed, float angle)
    {
        var tip = centre + Rotate(new Vector2(0f, -radius), angle);
        var left = centre + Rotate(new Vector2(-radius * 0.13f, -radius * 0.28f), angle);
        var right = centre + Rotate(new Vector2(radius * 0.13f, -radius * 0.28f), angle);

        draw.AddTriangleFilled(tip, right, centre, packed);
        draw.AddTriangleFilled(tip, centre, left, packed);
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2((v.X * cos) - (v.Y * sin), (v.X * sin) + (v.Y * cos));
    }

    /// <summary>
    /// A flame glyph for a beacon's state. Burning beacons breathe slightly, which is what makes a lit
    /// atlas feel alive rather than a list of green dots.
    /// </summary>
    public static void Flame(BeaconFlame flame, float size, bool animate = true)
    {
        var origin = ImGui.GetCursorScreenPos();
        FlameAt(ImGui.GetWindowDrawList(), new Vector2(origin.X + (size / 2f), origin.Y + (size / 2f)), size, flame, animate);
        ImGui.Dummy(new Vector2(size, size));
    }

    /// <summary>
    /// The same flame, drawn about a point rather than at the cursor.
    ///
    /// List rows are laid out with the draw list rather than the cursor, because mixing cursor-relative
    /// widgets with absolute drawing is what let row content overflow its own box in the first place.
    /// </summary>
    public static void FlameAt(ImDrawListPtr draw, Vector2 centre, float size, BeaconFlame flame, bool animate = true)
    {
        var colour = Theme.FlameColour(flame);

        // A slow breath, not a flicker: anything faster reads as a broken UI in peripheral vision.
        var pulse = animate && flame.IsBurning
            ? 1f + (0.08f * MathF.Sin((float)ImGui.GetTime() * 2.2f))
            : 1f;

        var w = size * 0.55f * pulse;
        var h = size * pulse;

        var tip = new Vector2(centre.X, centre.Y - (h * 0.5f));
        var left = new Vector2(centre.X - (w * 0.5f), centre.Y + (h * 0.22f));
        var right = new Vector2(centre.X + (w * 0.5f), centre.Y + (h * 0.22f));
        var basePoint = new Vector2(centre.X, centre.Y + (h * 0.45f));

        var packed = colour.Packed();
        draw.AddTriangleFilled(tip, right, basePoint, packed);
        draw.AddTriangleFilled(tip, basePoint, left, packed);

        if (flame.IsBurning)
        {
            // A pale heart, so a burning flame reads at a glance even at list size.
            var heart = new Vector2(centre.X, centre.Y + (h * 0.12f));
            draw.AddCircleFilled(heart, w * 0.2f, new Vector4(1f, 0.89f, 0.71f, 1f).Packed(), 10);
        }
    }

    /// <summary>
    /// A bar showing how much of a flame's burn is left. Tells a traveller whether the trip is worth
    /// making, which a simple "lit" badge cannot.
    /// </summary>
    public static void BurnBar(BeaconFlame flame, float width, float height = 3f)
    {
        var origin = ImGui.GetCursorScreenPos();
        BurnBarAt(ImGui.GetWindowDrawList(), origin, width, flame, height);
        ImGui.Dummy(new Vector2(width, height * ImGuiHelpers.GlobalScale));
    }

    /// <summary>The same bar, drawn at a point rather than at the cursor.</summary>
    public static void BurnBarAt(ImDrawListPtr draw, Vector2 origin, float width, BeaconFlame flame, float height = 3f)
    {
        var scaledHeight = height * ImGuiHelpers.GlobalScale;

        var track = new Vector2(origin.X + width, origin.Y + scaledHeight);
        draw.AddRectFilled(origin, track, Theme.BrassDim.Packed(), 1f);

        if (flame.IsBurning && flame.Remaining is { } remaining && flame.LitAt is { } litAt && flame.LitUntil is { } until)
        {
            var total = (until - litAt).TotalSeconds;
            var fraction = total > 0 ? Math.Clamp(remaining.TotalSeconds / total, 0d, 1d) : 0d;

            if (fraction > 0)
            {
                var filled = new Vector2(origin.X + (width * (float)fraction), origin.Y + scaledHeight);
                draw.AddRectFilled(origin, filled, Theme.FlameColour(flame).Packed(), 1f);
            }
        }
    }

    /// <summary>
    /// A beacon's share code: a wax seal you press to copy, with the code itself written beside it.
    ///
    /// The code deliberately is not written on the seal. A real seal carries a device, not lettering,
    /// and four characters crammed into a 26 pixel disc of dark wax cannot be read at any contrast --
    /// which is exactly what the first attempt proved. The seal carries a flame and does the copying;
    /// the code sits next to it in ink on paper, where it is legible without hovering anything.
    /// </summary>
    public static void ShareCode(string shareCode, float sealDiameter)
    {
        var draw = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();

        var radius = sealDiameter / 2f;
        var gap = 8f * scale;

        var codeSize = ImGui.CalcTextSize(shareCode);
        var height = MathF.Max(sealDiameter, codeSize.Y);

        // One hit box over the seal and the code together: both are the same affordance.
        ImGui.InvisibleButton("##share" + shareCode, new Vector2(sealDiameter + gap + codeSize.X, height));
        var hovered = ImGui.IsItemHovered();

        var centre = new Vector2(origin.X + radius, origin.Y + (height / 2f));

        draw.AddCircleFilled(centre, radius, (hovered ? Theme.WaxBright : Theme.Wax).Packed(), 28);
        draw.AddCircle(centre, radius * 0.82f, Theme.WaxDeep.Packed(), 28, 1f);

        DrawSealDevice(draw, centre, radius);

        var codePos = new Vector2(origin.X + sealDiameter + gap, origin.Y + ((height - codeSize.Y) / 2f));
        draw.AddText(codePos, (hovered ? Theme.Wax : Theme.Ink).Packed(), shareCode);

        // Underline on hover, so it reads as something you can act on.
        if (hovered)
        {
            var underlineY = codePos.Y + codeSize.Y;
            draw.AddLine(
                new Vector2(codePos.X, underlineY),
                new Vector2(codePos.X + codeSize.X, underlineY),
                Theme.Wax.Fade(0.6f).Packed(),
                1f);
        }

        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(shareCode);
            copiedCode = shareCode;
            copiedAt = DateTime.UtcNow;
        }

        Tooltip("Click to copy this share code. Anyone can paste it into /beacon go to find this beacon.");

        // Confirm the copy happened; a click that changes nothing on screen feels broken.
        if (copiedCode == shareCode && DateTime.UtcNow - copiedAt < CopiedFeedbackDuration)
        {
            ImGui.SameLine(0, gap);
            Text(Theme.VerdigrisInk, "Copied");
        }
    }

    private static string? copiedCode;

    private static DateTime copiedAt;

    private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromSeconds(2);

    /// <summary>The flame pressed into the wax. Drawn as a shallow relief rather than a flat shape.</summary>
    private static void DrawSealDevice(ImDrawListPtr draw, Vector2 centre, float radius)
    {
        var h = radius * 0.92f;
        var w = radius * 0.52f;

        var tip = new Vector2(centre.X, centre.Y - (h * 0.52f));
        var left = new Vector2(centre.X - (w * 0.5f), centre.Y + (h * 0.20f));
        var right = new Vector2(centre.X + (w * 0.5f), centre.Y + (h * 0.20f));
        var foot = new Vector2(centre.X, centre.Y + (h * 0.46f));

        // A dark impression offset down-right, then the lit face above it: the wax looks stamped.
        var shadow = new Vector2(0.8f, 0.8f);
        draw.AddTriangleFilled(tip + shadow, right + shadow, foot + shadow, Theme.WaxDeep.Packed());
        draw.AddTriangleFilled(tip + shadow, foot + shadow, left + shadow, Theme.WaxDeep.Packed());

        var face = Theme.WaxLight.Packed();
        draw.AddTriangleFilled(tip, right, foot, face);
        draw.AddTriangleFilled(tip, foot, left, face);
    }

    /// <summary>
    /// A tooltip in fixed, legible colours.
    ///
    /// Never use ImGui.SetTooltip directly here. A tooltip inherits whatever ImGuiCol.Text is pushed
    /// at the call site, and half this UI draws on parchment with dark ink text -- which the tooltip
    /// then renders on its dark popup background, giving brown on brown. Setting both ends explicitly
    /// is the only way a tooltip stays readable wherever it is raised from.
    ///
    /// Includes its own hover test, so call sites are a single line.
    /// </summary>
    public static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Leather);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Brass);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Cream);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9f, 7f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(340f * scale);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
    }

    /// <summary>
    /// Text that behaves like a link: underlined on hover, and returning true when clicked.
    ///
    /// Drawn rather than made a button so it sits inline in a sentence without the padding and frame
    /// a button brings with it.
    /// </summary>
    public static bool LinkText(string text, Vector4 colour, Vector4 hoverColour)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text);

        ImGui.InvisibleButton("##link" + text, size);
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        draw.AddText(origin, (hovered ? hoverColour : colour).Packed(), text);

        if (hovered)
        {
            draw.AddLine(
                new Vector2(origin.X, origin.Y + size.Y),
                new Vector2(origin.X + size.X, origin.Y + size.Y),
                hoverColour.Packed(),
                1f);

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return ImGui.IsItemClicked();
    }

    /// <summary>A small caps-style label for a field on the parchment page.</summary>
    public static void PageLabel(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.InkFaint);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>Text in a colour, without the ceremony of a push/pop at every call site.</summary>
    public static void Text(Vector4 colour, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>Wrapped text in a colour.</summary>
    public static void TextWrapped(Vector4 colour, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A tag chip, outlined rather than filled so a row of them stays quiet.</summary>
    public static void Tag(string label, Vector4 border, Vector4 text)
    {
        var padding = new Vector2(5f, 1f) * ImGuiHelpers.GlobalScale;
        var size = ImGui.CalcTextSize(label);
        var origin = ImGui.GetCursorScreenPos();
        var end = new Vector2(origin.X + size.X + (padding.X * 2f), origin.Y + size.Y + (padding.Y * 2f));

        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(origin, end, border.Packed(), 1f);
        draw.AddText(new Vector2(origin.X + padding.X, origin.Y + padding.Y), text.Packed(), label);

        ImGui.Dummy(new Vector2(size.X + (padding.X * 2f), size.Y + (padding.Y * 2f)));
    }

    /// <summary>
    /// A button in Beacon's own colours. Used for the one action a panel is really about, so that
    /// "set out" and "light the beacon" read as the point of the page.
    /// </summary>
    public static bool AccentButton(string label, Vector2 size, bool enabled = true)
    {
        // Brass is a light metal, so the label is engraved dark into it rather than printed pale on
        // top. Cream on brass never cleared 4:1 in any state, and only 1.6:1 when pressed; dark on
        // brass clears 6:1 throughout. Disabled drops to the dark panel, which is both readable and
        // unmistakably inactive.
        var background = enabled ? Theme.BrassBright : Theme.Panel;
        var text = enabled ? Theme.Leather : Theme.Muted;

        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled ? Theme.Gold : background);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled ? Theme.GoldBright : background);
        ImGui.PushStyleColor(ImGuiCol.Text, text);

        var clicked = ImGui.Button(label, size) && enabled;

        ImGui.PopStyleColor(4);
        return clicked;
    }

    /// <summary>
    /// Shortens text until it fits a pixel width, appending an ellipsis.
    ///
    /// Measured rather than counted. A fixed character limit is wrong at every UI scale and for every
    /// name that is not average width, which is how list text ended up crossing its own border.
    /// </summary>
    public static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string Ellipsis = "…";
        var ellipsisWidth = ImGui.CalcTextSize(Ellipsis).X;

        if (maxWidth <= ellipsisWidth)
            return Ellipsis;

        // Binary search the longest prefix that fits, rather than trimming a character at a time.
        var low = 0;
        var high = text.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisWidth <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return low <= 0 ? Ellipsis : text[..low].TrimEnd() + Ellipsis;
    }

    /// <summary>Formats a remaining duration the way a person would say it.</summary>
    public static string Remaining(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => "moments left",
        < 60 => $"{span.Minutes}m left",
        _ => $"{(int)span.TotalHours}h {span.Minutes}m left",
    };

    /// <summary>Formats a past instant the way a person would say it.</summary>
    public static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 24 * 60 => $"{(int)span.TotalHours}h ago",
            _ => $"{(int)span.TotalDays}d ago",
        };
    }
}
