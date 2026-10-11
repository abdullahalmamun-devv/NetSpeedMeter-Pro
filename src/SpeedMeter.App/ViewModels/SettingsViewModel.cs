using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;

    private string theme = "Dark";
    private bool bits;
    private double interval = 1.0;
    private string selectedTimezoneId = TimeZoneInfo.Local.Id;
    private int billingCycleDay = 1;
    private double widgetOpacity = 0.90;
    private bool widgetTopmost = true;
    private bool widgetClickThrough = false;
    private string? statusMessage;

    public string Theme { get => theme; set => SetProperty(ref theme, value); }
    public bool Bits { get => bits; set => SetProperty(ref bits, value); }
    public double Interval { get => interval; set => SetProperty(ref interval, value); }
    public string SelectedTimezoneId { get => selectedTimezoneId; set => SetProperty(ref selectedTimezoneId, value); }
    public int BillingCycleDay { get => billingCycleDay; set => SetProperty(ref billingCycleDay, Math.Clamp(value, 1, 31)); }
    public double WidgetOpacity { get => widgetOpacity; set => SetProperty(ref widgetOpacity, Math.Clamp(value, 0.2, 1.0)); }
    public bool WidgetTopmost { get => widgetTopmost; set => SetProperty(ref widgetTopmost, value); }
    public bool WidgetClickThrough { get => widgetClickThrough; set => SetProperty(ref widgetClickThrough, value); }
    public string? StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }

    public ObservableCollection<string> AvailableTimezones { get; } = [];

    public ICommand SaveCommand { get; }
    public ICommand ResetDefaultsCommand { get; }

    public SettingsViewModel(ISettingsService settingsService)
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            AvailableTimezones.Add(tz.Id);
        }

        LoadCurrent();

        SaveCommand = new RelayCommand(Save);
        ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
    }

    public void LoadCurrent()
    {
        var s = settingsService.CurrentSettings;
        theme = s.Theme;
        bits = s.DisplayUnits == "Mbps";
        interval = s.SampleIntervalSeconds;
        selectedTimezoneId = s.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        billingCycleDay = s.ActiveQuotaRule?.CycleDay ?? 1;
        widgetOpacity = s.WidgetOpacity;
        widgetTopmost = s.WidgetTopmost;
        widgetClickThrough = s.ClickThrough;

        OnPropertyChanged(string.Empty);
    }

    public void Save()
    {
        try
        {
            var s = settingsService.CurrentSettings;
            s.Theme = theme;
            s.DisplayUnits = bits ? "Mbps" : "MB/s";
            s.SampleIntervalSeconds = interval;
            s.ReportingTimezoneId = selectedTimezoneId;
            s.WidgetOpacity = widgetOpacity;
            s.WidgetTopmost = widgetTopmost;
            s.ClickThrough = widgetClickThrough;

            if (s.ActiveQuotaRule is not null)
            {
                s.ActiveQuotaRule = s.ActiveQuotaRule with { CycleDay = billingCycleDay };
            }

            s.Sanitize();
            settingsService.Save(s);
            StatusMessage = theme == "Dark" 
                ? "Settings saved successfully." 
                : "Settings saved. (Note: Dark theme is active; dynamic theme switching is scheduled for V1.5).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    public void ResetToDefaults()
    {
        try
        {
            var def = new SpeedMeterSettings();
            def.Sanitize();
            settingsService.Save(def);
            LoadCurrent();
            StatusMessage = "Settings reset to defaults.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed: {ex.Message}";
        }
    }
}
