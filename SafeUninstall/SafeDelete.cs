namespace SafeUninstall;

// Deletes only inside a folder that already passed the safety checks.
// Never uses recursive directory delete, and never follows a link or junction.
static class SafeDelete
{
    public static string? RefusalReason(string root, DeleteKind kind)
    {
        if (!PathRules.TryFull(root, out var full))
            return "The path is not valid.";
        if (PathRules.IsDriveRoot(full))
            return "A drive cannot be deleted.";
        if (SafetyClassifier.IsPersonalPath(full))
            return "Personal files cannot be deleted.";
        if (PathRules.HasUnsafeAncestry(full))
            return "The folder or one of its parents is a link, junction, or cannot be verified.";
        if (IsOurInstallFolder(full))
            return "That folder is SafeUninstall.";

        if (kind == DeleteKind.TestSandbox)
            return IsTestSandbox(full) ? null : "Test cleanup is only allowed in a SafeUninstall test folder.";

        if (kind == DeleteKind.Junk)
        {
            if (!JunkCatalog.IsApproved(full))
                return "This folder is not on the safe cleanup list.";
            return null;
        }

        if (PathRules.IsUnder(full, KnownFolders.Windows))
            return "Windows folders cannot be deleted.";
        return LeftoverScanner.LocationBlock(full);
    }

    public static DeleteReport DeleteTree(
        string root,
        DeleteKind kind,
        bool deleteRoot,
        CancellationToken ct,
        string[]? patterns = null,
        bool includeSubdirectories = true)
    {
        var refusal = RefusalReason(root, kind);
        if (refusal != null)
            throw new InvalidOperationException(refusal);

        var full = Path.GetFullPath(root);
        if (IsExplorerCache(full) && (patterns == null || patterns.Length == 0))
            throw new InvalidOperationException("Refusing to clear the Explorer folder without a cache file filter.");

        var windowsJunkRoots = JunkCatalog.ApprovedPaths()
            .Where(path => PathRules.IsUnder(path, KnownFolders.Windows))
            .ToArray();

        var report = new DeleteReport();
        DeleteInside(full, full, kind, ct, patterns, includeSubdirectories, windowsJunkRoots, report, 0);
        if (deleteRoot && report.Skipped == 0 && !PathRules.HasUnsafeAncestry(full))
        {
            try
            {
                Directory.Delete(full, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (report.Errors.Count < 8)
                    report.Errors.Add(ex.Message);
            }
        }

        return report;
    }

    static void DeleteInside(
        string dir,
        string root,
        DeleteKind kind,
        CancellationToken ct,
        string[]? patterns,
        bool includeSubdirectories,
        string[] windowsJunkRoots,
        DeleteReport report,
        int depth)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 80 || PathRules.HasUnsafeAncestry(dir))
        {
            report.Skipped++;
            return;
        }

        List<string> files = new();
        List<string> folders = new();
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    report.Skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    report.Skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                    folders.Add(entry);
                else
                    files.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Skipped++;
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!NamePattern.Matches(file, patterns))
                continue;
            try
            {
                EnsureFile(file, root, kind, windowsJunkRoots);
                if (PathRules.HasUnsafeAncestry(Path.GetDirectoryName(file)!))
                {
                    report.Skipped++;
                    continue;
                }

                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    report.Skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);

                long length = 0;
                try { length = new FileInfo(file).Length; } catch { /* size is only for the report */ }
                File.Delete(file);
                report.DeletedFiles++;
                report.DeletedBytes += length;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Skipped++;
                if (report.Errors.Count < 8)
                    report.Errors.Add(Path.GetFileName(file) + ": " + ex.Message);
            }
        }

        if (!includeSubdirectories)
            return;

        foreach (var folder in folders)
        {
            if (PathRules.GetReparse(folder) != false)
            {
                report.Skipped++;
                continue;
            }

            DeleteInside(folder, root, kind, ct, patterns, true, windowsJunkRoots, report, depth + 1);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (PathRules.HasUnsafeAncestry(folder))
                {
                    report.Skipped++;
                    continue;
                }
                Directory.Delete(folder, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The folder still has a locked file or a link we refused to follow.
            }
        }
    }

    static void EnsureFile(string file, string root, DeleteKind kind, IReadOnlyList<string> windowsJunkRoots)
    {
        if (!PathRules.TryFull(file, out var full))
            throw new InvalidOperationException("Blocked an invalid path.");
        if (!PathRules.IsStrictlyInside(full, root))
            throw new InvalidOperationException("Blocked a path outside the selected folder.");
        if (SafetyClassifier.IsPersonalPath(full))
            throw new InvalidOperationException("Blocked a personal file.");

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && PathRules.Same(full, exe))
            throw new InvalidOperationException("Blocked the running program.");

        if (PathRules.IsUnder(full, KnownFolders.Windows))
        {
            var allowed = kind == DeleteKind.Junk && windowsJunkRoots.Any(junk => PathRules.IsUnder(full, junk));
            if (!allowed)
                throw new InvalidOperationException("Blocked a Windows file.");
        }
    }

    public static bool IsTestSandbox(string path)
    {
        if (!PathRules.TryFull(path, out var full))
            return false;
        var name = new DirectoryInfo(full).Name;
        return name.StartsWith("SafeUninstallTest_", StringComparison.Ordinal)
            && PathRules.IsStrictlyInside(full, Path.GetTempPath());
    }

    static bool IsOurInstallFolder(string path)
    {
        if (FolderNamedSafeUninstall(AppContext.BaseDirectory, path))
            return true;
        var processDir = Path.GetDirectoryName(Environment.ProcessPath);
        return processDir != null && FolderNamedSafeUninstall(processDir, path);
    }

    static bool FolderNamedSafeUninstall(string folder, string path)
    {
        if (!PathRules.TryFull(folder, out var full))
            return false;
        var name = new DirectoryInfo(full).Name;
        return name.Equals("SafeUninstall", StringComparison.OrdinalIgnoreCase) && PathRules.IsUnder(path, full);
    }

    static bool IsExplorerCache(string path)
    {
        var explorer = Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Windows", "Explorer");
        return PathRules.Same(path, explorer);
    }
}
