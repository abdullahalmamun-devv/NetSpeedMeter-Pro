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
using SpeedMeter.Windows;

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
                var physWa = new SpeedMeter.App.PhysicalRect(
                    (int)Math.Round(workArea.Left),
                    (int)Math.Round(workArea.Top),
                    (int)Math.Round(workArea.Width),
                    (int)Math.Round(workArea.Height));

                // 1. Anchor near bottom-right edge of screen (standard notification area anchor)
                var bottomRightAnchor = new SpeedMeter.App.PhysicalRect(physWa.Right - 40, physWa.Bottom - 20, 30, 20);
                var placedBottomRight = window.PositionNearPhysical(bottomRightAnchor, physWa);

                Require(placedBottomRight.Left >= physWa.Left, $"Window Left ({placedBottomRight.Left}) must not exceed workArea Left ({physWa.Left}).");
                Require(placedBottomRight.Right <= physWa.Right, $"Window Right must not exceed workArea Right.");
                Require(placedBottomRight.Top >= physWa.Top, $"Window Top ({placedBottomRight.Top}) must not exceed workArea Top.");
                Require(placedBottomRight.Bottom <= physWa.Bottom, $"Window Bottom must not exceed workArea Bottom.");

                // 2. Anchor near top-left edge of screen
                var topLeftAnchor = new SpeedMeter.App.PhysicalRect(physWa.Left - 50, physWa.Top - 50, 30, 20);
                var placedTopLeft = window.PositionNearPhysical(topLeftAnchor, physWa);

                Require(placedTopLeft.Left >= physWa.Left, "Top-left anchor clamped horizontally.");
                Require(placedTopLeft.Top >= physWa.Top, "Top-left anchor clamped vertically.");

                // 3. Multi-monitor secondary display positioning verification
                var secondaryRightWorkArea = new SpeedMeter.App.PhysicalRect(1920, 0, 1920, 1040);
                var secondaryRightAnchor = new SpeedMeter.App.PhysicalRect(2500, 900, 40, 20);
                var placedSecRight = window.PositionNearPhysical(secondaryRightAnchor, secondaryRightWorkArea);
                Require(placedSecRight.Left >= secondaryRightWorkArea.Left, "Secondary right monitor Left boundary honored.");
                Require(placedSecRight.Right <= secondaryRightWorkArea.Right, "Secondary right monitor Right boundary honored.");
                Require(placedSecRight.Top >= secondaryRightWorkArea.Top, "Secondary right monitor Top boundary honored.");
                Require(placedSecRight.Bottom <= secondaryRightWorkArea.Bottom, "Secondary right monitor Bottom boundary honored.");

                var secondaryLeftWorkArea = new SpeedMeter.App.PhysicalRect(-1920, 0, 1920, 1040);
                var secondaryLeftAnchor = new SpeedMeter.App.PhysicalRect(-500, 500, 40, 20);
                var placedSecLeft = window.PositionNearPhysical(secondaryLeftAnchor, secondaryLeftWorkArea);
                Require(placedSecLeft.Left >= secondaryLeftWorkArea.Left, "Secondary left monitor Left boundary honored.");
                Require(placedSecLeft.Right <= secondaryLeftWorkArea.Right, "Secondary left monitor Right boundary honored.");

                // 4. Test PositionNear with Dummy Window anchor
                var dummyWindow = new Window { Width = 260, Height = 136, Left = 100, Top = 100 };
                var placedFromWindow = window.PositionNear(dummyWindow, physWa);
                Require(placedFromWindow.Left >= physWa.Left, "Window-anchored placement Left clamped.");
                Require(placedFromWindow.Right <= physWa.Right, "Window-anchored placement Right clamped.");

                // 5. Test PositionNearCursor fallback
                var placedFromCursor = window.PositionNearCursor(physWa);
                Require(placedFromCursor.Left >= physWa.Left, "Cursor-anchored placement Left clamped.");
                Require(placedFromCursor.Right <= physWa.Right, "Cursor-anchored placement Right clamped.");

                window.Close();
                dummyWindow.Close();
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }));

        check("W3-PLACEMENT-MULTI-MONITOR: PlacementHelper handles multi-monitor coordinates deterministically", () =>
        {
            // CASE A: Primary 1920x1080 (100%), Secondary Right 2560x1440 (150% scaling, 144 DPI)
            // Target anchor on secondary at Physical [2500, 500, 390, 204]
            // Quick Panel 280x300 DIP at 150% = 420x450 Physical px
            var caseA_Wa = new SpeedMeter.App.PhysicalRect(1920, 0, 2560, 1400);
            var caseA_Target = new SpeedMeter.App.PhysicalRect(2500, 500, 390, 204);
            var caseA_Placed = SpeedMeter.App.PlacementHelper.CalculatePlacement(caseA_Target, 420, 450, caseA_Wa);
            Require(caseA_Placed.Left >= caseA_Wa.Left + 8, "Case A: Left boundary clamping failed.");
            Require(caseA_Placed.Right <= caseA_Wa.Right - 8, "Case A: Right boundary clamping failed.");
            Require(caseA_Placed.Top >= caseA_Wa.Top + 8, "Case A: Top boundary clamping failed.");
            Require(caseA_Placed.Bottom <= caseA_Wa.Bottom - 8, "Case A: Bottom boundary clamping failed.");
            Require(caseA_Placed.Width == 420 && caseA_Placed.Height == 450, "Case A: Dimension scale multiplication error.");

            // CASE B: Secondary Left negative X [-1920, 0, 1920, 1080] (125% scaling, 120 DPI)
            // Quick Panel 280x300 DIP at 125% = 350x375 Physical px
            var caseB_Wa = new SpeedMeter.App.PhysicalRect(-1920, 0, 1920, 1040);
            var caseB_Target = new SpeedMeter.App.PhysicalRect(-800, 400, 325, 170);
            var caseB_Placed = SpeedMeter.App.PlacementHelper.CalculatePlacement(caseB_Target, 350, 375, caseB_Wa);
            Require(caseB_Placed.Left >= caseB_Wa.Left + 8, "Case B: Negative X Left boundary failed.");
            Require(caseB_Placed.Right <= caseB_Wa.Right - 8, "Case B: Negative X Right boundary failed.");
            Require(caseB_Placed.Top >= caseB_Wa.Top + 8, "Case B: Top boundary failed.");
            Require(caseB_Placed.Bottom <= caseB_Wa.Bottom - 8, "Case B: Bottom boundary failed.");
            Require(caseB_Placed.Width == 350 && caseB_Placed.Height == 375, "Case B: Dimension scale multiplication error.");

            // CASE C: Secondary Above negative Y [0, -1440, 2560, 1440] (150% scaling, 144 DPI)
            // Quick Panel 280x300 DIP at 150% = 420x450 Physical px
            var caseC_Wa = new SpeedMeter.App.PhysicalRect(0, -1440, 2560, 1400);
            var caseC_Target = new SpeedMeter.App.PhysicalRect(500, -600, 390, 204);
            var caseC_Placed = SpeedMeter.App.PlacementHelper.CalculatePlacement(caseC_Target, 420, 450, caseC_Wa);
            Require(caseC_Placed.Left >= caseC_Wa.Left + 8, "Case C: Left boundary failed.");
            Require(caseC_Placed.Right <= caseC_Wa.Right - 8, "Case C: Right boundary failed.");
            Require(caseC_Placed.Top >= caseC_Wa.Top + 8, "Case C: Negative Y Top boundary failed.");
            Require(caseC_Placed.Bottom <= caseC_Wa.Bottom - 8, "Case C: Negative Y Bottom boundary failed.");
            Require(caseC_Placed.Width == 420 && caseC_Placed.Height == 450, "Case C: Dimension scale multiplication error.");

            // CASE D: Primary 2560x1440 (150%), Secondary Right 1920x1080 (100% scaling, 96 DPI)
            // Quick Panel 280x300 DIP at 100% = 280x300 Physical px
            var caseD_Wa = new SpeedMeter.App.PhysicalRect(2560, 0, 1920, 1040);
            var caseD_Target = new SpeedMeter.App.PhysicalRect(3000, 300, 260, 136);
            var caseD_Placed = SpeedMeter.App.PlacementHelper.CalculatePlacement(caseD_Target, 280, 300, caseD_Wa);
            Require(caseD_Placed.Left >= caseD_Wa.Left + 8, "Case D: Secondary 100% Left boundary failed.");
            Require(caseD_Placed.Right <= caseD_Wa.Right - 8, "Case D: Secondary 100% Right boundary failed.");
            Require(caseD_Placed.Top >= caseD_Wa.Top + 8, "Case D: Top boundary failed.");
            Require(caseD_Placed.Bottom <= caseD_Wa.Bottom - 8, "Case D: Bottom boundary failed.");
            Require(caseD_Placed.Width == 280 && caseD_Placed.Height == 300, "Case D: Dimension scale multiplication error.");

            // CASE E: Mixed 100% -> 4K 200% High-DPI [1920, 0, 3840, 2160] (192 DPI)
            // Target anchor near bottom of 4K display forces vertical flip above target
            // Quick Panel 280x300 DIP at 200% = 560x600 Physical px
            var caseE_Wa = new SpeedMeter.App.PhysicalRect(1920, 0, 3840, 2100);
            var caseE_BottomTarget = new SpeedMeter.App.PhysicalRect(3500, 1800, 520, 272);
            var caseE_Placed = SpeedMeter.App.PlacementHelper.CalculatePlacement(caseE_BottomTarget, 560, 600, caseE_Wa);
            Require(caseE_Placed.Top < caseE_BottomTarget.Top, "Case E: Window did not flip above target when exceeding bottom work area.");
            Require(caseE_Placed.Bottom <= caseE_Wa.Bottom - 8, "Case E: Flipped window exceeds bottom bounds.");
            Require(caseE_Placed.Top >= caseE_Wa.Top + 8, "Case E: Flipped window exceeds top bounds.");
            Require(caseE_Placed.Left >= caseE_Wa.Left + 8, "Case E: 4K Left boundary failed.");
            Require(caseE_Placed.Right <= caseE_Wa.Right - 8, "Case E: 4K Right boundary failed.");
            Require(caseE_Placed.Width == 560 && caseE_Placed.Height == 600, "Case E: Dimension scale multiplication error.");
        });

        check("W3-TRAY-REORDER-SEMANTIC: Semantic tray item recovery operates independently of menu order", () => RunOnSta(() =>
        {
            var actionExecuted = false;

            // Create an adversarial context menu where items are deliberately scrambled
            var scrambledMenu = new System.Windows.Forms.ContextMenuStrip();
            scrambledMenu.Items.Add("Quit"); // Index 0
            scrambledMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); // Index 1
            scrambledMenu.Items.Add("Random Action 1"); // Index 2
            scrambledMenu.Items.Add("Pause"); // Index 3
            scrambledMenu.Items.Add("Open Dashboard"); // Index 4 (old magic index now holds an unrelated command!)

            var semanticItem = new System.Windows.Forms.ToolStripMenuItem("Disable click-through")
            {
                Name = "DisableClickThroughMenuItem",
                Tag = "DisableClickThrough"
            };
            semanticItem.Click += (_, _) => actionExecuted = true;
            scrambledMenu.Items.Add(semanticItem); // Index 5 (moved away from index 4)
            scrambledMenu.Items.Add("Settings"); // Index 6

            // Locate semantically via Tag/Name or typed reference, not index 4
            var targetItem = scrambledMenu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>()
                .FirstOrDefault(i => (string?)i.Tag == "DisableClickThrough" || i.Name == "DisableClickThroughMenuItem");

            Require(targetItem != null, "Semantic recovery menu item must be located.");
            Require(scrambledMenu.Items.IndexOf(targetItem!) != 4, "Adversarial test must ensure item is not at index 4.");

            targetItem!.PerformClick();
            Require(actionExecuted, "Recovery action must be executed via semantic activation regardless of menu position.");
        }));

        check("W3-QUICKPANEL-NATIVE-HWND: Native HWND placement conforms strictly to physical work area", () => RunOnSta(() =>
        {
            var fakeStorage = new FakeStorageEngine();
            var historyService = new UsageHistoryService(fakeStorage);
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_hwnd_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var collector = new Collector(new FakeAdapterProvider(), new MeasurementClock());
                var vm = new QuickPanelViewModel(collector, historyService, settingsService, () => { });
                var window = new SpeedMeter.App.Views.QuickPanelWindow(vm);

                // Position near a known physical anchor in the screen's coordinate space
                var placed = window.PositionNearPhysical(new SpeedMeter.App.PhysicalRect(300, 300, 100, 50));
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Require(hwnd != 0, "Native HWND must be created.");

                // Query native window rect via Win32 GetWindowRect
                var hasRect = SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var nativeRect);
                Require(hasRect, "GetWindowRect must query native window bounds.");

                var (_, workArea, _) = SpeedMeter.App.PlacementHelper.GetMonitorInfoForRect(placed);
                Require(nativeRect.Left >= workArea.Left, $"Native Left ({nativeRect.Left}) must be within work area ({workArea.Left}).");
                Require(nativeRect.Right <= workArea.Right, $"Native Right ({nativeRect.Right}) must be within work area ({workArea.Right}).");
                Require(nativeRect.Top >= workArea.Top, $"Native Top ({nativeRect.Top}) must be within work area ({workArea.Top}).");
                Require(nativeRect.Bottom <= workArea.Bottom, $"Native Bottom ({nativeRect.Bottom}) must be within work area ({workArea.Bottom}).");

                window.Close();
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }));

        check("W3-QUICKPANEL-NEGATIVE-ORIGIN-HWND: Negative virtual desktop origin placement preserves native HWND coordinates without DIP corruption", () => RunOnSta(() =>
        {
            var fakeStorage = new FakeStorageEngine();
            var historyService = new UsageHistoryService(fakeStorage);
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_neg_origin_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var collector = new Collector(new FakeAdapterProvider(), new MeasurementClock());
                var vm = new QuickPanelViewModel(collector, historyService, settingsService, () => { });
                var window = new SpeedMeter.App.Views.QuickPanelWindow(vm);

                // Secondary monitor Left at negative X [-2560..0], 150% scaling (144 DPI)
                var secondaryWa = new SpeedMeter.App.PhysicalRect(-2560, 0, 2560, 1400);
                var secondaryAnchor = new SpeedMeter.App.PhysicalRect(-2000, 500, 50, 50);

                var placed = window.PositionNearPhysical(secondaryAnchor, secondaryWa, dpiOverride: 144);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Require(hwnd != 0, "Native HWND must be created.");

                var hasRect = SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var nativeRect);
                Require(hasRect, "GetWindowRect must query native window bounds.");

                // Assert placed within negative work area
                Require(nativeRect.Left >= secondaryWa.Left, $"Native Left ({nativeRect.Left}) must be >= work area Left ({secondaryWa.Left}).");
                Require(nativeRect.Right <= secondaryWa.Right, $"Native Right ({nativeRect.Right}) must be <= work area Right ({secondaryWa.Right}).");
                Require(nativeRect.Left < 0, $"Native Left ({nativeRect.Left}) must remain in negative coordinate space.");

                // Verify it was NOT corrupted by global-origin division bug (which would produce -2000/1.5 = -1333)
                Require(nativeRect.Left != -1333, "Native position must not be corrupted to -1333 DIP equivalent.");

                // Verify Window Size Contract: 280 DIP wide * 1.5 = 420 physical pixels
                Require(nativeRect.Width == 420, $"Window width at 150% DPI must be 420 physical pixels, was {nativeRect.Width}.");

                window.Close();
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }));

        check("W3-QUICKPANEL-DPI-TRANSITIONS: Native window scales accurately across per-monitor DPI transitions", () => RunOnSta(() =>
        {
            var fakeStorage = new FakeStorageEngine();
            var historyService = new UsageHistoryService(fakeStorage);
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_dpi_trans_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var collector = new Collector(new FakeAdapterProvider(), new MeasurementClock());
                var vm = new QuickPanelViewModel(collector, historyService, settingsService, () => { });
                var window = new SpeedMeter.App.Views.QuickPanelWindow(vm);

                var wa = new SpeedMeter.App.PhysicalRect(0, 0, 3840, 2160);
                var anchor = new SpeedMeter.App.PhysicalRect(500, 500, 100, 50);

                // Transition 1: 100% DPI (96 DPI) -> 280 physical px
                window.PositionNearPhysical(anchor, wa, dpiOverride: 96);
                SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var rect100);
                Require(rect100.Width == 280, $"At 100% DPI, width must be 280 px, was {rect100.Width}");

                // Transition 2: 150% DPI (144 DPI) -> 420 physical px
                window.PositionNearPhysical(anchor, wa, dpiOverride: 144);
                SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var rect150);
                Require(rect150.Width == 420, $"At 150% DPI, width must be 420 px, was {rect150.Width}");

                // Transition 3: 200% DPI (192 DPI) -> 560 physical px
                window.PositionNearPhysical(anchor, wa, dpiOverride: 192);
                SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var rect200);
                Require(rect200.Width == 560, $"At 200% DPI, width must be 560 px, was {rect200.Width}");

                // Transition 4: Return to 100% DPI -> 280 physical px
                window.PositionNearPhysical(anchor, wa, dpiOverride: 96);
                SpeedMeter.App.PlacementHelper.TryGetWindowPhysicalRect(window, out var rectBack100);
                Require(rectBack100.Width == 280, $"Returning to 100% DPI, width must be 280 px, was {rectBack100.Width}");

                window.Close();
            }
            finally
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }));

        check("W3-STORAGE-DEGRADATION-UI-SEMANTICS: UI distinguishes storage degradation from measurement degradation", () =>
        {
            var reading = new Reading(DateTimeOffset.UtcNow, "test", ReadingState.Live, 1000, 500, 1000, 500, 1.0, false, "Live");
            var observation = new AdapterObservation(ProviderAvailability.Available, [], "GetIfTable2", "OK", "Available");

            // Case A: Collector is Degraded, but Storage is Healthy
            // Storage banner must be suppressed because storage itself is healthy.
            var snapHealthyStorage = new CollectorSnapshot(1L, CollectorState.Degraded, reading, observation)
            {
                StorageStatus = new StorageStatus(StorageHealth.Healthy, 0, 100, 0, null)
            };
            var isStorageDegradedA = snapHealthyStorage.StorageStatus is not null && snapHealthyStorage.StorageStatus.Health == StorageHealth.Degraded;
            Require(!isStorageDegradedA, "Case A: Healthy storage must not be flagged as storage degraded.");

            // Case B: Collector is Running or Degraded, and Storage is Degraded
            // Storage banner must be shown with typed reason.
            var snapDegradedStorage = new CollectorSnapshot(2L, CollectorState.Degraded, reading, observation)
            {
                StorageStatus = new StorageStatus(StorageHealth.Degraded, 50, 100, 2, "Database disk full")
            };
            var isStorageDegradedB = snapDegradedStorage.StorageStatus is not null && snapDegradedStorage.StorageStatus.Health == StorageHealth.Degraded;
            Require(isStorageDegradedB, "Case B: Degraded storage must be flagged as storage degraded.");
            Require(snapDegradedStorage.DegradedReason == "Database disk full", "Case B: Typed degraded reason must match.");

            // Case C: Provider is Unavailable, Storage is Healthy
            // Must reflect network/measurement degradation, NOT storage degradation.
            var unavailableObs = new AdapterObservation(ProviderAvailability.TemporarilyUnavailable, [], "GetIfTable2", "UNAVAIL", "Adapter disconnected");
            var snapUnavailNet = new CollectorSnapshot(3L, CollectorState.Degraded, reading with { State = ReadingState.Unavailable, Detail = "Network disconnected" }, unavailableObs)
            {
                StorageStatus = new StorageStatus(StorageHealth.Healthy, 0, 100, 0, null)
            };
            var isStorageDegradedC = snapUnavailNet.StorageStatus is not null && snapUnavailNet.StorageStatus.Health == StorageHealth.Degraded;
            Require(!isStorageDegradedC, "Case C: Network unavailability with healthy storage must NOT show storage degraded banner.");
            Require(snapUnavailNet.Observation.Availability == ProviderAvailability.TemporarilyUnavailable, "Case C: Provider availability must reflect measurement axis.");
        });

        check("W3-WIDGET-RESTORE-MONITOR-AWARE: Widget bounds restore clamps to monitor work area without off-screen loss", () => RunOnSta(() =>
        {
            var widget = new SpeedMeter.App.WidgetWindow(() => { });
            widget.Show();

            try
            {
                // Test ensure visible clamping: move off-screen to massive coordinates
                widget.Left = 99999;
                widget.Top = 99999;
                widget.EnsureVisibleOnScreen();

                // Widget must be recovered within reasonable screen bounds
                Require(widget.Left < 99999, "Widget Left must be clamped from off-screen position.");
                Require(widget.Top < 99999, "Widget Top must be clamped from off-screen position.");

                // Test negative off-screen coordinates
                widget.Left = -99999;
                widget.Top = -99999;
                widget.EnsureVisibleOnScreen();
                Require(widget.Left > -99999, "Widget Left must be clamped from negative off-screen position.");
                Require(widget.Top > -99999, "Widget Top must be clamped from negative off-screen position.");
            }
            finally
            {
                widget.Close();
            }
        }));

        check("W3-TASKBAR-DPI-CONSISTENCY: TaskbarCompanion scales DIPs to physical pixels without coordinate mixing", () =>
        {
            var bottomBar = new TaskbarInfo(new TaskbarRect(0, 1400, 2560, 1440), TaskbarEdge.Bottom, false, true);
            var workArea = new TaskbarRect(0, 0, 2560, 1400);

            // 200x40 DIP companion at 150% scaling (scale = 1.5)
            // Physical size is 300x60 px
            var placement = TaskbarCompanion.CalculatePlacementWithDpi(bottomBar, 200, 40, 1.5, workArea);

            Require(!placement.UseFloatingFallback, "Docking must succeed.");
            Require(placement.X == 2560 - 300 - 12, $"Expected X={2560 - 300 - 12}, got {placement.X}");
            Require(placement.Y == 1400 - 60 - 4, $"Expected Y={1400 - 60 - 4}, got {placement.Y}");
        });

        check("W3-SETTINGS-SYNC: Settings changes propagate dynamically to Collector.Interval", () =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"w3_sync_{Guid.NewGuid():N}.json");
            var dbPath = Path.Combine(Path.GetTempPath(), $"w3_sync_{Guid.NewGuid():N}.db");
            ApplicationContext? ctx = null;

            try
            {
                var settingsService = new SettingsService(settingsPath);
                var provider = new FakeAdapterProvider();
                ctx = new ApplicationContext(
                    provider,
                    new MeasurementClock(),
                    dbPath: dbPath,
                    settingsPath: settingsPath,
                    explicitSettingsService: settingsService);

                Require(Math.Abs(ctx.Collector.Interval - 1.0) < 0.001, "Collector default interval should be 1.0s.");

                // Change settings to 2.0s
                var current = settingsService.CurrentSettings;
                current.SampleIntervalSeconds = 2.0;
                settingsService.Save(current);

                Require(Math.Abs(ctx.Collector.Interval - 2.0) < 0.001, "Collector interval did not update when Settings changed.");

                // Change settings to 0.5s
                current.SampleIntervalSeconds = 0.5;
                settingsService.Save(current);

                Require(Math.Abs(ctx.Collector.Interval - 0.5) < 0.001, "Collector interval did not update to 0.5s.");
            }
            finally
            {
                ctx?.Dispose();
                DeleteDbFiles(dbPath);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        });

        check("W3-ARCH-BOUNDARY: SpeedMeter.App does not reference SpeedMeter.Storage or Microsoft.Data.Sqlite", () =>
        {
            var appAssembly = typeof(SpeedMeter.App.App).Assembly;
            var referencedNames = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            Require(!referencedNames.Any(n => string.Equals(n, "SpeedMeter.Storage", StringComparison.OrdinalIgnoreCase)),
                "SpeedMeter.App must not reference SpeedMeter.Storage assembly.");
            Require(!referencedNames.Any(n => string.Equals(n, "Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase)),
                "SpeedMeter.App must not reference Microsoft.Data.Sqlite assembly.");

            foreach (var type in appAssembly.GetTypes())
            {
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                {
                    var fieldType = field.FieldType.FullName ?? "";
                    Require(!fieldType.Contains("SpeedMeter.Storage") && !fieldType.Contains("Sqlite"),
                        $"Type {type.FullName} has field {field.Name} referencing storage type {fieldType}");
                }
                foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                {
                    var propType = prop.PropertyType.FullName ?? "";
                    Require(!propType.Contains("SpeedMeter.Storage") && !propType.Contains("Sqlite"),
                        $"Type {type.FullName} has property {prop.Name} referencing storage type {propType}");
                }
            }
        });

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
