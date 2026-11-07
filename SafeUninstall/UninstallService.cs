namespace SafeUninstall;

sealed class UninstallPlan
{
    public bool CanRun { get; init; }
    public string FileName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Message { get; init; } = "";
    public bool StoreApp { get; init; }

    public string DisplayCommand =>
        string.IsNullOrWhiteSpace(FileName) ? Message : $"{FileName} {Arguments}".Trim();
}

static class UninstallService
{
    static readonly string[] BlockedHosts =
    [
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
        "mshta.exe", "bash.exe", "reg.exe", "regedit.exe", "wt.exe"
    ];

    public static UninstallPlan Plan(InstalledApp app)
    {
        if (app.IsStore)
        {
            if (app.NonRemovable || app.IsFramework || string.IsNullOrWhiteSpace(app.PackageFullName))
                return Block("This Store app cannot be removed.");
            if (!Regex.IsMatch(app.PackageFullName, @"^[A-Za-z0-9][A-Za-z0-9\.\-_]{1,250}$"))
                return Block("The Store package name looked unusual, so it was not removed.");
            return new UninstallPlan { CanRun = true, StoreApp = true, Message = app.PackageFullName };
        }

        return Plan(app.UninstallString, app.WindowsInstaller, app.RegistryKeyName, app.SilentOnly);
    }

    public static UninstallPlan Plan(string? uninstallString, bool windowsInstaller, string registryKeyName, bool silentOnly)
    {
        if (windowsInstaller && IsProductCode(registryKeyName))
            return Allow(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/x " + registryKeyName);

        if (silentOnly)
            return Block("This program only registered a silent uninstaller. SafeUninstall will not run that. Remove it from Windows Settings, Apps, if it is listed.");

        if (string.IsNullOrWhiteSpace(uninstallString))
            return Block("No official uninstaller was found. SafeUninstall will not delete its folder.");

        if (!TrySplit(uninstallString, out var file, out var args))
            return Block("The uninstall command could not be read, so it was not started.");

        if (args.Contains('&') || args.Contains('|') || args.Contains('\r') || args.Contains('\n'))
            return Block("This uninstall command looks unusual, so SafeUninstall will not run it. Remove the app from Windows Settings, Apps, instead.");

        var host = Path.GetFileName(file);
        if (BlockedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            return Block("This uninstall command uses a script host, so SafeUninstall will not run it. Remove the app from Windows Settings, Apps, instead.");

        if (host.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            file = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        else if (host.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase))
            file = Path.Combine(Environment.SystemDirectory, "rundll32.exe");

        if (!Path.IsPathRooted(file))
            return Block("The uninstaller path is not a full path, so it was not started.");

        if (!UninstallerLooksPresent(file))
            return Block("The uninstaller file was not found: " + file);

        return Allow(file, args);
    }

    static bool UninstallerLooksPresent(string file)
    {
        // A disconnected network path can stall File.Exists for a long time.
        // Those uninstallers are still offered, and the user sees the command before it runs.
        if (file.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        var root = Path.GetPathRoot(file);
        if (string.IsNullOrEmpty(root) || !ReadyLocalRoots().Contains(root))
            return true;

        try
        {
            return File.Exists(file);
        }
        catch
        {
            return false;
        }
    }

    static HashSet<string>? readyLocalRoots;

    static HashSet<string> ReadyLocalRoots()
    {
        if (readyLocalRoots != null)
            return readyLocalRoots;

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                        continue;
                    if (!drive.IsReady)
                        continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
                catch
                {
                    // Skip drives that cannot be queried.
                }
            }
        }
        catch
        {
            // Fall back to the system drive so a drive query cannot block uninstall checks.
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(system))
                roots.Add(system);
        }

        readyLocalRoots = roots;
        return roots;
    }

    public static void Launch(UninstallPlan plan)
    {
        if (!plan.CanRun || plan.StoreApp)
            throw new InvalidOperationException(plan.Message);
        Process.Start(new ProcessStartInfo
        {
            FileName = plan.FileName,
            Arguments = plan.Arguments,
            UseShellExecute = true
        });
    }

    public static async Task<string?> RemoveStoreAppAsync(string packageFullName, CancellationToken ct)
    {
        if (!Regex.IsMatch(packageFullName, @"^[A-Za-z0-9][A-Za-z0-9\.\-_]{1,250}$"))
            return "The Store package name looked unusual, so it was not removed.";

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("Remove-AppxPackage -Package $env:SAFEUNINSTALL_PKG -ErrorAction Stop");
        psi.Environment["SAFEUNINSTALL_PKG"] = packageFullName;

        using var process = new Process { StartInfo = psi };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        using var _ = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        if (process.ExitCode == 0)
            return null;
        var detail = string.IsNullOrWhiteSpace(error) ? output : error;
        return string.IsNullOrWhiteSpace(detail) ? "Microsoft Store did not remove that app." : detail.Trim();
    }

    static UninstallPlan Allow(string file, string args) => new()
    {
        CanRun = true,
        FileName = file,
        Arguments = args
    };

    static UninstallPlan Block(string message) => new() { CanRun = false, Message = message };

    static bool IsProductCode(string value) =>
        Regex.IsMatch(value, @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$");

    static bool TrySplit(string command, out string file, out string args)
    {
        file = "";
        args = "";
        command = command.Trim();
        if (command.Length == 0 || command.Contains('\r') || command.Contains('\n'))
            return false;

        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end <= 1)
                return false;
            file = command[1..end];
            args = command[(end + 1)..].Trim();
            return file.Length > 0;
        }

        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            file = command[..(exe + 4)].Trim().Trim('"');
            args = command[(exe + 4)..].Trim();
            return file.Length > 0;
        }

        var space = command.IndexOf(' ');
        if (space < 0)
        {
            file = command;
            return true;
        }

        file = command[..space];
        args = command[(space + 1)..].Trim();
        return file.Length > 0;
    }
}
