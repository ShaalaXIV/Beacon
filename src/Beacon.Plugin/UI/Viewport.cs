using Dalamud.Bindings.ImGui;

namespace Beacon.UI;

/// <summary>
/// The id ImGui gave the game's main viewport, recorded once a frame.
///
/// Capturing the screen needs that id, it is a hash rather than zero, and it can only be read from
/// inside a draw callback. A capture is started from a button, a chat command or a background
/// continuation, so the value is remembered here rather than looked up at the point of use.
/// </summary>
public static class Viewport
{
    /// <summary>Zero until the first frame has been drawn.</summary>
    public static uint MainId { get; private set; }

    public static void Record() => MainId = ImGui.GetMainViewport().ID;
}
