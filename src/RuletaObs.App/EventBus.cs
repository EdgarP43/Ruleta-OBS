using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace RuletaObs.App;

public sealed class EventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int SubscriberCount => _subscribers.Count;

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(string eventName, object data)
    {
        var payload = $"event: {eventName}\ndata: {JsonSerializer.Serialize(data, JsonOptions)}\n\n";
        foreach (var subscriber in _subscribers.Values)
            subscriber.Writer.TryWrite(payload);
    }
}
