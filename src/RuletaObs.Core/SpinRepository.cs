using Microsoft.Data.Sqlite;

namespace RuletaObs.Core;

public sealed class SpinRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpinRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        DataDirectory = dataDirectory;
        DatabasePath = Path.Combine(dataDirectory, "ruleta.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS processed_events (
    event_id TEXT PRIMARY KEY,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS spin_entries (
    id TEXT PRIMARY KEY,
    event_id TEXT NOT NULL,
    source INTEGER NOT NULL,
    viewer_id TEXT NOT NULL,
    viewer_login TEXT NOT NULL,
    viewer_display_name TEXT NOT NULL,
    avatar_url TEXT NULL,
    amount INTEGER NOT NULL,
    reward_id TEXT NULL,
    reward_title TEXT NULL,
    redemption_id TEXT NULL,
    status INTEGER NOT NULL,
    prize_name TEXT NULL,
    created_at TEXT NOT NULL,
    spun_at TEXT NULL,
    closed_at TEXT NULL,
    twitch_sync_pending INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_spin_status_created ON spin_entries(status, created_at);
CREATE INDEX IF NOT EXISTS ix_spin_redemption ON spin_entries(redemption_id);
";
        command.ExecuteNonQuery();
    }

    public async Task<bool> TryAddSpinsAsync(
        string eventId,
        SpinSource source,
        string viewerId,
        string viewerLogin,
        string viewerDisplayName,
        string? avatarUrl,
        int amount,
        string? rewardId,
        string? rewardTitle,
        string? redemptionId,
        int spins,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("El evento necesita ID.", nameof(eventId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var seen = connection.CreateCommand())
            {
                seen.Transaction = transaction;
                seen.CommandText = "INSERT OR IGNORE INTO processed_events(event_id, created_at) VALUES($id, $now); SELECT changes();";
                seen.Parameters.AddWithValue("$id", eventId);
                seen.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                var inserted = Convert.ToInt32(await seen.ExecuteScalarAsync(cancellationToken));
                if (inserted == 0)
                {
                    transaction.Commit();
                    return false;
                }
            }

            for (var i = 0; i < Math.Max(0, spins); i++)
            {
                var entry = new SpinEntry
                {
                    EventId = eventId,
                    Source = source,
                    ViewerId = viewerId,
                    ViewerLogin = viewerLogin,
                    ViewerDisplayName = viewerDisplayName,
                    AvatarUrl = avatarUrl,
                    Amount = amount,
                    RewardId = rewardId,
                    RewardTitle = rewardTitle,
                    RedemptionId = redemptionId,
                    CreatedAt = DateTimeOffset.UtcNow.AddTicks(i)
                };
                Insert(connection, transaction, entry);
            }

            transaction.Commit();
            return true;
        }
        finally { _gate.Release(); }
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, SpinEntry e)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO spin_entries
(id,event_id,source,viewer_id,viewer_login,viewer_display_name,avatar_url,amount,reward_id,reward_title,redemption_id,status,prize_name,created_at,spun_at,closed_at,twitch_sync_pending)
VALUES($id,$event,$source,$vid,$login,$name,$avatar,$amount,$reward,$rewardTitle,$redemption,$status,$prize,$created,$spun,$closed,$sync);";
        command.Parameters.AddWithValue("$id", e.Id);
        command.Parameters.AddWithValue("$event", e.EventId);
        command.Parameters.AddWithValue("$source", (int)e.Source);
        command.Parameters.AddWithValue("$vid", e.ViewerId);
        command.Parameters.AddWithValue("$login", e.ViewerLogin);
        command.Parameters.AddWithValue("$name", e.ViewerDisplayName);
        command.Parameters.AddWithValue("$avatar", (object?)e.AvatarUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$amount", e.Amount);
        command.Parameters.AddWithValue("$reward", (object?)e.RewardId ?? DBNull.Value);
        command.Parameters.AddWithValue("$rewardTitle", (object?)e.RewardTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$redemption", (object?)e.RedemptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)e.Status);
        command.Parameters.AddWithValue("$prize", (object?)e.PrizeName ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", e.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$spun", (object?)e.SpunAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$closed", (object?)e.ClosedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sync", e.TwitchSyncPending ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static SpinEntry Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        EventId = r.GetString(1),
        Source = (SpinSource)r.GetInt32(2),
        ViewerId = r.GetString(3),
        ViewerLogin = r.GetString(4),
        ViewerDisplayName = r.GetString(5),
        AvatarUrl = r.IsDBNull(6) ? null : r.GetString(6),
        Amount = r.GetInt32(7),
        RewardId = r.IsDBNull(8) ? null : r.GetString(8),
        RewardTitle = r.IsDBNull(9) ? null : r.GetString(9),
        RedemptionId = r.IsDBNull(10) ? null : r.GetString(10),
        Status = (SpinStatus)r.GetInt32(11),
        PrizeName = r.IsDBNull(12) ? null : r.GetString(12),
        CreatedAt = DateTimeOffset.Parse(r.GetString(13)),
        SpunAt = r.IsDBNull(14) ? null : DateTimeOffset.Parse(r.GetString(14)),
        ClosedAt = r.IsDBNull(15) ? null : DateTimeOffset.Parse(r.GetString(15)),
        TwitchSyncPending = r.GetInt32(16) == 1
    };

    private const string Columns = "id,event_id,source,viewer_id,viewer_login,viewer_display_name,avatar_url,amount,reward_id,reward_title,redemption_id,status,prize_name,created_at,spun_at,closed_at,twitch_sync_pending";

    public async Task<IReadOnlyList<SpinEntry>> ListAsync(SpinStatus? status = null, int take = 200, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM spin_entries " + (status is null ? "" : "WHERE status=$status ") + "ORDER BY created_at ASC LIMIT $take;";
            if (status is not null) command.Parameters.AddWithValue("$status", (int)status.Value);
            command.Parameters.AddWithValue("$take", take);
            var result = new List<SpinEntry>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<QueueSummary>> GetQueueSummaryAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ListAsync(null, 2000, cancellationToken);
        return entries
            .Where(e => e.Status is SpinStatus.Pending or SpinStatus.AwaitingConfirmation)
            .GroupBy(e => string.IsNullOrWhiteSpace(e.ViewerId) ? e.ViewerLogin : e.ViewerId)
            .Select(g =>
            {
                var last = g.OrderByDescending(e => e.CreatedAt).First();
                return new QueueSummary(last.ViewerId, last.ViewerLogin, last.ViewerDisplayName, last.AvatarUrl,
                    g.Count(e => e.Status == SpinStatus.Pending),
                    g.Count(e => e.Status == SpinStatus.AwaitingConfirmation), last.Source);
            })
            .OrderByDescending(q => q.PendingSpins + q.AwaitingConfirmation)
            .ThenBy(q => q.ViewerDisplayName)
            .ToList();
    }

    public async Task<SpinEntry?> GetCurrentAwaitingAsync(CancellationToken cancellationToken = default)
        => (await ListAsync(SpinStatus.AwaitingConfirmation, 1, cancellationToken)).FirstOrDefault();

    public async Task<SpinEntry?> GetNextPendingAsync(CancellationToken cancellationToken = default)
        => (await ListAsync(SpinStatus.Pending, 1, cancellationToken)).FirstOrDefault();

    public async Task<bool> CountOpenForRedemptionAsync(string redemptionId, CancellationToken cancellationToken = default)
    {
        var entries = await ListAsync(null, 2000, cancellationToken);
        return entries.Any(e => e.RedemptionId == redemptionId && e.Status is SpinStatus.Pending or SpinStatus.AwaitingConfirmation);
    }

    public async Task<bool> HasCompletedForRedemptionAsync(string redemptionId, CancellationToken cancellationToken = default)
    {
        var entries = await ListAsync(null, 2000, cancellationToken);
        return entries.Any(e => e.RedemptionId == redemptionId && e.Status == SpinStatus.Completed);
    }

    public async Task UpdateAsync(SpinEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE spin_entries SET status=$status, prize_name=$prize, spun_at=$spun, closed_at=$closed, twitch_sync_pending=$sync WHERE id=$id;";
            command.Parameters.AddWithValue("$status", (int)entry.Status);
            command.Parameters.AddWithValue("$prize", (object?)entry.PrizeName ?? DBNull.Value);
            command.Parameters.AddWithValue("$spun", (object?)entry.SpunAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$closed", (object?)entry.ClosedAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$sync", entry.TwitchSyncPending ? 1 : 0);
            command.Parameters.AddWithValue("$id", entry.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ExportOpenQueueJsonAsync(CancellationToken cancellationToken = default)
    {
        var open = (await ListAsync(null, 5000, cancellationToken))
            .Where(e => e.Status is SpinStatus.Pending or SpinStatus.AwaitingConfirmation).ToList();
        return System.Text.Json.JsonSerializer.Serialize(open, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM spin_entries WHERE status=$completed OR status=$cancelled;";
                command.Parameters.AddWithValue("$completed", (int)SpinStatus.Completed);
                command.Parameters.AddWithValue("$cancelled", (int)SpinStatus.Cancelled);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            using (var changes = connection.CreateCommand())
            {
                changes.CommandText = "SELECT changes();";
                return Convert.ToInt32(await changes.ExecuteScalarAsync(cancellationToken));
            }
        }
        finally { _gate.Release(); }
    }

    public void BackupIfNeeded()
    {
        try
        {
            var backupDir = Path.Combine(DataDirectory, "backups");
            Directory.CreateDirectory(backupDir);
            var today = Path.Combine(backupDir, $"ruleta-{DateTime.Now:yyyyMMdd}.db");
            if (File.Exists(today)) return;
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA wal_checkpoint(FULL);";
                command.ExecuteNonQuery();
            }
            if (File.Exists(DatabasePath)) File.Copy(DatabasePath, today, false);
            foreach (var old in Directory.GetFiles(backupDir, "ruleta-*.db").OrderByDescending(f => f).Skip(7))
                File.Delete(old);
        }
        catch { /* El respaldo nunca debe impedir abrir la app. */ }
    }
}
