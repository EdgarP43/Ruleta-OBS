using System.Net.Http.Headers;
using System.Text.Json;

namespace RuletaObs.Twitch;

public sealed class TwitchAuthPendingException : Exception
{
    public TwitchAuthPendingException(string message) : base(message) { }
}

public sealed class TwitchAuthClient
{
    private readonly HttpClient _http;
    public TwitchAuthClient(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<DeviceCodeSession> StartDeviceFlowAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scopes"] = string.Join(" ", TwitchScopes.Required)
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/device", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        return new DeviceCodeSession(
            r.GetProperty("device_code").GetString()!,
            r.GetProperty("user_code").GetString()!,
            r.GetProperty("verification_uri").GetString()!,
            r.GetProperty("interval").GetInt32(),
            r.GetProperty("expires_in").GetInt32());
    }

    public async Task<TokenSet> ExchangeDeviceCodeAsync(string clientId, string deviceCode, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scopes"] = string.Join(" ", TwitchScopes.Required),
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TwitchAuthPendingException(body);
        return ParseToken(body);
    }

    public async Task<TokenSet> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseToken(body);
    }

    public async Task<TwitchUser?> ValidateAndGetUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        return new TwitchUser(
            r.GetProperty("user_id").GetString()!,
            r.GetProperty("login").GetString()!,
            r.GetProperty("login").GetString()!,
            null);
    }

    private static TokenSet ParseToken(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        var scopes = r.TryGetProperty("scope", out var s)
            ? s.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : Array.Empty<string>();
        return new TokenSet(
            r.GetProperty("access_token").GetString()!,
            r.GetProperty("refresh_token").GetString()!,
            DateTimeOffset.UtcNow.AddSeconds(r.GetProperty("expires_in").GetInt32()),
            scopes);
    }
}
