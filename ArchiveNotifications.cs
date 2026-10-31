using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DontBlink.Services;

public record ArchiveNotice(string Stage, string Key, string Camera, DateTime TimestampUtc, string Message);
public sealed class ArchiveNotifications
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    public Subscription Subscribe()
    {
        var subscription = new Subscription(this);
        _subscriptions[subscription.Id] = subscription;
        return subscription;
    }
    public async Task PublishAsync(ArchiveNotice notice, CancellationToken ct)
    {
        var pending = new List<Task>();
        foreach (var client in _subscriptions.Values)
        {
            var delivery = new Delivery(notice);
            if (client.Queue.Writer.TryWrite(delivery)) pending.Add(delivery.Sent.Task);
        }
        if (pending.Count == 0) return;
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch (TimeoutException) { throw new IOException("Client notification could not be delivered before archiving. USB original kept; the next pass will retry."); }
    }
    public sealed record Delivery(ArchiveNotice Notice)
    {
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Subscription(ArchiveNotifications owner) : IDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal Channel<Delivery> Queue { get; } = Channel.CreateUnbounded<Delivery>();
        public ChannelReader<Delivery> Reader => Queue.Reader;
        public void Dispose()
        {
            owner._subscriptions.TryRemove(Id, out _); Queue.Writer.TryComplete();
            while (Queue.Reader.TryRead(out var delivery)) delivery.Sent.TrySetException(new IOException("Client disconnected before notification delivery."));
        }
    }
}
