using Microsoft.Win32;

namespace SafeUninstall;

static class InstalledAppScanner
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> Scan(CancellationToken ct, IProgress<string>? progress, bool includeStore)
    {
        var apps = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report("Reading installed programs...");
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, apps, seen, ct);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, apps, seen, ct);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Registry64, apps, seen, ct);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Registry32, apps, seen, ct);

        if (includeStore)
        {
            progress?.Report("Reading Microsoft Store apps...");
            try
            {
                foreach (var app in ReadStoreApps(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var id = "store\n" + app.PackageFullName;
                    if (seen.Add(id))
                        apps.Add(app);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report("Microsoft Store apps were not listed. " + ex.Message);
            }
        }

        return apps
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static void ReadHive(RegistryHive hive, RegistryView view, List<InstalledApp> apps, HashSet<string> seen, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallKey);
            if (uninstall == null)
                return;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key == null)
                        continue;
                    var app = ReadApp(key, subName);
                    if (app == null)
                        continue;
                    var id = app.DisplayName + "\n" + app.Version + "\n" + app.UninstallString + "\n" + app.PackageFullName;
                    if (seen.Add(id))
                        apps.Add(app);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // One broken uninstall key should not hide the rest.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // This hive can be unreadable without administrator rights.
        }
    }

    static InstalledApp? ReadApp(RegistryKey key, string subName)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var release = key.GetValue("ReleaseType") as string ?? "";
        var parent = key.GetValue("ParentKeyName") as string ?? "";
        var parentDisplay = key.GetValue("ParentDisplayName") as string ?? "";
        var system = key.GetValue("SystemComponent") is int flag && flag == 1;
        var windowsUpdate = system
            || parent.Length > 0
            || parentDisplay.Length > 0
            || release is "Update" or "Security Update" or "Hotfix" or "ServicePack" or "Update Rollup"
            || SafetyClassifier.IsWindowsUpdateName(name);

        var uninstall = (key.GetValue("UninstallString") as string)?.Trim() ?? "";
        var quiet = (key.GetValue("QuietUninstallString") as string)?.Trim() ?? "";
        var silentOnly = uninstall.Length == 0 && quiet.Length > 0;
        var publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "";
        var version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "";
        var location = CleanLocation(key.GetValue("InstallLocation") as string);
        var draft = new InstalledApp
        {
            DisplayName = name,
            Publisher = publisher,
            Version = version,
            SizeBytes = ReadSize(key.GetValue("EstimatedSize")),
            InstallDate = ReadDate(key.GetValue("InstallDate") as string),
            InstallLocation = location,
            UninstallString = uninstall,
            RegistryKeyName = subName,
            WindowsInstaller = key.GetValue("WindowsInstaller") is int msi && msi == 1,
            SilentOnly = silentOnly,
            IsSystemComponent = system,
            IsWindowsUpdate = windowsUpdate
        };

        SafetyClassifier.ClassifyInstalledApp(draft, out var level, out var reason, out var hide, out var canUninstall, out var block);
        return new InstalledApp
        {
            DisplayName = draft.DisplayName,
            Publisher = draft.Publisher,
            Version = draft.Version,
            SizeBytes = draft.SizeBytes,
            InstallDate = draft.InstallDate,
            InstallLocation = draft.InstallLocation,
            UninstallString = draft.UninstallString,
            RegistryKeyName = draft.RegistryKeyName,
            WindowsInstaller = draft.WindowsInstaller,
            SilentOnly = draft.SilentOnly,
            IsSystemComponent = system,
            IsWindowsUpdate = windowsUpdate,
            Safety = level,
            Reason = MergeReason(reason, canUninstall, block),
            HideByDefault = hide,
            CanUninstall = canUninstall,
            UninstallBlock = block
        };
    }

    static string MergeReason(string reason, bool canUninstall, string block)
    {
        if (canUninstall || block.Length == 0 || reason.Contains(block, StringComparison.Ordinal))
            return reason.Trim();
        return (reason + " " + block).Trim();
    }

    static List<InstalledApp> ReadStoreApps(CancellationToken ct)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), "SafeUninstall-store-" + Guid.NewGuid().ToString("N") + ".json");
        var script = """
            $ErrorActionPreference = 'Stop'
            $pkgs = @(Get-AppxPackage | ForEach-Object {
              [PSCustomObject]@{
                Name = [string]$_.Name
                PackageFullName = [string]$_.PackageFullName
                Publisher = [string]$_.Publisher
                Version = [string]$_.Version
                NonRemovable = [bool]$_.NonRemovable
                IsFramework = [bool]$_.IsFramework
                InstallLocation = [string]$_.InstallLocation
              }
            })
            $pkgs | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $env:SAFEUNINSTALL_JSON -Encoding utf8
            """;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.Environment["SAFEUNINSTALL_JSON"] = jsonPath;

            using var process = new Process { StartInfo = psi };
            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            using var _ = ct.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            });
            if (!process.WaitForExit(60000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                throw new TimeoutException("Listing Microsoft Store apps took too long.");
            }

            process.WaitForExit();
            ct.ThrowIfCancellationRequested();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "PowerShell could not list Store apps." : error.Trim());

            if (!File.Exists(jsonPath))
                return new();
            var json = File.ReadAllText(jsonPath).Trim();
            return ParseStore(json);
        }
        finally
        {
            try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { /* temp file */ }
        }
    }

    static List<InstalledApp> ParseStore(string json)
    {
        if (json.Length == 0 || json == "null")
            return new();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<StorePackage> packages;
        if (json.StartsWith('['))
            packages = JsonSerializer.Deserialize<List<StorePackage>>(json, options) ?? new();
        else
        {
            var one = JsonSerializer.Deserialize<StorePackage>(json, options);
            packages = one == null ? new() : new() { one };
        }

        var apps = new List<InstalledApp>();
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Name) || string.IsNullOrWhiteSpace(package.PackageFullName))
                continue;
            var draft = new InstalledApp
            {
                DisplayName = PrettifyStoreName(package.Name),
                Publisher = ShortPublisher(package.Publisher),
                Version = package.Version?.Trim() ?? "",
                InstallLocation = package.InstallLocation?.Trim() ?? "",
                IsStore = true,
                IsFramework = package.IsFramework,
                NonRemovable = package.NonRemovable,
                PackageFullName = package.PackageFullName.Trim()
            };
            SafetyClassifier.ClassifyInstalledApp(draft, out var level, out var reason, out var hide, out var canUninstall, out var block);
            apps.Add(new InstalledApp
            {
                DisplayName = draft.DisplayName,
                Publisher = draft.Publisher,
                Version = draft.Version,
                InstallLocation = draft.InstallLocation,
                IsStore = true,
                IsFramework = package.IsFramework,
                NonRemovable = package.NonRemovable,
                PackageFullName = draft.PackageFullName,
                Safety = level,
                Reason = MergeReason(reason, canUninstall, block),
                HideByDefault = hide,
                CanUninstall = canUninstall,
                UninstallBlock = block,
                SizeBytes = -1,
                InstallDate = ""
            });
        }

        return apps;
    }

    static string PrettifyStoreName(string name)
    {
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var start = 0;
        while (start < parts.Length - 1 && parts[start] is "Microsoft" or "Windows" or "MicrosoftWindows")
            start++;
        var chosen = parts.Skip(start).ToArray();
        if (chosen.Length == 0 || chosen.All(part => part.Length < 3 || part.All(char.IsDigit)))
            chosen = parts;
        var shortName = string.Join(" ", chosen);
        shortName = Regex.Replace(shortName, "(?<=[a-z0-9])(?=[A-Z])", " ");
        shortName = Regex.Replace(shortName, "(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return shortName;
    }

    static string ShortPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
            return "";
        var text = publisher.Trim();
        var cn = text.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (cn < 0)
            return text;
        var start = cn + 3;
        var end = text.IndexOf(',', start);
        return (end < 0 ? text[start..] : text[start..end]).Trim().Trim('"');
    }

    static long ReadSize(object? value)
    {
        long kb = value switch
        {
            int i => (uint)i,
            long l => l,
            _ => 0
        };
        return kb <= 0 ? -1 : kb * 1024L;
    }

    static string ReadDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        if (DateTime.TryParseExact(raw.Trim(), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            return date.ToString("yyyy-MM-dd");
        return raw.Trim();
    }

    static string CleanLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var text = value.Trim().Trim('"');
        try
        {
            if (File.Exists(text))
                return Path.GetDirectoryName(text) ?? text;
        }
        catch
        {
            return text;
        }

        return text;
    }

    sealed class StorePackage
    {
        public string? Name { get; set; }
        public string? PackageFullName { get; set; }
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public bool NonRemovable { get; set; }
        public bool IsFramework { get; set; }
        public string? InstallLocation { get; set; }
    }
}
