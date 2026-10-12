using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedMeter.Core;
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

        check("CONS-SAFETY uninstall rejects unowned and command-sensitive install paths", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var unowned = Path.Combine(root, "Unowned");
                Directory.CreateDirectory(unowned);
                var retained = Path.Combine(unowned, "retain.txt");
                File.WriteAllText(retained, "must remain");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: unowned)),
                    "Uninstall accepted a non-product directory.");
                Rejects(() => InstallerEngine.InstallAsync(new InstallOptions(TargetDirectory: unowned)).GetAwaiter().GetResult(),
                    "Install accepted a nonempty non-product directory.");
                Require(File.ReadAllText(retained) == "must remain", "Rejected install altered an existing directory.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: Path.Combine(root, "x&calc"))),
                    "Uninstall accepted a command separator in -Dir.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: Path.Combine(root, "x\"&calc"))),
                    "Uninstall accepted a quote in -Dir.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: Path.Combine(root, "Unowned", "..", "Unowned"))),
                    "Uninstall accepted dot-segment traversal in -Dir.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY test purge accepts only the sentinelled disposable root", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "install");
                var data = Path.Combine(root, "UserData");
                Directory.CreateDirectory(install);
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, ".speedmeter-pro-install-root"),
                    InstallerEngine.InstallMarkerContent(install));
                InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = data });

                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)),
                    "A test install could purge real SpeedMeter Pro data.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = InstallerEngine.GetDefaultUserDataDirectory() }),
                    "Test override accepted the real product data directory.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = Path.Combine(root, "nested", "..", "UserData") }),
                    "Test override accepted traversal in the data path.");
                Rejects(() => TestSandboxGuard.ValidateRoot(Path.GetTempPath()), "Temp parent was accepted as a test root.");
                Rejects(() => TestSandboxGuard.ValidateRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                    "User profile was accepted as a test root.");
                Rejects(() => TestSandboxGuard.ValidateChild(Path.Combine(root, "..", "escape"), root),
                    "Dot-segment traversal was accepted.");

                var inside = Path.Combine(root, "inside");
                Directory.CreateDirectory(inside);
                var link = Path.Combine(data, "link");
                var linked = false;
                try { Directory.CreateSymbolicLink(link, inside); linked = true; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
                if (linked)
                {
                    try
                    {
                        Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                            { TestUserDataDirectory = data }), "Reparse point inside purge data was accepted.");
                        Rejects(() => TestDeleteGuard.DeleteRoot(root), "Test delete guard accepted a reparse point.");
                    }
                    finally { Directory.Delete(link); }
                }
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });
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

    private static void Rejects(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        Require(false, message);
    }
}
