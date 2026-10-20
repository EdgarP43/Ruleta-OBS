using System.Text.Json;
using RuletaObs.Core;

namespace RuletaObs.Twitch;

public static class EventSubParser
{
    public static TwitchSpinEvent? ParseNotification(string json, out string messageId, out string subscriptionType)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        messageId = root.GetProperty("metadata").GetProperty("message_id").GetString() ?? Guid.NewGuid().ToString("N");
        subscriptionType = root.GetProperty("metadata").GetProperty("message_type").GetString() ?? string.Empty;
        if (subscriptionType != "notification") return null;

        var subscription = root.GetProperty("payload").GetProperty("subscription");
        var type = subscription.GetProperty("type").GetString();
        var ev = root.GetProperty("payload").GetProperty("event");
        var occurred = ev.TryGetProperty("redeemed_at", out var ts) && ts.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(ts.GetString()!)
            : DateTimeOffset.UtcNow;

        if (type == "channel.channel_points_custom_reward_redemption.add")
        {
            var reward = ev.GetProperty("reward");
            return new TwitchSpinEvent(
                messageId,
                SpinSource.ChannelPoints,
                ev.GetProperty("user_id").GetString() ?? "",
                ev.GetProperty("user_login").GetString() ?? "",
                ev.GetProperty("user_name").GetString() ?? "",
                reward.TryGetProperty("cost", out var cost) ? cost.GetInt32() : 0,
                reward.GetProperty("id").GetString(),
                reward.TryGetProperty("title", out var title) ? title.GetString() : null,
                ev.GetProperty("id").GetString(),
                occurred);
        }

        if (type == "channel.cheer")
        {
            var anonymous = ev.TryGetProperty("is_anonymous", out var anon) && anon.GetBoolean();
            return new TwitchSpinEvent(
                messageId,
                SpinSource.Bits,
                anonymous ? "anonymous" : ev.GetProperty("user_id").GetString() ?? "",
                anonymous ? "anonimo" : ev.GetProperty("user_login").GetString() ?? "",
                anonymous ? "Anónimo" : ev.GetProperty("user_name").GetString() ?? "",
                ev.GetProperty("bits").GetInt32(),
                null,
                null,
                null,
                DateTimeOffset.UtcNow);
        }

        return null;
    }
}
