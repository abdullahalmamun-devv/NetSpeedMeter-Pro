using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedMeter.Application;

namespace SpeedMeter.App;

internal sealed class CrashRecoveryWindow : Window
{
    private readonly CrashReport report;

    public CrashRecoveryWindow(CrashReport report)
    {
        this.report = report;
        Title = "SpeedMeter Pro recovery";
        Width = 540;
        MinWidth = 440;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "SpeedMeter Pro closed unexpectedly.",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"A local diagnostic report was created (ID {report.Id}). You can review or save it. Nothing was sent.",
            Margin = new Thickness(0, 12, 0, 18),
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new WrapPanel();
        var viewButton = ActionButton("View report", () => ShowPreview(false));
        actions.Children.Add(viewButton);
        actions.Children.Add(ActionButton("Save report", () => ShowPreview(true)));
        actions.Children.Add(ActionButton("Report issue", OpenIssue));
        actions.Children.Add(ActionButton("Dismiss", Close));
        content.Children.Add(actions);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => viewButton.Focus();
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8), MinWidth = 100 };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private void ShowPreview(bool offerSave)
    {
        var preview = new Window
        {
            Title = "Review diagnostic report",
            Owner = this,
            Width = 760,
            Height = 560,
            MinWidth = 480,
            MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        var layout = new DockPanel { Margin = new Thickness(16) };
        var note = new TextBlock
        {
            Text = "Review the redacted report before saving or sharing. No report is sent automatically.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(note, Dock.Top);
        layout.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = ActionButton("Save a copy…", () => SaveReport(preview));
        buttons.Children.Add(save);
        buttons.Children.Add(ActionButton("Close", preview.Close));
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        var text = new TextBox
        {
            Text = CrashReportService.FormatReport(report),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(text, "Redacted diagnostic report preview");
        layout.Children.Add(text);
        preview.Content = layout;
        preview.Show();
        if (offerSave) save.Focus();
    }

    private void SaveReport(Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostic report",
            FileName = $"SpeedMeterPro-crash-{report.Id}.txt",
            Filter = "Text report (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, CrashReportService.FormatReport(report));
            MessageBox.Show(owner, "The report was saved to the location you selected.", "SpeedMeter Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "The report could not be saved. Choose another location and try again.", "SpeedMeter Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenIssue()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CrashReportService.CreateIssueUri(report).AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "The issue page could not be opened. You can still save the report and visit the project’s GitHub Issues page manually.", "SpeedMeter Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
