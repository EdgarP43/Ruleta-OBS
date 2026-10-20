using System.Collections.Concurrent;
using RuletaObs.Core;
using RuletaObs.Twitch;

namespace RuletaObs.App;

public sealed class AuthFlowState
{
    public DeviceCodeSession? Pending { get; set; }
    public string? LastError { get; set; }
    public bool IsPolling { get; set; }
}

public sealed class TwitchRuntimeState
{
    private readonly ConcurrentQueue<string> _logs = new();
    public string? LastLog { get; private set; }
    public bool EventSubConnected { get; set; }
    public int OverlayClients { get; set; }

    public void Log(string message)
    {
        LastLog = $"{DateTime.Now:HH:mm:ss} {message}";
        _logs.Enqueue(LastLog);
        while (_logs.Count > 50) _logs.TryDequeue(out _);
    }

    public IReadOnlyList<string> Logs => _logs.ToList();
}

public sealed class TwitchEventProcessor
{
    private readonly SpinRepository _repo;
    private readonly ConfigStore _config;
    private readonly EventBus _bus;
    private readonly TwitchHelixClient _helix;
    private readonly TwitchRuntimeState _state;
    private readonly SpinService _spin;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string? Avatar, string DisplayName, DateTimeOffset Expires)> _profileCache = new();

    public TwitchEventProcessor(SpinRepository repo, ConfigStore config, EventBus bus, TwitchHelixClient helix, TwitchRuntimeState state, SpinService spin)
    {
        _repo = repo;
        _config = config;
        _bus = bus;
        _helix = helix;
        _state = state;
        _spin = spin;
    }

    public async Task ProcessAsync(TwitchSpinEvent ev, CancellationToken cancellationToken = default)
    {
        var config = _config.Current;
        int spins;
        if (ev.Source == SpinSource.ChannelPoints)
        {
            if (string.IsNullOrWhiteSpace(config.PointsRewardId) || !string.Equals(config.PointsRewardId, ev.RewardId, StringComparison.OrdinalIgnoreCase))
            {
                _state.Log($"Canje ignorado: no es la recompensa asignada ({ev.RewardTitle ?? ev.RewardId}).");
                return;
            }
            spins = Math.Max(1, config.Rules.SpinsPerRedemption);
        }
        else
        {
            spins = config.Rules.CalculateSpinsForBits(ev.Amount);
            if (spins <= 0)
            {
                await _repo.TryAddSpinsAsync(ev.EventId, ev.Source, ev.ViewerId, ev.ViewerLogin, ev.ViewerDisplayName, null, ev.Amount, ev.RewardId, ev.RewardTitle, ev.RedemptionId, 0, cancellationToken);
                _state.Log($"Cheer de {ev.ViewerDisplayName} por {ev.Amount} bits: no alcanza para una tirada.");
                return;
            }
        }

        string? avatar = null;
        string displayName = ev.ViewerDisplayName;
        if (_profileCache.TryGetValue(ev.ViewerId, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            avatar = cached.Avatar;
            displayName = cached.DisplayName;
        }
        else
        {
            try
            {
                var user = await _helix.GetUserByIdAsync(ev.ViewerId, cancellationToken);
                avatar = user?.ProfileImageUrl;
                if (!string.IsNullOrWhiteSpace(user?.DisplayName)) displayName = user.DisplayName;
                _profileCache[ev.ViewerId] = (avatar, displayName, DateTimeOffset.UtcNow.AddMinutes(10));
            }
            catch { /* El avatar es opcional. */ }
        }

        var inserted = await _repo.TryAddSpinsAsync(ev.EventId, ev.Source, ev.ViewerId, ev.ViewerLogin, displayName, avatar,
            ev.Amount, ev.RewardId, ev.RewardTitle, ev.RedemptionId, spins, cancellationToken);
        if (!inserted)
        {
            _state.Log("Evento duplicado de Twitch ignorado.");
            return;
        }

        _state.Log($"{displayName} agregó {spins} tirada(s).");
        _bus.Publish("alert", new
        {
            viewer = displayName,
            avatar,
            source = ev.Source.ToString(),
            amount = ev.Amount,
            spins,
            message = spins == 1 ? "agregó 1 tirada a la cola" : $"agregó {spins} tiradas a la cola"
        });
        _bus.Publish("queue", new { updatedAt = DateTimeOffset.UtcNow });
        await _spin.TryAutoSpinAsync(cancellationToken);
    }
}

public sealed class TwitchWorker : BackgroundService
{
    private readonly ConfigStore _config;
    private readonly TokenManager _tokens;
    private readonly TwitchHelixClient _helix;
    private readonly TwitchEventProcessor _processor;
    private readonly TwitchRuntimeState _state;
    private readonly EventSubWebSocketClient _eventSub = new();

    public TwitchWorker(ConfigStore config, TokenManager tokens, TwitchHelixClient helix, TwitchEventProcessor processor, TwitchRuntimeState state)
    {
        _config = config;
        _tokens = tokens;
        _helix = helix;
        _processor = processor;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _config.Current;
            var token = await _tokens.GetValidTokenAsync(stoppingToken);
            if (token is null || string.IsNullOrWhiteSpace(config.BroadcasterId))
            {
                _state.EventSubConnected = false;
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                continue;
            }

            try
            {
                await _eventSub.RunAsync(async (sessionId, ct) =>
                {
                    var broadcasterId = _config.Current.BroadcasterId!;
                    await _helix.CreateEventSubWebSocketSubscriptionAsync(sessionId,
                        "channel.channel_points_custom_reward_redemption.add",
                        new { broadcaster_user_id = broadcasterId }, ct);
                    await _helix.CreateEventSubWebSocketSubscriptionAsync(sessionId,
                        "channel.cheer",
                        new { broadcaster_user_id = broadcasterId }, ct);
                    _state.EventSubConnected = true;
                }, async (ev, ct) => await _processor.ProcessAsync(ev, ct), msg => _state.Log(msg), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _state.EventSubConnected = false;
                _state.Log($"EventSub detenido: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
