using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Beacons;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>
/// Points the way to the beacon you are heading for.
///
/// Teleporting drops you at an aetheryte, which can be several hundred yalms and a hill away from the
/// actual spot. Without something pointing, the last leg of every journey is squinting at the map.
/// Drawn as a marker when the place is in view, and as an arrow at the screen edge when it is not.
/// </summary>
public sealed class BeaconOverlay : Window
{
    private readonly Configuration config;

    private readonly AtlasService atlas;

    private readonly TravelService travel;

    private readonly LocationService location;

    /// <summary>Keeps the edge arrow off the very border, where it would be clipped.</summary>
    private const float EdgeMargin = 48f;

    private readonly Func<bool> atlasIsOpen;

    public BeaconOverlay(
        Configuration config,
        AtlasService atlas,
        TravelService travel,
        LocationService location,
        Func<bool> atlasIsOpen)
        : base("##BeaconOverlay",
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove)
    {
        this.config = config;
        this.atlas = atlas;
        this.travel = travel;
        this.location = location;
        this.atlasIsOpen = atlasIsOpen;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;
    }

    /// <summary>Only draws when there is somewhere to point at, in this zone, on this world.</summary>
    public override bool DrawConditions()
    {
        if (!config.ShowOverlay || !Svc.InWorld)
            return false;

        return Target is not null;
    }

    /// <summary>
    /// Stop marking a place once you are standing on it. Comfortably inside the range needed to light
    /// a beacon, so the marker never disappears while the button is still out of reach.
    /// </summary>
    private const float HideWithinYalms = 15f;

    /// <summary>
    /// What the overlay is pointing at.
    ///
    /// A journey always wins: if you asked to be taken somewhere, that is what gets marked. Otherwise
    /// it follows the atlas selection, but only while the atlas is actually open -- the selection is a
    /// browsing artefact, and it should not leave a marker hanging in the world after you close the
    /// window, or after you put a beacon out and are simply standing there.
    ///
    /// Either way the marker disappears once you are on top of the place, because pointing at the
    /// ground under your own feet is noise.
    /// </summary>
    private BeaconDto? Target
    {
        get
        {
            var candidate = travel.IsTravelling
                ? travel.Destination
                : atlasIsOpen() ? atlas.Selected : null;

            if (candidate is null)
                return null;

            var distance = location.DistanceTo(candidate);
            if (distance is null || distance <= HideWithinYalms)
                return null;

            return candidate;
        }
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        Position = viewport.Pos;
        Size = viewport.Size;
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        if (Target is not { } beacon)
            return;

        var distance = location.DistanceTo(beacon);
        if (distance is null)
            return;

        var world = new Vector3(beacon.Location.X, beacon.Location.Y, beacon.Location.Z);
        var draw = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;

        if (Svc.GameGui.WorldToScreen(world, out var screen))
        {
            DrawMarker(draw, screen, beacon, distance.Value, scale);
            return;
        }

        DrawEdgeArrow(draw, world, beacon, distance.Value, scale);
    }

    /// <summary>A flame and a distance, drawn over the spot itself.</summary>
    private static void DrawMarker(ImDrawListPtr draw, Vector2 screen, BeaconDto beacon, float distance, float scale)
    {
        var colour = beacon.Flame.IsBurning ? Theme.Ember : Theme.BrassBright;
        var packed = colour.Packed();

        var radius = 7f * scale;
        draw.AddCircle(screen, radius, packed, 20, 2f);
        draw.AddCircleFilled(screen, radius * 0.35f, packed, 12);

        var label = $"{beacon.Name}  {distance:0}y";
        var size = ImGui.CalcTextSize(label);
        var textPos = new Vector2(screen.X - (size.X / 2f), screen.Y + (radius * 1.8f));

        // A dark plate behind the label, so it stays readable over bright scenery.
        draw.AddRectFilled(
            new Vector2(textPos.X - (4f * scale), textPos.Y - (2f * scale)),
            new Vector2(textPos.X + size.X + (4f * scale), textPos.Y + size.Y + (2f * scale)),
            Theme.Leather.Fade(0.72f).Packed(),
            2f);

        draw.AddText(textPos, Theme.Cream.Packed(), label);
    }

    /// <summary>
    /// An arrow pinned to the screen edge, pointing the direction to turn.
    ///
    /// The projected point is used even when it is behind the camera, where the projection mirrors it;
    /// the direction is therefore taken from the world-space bearing instead, which is always right.
    /// </summary>
    private void DrawEdgeArrow(ImDrawListPtr draw, Vector3 world, BeaconDto beacon, float distance, float scale)
    {
        var player = location.CurrentPosition;
        if (player is null)
            return;

        var viewport = ImGui.GetMainViewport();
        var centre = viewport.Pos + (viewport.Size / 2f);

        // Bearing from the camera's point of view, derived by projecting a point just in front of the
        // player towards the target; this avoids needing the camera matrix directly.
        var toTarget = new Vector2(world.X - player.Value.X, world.Z - player.Value.Z);
        if (toTarget.LengthSquared() < 0.01f)
            return;

        toTarget = Vector2.Normalize(toTarget);

        // Rotate into screen space using the player's facing, so "up" on screen is "ahead".
        var facing = Svc.Objects.LocalPlayer?.Rotation ?? 0f;
        var (sin, cos) = MathF.SinCos(facing);

        var forward = new Vector2(-sin, -cos);
        var right = new Vector2(-cos, sin);

        var screenDirection = new Vector2(
            Vector2.Dot(toTarget, right),
            -Vector2.Dot(toTarget, forward));

        if (screenDirection.LengthSquared() < 0.0001f)
            return;

        screenDirection = Vector2.Normalize(screenDirection);

        var radius = (Math.Min(viewport.Size.X, viewport.Size.Y) / 2f) - (EdgeMargin * scale);
        var tip = centre + (screenDirection * radius);

        var colour = (beacon.Flame.IsBurning ? Theme.Ember : Theme.BrassBright).Packed();
        var size = 11f * scale;

        var perpendicular = new Vector2(-screenDirection.Y, screenDirection.X);
        draw.AddTriangleFilled(
            tip + (screenDirection * size),
            tip - (screenDirection * size * 0.4f) + (perpendicular * size * 0.7f),
            tip - (screenDirection * size * 0.4f) - (perpendicular * size * 0.7f),
            colour);

        var label = $"{beacon.Name}  {distance:0}y";
        var textSize = ImGui.CalcTextSize(label);
        var textPos = tip - (screenDirection * size * 2.6f) - (textSize / 2f);

        draw.AddRectFilled(
            new Vector2(textPos.X - (4f * scale), textPos.Y - (2f * scale)),
            new Vector2(textPos.X + textSize.X + (4f * scale), textPos.Y + textSize.Y + (2f * scale)),
            Theme.Leather.Fade(0.72f).Packed(),
            2f);

        draw.AddText(textPos, Theme.Cream.Packed(), label);
    }
}
