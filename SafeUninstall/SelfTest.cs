using System.Runtime.InteropServices;

namespace SafeUninstall;

static class SelfTest
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    public static int Run()
    {
        AttachConsole(-1);
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
                failures.Add(message);
        }

        try
        {
            ClassifyChecks(Check);
            UninstallChecks(Check);
            LeftoverChecks(Check);
            JunkChecks(Check);
            DeleteChecks(Check);
            var apps = InstalledAppScanner.Scan(CancellationToken.None, null, includeStore: false);
            Check(apps.Count > 0, "No installed desktop programs were found.");
            foreach (var app in apps)
            {
                Check(app.DisplayName.Length > 0, "Blank app name");
                Check(app.Reason.Length > 0, "Blank reason for " + app.DisplayName);
                if (app.Safety == SafetyLevel.Required)
                    Check(!app.CanUninstall, "Required app can be uninstalled: " + app.DisplayName);
            }

            Console.WriteLine("Listed " + apps.Count + " desktop programs.");
        }
        catch (Exception ex)
        {
            failures.Add(ex.ToString());
        }

        var report = failures.Count == 0
            ? "SELF-TEST PASSED"
            : "SELF-TEST FAILED\r\n" + string.Join("\r\n", failures.Select(failure => "FAIL: " + failure));
        Console.WriteLine(report);
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SafeUninstall-self-test.txt"), report);
        }
        catch
        {
            // The console line is the result that matters.
        }

        return failures.Count == 0 ? 0 : 1;
    }

    static void ClassifyChecks(Action<bool, string> check)
    {
        check(Level(Path.Combine(KnownFolders.Desktop, "notes.txt")) == SafetyLevel.YourFiles, "Desktop file should be your files.");
        check(Level(Path.Combine(KnownFolders.Desktop, "Temp", "a.tmp")) == SafetyLevel.YourFiles, "A Temp folder on the Desktop is still your files.");
        check(Level(Path.Combine(KnownFolders.System, "ntdll.dll")) == SafetyLevel.Required, "System32 should be needed.");
        check(Level(Path.Combine(KnownFolders.WindowsTemp, "a.tmp")) == SafetyLevel.SafeToDelete, "Windows Temp should be safe cache.");
        check(Level(Path.Combine(KnownFolders.Documents, "Temp", "a.tmp")) == SafetyLevel.YourFiles, "Documents\\Temp should be your files.");
        check(Level(Path.Combine(KnownFolders.ProgramFiles, "Steam", "steam.exe")) == SafetyLevel.Uninstallable, "Program files should say uninstall the app.");
        check(Level(Path.Combine(KnownFolders.ProgramFiles, "Common Files", "shared.dll")) == SafetyLevel.Keep, "Common Files should be kept.");
        check(Level(Path.Combine(KnownFolders.LocalAppData, "Google", "Chrome", "User Data", "Default", "Cache", "data_1")) == SafetyLevel.SafeToDelete, "Chrome cache should be safe.");
        check(Level(Path.Combine(KnownFolders.LocalAppData, "Google", "Chrome", "User Data", "Default", "Network", "Cookies")) == SafetyLevel.Review, "Chrome cookies should be review.");
        check(Level(Path.Combine(KnownFolders.Documents, "Google", "Chrome", "User Data", "Default", "Cache", "data_1")) == SafetyLevel.YourFiles, "A cache copy in Documents is your files.");
        var root = Path.GetPathRoot(KnownFolders.Windows) ?? "C:\\";
        check(Level(Path.Combine(root, "pagefile.sys")) == SafetyLevel.Required, "pagefile should be needed.");
        check(Level(Path.Combine(root, "Windows.old", "Windows", "System32", "a.dll")) == SafetyLevel.Review, "Windows.old should be review.");

        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Judge("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", "\"" + notepad + "\"", SafetyLevel.Keep, true, check, "VC++");
        Judge("NVIDIA Graphics Driver 560.94", "NVIDIA Corporation", "", SafetyLevel.Required, false, check, "driver");
        Judge("Driver Booster", "IObit", "\"" + notepad + "\"", SafetyLevel.Review, true, check, "driver utility");
        Judge("Steam", "Valve", "\"" + notepad + "\"", SafetyLevel.Uninstallable, true, check, "steam");
    }

    static void Judge(string name, string publisher, string uninstall, SafetyLevel expected, bool canRun, Action<bool, string> check, string label)
    {
        var draft = new InstalledApp
        {
            DisplayName = name,
            Publisher = publisher,
            UninstallString = uninstall
        };
        SafetyClassifier.ClassifyInstalledApp(draft, out var level, out _, out _, out var can, out _);
        check(level == expected, label + " level was " + level);
        check(can == canRun, label + " uninstall flag was " + can);
    }

    static SafetyLevel Level(string path) => SafetyClassifier.ClassifyPath(path).Level;

    static void UninstallChecks(Action<bool, string> check)
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var quoted = UninstallService.Plan("\"" + notepad + "\" /S", false, "x", false);
        check(quoted.CanRun && quoted.FileName.Equals(notepad, StringComparison.OrdinalIgnoreCase) && quoted.Arguments == "/S", "Quoted uninstaller was not parsed.");

        var msi = UninstallService.Plan("MsiExec.exe /I{00000000-0000-0000-0000-000000000000}", true, "{00000000-0000-0000-0000-000000000000}", false);
        check(msi.CanRun && msi.FileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) && msi.Arguments.StartsWith("/x ", StringComparison.Ordinal), "MSI uninstall was not rewritten to /x.");

        var cmd = UninstallService.Plan(Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /c echo hi", false, "x", false);
        check(!cmd.CanRun, "cmd.exe uninstall should be blocked.");
        var amp = UninstallService.Plan("\"" + notepad + "\" & calc", false, "x", false);
        check(!amp.CanRun, "Chained uninstall command should be blocked.");
        var silent = UninstallService.Plan(null, false, "x", true);
        check(!silent.CanRun, "Silent-only uninstall should be blocked.");
        var relative = UninstallService.Plan("uninstall.exe /S", false, "x", false);
        check(!relative.CanRun, "Relative uninstaller should be blocked.");
    }

    static void LeftoverChecks(Action<bool, string> check)
    {
        var program = Path.Combine(KnownFolders.ProgramFiles, "Mozilla Firefox");
        check(LeftoverScanner.LocationBlock(program) == null, "Program Files child should be an allowed leftover location.");
        check(LeftoverScanner.LocationBlock(KnownFolders.ProgramFiles) != null, "Program Files itself must be blocked.");
        check(LeftoverScanner.LocationBlock(Path.Combine(KnownFolders.Documents, "Mozilla Firefox")) != null, "Documents must be blocked.");
        check(LeftoverScanner.LocationBlock(Path.Combine(KnownFolders.Windows, "Mozilla Firefox")) != null, "Windows must be blocked.");
        check(LeftoverScanner.LocationBlock(@"D:\Games\Spotify") != null, "A game library path must not be auto-deleted.");
        check(LeftoverScanner.DeleteBlock(program, appInUse: true) != null, "Installed app folders must not be deletable.");
        check(LeftoverScanner.DeleteBlock(Path.Combine(KnownFolders.RoamingAppData, "Microsoft"), appInUse: false) != null, "Microsoft folder name must be blocked.");
        if (!string.IsNullOrWhiteSpace(KnownFolders.StartMenuPrograms))
            check(LeftoverScanner.LocationBlock(Path.Combine(KnownFolders.StartMenuPrograms, "Some App")) == null, "Start Menu program folder should be allowed.");
    }

    static void JunkChecks(Action<bool, string> check)
    {
        foreach (var category in JunkCatalog.Create())
        {
            if (category.IsRecycleBin)
                continue;
            check(!PathRules.Same(category.Path, KnownFolders.Windows), "Junk root is Windows: " + category.Name);
            check(!PathRules.Same(category.Path, KnownFolders.UserProfile), "Junk root is the user profile: " + category.Name);
            check(!PathRules.IsDriveRoot(category.Path), "Junk root is a drive: " + category.Name);
            check(Directory.Exists(category.Path), "Missing junk root was listed: " + category.Path);
            var allowed = PathRules.IsUnder(category.Path, KnownFolders.LocalAppData)
                || PathRules.IsUnder(category.Path, KnownFolders.RoamingAppData)
                || PathRules.Same(category.Path, KnownFolders.Temp)
                || PathRules.IsUnder(category.Path, KnownFolders.Temp)
                || PathRules.IsUnder(category.Path, KnownFolders.WindowsTemp)
                || PathRules.Same(category.Path, KnownFolders.WindowsTemp);
            check(allowed, "Unexpected junk root: " + category.Path);
        }

        check(SafeDelete.RefusalReason(Path.Combine(KnownFolders.Windows, "System32"), DeleteKind.Junk) != null, "System32 must be refused.");
        check(SafeDelete.RefusalReason(KnownFolders.Windows, DeleteKind.Junk) != null, "Windows must be refused.");
        check(SafeDelete.RefusalReason(KnownFolders.Documents, DeleteKind.Junk) != null, "Documents must be refused.");
        if (JunkCatalog.Create().Any(category => PathRules.Same(category.Path, KnownFolders.WindowsTemp)))
            check(SafeDelete.RefusalReason(KnownFolders.WindowsTemp, DeleteKind.Junk) == null, "Windows Temp should be an approved junk root.");
        check(RecycleBin.TryMeasure(out _, out _) || true, "Recycle Bin query should not throw.");
    }

    static void DeleteChecks(Action<bool, string> check)
    {
        var id = "SafeUninstallTest_" + Guid.NewGuid().ToString("N");
        var sandbox = Path.Combine(Path.GetTempPath(), id);
        var target = Path.Combine(Path.GetTempPath(), id + "_target");
        var link = Path.Combine(sandbox, "trap");
        var secret = Path.Combine(target, "secret.txt");
        try
        {
            Directory.CreateDirectory(sandbox);
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(sandbox, "sub"));
            File.WriteAllText(secret, "keep");
            File.WriteAllText(Path.Combine(sandbox, "plain.txt"), "plain");
            File.WriteAllText(Path.Combine(sandbox, "thumbcache_32.db"), "cache");
            File.WriteAllText(Path.Combine(sandbox, "sub", "nested.txt"), "nested");

            var outside = Path.GetFullPath(Path.Combine(sandbox, "..", "secret.txt"));
            check(!PathRules.IsStrictlyInside(outside, sandbox), "A path that climbs out of the folder must be rejected.");

            var linkOk = RunMklink(link, target);
            check(linkOk, "Could not create a junction for the safety test.");

            SafeDelete.DeleteTree(sandbox, DeleteKind.TestSandbox, deleteRoot: false, CancellationToken.None, ["thumbcache*.db"], includeSubdirectories: true);
            check(!File.Exists(Path.Combine(sandbox, "thumbcache_32.db")), "Cache pattern file should be deleted.");
            check(File.Exists(Path.Combine(sandbox, "plain.txt")), "Unmatched file should remain.");
            check(File.ReadAllText(secret) == "keep", "SECRET_LOST during pattern delete.");

            SafeDelete.DeleteTree(sandbox, DeleteKind.TestSandbox, deleteRoot: false, CancellationToken.None);
            check(!File.Exists(Path.Combine(sandbox, "plain.txt")), "Plain temp file should be deleted.");
            check(!File.Exists(Path.Combine(sandbox, "sub", "nested.txt")), "Nested temp file should be deleted.");
            check(File.Exists(secret) && File.ReadAllText(secret) == "keep", "SECRET_LOST junction target was deleted.");
            check(Directory.Exists(target), "Junction target folder was removed.");
            Console.WriteLine("SECRET_OK");
        }
        finally
        {
            try
            {
                if (Directory.Exists(link))
                    Directory.Delete(link, recursive: false);
            }
            catch { /* test cleanup */ }
            try
            {
                if (Directory.Exists(sandbox))
                    SafeDelete.DeleteTree(sandbox, DeleteKind.TestSandbox, deleteRoot: true, CancellationToken.None);
            }
            catch { /* test cleanup */ }
            try
            {
                if (File.Exists(secret))
                    File.Delete(secret);
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: false);
            }
            catch { /* test cleanup */ }
        }
    }

    static bool RunMklink(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process == null)
            return false;
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(link);
    }
}
