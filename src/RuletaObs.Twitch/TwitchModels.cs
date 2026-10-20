using RuletaObs.Core;

namespace RuletaObs.Twitch;

public sealed record TwitchUser(string Id, string Login, string DisplayName, string? ProfileImageUrl);
public sealed record TwitchReward(string Id, string Title, int Cost, bool IsEnabled, bool ManageableByThisApp);
public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);
public sealed record DeviceCodeSession(string DeviceCode, string UserCode, string VerificationUri, int IntervalSeconds, int ExpiresInSeconds);

public sealed record TwitchSpinEvent(
    string EventId,
    SpinSource Source,
    string ViewerId,
    string ViewerLogin,
    string ViewerDisplayName,
    int Amount,
    string? RewardId,
    string? RewardTitle,
    string? RedemptionId,
    DateTimeOffset OccurredAt);

public static class TwitchScopes
{
    public const string ManageRedemptions = "channel:manage:redemptions";
    public const string ReadRedemptions = "channel:read:redemptions";
    public const string BitsRead = "bits:read";
    public static readonly string[] Required = { ManageRedemptions, ReadRedemptions, BitsRead };
}
