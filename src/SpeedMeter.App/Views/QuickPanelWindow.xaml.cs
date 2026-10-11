using System;
using System.Windows;
using System.Windows.Input;
using SpeedMeter.App.ViewModels;

namespace SpeedMeter.App.Views;

public partial class QuickPanelWindow : Window
{
    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        Deactivated += (_, _) =>
        {
            Hide();
        };
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void PositionNear(Rect target, Rect? workAreaOverride = null)
    {
        var workArea = workAreaOverride ?? PlacementHelper.GetWorkAreaForTarget(target);
        var placement = PlacementHelper.CalculatePlacement(target, Width, Height, workArea);
        Left = placement.Left;
        Top = placement.Top;
    }
}
