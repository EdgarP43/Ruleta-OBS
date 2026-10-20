using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using RuletaObs.App;
using RuletaObs.Core;
using RuletaObs.Twitch;

var dataDirectory = Environment.GetEnvironmentVariable("RULETA_DATA_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RuletaObs");
Directory.CreateDirectory(dataDirectory);

var configStore = new ConfigStore(dataDirectory);
if (string.IsNullOrWhiteSpace(configStore.Current.TwitchClientId))
{
    var fromEnvironment = Environment.GetEnvironmentVariable("RULETA_TWITCH_CLIENT_ID");
    var initialClientId = !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : BuiltInTwitchClientId.Value;
    if (!string.IsNullOrWhiteSpace(initialClientId))
    {
        configStore.Current.TwitchClientId = initialClientId.Trim();
        await configStore.SaveAsync(configStore.Current);
    }
}

var spinRepository = new SpinRepository(dataDirectory);
spinRepository.BackupIfNeeded();

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("RULETA_URL") ?? "http://127.0.0.1:47832");
builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton(spinRepository);
builder.Services.AddSingleton(new TokenStore(dataDirectory));
builder.Services.AddSingleton<TwitchAuthClient>();
builder.Services.AddSingleton<TokenManager>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<TwitchRuntimeState>();
builder.Services.AddSingleton<AuthFlowState>();
builder.Services.AddSingleton<TwitchHelixClient>(sp => new TwitchHelixClient(
    ct => sp.GetRequiredService<TokenManager>().GetValidTokenAsync(ct),
    () => sp.GetRequiredService<ConfigStore>().Current.TwitchClientId));
builder.Services.AddSingleton<SpinService>();
builder.Services.AddSingleton<TwitchEventProcessor>();
builder.Services.AddHostedService<TwitchWorker>();

var app = builder.Build();
var config = app.Services.GetRequiredService<ConfigStore>();
var repo = app.Services.GetRequiredService<SpinRepository>();
var bus = app.Services.GetRequiredService<EventBus>();
var runtime = app.Services.GetRequiredService<TwitchRuntimeState>();
var tokens = app.Services.GetRequiredService<TokenManager>();
var auth = app.Services.GetRequiredService<TwitchAuthClient>();
var helix = app.Services.GetRequiredService<TwitchHelixClient>();
var authFlow = app.Services.GetRequiredService<AuthFlowState>();
var spinService = app.Services.GetRequiredService<SpinService>();

string? GetEmbeddedText(string fileName)
{
    var assembly = Assembly.GetExecutingAssembly();
    var resource = assembly.GetManifestResourceNames()
        .FirstOrDefault(n => n.Replace('/', '.').EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase) ||
                             n.Replace('\\', '.').EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase) ||
                             n.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    if (resource is null) return null;
    using var stream = assembly.GetManifestResourceStream(resource);
    if (stream is null) return null;
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
}

IResult Page(string fileName)
{
    var html = GetEmbeddedText(fileName);
    return html is null ? Results.NotFound($"Falta {fileName}") : Results.Content(html, "text/html; charset=utf-8");
}

bool HasLocalKey(HttpRequest request)
{
    var expected = config.Current.OverlayKey;
    return request.Headers.TryGetValue("X-Ruleta-Key", out var value) && value.ToString() == expected;
}

async Task PublishQueueAsync()
{
    bus.Publish("queue", new { updatedAt = DateTimeOffset.UtcNow, summary = await repo.GetQueueSummaryAsync() });
}

async Task<bool> TrySyncRedemptionAsync(SpinEntry entry, CancellationToken cancellationToken)
{
    if (entry.Source != SpinSource.ChannelPoints || string.IsNullOrWhiteSpace(entry.RedemptionId) ||
        string.IsNullOrWhiteSpace(entry.RewardId) || !config.Current.PointsRewardManageable ||
        string.IsNullOrWhiteSpace(config.Current.BroadcasterId)) return true;

    if (await repo.CountOpenForRedemptionAsync(entry.RedemptionId, cancellationToken)) return true;
    var fulfilled = await repo.HasCompletedForRedemptionAsync(entry.RedemptionId, cancellationToken);
    var ok = await helix.UpdateRedemptionStatusAsync(config.Current.BroadcasterId!, entry.RewardId!, entry.RedemptionId!, fulfilled, cancellationToken);
    if (!ok)
    {
        entry.TwitchSyncPending = true;
        await repo.UpdateAsync(entry, cancellationToken);
    }
    return ok;
}

app.MapGet("/", () => Page("index.html"));
app.MapGet("/setup", () => Page("index.html"));
app.MapGet("/panel", () => Page("panel.html"));
app.MapGet("/overlay", (HttpRequest request) =>
    request.Query["key"].ToString() == config.Current.OverlayKey ? Page("overlay.html") : Results.StatusCode(StatusCodes.Status403Forbidden));
app.MapGet("/assets/{file}", (string file) =>
{
    var content = GetEmbeddedText(file);
    if (content is null) return Results.NotFound();
    var type = Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".html" => "text/html; charset=utf-8",
        _ => "application/octet-stream"
    };
    return Results.Content(content, type);
});
app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapGet("/api/status", async () => Results.Json(new
{
    dataDirectory,
    broadcasterId = config.Current.BroadcasterId,
    broadcasterLogin = config.Current.BroadcasterLogin,
    broadcasterDisplayName = config.Current.BroadcasterDisplayName,
    twitchClientConfigured = !string.IsNullOrWhiteSpace(config.Current.TwitchClientId),
    twitchConnected = tokens.HasToken,
    eventSubConnected = runtime.EventSubConnected,
    overlayClients = runtime.OverlayClients,
    lastLog = runtime.LastLog,
    logs = runtime.Logs,
    queue = await repo.GetQueueSummaryAsync(),
    current = await repo.GetCurrentAwaitingAsync()
}));

app.MapGet("/api/config", () => Results.Json(config.Current));
app.MapPost("/api/config", async (AppConfig updated, HttpRequest request) =>
{
    if (!HasLocalKey(request)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    if (updated.Prizes is null || updated.Prizes.Count == 0) return ApiResults.Error("Debe existir al menos un premio.");
    if (updated.Prizes.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Weight < 0)) return ApiResults.Error("Revisa nombre y peso de los premios.");
    updated.OverlayKey = config.Current.OverlayKey;
    await config.SaveAsync(updated);
    bus.Publish("config", new { updatedAt = DateTimeOffset.UtcNow });
    await PublishQueueAsync();
    return Results.Json(config.Current);
});

app.MapGet("/api/queue", async () => Results.Json(new
{
    summary = await repo.GetQueueSummaryAsync(),
    current = await repo.GetCurrentAwaitingAsync(),
    pending = await repo.ListAsync(SpinStatus.Pending, 100),
    prizes = config.Current.Prizes,
    soundEnabled = config.Current.SoundEnabled
}));

app.MapGet("/api/events", async (HttpContext context, HttpRequest request) =>
{
    var isOverlay = request.Query["client"].ToString() == "overlay";
    if (isOverlay && request.Query["key"].ToString() != config.Current.OverlayKey)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    var (id, reader) = bus.Subscribe();
    if (isOverlay) runtime.OverlayClients++;
    try
    {
        await context.Response.WriteAsync("event: hello\ndata: {\"ok\":true}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        while (!context.RequestAborted.IsCancellationRequested)
        {
            var readTask = reader.WaitToReadAsync(context.RequestAborted).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted));
            if (completed != readTask)
            {
                await context.Response.WriteAsync(": heartbeat\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                continue;
            }
            while (reader.TryRead(out var payload))
            {
                await context.Response.WriteAsync(payload, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        bus.Unsubscribe(id);
        if (isOverlay) runtime.OverlayClients = Math.Max(0, runtime.OverlayClients - 1);
    }
});

app.MapPost("/api/twitch/device/start", async (DeviceStartRequest request, HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    if (!string.IsNullOrWhiteSpace(request.ClientId) && request.ClientId.Trim() != config.Current.TwitchClientId)
    {
        config.Current.TwitchClientId = request.ClientId.Trim();
        await config.SaveAsync(config.Current);
    }
    if (string.IsNullOrWhiteSpace(config.Current.TwitchClientId))
        return ApiResults.Error("Falta el Client ID público de Twitch. El desarrollador debe integrarlo o pegarlo en el campo avanzado.");

    var session = await auth.StartDeviceFlowAsync(config.Current.TwitchClientId, http.HttpContext.RequestAborted);
    authFlow.Pending = session;
    authFlow.LastError = null;
    if (!authFlow.IsPolling)
    {
        authFlow.IsPolling = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(session.ExpiresInSeconds);
                var interval = Math.Max(2, session.IntervalSeconds);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval));
                    try
                    {
                        var token = await auth.ExchangeDeviceCodeAsync(config.Current.TwitchClientId, session.DeviceCode);
                        await tokens.SaveAsync(token);
                        var user = await helix.GetCurrentUserAsync();
                        config.Current.BroadcasterId = user.Id;
                        config.Current.BroadcasterLogin = user.Login;
                        config.Current.BroadcasterDisplayName = user.DisplayName;
                        await config.SaveAsync(config.Current);
                        runtime.Log($"Canal conectado: {user.DisplayName}");
                        bus.Publish("status", new { connected = true, user.DisplayName });
                        break;
                    }
                    catch (TwitchAuthPendingException ex)
                    {
                        if (ex.Message.Contains("slow_down", StringComparison.OrdinalIgnoreCase)) interval += 2;
                    }
                }
            }
            catch (Exception ex)
            {
                authFlow.LastError = ex.Message;
                runtime.Log($"No se pudo conectar Twitch: {ex.Message}");
            }
            finally { authFlow.IsPolling = false; authFlow.Pending = null; }
        });
    }
    return Results.Json(new { session.UserCode, session.VerificationUri, session.ExpiresInSeconds });
});

app.MapPost("/api/twitch/disconnect", async (HttpRequest request) =>
{
    if (!HasLocalKey(request)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    await tokens.ClearAsync();
    config.Current.BroadcasterId = null;
    config.Current.BroadcasterLogin = null;
    config.Current.BroadcasterDisplayName = null;
    await config.SaveAsync(config.Current);
    runtime.EventSubConnected = false;
    runtime.Log("Twitch desconectado en esta PC.");
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/twitch/rewards", async (CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(config.Current.BroadcasterId)) return ApiResults.Error("Primero conecta Twitch.");
    try { return Results.Json(await helix.GetRewardsAsync(config.Current.BroadcasterId!, cancellationToken)); }
    catch (Exception ex) { return ApiResults.Error($"Twitch devolvió un error: {ex.Message}"); }
});

app.MapPost("/api/twitch/rewards/create", async (CreateRewardRequest request, HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(config.Current.BroadcasterId)) return ApiResults.Error("Primero conecta Twitch.");
    if (string.IsNullOrWhiteSpace(request.Title) || request.Cost <= 0) return ApiResults.Error("La recompensa necesita nombre y costo mayor a cero.");
    try
    {
        var reward = await helix.CreateRewardAsync(config.Current.BroadcasterId!, request.Title.Trim(), request.Cost,
            string.IsNullOrWhiteSpace(request.Prompt) ? "Canjea y agrega una tirada a la ruleta" : request.Prompt, http.HttpContext.RequestAborted);
        config.Current.PointsRewardId = reward.Id;
        config.Current.PointsRewardTitle = reward.Title;
        config.Current.PointsRewardManageable = true;
        await config.SaveAsync(config.Current);
        return Results.Json(reward);
    }
    catch (Exception ex) { return ApiResults.Error($"No se pudo crear la recompensa: {ex.Message}"); }
});

app.MapPost("/api/twitch/rewards/assign", async (AssignRewardRequest request, HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    config.Current.PointsRewardId = request.RewardId;
    config.Current.PointsRewardTitle = request.Title;
    config.Current.PointsRewardManageable = request.ManageableByThisApp;
    await config.SaveAsync(config.Current);
    return Results.Json(new { ok = true, warning = request.ManageableByThisApp ? null : "Esta recompensa no fue creada por esta app: se podrá usar, pero Twitch no permitirá completarla ni reembolsarla automáticamente." });
});

app.MapPost("/api/queue/simulate", async (SimulateRequest request, HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    var source = Enum.TryParse<SpinSource>(request.Source, true, out var parsed) ? parsed : SpinSource.ManualTest;
    var name = string.IsNullOrWhiteSpace(request.DisplayName) ? "EspectadorDemo" : request.DisplayName.Trim();
    var spins = Math.Clamp(request.Spins, 1, 20);
    var inserted = await repo.TryAddSpinsAsync($"manual:{Guid.NewGuid():N}", source, $"demo:{name}", name.ToLowerInvariant(), name, null,
        request.Amount, config.Current.PointsRewardId, config.Current.PointsRewardTitle, null, spins, http.HttpContext.RequestAborted);
    if (inserted) bus.Publish("alert", new { viewer = name, source = source.ToString(), amount = request.Amount, spins, message = spins == 1 ? "agregó 1 tirada de prueba" : $"agregó {spins} tiradas de prueba" });
    await PublishQueueAsync();
    if (inserted) await spinService.TryAutoSpinAsync(http.HttpContext.RequestAborted);
    return Results.Json(new { ok = inserted, spins });
});

app.MapPost("/api/spin/next", async (HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    if (await repo.GetCurrentAwaitingAsync(http.HttpContext.RequestAborted) is not null) return ApiResults.Error("Ya hay una tirada esperando confirmación.");
    SpinResult? payload;
    try { payload = await spinService.TrySpinNextAsync(http.HttpContext.RequestAborted); }
    catch (Exception ex) { return ApiResults.Error(ex.Message); }
    if (payload is null) return ApiResults.Error("No hay tiradas pendientes.");
    return Results.Json(payload);
});

app.MapPost("/api/spin/confirm", async (HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    var current = await repo.GetCurrentAwaitingAsync(http.HttpContext.RequestAborted);
    if (current is null) return ApiResults.Error("No hay una tirada esperando confirmación.");
    current.Complete(DateTimeOffset.UtcNow);
    await repo.UpdateAsync(current, http.HttpContext.RequestAborted);
    var synced = await TrySyncRedemptionAsync(current, http.HttpContext.RequestAborted);
    bus.Publish("closed", new { entry = current, action = "completed", synced });
    await PublishQueueAsync();
    await spinService.TryAutoSpinAsync(http.HttpContext.RequestAborted);
    return Results.Json(new { ok = true, entry = current, twitchSynced = synced });
});

app.MapPost("/api/spin/cancel", async (HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    var current = await repo.GetCurrentAwaitingAsync(http.HttpContext.RequestAborted) ?? await repo.GetNextPendingAsync(http.HttpContext.RequestAborted);
    if (current is null) return ApiResults.Error("No hay tirada para cancelar.");
    current.Cancel(DateTimeOffset.UtcNow);
    await repo.UpdateAsync(current, http.HttpContext.RequestAborted);
    var synced = await TrySyncRedemptionAsync(current, http.HttpContext.RequestAborted);
    bus.Publish("closed", new { entry = current, action = "cancelled", synced });
    await PublishQueueAsync();
    await spinService.TryAutoSpinAsync(http.HttpContext.RequestAborted);
    return Results.Json(new { ok = true, entry = current, twitchSynced = synced });
});

app.MapGet("/api/queue/export", async (HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    var json = await repo.ExportOpenQueueJsonAsync(http.HttpContext.RequestAborted);
    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"ruleta-cola-{DateTime.Now:yyyyMMddHHmm}.json");
});

app.MapPost("/api/queue/import", async (List<ImportEntryRequest> entries, HttpRequest http) =>
{
    if (!HasLocalKey(http)) return ApiResults.Error("Clave local inválida.", StatusCodes.Status403Forbidden);
    var count = 0;
    foreach (var item in entries.Take(500))
    {
        var name = string.IsNullOrWhiteSpace(item.DisplayName) ? "Importado" : item.DisplayName.Trim();
        var source = Enum.TryParse<SpinSource>(item.Source, true, out var parsed) ? parsed : SpinSource.ManualTest;
        if (await repo.TryAddSpinsAsync($"import:{Guid.NewGuid():N}", source, $"import:{name}", name.ToLowerInvariant(), name, null, 0, null, null, null, Math.Clamp(item.Spins, 1, 100), http.HttpContext.RequestAborted)) count++;
    }
    await PublishQueueAsync();
    return Results.Json(new { ok = true, importedGroups = count });
});

app.MapGet("/healthz", () => Results.Ok(new { ok = true, utc = DateTimeOffset.UtcNow }));

if (!string.Equals(Environment.GetEnvironmentVariable("RULETA_NO_BROWSER"), "1", StringComparison.Ordinal))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var address = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:47832";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address + "/") { UseShellExecute = true });
        }
        catch { /* Si el navegador no abre, el usuario puede entrar a http://127.0.0.1:47832 */ }
    });
}

app.Run();
