using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RuletaObs.Core;
using RuletaObs.Twitch;

namespace RuletaObs.App;

public sealed class TokenStore
{
    private readonly string _path;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RuletaObs.Twitch.Token.v1");

    public TokenStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "twitch-token.bin");
    }

    public bool HasToken => File.Exists(_path);

    public TokenSet? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<TokenSet>(Encoding.UTF8.GetString(clear));
        }
        catch { return null; }
    }

    public void Save(TokenSet token)
    {
        var clear = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
        var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed class TokenManager
{
    private readonly TokenStore _store;
    private readonly ConfigStore _config;
    private readonly TwitchAuthClient _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenSet? _cached;

    public TokenManager(TokenStore store, ConfigStore config, TwitchAuthClient auth)
    {
        _store = store;
        _config = config;
        _auth = auth;
        _cached = store.Load();
    }

    public bool HasToken => _cached is not null || _store.HasToken;

    public async Task SaveAsync(TokenSet token)
    {
        await _gate.WaitAsync();
        try { _cached = token; _store.Save(token); }
        finally { _gate.Release(); }
    }

    public async Task<TokenSet?> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cached ??= _store.Load();
            if (_cached is null) return null;
            if (_cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(3)) return _cached;

            var clientId = _config.Current.TwitchClientId;
            if (string.IsNullOrWhiteSpace(clientId)) return _cached;
            _cached = await _auth.RefreshAsync(clientId, _cached.RefreshToken, cancellationToken);
            _store.Save(_cached);
            return _cached;
        }
        catch
        {
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try { _cached = null; _store.Clear(); }
        finally { _gate.Release(); }
    }
}
