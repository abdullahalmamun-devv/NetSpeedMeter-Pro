using System;
using Microsoft.Data.Sqlite;

namespace SpeedMeter.Storage;

public sealed class IncompatibleSchemaException : Exception
{
    public IncompatibleSchemaException(string message) : base(message) { }
}

public static class SqliteMigrations
{
    public const int CurrentSchemaVersion = 2;

    public static void ApplyMigrations(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Ensure schema_migrations exists
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY CHECK(version > 0),
                    migration_id TEXT NOT NULL UNIQUE,
                    checksum TEXT NOT NULL,
                    applied_at_utc_ms INTEGER NOT NULL
                ) STRICT;
            ";
            cmd.ExecuteNonQuery();
        }

        var currentVersion = GetLatestAppliedVersion(connection);

        if (currentVersion > CurrentSchemaVersion)
        {
            throw new IncompatibleSchemaException(
                $"Database schema version {currentVersion} is newer than supported version {CurrentSchemaVersion}. Execution on newer schema is not supported.");
        }

        if (currentVersion < 1)
        {
            ApplyMigration1(connection);
        }
        if (currentVersion < 2)
        {
            ApplyMigration2(connection);
        }
    }

    public static int GetLatestAppliedVersion(SqliteConnection connection)
    {
        var migrationVersion = 0;
        try
        {
            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var r1 = cmd1.ExecuteScalar();
            if (r1 is not null && r1 != DBNull.Value) migrationVersion = Convert.ToInt32(r1);
        }
        catch { }

        var userVersion = 0;
        try
        {
            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "PRAGMA user_version;";
            var r2 = cmd2.ExecuteScalar();
            if (r2 is not null && r2 != DBNull.Value) userVersion = Convert.ToInt32(r2);
        }
        catch { }

        return Math.Max(migrationVersion, userVersion);
    }

    private static void ApplyMigration1(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE collection_sessions (
                        session_id TEXT NOT NULL PRIMARY KEY,
                        monotonic_epoch TEXT NOT NULL,
                        started_at_utc_ms INTEGER NOT NULL,
                        ended_at_utc_ms INTEGER,
                        stop_reason TEXT,
                        ended_cleanly INTEGER NOT NULL DEFAULT 0 CHECK(ended_cleanly IN (0,1))
                    ) STRICT;

                    CREATE TABLE clock_segments (
                        segment_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        monotonic_anchor_ticks INTEGER NOT NULL CHECK(monotonic_anchor_ticks >= 0),
                        utc_anchor_ms INTEGER NOT NULL,
                        ticks_per_second INTEGER NOT NULL CHECK(ticks_per_second > 0),
                        timing_confidence TEXT NOT NULL,
                        change_reason TEXT
                    ) STRICT;

                    CREATE TABLE network_profiles (
                        profile_id TEXT NOT NULL PRIMARY KEY,
                        friendly_name TEXT NOT NULL,
                        os_profile_key TEXT,
                        identity_confidence TEXT NOT NULL,
                        created_at_utc_ms INTEGER NOT NULL
                    ) STRICT;

                    INSERT INTO network_profiles VALUES('unidentified','Unidentified network',NULL,'unresolved',0);

                    CREATE TABLE scope_revisions (
                        revision_id TEXT NOT NULL PRIMARY KEY,
                        scope_id TEXT NOT NULL,
                        revision_number INTEGER NOT NULL CHECK(revision_number > 0),
                        scope_mode TEXT NOT NULL,
                        source_method TEXT NOT NULL,
                        source_version TEXT NOT NULL,
                        boundary_definition TEXT NOT NULL,
                        validity TEXT NOT NULL,
                        created_at_utc_ms INTEGER NOT NULL,
                        UNIQUE(scope_id, revision_number)
                    ) STRICT;

                    CREATE TABLE scope_membership (
                        membership_id TEXT NOT NULL PRIMARY KEY,
                        scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
                        interface_id TEXT NOT NULL,
                        observed_incarnation TEXT,
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        starts_at_monotonic_ticks INTEGER NOT NULL CHECK(starts_at_monotonic_ticks >= 0),
                        ends_at_monotonic_ticks INTEGER,
                        CHECK(ends_at_monotonic_ticks IS NULL OR ends_at_monotonic_ticks >= starts_at_monotonic_ticks)
                    ) STRICT;

                    CREATE TABLE counter_checkpoints (
                        checkpoint_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        interface_id TEXT NOT NULL,
                        source_method TEXT NOT NULL,
                        observed_source_epoch TEXT,
                        observed_sequence INTEGER NOT NULL CHECK(observed_sequence >= 0),
                        monotonic_ticks INTEGER NOT NULL CHECK(monotonic_ticks >= 0),
                        observed_at_utc_ms INTEGER NOT NULL,
                        counter_rx BLOB NOT NULL CHECK(length(counter_rx) = 8),
                        counter_tx BLOB NOT NULL CHECK(length(counter_tx) = 8),
                        UNIQUE(session_id, interface_id, source_method, observed_sequence)
                    ) STRICT;

                    CREATE TABLE usage_intervals (
                        interval_id TEXT NOT NULL PRIMARY KEY,
                        source_interval_key TEXT NOT NULL UNIQUE,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        clock_segment_id TEXT NOT NULL REFERENCES clock_segments(segment_id),
                        scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        start_utc_ms INTEGER NOT NULL,
                        end_utc_ms INTEGER NOT NULL,
                        monotonic_duration_ticks INTEGER,
                        bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
                        bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
                        is_coarse INTEGER NOT NULL DEFAULT 0 CHECK(is_coarse IN (0,1)),
                        is_gap INTEGER NOT NULL DEFAULT 0 CHECK(is_gap IN (0,1)),
                        gap_reason TEXT,
                        timing_confidence TEXT NOT NULL,
                        CHECK(monotonic_duration_ticks IS NULL OR monotonic_duration_ticks >= 0),
                        CHECK((is_gap = 0 AND gap_reason IS NULL AND monotonic_duration_ticks IS NOT NULL AND monotonic_duration_ticks > 0)
                           OR (is_gap = 1 AND gap_reason IS NOT NULL AND bytes_rx = 0 AND bytes_tx = 0))
                    ) STRICT;

                    CREATE INDEX idx_intervals_scope_time ON usage_intervals(scope_revision_id, start_utc_ms, end_utc_ms);
                    CREATE INDEX idx_intervals_network_time ON usage_intervals(network_profile_id, start_utc_ms, end_utc_ms);

                    CREATE TABLE daily_rollups (
                        start_utc_ms INTEGER NOT NULL,
                        end_utc_ms INTEGER NOT NULL,
                        reporting_timezone TEXT NOT NULL,
                        calendar_rule_revision TEXT NOT NULL,
                        scope_id TEXT NOT NULL,
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
                        bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
                        valid_duration_ms INTEGER NOT NULL CHECK(valid_duration_ms >= 0),
                        gap_duration_ms INTEGER NOT NULL CHECK(gap_duration_ms >= 0),
                        peak_rx_interval_id TEXT,
                        peak_tx_interval_id TEXT,
                        CHECK(end_utc_ms > start_utc_ms),
                        PRIMARY KEY(start_utc_ms, end_utc_ms, reporting_timezone, calendar_rule_revision, scope_id, network_profile_id)
                    ) STRICT;

                    INSERT INTO schema_migrations (version, migration_id, checksum, applied_at_utc_ms)
                    VALUES (1, 'V1_INITIAL_SCHEMA', '1a8f9b4c', " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + @");
                    PRAGMA user_version = 1;
                ";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void ApplyMigration2(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS quota_threshold_events (
                        event_id TEXT NOT NULL PRIMARY KEY,
                        rule_id TEXT NOT NULL,
                        rule_revision INTEGER NOT NULL CHECK(rule_revision > 0),
                        cycle_identifier TEXT NOT NULL,
                        threshold_bps INTEGER NOT NULL CHECK(threshold_bps > 0),
                        crossed_at_utc_ms INTEGER NOT NULL,
                        effective_usage_bytes INTEGER NOT NULL,
                        UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)
                    ) STRICT;

                    INSERT INTO schema_migrations (version, migration_id, checksum, applied_at_utc_ms)
                    VALUES (2, 'V2_QUOTA_THRESHOLD_EVENTS', '2b9f1c7d', " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + @");

                    PRAGMA user_version = 2;
                ";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
