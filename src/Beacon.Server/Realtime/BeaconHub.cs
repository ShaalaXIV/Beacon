using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Beacon.Shared;
using Beacon.Shared.Beacons;

namespace Beacon.Server.Realtime;

/// <summary>
/// Fans beacon events out to every connected plugin.
///
/// Each subscriber owns a bounded channel and its own writer loop, so a client on a bad connection
/// cannot stall the broadcaster or grow the server's memory: once its queue is full, its oldest
/// pending events are dropped. Losing a stale flame update for one slow client is fine. Blocking
/// every other client behind it, or running out of memory, is not.
/// </summary>
public sealed class BeaconHub(ILogger<BeaconHub> logger)
{
    /// <summary>
    /// Events buffered per client. Deep enough to absorb a burst of lights, shallow enough that a
    /// client that has silently gone away cannot hold much.
    /// </summary>
    private const int PerClientQueueDepth = 64;

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, Subscriber> subscribers = new();

    /// <summary>How many plugins are currently listening. Surfaced on the health endpoint.</summary>
    public int SubscriberCount => subscribers.Count;

    /// <summary>
    /// Runs a connected socket until it closes or the server shuts down.
    /// Owns the socket for its lifetime; the caller must not use it afterwards.
    /// </summary>
    public async Task HandleAsync(WebSocket socket, Guid? accountId, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var subscriber = new Subscriber(id, socket, accountId);
        subscribers[id] = subscriber;

        logger.LogDebug("Hub: subscriber {Id} connected ({Count} total).", id, subscribers.Count);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // The read loop exists to notice the client going away; the write loop does the real work.
            var writing = WriteLoopAsync(subscriber, linked.Token);
            var reading = ReadLoopAsync(subscriber, linked.Token);

            await Task.WhenAny(writing, reading);
            await linked.CancelAsync();

            // Let the other half unwind so the socket is never disposed mid-send.
            await Task.WhenAll(
                Swallow(writing),
                Swallow(reading));
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            subscriber.Events.Writer.TryComplete();
            logger.LogDebug("Hub: subscriber {Id} disconnected ({Count} remain).", id, subscribers.Count);

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Hub: closing subscriber {Id} failed.", id);
                }
            }
        }
    }

    /// <summary>
    /// Queues an event for every subscriber. Never blocks and never throws, so callers can broadcast
    /// from inside a request without a realtime problem turning into a failed write.
    /// </summary>
    public void Broadcast(BeaconEvent evt)
    {
        foreach (var subscriber in subscribers.Values)
        {
            if (subscriber.Events.Writer.TryWrite(evt))
                continue;

            // DropOldest means this should not happen, but a completed channel returns false too.
            logger.LogDebug("Hub: dropped {Kind} for subscriber {Id}.", evt.Kind, subscriber.Id);
        }
    }

    private async Task WriteLoopAsync(Subscriber subscriber, CancellationToken ct)
    {
        await foreach (var evt in subscriber.Events.Reader.ReadAllAsync(ct))
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(evt, BeaconJson.Options);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);

            await subscriber.Socket.SendAsync(
                payload,
                WebSocketMessageType.Text,
                endOfMessage: true,
                timeout.Token);
        }
    }

    private static async Task ReadLoopAsync(Subscriber subscriber, CancellationToken ct)
    {
        // Clients are not expected to say anything. Anything they do send is drained and ignored;
        // the point of this loop is to observe the close handshake and unblock the connection.
        var buffer = new byte[512];
        while (!ct.IsCancellationRequested)
        {
            var result = await subscriber.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return;
        }
    }

    private async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected: this is how the loops are stopped.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Hub: socket ended abruptly.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hub: unexpected subscriber failure.");
        }
    }

    private sealed class Subscriber(Guid id, WebSocket socket, Guid? accountId)
    {
        public Guid Id { get; } = id;

        public WebSocket Socket { get; } = socket;

        /// <summary>Set when the listener authenticated. Reserved for per-account filtering later.</summary>
        public Guid? AccountId { get; } = accountId;

        public Channel<BeaconEvent> Events { get; } =
            Channel.CreateBounded<BeaconEvent>(new BoundedChannelOptions(PerClientQueueDepth)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }
}
