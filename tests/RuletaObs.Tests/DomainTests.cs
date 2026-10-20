using RuletaObs.Core;
using Xunit;

namespace RuletaObs.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Spin_must_be_spun_before_completion()
    {
        var entry = new SpinEntry();
        Assert.Throws<InvalidOperationException>(() => entry.Complete(DateTimeOffset.UtcNow));
        entry.MarkSpun("Premio", DateTimeOffset.UtcNow);
        entry.Complete(DateTimeOffset.UtcNow);
        Assert.Equal(SpinStatus.Completed, entry.Status);
        Assert.Throws<InvalidOperationException>(() => entry.Complete(DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(250, 2)]
    public void Bits_conversion_uses_floor(int bits, int expected)
    {
        var rules = new ConversionRules { BitsPerSpin = 100 };
        Assert.Equal(expected, rules.CalculateSpinsForBits(bits));
    }

    [Fact]
    public void Bits_bonus_applies_highest_tier_once()
    {
        var rules = new ConversionRules
        {
            BitsPerSpin = 100,
            BonusTiers = new() { new BonusTier { MinimumBits = 500, BonusSpins = 1 }, new BonusTier { MinimumBits = 1000, BonusSpins = 3 } }
        };
        Assert.Equal(13, rules.CalculateSpinsForBits(1000));
    }

    [Fact]
    public void Picker_respects_enabled_weights()
    {
        var prizes = new List<Prize>
        {
            new() { Name = "Off", Weight = 100, IsEnabled = false },
            new() { Name = "Only", Weight = 1, IsEnabled = true }
        };
        var picker = new WeightedPrizePicker(new Random(1));
        Assert.Equal("Only", picker.Pick(prizes).Name);
    }
}
