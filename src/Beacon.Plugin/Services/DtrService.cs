using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace Beacon.Services;

/// <summary>
/// The server info bar entry: how many beacons are burning, at a glance, without opening anything.
///
/// Updated only when the text actually changes. The info bar redraws on every frame it is touched,
/// and a plugin that writes to it unconditionally is a plugin that shows up in frame-time profiles.
/// </summary>
public sealed class DtrService(Configuration config, AtlasService atlas, TravelService travel) : IDisposable
{
    private IDtrBarEntry? entry;

    private string lastText = string.Empty;

    /// <summary>Raised when the player clicks the entry, so the plugin can open the atlas.</summary>
    public event Action? Clicked;

    public void Start()
    {
        try
        {
            entry = Svc.DtrBar.Get("Beacon");
            entry.OnClick = _ => Clicked?.Invoke();
        }
        catch (Exception ex)
        {
            // Not being in the info bar is a cosmetic loss, never a reason to fail loading.
            Svc.Log.Warning(ex, "Beacon: could not create the server info bar entry.");
        }
    }

    public void Tick()
    {
        if (entry is null)
            return;

        if (!config.ShowDtrEntry)
        {
            if (entry.Shown)
                entry.Shown = false;

            return;
        }

        var text = BuildText();

        if (!entry.Shown)
            entry.Shown = true;

        if (text == lastText)
            return;

        lastText = text;
        entry.Text = new SeString(new TextPayload(text));
        entry.Tooltip = new SeString(new TextPayload("Beacon - click to open the atlas."));
    }

    private string BuildText()
    {
        // While travelling, the info bar is more useful showing the journey than a count.
        if (travel.IsTravelling)
            return $" {travel.Stage}";

        var lit = atlas.LitCount;
        return lit switch
        {
            0 => " Beacon",
            1 => " 1 lit",
            _ => $" {lit} lit",
        };
    }

    public void Dispose()
    {
        try
        {
            entry?.Remove();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Beacon: could not remove the info bar entry.");
        }

        entry = null;
    }
}
