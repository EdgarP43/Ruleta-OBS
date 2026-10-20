using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RuletaObs.Twitch;

public sealed class EventSubWebSocketClient
{
    private string _url = "wss://eventsub.wss.twitch.tv/ws";
    private DateTimeOffset _lastMessage = DateTimeOffset.UtcNow;
    private int _keepaliveSeconds = 30;

    public async Task RunAsync(
        Func<string, CancellationToken, Task> onSessionAsync,
        Func<TwitchSpinEvent, CancellationToken, Task> onEventAsync,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(onSessionAsync, onEventAsync, log, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log($"EventSub desconectado: {ex.Message}. Reintentando en 5 segundos.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ConnectOnceAsync(
        Func<string, CancellationToken, Task> onSessionAsync,
        Func<TwitchSpinEvent, CancellationToken, Task> onEventAsync,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(_url), cancellationToken);
        _url = "wss://eventsub.wss.twitch.tv/ws";
        _lastMessage = DateTimeOffset.UtcNow;
        log("EventSub conectado. Esperando sesión de Twitch.");

        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watchdog = Task.Run(async () =>
        {
            while (!watchdogCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), watchdogCts.Token);
                if (DateTimeOffset.UtcNow - _lastMessage > TimeSpan.FromSeconds(_keepaliveSeconds + 20))
                {
                    log("EventSub sin keepalive: cerrando para reconectar.");
                    try { socket.Abort(); } catch { }
                    break;
                }
            }
        }, watchdogCts.Token);

        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(socket, buffer, cancellationToken);
                if (text is null) break;
                _lastMessage = DateTimeOffset.UtcNow;

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var messageType = root.GetProperty("metadata").GetProperty("message_type").GetString();
                var payload = root.GetProperty("payload");

                if (messageType == "session_welcome")
                {
                    var session = payload.GetProperty("session");
                    _keepaliveSeconds = session.TryGetProperty("keepalive_timeout_seconds", out var k) && k.ValueKind == JsonValueKind.Number ? k.GetInt32() : 30;
                    var sessionId = session.GetProperty("id").GetString()!;
                    await onSessionAsync(sessionId, cancellationToken);
                    log("Suscripciones de EventSub activas.");
                }
                else if (messageType == "session_reconnect")
                {
                    _url = payload.GetProperty("session").GetProperty("reconnect_url").GetString() ?? _url;
                    log("Twitch pidió reconexión de EventSub.");
                    break;
                }
                else if (messageType == "notification")
                {
                    var parsed = EventSubParser.ParseNotification(text, out _, out _);
                    if (parsed is not null) await onEventAsync(parsed, cancellationToken);
                }
                else if (messageType == "revocation")
                {
                    log("Twitch revocó una suscripción de EventSub.");
                }
            }
        }
        finally
        {
            watchdogCts.Cancel();
            try { await watchdog; } catch { }
        }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
