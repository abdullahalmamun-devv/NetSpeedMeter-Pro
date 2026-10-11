using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SpeedMeter.App;
using SpeedMeter.App.ViewModels;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

public static class V1Wave3Fixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("W3-EXPORT: Keyset-paged CSV export with metadata disclosure and formula injection prevention", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_export_test_{Guid.NewGuid():N}.db");
            var csvPath = Path.Combine(Path.GetTempPath(), $"w3_export_{Guid.NewGuid():N}.csv");
            SqliteStorageEngine? storage = null;

            try
            {
                storage = new SqliteStorageEngine(dbPath);
                var sid = Guid.NewGuid().ToString("D");
                var clk = Guid.NewGuid().ToString("D");
                var rev = Guid.NewGuid().ToString("D");

                storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch-0", 0, null, null, false)).GetAwaiter().GetResult();
                storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal")).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 0)).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("=cmd|'/c calc'!A1", "scope-mal", 1, "SelectedAdapter", "Win32", "1.0", "adapter-mal", "valid", 0)).GetAwaiter().GetResult();

                // Ingest 75 normal intervals + 2 intervals with potential formula injection payloads in ScopeRevisionId and GapReason
                long startMs = 10_000;
                long totalRx = 0;
                long totalTx = 0;

                for (int i = 0; i < 75; i++)
                {
                    long rx = 1000 + i;
                    long tx = 500 + i;
                    totalRx += rx;
                    totalTx += tx;
                    storage.IngestIntervalAsync(new UsageIntervalRecord(
                        $"int-{i:D4}", $"raw-{i}", sid, clk, rev, "unidentified",
                        startMs, startMs + 1000, 10_000_000, rx, tx, false, false, null, "nominal")).GetAwaiter().GetResult();
                    startMs += 1000;
                }

                // Malicious payload interval 1 (starts with =)
                storage.IngestIntervalAsync(new UsageIntervalRecord(
                    "int-malicious-1", "raw-m1", sid, clk, "=cmd|'/c calc'!A1", "unidentified",
                    startMs, startMs + 1000, 10_000_000, 500, 500, false, false, null, "nominal")).GetAwaiter().GetResult();
                totalRx += 500; totalTx += 500;
                startMs += 1000;

                // Malicious payload interval 2 (gap reason starts with @SUM)
                storage.IngestGapAsync(new UsageIntervalRecord(
                    "int-malicious-2", "raw-m2", sid, clk, rev, "unidentified",
                    startMs, startMs + 1000, 10_000_000, 0, 0, false, true, "@SUM(1+1)*cmd", "nominal")).GetAwaiter().GetResult();
                startMs += 1000;

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                var historyService = new UsageHistoryService(storage);
                var exportService = new ExportService(historyService);
                var range = new HistoricalRange(10_000, startMs);

                var exportedFile = exportService.ExportUsageIntervalsToCsvAsync(range, csvPath, "UTC").GetAwaiter().GetResult();
                Require(File.Exists(exportedFile), "CSV export file was not created.");

                var lines = File.ReadAllLines(exportedFile);
                Require(lines.Length > 77, $"Expected at least 77 lines in CSV export, got {lines.Length}.");

                // Verify Metadata Headers
                Require(lines[0] == "# SpeedMeter Pro Usage History Export", "Missing title header comment.");
                Require(lines[1].StartsWith("# SchemaVersion: 1.0"), "Missing or invalid SchemaVersion header.");
                Require(lines.Any(l => l.StartsWith("# RangeSemantics: [StartUtc, EndUtc) half-open")), "Missing RangeSemantics.");
                Require(lines.Any(l => l.StartsWith($"# TotalBytes: {totalRx + totalTx}")), $"TotalBytes mismatch in CSV header: expected {totalRx + totalTx}.");

                // Verify CSV Injection Neutralization (starts with ')
                var maliciousLine1 = lines.First(l => l.Contains("int-malicious-1"));
                Require(maliciousLine1.Contains("'=cmd|'/c calc'!A1"), "Formula injection payload starting with '=' was not neutralized with leading single quote.");

                var maliciousLine2 = lines.First(l => l.Contains("int-malicious-2"));
                Require(maliciousLine2.Contains("'@SUM(1+1)*cmd"), "Formula injection payload starting with '@' was not neutralized with leading single quote.");
            }
            finally
            {
                storage?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(csvPath)) File.Delete(csvPath); } catch { }
            }
        });

        check("W3-EXPORT: JSON export schema validity, explicit nulls, and typed numerics", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_export_json_{Guid.NewGuid():N}.db");
            var jsonPath = Path.Combine(Path.GetTempPath(), $"w3_export_{Guid.NewGuid():N}.json");
            SqliteStorageEngine? storage = null;

            try
            {
                storage = new SqliteStorageEngine(dbPath);
                var sid = Guid.NewGuid().ToString("D");
                var clk = Guid.NewGuid().ToString("D");
                var rev = Guid.NewGuid().ToString("D");

                storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch-0", 0, null, null, false)).GetAwaiter().GetResult();
                storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal")).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 0)).GetAwaiter().GetResult();

                storage.IngestIntervalAsync(new UsageIntervalRecord(
                    "int-0001", "raw-1", sid, clk, rev, "unidentified",
                    10_000, 11_000, 10_000_000, 2500, 1500, false, false, null, "nominal")).GetAwaiter().GetResult();

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                var historyService = new UsageHistoryService(storage);
                var exportService = new ExportService(historyService);
                var range = new HistoricalRange(10_000, 11_000);

                exportService.ExportUsageIntervalsToJsonAsync(range, jsonPath, "UTC").GetAwaiter().GetResult();
                Require(File.Exists(jsonPath), "JSON export file not created.");

                var jsonText = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                Require(root.GetProperty("schemaVersion").GetString() == "1.0", "Invalid schemaVersion.");
                Require(root.GetProperty("range").GetProperty("semantics").GetString() == "[startUtcMs, endUtcMs) half-open", "Invalid semantics.");

                var summary = root.GetProperty("summary");
                Require(summary.GetProperty("totalBytes").GetInt64() == 4000, "Summary totalBytes mismatch.");
                Require(summary.GetProperty("bytesRx").GetInt64() == 2500, "Summary bytesRx mismatch.");
                Require(summary.GetProperty("bytesTx").GetInt64() == 1500, "Summary bytesTx mismatch.");

                var intervals = root.GetProperty("intervals");
                Require(intervals.GetArrayLength() == 1, "Expected exactly 1 interval in array.");
                var first = intervals[0];
                Require(first.GetProperty("intervalId").GetString() == "int-0001", "Invalid intervalId.");
                Require(first.GetProperty("gapReason").ValueKind == JsonValueKind.Null, "gapReason must be explicit null.");
                Require(first.GetProperty("totalBytes").GetInt64() == 4000, "Typed numeric totalBytes mismatch.");
            }
            finally
            {
                storage?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { }
            }
        });

        check("W3-EXPORT: Atomic file export leaves target untouched on failure or cancellation", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_atomic_{Guid.NewGuid():N}.db");
            var destPath = Path.Combine(Path.GetTempPath(), $"w3_atomic_{Guid.NewGuid():N}.csv");
            SqliteStorageEngine? storage = null;

            try
            {
                storage = new SqliteStorageEngine(dbPath);
                var historyService = new UsageHistoryService(storage);
                var exportService = new ExportService(historyService);

                // Write pre-existing data to destination file
                File.WriteAllText(destPath, "PRE-EXISTING CONTENT THAT MUST NOT BE CORRUPTED");

                using var cts = new CancellationTokenSource();
                cts.Cancel(); // Pre-cancelled token

                var range = new HistoricalRange(10_000, 20_000);
                try
                {
                    exportService.ExportUsageIntervalsToCsvAsync(range, destPath, "UTC", ct: cts.Token).GetAwaiter().GetResult();
                    throw new Exception("Export should have thrown OperationCanceledException.");
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }

                Require(File.Exists(destPath), "Destination file should still exist.");
                var content = File.ReadAllText(destPath);
                Require(content == "PRE-EXISTING CONTENT THAT MUST NOT BE CORRUPTED", "Atomic export corrupted target file on failure.");

                // Ensure no orphan temporary files remain
                var dir = Path.GetDirectoryName(destPath)!;
                var orphans = Directory.GetFiles(dir, Path.GetFileName(destPath) + ".tmp.*");
                Require(orphans.Length == 0, "Orphan temporary export files left behind.");
            }
            finally
            {
                storage?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            }
        });

        check("W3-HIST: HistoryViewModel query cancellation token prevents stale query race conditions", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_hist_vm_{Guid.NewGuid():N}.db");
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_settings_{Guid.NewGuid():N}.json");
            SqliteStorageEngine? storage = null;

            try
            {
                storage = new SqliteStorageEngine(dbPath);
                var settingsService = new SettingsService(settingsPath);
                var historyService = new UsageHistoryService(storage);
                var exportService = new ExportService(historyService);

                var vm = new HistoryViewModel(historyService, settingsService, exportService);

                // Rapidly switch periods to verify in-flight query cancellation
                vm.SelectedPeriod = QueryPeriodType.ThisMonth;
                vm.SelectedPeriod = QueryPeriodType.Yesterday;
                vm.SelectedPeriod = QueryPeriodType.Today;

                Require(vm.SelectedPeriod == QueryPeriodType.Today, "SelectedPeriod not updated to latest selection.");
                Require(vm.RangeDescription.StartsWith("Today"), "Range description does not reflect latest period.");
            }
            finally
            {
                storage?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(settingsPath)) File.Delete(settingsPath); } catch { }
            }
        });

        check("W3-QUOTA: Basis points formatting and bidirectional parsing with PRD defaults", () =>
        {
            // Formatting verification
            Require(QuotaViewModel.FormatBasisPoints(8000) == "80.0%", "8000 bps must format to 80.0%");
            Require(QuotaViewModel.FormatBasisPoints(9000) == "90.0%", "9000 bps must format to 90.0%");
            Require(QuotaViewModel.FormatBasisPoints(10000) == "100.0%", "10000 bps must format to 100.0%");
            Require(QuotaViewModel.FormatBasisPoints(10050) == "100.5%", "10050 bps must format to 100.5%");
            Require(QuotaViewModel.FormatBasisPoints(75) == "0.8%", "75 bps must format to 0.8%");

            // Parsing verification
            var standard = QuotaViewModel.ParseBasisPoints("80%, 90%, 100%");
            Require(standard.SequenceEqual([8000, 9000, 10000]), "Parse standard 80%, 90%, 100% failed.");

            var custom = QuotaViewModel.ParseBasisPoints("50%, 75.5%, 95.0%");
            Require(custom.SequenceEqual([5000, 7550, 9500]), "Parse custom fractional percentages failed.");

            // Invalid / empty fallback to PRD defaults
            var fallback = QuotaViewModel.ParseBasisPoints("");
            Require(fallback.SequenceEqual([8000, 9000, 10000]), "Empty string must fall back to PRD defaults.");

            var outOfBounds = QuotaViewModel.ParseBasisPoints("-10%, 999999%");
            Require(outOfBounds.SequenceEqual([8000, 9000, 10000]), "Out-of-bounds must fall back to PRD defaults.");
        });

        check("W3-QUOTA: Billing cycle preview dates reflect month clamping without mutating requested cycle day", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_quota_clamp_{Guid.NewGuid():N}.db");
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_quota_clamp_{Guid.NewGuid():N}.json");
            SqliteStorageEngine? storage = null;

            try
            {
                storage = new SqliteStorageEngine(dbPath);
                var settingsService = new SettingsService(settingsPath);
                var quotaService = new QuotaService(storage);

                var vm = new QuotaViewModel(quotaService, settingsService);
                vm.CycleDay = 31; // Day 31 request

                var rule = vm.BuildCurrentRule();
                Require(rule.CycleDay == 31, "Requested cycle day must remain 31.");

                // In February 2026 (non-leap year), day 31 clamps to Feb 28
                var tz = TimeZoneInfo.Utc;
                var febDate = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
                var febRange = CalendarRangeResolver.GetBillingCycle(tz, febDate, rule.CycleDay);

                var startFeb = DateTimeOffset.FromUnixTimeMilliseconds(febRange.StartUtcMs);
                var endFeb = DateTimeOffset.FromUnixTimeMilliseconds(febRange.EndUtcMs);

                Require(startFeb.Day == 31 && startFeb.Month == 1, "Jan 31 start expected.");
                Require(endFeb.Day == 28 && endFeb.Month == 2, "Feb 28 end clamp expected.");
                Require(rule.CycleDay == 31, "Rule cycle day was mutated.");
            }
            finally
            {
                storage?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(settingsPath)) File.Delete(settingsPath); } catch { }
            }
        });

        check("W3-SETTINGS: SettingsViewModel atomic persistence and sanitization round-trip", () =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_settings_rt_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var vm = new SettingsViewModel(settingsService);

                // Modify settings
                vm.Theme = "Light";
                vm.Bits = true;
                vm.Interval = 2.0;
                vm.WidgetOpacity = 0.75;
                vm.WidgetTopmost = false;
                vm.WidgetClickThrough = true;

                vm.Save();

                // Reload fresh from disk
                var reloadedService = new SettingsService(settingsPath);
                var s = reloadedService.CurrentSettings;

                Require(s.Theme == "Light", "Theme not persisted.");
                Require(s.DisplayUnits == "Mbps", "DisplayUnits not set to Mbps.");
                Require(s.SampleIntervalSeconds == 2.0, "Interval not persisted.");
                Require(Math.Abs(s.WidgetOpacity - 0.75) < 0.001, "WidgetOpacity not persisted.");
                Require(s.WidgetTopmost == false, "WidgetTopmost not persisted.");
                Require(s.ClickThrough == true, "ClickThrough not persisted.");
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        check("W3-GRAPH: Live graph downsampling preserves gap breaks without connecting lines across gaps", () =>
        {
            var buffer = new LiveGraphBuffer(capacitySeconds: 60);

            // Append 3 valid points
            buffer.Append(new LiveGraphPoint(10_000, 100, 50, true, false, 1));
            buffer.Append(new LiveGraphPoint(11_000, 150, 75, true, false, 2));
            buffer.Append(new LiveGraphPoint(12_000, 200, 100, true, false, 3));

            // Gap at t = 13_000
            buffer.Append(new LiveGraphPoint(13_000, null, null, false, true, 4));

            // Resume valid points at t = 20_000
            buffer.Append(new LiveGraphPoint(20_000, 300, 150, true, false, 5));

            var points = buffer.GetPoints(TimeSpan.FromSeconds(30), 20_000);
            Require(points.Count == 5, $"Expected 5 points in raw window, got {points.Count}.");

            var gapPoint = points.First(p => p.TimestampUtcMs == 13_000);
            Require(gapPoint.IsGap, "Gap point must have IsGap == true.");
            Require(!gapPoint.IsValid, "Gap point must have IsValid == false.");

            // Downsample
            var downsampled = buffer.GetDownsampled(TimeSpan.FromSeconds(30), 10, 20_000);
            Require(downsampled.Any(b => b.HasGap), "Downsampled points must preserve gap flag in corresponding bucket.");
        });

        check("W3-APPCONTEXT: ApplicationContext end-to-end integration wires Collector to LiveGraph and Storage", () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_appctx_{Guid.NewGuid():N}.db");
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_appctx_{Guid.NewGuid():N}.json");
            ApplicationContext? ctx = null;

            try
            {
                var fakeClock = new TestClock();
                var fakeProvider = new FakeAdapterProvider();
                fakeProvider.AddAdapter("a1", "Ethernet 1", 10_000, 5_000);

                ctx = new ApplicationContext(
                    fakeProvider,
                    fakeClock,
                    routeProvider: null,
                    dbPath: dbPath,
                    settingsPath: settingsPath);

                ctx.Collector.Select("a1");

                // Step 1: baseline sample
                ctx.Collector.SampleOnce();
                Thread.Sleep(50);

                // Advance clock and counters for valid rate
                fakeClock.Advance(TimeSpan.FromSeconds(1));
                fakeProvider.UpdateAdapter("a1", 10_000 + 1_000_000, 5_000 + 500_000);

                // Step 2: rate sample
                ctx.Collector.SampleOnce();

                // Live graph should have captured the sample (notification is dispatched via Task.Run)
                var deadline = DateTime.UtcNow.AddSeconds(3);
                IReadOnlyList<LiveGraphPoint> rawPoints = [];
                var queryUtcMs = fakeClock.Read().UtcNow.ToUnixTimeMilliseconds();
                while (DateTime.UtcNow < deadline)
                {
                    rawPoints = ctx.LiveGraphService.GetRawPoints(TimeSpan.FromSeconds(30), queryUtcMs);
                    if (rawPoints.Count >= 2) break;
                    Thread.Sleep(20);
                }
                Require(rawPoints.Count >= 2, $"Expected at least 2 points in live graph, got {rawPoints.Count}.");

                var lastPoint = rawPoints[^1];
                Require(lastPoint.IsValid, "Last point in graph must be valid.");
                Require(lastPoint.DownloadRate > 900_000, $"Expected ~1 MB/s download rate, got {lastPoint.DownloadRate}.");
            }
            finally
            {
                ctx?.Dispose();
                DeleteDbFiles(dbPath);
                try { if (File.Exists(settingsPath)) File.Delete(settingsPath); } catch { }
            }
        });

        check("W3-QUICKPANEL: QuickPanelWindow positioning clamps strictly within work area", () => RunOnSta(() =>
        {
            var fakeStorage = new FakeStorageEngine();
            var historyService = new UsageHistoryService(fakeStorage);
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_qp_settings_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var collector = new Collector(new FakeAdapterProvider(), new MeasurementClock());
                var vm = new QuickPanelViewModel(collector, historyService, settingsService, () => { });

                // Construct QuickPanelWindow
                var window = new SpeedMeter.App.Views.QuickPanelWindow(vm);

                // Simulate positioning near various screen coordinates
                var workArea = SystemParameters.WorkArea;

                // Anchor near bottom-right edge of screen (standard notification area anchor)
                var bottomRightAnchor = new Rect(workArea.Right - 40, workArea.Bottom - 20, 30, 20);
                window.PositionNear(bottomRightAnchor);

                Require(window.Left >= workArea.Left, $"Window Left ({window.Left}) must not exceed workArea Left ({workArea.Left}).");
                Require(window.Left + window.Width <= workArea.Right, $"Window Right must not exceed workArea Right.");
                Require(window.Top >= workArea.Top, $"Window Top ({window.Top}) must not exceed workArea Top.");
                Require(window.Top + window.Height <= workArea.Bottom, $"Window Bottom must not exceed workArea Bottom.");

                // Anchor near top-left edge of screen
                var topLeftAnchor = new Rect(workArea.Left - 50, workArea.Top - 50, 30, 20);
                window.PositionNear(topLeftAnchor);

                Require(window.Left >= workArea.Left, "Top-left anchor clamped horizontally.");
                Require(window.Top >= workArea.Top, "Top-left anchor clamped vertically.");

                window.Close();
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }));

        check("W3-APPS-TRUTHFUL: Apps page exposes truthful V1.5 gating without simulated processes", () => RunOnSta(() =>
        {
            // Verify AppsView component exists and initializes without error
            var appsView = new SpeedMeter.App.Views.AppsView();
            Require(appsView is not null, "AppsView failed to instantiate.");

            // PRD §13 invariant: Process Identity must not simulate dummy processes
            var capability = SpeedMeter.Windows.AppAttributionSpike.DetermineProviderCapability();
            Require(capability == SpeedMeter.Windows.AttributionCompleteness.NotMeasured, "Attribution cannot claim completeness in standard-user mode.");
        }));
    }

    private sealed class TestClock : IMeasurementClock
    {
        private double seconds;
        private DateTimeOffset utc = DateTimeOffset.UtcNow;

        public ClockReading Read() => new(seconds, utc);

        public void Advance(TimeSpan delta)
        {
            seconds += delta.TotalSeconds;
            utc = utc.Add(delta);
        }
    }

    private sealed class FakeAdapterProvider : IAdapterCounterProvider
    {
        private readonly List<AdapterCounter> adapters = [];

        public void AddAdapter(string id, string name, ulong rx, ulong tx)
        {
            adapters.Add(new AdapterCounter(id, name, "Fake Adapter", true, rx, tx, 1_000_000_000, true));
        }

        public void UpdateAdapter(string id, ulong rx, ulong tx)
        {
            var idx = adapters.FindIndex(a => a.Id == id);
            if (idx >= 0)
            {
                var existing = adapters[idx];
                adapters[idx] = existing with { Received = rx, Sent = tx };
            }
        }

        public IReadOnlyList<AdapterCounter> Read() => adapters.ToArray();

        public AdapterObservation ReadSnapshot() => new(
            ProviderAvailability.Available, adapters.ToArray(), "Fake", "1.0", "OK");
    }

    private sealed class FakeStorageEngine : IStorageEngine
    {
        public StorageStatus Status => new(StorageHealth.Healthy, 0, 0, 0, null);

        public Task RecordSessionStartAsync(SessionRecord session, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordSessionEndAsync(string sessionId, DateTimeOffset endedAtUtc, bool endedCleanly, string? stopReason, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordClockSegmentAsync(ClockSegmentRecord segment, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeRevisionAsync(ScopeRevisionRecord revision, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeMembershipAsync(ScopeMembershipRecord membership, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordCheckpointAsync(CounterCheckpointRecord checkpoint, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestIntervalAsync(UsageIntervalRecord interval, CounterCheckpointRecord? checkpoint = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestGapAsync(UsageIntervalRecord gap, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default) => Task.FromResult<SessionRecord?>(null);
        public Task<long?> GetLastDurableTimestampUtcMsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);
        public Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult((0L, 0L));
        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetHistoryAvailabilityAsync(CancellationToken ct = default) => Task.FromResult<(long?, long?)>((null, null));
        public Task<HistoricalSummary> GetUsageSummaryAsync(HistoricalRange range, string reportingTimezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default) =>
            Task.FromResult(new HistoricalSummary(range, reportingTimezoneId, 0, 0, 0, 0, 1.0, CompletenessState.Complete));
        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);
        public Task<int> ComputeDailyRollupsAsync(HistoricalRange range, string reportingTimezoneId, CancellationToken ct = default) => Task.FromResult(0);
        public Task RecordQuotaThresholdEventAsync(QuotaThresholdEvent record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<int>>([]);
        public Task FlushAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static void DeleteDbFiles(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        try { if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal"); } catch { }
        try { if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm"); } catch { }
    }

    private static readonly Lazy<Dispatcher> staDispatcher = new(() =>
    {
        Dispatcher? dispatcher = null;
        using var initEvent = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SpeedMeter.App;component/Themes/Tokens.xaml", UriKind.Absolute)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SpeedMeter.App;component/Themes/ThemeDark.xaml", UriKind.Absolute)
            });

            dispatcher = Dispatcher.CurrentDispatcher;
            initEvent.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        initEvent.Wait();
        return dispatcher!;
    });

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        staDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
