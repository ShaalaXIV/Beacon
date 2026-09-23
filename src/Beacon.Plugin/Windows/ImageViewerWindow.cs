using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Profiles;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>
/// One picture, or a whole gallery, at a size worth looking at.
///
/// A window rather than a modal popup. The card draws inside a child window, and an ImGui popup
/// opened from inside a child cannot be begun outside it -- the id stacks do not match, so the popup
/// silently never appears. That is exactly what happened to the gallery button. A window has no such
/// coupling, and it is resizable and movable, which is what somebody wanting a bigger picture
/// actually wants.
/// </summary>
public sealed class ImageViewerWindow : Window
{
    private readonly ImageCache images;

    /// <summary>The single image being shown, if the reader opened one picture.</summary>
    private Guid? single;

    /// <summary>The card whose gallery is being shown, if the reader opened the gallery.</summary>
    private ProfileDto? gallery;

    public ImageViewerWindow(ImageCache images)
        : base("Likeness###BeaconLikeness")
    {
        this.images = images;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 260),
            MaximumSize = new Vector2(3000, 2400),
        };
    }

    /// <summary>Opens one picture on its own.</summary>
    public void Show(Guid imageId, string title)
    {
        single = imageId;
        gallery = null;
        WindowName = $"{title}###BeaconLikeness";
        Size = FitToScreen();
        SizeCondition = ImGuiCond.Appearing;
        IsOpen = true;
    }

    /// <summary>Opens every picture on a card.</summary>
    public void ShowGallery(ProfileDto profile)
    {
        gallery = profile;
        single = null;
        WindowName = $"{profile.Identity.Name} — gallery###BeaconLikeness";
        Size = FitToScreen();
        SizeCondition = ImGuiCond.Appearing;
        IsOpen = true;
    }

    /// <summary>A generous default that still fits on the monitor it opens on.</summary>
    private static Vector2 FitToScreen()
    {
        var work = ImGui.GetMainViewport().WorkSize;
        return new Vector2(MathF.Min(work.X * 0.6f, 900f), MathF.Min(work.Y * 0.8f, 1000f));
    }

    public override void Draw()
    {
        if (single is { } imageId)
        {
            DrawSingle(imageId);
            return;
        }

        if (gallery is { } profile)
        {
            DrawGallery(profile);
            return;
        }

        IsOpen = false;
    }

    private void DrawSingle(Guid imageId)
    {
        var texture = images.Get(imageId, thumb: false);

        if (texture is null)
        {
            Ornament.Text(Theme.MutedDeep, "Loading...");
            return;
        }

        DrawFitted(texture.Handle, texture.Width, texture.Height, ImGui.GetContentRegionAvail());
    }

    private void DrawGallery(ProfileDto profile)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;

        foreach (var image in profile.Gallery)
        {
            Ornament.Text(Theme.BrassBright, ProfileLabels.Describe(image.Category));

            if (images.Get(image.ImageId, thumb: false) is { } texture)
            {
                var drawWidth = MathF.Min(width, texture.Width);
                var drawHeight = drawWidth * texture.Height / MathF.Max(1f, texture.Width);

                ImGui.PushID(image.ImageId.ToString());
                if (ImGui.ImageButton(texture.Handle, new Vector2(drawWidth, drawHeight)))
                    Show(image.ImageId, profile.Identity.Name);

                ImGui.PopID();
                Ornament.Tooltip("Click to see this one on its own.");
            }
            else
                Ornament.Text(Theme.MutedDeep, "Loading...");

            if (!string.IsNullOrWhiteSpace(image.Caption))
                Ornament.TextWrapped(Theme.Muted, image.Caption!);

            Ornament.FleuronDivider(Theme.BrassDim);
        }

        if (profile.Gallery.Count == 0)
            Ornament.Text(Theme.MutedDeep, "Nothing in this gallery yet.");

        ImGui.Dummy(new Vector2(0, 4f * scale));
    }

    /// <summary>
    /// Draws a picture as large as the space allows without distorting or cropping it, centred.
    /// A tall portrait and a wide scene both arrive whole.
    /// </summary>
    private static void DrawFitted(ImTextureID handle, float imageWidth, float imageHeight, Vector2 available)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return;

        available = new Vector2(MathF.Max(32f, available.X), MathF.Max(32f, available.Y));

        var fit = MathF.Min(available.X / imageWidth, available.Y / imageHeight);
        var size = new Vector2(imageWidth * fit, imageHeight * fit);

        var indentX = MathF.Max(0f, (available.X - size.X) / 2f);
        var indentY = MathF.Max(0f, (available.Y - size.Y) / 2f);

        if (indentY > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + indentY);

        if (indentX > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indentX);

        ImGui.Image(handle, size);
    }
}
