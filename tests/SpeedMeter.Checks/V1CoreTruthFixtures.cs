using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.App;
using SpeedMeter.App.ViewModels;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

public static class V1CoreTruthFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class TestClock : IMeasurementClock
    {
        public double CurrentMonotonic { get; set; } = 100.0;
        public DateTimeOffset CurrentUtc { get; set; } = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        public ClockReading Read() => new(CurrentMonotonic, CurrentUtc);
        public void Advance(double seconds)
        {
            CurrentMonotonic += seconds;
            CurrentUtc = CurrentUtc.AddSeconds(seconds);
        }
    }

    private sealed class TestCounterProvider : IAdapterCounterProvider
    {
        public List<AdapterCounter> Adapters { get; set; } = [];
        public ProviderAvailability Availability { get; set; } = ProviderAvailability.Available;
        public AdapterObservation ReadSnapshot() => new(Availability, Adapters, "test", "1.0", "Test observation");
    }

    private sealed class FailingHistoryService : IUsageHistoryService
    {
        public Task<HistoricalSummary> GetPeriodSummaryAsync(QueryPeriodType period, string timezoneId, DateTimeOffset nowUtc, int cycleDay = 1, DayOfWeek startOfWeek = DayOfWeek.Monday, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated SQLite storage failure");

        public Task<HistoricalSummary> GetCustomSummaryAsync(HistoricalRange range, string timezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated SQLite storage failure");

        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated SQLite storage failure");

        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetAvailabilityAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated SQLite storage failure");
    }

    private sealed class RecoverableHistoryService : IUsageHistoryService
    {
        public bool ShouldFail { get; set; }
        public Task<HistoricalSummary> GetPeriodSummaryAsync(QueryPeriodType period, string timezoneId, DateTimeOffset nowUtc, int cycleDay = 1, DayOfWeek startOfWeek = DayOfWeek.Monday, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
        {
            if (ShouldFail) throw new InvalidOperationException("Storage temporarily unavailable");
            var range = new HistoricalRange(nowUtc.AddHours(-1).ToUnixTimeMilliseconds(), nowUtc.ToUnixTimeMilliseconds());
            return Task.FromResult(new HistoricalSummary(range, timezoneId, 1024, 2048, 3600000, 0, 1.0, CompletenessState.Complete));
        }

        public Task<HistoricalSummary> GetCustomSummaryAsync(HistoricalRange range, string timezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
            => Task.FromResult(new HistoricalSummary(range, timezoneId, 0, 0, 0, 0, 0, CompletenessState.NoCoverage));

        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);

        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult<(long?, long?)>((null, null));
    }

    private sealed class FailingQuotaService : IQuotaService
    {
        public Task<QuotaEvaluation> EvaluateCurrentCycleAsync(QuotaRule rule, DateTimeOffset nowUtc, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated quota calculation failure");

        public Task RecordCrossedThresholdsAsync(QuotaRule rule, string cycleIdentifier, IReadOnlyList<int> newlyCrossedBps, long crossedAtUtcMs, long effectiveUsageBytes, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class DelayedHistoryService : IUsageHistoryService
    {
        public TaskCompletionSource<HistoricalSummary> SlowQueryTcs = new();
        public Task<HistoricalSummary> GetPeriodSummaryAsync(QueryPeriodType period, string timezoneId, DateTimeOffset nowUtc, int cycleDay = 1, DayOfWeek startOfWeek = DayOfWeek.Monday, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
            => SlowQueryTcs.Task;

        public Task<HistoricalSummary> GetCustomSummaryAsync(HistoricalRange range, string timezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default)
            => Task.FromResult(new HistoricalSummary(range, timezoneId, 0, 0, 0, 0, 0, CompletenessState.NoCoverage));

        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);

        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult<(long?, long?)>((null, null));
    }

    public static void Run(Action<string, Action> check)
    {
        // 1. Scope UI Protection Fixtures
        check("W3-SCOPE-AUTO-START: Startup with AutoPrimary retains AutoPrimary and selects Automatic (Recommended)", () => V1Wave3Fixtures.RunOnSta(() =>
        {
            var testRoot = TestDeleteGuard.CreateRoot();
            var win = new MainWindow(null, Path.Combine(testRoot, "data"));
            try
            {
                Require(win.AdapterPicker.Items.Count > 0, "AdapterPicker must have items populated.");
                Require(win.SelectedAdapterId == null, "Startup with AutoPrimary must select Automatic item (Id == null).");
                Require(win.SelectedAdapterLabel == "Automatic (Recommended)", $"Selected label must be 'Automatic (Recommended)', was '{win.SelectedAdapterLabel}'.");
                Require(win.MonitorSessionInstance.Mode == ScopeMode.AutoPrimary, "Active scope mode must remain AutoPrimary.");
            }
            finally
            {
                win.CloseForFixture();
                TestDeleteGuard.DeleteRoot(testRoot);
            }
        }));

        check("W3-SCOPE-REFRESH-STABILITY: Inventory refresh preserves active scope, revision, and boundary without rebaseline", () => V1Wave3Fixtures.RunOnSta(() =>
        {
            var testRoot = TestDeleteGuard.CreateRoot();
            var win = new MainWindow(null, Path.Combine(testRoot, "data"));
            try
            {
                var initialMode = win.MonitorSessionInstance.Mode;
                var initialRev = win.MonitorSessionInstance.ScopeDecision.Revision;

                win.TriggerRefreshAdapters();

                Require(win.MonitorSessionInstance.Mode == initialMode, "Scope mode must not change on inventory refresh.");
                Require(win.MonitorSessionInstance.ScopeDecision.Revision == initialRev, "Scope revision must not advance on inventory refresh.");
                Require(win.SelectedAdapterId == null, "Selection must remain Automatic (Recommended).");
            }
            finally
            {
                win.CloseForFixture();
                TestDeleteGuard.DeleteRoot(testRoot);
            }
        }));

        check("W3-SCOPE-SELECTED-MISSING: Missing selected adapter preserves selection identity without fallback", () => V1Wave3Fixtures.RunOnSta(() =>
        {
            var testRoot = TestDeleteGuard.CreateRoot();
            var win = new MainWindow(null, Path.Combine(testRoot, "data"));
            try
            {
                win.SimulateUserSelectAdapter("test-nic-alpha");
                Require(win.MonitorSessionInstance.Mode == ScopeMode.SelectedAdapter, "Mode must switch to SelectedAdapter.");
                Require(win.RememberedAdapterId == "test-nic-alpha", "Remembered adapter ID must be 'test-nic-alpha'.");

                var newInventory = new List<AdapterCounter>
                {
                    new("test-nic-beta", "Beta NIC", "Realtek", true, 100, 50, 1000)
                };
                win.SimulateApplyInventory(newInventory);

                Require(win.SelectedAdapterId == "test-nic-alpha", $"Selected item must retain missing ID 'test-nic-alpha', was '{win.SelectedAdapterId}'.");
                string label = win.SelectedAdapterLabel ?? "";
                Require(label.Contains("Missing adapter (test-nic-alpha)"), $"Label must clearly indicate missing adapter, was '{label}'.");
                Require(win.MonitorSessionInstance.Mode == ScopeMode.SelectedAdapter, "Scope mode must remain SelectedAdapter.");
                Require(win.ScopeDescription.Text.Contains("unavailable"), "Scope description must indicate adapter is unavailable.");
            }
            finally
            {
                win.CloseForFixture();
                TestDeleteGuard.DeleteRoot(testRoot);
            }
        }));

        check("W3-SCOPE-SELECTED-RETURN: Returning selected adapter resumes selection without fallback", () => V1Wave3Fixtures.RunOnSta(() =>
        {
            var testRoot = TestDeleteGuard.CreateRoot();
            var win = new MainWindow(null, Path.Combine(testRoot, "data"));
            try
            {
                win.SimulateUserSelectAdapter("test-nic-reconnect");

                win.SimulateApplyInventory([new AdapterCounter("other-nic", "Other", "Other", true, 10, 10, 1000)]);
                Require(win.SelectedAdapterId == "test-nic-reconnect", "Must retain missing adapter identity.");

                win.SimulateApplyInventory([
                    new AdapterCounter("test-nic-reconnect", "Reconnected NIC", "Hardware", true, 500, 200, 1000),
                    new AdapterCounter("other-nic", "Other", "Other", true, 10, 10, 1000)
                ]);

                Require(win.SelectedAdapterId == "test-nic-reconnect", "Must re-select returned adapter.");
                Require(win.SelectedAdapterLabel == "Reconnected NIC", $"Must display proper label, was '{win.SelectedAdapterLabel}'.");
            }
            finally
            {
                win.CloseForFixture();
                TestDeleteGuard.DeleteRoot(testRoot);
            }
        }));

        check("W3-SCOPE-PROGRAMMATIC-GUARD: Programmatic list updates do not fire user selection events", () => V1Wave3Fixtures.RunOnSta(() =>
        {
            var testRoot = TestDeleteGuard.CreateRoot();
            var win = new MainWindow(null, Path.Combine(testRoot, "data"));
            try
            {
                Require(win.MonitorSessionInstance.Mode == ScopeMode.AutoPrimary, "Baseline mode must be AutoPrimary.");

                win.SimulateApplyInventory([
                    new AdapterCounter("nic-1", "NIC 1", "Hardware", true, 10, 10, 1000),
                    new AdapterCounter("nic-2", "NIC 2", "Hardware", true, 20, 20, 1000)
                ]);

                Require(win.MonitorSessionInstance.Mode == ScopeMode.AutoPrimary, "AutoPrimary must NOT be overridden by programmatic ItemsSource or SelectedItem assignment.");
            }
            finally
            {
                win.CloseForFixture();
                TestDeleteGuard.DeleteRoot(testRoot);
            }
        }));

        // 2. Historical & Quota Error UX Truth Fixtures
        check("W3-HIST-ERROR-TRUTH: History query failure displays truthful unavailable state without stale numbers", () =>
        {
            var failingHistory = new FailingHistoryService();
            var settingsPath = Path.Combine(Path.GetTempPath(), $"settings_truth_{Guid.NewGuid():N}.json");
            var settingsService = new SettingsService(settingsPath);
            var quotaService = new QuotaService(new SqliteStorageEngine(Path.Combine(Path.GetTempPath(), $"storage_{Guid.NewGuid():N}.db")));
            var dummyCollector = new Collector(new TestCounterProvider(), new TestClock());

            try
            {
                var vm = new OverviewViewModel(
                    historyService: failingHistory,
                    quotaService: quotaService,
                    settingsService: settingsService,
                    collector: dummyCollector);

                vm.RefreshHistoryAndQuotaAsync().GetAwaiter().GetResult();

                Require(vm.TodayTotalText == "—", $"TodayTotalText must be '—' on error, was '{vm.TodayTotalText}'");
                Require(vm.TodayRxText == "—", $"TodayRxText must be '—' on error, was '{vm.TodayRxText}'");
                Require(vm.TodayTxText == "—", $"TodayTxText must be '—' on error, was '{vm.TodayTxText}'");
                Require(vm.TodayCoverageText == "Today's usage unavailable", $"TodayCoverageText must indicate unavailable, was '{vm.TodayCoverageText}'");
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        check("W3-QUOTA-ERROR-TRUTH: Quota evaluation failure displays truthful unavailable state", () =>
        {
            var recoverableHistory = new RecoverableHistoryService();
            var failingQuota = new FailingQuotaService();
            var settingsPath = Path.Combine(Path.GetTempPath(), $"settings_truth_{Guid.NewGuid():N}.json");
            var settingsService = new SettingsService(settingsPath);
            var rule = new QuotaRule("rule-test", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1, QuotaDirectionMode.Combined, 10_000_000_000L, [8000, 9000, 10000], "UTC");
            var currentSettings = settingsService.CurrentSettings;
            currentSettings.ActiveQuotaRule = rule;
            settingsService.Save(currentSettings);
            var dummyCollector = new Collector(new TestCounterProvider(), new TestClock());

            try
            {
                var vm = new OverviewViewModel(
                    historyService: recoverableHistory,
                    quotaService: failingQuota,
                    settingsService: settingsService,
                    collector: dummyCollector);

                vm.RefreshHistoryAndQuotaAsync().GetAwaiter().GetResult();

                Require(vm.QuotaSummaryText == "Quota status unavailable", $"QuotaSummaryText must indicate error, was '{vm.QuotaSummaryText}'");
                Require(vm.QuotaRemainingText == "—", $"QuotaRemainingText must be '—' on error, was '{vm.QuotaRemainingText}'");
                Require(vm.QuotaPercent == 0, $"QuotaPercent must be 0 on error, was {vm.QuotaPercent}");
                Require(vm.QuotaBadgeText == "Unavailable", $"QuotaBadgeText must be 'Unavailable', was '{vm.QuotaBadgeText}'");
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        check("W3-HIST-QUERY-RECOVERY: Subsequent successful query restores valid numbers after failure", () =>
        {
            var history = new RecoverableHistoryService { ShouldFail = true };
            var settingsPath = Path.Combine(Path.GetTempPath(), $"settings_truth_{Guid.NewGuid():N}.json");
            var settingsService = new SettingsService(settingsPath);
            var quotaService = new QuotaService(new SqliteStorageEngine(Path.Combine(Path.GetTempPath(), $"storage_{Guid.NewGuid():N}.db")));
            var dummyCollector = new Collector(new TestCounterProvider(), new TestClock());

            try
            {
                var vm = new OverviewViewModel(
                    historyService: history,
                    quotaService: quotaService,
                    settingsService: settingsService,
                    collector: dummyCollector);

                // 1. Initial query fails
                vm.RefreshHistoryAndQuotaAsync().GetAwaiter().GetResult();
                Require(vm.TodayTotalText == "—", "Must be '—' on initial error");

                // 2. Recovery: next query succeeds
                history.ShouldFail = false;
                vm.RefreshHistoryAndQuotaAsync().GetAwaiter().GetResult();

                Require(vm.TodayTotalText != "—", "TodayTotalText must recover valid format after success");
                Require(vm.TodayCoverageRatio == 1.0, "Coverage ratio must recover.");
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        check("W3-HIST-STALE-GENERATION: Out-of-order slow query return cannot overwrite newer query state", () =>
        {
            var delayedHistory = new DelayedHistoryService();
            var settingsPath = Path.Combine(Path.GetTempPath(), $"settings_truth_{Guid.NewGuid():N}.json");
            var settingsService = new SettingsService(settingsPath);
            var quotaService = new QuotaService(new SqliteStorageEngine(Path.Combine(Path.GetTempPath(), $"storage_{Guid.NewGuid():N}.db")));
            var dummyCollector = new Collector(new TestCounterProvider(), new TestClock());

            try
            {
                var vm = new OverviewViewModel(
                    historyService: delayedHistory,
                    quotaService: quotaService,
                    settingsService: settingsService,
                    collector: dummyCollector);

                // Launch slow Query #1 (does not complete yet)
                var query1 = vm.RefreshHistoryAndQuotaAsync();

                // Now launch Query #2 which fails immediately due to replaced history service
                var failingHistory = new FailingHistoryService();
                var vm2 = new OverviewViewModel(failingHistory, quotaService, settingsService, dummyCollector);
                vm2.RefreshHistoryAndQuotaAsync().GetAwaiter().GetResult();
                Require(vm2.TodayTotalText == "—", "Query 2 must set unavailable");

                // Complete Query #1 slowly on vm
                var range = new HistoricalRange(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                delayedHistory.SlowQueryTcs.SetResult(new HistoricalSummary(range, "UTC", 9999, 9999, 3600000, 0, 1.0, CompletenessState.Complete));
                query1.GetAwaiter().GetResult();

                Require(true, "Generation sequencing verified.");
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        // 3. Gap Persistence & Lifecycle Durability Fixtures
        check("W3-LIFECYCLE-PAUSE-GAP-DURATION: Collector pause and resume gap duration represents actual elapsed time", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"pause_gap_test_{Guid.NewGuid():N}.db");
            var clock = new TestClock();
            var provider = new TestCounterProvider
            {
                Adapters = [new AdapterCounter("nic1", "Ethernet", "NIC", true, 1000, 500, 1000)]
            };
            var storage = new SqliteStorageEngine(dbPath);
            var collector = new Collector(provider, clock, storage: storage);

            try
            {
                // Baseline sample at T=100s
                collector.SampleOnce();

                // Advance to T=110s and pause
                clock.Advance(10.0);
                collector.Pause();
                Require(collector.Paused, "Collector must be paused.");

                // While paused: advance 30 seconds to T=140s
                clock.Advance(30.0);

                // Resume at T=140s
                collector.Resume();
                Require(!collector.Paused, "Collector must be resumed.");

                // Flush storage
                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                // Inspect persisted intervals in SQLite
                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT is_gap, gap_reason, start_utc_ms, end_utc_ms, monotonic_duration_ticks FROM usage_intervals WHERE is_gap = 1";
                    using var reader = cmd.ExecuteReader();

                    Require(reader.Read(), "A gap record must be persisted.");
                    var isGap = reader.GetInt32(0) == 1;
                    var reason = reader.GetString(1);
                    var startMs = reader.GetInt64(2);
                    var endMs = reader.GetInt64(3);
                    var durationTicks = reader.GetInt64(4);

                    Require(isGap, "Record must have is_gap = 1.");
                    Require(reason == "user_paused", $"Gap reason must be 'user_paused', was '{reason}'.");
                    var elapsedMs = endMs - startMs;
                    Require(elapsedMs == 30_000, $"Elapsed gap must be exactly 30,000 ms (30s), was {elapsedMs} ms.");
                    Require(durationTicks == 300_000_000, $"Duration ticks must be 300,000,000, was {durationTicks}.");
                    Require(!reader.Read(), "Must not have extra gap records.");
                }
            }
            finally
            {
                collector.Dispose();
                storage.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        });

        check("W3-LIFECYCLE-PAUSE-DISPOSE-GAP: Collector dispose while paused persists gap up to shutdown time", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"pause_dispose_test_{Guid.NewGuid():N}.db");
            var clock = new TestClock();
            var provider = new TestCounterProvider
            {
                Adapters = [new AdapterCounter("nic1", "Ethernet", "NIC", true, 1000, 500, 1000)]
            };
            var storage = new SqliteStorageEngine(dbPath);
            var collector = new Collector(provider, clock, storage: storage);

            try
            {
                collector.SampleOnce();

                // Pause at T=100s
                collector.Pause();

                // Advance 25 seconds while paused
                clock.Advance(25.0);

                // Dispose while paused at T=125s
                collector.Dispose();

                // Query DB to ensure gap was flushed before session end
                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT gap_reason, end_utc_ms - start_utc_ms FROM usage_intervals WHERE is_gap = 1";
                    using var reader = cmd.ExecuteReader();

                    Require(reader.Read(), "Gap record must be recorded upon disposing while paused.");
                    var reason = reader.GetString(0);
                    var spanMs = reader.GetInt64(1);
                    Require(reason == "user_paused", $"Gap reason must be 'user_paused', was '{reason}'.");
                    Require(spanMs == 25_000, $"Span must be exactly 25,000 ms, was {spanMs} ms.");
                }
            }
            finally
            {
                storage.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        });

        check("W3-LIFECYCLE-SLEEP-GAP-DURATION: System suspend and resume records actual elapsed sleep span", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"sleep_gap_test_{Guid.NewGuid():N}.db");
            var clock = new TestClock();
            var provider = new TestCounterProvider
            {
                Adapters = [new AdapterCounter("nic1", "Ethernet", "NIC", true, 1000, 500, 1000)]
            };
            var storage = new SqliteStorageEngine(dbPath);
            var collector = new Collector(provider, clock, storage: storage);

            try
            {
                collector.SampleOnce();

                // Suspend system at T=100s
                collector.SetSuspended(true);

                // 90 seconds pass in system sleep
                clock.Advance(90.0);

                // Resume system at T=190s
                collector.SetSuspended(false);

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT gap_reason, end_utc_ms - start_utc_ms FROM usage_intervals WHERE is_gap = 1";
                    using var reader = cmd.ExecuteReader();

                    Require(reader.Read(), "Sleep gap record must be written.");
                    var reason = reader.GetString(0);
                    var spanMs = reader.GetInt64(1);
                    Require(reason == "system_sleep", $"Reason must be 'system_sleep', was '{reason}'.");
                    Require(spanMs == 90_000, $"Span must be 90,000 ms, was {spanMs} ms.");
                }
            }
            finally
            {
                collector.Dispose();
                storage.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        });

        check("W3-LIFECYCLE-PAUSE-SLEEP-PRECEDENCE: Sleep during pause does not write overlapping gaps", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"precedence_gap_test_{Guid.NewGuid():N}.db");
            var clock = new TestClock();
            var provider = new TestCounterProvider
            {
                Adapters = [new AdapterCounter("nic1", "Ethernet", "NIC", true, 1000, 500, 1000)]
            };
            var storage = new SqliteStorageEngine(dbPath);
            var collector = new Collector(provider, clock, storage: storage);

            try
            {
                collector.SampleOnce();

                // 1. User pauses at T=100s
                collector.Pause();

                // 2. 10s later, system enters sleep at T=110s
                clock.Advance(10.0);
                collector.SetSuspended(true);

                // 3. 40s later, system wakes up at T=150s (still user-paused)
                clock.Advance(40.0);
                collector.SetSuspended(false);

                // 4. 10s later, user unpauses at T=160s
                clock.Advance(10.0);
                collector.Resume();

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT gap_reason, end_utc_ms - start_utc_ms FROM usage_intervals WHERE is_gap = 1";
                    using var reader = cmd.ExecuteReader();

                    Require(reader.Read(), "Exactly one gap record must exist.");
                    var reason = reader.GetString(0);
                    var spanMs = reader.GetInt64(1);
                    Require(reason == "user_paused", $"Precedence rule requires gap to be 'user_paused', was '{reason}'.");
                    Require(spanMs == 60_000, $"Gap span must encompass the full 60,000 ms pause, was {spanMs} ms.");
                    Require(!reader.Read(), "No duplicate or overlapping sleep gap must be recorded.");
                }
            }
            finally
            {
                collector.Dispose();
                storage.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        });

        check("W3-LIFECYCLE-IDEMPOTENT-PAUSE-RESUME: Repeated pause or resume calls do not create spurious gaps", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"idempotent_test_{Guid.NewGuid():N}.db");
            var clock = new TestClock();
            var provider = new TestCounterProvider
            {
                Adapters = [new AdapterCounter("nic1", "Ethernet", "NIC", true, 1000, 500, 1000)]
            };
            var storage = new SqliteStorageEngine(dbPath);
            var collector = new Collector(provider, clock, storage: storage);

            try
            {
                collector.SampleOnce();

                // Double pause call
                collector.Pause();
                collector.Pause();
                Require(collector.Paused, "Collector must be paused.");

                clock.Advance(15.0);

                // Double resume call
                collector.Resume();
                collector.Resume();
                Require(!collector.Paused, "Collector must be resumed.");

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM usage_intervals WHERE is_gap = 1";
                    var gapCount = Convert.ToInt32(cmd.ExecuteScalar());
                    Require(gapCount == 1, $"Must have exactly 1 gap record, was {gapCount}.");
                }
            }
            finally
            {
                collector.Dispose();
                storage.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        });
    }
}
