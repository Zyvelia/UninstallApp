namespace SafeUninstall;

sealed class LeftoverHit
{
    public required string Path { get; init; }
    public long Bytes { get; set; }
    public long Files { get; set; }
    public bool Partial { get; set; }
    public string Advice { get; set; } = "";
    public string? BlockReason { get; set; }
    public string FolderName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
}

static class LeftoverScanner
{
    static readonly HashSet<string> BlockedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "users", "program files", "program files x86", "programdata", "appdata",
        "desktop", "documents", "downloads", "pictures", "music", "videos", "system32", "syswow64",
        "temp", "microsoft", "windowsapps", "common files", "installer", "local", "roaming",
        "locallow", "programs", "start menu", "package cache", "windows defender", "onedrive",
        "public", "default", "recovery", "boot", "efi", "system volume information"
    };

    public static string Normalize(string name)
    {
        var text = name.Trim().ToLowerInvariant();
        text = Regex.Replace(text, @"\([^)]*\)", " ");
        text = text.Replace('®', ' ').Replace('™', ' ');
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static string? LocationBlock(string folder)
    {
        if (!PathRules.TryFull(folder, out var full))
            return "The path is not valid.";
        if (PathRules.IsDriveRoot(full))
            return "A drive cannot be deleted.";
        if (SafetyClassifier.IsPersonalPath(full))
            return "Personal files are not deleted.";
        if (PathRules.IsUnder(full, KnownFolders.Windows))
            return "Windows folders are not deleted.";
        if (IsSensitive(full))
            return "This system location is not deleted.";
        if (IsStandardAppLocation(full))
            return null;
        return "SafeUninstall will not delete this folder because it sits outside normal app locations (Program Files, AppData, ProgramData, and the Start Menu). You can remove it yourself in File Explorer if you are sure.";
    }

    public static string? DeleteBlock(string folder, bool appInUse)
    {
        if (appInUse)
            return "This app is still installed. Uninstall it on the Applications tab first. Deleting the folder now can break it.";
        if (!PathRules.TryFull(folder, out var full))
            return "The path is not valid.";
        var name = Path.GetFileName(PathRules.TrimEnd(full));
        if (Normalize(name).Length < 3)
            return "That folder name is too short to delete safely.";
        if (BlockedNames.Contains(Normalize(name)))
            return "This folder uses a Windows name, so it will not be deleted.";
        if (SafeDelete.IsTestSandbox(full))
            return null;
        if (PathRules.GetReparse(full) == true)
            return "This is a link or junction. It will not be deleted.";
        var processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (processDir != null && new DirectoryInfo(processDir).Name.Equals("SafeUninstall", StringComparison.OrdinalIgnoreCase)
            && PathRules.IsUnder(full, processDir))
            return "This folder is SafeUninstall.";
        return LocationBlock(full);
    }

    public static List<LeftoverHit> Find(string appName, IReadOnlyList<InstalledApp> apps, CancellationToken ct, IProgress<string>? progress, out string message)
    {
        message = "";
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var main = Normalize(appName);
        if (IsUsefulKey(main))
            keys.Add(main);

        foreach (var app in apps)
        {
            if (!Normalize(app.DisplayName).Equals(main, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(app.InstallLocation))
                continue;
            var folder = Normalize(Path.GetFileName(PathRules.TrimEnd(app.InstallLocation)));
            if (IsUsefulKey(folder))
                keys.Add(folder);
        }

        if (keys.Count == 0)
        {
            message = "That name is too short or too generic, so nothing was searched.";
            return new();
        }

        var hits = new List<LeftoverHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in Candidates(ct, progress))
        {
            ct.ThrowIfCancellationRequested();
            if (!PathRules.TryFull(candidate, out var full))
                continue;
            if (!seen.Add(PathRules.TrimEnd(full)))
                continue;
            if (!keys.Contains(Normalize(Path.GetFileName(PathRules.TrimEnd(full)))))
                continue;

            progress?.Report("Measuring " + full);
            var measured = FileWalk.Measure(full, ct, includeSubdirectories: true, patterns: null);
            var inUse = apps.Any(app => FolderInUse(Path.GetFileName(full), app));
            var block = DeleteBlock(full, inUse);
            hits.Add(new LeftoverHit
            {
                Path = full,
                Bytes = measured.RootDenied ? -1 : measured.Bytes,
                Files = measured.RootDenied ? -1 : measured.Files,
                Partial = measured.Partial,
                BlockReason = block,
                Advice = block == null ? "Leftover — you can delete it" : AdviceFor(block)
            });
            if (hits.Count >= 200)
            {
                message = "Stopped after 200 matching folders.";
                break;
            }
        }

        if (hits.Count == 0 && message.Length == 0)
            message = "No exact leftover folders found. Similar names are ignored so an unrelated folder is not offered for deletion.";
        return hits;
    }

    static string AdviceFor(string block)
    {
        if (block.StartsWith("This app is still installed", StringComparison.Ordinal))
            return "Needed while the app is installed";
        if (block.StartsWith("SafeUninstall will not delete this folder because it sits outside", StringComparison.Ordinal))
            return "Review — not deleted by this app";
        return "Needed — do not delete";
    }

    static bool FolderInUse(string folderName, InstalledApp app)
    {
        var name = Normalize(folderName);
        if (Normalize(app.DisplayName) == name)
            return true;
        if (string.IsNullOrWhiteSpace(app.InstallLocation))
            return false;
        return Normalize(Path.GetFileName(PathRules.TrimEnd(app.InstallLocation))) == name;
    }

    static bool IsUsefulKey(string normalized) =>
        normalized.Length >= 3 && !BlockedNames.Contains(normalized);

    static bool IsStandardAppLocation(string full)
    {
        var parent = Path.GetDirectoryName(PathRules.TrimEnd(full));
        if (string.IsNullOrEmpty(parent))
            return false;
        if (Parents().Any(root => PathRules.Same(parent, root)))
            return true;

        var grand = Path.GetDirectoryName(parent);
        if (string.IsNullOrEmpty(grand))
            return false;
        if (!DeepRoots().Any(root => PathRules.Same(grand, root)))
            return false;
        var parentName = Path.GetFileName(parent);
        return !parentName.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            && !parentName.Equals("Windows", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSensitive(string full)
    {
        string[] tails =
        [
            Path.Combine(KnownFolders.ProgramFiles, "WindowsApps"),
            Path.Combine(KnownFolders.ProgramFilesX86, "WindowsApps"),
            Path.Combine(KnownFolders.ProgramFiles, "Common Files"),
            Path.Combine(KnownFolders.ProgramFilesX86, "Common Files"),
            Path.Combine(KnownFolders.ProgramData, "Package Cache"),
            Path.Combine(KnownFolders.ProgramData, "Microsoft", "Windows"),
            Path.Combine(KnownFolders.ProgramData, "Microsoft", "Windows Defender")
        ];
        return tails.Any(tail => !string.IsNullOrWhiteSpace(tail) && PathRules.IsUnder(full, tail));
    }

    static IEnumerable<string> Parents()
    {
        return new[]
        {
            KnownFolders.ProgramFiles,
            KnownFolders.ProgramFilesX86,
            KnownFolders.ProgramData,
            KnownFolders.LocalAppData,
            KnownFolders.RoamingAppData,
            KnownFolders.LocalLow,
            KnownFolders.StartMenuPrograms,
            KnownFolders.CommonPrograms
        }.Where(path => !string.IsNullOrWhiteSpace(path));
    }

    static IEnumerable<string> DeepRoots()
    {
        return new[]
        {
            KnownFolders.LocalAppData,
            KnownFolders.RoamingAppData,
            KnownFolders.LocalLow,
            KnownFolders.StartMenuPrograms,
            KnownFolders.CommonPrograms
        }.Where(path => !string.IsNullOrWhiteSpace(path));
    }

    static IEnumerable<string> Candidates(CancellationToken ct, IProgress<string>? progress)
    {
        foreach (var root in Parents())
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Looking in " + root);
            foreach (var dir in SafeDirectories(root))
            {
                if (SkipBranch(dir))
                    continue;
                yield return dir;
                if (!DeepRoots().Any(deep => PathRules.Same(root, deep)))
                    continue;
                foreach (var sub in SafeDirectories(dir))
                {
                    if (!SkipBranch(sub))
                        yield return sub;
                }
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;
            progress?.Report("Looking in " + drive.Name);
            foreach (var dir in SafeDirectories(drive.RootDirectory.FullName))
            {
                if (SkipDriveBranch(dir))
                    continue;
                yield return dir;
                foreach (var sub in SafeDirectories(dir))
                {
                    if (!SkipDriveBranch(sub))
                        yield return sub;
                }
            }
        }
    }

    static bool SkipBranch(string dir)
    {
        var name = Path.GetFileName(PathRules.TrimEnd(dir));
        if (name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsSensitive(dir) || PathRules.GetReparse(dir) != false;
    }

    static bool SkipDriveBranch(string dir)
    {
        if (SkipBranch(dir))
            return true;
        if (PathRules.IsUnder(dir, KnownFolders.Windows))
            return true;
        if (PathRules.IsUnder(dir, KnownFolders.ProgramFiles) || PathRules.IsUnder(dir, KnownFolders.ProgramFilesX86))
            return true;
        if (PathRules.IsUnder(dir, KnownFolders.ProgramData))
            return true;
        if (PathRules.IsUnder(dir, Path.Combine(Path.GetPathRoot(dir) ?? "", "Users")))
            return true;
        return SafetyClassifier.IsPersonalPath(dir);
    }

    static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
