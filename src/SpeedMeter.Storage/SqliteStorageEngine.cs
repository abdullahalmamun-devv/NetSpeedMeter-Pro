using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.Core;

namespace SpeedMeter.Storage;

public sealed class SqliteStorageEngine : IStorageEngine, IWave4EventStore
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
    private sealed record QuotaEventCmd(QuotaThresholdEvent Record) : IStorageCommand;
    private sealed record ConnectivityEventCmd(ConnectivityEventRecord Record, TaskCompletionSource<bool> Tcs) : IStorageCommand;
    private sealed record AlertCasCmd(string Key, long ExpectedVersion, AlertIncidentState NextState, AlertEvent Event, TaskCompletionSource<bool> Tcs) : IStorageCommand;
    private sealed record QuotaAlertCmd(AlertEvent Event, TaskCompletionSource<bool> Tcs) : IStorageCommand;
    private sealed record FlushCmd(TaskCompletionSource<bool> Tcs) : IStorageCommand;
    private sealed record PruneHistoryCmd(long BeforeUtcMs, TaskCompletionSource<int> Tcs) : IStorageCommand;
    private sealed record ClearHistoryCmd(TaskCompletionSource<bool> Tcs) : IStorageCommand;

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

    public async Task RecordConnectivityEventAsync(ConnectivityEventRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new ConnectivityEventCmd(record, completion), ct).ConfigureAwait(false);
        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryCommitAlertAsync(string key, long expectedVersion, AlertIncidentState nextState, AlertEvent alertEvent, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(nextState);
        ArgumentNullException.ThrowIfNull(alertEvent);
        if (key != nextState.Key || nextState.Version != checked(expectedVersion + 1))
            throw new ArgumentException("Alert state key/version mismatch.", nameof(nextState));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new AlertCasCmd(key, expectedVersion, nextState, alertEvent, completion), ct).ConfigureAwait(false);
        return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryRecordQuotaAlertAsync(AlertEvent alertEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alertEvent);
        if (alertEvent.Kind != AlertKind.QuotaThreshold) throw new ArgumentException("Quota alert kind required.", nameof(alertEvent));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new QuotaAlertCmd(alertEvent, completion), ct).ConfigureAwait(false);
        return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> PruneHistoryAsync(long beforeUtcMs, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new PruneHistoryCmd(beforeUtcMs, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAllHistoryAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new ClearHistoryCmd(tcs), ct).ConfigureAwait(false);
        await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask EnqueueCommandAsync(IStorageCommand cmd, CancellationToken ct)
    {
        if (disposed) throw new ObjectDisposedException(nameof(SqliteStorageEngine));
        if (channel.Reader.Count >= channelCapacity * 0.9)
        {
            health = StorageHealth.Degraded;
            degradedReason = "Storage write queue saturated.";
        }

        try
        {
            await channel.Writer.WriteAsync(cmd, ct).ConfigureAwait(false);
            Interlocked.Increment(ref enqueuedCount);
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
        using var ctsTimeout = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);
        try
        {
            await channel.Writer.WriteAsync(new FlushCmd(tcs), linked.Token).ConfigureAwait(false);
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
            channel.Writer.TryComplete(ex);
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
                var durableCompletions = new List<TaskCompletionSource<bool>>();
                var alertResults = new List<(TaskCompletionSource<bool> Completion, bool Committed)>();
                var pruneResults = new List<(TaskCompletionSource<int> Completion, int Count)>();
                var clearCompletions = new List<TaskCompletionSource<bool>>();
                var committedIntervals = 0;
                var committedGaps = 0;
                var committedCheckpoints = 0;

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
                            committedIntervals += ExecuteInterval(conn, tx, iv.Record);
                            if (iv.Checkpoint is not null)
                            {
                                ExecuteCheckpoint(conn, tx, iv.Checkpoint);
                                committedCheckpoints++;
                            }
                            break;
                        case GapCmd gap:
                            committedGaps += ExecuteInterval(conn, tx, gap.Record);
                            break;
                        case QuotaEventCmd qe:
                            ExecuteQuotaEvent(conn, tx, qe.Record);
                            break;
                        case ConnectivityEventCmd ce:
                            ExecuteConnectivityEvent(conn, tx, ce.Record);
                            durableCompletions.Add(ce.Tcs);
                            break;
                        case AlertCasCmd ac:
                            var committed = ExecuteAlertCas(conn, tx, ac.Key, ac.ExpectedVersion, ac.NextState, ac.Event);
                            alertResults.Add((ac.Tcs, committed));
                            break;
                        case QuotaAlertCmd qa:
                            alertResults.Add((qa.Tcs, ExecuteAlertEvent(conn, tx, qa.Event) > 0));
                            break;
                        case PruneHistoryCmd prune:
                            var deleted = ExecutePruneHistory(conn, tx, prune.BeforeUtcMs);
                            pruneResults.Add((prune.Tcs, deleted));
                            break;
                        case ClearHistoryCmd clear:
                            ExecuteClearHistory(conn, tx);
                            clearCompletions.Add(clear.Tcs);
                            break;
                        case FlushCmd flush:
                            flushCompletions.Add(flush.Tcs);
                            break;
                    }
                }

                tx.Commit();
                Interlocked.Add(ref persistedIntervals, committedIntervals);
                Interlocked.Add(ref persistedGaps, committedGaps);
                Interlocked.Add(ref persistedCheckpoints, committedCheckpoints);

                foreach (var f in flushCompletions)
                {
                    f.TrySetResult(true);
                }
                foreach (var completion in durableCompletions) completion.TrySetResult(true);
                foreach (var result in alertResults) result.Completion.TrySetResult(result.Committed);
                foreach (var (tcs, count) in pruneResults) tcs.TrySetResult(count);
                foreach (var c in clearCompletions) c.TrySetResult(true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                health = StorageHealth.Degraded;
                degradedReason = $"Database write batch failed: {ex.Message}";
                // Fail pending flush tokens
                foreach (var cmd in batch)
                {
                    if (cmd is FlushCmd f) f.Tcs.TrySetException(ex);
                    if (cmd is ConnectivityEventCmd ce) ce.Tcs.TrySetException(ex);
                    if (cmd is AlertCasCmd ac) ac.Tcs.TrySetException(ex);
                    if (cmd is QuotaAlertCmd qa) qa.Tcs.TrySetException(ex);
                    if (cmd is PruneHistoryCmd pr) pr.Tcs.TrySetException(ex);
                    if (cmd is ClearHistoryCmd cl) cl.Tcs.TrySetException(ex);
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

    private static int ExecuteInterval(SqliteConnection conn, SqliteTransaction tx, UsageIntervalRecord r)
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
        return cmd.ExecuteNonQuery();
    }

    private static void ExecuteQuotaEvent(SqliteConnection conn, SqliteTransaction tx, QuotaThresholdEvent r)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO quota_threshold_events(
                event_id, rule_id, rule_revision, cycle_identifier, threshold_bps, crossed_at_utc_ms, effective_usage_bytes)
            VALUES (
                @id, @ruleId, @rev, @cycle, @th, @crossedAt, @usage);
        ";
        cmd.Parameters.AddWithValue("@id", r.EventId);
        cmd.Parameters.AddWithValue("@ruleId", r.RuleId);
        cmd.Parameters.AddWithValue("@rev", r.RuleRevision);
        cmd.Parameters.AddWithValue("@cycle", r.CycleIdentifier);
        cmd.Parameters.AddWithValue("@th", r.ThresholdBps);
        cmd.Parameters.AddWithValue("@crossedAt", r.CrossedAtUtcMs);
        cmd.Parameters.AddWithValue("@usage", r.EffectiveUsageBytes);
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
            sql += " AND (end_utc_ms > @start OR (is_gap = 1 AND start_utc_ms = end_utc_ms AND start_utc_ms >= @start))";
            cmd.Parameters.AddWithValue("@start", startUtcMs.Value);
        }
        if (endUtcMs.HasValue)
        {
            sql += " AND start_utc_ms < @end";
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
        // Enforces PRD §11 & §22: Range-aware byte accumulation using canonical IntegerSplitter
        if (startUtcMs.HasValue && endUtcMs.HasValue && endUtcMs.Value > startUtcMs.Value)
        {
            var range = new HistoricalRange(startUtcMs.Value, endUtcMs.Value);
            var summary = await GetUsageSummaryAsync(range, "UTC", scopeRevisionId, ct: ct).ConfigureAwait(false);
            return (summary.BytesRx, summary.BytesTx);
        }

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

    public async Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetHistoryAvailabilityAsync(CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(start_utc_ms), MAX(end_utc_ms) FROM usage_intervals;";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false) && !reader.IsDBNull(0) && !reader.IsDBNull(1))
        {
            return (reader.GetInt64(0), reader.GetInt64(1));
        }

        // Fallback to sessions if no intervals have been recorded yet
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT MIN(started_at_utc_ms), MAX(COALESCE(ended_at_utc_ms, started_at_utc_ms)) FROM collection_sessions;";
        using var reader2 = await cmd2.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader2.ReadAsync(ct).ConfigureAwait(false) && !reader2.IsDBNull(0) && !reader2.IsDBNull(1))
        {
            return (reader2.GetInt64(0), reader2.GetInt64(1));
        }

        return (null, null);
    }

    public async Task<HistoricalSummary> GetUsageSummaryAsync(
        HistoricalRange range, string reportingTimezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        var sql = @"
            SELECT interval_id, start_utc_ms, end_utc_ms, bytes_rx, bytes_tx, is_gap
            FROM usage_intervals
            WHERE (end_utc_ms > @rangeStart OR
                   (is_gap = 1 AND start_utc_ms = end_utc_ms AND start_utc_ms >= @rangeStart))
              AND start_utc_ms < @rangeEnd
        ";
        cmd.Parameters.AddWithValue("@rangeStart", range.StartUtcMs);
        cmd.Parameters.AddWithValue("@rangeEnd", range.EndUtcMs);

        if (scopeRevisionId is not null)
        {
            sql += " AND scope_revision_id = @rev";
            cmd.Parameters.AddWithValue("@rev", scopeRevisionId);
        }
        if (networkProfileId is not null)
        {
            sql += " AND network_profile_id = @net";
            cmd.Parameters.AddWithValue("@net", networkProfileId);
        }
        sql += " ORDER BY start_utc_ms ASC;";
        cmd.CommandText = sql;

        long totalRx = 0;
        long totalTx = 0;
        long observedDurationMs = 0;
        long missingDurationMs = 0;
        var intervalCount = 0;
        long lastEndMs = long.MinValue;
        long lastObservedEndMs = long.MinValue;
        long lastMissingEndMs = long.MinValue;
        var hasTemporalOverlap = false;

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            intervalCount++;
            var startMs = reader.GetInt64(1);
            var endMs = reader.GetInt64(2);
            var rawRx = reader.GetInt64(3);
            var rawTx = reader.GetInt64(4);
            var isGap = reader.GetInt32(5) == 1;

            // Invariant: Temporal overlap between intervals in the same scope stream indicates corruption
            if (endMs > startMs && startMs < lastEndMs)
            {
                hasTemporalOverlap = true;
            }
            lastEndMs = Math.Max(lastEndMs, endMs);

            if (isGap)
            {
                var gapOverlap = Math.Max(0L,
                    Math.Min(range.EndUtcMs, endMs) - Math.Max(Math.Max(range.StartUtcMs, startMs), lastMissingEndMs));
                missingDurationMs = CheckedIntegerMath.CheckedAdd(missingDurationMs, gapOverlap, "missing duration accumulation");
                lastMissingEndMs = Math.Max(lastMissingEndMs, endMs);
            }
            else
            {
                var validOverlap = Math.Max(0L,
                    Math.Min(range.EndUtcMs, endMs) - Math.Max(Math.Max(range.StartUtcMs, startMs), lastObservedEndMs));
                observedDurationMs = CheckedIntegerMath.CheckedAdd(observedDurationMs, validOverlap, "observed duration accumulation");
                lastObservedEndMs = Math.Max(lastObservedEndMs, endMs);

                var (allocRx, allocTx) = IntegerSplitter.Allocate(startMs, endMs, rawRx, rawTx, range.StartUtcMs, range.EndUtcMs);
                totalRx = CheckedIntegerMath.CheckedAdd(totalRx, allocRx, "historical summary rx");
                totalTx = CheckedIntegerMath.CheckedAdd(totalTx, allocTx, "historical summary tx");
            }
        }

        // A clock-change gap is an event with exact monotonic duration but no
        // trustworthy UTC span. Detect it even when a subrange misses its point
        // marker but intersects the old/new wall-clock anchors.
        var hasClockDiscontinuity = await HasClockDiscontinuityAsync(range, ct).ConfigureAwait(false);

        // Determine completeness
        var (earliestMs, latestMs) = await GetHistoryAvailabilityAsync(ct).ConfigureAwait(false);
        CompletenessState completeness;

        if (health != StorageHealth.Healthy)
        {
            completeness = CompletenessState.StorageDegraded;
        }
        else if (hasClockDiscontinuity)
        {
            completeness = CompletenessState.ClockUncertain;
        }
        else if (hasTemporalOverlap)
        {
            completeness = CompletenessState.StorageDegraded;
        }
        else if (!earliestMs.HasValue)
        {
            completeness = CompletenessState.NoCoverage;
        }
        else if (range.EndUtcMs <= earliestMs.Value)
        {
            completeness = CompletenessState.Unavailable;
        }
        else if (intervalCount == 0)
        {
            completeness = CompletenessState.NoCoverage;
        }
        else if (missingDurationMs == 0 && observedDurationMs >= range.DurationMs)
        {
            completeness = CompletenessState.Complete;
        }
        else
        {
            completeness = CompletenessState.Partial;
        }

        // PRD §11 & §22 & Prompt §14/15: Truthful coverage denominator
        // Denominator must account for unexplained holes in monitoring rather than assuming 100%
        long expectedMonitoredMs = range.DurationMs;
        if (earliestMs.HasValue && latestMs.HasValue)
        {
            var effStart = Math.Max(range.StartUtcMs, earliestMs.Value);
            var effEnd = Math.Min(range.EndUtcMs, latestMs.Value);
            if (effEnd > effStart)
            {
                expectedMonitoredMs = effEnd - effStart;
            }
        }
        var observableTotal = Math.Max(expectedMonitoredMs, observedDurationMs + missingDurationMs);
        observableTotal = Math.Max(1L, observableTotal);
        var coverage = Math.Clamp((double)observedDurationMs / observableTotal, 0.0, 1.0);

        return new HistoricalSummary(
            range,
            reportingTimezoneId,
            totalRx,
            totalTx,
            observedDurationMs,
            missingDurationMs,
            coverage,
            completeness,
            scopeRevisionId,
            networkProfileId,
            IsEstimated: hasClockDiscontinuity);
    }

    private async Task<bool> HasClockDiscontinuityAsync(
        HistoricalRange range, CancellationToken ct)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT g.start_utc_ms,
                   (SELECT next.utc_anchor_ms
                    FROM clock_segments AS prior
                    JOIN clock_segments AS next ON next.session_id = prior.session_id
                    WHERE prior.segment_id = g.clock_segment_id
                      AND next.rowid > prior.rowid
                    ORDER BY next.rowid
                    LIMIT 1) AS next_utc_ms
            FROM usage_intervals AS g
            WHERE g.is_gap = 1 AND g.timing_confidence = 'utc_discontinuous';";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var startMs = reader.GetInt64(0);
            var endMs = reader.IsDBNull(1) ? startMs : reader.GetInt64(1);
            var lowMs = Math.Min(startMs, endMs);
            var highMs = Math.Max(startMs, endMs);
            if (range.Contains(startMs) || range.Contains(endMs) ||
                (lowMs < range.EndUtcMs && highMs > range.StartUtcMs))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(
        HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default)
    {
        if (pageSize <= 0) pageSize = 50;
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        var sql = @"
            SELECT interval_id, source_interval_key, session_id, clock_segment_id, scope_revision_id, network_profile_id,
                   start_utc_ms, end_utc_ms, monotonic_duration_ticks, bytes_rx, bytes_tx, is_coarse, is_gap, gap_reason, timing_confidence
            FROM usage_intervals
            WHERE (end_utc_ms > @rangeStart OR
                   (is_gap = 1 AND start_utc_ms = end_utc_ms AND start_utc_ms >= @rangeStart))
              AND start_utc_ms < @rangeEnd
        ";
        cmd.Parameters.AddWithValue("@rangeStart", range.StartUtcMs);
        cmd.Parameters.AddWithValue("@rangeEnd", range.EndUtcMs);

        if (cursorStartUtcMs.HasValue && !string.IsNullOrEmpty(cursorIntervalId))
        {
            sql += " AND (start_utc_ms > @cTime OR (start_utc_ms = @cTime AND interval_id > @cId))";
            cmd.Parameters.AddWithValue("@cTime", cursorStartUtcMs.Value);
            cmd.Parameters.AddWithValue("@cId", cursorIntervalId);
        }

        sql += " ORDER BY start_utc_ms ASC, interval_id ASC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", pageSize);
        cmd.CommandText = sql;

        var list = new List<UsageIntervalRecord>(pageSize);
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

    public async Task<int> ComputeDailyRollupsAsync(HistoricalRange range, string reportingTimezoneId, CancellationToken ct = default)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(reportingTimezoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var startOffset = DateTimeOffset.FromUnixTimeMilliseconds(range.StartUtcMs);
        var endOffset = DateTimeOffset.FromUnixTimeMilliseconds(range.EndUtcMs);

        var localStart = TimeZoneInfo.ConvertTime(startOffset, tz).Date;
        var localEnd = TimeZoneInfo.ConvertTime(endOffset, tz).Date;

        var count = 0;
        for (var date = localStart; date <= localEnd; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            // Authoritative day range resolution using CalendarRangeResolver
            var dayRange = CalendarRangeResolver.GetDayRange(date, tz);

            if (dayRange.EndUtcMs <= range.StartUtcMs || dayRange.StartUtcMs >= range.EndUtcMs) continue;

            // Query distinct scope and network dimensions for this day to preserve exact provenance
            var dimensions = new List<(string ScopeId, string NetworkProfileId)>();
            using (var connDims = CreateConnection())
            {
                await connDims.OpenAsync(ct).ConfigureAwait(false);
                ConfigurePragmas(connDims);
                using var cmdDims = connDims.CreateCommand();
                cmdDims.CommandText = @"
                    SELECT DISTINCT scope_revision_id, network_profile_id
                    FROM usage_intervals
                    WHERE end_utc_ms > @dayStart AND start_utc_ms < @dayEnd;
                ";
                cmdDims.Parameters.AddWithValue("@dayStart", dayRange.StartUtcMs);
                cmdDims.Parameters.AddWithValue("@dayEnd", dayRange.EndUtcMs);
                using var readerDims = await cmdDims.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await readerDims.ReadAsync(ct).ConfigureAwait(false))
                {
                    dimensions.Add((readerDims.GetString(0), readerDims.GetString(1)));
                }
            }

            if (dimensions.Count == 0)
            {
                dimensions.Add(("canonical_all", "unidentified"));
            }

            foreach (var (scopeId, netId) in dimensions)
            {
                var summary = await GetUsageSummaryAsync(
                    dayRange,
                    reportingTimezoneId,
                    scopeRevisionId: scopeId == "canonical_all" ? null : scopeId,
                    networkProfileId: netId == "unidentified" ? null : netId,
                    ct: ct).ConfigureAwait(false);

                if (summary.TotalBytes > 0 || summary.ObservedDurationMs > 0 || summary.MissingDurationMs > 0)
                {
                    using var conn = CreateConnection();
                    await conn.OpenAsync(ct).ConfigureAwait(false);
                    ConfigurePragmas(conn);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO daily_rollups (
                            start_utc_ms, end_utc_ms, reporting_timezone, calendar_rule_revision, scope_id, network_profile_id,
                            bytes_rx, bytes_tx, valid_duration_ms, gap_duration_ms, peak_rx_interval_id, peak_tx_interval_id
                        ) VALUES (
                            @start, @end, @tz, '1', @scope, @net,
                            @rx, @tx, @validDur, @gapDur, NULL, NULL
                        );
                    ";
                    cmd.Parameters.AddWithValue("@start", dayRange.StartUtcMs);
                    cmd.Parameters.AddWithValue("@end", dayRange.EndUtcMs);
                    cmd.Parameters.AddWithValue("@tz", reportingTimezoneId);
                    cmd.Parameters.AddWithValue("@scope", scopeId);
                    cmd.Parameters.AddWithValue("@net", netId);
                    cmd.Parameters.AddWithValue("@rx", summary.BytesRx);
                    cmd.Parameters.AddWithValue("@tx", summary.BytesTx);
                    cmd.Parameters.AddWithValue("@validDur", summary.ObservedDurationMs);
                    cmd.Parameters.AddWithValue("@gapDur", summary.MissingDurationMs);

                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    count++;
                }
            }
        }
        return count;
    }

    public async Task RecordQuotaThresholdEventAsync(QuotaThresholdEvent record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await EnqueueCommandAsync(new QuotaEventCmd(record), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(
        string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT threshold_bps FROM quota_threshold_events
            WHERE rule_id = @ruleId AND rule_revision = @rev AND cycle_identifier = @cycle
            ORDER BY threshold_bps ASC;
        ";
        cmd.Parameters.AddWithValue("@ruleId", ruleId);
        cmd.Parameters.AddWithValue("@rev", ruleRevision);
        cmd.Parameters.AddWithValue("@cycle", cycleIdentifier);

        var list = new List<int>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(reader.GetInt32(0));
        }
        return list;
    }

    public async Task<IReadOnlyList<ConnectivityEventRecord>> GetConnectivityEventsAsync(long startUtcMs, long endUtcMs, int limit = 500, CancellationToken ct = default)
    {
        if (endUtcMs <= startUtcMs) throw new ArgumentOutOfRangeException(nameof(endUtcMs));
        if (limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_id,event_type,evidence_source,scope_id,network_profile_id,state,observed_at_utc_ms,confirmed_at_utc_ms,certainty,detail FROM connectivity_events WHERE observed_at_utc_ms >= @start AND observed_at_utc_ms < @end ORDER BY observed_at_utc_ms,event_id LIMIT @limit;";
        cmd.Parameters.AddWithValue("@start", startUtcMs);
        cmd.Parameters.AddWithValue("@end", endUtcMs);
        cmd.Parameters.AddWithValue("@limit", limit);
        var events = new List<ConnectivityEventRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            events.Add(new ConnectivityEventRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return events;
    }

    public async Task<AlertIncidentState?> GetAlertStateAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT state_key,version,active_incident_id,active_rule_revision,last_trigger_utc_ms,next_incident_number FROM alert_rule_state WHERE state_key=@key;";
        cmd.Parameters.AddWithValue("@key", key);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new AlertIncidentState(reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    public async Task<IReadOnlyList<AlertEvent>> GetAlertEventsAsync(long startUtcMs, long endUtcMs, int limit = 500, CancellationToken ct = default)
    {
        if (endUtcMs <= startUtcMs) throw new ArgumentOutOfRangeException(nameof(endUtcMs));
        if (limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        ConfigurePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_id,incident_id,rule_id,rule_revision,alert_kind,phase,scope_id,network_id,observed_at_utc_ms,observed_metric,evidence_description,suppress_popup FROM alert_events WHERE observed_at_utc_ms >= @start AND observed_at_utc_ms < @end ORDER BY observed_at_utc_ms,event_id LIMIT @limit;";
        cmd.Parameters.AddWithValue("@start", startUtcMs);
        cmd.Parameters.AddWithValue("@end", endUtcMs);
        cmd.Parameters.AddWithValue("@limit", limit);
        var events = new List<AlertEvent>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            events.Add(new AlertEvent(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), Enum.Parse<AlertKind>(reader.GetString(4)), Enum.Parse<AlertEventPhase>(reader.GetString(5)), reader.GetString(6), reader.GetString(7), reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetDouble(9), reader.GetString(10), reader.GetInt64(11) != 0));
        }
        return events;
    }

    private static void ExecuteConnectivityEvent(SqliteConnection conn, SqliteTransaction tx, ConnectivityEventRecord record)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO connectivity_events(event_id,event_type,evidence_source,scope_id,network_profile_id,state,observed_at_utc_ms,confirmed_at_utc_ms,certainty,detail) VALUES(@id,@type,@source,@scope,@profile,@state,@observed,@confirmed,@certainty,@detail) ON CONFLICT(event_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@id", record.EventId);
        cmd.Parameters.AddWithValue("@type", record.EventType);
        cmd.Parameters.AddWithValue("@source", record.EvidenceSource);
        cmd.Parameters.AddWithValue("@scope", (object?)record.ScopeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@profile", (object?)record.NetworkProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@state", record.State);
        cmd.Parameters.AddWithValue("@observed", record.ObservedAtUtcMs);
        cmd.Parameters.AddWithValue("@confirmed", (object?)record.ConfirmedAtUtcMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@certainty", record.Certainty);
        cmd.Parameters.AddWithValue("@detail", (object?)record.Detail ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static bool ExecuteAlertCas(SqliteConnection conn, SqliteTransaction tx, string key, long expectedVersion, AlertIncidentState nextState, AlertEvent alertEvent)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (expectedVersion == 0)
        {
            cmd.CommandText = "INSERT INTO alert_rule_state(state_key,version,active_incident_id,active_rule_revision,last_trigger_utc_ms,next_incident_number) VALUES(@key,@version,@active,@revision,@last,@next) ON CONFLICT(state_key) DO NOTHING;";
        }
        else
        {
            cmd.CommandText = "UPDATE alert_rule_state SET version=@version,active_incident_id=@active,active_rule_revision=@revision,last_trigger_utc_ms=@last,next_incident_number=@next WHERE state_key=@key AND version=@expected;";
            cmd.Parameters.AddWithValue("@expected", expectedVersion);
        }
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@version", nextState.Version);
        cmd.Parameters.AddWithValue("@active", (object?)nextState.ActiveIncidentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@revision", nextState.ActiveRuleRevision);
        cmd.Parameters.AddWithValue("@last", nextState.LastTriggerUtcMs);
        cmd.Parameters.AddWithValue("@next", nextState.NextIncidentNumber);
        if (cmd.ExecuteNonQuery() == 0) return false;
        if (ExecuteAlertEvent(conn, tx, alertEvent) == 0) throw new InvalidOperationException("Alert CAS committed without a unique event.");
        return true;
    }

    private static int ExecuteAlertEvent(SqliteConnection conn, SqliteTransaction tx, AlertEvent alertEvent)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO alert_events(event_id,incident_id,rule_id,rule_revision,alert_kind,phase,scope_id,network_id,observed_at_utc_ms,observed_metric,evidence_description,suppress_popup) VALUES(@id,@incident,@rule,@revision,@kind,@phase,@scope,@network,@at,@metric,@evidence,@suppress) ON CONFLICT(event_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@id", alertEvent.EventId);
        cmd.Parameters.AddWithValue("@incident", alertEvent.IncidentId);
        cmd.Parameters.AddWithValue("@rule", alertEvent.RuleId);
        cmd.Parameters.AddWithValue("@revision", alertEvent.RuleRevision);
        cmd.Parameters.AddWithValue("@kind", alertEvent.Kind.ToString());
        cmd.Parameters.AddWithValue("@phase", alertEvent.Phase.ToString());
        cmd.Parameters.AddWithValue("@scope", alertEvent.ScopeId);
        cmd.Parameters.AddWithValue("@network", alertEvent.NetworkId);
        cmd.Parameters.AddWithValue("@at", alertEvent.ObservedUtcMs);
        cmd.Parameters.AddWithValue("@metric", (object?)alertEvent.ObservedMetric ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@evidence", alertEvent.EvidenceDescription);
        cmd.Parameters.AddWithValue("@suppress", alertEvent.SuppressPopup ? 1 : 0);
        return cmd.ExecuteNonQuery();
    }

    private static int ExecutePruneHistory(SqliteConnection conn, SqliteTransaction tx, long beforeUtcMs)
    {
        if (beforeUtcMs <= 0) throw new ArgumentOutOfRangeException(nameof(beforeUtcMs), "Prune cutoff timestamp must be positive.");
        var totalDeleted = 0;

        using var cmd1 = conn.CreateCommand();
        cmd1.Transaction = tx;
        cmd1.CommandText = "DELETE FROM usage_intervals WHERE end_utc_ms <= @before;";
        cmd1.Parameters.AddWithValue("@before", beforeUtcMs);
        totalDeleted += cmd1.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.Transaction = tx;
        cmd2.CommandText = "DELETE FROM daily_rollups WHERE end_utc_ms <= @before;";
        cmd2.Parameters.AddWithValue("@before", beforeUtcMs);
        totalDeleted += cmd2.ExecuteNonQuery();

        using var cmd3 = conn.CreateCommand();
        cmd3.Transaction = tx;
        cmd3.CommandText = "DELETE FROM connectivity_events WHERE observed_at_utc_ms <= @before;";
        cmd3.Parameters.AddWithValue("@before", beforeUtcMs);
        totalDeleted += cmd3.ExecuteNonQuery();

        using var cmd4 = conn.CreateCommand();
        cmd4.Transaction = tx;
        cmd4.CommandText = "DELETE FROM alert_events WHERE observed_at_utc_ms <= @before;";
        cmd4.Parameters.AddWithValue("@before", beforeUtcMs);
        totalDeleted += cmd4.ExecuteNonQuery();

        return totalDeleted;
    }

    private static void ExecuteClearHistory(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            DELETE FROM usage_intervals;
            DELETE FROM daily_rollups;
            DELETE FROM connectivity_events;
            DELETE FROM alert_events;
            DELETE FROM alert_rule_state;
            DELETE FROM counter_checkpoints;
            DELETE FROM quota_threshold_events;
        ";
        cmd.ExecuteNonQuery();
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
        if (health == StorageHealth.Healthy) health = StorageHealth.Closed;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
