using System;
using System.Threading.Tasks;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly MonitorSession monitor;
    private readonly Action showWidgetAction;
    private readonly Action openQuickPanelAction;

    private string currentDestination = "Overview";
    private string scopeStatusText = "Initializing…";
    private string collectorStateText = "Starting";
    private bool isDegraded;
    private string? degradedReason;
    private bool isPaused;

    public string CurrentDestination
    {
        get => currentDestination;
        set
        {
            if (SetProperty(ref currentDestination, value))
            {
                OnDestinationChanged(value);
            }
        }
    }

    public string ScopeStatusText { get => scopeStatusText; private set => SetProperty(ref scopeStatusText, value); }
    public string CollectorStateText { get => collectorStateText; private set => SetProperty(ref collectorStateText, value); }
    public bool IsDegraded { get => isDegraded; private set => SetProperty(ref isDegraded, value); }
    public string? DegradedReason { get => degradedReason; private set => SetProperty(ref degradedReason, value); }
    public bool IsPaused { get => isPaused; private set => SetProperty(ref isPaused, value); }

    public OverviewViewModel Overview { get; }
    public LiveTrafficViewModel LiveTraffic { get; }
    public HistoryViewModel History { get; }
    public QuotaViewModel Quota { get; }
    public SettingsViewModel Settings { get; }
    public NetworkToolsViewModel NetworkTools { get; }

    public ICommand NavigateCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ShowWidgetCommand { get; }
    public ICommand OpenQuickPanelCommand { get; }

    public MainViewModel(
        MonitorSession monitor,
        Action showWidgetAction,
        Action openQuickPanelAction)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.showWidgetAction = showWidgetAction ?? throw new ArgumentNullException(nameof(showWidgetAction));
        this.openQuickPanelAction = openQuickPanelAction ?? throw new ArgumentNullException(nameof(openQuickPanelAction));

        Overview = new OverviewViewModel(monitor.HistoryService, monitor.QuotaService, monitor.SettingsService, monitor.Collector, monitor.ConnectivityService);
        LiveTraffic = new LiveTrafficViewModel(monitor.LiveGraphService, monitor.Collector);
        History = new HistoryViewModel(monitor.HistoryService, monitor.SettingsService, monitor.ExportService);
        Quota = new QuotaViewModel(monitor.QuotaService, monitor.SettingsService);
        Settings = new SettingsViewModel(monitor.SettingsService, monitor.HistoryService, monitor.ConfigExportImportService);
        NetworkTools = new NetworkToolsViewModel(monitor.NetworkToolsService, monitor.ConnectivityService, monitor.SettingsService, monitor.Collector);

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string dest) CurrentDestination = dest;
        });

        TogglePauseCommand = new RelayCommand(() =>
        {
            monitor.TogglePause();
            IsPaused = monitor.Paused;
        });

        ShowWidgetCommand = new RelayCommand(showWidgetAction);
        OpenQuickPanelCommand = new RelayCommand(openQuickPanelAction);
    }

    public void UpdateSnapshot(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        IsPaused = monitor.Paused;
        CollectorStateText = snapshot.State.ToString();
        IsDegraded = snapshot.State == CollectorState.Degraded;
        DegradedReason = snapshot.DegradedReason;

        var decision = snapshot.ScopeDecision;
        if (decision is not null)
        {
            var mode = decision.Mode == ScopeMode.AutoPrimary ? "AutoPrimary" : "Manual";
            var iface = decision.PrimaryAdapterId is not null ? decision.Summary : "No candidate";
            ScopeStatusText = $"{mode}: {iface}";
        }

        Overview.UpdateSnapshot(snapshot);
        LiveTraffic.UpdateSnapshot(snapshot);
        NetworkTools.UpdateSnapshot(snapshot);
    }

    private void OnDestinationChanged(string destination)
    {
        switch (destination)
        {
            case "Overview":
                _ = Overview.RefreshHistoryAndQuotaAsync();
                break;
            case "History":
                _ = History.LoadPeriodAsync(History.SelectedPeriod);
                break;
            case "Quota":
                _ = Quota.RefreshEvaluationAsync();
                break;
            case "NetworkTools":
                NetworkTools.RefreshNetworkDetails();
                break;
            case "Settings":
                Settings.LoadCurrent();
                break;
        }
    }
}
