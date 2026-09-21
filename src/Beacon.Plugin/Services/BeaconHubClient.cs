using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Beacon.Shared;
using Beacon.Shared.Beacons;

namespace Beacon.Services;

/// <summary>
/// Keeps a live connection to the server's beacon hub so the atlas reflects what is happening now.
///
/// Received events are queued rather than dispatched, and drained on the framework thread. Touching
/// plugin state or ImGui from a socket callback is how plugins crash the game; the queue is the
/// boundary between "somebody lit a beacon" and "the UI may safely change".
/// </summary>
public sealed class BeaconHubClient(Configuration config) : IDisposable
{
    /// <summary>Reconnect backoff, stepped through on repeated failures and reset on success.</summary>
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    private readonly ConcurrentQueue<BeaconEvent> inbox = new();

    private CancellationTokenSource? lifetime;

    private Task? loop;

    private int failures;

    /// <summary>True while a socket is open and listening.</summary>
    public bool Connected { get; private set; }

    /// <summary>When the connection last came up, for the settings display.</summary>
    public DateTime? ConnectedSince { get; private set; }

    /// <summary>Starts connecting, and keeps reconnecting until stopped.</summary>
    public void Start()
    {
        if (loop is not null)
            return;

        lifetime = new CancellationTokenSource();
        loop = Task.Run(() => RunAsync(lifetime.Token));
    }

    /// <summary>Stops listening and closes the socket.</summary>
    public void Stop()
    {
        try
        {
            lifetime?.Cancel();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Beacon: stopping the hub client threw.");
        }

        loop = null;
        Connected = false;
        ConnectedSince = null;
    }

    /// <summary>Restarts the connection, after the server address or key changes.</summary>
    public void Restart()
    {
        Stop();
        lifetime?.Dispose();
        lifetime = null;
        failures = 0;
        Start();
    }

    /// <summary>
    /// Takes everything received since the last call. Must be called on the framework thread;
    /// that is the entire point of the queue.
    /// </summary>
    public IEnumerable<BeaconEvent> Drain()
    {
        while (inbox.TryDequeue(out var evt))
            yield return evt;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!config.HasAccount)
            {
                // Nothing to authenticate with yet. Wait for an account rather than hammering a 401.
                await DelayAsync(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            try
            {
                await ListenAsync(ct);
                failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Svc.Log.Debug(ex, "Beacon: hub connection dropped.");
                failures++;
            }
            finally
            {
                Connected = false;
                ConnectedSince = null;
            }

            var wait = Backoff[Math.Min(failures, Backoff.Length - 1)];
            await DelayAsync(wait, ct);
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader(ApiRoutes.ApiKeyHeader, config.SecretKey);
        socket.Options.SetRequestHeader(ApiRoutes.ProtocolHeader, ApiRoutes.ProtocolVersion.ToString());

        // A server that has gone away without closing cleanly is otherwise indistinguishable from a
        // quiet one; the keep-alive is what turns that into a reconnect.
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await socket.ConnectAsync(HubUri(), ct);

        Connected = true;
        ConnectedSince = DateTime.UtcNow;
        Svc.Log.Information("Beacon: connected to the beacon hub.");

        var buffer = new byte[8192];
        var message = new StringBuilder();

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            // A frame can be split across receives; only parse once the message is complete.
            if (!result.EndOfMessage)
                continue;

            var payload = message.ToString();
            message.Clear();

            TryQueue(payload);
        }
    }

    private void TryQueue(string payload)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<BeaconEvent>(payload, BeaconJson.Options);
            if (evt is not null)
                inbox.Enqueue(evt);
        }
        catch (JsonException ex)
        {
            // A frame we cannot read is not a reason to drop the connection.
            Svc.Log.Debug(ex, "Beacon: could not read a hub event.");
        }
    }

    /// <summary>Turns the configured HTTP base URL into the matching WebSocket URL.</summary>
    private Uri HubUri()
    {
        var builder = new UriBuilder(config.NormalisedServerUrl + ApiRoutes.Hub)
        {
            Scheme = config.NormalisedServerUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
        };

        return builder.Uri;
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        Stop();
        lifetime?.Dispose();
        lifetime = null;
    }
}
