using Compass.Server.Data;
using Compass.Server.Realtime;
using Compass.Shared.Beacons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Compass.Server.Services;

/// <summary>
/// Puts out flames whose time has run out.
///
/// The atlas lives or dies on whether "lit" means anything. Without this, every beacon anyone forgot
/// to snuff stays lit forever, people make the trip to an empty field, and they stop trusting the
/// green dot. Expiry is enforced here rather than only filtered on read so the state in the database
/// matches what players are told, and so the extinguish is pushed to everyone watching.
/// </summary>
public sealed class FlameSweeper(
    IServiceScopeFactory scopeFactory,
    BeaconHub hub,
    IOptions<CompassOptions> options,
    ILogger<FlameSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SweepInterval;
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromSeconds(30);

        using var timer = new PeriodicTimer(interval);

        // Sweep once at startup: the server may have been down while flames expired.
        await SweepSafelyAsync(stoppingToken);

        while (await SafeWaitAsync(timer, stoppingToken))
            await SweepSafelyAsync(stoppingToken);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SweepSafelyAsync(CancellationToken ct)
    {
        try
        {
            await SweepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A failed sweep must never kill the loop; the next tick will try again.
            logger.LogError(ex, "Flame sweep failed.");
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompassDbContext>();

        var now = DateTimeOffset.UtcNow;

        var expired = await db.Beacons
            .Where(b => b.IsLit && b.LitUntil != null && b.LitUntil <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        var ids = expired.Select(b => b.Id).ToList();

        // Close out the open history rows for these beacons in one query.
        var openLights = await db.Lights
            .Where(l => ids.Contains(l.BeaconId) && l.ExtinguishedAt == null)
            .ToListAsync(ct);

        foreach (var light in openLights)
        {
            light.ExtinguishedAt = now;
            light.Reason = ExtinguishReason.Expired;
        }

        foreach (var beacon in expired)
        {
            beacon.IsLit = false;
            beacon.LitUntil = null;
            beacon.LitAt = null;
            beacon.LitByName = null;
            beacon.LitByAccountId = null;
            beacon.LitNote = null;
        }

        await db.SaveChangesAsync(ct);

        foreach (var beacon in expired)
        {
            hub.Broadcast(new BeaconEvent
            {
                Kind = BeaconEventKind.Extinguished,
                BeaconId = beacon.Id,
                Flame = BeaconFlame.Dark,
                At = now,
            });
        }

        logger.LogInformation("Swept {Count} burnt-out flame(s).", expired.Count);
    }
}
