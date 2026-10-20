using System.Text.Json;

namespace RuletaObs.Core;

public sealed class AppConfig
{
    public string TwitchClientId { get; set; } = string.Empty;
    public string? BroadcasterId { get; set; }
    public string? BroadcasterLogin { get; set; }
    public string? BroadcasterDisplayName { get; set; }
    public string? PointsRewardId { get; set; }
    public string? PointsRewardTitle { get; set; }
    public bool PointsRewardManageable { get; set; }
    public string OverlayKey { get; set; } = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
    public bool SoundEnabled { get; set; } = true;
    public bool AutoSpinOnRedeem { get; set; } = true;
    public string? CelebrationMediaFileName { get; set; }
    public string CelebrationMediaVersion { get; set; } = string.Empty;
    public string CelebrationText { get; set; } = "¡{usuario} canjeó una tirada!";
    public ConversionRules Rules { get; set; } = new();
    public List<Prize> Prizes { get; set; } = DefaultPrizes();

    public static List<Prize> DefaultPrizes() => new()
    {
        new Prize { Name = "Nada", Weight = 25, Color = "#475569" },
        new Prize { Name = "Premio chico", Weight = 35, Color = "#0ea5e9" },
        new Prize { Name = "Premio mediano", Weight = 25, Color = "#22c55e" },
        new Prize { Name = "Premio grande", Weight = 10, Color = "#f59e0b" },
        new Prize { Name = "Jackpot", Weight = 5, Color = "#ec4899" }
    };
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "config.json");
        Current = Load();
    }

    public AppConfig Current { get; private set; }

    private AppConfig Load()
    {
        if (!File.Exists(_path)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Current = config;
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(config, JsonOptions), cancellationToken);
            File.Move(temp, _path, true);
        }
        finally { _gate.Release(); }
    }
}
