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
    private readonly IConfigExportImportService configExportImportService;
    private readonly AlertService alertService;
    private readonly ConnectivityService connectivityService;
    private bool disposed;

    public Collector Collector => collector;
    public CollectorShutdownResult? ShutdownResult => collector.ShutdownResult;
    public IStorageEngine Storage => storage;
    public IUsageHistoryService HistoryService => historyService;
    public IQuotaService QuotaService => quotaService;
    public ISettingsService SettingsService => settingsService;
    public ILiveGraphService LiveGraphService => liveGraphService;
    public IExportService ExportService => exportService;
    public IConfigExportImportService ConfigExportImportService => configExportImportService;
    public AlertService AlertService => alertService;
    public ConnectivityService ConnectivityService => connectivityService;

    public static string GetDefaultDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "SpeedMeterPro");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public ApplicationContext(
        IAdapterCounterProvider provider,
        IMeasurementClock? clock,
        IRouteProvider? routeProvider,
        string? dbPath,
        string? settingsPath)
        : this(provider, clock, routeProvider, dbPath, settingsPath, null, null)
    {
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
        this.configExportImportService = new ConfigExportImportService();

        IAlertStateStore alertStore = this.storage is SqliteStorageEngine sqlite
            ? new StorageAlertStateStore(sqlite)
            : new InMemoryAlertStateStore();
        this.alertService = new AlertService(alertStore);

        var endpoints = new[]
        {
            new ConnectivityEndpoint(
                "msft-ncsi",
                "Microsoft NCSI",
                ProbeProtocol.Https,
                "https://www.msftconnecttest.com/connecttest.txt",
                200,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(30),
                "Microsoft Windows Network Connectivity Status Indicator service")
        };
        var registry = new ConnectivityEndpointRegistry(endpoints);
        this.connectivityService = new ConnectivityService(registry, new HttpsConnectivityProbeProvider());

        var effectiveClock = clock ?? new MeasurementClock();
        this.collector = new Collector(provider, effectiveClock, routeProvider, this.storage);

        // Synchronize collector interval and connectivity with persisted settings
        var initialSettings = this.settingsService.CurrentSettings;
        if (initialSettings.SampleIntervalSeconds > 0)
        {
            this.collector.Interval = initialSettings.SampleIntervalSeconds;
        }
        this.connectivityService.SetConfiguration(
            initialSettings.ConnectionChecksEnabled,
            initialSettings.MeteredProbesAllowed);

        this.settingsService.SettingsChanged += OnSettingsChanged;

        // Feed live graph buffer on every collector tick
        this.collector.Updated += OnCollectorUpdated;
    }

    private void OnSettingsChanged(SpeedMeterSettings settings)
    {
        if (settings.SampleIntervalSeconds > 0)
        {
            collector.Interval = settings.SampleIntervalSeconds;
        }
        connectivityService.SetConfiguration(
            settings.ConnectionChecksEnabled,
            settings.MeteredProbesAllowed);
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

        settingsService.SettingsChanged -= OnSettingsChanged;
        collector.Updated -= OnCollectorUpdated;
        connectivityService.Dispose();
        collector.Dispose();
        storage.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        settingsService.SettingsChanged -= OnSettingsChanged;
        collector.Updated -= OnCollectorUpdated;
        connectivityService.Dispose();
        collector.Dispose();
        await storage.DisposeAsync().ConfigureAwait(false);
    }
}
