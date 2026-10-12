using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SpeedMeter.Installer;

/// <summary>The consumer shell around the same transactional engine used by silent deployments.</summary>
public sealed class InstallerWindow : Window
{
    private readonly bool _uninstall;
    private readonly string? _targetDir;
    private readonly string? _payloadPath;
    private readonly Stream? _payloadStream;
    private readonly bool _noLaunch;
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"SpeedMeterPro-Setup-{Guid.NewGuid():N}.log");
    private readonly CheckBox _desktop = new() { Content = "Create a desktop shortcut", Margin = new Thickness(0, 12, 0, 0) };
    private readonly CheckBox _startup = new() { Content = "Start SpeedMeter Pro with Windows", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _purge = new() { Content = "Also delete my usage history, settings and local data", Margin = new Thickness(0, 14, 0, 0) };
    private readonly CheckBox _launch = new() { Content = "Launch SpeedMeter Pro", IsChecked = true, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) };
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 22, 28, 22) };
    private readonly Button _primary = new() { MinWidth = 112, Padding = new Thickness(18, 8, 18, 8), IsDefault = true };
    private readonly Button _secondary = new() { Content = "Cancel", MinWidth = 100, Padding = new Thickness(18, 8, 18, 8), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
    private bool _busy;
    private bool _completed;

    public int ExitCode { get; private set; }

    public InstallerWindow(bool uninstall, string? targetDir, string? payloadPath, Stream? payloadStream,
        bool purgeRequested, bool noLaunch, bool launchRequested)
    {
        _uninstall = uninstall;
        _targetDir = targetDir;
        _payloadPath = payloadPath;
        _payloadStream = payloadStream;
        _noLaunch = noLaunch;
        _purge.IsChecked = purgeRequested;
        _launch.IsChecked = launchRequested || !noLaunch;
        if (!uninstall)
        {
            var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedMeter Pro.lnk");
            _desktop.IsChecked = File.Exists(desktopShortcut);
            try
            {
                _startup.IsChecked = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "SpeedMeterPro", null) is not null;
            }
            catch { _startup.IsChecked = false; }
        }

        Title = uninstall ? "Uninstall SpeedMeter Pro" : "Install SpeedMeter Pro";
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1, workArea.Width * 0.94);
        MaxHeight = Math.Max(1, workArea.Height * 0.90);
        MinWidth = Math.Min(480, MaxWidth);
        MinHeight = Math.Min(300, MaxHeight);
        Width = Math.Min(570, MaxWidth);
        Height = Math.Min(460, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brush("#101B2D");
        Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#F3F7FC");
        if (!SystemParameters.HighContrast)
        {
            _primary.Background = Brush("#79E2C1");
            _primary.Foreground = Brush("#071A2C");
            _primary.BorderThickness = new Thickness(0);
            _secondary.Background = Brush("#22334B");
            _secondary.Foreground = Brush("#F3F7FC");
            _secondary.BorderThickness = new Thickness(0);
            _progress.Foreground = Brush("#79E2C1");
        }

        var root = new DockPanel();
        Content = root;
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(28, 0, 28, 24) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(_secondary);
        footer.Children.Add(_primary);
        _primary.Click += PrimaryClicked;
        _secondary.Click += (_, _) => Close();
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        root.Children.Add(new ScrollViewer
        {
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true
        });

        ShowInitialState();
    }

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 25, FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
    };

    private TextBlock Detail(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#BECDE1")
    };

    private void ShowInitialState()
    {
        _body.Children.Clear();
        _body.Children.Add(Heading(_uninstall ? "Uninstall SpeedMeter Pro?" : "SpeedMeter Pro"));
        if (_uninstall)
        {
            _body.Children.Add(Detail("Remove the application from this Windows account."));
            _body.Children.Add(_purge);
            _body.Children.Add(Detail($"If unchecked, your data will be kept in:\n{InstallerEngine.GetDefaultUserDataDirectory()}"));
            _primary.Content = "Uninstall";
        }
        else
        {
            _body.Children.Add(Detail("Install SpeedMeter Pro on this computer. No administrator access or separate .NET download is required."));
            _body.Children.Add(Detail($"Install location:\n{_targetDir ?? InstallerEngine.GetDefaultInstallDirectory()}"));
            _body.Children.Add(_desktop);
            _body.Children.Add(_startup);
            _primary.Content = "Install";
        }
        _body.Children.Add(_status);
        _body.Children.Add(_progress);
    }

    private async void PrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            if (!_uninstall && !_noLaunch && _launch.IsChecked == true)
            {
                try
                {
                    var exe = Path.Combine(_targetDir ?? InstallerEngine.GetDefaultInstallDirectory(), "SpeedMeter.App.exe");
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} LAUNCH ERROR {ex}{Environment.NewLine}"); } catch { }
                    MessageBox.Show(this, "SpeedMeter Pro was installed, but could not launch. Try opening it from the Start menu.",
                        "Launch failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            Close();
            return;
        }

        if (_uninstall && _purge.IsChecked == true)
        {
            var answer = MessageBox.Show(this,
                "This will permanently delete your usage history, settings and all local SpeedMeter Pro data. This history cannot be recovered afterward. Continue?",
                "Permanently delete local data?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        _busy = true;
        _primary.IsEnabled = false;
        _secondary.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _status.Text = _uninstall ? "Uninstalling SpeedMeter Pro…" : "Installing SpeedMeter Pro…";
        // Read WPF controls on the dispatcher thread before starting filesystem work.
        var purgeUserData = _purge.IsChecked == true;
        var createDesktopShortcut = _desktop.IsChecked == true;
        var startWithWindows = _startup.IsChecked == true;
        try
        {
            void Log(string message)
            {
                try { File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
                catch { /* Logging must not interrupt a transactional install or uninstall. */ }
                var display = message.StartsWith("Installation failed:", StringComparison.Ordinal)
                    ? "Installation failed. Restoring the previous version…"
                    : message.StartsWith("Rollback error:", StringComparison.Ordinal)
                        ? "The previous version may need repair. See the setup log."
                        : message;
                Dispatcher.BeginInvoke(new Action(() => _status.Text = display));
            }
            if (_uninstall)
            {
                await Task.Run(() => InstallerEngine.UninstallAsync(new UninstallOptions(
                    InstallDirectory: _targetDir,
                    PurgeUserData: purgeUserData,
                    Log: Log)));
            }
            else
            {
                await Task.Run(() => InstallerEngine.InstallAsync(new InstallOptions(
                    TargetDirectory: _targetDir,
                    PayloadZipPath: _payloadPath,
                    PayloadStream: _payloadStream,
                    CreateDesktopShortcut: createDesktopShortcut,
                    StartWithWindows: startWithWindows,
                    Log: Log)));
            }

            _completed = true;
            ExitCode = 0;
            try { File.Delete(_logPath); } catch { }
            _body.Children.Clear();
            _body.Children.Add(Heading(_uninstall ? "SpeedMeter Pro uninstalled" : "SpeedMeter Pro installed successfully"));
            if (_uninstall)
            {
                _body.Children.Add(Detail(_purge.IsChecked == true ?
                    "Local data was removed. Application files will finish removing when setup closes." :
                    $"Your usage history and settings remain in:\n{InstallerEngine.GetDefaultUserDataDirectory()}\nApplication files will finish removing when setup closes."));
            }
            else if (!_noLaunch)
            {
                _body.Children.Add(_launch);
            }
            _primary.Content = "Finish";
            _primary.IsEnabled = true;
            _secondary.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ExitCode = ex is SpeedMeterRunningException ? 1618 : 1;
            _status.Text = ex switch
            {
                SpeedMeterRunningException => "SpeedMeter Pro could not close safely. Close it and try again.",
                FileNotFoundException => "The installation package is incomplete. Download Setup.exe again and retry.",
                UnauthorizedAccessException => "Windows denied access to a required file. Check the setup log and retry.",
                _ => _uninstall ? "Uninstall failed. Check the setup log and retry."
                    : "Installation failed. Check the setup log and retry."
            };
            try { File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} ERROR {ex}{Environment.NewLine}"); } catch { }
            if (File.Exists(_logPath)) _status.Text += $"\nSetup log: {_logPath}";
            _primary.IsEnabled = true;
            _secondary.IsEnabled = true;
            _primary.Content = "Retry";
            MessageBox.Show(this, _status.Text, "SpeedMeter Pro setup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }
}
