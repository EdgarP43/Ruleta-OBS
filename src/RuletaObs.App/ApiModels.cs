using RuletaObs.Core;

namespace RuletaObs.App;

public sealed record DeviceStartRequest(string? ClientId);
public sealed record CreateRewardRequest(string Title, int Cost, string? Prompt);
public sealed record AssignRewardRequest(string RewardId, string Title, bool ManageableByThisApp);
public sealed record SimulateRequest(string? DisplayName, string Source, int Spins, int Amount);
public sealed record ImportEntryRequest(string? DisplayName, string? Source, int Spins);
public sealed record MediaUploadRequest(string FileName, string ContentBase64);

public static class ApiResults
{
    public static IResult Error(string message, int status = 400) =>
        Results.Json(new { error = message }, statusCode: status);
}
