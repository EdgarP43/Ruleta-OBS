using RuletaObs.Core;
using RuletaObs.Twitch;
using Xunit;

namespace RuletaObs.Tests;

public sealed class EventSubParserTests
{
    [Fact]
    public void Parses_channel_points_redemption()
    {
        const string json = """{"metadata":{"message_id":"msg-1","message_type":"notification"},"payload":{"subscription":{"type":"channel.channel_points_custom_reward_redemption.add"},"event":{"id":"red-1","user_id":"10","user_login":"ana","user_name":"Ana","redeemed_at":"2026-10-19T00:00:00Z","reward":{"id":"reward-1","title":"Ruleta","cost":1000}}}}""";
        var ev = EventSubParser.ParseNotification(json, out var id, out _);
        Assert.Equal("msg-1", id);
        Assert.NotNull(ev);
        Assert.Equal(SpinSource.ChannelPoints, ev!.Source);
        Assert.Equal("reward-1", ev.RewardId);
        Assert.Equal("red-1", ev.RedemptionId);
    }

    [Fact]
    public void Parses_cheer_bits()
    {
        const string json = """{"metadata":{"message_id":"msg-2","message_type":"notification"},"payload":{"subscription":{"type":"channel.cheer"},"event":{"user_id":"11","user_login":"luis","user_name":"Luis","is_anonymous":false,"bits":250}}}""";
        var ev = EventSubParser.ParseNotification(json, out _, out _);
        Assert.NotNull(ev);
        Assert.Equal(SpinSource.Bits, ev!.Source);
        Assert.Equal(250, ev.Amount);
    }
}
