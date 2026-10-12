using System;
using System.Windows;
using System.Windows.Controls;
using SpeedMeter.Core;

namespace SpeedMeter.App.Views;

public partial class FirstRunWindow : Window
{
    private readonly SpeedMeterSettings settings;
    private readonly Action<SpeedMeterSettings> onCompleted;

    public FirstRunWindow(SpeedMeterSettings settings, Action<SpeedMeterSettings> onCompleted)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));

        InitializeComponent();

        // Bound the resizable introduction to the current work area at high DPI and on small screens.
        var availableWidth = Math.Max(1, SystemParameters.WorkArea.Width - 24);
        var availableHeight = Math.Max(1, SystemParameters.WorkArea.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);

        EnableChecksCheckBox.IsChecked = settings.ConnectionChecksEnabled;
        AutoUpdateCheckBox.IsChecked = settings.AutoUpdateCheckEnabled;
        ThemePicker.SelectedIndex = settings.Theme switch
        {
            "Light" => 1,
            "System" => 2,
            _ => 0
        };
        UnitsPicker.SelectedIndex = settings.DisplayUnits == "MB/s" ? 1 : 0;
    }

    private void GetStartedClick(object sender, RoutedEventArgs e)
    {
        settings.FirstRunCompleted = true;
        settings.ConnectionChecksEnabled = EnableChecksCheckBox.IsChecked == true;
        settings.AutoUpdateCheckEnabled = AutoUpdateCheckBox.IsChecked == true;

        settings.Theme = ThemePicker.SelectedIndex switch
        {
            1 => "Light",
            2 => "System",
            _ => "Dark"
        };

        settings.DisplayUnits = UnitsPicker.SelectedIndex == 1 ? "MB/s" : "Mbps";

        onCompleted(settings);
        Close();
    }
}
