using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SpeedMeter.Storage;

public sealed class SqliteStorageEngine : IStorageEngine
{
    private interface IStorageCommand { }

    private sealed record SessionStartCmd(SessionRecord Record) : IStorageCommand;
    private sealed record SessionEndCmd(string SessionId, DateTimeOffset EndedAt, bool EndedCleanly, string? StopReason) : IStorageCommand;
    private sealed record ClockSegmentCmd(ClockSegmentRecord Record) : IStorageCommand;
    private sealed record ScopeRevisionCmd(ScopeRevisionRecord Record) : IStorageCommand;
    private sealed record ScopeMembershipCmd(ScopeMembershipRecord Record) : IStorageCommand;
    private sealed record CheckpointCmd(CounterCheckpointRecord Record) : IStorageCommand;
    private sealed record IntervalCmd(UsageIntervalRecord Record, CounterCheckpointRecord? Checkpoint = null) : IStorageCommand;
    private sealed record GapCmd(UsageIntervalRecord Record) : IStorageCommand;
    private sealed record FlushCmd(TaskCompletionSource<bool> Tcs) : IStorageCommand;

    private readonly string connectionString;
    private readonly int channelCapacity;
    private readonly Channel<IStorageCommand> channel;
    private readonly Task writerTask;
    private readonly CancellationTokenSource cts = new();

    private long enqueuedCount;
    private long persistedIntervals;
    private long persistedCheckpoints;
    private long persistedGaps;
    private volatile StorageHealth health = StorageHealth.Healthy;
    private volatile string? degradedReason;
    private bool disposed;

    public StorageStatus Status => new(
        health,
        Interlocked.Read(ref enqueuedCount),
        Interlocked.Read(ref persistedIntervals),
        Interlocked.Read(ref persistedGaps),
        degradedReason);

    public SqliteStorageEngine(string databasePath, int channelCapacity = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.channelCapacity = channelCapacity > 0 ? channelCapacity : 500;

        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        connectionString = builder.ToString();

        // Initialize schema and migrations
        try
        {
            using var initConn = CreateConnection();
            initConn.Open();
            ConfigurePragmas(initConn);
            SqliteMigrations.ApplyMigrations(initConn);
        }
        catch (IncompatibleSchemaException ex)
        {
            health = StorageHealth.Degraded;
            degradedReason = ex.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            health = StorageHealth.Degraded;
            degradedReason = $"Database initialization failed: {ex.Message}";
        }

        var channelOptions = new BoundedChannelOptions(this.channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        channel = Channel.CreateBounded<IStorageCommand>(channelOptions);
        writerTask = Task.Run(WriterLoopAsync);
    }

    private SqliteConnection CreateConnection() => new(connectionString);

    private static void ConfigurePragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task RecordSessionStartAsync(SessionRecord session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await EnqueueCommandAsync(new SessionStartCmd(session), ct).ConfigureAwait(false);
    }

    public async Task RecordSessionEndAsync(string sessionId, DateTimeOffset endedAtUtc, bool endedCleanly, string? stopReason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await EnqueueCommandAsync(new SessionEndCmd(sessionId, endedAtUtc, endedCleanly, stopReason), ct).ConfigureAwait(false);
    }

    public async Task RecordClockSegmentAsync(ClockSegmentRecord segment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        await EnqueueCommandAsync(new ClockSegmentCmd(segment), ct).ConfigureAwait(false);
    }

    public async Task RecordScopeRevisionAsync(ScopeRevisionRecord revision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await EnqueueCommandAsync(new ScopeRevisionCmd(revision), ct).ConfigureAwait(false);
    }

    public async Task RecordScopeMembershipAsync(ScopeMembershipRecord membership, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        await EnqueueCommandAsync(new ScopeMembershipCmd(membership), ct).ConfigureAwait(false);
    }

    public async Task RecordCheckpointAsync(CounterCheckpointRecord checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await EnqueueCommandAsync(new CheckpointCmd(checkpoint), ct).ConfigureAwait(false);
    }

    public async Task IngestIntervalAsync(UsageIntervalRecord interval, CounterCheckpointRecord? checkpoint = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (interval.IsGap) throw new ArgumentException("Use IngestGapAsync for gap records.", nameof(interval));
        await EnqueueCommandAsync(new IntervalCmd(interval, checkpoint), ct).ConfigureAwait(false);
    }

    public async Task IngestGapAsync(UsageIntervalRecord gap, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gap);
        if (!gap.IsGap) throw new ArgumentException("Interval must have IsGap = true.", nameof(gap));
        await EnqueueCommandAsync(new GapCmd(gap), ct).ConfigureAwait(false);
    }

    private async ValueTask EnqueueCommandAsync(IStorageCommand cmd, CancellationToken ct)
    {
        if (disposed) throw new ObjectDisposedException(nameof(SqliteStorageEngine));
        Interlocked.Increment(ref enqueuedCount);

        if (channel.Reader.Count >= channelCapacity * 0.9)
        {
            health = StorageHealth.Degraded;
            degradedReason = "Storage write queue saturated.";
        }

        try
        {
            await channel.Writer.WriteAsync(cmd, ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            health = StorageHealth.Degraded;
            degradedReason = "Storage channel closed.";
            throw new InvalidOperationException("Storage channel closed.");
        }
    }

    public async Task FlushAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (disposed) return;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(new FlushCmd(tcs), ct).ConfigureAwait(false);

        using var ctsTimeout = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);
        try
        {
            await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ctsTimeout.IsCancellationRequested)
        {
            health = StorageHealth.Degraded;
            degradedReason = "Flush timed out.";
            throw new TimeoutException($"Flush timed out after {timeout.TotalSeconds} seconds.");
        }
    }

    private async Task WriterLoopAsync()
    {
        using var conn = CreateConnection();
        try
        {
            conn.Open();
            ConfigurePragmas(conn);
        }
        catch (Exception ex)
        {
            health = StorageHealth.Degraded;
            degradedReason = $"Failed to open writer connection: {ex.Message}";
            return;
        }

        var reader = channel.Reader;
        var batch = new List<IStorageCommand>(128);

        while (!cts.IsCancellationRequested || reader.Count > 0)
        {
            batch.Clear();
            try
            {
                if (await reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
                {
                    while (batch.Count < 100 && reader.TryRead(out var cmd))
                    {
                        batch.Add(cmd);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Drain remaining items on cancellation
                while (reader.TryRead(out var cmd))
                {
                    batch.Add(cmd);
                }
            }

            if (batch.Count == 0) continue;

            try
            {
                using var tx = conn.BeginTransaction();
                var flushCompletions = new List<TaskCompletionSource<bool>>();

                foreach (var cmd in batch)
                {
                    switch (cmd)
                    {
                        case SessionStartCmd start:
                            ExecuteSessionStart(conn, tx, start.Record);
                            break;
                        case SessionEndCmd end:
                            ExecuteSessionEnd(conn, tx, end.SessionId, end.EndedAt, end.EndedCleanly, end.StopReason);
                            break;
                        case ClockSegmentCmd segment:
                            ExecuteClockSegment(conn, tx, segment.Record);
                            break;
                        case ScopeRevisionCmd rev:
                            ExecuteScopeRevision(conn, tx, rev.Record);
                            break;
                        case ScopeMembershipCmd mem:
                            ExecuteScopeMembership(conn, tx, mem.Record);
                            break;
                        case CheckpointCmd cp:
                            ExecuteCheckpoint(conn, tx, cp.Record);
                            break;
                        case IntervalCmd iv:
                            ExecuteInterval(conn, tx, iv.Record);
                            if (iv.Checkpoint is not null)
                            {
                                ExecuteCheckpoint(conn, tx, iv.Checkpoint);
                                Interlocked.Increment(ref persistedCheckpoints);
                            }
                            Interlocked.Increment(ref persistedIntervals);
                            break;
                        case GapCmd gap:
                            ExecuteInterval(conn, tx, gap.Record);
                            Interlocked.Increment(ref persistedGaps);
                            break;
                        case FlushCmd flush:
                            flushCompletions.Add(flush.Tcs);
                            break;
                    }
                }

                tx.Commit();

                foreach (var f in flushCompletions)
                {
                    f.TrySetResult(true);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                health = StorageHealth.Degraded;
                degradedReason = $"Database write batch failed: {ex.Message}";
                // Fail pending flush tokens
                foreach (var cmd in batch)
                {
                    if (cmd is FlushCmd f) f.Tcs.TrySetException(ex);
                }
            }
        }
    }

    private static void ExecuteSessionStart(SqliteConnection conn, SqliteTransaction tx, SessionRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO collection_sessions(session_id, monotonic_epoch, started_at_utc_ms, ended_at_utc_ms, stop_reason, ended_cleanly)
            VALUES (@sid, @epoch, @start, @end, @reason, @clean)
            ON CONFLICT(session_id) DO UPDATE SET
                ended_at_utc_ms = excluded.ended_at_utc_ms,
                stop_reason = excluded.stop_reason,
                ended_cleanly = excluded.ended_cleanly;
        ";
        cmd.Parameters.AddWithValue("@sid", r.SessionId);
        cmd.Parameters.AddWithValue("@epoch", r.MonotonicEpoch);
        cmd.Parameters.AddWithValue("@start", r.StartedAtUtcMs);
        cmd.Parameters.AddWithValue("@end", (object?)r.EndedAtUtcMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", (object?)r.StopReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@clean", r.EndedCleanly ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteSessionEnd(SqliteConnection conn, SqliteTransaction tx, string sid, DateTimeOffset endedAt, bool endedCleanly, string? reason)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE collection_sessions
            SET ended_at_utc_ms = @end, ended_cleanly = @clean, stop_reason = @reason
            WHERE session_id = @sid;
        ";
        cmd.Parameters.AddWithValue("@sid", sid);
        cmd.Parameters.AddWithValue("@end", endedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@clean", endedCleanly ? 1 : 0);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteClockSegment(SqliteConnection conn, SqliteTransaction tx, ClockSegmentRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO clock_segments(segment_id, session_id, monotonic_anchor_ticks, utc_anchor_ms, ticks_per_second, timing_confidence, change_reason)
            VALUES (@segId, @sid, @mono, @utc, @tps, @conf, @reason);
        ";
        cmd.Parameters.AddWithValue("@segId", r.SegmentId);
        cmd.Parameters.AddWithValue("@sid", r.SessionId);
        cmd.Parameters.AddWithValue("@mono", r.MonotonicAnchorTicks);
        cmd.Parameters.AddWithValue("@utc", r.UtcAnchorMs);
        cmd.Parameters.AddWithValue("@tps", r.TicksPerSecond);
        cmd.Parameters.AddWithValue("@conf", r.TimingConfidence);
        cmd.Parameters.AddWithValue("@reason", (object?)r.ChangeReason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteScopeRevision(SqliteConnection conn, SqliteTransaction tx, ScopeRevisionRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO scope_revisions(revision_id, scope_id, revision_number, scope_mode, source_method, source_version, boundary_definition, validity, created_at_utc_ms)
            VALUES (@revId, @scopeId, @revNum, @mode, @method, @ver, @bound, @valid, @created);
        ";
        cmd.Parameters.AddWithValue("@revId", r.RevisionId);
        cmd.Parameters.AddWithValue("@scopeId", r.ScopeId);
        cmd.Parameters.AddWithValue("@revNum", r.RevisionNumber);
        cmd.Parameters.AddWithValue("@mode", r.ScopeMode);
        cmd.Parameters.AddWithValue("@method", r.SourceMethod);
        cmd.Parameters.AddWithValue("@ver", r.SourceVersion);
        cmd.Parameters.AddWithValue("@bound", r.BoundaryDefinition);
        cmd.Parameters.AddWithValue("@valid", r.Validity);
        cmd.Parameters.AddWithValue("@created", r.CreatedAtUtcMs);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteScopeMembership(SqliteConnection conn, SqliteTransaction tx, ScopeMembershipRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO scope_membership(membership_id, scope_revision_id, interface_id, observed_incarnation, network_profile_id, session_id, starts_at_monotonic_ticks, ends_at_monotonic_ticks)
            VALUES (@memId, @revId, @ifId, @inc, @profId, @sid, @start, @end);
        ";
        cmd.Parameters.AddWithValue("@memId", r.MembershipId);
        cmd.Parameters.AddWithValue("@revId", r.ScopeRevisionId);
        cmd.Parameters.AddWithValue("@ifId", r.InterfaceId);
        cmd.Parameters.AddWithValue("@inc", (object?)r.ObservedIncarnation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@profId", r.NetworkProfileId);
        cmd.Parameters.AddWithValue("@sid", r.SessionId);
        cmd.Parameters.AddWithValue("@start", r.StartsAtMonotonicTicks);
        cmd.Parameters.AddWithValue("@end", (object?)r.EndsAtMonotonicTicks ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteCheckpoint(SqliteConnection conn, SqliteTransaction tx, CounterCheckpointRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO counter_checkpoints(checkpoint_id, session_id, interface_id, source_method, observed_source_epoch, observed_sequence, monotonic_ticks, observed_at_utc_ms, counter_rx, counter_tx)
            VALUES (@cpId, @sid, @ifId, @method, @epoch, @seq, @ticks, @utc, @rx, @tx);
        ";
        cmd.Parameters.AddWithValue("@cpId", r.CheckpointId);
        cmd.Parameters.AddWithValue("@sid", r.SessionId);
        cmd.Parameters.AddWithValue("@ifId", r.InterfaceId);
        cmd.Parameters.AddWithValue("@method", r.SourceMethod);
        cmd.Parameters.AddWithValue("@epoch", (object?)r.ObservedSourceEpoch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seq", r.ObservedSequence);
        cmd.Parameters.AddWithValue("@ticks", r.MonotonicTicks);
        cmd.Parameters.AddWithValue("@utc", r.ObservedAtUtcMs);
        cmd.Parameters.AddWithValue("@rx", r.CounterRx);
        cmd.Parameters.AddWithValue("@tx", r.CounterTx);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteInterval(SqliteConnection conn, SqliteTransaction tx, UsageIntervalRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO usage_intervals(
                interval_id, source_interval_key, session_id, clock_segment_id, scope_revision_id,
                network_profile_id, start_utc_ms, end_utc_ms, monotonic_duration_ticks,
                bytes_rx, bytes_tx, is_coarse, is_gap, gap_reason, timing_confidence)
            VALUES (
                @id, @key, @sid, @segId, @revId,
                @profId, @start, @end, @duration,
                @rx, @tx, @coarse, @gap, @reason, @conf);
        ";
        cmd.Parameters.AddWithValue("@id", r.IntervalId);
        cmd.Parameters.AddWithValue("@key", r.SourceIntervalKey);
        cmd.Parameters.AddWithValue("@sid", r.SessionId);
        cmd.Parameters.AddWithValue("@segId", r.ClockSegmentId);
        cmd.Parameters.AddWithValue("@revId", r.ScopeRevisionId);
        cmd.Parameters.AddWithValue("@profId", r.NetworkProfileId);
        cmd.Parameters.AddWithValue("@start", r.StartUtcMs);
        cmd.Parameters.AddWithValue("@end", r.EndUtcMs);
        cmd.Parameters.AddWithValue("@duration", (object?)r.MonotonicDurationTicks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rx", r.BytesRx);
        cmd.Parameters.AddWithValue("@tx", r.BytesTx);
        cmd.Parameters.AddWithValue("@coarse", r.IsCoarse ? 1 : 0);
        cmd.Parameters.AddWithValue("@gap", r.IsGap ? 1 : 0);
        cmd.Parameters.AddWithValue("@reason", (object?)r.GapReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@conf", r.TimingConfidence);
        cmd.ExecuteNonQuery();
    }

    public async Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, monotonic_epoch, started_at_utc_ms, ended_at_utc_ms, stop_reason, ended_cleanly
            FROM collection_sessions
            ORDER BY started_at_utc_ms DESC
            LIMIT 1;
        ";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new SessionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) == 1);
        }
        return null;
    }

    public async Task<long?> GetLastDurableTimestampUtcMsAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT MAX(ts) FROM (
                SELECT MAX(end_utc_ms) AS ts FROM usage_intervals WHERE session_id = @sid
                UNION ALL
                SELECT MAX(observed_at_utc_ms) AS ts FROM counter_checkpoints WHERE session_id = @sid
            );
        ";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value ? Convert.ToInt64(result) : null;
    }

    public async Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(
        string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        var sql = "SELECT interval_id, source_interval_key, session_id, clock_segment_id, scope_revision_id, network_profile_id, start_utc_ms, end_utc_ms, monotonic_duration_ticks, bytes_rx, bytes_tx, is_coarse, is_gap, gap_reason, timing_confidence FROM usage_intervals WHERE 1=1";
        if (scopeRevisionId is not null)
        {
            sql += " AND scope_revision_id = @rev";
            cmd.Parameters.AddWithValue("@rev", scopeRevisionId);
        }
        if (startUtcMs.HasValue)
        {
            sql += " AND end_utc_ms >= @start";
            cmd.Parameters.AddWithValue("@start", startUtcMs.Value);
        }
        if (endUtcMs.HasValue)
        {
            sql += " AND start_utc_ms <= @end";
            cmd.Parameters.AddWithValue("@end", endUtcMs.Value);
        }
        sql += " ORDER BY start_utc_ms ASC;";
        cmd.CommandText = sql;

        var list = new List<UsageIntervalRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new UsageIntervalRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt32(11) == 1,
                reader.GetInt32(12) == 1,
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetString(14)));
        }
        return list;
    }

    public async Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(
        string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default)
    {
        // Enforces PRD §22 & Prompt §20: Checked application-side summation rather than potentially overflowing SQL SUM()
        var intervals = await GetIntervalsAsync(scopeRevisionId, startUtcMs, endUtcMs, ct).ConfigureAwait(false);
        long rx = 0;
        long tx = 0;
        foreach (var iv in intervals)
        {
            if (!iv.IsGap)
            {
                rx = CheckedIntegerMath.CheckedAdd(rx, iv.BytesRx, "total rx aggregation");
                tx = CheckedIntegerMath.CheckedAdd(tx, iv.BytesTx, "total tx aggregation");
            }
        }
        return (rx, tx);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        cts.Cancel();
        channel.Writer.TryComplete();
        try
        {
            await writerTask.ConfigureAwait(false);
        }
        catch { }
        cts.Dispose();
        health = StorageHealth.Closed;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
