using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Beacon.UI;

/// <summary>
/// The Beacon palette: dark leather and brass chrome around an aged parchment page.
///
/// Venue plugins all look like the rest of the game's UI, which is the right call for a directory of
/// shops. This is a directory of places people have imagined, so it is styled like a cartographer's
/// atlas instead: warm, inked, and deliberately not another grey panel.
/// </summary>
public static class Theme
{
    // --- Chrome: the leather-and-brass frame -----------------------------

    public static readonly Vector4 Leather = Rgb(0x20, 0x18, 0x0F);

    public static readonly Vector4 LeatherLight = Rgb(0x2C, 0x21, 0x14);

    public static readonly Vector4 Panel = Rgb(0x1C, 0x15, 0x09);

    public static readonly Vector4 PanelRaised = Rgb(0x2A, 0x1F, 0x10);

    public static readonly Vector4 Well = Rgb(0x17, 0x11, 0x0A);

    // --- Brass: rules, borders and highlights ----------------------------

    public static readonly Vector4 Brass = Rgb(0x8A, 0x6A, 0x3A);

    public static readonly Vector4 BrassBright = Rgb(0xC9, 0x9A, 0x48);

    public static readonly Vector4 BrassDim = Rgb(0x4A, 0x37, 0x18);

    public static readonly Vector4 Gold = Rgb(0xE8, 0xA3, 0x3D);

    /// <summary>A struck brass plate, for a pressed accent button.</summary>
    public static readonly Vector4 GoldBright = Rgb(0xF2, 0xBC, 0x62);

    /// <summary>
    /// Brass as a text colour on the dark chrome.
    ///
    /// <see cref="Brass"/> itself is a mid tone: too light to carry cream text and too dark to read as
    /// text against the panels. It is a surface and a border colour only; this is the readable sibling.
    /// </summary>
    public static readonly Vector4 BrassText = Rgb(0xA7, 0x81, 0x46);

    // --- Parchment: the page a beacon is written on ----------------------

    public static readonly Vector4 Parchment = Rgb(0xE9, 0xDC, 0xBE);

    public static readonly Vector4 ParchmentShade = Rgb(0xDD, 0xCD, 0xA8);

    public static readonly Vector4 ParchmentRule = Rgb(0xBF, 0xA8, 0x77);

    public static readonly Vector4 Ink = Rgb(0x3A, 0x2A, 0x14);

    public static readonly Vector4 InkSoft = Rgb(0x4A, 0x38, 0x23);

    public static readonly Vector4 InkFaint = Rgb(0x67, 0x55, 0x3D);

    // --- Text on the dark side -------------------------------------------

    public static readonly Vector4 Cream = Rgb(0xF0, 0xDF, 0xBC);

    public static readonly Vector4 CreamDim = Rgb(0xE4, 0xD4, 0xB2);

    public static readonly Vector4 Muted = Rgb(0x9C, 0x87, 0x63);

    /// <summary>
    /// The dimmest text allowed on the dark chrome. Solved against PanelRaised, the lightest of the
    /// dark surfaces, so it stays readable on a selected row as well as an unselected one.
    /// </summary>
    public static readonly Vector4 MutedDeep = Rgb(0x9A, 0x85, 0x61);

    // --- Fire -------------------------------------------------------------

    /// <summary>A beacon burning right now.</summary>
    public static readonly Vector4 Ember = Rgb(0xFF, 0x9A, 0x3C);

    /// <summary>A flame running low on time.</summary>
    public static readonly Vector4 EmberLow = Rgb(0xA8, 0x61, 0x1F);

    /// <summary>A beacon that is dark.</summary>
    public static readonly Vector4 Ash = Rgb(0x55, 0x45, 0x2C);

    // --- Accents ----------------------------------------------------------

    /// <summary>Sealing wax, used for the share code and destructive actions.</summary>
    public static readonly Vector4 Wax = Rgb(0x8C, 0x2F, 0x22);

    public static readonly Vector4 WaxBright = Rgb(0xB8, 0x45, 0x33);

    /// <summary>Wax as a text colour on the dark chrome, where the sealing red is far too dark.</summary>
    public static readonly Vector4 WaxText = Rgb(0xD6, 0x66, 0x56);

    /// <summary>The shadowed side of a pressed seal.</summary>
    public static readonly Vector4 WaxDeep = Rgb(0x5E, 0x1C, 0x13);

    /// <summary>The lit face of a pressed seal. Light enough to read as relief against the wax.</summary>
    public static readonly Vector4 WaxLight = Rgb(0xE0, 0x8A, 0x74);

    /// <summary>A connected server. Tuned for the dark chrome; unreadable on parchment.</summary>
    public static readonly Vector4 Verdigris = Rgb(0x7E, 0x94, 0x63);

    /// <summary>
    /// The same "all is well" green, dark enough to read as ink on paper.
    ///
    /// Two greens rather than one because the palette spans two very different grounds. Reusing the
    /// chrome green on parchment gives 2.4:1, which is worse than the problem it was meant to signal.
    /// </summary>
    public static readonly Vector4 VerdigrisInk = Rgb(0x35, 0x53, 0x25);

    private static int pushedColours;

    private static int pushedVars;

    /// <summary>
    /// Applies the Beacon styling to everything drawn until <see cref="Pop"/>.
    ///
    /// Counts its own pushes rather than trusting a fixed number, so that changing the palette later
    /// cannot leave ImGui's stacks unbalanced, which corrupts every window drawn afterwards, not just ours.
    /// </summary>
    public static void Push(bool enabled)
    {
        pushedColours = 0;
        pushedVars = 0;

        if (!enabled)
            return;

        Colour(ImGuiCol.WindowBg, Leather);
        Colour(ImGuiCol.ChildBg, Panel);
        Colour(ImGuiCol.PopupBg, LeatherLight);
        Colour(ImGuiCol.Border, Brass);
        Colour(ImGuiCol.BorderShadow, new Vector4(0, 0, 0, 0));

        Colour(ImGuiCol.Text, CreamDim);
        Colour(ImGuiCol.TextDisabled, MutedDeep);

        Colour(ImGuiCol.TitleBg, LeatherLight);
        Colour(ImGuiCol.TitleBgActive, LeatherLight);
        Colour(ImGuiCol.TitleBgCollapsed, Leather);

        Colour(ImGuiCol.FrameBg, Well);
        Colour(ImGuiCol.FrameBgHovered, PanelRaised);
        Colour(ImGuiCol.FrameBgActive, BrassDim);

        Colour(ImGuiCol.Button, PanelRaised);
        Colour(ImGuiCol.ButtonHovered, BrassDim);
        Colour(ImGuiCol.ButtonActive, Brass);

        Colour(ImGuiCol.Header, BrassDim);
        Colour(ImGuiCol.HeaderHovered, Brass);
        Colour(ImGuiCol.HeaderActive, Brass);

        Colour(ImGuiCol.Separator, BrassDim);
        Colour(ImGuiCol.SeparatorHovered, Brass);
        Colour(ImGuiCol.SeparatorActive, BrassBright);

        Colour(ImGuiCol.CheckMark, Gold);
        Colour(ImGuiCol.SliderGrab, Brass);
        Colour(ImGuiCol.SliderGrabActive, Gold);

        Colour(ImGuiCol.ScrollbarBg, Well);
        Colour(ImGuiCol.ScrollbarGrab, BrassDim);
        Colour(ImGuiCol.ScrollbarGrabHovered, Brass);
        Colour(ImGuiCol.ScrollbarGrabActive, BrassBright);

        Colour(ImGuiCol.Tab, Panel);
        Colour(ImGuiCol.TabHovered, BrassDim);
        Colour(ImGuiCol.TabActive, PanelRaised);

        Colour(ImGuiCol.TableHeaderBg, LeatherLight);
        Colour(ImGuiCol.TableBorderStrong, Brass);
        Colour(ImGuiCol.TableBorderLight, BrassDim);

        // Square corners throughout: an atlas page has corners, not rounded tabs.
        Var(ImGuiStyleVar.WindowRounding, 2f);
        Var(ImGuiStyleVar.ChildRounding, 1f);
        Var(ImGuiStyleVar.FrameRounding, 1f);
        Var(ImGuiStyleVar.PopupRounding, 2f);
        Var(ImGuiStyleVar.ScrollbarRounding, 1f);
        Var(ImGuiStyleVar.GrabRounding, 1f);
        Var(ImGuiStyleVar.TabRounding, 1f);
        Var(ImGuiStyleVar.WindowBorderSize, 1f);
        Var(ImGuiStyleVar.FrameBorderSize, 1f);
        Var(ImGuiStyleVar.ChildBorderSize, 1f);
        Var(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        Var(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f));
        Var(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 6f));
    }

    private static int pushedPageColours;

    /// <summary>
    /// Switches to the parchment page: light ground, dark ink, and light controls to match.
    ///
    /// The whole set has to move together. Pushing only the background and the text colour, which is
    /// what this used to do, leaves every button and input still painted in dark-chrome brass -- so a
    /// button inside the page drew near-black on near-black. Any surface change must carry its
    /// controls with it.
    /// </summary>
    public static void PushPage()
    {
        pushedPageColours = 0;

        PageColour(ImGuiCol.ChildBg, Parchment);
        PageColour(ImGuiCol.Text, Ink);
        PageColour(ImGuiCol.TextDisabled, InkFaint);
        PageColour(ImGuiCol.Border, ParchmentRule);

        PageColour(ImGuiCol.Button, ParchmentShade);
        PageColour(ImGuiCol.ButtonHovered, ParchmentRule);
        PageColour(ImGuiCol.ButtonActive, Brass);

        PageColour(ImGuiCol.FrameBg, ParchmentShade);
        PageColour(ImGuiCol.FrameBgHovered, ParchmentRule);
        PageColour(ImGuiCol.FrameBgActive, ParchmentRule);

        PageColour(ImGuiCol.Header, ParchmentRule);
        PageColour(ImGuiCol.HeaderHovered, ParchmentRule);
        PageColour(ImGuiCol.HeaderActive, Brass);

        PageColour(ImGuiCol.Separator, ParchmentRule);
        PageColour(ImGuiCol.CheckMark, Wax);
        PageColour(ImGuiCol.PopupBg, Parchment);
    }

    /// <summary>Removes exactly what <see cref="PushPage"/> applied.</summary>
    public static void PopPage()
    {
        if (pushedPageColours > 0)
        {
            ImGui.PopStyleColor(pushedPageColours);
            pushedPageColours = 0;
        }
    }

    private static void PageColour(ImGuiCol target, Vector4 colour)
    {
        ImGui.PushStyleColor(target, colour);
        pushedPageColours++;
    }

    /// <summary>Removes exactly what <see cref="Push"/> applied.</summary>
    public static void Pop()
    {
        if (pushedVars > 0)
        {
            ImGui.PopStyleVar(pushedVars);
            pushedVars = 0;
        }

        if (pushedColours > 0)
        {
            ImGui.PopStyleColor(pushedColours);
            pushedColours = 0;
        }
    }

    private static void Colour(ImGuiCol target, Vector4 colour)
    {
        ImGui.PushStyleColor(target, colour);
        pushedColours++;
    }

    private static void Var(ImGuiStyleVar target, float value)
    {
        ImGui.PushStyleVar(target, value);
        pushedVars++;
    }

    private static void Var(ImGuiStyleVar target, Vector2 value)
    {
        ImGui.PushStyleVar(target, value);
        pushedVars++;
    }

    private static Vector4 Rgb(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Same colour at a different opacity, for washes and disabled states.</summary>
    public static Vector4 Fade(this Vector4 colour, float alpha) =>
        new(colour.X, colour.Y, colour.Z, alpha);

    /// <summary>Packed ABGR, which is what the ImGui draw list wants.</summary>
    public static uint Packed(this Vector4 colour) => ImGui.ColorConvertFloat4ToU32(colour);

    /// <summary>
    /// The colour a flame should be drawn in, warning by fading towards the low ember colour as its
    /// time runs out. Dark beacons get ash.
    /// </summary>
    public static Vector4 FlameColour(Beacon.Shared.Beacons.BeaconFlame flame)
    {
        if (!flame.IsBurning)
            return Ash;

        var remaining = flame.Remaining;
        if (remaining is null)
            return Ember;

        // Below twenty minutes the flame visibly cools, so a traveller can tell at a glance
        // whether it is worth setting out.
        var minutes = remaining.Value.TotalMinutes;
        if (minutes >= 20)
            return Ember;

        var t = (float)Math.Clamp(minutes / 20d, 0d, 1d);
        return Vector4.Lerp(EmberLow, Ember, t);
    }
}
