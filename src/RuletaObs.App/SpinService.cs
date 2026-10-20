using RuletaObs.Core;

namespace RuletaObs.App;

public sealed record SpinResult(SpinEntry Entry, Prize Prize, List<Prize> Prizes);

public sealed class SpinService
{
    private readonly SpinRepository _repo;
    private readonly ConfigStore _config;
    private readonly EventBus _bus;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpinService(SpinRepository repo, ConfigStore config, EventBus bus)
    {
        _repo = repo;
        _config = config;
        _bus = bus;
    }

    public async Task<SpinResult?> TrySpinNextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await _repo.GetCurrentAwaitingAsync(cancellationToken) is not null) return null;
            var next = await _repo.GetNextPendingAsync(cancellationToken);
            if (next is null) return null;

            var enabled = _config.Current.Prizes.Where(p => p.IsEnabled && p.Weight > 0).ToList();
            var prize = new WeightedPrizePicker().Pick(enabled);
            next.MarkSpun(prize.Name, DateTimeOffset.UtcNow);
            await _repo.UpdateAsync(next, cancellationToken);

            var result = new SpinResult(next, prize, enabled);
            _bus.Publish("spin", result);
            _bus.Publish("queue", new { updatedAt = DateTimeOffset.UtcNow, summary = await _repo.GetQueueSummaryAsync(cancellationToken) });
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task TryAutoSpinAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Current.AutoSpinOnRedeem) return;
        try { await TrySpinNextAsync(cancellationToken); }
        catch { /* Si no hay premios válidos, la tirada queda pendiente para giro manual. */ }
    }
}
