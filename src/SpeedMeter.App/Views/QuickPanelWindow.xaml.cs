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

    public void PositionNear(Rect target)
    {
        var workArea = SystemParameters.WorkArea;
        var width = Width;
        var height = Height;

        // Default position: below target if fits, otherwise above
        double left = target.Left + (target.Width - width) / 2.0;
        double top = target.Bottom + 6;

        if (top + height > workArea.Bottom)
        {
            top = target.Top - height - 6;
        }

        // Clamp horizontally within work area
        if (left < workArea.Left) left = workArea.Left + 8;
        if (left + width > workArea.Right) left = workArea.Right - width - 8;

        // Clamp vertically
        if (top < workArea.Top) top = workArea.Top + 8;
        if (top + height > workArea.Bottom) top = workArea.Bottom - height - 8;

        Left = left;
        Top = top;
    }
}
