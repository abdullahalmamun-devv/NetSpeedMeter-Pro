using System;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Windows;

namespace SpeedMeter.App;

public sealed class MonitorSession : IDisposable
{
    private readonly ApplicationContext context;

    public Collector Collector => context.Collector;
    public CollectorSnapshot Latest => context.Collector.Latest;
    public IUsageHistoryService HistoryService => context.HistoryService;
    public IQuotaService QuotaService => context.QuotaService;
    public ISettingsService SettingsService => context.SettingsService;
    public ILiveGraphService LiveGraphService => context.LiveGraphService;
    public IExportService ExportService => context.ExportService;

    public MonitorSession(string? dbPath = null, string? settingsPath = null)
    {
        var adapterProvider = new AdapterProvider();
        var routeProvider = new RouteProvider();
        context = new ApplicationContext(
            adapterProvider,
            new MeasurementClock(),
            routeProvider,
            dbPath: dbPath,
            settingsPath: settingsPath);
    }

    public double Interval
    {
        get => Collector.Interval;
        set => Collector.Interval = value;
    }

    public bool Paused => Collector.Paused;

    public void Start() => Collector.Start();

    public void Select(string? id) => Collector.Select(id);

    public void SetScope(ScopeMode mode, string? selectedAdapterId = null) => Collector.SetScope(mode, selectedAdapterId);

    public void Reset() => Collector.Reset();

    public void TogglePause() => Collector.TogglePause();

    public void SetSuspended(bool value) => Collector.SetSuspended(value);

    public void Dispose()
    {
        context.Dispose();
    }
}
