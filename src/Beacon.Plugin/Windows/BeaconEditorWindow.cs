using System.Numerics;
using Beacon.Services;
using Beacon.Shared.Beacons;
using Beacon.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Beacon.Windows;

/// <summary>
/// Raises a new beacon, or edits one you keep.
///
/// The location is captured rather than typed. A beacon is defined by a spot somebody chose by
/// standing on it, and no coordinate box would ever be as accurate, or as quick.
/// </summary>
public sealed class BeaconEditorWindow : Window
{
    private readonly Configuration config;

    private readonly AtlasService atlas;

    private readonly LocationService location;

    private readonly ScreenshotService screenshots;

    private readonly ImageCache images;

    private readonly NotificationService notifications;

    private Guid? editingId;

    private string name = string.Empty;

    private string description = string.Empty;

    private BeaconKind kind = BeaconKind.Camp;

    private string tagsText = string.Empty;

    private BeaconVisibility visibility = BeaconVisibility.Public;

    private bool allowPublicLighting;

    private BeaconLocation? capturedLocation;

    private BeaconRealm? capturedRealm;

    private byte[]? pendingImage;

    private string? pendingImageName;

    private bool clearExistingImage;

    private Guid? existingImageId;

    private string? validationError;

    private bool submitting;

    public BeaconEditorWindow(
        Configuration config,
        AtlasService atlas,
        LocationService location,
        ScreenshotService screenshots,
        ImageCache images,
        NotificationService notifications)
        : base("Raise a beacon###BeaconEditor")
    {
        this.config = config;
        this.atlas = atlas;
        this.location = location;
        this.screenshots = screenshots;
        this.images = images;
        this.notifications = notifications;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 520),
            MaximumSize = new Vector2(900, 1200),
        };

        Size = new Vector2(520, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.Push(config.UseBeaconTheme);

    public override void PostDraw() => Theme.Pop();

    /// <summary>Opens the editor for a new beacon at the player's feet, or to edit an existing one.</summary>
    public void Open(BeaconDto? beacon)
    {
        validationError = null;
        submitting = false;
        pendingImage = null;
        pendingImageName = null;
        clearExistingImage = false;

        if (beacon is null)
        {
            editingId = null;
            name = string.Empty;
            description = string.Empty;
            kind = BeaconKind.Camp;
            tagsText = string.Empty;
            visibility = BeaconVisibility.Public;
            allowPublicLighting = false;
            existingImageId = null;
            WindowName = "Raise a beacon###BeaconEditor";

            CaptureHere();
        }
        else
        {
            editingId = beacon.Id;
            name = beacon.Name;
            description = beacon.Description;
            kind = beacon.Kind;
            tagsText = string.Join(", ", beacon.Tags);
            visibility = beacon.Visibility;
            allowPublicLighting = beacon.AllowPublicLighting;
            existingImageId = beacon.ImageId;

            // Keep the beacon where it is unless the keeper explicitly moves it.
            capturedLocation = beacon.Location;
            capturedRealm = beacon.Realm;

            WindowName = $"Edit {beacon.Name}###BeaconEditor";
        }

        IsOpen = true;
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        DrawLocation(scale);
        Ornament.FleuronDivider(Theme.BrassDim);

        DrawFields(scale);
        Ornament.FleuronDivider(Theme.BrassDim);

        DrawScreenshotSection(scale);
        Ornament.FleuronDivider(Theme.BrassDim);

        DrawVisibility(scale);

        ImGui.Spacing();
        DrawSubmit(scale);
    }

    // --- Where -----------------------------------------------------------

    private void DrawLocation(float scale)
    {
        Ornament.Text(Theme.BrassBright, "Where");

        if (capturedLocation is { } captured && capturedRealm is { } realm)
        {
            Ornament.Text(Theme.CreamDim, $"{captured.ZoneName}  ·  {realm}");
            Ornament.Text(Theme.Muted, captured.MapCoordinateText);

            if (!string.IsNullOrWhiteSpace(captured.NearestAetheryteName))
                Ornament.Text(Theme.MutedDeep, $"Travellers will arrive at {captured.NearestAetheryteName}.");
            else
                Ornament.Text(Theme.Wax, "No aetheryte found for this zone. Others will not be able to teleport here.");
        }
        else
        {
            Ornament.TextWrapped(Theme.Wax, "No location captured. Stand where the beacon belongs and capture it.");
        }

        if (ImGui.Button(editingId is null ? "Capture where I stand" : "Move it to where I stand"))
            CaptureHere();

        if (!Svc.InWorld)
        {
            ImGui.SameLine();
            Ornament.Text(Theme.MutedDeep, "(you need to be in the world)");
        }
    }

    private void CaptureHere()
    {
        var here = location.CaptureHere();
        var realm = location.CaptureRealm();

        if (here is null || realm is null)
        {
            validationError = "Could not read your position. Wait until you have finished loading in.";
            return;
        }

        capturedLocation = here;
        capturedRealm = realm;
        validationError = null;
    }

    // --- What ------------------------------------------------------------

    private void DrawFields(float scale)
    {
        Ornament.Text(Theme.BrassBright, "What it is");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##name", "The Bramblewood Camp", ref name, BeaconLimits.NameMaxLength);

        ImGui.SetNextItemWidth(200f * scale);
        if (ImGui.BeginCombo("##kind", BeaconLabels.Describe(kind)))
        {
            foreach (var option in Enum.GetValues<BeaconKind>())
            {
                if (ImGui.Selectable(BeaconLabels.Describe(option), kind == option))
                    kind = option;

                Ornament.Tooltip(BeaconLabels.Hint(option));
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Ornament.Text(Theme.MutedDeep, BeaconLabels.Hint(kind));

        ImGui.Spacing();
        Ornament.PageLabel("What happens here, and who is welcome");

        ImGui.InputTextMultiline(
            "##description",
            ref description,
            BeaconLimits.DescriptionMaxLength,
            new Vector2(-1, 90f * scale));

        var remaining = BeaconLimits.DescriptionMaxLength - description.Length;
        Ornament.Text(remaining < 100 ? Theme.Wax : Theme.MutedDeep, $"{remaining} characters left");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##tags", "tags, comma separated: tavern, drop-in, lore-friendly", ref tagsText, 200);

        var tags = ParseTags(tagsText);
        if (tags.Count > BeaconLimits.MaxTags)
            Ornament.Text(Theme.Wax, $"Only the first {BeaconLimits.MaxTags} tags will be kept.");
    }

    // --- Likeness ---------------------------------------------------------

    private void DrawScreenshotSection(float scale)
    {
        Ornament.Text(Theme.BrassBright, "Its likeness");

        if (ImGui.Button(screenshots.Capturing ? "Capturing..." : "Capture the view"))
            _ = CaptureScreenshotAsync();

        ImGui.SameLine();

        if (ImGui.Button("Choose a file"))
        {
            screenshots.PickFile((bytes, fileName) =>
            {
                pendingImage = bytes;
                pendingImageName = fileName;
                clearExistingImage = false;
            });
        }

        var hasSomething = pendingImage is not null || (existingImageId is not null && !clearExistingImage);

        if (hasSomething)
        {
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                pendingImage = null;
                pendingImageName = null;
                clearExistingImage = existingImageId is not null;
            }
        }

        if (screenshots.LastError is { } error)
            Ornament.TextWrapped(Theme.Wax, error);

        if (pendingImage is not null)
        {
            Ornament.Text(Theme.Verdigris,
                $"Ready to upload: {pendingImageName} ({pendingImage.Length / 1024} KB)");
        }
        else if (existingImageId is { } imageId && !clearExistingImage)
        {
            var texture = images.Get(imageId, thumb: true);
            if (texture is not null)
            {
                var width = Math.Min(240f * scale, ImGui.GetContentRegionAvail().X);
                var height = width * texture.Height / Math.Max(1f, texture.Width);
                ImGui.Image(texture.Handle, new Vector2(width, height));
            }
        }
        else
        {
            Ornament.TextWrapped(Theme.MutedDeep,
                "A picture is what makes a stranger decide to make the trip. Worth the ten seconds.");
        }
    }

    private async Task CaptureScreenshotAsync()
    {
        var bytes = await screenshots.CaptureAsync();
        if (bytes is null)
            return;

        pendingImage = bytes;
        pendingImageName = "capture.png";
        clearExistingImage = false;
    }

    // --- Who can see it ---------------------------------------------------

    private void DrawVisibility(float scale)
    {
        Ornament.Text(Theme.BrassBright, "Who may see it");

        ImGui.SetNextItemWidth(160f * scale);
        if (ImGui.BeginCombo("##visibility", BeaconLabels.Describe(visibility)))
        {
            foreach (var option in Enum.GetValues<BeaconVisibility>())
            {
                if (ImGui.Selectable(BeaconLabels.Describe(option), visibility == option))
                    visibility = option;

                Ornament.Tooltip(BeaconLabels.Hint(option));
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        Ornament.Text(Theme.MutedDeep, BeaconLabels.Hint(visibility));

        ImGui.Checkbox("Anyone standing here may light it", ref allowPublicLighting);

        Ornament.Tooltip(
            "Leave this off and only you can light the beacon.\n"
            + "Turn it on to declare the place a commons: anyone who is there\n"
            + "can announce that it is alive, whether or not you are online.");
    }

    // --- Submit -----------------------------------------------------------

    private void DrawSubmit(float scale)
    {
        if (validationError is { } error)
            Ornament.TextWrapped(Theme.Wax, error);

        var buttonSize = new Vector2(150f * scale, 28f * scale);

        if (Ornament.AccentButton(editingId is null ? "Raise it" : "Save changes", buttonSize, !submitting))
            Submit();

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(90f * scale, 28f * scale)))
            IsOpen = false;
    }

    private void Submit()
    {
        var trimmedName = name.Trim();

        if (trimmedName.Length < BeaconLimits.NameMinLength)
        {
            validationError = $"Give it a name of at least {BeaconLimits.NameMinLength} characters.";
            return;
        }

        if (trimmedName.Length > BeaconLimits.NameMaxLength)
        {
            validationError = $"That name is longer than {BeaconLimits.NameMaxLength} characters.";
            return;
        }

        if (capturedLocation is not { } captured || capturedRealm is not { } realm)
        {
            validationError = "Capture a location first.";
            return;
        }

        if (captured.TerritoryId == 0)
        {
            validationError = "That location has no zone. Capture it again once you have loaded in.";
            return;
        }

        validationError = null;
        submitting = true;

        var tags = ParseTags(tagsText);

        if (editingId is { } id)
        {
            atlas.Update(
                id,
                new UpdateBeaconRequest
                {
                    Name = trimmedName,
                    Description = description.Trim(),
                    Kind = kind,
                    Location = captured,
                    Realm = realm,
                    Tags = tags,
                    Visibility = visibility,
                    AllowPublicLighting = allowPublicLighting,
                    ClearImage = clearExistingImage ? true : null,
                },
                pendingImage,
                pendingImageName);

            notifications.Toast($"{trimmedName} updated.", null);
            submitting = false;
            IsOpen = false;
            return;
        }

        atlas.Create(
            new CreateBeaconRequest
            {
                Name = trimmedName,
                Description = description.Trim(),
                Kind = kind,
                Location = captured,
                Realm = realm,
                Tags = tags,
                Visibility = visibility,
                AllowPublicLighting = allowPublicLighting,
            },
            pendingImage,
            pendingImageName,
            _ =>
            {
                submitting = false;
                IsOpen = false;
            });
    }

    private static List<string> ParseTags(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
