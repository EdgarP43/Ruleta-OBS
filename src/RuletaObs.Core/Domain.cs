namespace RuletaObs.Core;

public enum SpinSource
{
    ChannelPoints,
    Bits,
    ManualTest
}

public enum SpinStatus
{
    Pending,
    Spinning,
    AwaitingConfirmation,
    Completed,
    Cancelled
}

public sealed class Prize
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Premio";
    public int Weight { get; set; } = 10;
    public string Color { get; set; } = "#7c3aed";
    public bool IsEnabled { get; set; } = true;
}

public sealed class BonusTier
{
    public int MinimumBits { get; set; }
    public int BonusSpins { get; set; }
}

public sealed class ConversionRules
{
    public int SpinsPerRedemption { get; set; } = 1;
    public int BitsPerSpin { get; set; } = 100;
    public List<BonusTier> BonusTiers { get; set; } = new();

    public int CalculateSpinsForBits(int bits)
    {
        if (bits <= 0 || BitsPerSpin <= 0) return 0;
        var spins = bits / BitsPerSpin;
        var bonus = BonusTiers
            .Where(t => bits >= t.MinimumBits)
            .OrderByDescending(t => t.MinimumBits)
            .Select(t => t.BonusSpins)
            .FirstOrDefault();
        return Math.Max(0, spins + Math.Max(0, bonus));
    }
}

public sealed class SpinEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventId { get; set; } = string.Empty;
    public SpinSource Source { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerDisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int Amount { get; set; }
    public string? RewardId { get; set; }
    public string? RewardTitle { get; set; }
    public string? RedemptionId { get; set; }
    public SpinStatus Status { get; set; } = SpinStatus.Pending;
    public string? PrizeName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SpunAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public bool TwitchSyncPending { get; set; }

    public void MarkSpun(string prizeName, DateTimeOffset now)
    {
        if (Status != SpinStatus.Pending)
            throw new InvalidOperationException($"No se puede girar una tirada en estado {Status}.");
        Status = SpinStatus.AwaitingConfirmation;
        PrizeName = prizeName;
        SpunAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != SpinStatus.AwaitingConfirmation)
            throw new InvalidOperationException("Solo se puede confirmar una tirada ya girada.");
        Status = SpinStatus.Completed;
        ClosedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is SpinStatus.Completed or SpinStatus.Cancelled)
            throw new InvalidOperationException("La tirada ya está cerrada.");
        Status = SpinStatus.Cancelled;
        ClosedAt = now;
    }
}

public sealed record QueueSummary(
    string ViewerId,
    string ViewerLogin,
    string ViewerDisplayName,
    string? AvatarUrl,
    int PendingSpins,
    int AwaitingConfirmation,
    SpinSource LastSource);

public sealed class WeightedPrizePicker
{
    private readonly Random _random;

    public WeightedPrizePicker(Random? random = null) => _random = random ?? Random.Shared;

    public Prize Pick(IReadOnlyList<Prize> prizes)
    {
        var enabled = prizes.Where(p => p.IsEnabled && p.Weight > 0).ToList();
        if (enabled.Count == 0) throw new InvalidOperationException("No hay premios activos con peso mayor a cero.");
        var total = enabled.Sum(p => p.Weight);
        var roll = _random.Next(total);
        foreach (var prize in enabled)
        {
            if (roll < prize.Weight) return prize;
            roll -= prize.Weight;
        }
        return enabled[^1];
    }
}
