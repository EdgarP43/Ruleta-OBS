using RuletaObs.Core;
using Xunit;

namespace RuletaObs.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RuletaObsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Duplicate_event_never_creates_duplicate_spins()
    {
        var repo = new SpinRepository(_dir);
        Assert.True(await repo.TryAddSpinsAsync("evt-1", SpinSource.Bits, "u1", "ana", "Ana", null, 200, null, null, null, 2));
        Assert.False(await repo.TryAddSpinsAsync("evt-1", SpinSource.Bits, "u1", "ana", "Ana", null, 200, null, null, null, 2));
        var pending = await repo.ListAsync(SpinStatus.Pending);
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task Queue_groups_pending_spins_by_viewer()
    {
        var repo = new SpinRepository(_dir);
        await repo.TryAddSpinsAsync("evt-2", SpinSource.ChannelPoints, "u1", "ana", "Ana", null, 100, "r1", "Ruleta", "red1", 3);
        var next = await repo.GetNextPendingAsync();
        Assert.NotNull(next);
        next!.MarkSpun("Premio", DateTimeOffset.UtcNow);
        await repo.UpdateAsync(next);
        var summary = Assert.Single(await repo.GetQueueSummaryAsync());
        Assert.Equal(2, summary.PendingSpins);
        Assert.Equal(1, summary.AwaitingConfirmation);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
