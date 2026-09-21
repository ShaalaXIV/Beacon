using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Beacon.Shared.Beacons;

namespace Beacon.Services;

/// <summary>
/// Gets a picture of a place, either by capturing what the player is looking at or by letting them
/// choose a screenshot they have already framed and edited.
///
/// Both matter. The capture button is what makes posting a beacon a ten-second job; the file picker
/// is what lets somebody who cares about their screenshots post the good one.
/// </summary>
public sealed class ScreenshotService : IDisposable
{
    private readonly FileDialogManager dialogs = new();

    /// <summary>Set while a capture is running, so the button can show it is working.</summary>
    public bool Capturing { get; private set; }

    /// <summary>The last failure, for the editor to show inline.</summary>
    public string? LastError { get; private set; }

    /// <summary>Draws the file dialog, if one is open. Must be called every frame from the UI callback.</summary>
    public void Draw() => dialogs.Draw();

    /// <summary>
    /// Captures the game viewport as PNG bytes.
    ///
    /// Captured without transparency and after ImGui has rendered is deliberate: the player should get
    /// the scene as they see it, but not with every open plugin window baked into their beacon's photo.
    /// </summary>
    public async Task<byte[]?> CaptureAsync(CancellationToken ct = default)
    {
        if (Capturing)
            return null;

        Capturing = true;
        LastError = null;

        try
        {
            var args = new ImGuiViewportTextureArgs
            {
                // Grab the frame before ImGui draws, so the atlas window is not in the picture.
                TakeBeforeImGuiRender = true,
                KeepTransparency = false,
                AutoUpdate = false,
            };

            using var texture = await Svc.Textures.CreateFromImGuiViewportAsync(args, "Beacon capture", ct);

            var png = FindPngEncoder();
            if (png is null)
            {
                LastError = "This machine has no PNG encoder available.";
                return null;
            }

            using var buffer = new MemoryStream();
            await Svc.TextureReadback.SaveToStreamAsync(
                texture,
                png.Value,
                buffer,
                props: null,
                leaveWrapOpen: true,
                leaveStreamOpen: true,
                cancellationToken: ct);

            var bytes = buffer.ToArray();

            if (bytes.Length > BeaconLimits.MaxImageUploadBytes)
            {
                LastError = "That capture came out too large. Try a smaller window.";
                return null;
            }

            return bytes;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Beacon: viewport capture failed.");
            LastError = "Could not capture the screen. Choose a file instead.";
            return null;
        }
        finally
        {
            Capturing = false;
        }
    }

    /// <summary>
    /// Opens a file picker for an existing screenshot. The callback runs on the UI thread with the
    /// file's bytes and name, or is not called at all if the player cancels.
    /// </summary>
    public void PickFile(Action<byte[], string> onPicked, int maxFileBytes = BeaconLimits.MaxImageUploadBytes)
    {
        LastError = null;

        dialogs.OpenFileDialog(
            "Choose a screenshot",
            "Images{.png,.jpg,.jpeg,.webp,.bmp}",
            (confirmed, paths) =>
            {
                if (!confirmed || paths.Count == 0)
                    return;

                var path = paths[0];

                try
                {
                    var info = new FileInfo(path);

                    if (!info.Exists)
                    {
                        LastError = "That file is no longer there.";
                        return;
                    }

                    if (info.Length > maxFileBytes)
                    {
                        LastError = $"That file is larger than {maxFileBytes / (1024 * 1024)} MB.";
                        return;
                    }

                    onPicked(File.ReadAllBytes(path), Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning(ex, "Beacon: could not read the chosen screenshot.");
                    LastError = "Could not read that file.";
                }
            },
            selectionCountMax: 1,
            startPath: DefaultScreenshotFolder(),
            isModal: true);
    }

    /// <summary>
    /// Crops and resizes a decoded image on the GPU, then encodes the result as a PNG ready to upload.
    /// Keeping this on Dalamud's texture path avoids shipping a second image library with the plugin.
    /// </summary>
    public async Task<byte[]?> CropToPngAsync(
        IDalamudTextureWrap source,
        Vector2 uv0,
        Vector2 uv1,
        int width,
        int height,
        CancellationToken ct = default)
    {
        LastError = null;

        try
        {
            var args = new TextureModificationArgs
            {
                Uv0 = uv0,
                Uv1 = uv1,
                NewWidth = width,
                NewHeight = height,
            };

            using var cropped = await Svc.Textures.CreateFromExistingTextureAsync(
                source,
                args,
                leaveWrapOpen: true,
                debugName: "Beacon portrait crop",
                cancellationToken: ct);

            var png = FindPngEncoder();
            if (png is null)
            {
                LastError = "This machine has no PNG encoder available.";
                return null;
            }

            using var buffer = new MemoryStream();
            await Svc.TextureReadback.SaveToStreamAsync(
                cropped,
                png.Value,
                buffer,
                props: null,
                leaveWrapOpen: true,
                leaveStreamOpen: true,
                cancellationToken: ct);

            var bytes = buffer.ToArray();
            if (bytes.Length > BeaconLimits.MaxImageUploadBytes)
            {
                LastError = "The cropped portrait is still too large to upload.";
                return null;
            }

            return bytes;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Beacon: portrait crop failed.");
            LastError = "Could not crop that portrait. Try another image.";
            return null;
        }
    }

    /// <summary>
    /// The game's own screenshot folder, so the picker opens where the player's screenshots actually
    /// are rather than in some documents root.
    /// </summary>
    private static string? DefaultScreenshotFolder()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "My Games", "FINAL FANTASY XIV - A Realm Reborn", "screenshots");
            return Directory.Exists(folder) ? folder : null;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Beacon: could not resolve the screenshot folder.");
            return null;
        }
    }

    /// <summary>Finds the PNG encoder's container id, which the readback API needs to pick a format.</summary>
    private static Guid? FindPngEncoder()
    {
        try
        {
            var encoders = Svc.TextureReadback.GetSupportedImageEncoderInfos();
            foreach (var encoder in encoders)
            {
                if (encoder.MimeTypes.Any(m => m.Contains("png", StringComparison.OrdinalIgnoreCase))
                    || encoder.Extensions.Any(e => e.Contains("png", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder.ContainerGuid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Beacon: could not enumerate image encoders.");
            return null;
        }
    }

    public void Dispose() => dialogs.Reset();
}
