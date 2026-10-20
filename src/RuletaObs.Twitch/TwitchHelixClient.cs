using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RuletaObs.Twitch;

public sealed class TwitchHelixClient
{
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<TokenSet?>> _tokenProvider;
    private readonly Func<string> _clientIdProvider;

    public TwitchHelixClient(Func<CancellationToken, Task<TokenSet?>> tokenProvider, Func<string> clientIdProvider, HttpClient? http = null)
    {
        _tokenProvider = tokenProvider;
        _clientIdProvider = clientIdProvider;
        _http = http ?? new HttpClient { BaseAddress = new Uri("https://api.twitch.tv/helix/") };
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken) ?? throw new InvalidOperationException("Primero conecta una cuenta de Twitch.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", _clientIdProvider());
        return request;
    }

    public async Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        using var request = await RequestAsync(HttpMethod.Get, "users", cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var u = doc.RootElement.GetProperty("data")[0];
        return MapUser(u);
    }

    public async Task<TwitchUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous") return null;
        using var request = await RequestAsync(HttpMethod.Get, $"users?id={Uri.EscapeDataString(userId)}", cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        return data.GetArrayLength() == 0 ? null : MapUser(data[0]);
    }

    private static TwitchUser MapUser(JsonElement u) => new(
        u.GetProperty("id").GetString()!,
        u.GetProperty("login").GetString()!,
        u.GetProperty("display_name").GetString()!,
        u.TryGetProperty("profile_image_url", out var img) ? img.GetString() : null);

    public async Task<IReadOnlyList<TwitchReward>> GetRewardsAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        var manageable = await GetRewardsRawAsync(broadcasterId, true, cancellationToken);
        var manageableIds = manageable.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = await GetRewardsRawAsync(broadcasterId, false, cancellationToken);
        return all.Select(r => r with { ManageableByThisApp = manageableIds.Contains(r.Id) }).ToList();
    }

    private async Task<IReadOnlyList<TwitchReward>> GetRewardsRawAsync(string broadcasterId, bool onlyManageable, CancellationToken cancellationToken)
    {
        var url = $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&only_manageable_rewards={(onlyManageable ? "true" : "false")}";
        using var request = await RequestAsync(HttpMethod.Get, url, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(r => new TwitchReward(
            r.GetProperty("id").GetString()!,
            r.GetProperty("title").GetString()!,
            r.GetProperty("cost").GetInt32(),
            !r.TryGetProperty("is_enabled", out var enabled) || enabled.GetBoolean(),
            onlyManageable)).ToList();
    }

    public async Task<TwitchReward> CreateRewardAsync(string broadcasterId, string title, int cost, string prompt, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            title,
            cost,
            prompt,
            is_enabled = true,
            background_color = "#7C3AED",
            should_redemptions_skip_request_queue = false
        });
        using var request = await RequestAsync(HttpMethod.Post, $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}", cancellationToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement.GetProperty("data")[0];
        return new TwitchReward(r.GetProperty("id").GetString()!, r.GetProperty("title").GetString()!, r.GetProperty("cost").GetInt32(), true, true);
    }

    public async Task<bool> UpdateRedemptionStatusAsync(string broadcasterId, string rewardId, string redemptionId, bool fulfilled, CancellationToken cancellationToken = default)
    {
        var url = $"channel_points/custom_rewards/redemptions?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&reward_id={Uri.EscapeDataString(rewardId)}&id={Uri.EscapeDataString(redemptionId)}";
        using var request = await RequestAsync(HttpMethod.Patch, url, cancellationToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { status = fulfilled ? "FULFILLED" : "CANCELED" }), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task CreateEventSubWebSocketSubscriptionAsync(string sessionId, string type, object condition, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type,
            version = "1",
            condition,
            transport = new { method = "websocket", session_id = sessionId }
        });
        using var request = await RequestAsync(HttpMethod.Post, "eventsub/subscriptions", cancellationToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode && !body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Twitch rechazó la suscripción {type}: {body}");
    }
}
