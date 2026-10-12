using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        check("CONS-INSTALL cancelling setup or uninstall retains user-exit status", () => OnSta(() =>
        {
            foreach (var uninstall in new[] { false, true })
            {
                var window = new InstallerWindow(uninstall, null, null, null, false, false, false);
                Require(window.ExitCode == 1602, "Initial installer window falsely reported completed work.");
                var cancel = Descendants(window).OfType<Button>().Single(x => x.Content?.ToString() == "Cancel");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(window.ExitCode == 1602, "Cancelling installer window falsely reported completed work.");
            }
        }));

        check("CONS-SAFETY uninstall rejects unowned and traversing install paths", () =>
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
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: Path.Combine(root, "Unowned", "..", "Unowned"))),
                    "Uninstall accepted dot-segment traversal in -Dir.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY uninstall rejects missing, forged and path-mismatched ownership markers", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "SpeedMeterPro");
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                var marker = Path.Combine(install, ".speedmeter-pro-install-root");
                var unrelated = Path.Combine(root, "Unrelated");
                Directory.CreateDirectory(unrelated);
                File.WriteAllText(Path.Combine(unrelated, "retain.txt"), "retain");

                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install)),
                    "Uninstall accepted an install tree with no ownership marker.");
                File.WriteAllText(marker, "SpeedMeterPro.InstallRoot.v1:FORGED");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install)),
                    "Uninstall accepted a forged ownership marker.");
                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(Path.Combine(root, "Other", "SpeedMeterPro")));
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install)),
                    "Uninstall accepted a marker bound to another path.");

                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(install));
                InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install));
                Require(File.ReadAllText(Path.Combine(unrelated, "retain.txt")) == "retain",
                    "Rejected uninstall changed an unrelated file.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY uninstall rechecks ownership after marker or target replacement", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "SpeedMeterPro");
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                var marker = Path.Combine(install, ".speedmeter-pro-install-root");
                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(install));
                var options = new UninstallOptions(InstallDirectory: install);
                InstallerEngine.ValidateUninstallRequest(options);

                File.WriteAllText(marker, "changed after validation");
                Rejects(() => InstallerEngine.UninstallAsync(options).GetAwaiter().GetResult(),
                    "Uninstall accepted an ownership marker changed after earlier validation.");
                Require(File.Exists(Path.Combine(install, "SpeedMeter.App.exe")),
                    "A rejected changed-marker uninstall removed an install file.");

                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(install));
                InstallerEngine.ValidateUninstallRequest(options);
                var parked = Path.Combine(root, "ParkedOwnedInstall");
                Directory.Move(install, parked);
                Directory.CreateDirectory(install);
                var decoy = Path.Combine(install, "retain.txt");
                File.WriteAllText(decoy, "retain");
                Rejects(() => InstallerEngine.UninstallAsync(options).GetAwaiter().GetResult(),
                    "Uninstall accepted a replaced target after earlier validation.");
                Require(File.ReadAllText(decoy) == "retain" &&
                    File.Exists(Path.Combine(parked, "SpeedMeter.App.exe")),
                    "A rejected target-replacement uninstall changed the decoy or parked installation.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY valid NTFS metacharacters do not block an owned install path", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "Family & (Work)!", "SpeedMeterPro");
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, ".speedmeter-pro-install-root"),
                    InstallerEngine.InstallMarkerContent(install));
                var options = new UninstallOptions(InstallDirectory: install);
                InstallerEngine.ValidateUninstallRequest(options);
                InstallerEngine.UninstallAsync(options).GetAwaiter().GetResult();
                Require(!Directory.Exists(install),
                    "An owned install beneath a path with valid NTFS metacharacters was not removed.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY uninstall rejects a reparse component in an owned-looking path", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var inside = Path.Combine(root, "Inside");
                var install = Path.Combine(inside, "SpeedMeterPro");
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                var alias = Path.Combine(root, "Alias");
                if (!TryCreateReparseAlias(alias, inside))
                    throw new SkipCheckException("Neither directory symbolic links nor junctions are available in this test context.");
                try
                {
                    var linkedInstall = Path.Combine(alias, "SpeedMeterPro");
                    File.WriteAllText(Path.Combine(install, ".speedmeter-pro-install-root"),
                        InstallerEngine.InstallMarkerContent(linkedInstall));
                    Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: linkedInstall)),
                        "Uninstall accepted a reparse point in the install path.");
                }
                finally { Directory.Delete(alias); }
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY helper request rejects arbitrary target, nonce and parent tampering", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "SpeedMeterPro");
                Directory.CreateDirectory(install);
                var source = Path.Combine(install, "Uninstall.exe");
                File.WriteAllText(source, "fixture helper identity");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture app");
                var marker = Path.Combine(install, ".speedmeter-pro-install-root");
                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(install));
                var nonce = Guid.NewGuid().ToString("N");
                var helperDir = Path.Combine(root, "Helpers", nonce);
                Directory.CreateDirectory(helperDir);
                var helper = Path.Combine(helperDir, "Uninstall.exe");
                File.Copy(source, helper);
                var requestPath = Path.Combine(helperDir, "request.json");
                using var parent = Process.GetCurrentProcess();
                var good = new CleanupRequestFixture(install, source,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
                    parent.Id, parent.StartTime.ToUniversalTime().Ticks, nonce,
                    ["-Uninstall", "-Dir", install]);
                void Write(CleanupRequestFixture request) => File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
                void Validate() => UninstallBootstrap.ValidateRequestForTests(requestPath, root, helper, install);
                void ValidateParent() => UninstallBootstrap.ValidateParentForTests(requestPath, root, helper, install);

                Write(good);
                Validate();
                ValidateParent();
                var arbitrary = Path.Combine(root, "Arbitrary", "SpeedMeterPro");
                Write(good with { Target = arbitrary });
                Rejects(Validate, "Cleanup helper accepted an arbitrary target.");
                Write(good with { Nonce = Guid.NewGuid().ToString("N") });
                Rejects(Validate, "Cleanup helper accepted an incorrect nonce.");
                Write(good with { ParentPid = 0 });
                Rejects(Validate, "Cleanup helper accepted a missing parent PID.");
                Write(good with { ParentStartUtcTicks = 0 });
                Rejects(Validate, "Cleanup helper accepted missing parent start-time context.");
                Write(good with { ParentStartUtcTicks = good.ParentStartUtcTicks - 1 });
                Rejects(ValidateParent, "Cleanup helper accepted a stale parent start time.");
                Write(good with { ParentPid = int.MaxValue });
                try
                {
                    ValidateParent();
                    throw new InvalidOperationException("Cleanup helper accepted a nonexistent parent PID.");
                }
                catch (ArgumentException) { /* Process.GetProcessById fails closed. */ }
                Write(good with { OriginalArgs = ["-Uninstall", "-Dir", arbitrary] });
                Rejects(Validate, "Cleanup helper accepted command arguments targeting another directory.");

                Write(good);
                File.WriteAllText(marker, "tampered");
                Rejects(Validate, "Cleanup helper accepted a modified ownership marker.");
                File.WriteAllText(marker, InstallerEngine.InstallMarkerContent(install));
                File.AppendAllText(helper, "tampered");
                Rejects(Validate, "Cleanup helper accepted a modified helper binary.");
                File.Copy(source, helper, overwrite: true);
                Write(good);
                Validate();
                var parked = Path.Combine(root, "ParkedInstall");
                Directory.Move(install, parked);
                Directory.CreateDirectory(install);
                File.WriteAllText(source, "fixture helper identity");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "decoy app");
                var decoy = Path.Combine(install, "retain.txt");
                File.WriteAllText(decoy, "retain");
                Rejects(Validate, "Cleanup helper accepted a replaced target without its ownership marker.");
                Require(File.ReadAllText(decoy) == "retain" &&
                    File.Exists(Path.Combine(parked, "SpeedMeter.App.exe")),
                    "Rejected helper target replacement changed the decoy or parked install.");
                Require(File.Exists(Path.Combine(install, "SpeedMeter.App.exe")),
                    "Rejected helper requests altered the installation.");
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
                    { TestUserDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }),
                    "Test override accepted LocalAppData itself.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }),
                    "Test override accepted the user-profile root.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = Path.GetPathRoot(root)! }),
                    "Test override accepted the drive root.");
                Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: root, PurgeUserData: true)
                    { TestUserDataDirectory = data }),
                    "Uninstall accepted the disposable root as an installation target.");
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
                var outsideFile = Path.Combine(inside, "retain.txt");
                File.WriteAllText(outsideFile, "retain");
                var link = Path.Combine(data, "link");
                var linked = TryCreateReparseAlias(link, inside);
                if (linked)
                {
                    try
                    {
                        Rejects(() => InstallerEngine.ValidateUninstallRequest(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                            { TestUserDataDirectory = data }), "Reparse point inside purge data was accepted.");
                        Rejects(() => InstallerEngine.UninstallAsync(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                            { TestUserDataDirectory = data }).GetAwaiter().GetResult(),
                            "Uninstall accepted a reparse point inserted after prior validation.");
                        Require(File.ReadAllText(outsideFile) == "retain" && Directory.Exists(install),
                            "Rejected reparse insertion changed the outside file or owned install.");
                        Rejects(() => TestDeleteGuard.DeleteRoot(root), "Test delete guard accepted a reparse point.");
                    }
                    finally { Directory.Delete(link); }
                }
                else throw new SkipCheckException("Neither directory symbolic links nor junctions are available for purge reparse testing.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY quarantine purge removes only selected data and owned install", () =>
        {
            var root = TestDeleteGuard.CreateRoot();
            try
            {
                var install = Path.Combine(root, "install");
                var data = Path.Combine(root, "UserData");
                var unrelated = Path.Combine(root, "Unrelated");
                Directory.CreateDirectory(install);
                Directory.CreateDirectory(Path.Combine(data, "nested"));
                Directory.CreateDirectory(unrelated);
                File.WriteAllText(Path.Combine(install, "Uninstall.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, "SpeedMeter.App.exe"), "fixture");
                File.WriteAllText(Path.Combine(install, ".speedmeter-pro-install-root"),
                    InstallerEngine.InstallMarkerContent(install));
                File.WriteAllText(Path.Combine(data, "nested", "history.db"), "disposable history");
                var sibling = Path.Combine(unrelated, "retain.txt");
                File.WriteAllText(sibling, "retain");

                InstallerEngine.UninstallAsync(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                    { TestUserDataDirectory = data }).GetAwaiter().GetResult();
                Require(!Directory.Exists(install) && !Directory.Exists(data),
                    "A successful sandbox uninstall retained the install or selected data tree.");
                Require(File.ReadAllText(sibling) == "retain", "Sandbox purge changed an unrelated sibling.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });

        check("CONS-SAFETY failed purge reports residual data after removing owned install", () =>
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
                var locked = Path.Combine(data, "locked.db");
                File.WriteAllText(locked, "disposable locked history");
                using (var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try
                    {
                        InstallerEngine.UninstallAsync(new UninstallOptions(InstallDirectory: install, PurgeUserData: true)
                            { TestUserDataDirectory = data }).GetAwaiter().GetResult();
                        throw new InvalidOperationException("Locked-data purge falsely reported complete success.");
                    }
                    catch (IOException ex)
                    {
                        Require(ex.Message.Contains("some local data could not be deleted", StringComparison.Ordinal),
                            "Failed purge did not report the required residual-data outcome.");
                    }
                }
                Require(!Directory.Exists(install), "Failed purge left the owned application installed.");
                var residual = Directory.Exists(data) ||
                    Directory.EnumerateDirectories(root, ".UserData.removing.*").Any();
                Require(residual, "Failed purge did not leave a discoverable residual data tree.");
            }
            finally { TestDeleteGuard.DeleteRoot(root); }
        });
    }

    private sealed record CleanupRequestFixture(
        string Target, string SourceExecutable, string SourceSha256,
        int ParentPid, long ParentStartUtcTicks, string Nonce, string[] OriginalArgs);

    private static bool TryCreateReparseAlias(string alias, string inside)
    {
        try { Directory.CreateSymbolicLink(alias, inside); return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }

        // A junction normally needs no symlink privilege. Both paths are beneath a
        // TestSandboxGuard root; encoded PowerShell avoids shell parsing of profile paths.
        string Quote(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
        var script = $"New-Item -ItemType Junction -Path {Quote(alias)} -Target {Quote(inside)} -ErrorAction Stop | Out-Null";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
                return false;
            }
            if (process.ExitCode == 0 && Directory.Exists(alias) &&
                (File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0) return true;
            if (Directory.Exists(alias) && (File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(alias);
            return false;
        }
        catch { return false; }
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
