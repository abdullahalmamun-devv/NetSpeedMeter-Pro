using System;
using System.IO;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Application;

public sealed class ApplicationContext : IDisposable, IAsyncDisposable
{
    private readonly IStorageEngine storage;
    private readonly Collector collector;
    private readonly IUsageHistoryService historyService;
    private readonly IQuotaService quotaService;
    private readonly ISettingsService settingsService;
    private readonly ILiveGraphService liveGraphService;
    private readonly IExportService exportService;
    private bool disposed;

    public Collector Collector => collector;
    public IStorageEngine Storage => storage;
    public IUsageHistoryService HistoryService => historyService;
    public IQuotaService QuotaService => quotaService;
    public ISettingsService SettingsService => settingsService;
    public ILiveGraphService LiveGraphService => liveGraphService;
    public IExportService ExportService => exportService;

    public static string GetDefaultDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "SpeedMeterPro");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public ApplicationContext(
        IAdapterCounterProvider provider,
        IMeasurementClock? clock = null,
        IRouteProvider? routeProvider = null,
        string? dbPath = null,
        string? settingsPath = null,
        IStorageEngine? explicitStorage = null,
        ISettingsService? explicitSettingsService = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var dataDir = GetDefaultDataDirectory();
        var effectiveDbPath = dbPath ?? Path.Combine(dataDir, "speedmeter.db");
        var effectiveSettingsPath = settingsPath ?? Path.Combine(dataDir, "settings.json");

        this.storage = explicitStorage ?? new SqliteStorageEngine(effectiveDbPath);
        this.settingsService = explicitSettingsService ?? new SettingsService(effectiveSettingsPath);
        this.historyService = new UsageHistoryService(this.storage);
        this.quotaService = new QuotaService(this.storage);
        this.liveGraphService = new LiveGraphService(capacitySeconds: 3600);
        this.exportService = new ExportService(this.historyService);

        var effectiveClock = clock ?? new MeasurementClock();
        this.collector = new Collector(provider, effectiveClock, routeProvider, this.storage);

        // Feed live graph buffer on every collector tick
        this.collector.Updated += OnCollectorUpdated;
    }

    private void OnCollectorUpdated(CollectorSnapshot snapshot)
    {
        var reading = snapshot.Reading;
        var isGap = snapshot.State is CollectorState.Paused or CollectorState.Suspended or CollectorState.Failed;
        var isValid = reading.State == ReadingState.Live && reading.Download.HasValue && reading.Upload.HasValue;

        liveGraphService.RecordSample(
            reading.Timestamp.ToUnixTimeMilliseconds(),
            reading.Download,
            reading.Upload,
            isValid,
            isGap,
            snapshot.Sequence);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        collector.Updated -= OnCollectorUpdated;
        collector.Dispose();
        storage.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        collector.Updated -= OnCollectorUpdated;
        collector.Dispose();
        await storage.DisposeAsync().ConfigureAwait(false);
    }
}
