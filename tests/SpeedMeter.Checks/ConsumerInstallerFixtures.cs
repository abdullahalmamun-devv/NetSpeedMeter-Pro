using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedMeter.Installer;

namespace SpeedMeter.Checks;

public static class ConsumerInstallerFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("CONS-INSTALL consumer setup shows per-user path and optional choices", () => OnSta(() =>
        {
            var window = new InstallerWindow(false, null, null, null, false, false, false);
            var controls = Descendants(window).ToArray();
            var labels = controls.OfType<TextBlock>().Select(x => x.Text).ToArray();
            var choices = controls.OfType<CheckBox>().ToArray();
            Require(labels.Any(x => x.Contains(InstallerEngine.GetDefaultInstallDirectory(), StringComparison.Ordinal)),
                "Consumer setup did not show the per-user installation path.");
            var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedMeter Pro.lnk");
            var existingStartup = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "SpeedMeterPro", null) is not null;
            Require(choices.Any(x => x.Content?.ToString() == "Create a desktop shortcut" && x.IsChecked == File.Exists(desktopShortcut)),
                "Desktop shortcut choice must reflect the current installation state.");
            Require(choices.Any(x => x.Content?.ToString() == "Start SpeedMeter Pro with Windows" && x.IsChecked == existingStartup),
                "Windows startup choice must reflect the current installation state.");
            window.Close();
        }));

        check("CONS-INSTALL uninstall defaults to retaining data and states location", () => OnSta(() =>
        {
            var window = new InstallerWindow(true, null, null, null, false, false, false);
            var controls = Descendants(window).ToArray();
            var purge = controls.OfType<CheckBox>().Single(x => x.Content?.ToString()?.Contains("delete my usage history") == true);
            Require(purge.IsChecked == false, "Uninstall must default to keeping user data.");
            Require(controls.OfType<TextBlock>().Any(x => x.Text.Contains(InstallerEngine.GetDefaultUserDataDirectory(), StringComparison.Ordinal)),
                "Uninstaller did not disclose the retained data path.");
            window.Close();
        }));

        check("CONS-INSTALL body scrolls while actions remain outside scroll region", () => OnSta(() =>
        {
            var window = new InstallerWindow(false, null, null, null, false, false, false);
            var root = (DockPanel)window.Content;
            var scroll = root.Children.OfType<ScrollViewer>().Single();
            var footer = root.Children.OfType<WrapPanel>().Single();
            Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Setup body must scroll when text scaling reduces available height.");
            Require(DockPanel.GetDock(footer) == Dock.Bottom,
                "Setup actions must remain outside the scrolling body.");
            Require(window.ResizeMode == ResizeMode.CanResize && window.MaxHeight <= SystemParameters.WorkArea.Height,
                "Setup window must be resizable and bounded to the desktop work area.");
            window.Close();
        }));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Installer WPF fixture failed.", failure);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
