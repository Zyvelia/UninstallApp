namespace SafeUninstall;

sealed class FolderBucket
{
    public required string Path { get; init; }
    public long Bytes { get; set; }
    public long Files { get; set; }
    public long SafeBytes { get; set; }
    public SafetyLevel Advice { get; set; }
    public string Reason { get; set; } = "";

    public string DisplayReason()
    {
        if (SafeBytes > 0 && Advice != SafetyLevel.SafeToDelete)
            return Reason + " Safe cache inside: " + SizeText.Format(SafeBytes) + ". Remove that from Junk and caches. Deleting this whole folder would also remove settings.";
        return Reason;
    }
}

sealed class BrowseEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Bytes { get; init; } = -1;
    public SafetyLevel Level { get; init; }
    public string Reason { get; init; } = "";
    public bool BlockedLink { get; init; }
}

sealed class FileScanResult
{
    public List<FolderBucket> Buckets { get; } = new();
    public long[] BytesBySafety { get; } = new long[6];
    public long TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public long UnreadableDirectories { get; set; }
    public long SkippedLinks { get; set; }
    public List<string> DriveLines { get; } = new();
    public bool Cancelled { get; set; }

    public long BytesOf(SafetyLevel level) => BytesBySafety[(int)level];

    public string Summary()
    {
        return string.Join("    ", new[]
        {
            "Needed " + SizeText.Format(BytesOf(SafetyLevel.Required)),
            "Other apps " + SizeText.Format(BytesOf(SafetyLevel.Keep)),
            "Your files " + SizeText.Format(BytesOf(SafetyLevel.YourFiles)),
            "Program folders " + SizeText.Format(BytesOf(SafetyLevel.Uninstallable)),
            "Review " + SizeText.Format(BytesOf(SafetyLevel.Review)),
            "Safe cache " + SizeText.Format(BytesOf(SafetyLevel.SafeToDelete))
        });
    }
}

static class FileSafetyScanner
{
    public static FileScanResult Scan(bool includeRemovable, CancellationToken ct, IProgress<string>? progress)
    {
        var result = new FileScanResult();
        var snap = FolderSnapshot.Capture();
        var buckets = new Dictionary<string, FolderBucket>(StringComparer.OrdinalIgnoreCase);
        long files = 0;
        long dirs = 0;
        var lastReport = Environment.TickCount64;

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            if (!drive.IsReady)
                continue;
            var wanted = drive.DriveType == DriveType.Fixed
                || (includeRemovable && drive.DriveType == DriveType.Removable);
            if (!wanted)
                continue;

            long used = 0;
            try { used = drive.TotalSize - drive.AvailableFreeSpace; } catch { /* some drives refuse size */ }
            result.DriveLines.Add($"{drive.Name}  {SizeText.Format(used)} used");
            try
            {
                Walk(drive.RootDirectory.FullName, snap, buckets, result, ct, progress, ref files, ref dirs, ref lastReport);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
        }

        result.Buckets.AddRange(buckets.Values.OrderByDescending(bucket => bucket.Bytes));
        return result;
    }

    static void Walk(
        string root,
        FolderSnapshot snap,
        Dictionary<string, FolderBucket> buckets,
        FileScanResult result,
        CancellationToken ct,
        IProgress<string>? progress,
        ref long files,
        ref long dirs,
        ref long lastReport)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            if (depth > 80)
                continue;
            dirs++;
            var now = Environment.TickCount64;
            if (now - lastReport > 200)
            {
                lastReport = now;
                progress?.Report($"Reading files: {files:N0}    folders: {dirs:N0}    {Shorten(dir)}");
            }

            List<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.UnreadableDirectories++;
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    result.UnreadableDirectories++;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    result.SkippedLinks++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (TrySkipTree(entry, buckets, snap))
                        continue;
                    stack.Push((entry, depth + 1));
                    continue;
                }

                long length;
                try { length = new FileInfo(entry).Length; }
                catch { result.UnreadableDirectories++; continue; }

                var (level, _) = SafetyClassifier.Classify(entry, snap);
                var bucketPath = BucketFor(entry, snap);
                if (!buckets.TryGetValue(bucketPath, out var bucket))
                {
                    var (advice, reason) = SafetyClassifier.Classify(bucketPath, snap);
                    bucket = new FolderBucket
                    {
                        Path = bucketPath,
                        Advice = advice,
                        Reason = reason
                    };
                    buckets.Add(bucketPath, bucket);
                }

                bucket.Bytes += length;
                bucket.Files++;
                if (level == SafetyLevel.SafeToDelete)
                    bucket.SafeBytes += length;
                result.BytesBySafety[(int)level] += length;
                result.TotalBytes += length;
                result.TotalFiles++;
                files++;
            }
        }
    }

    static bool TrySkipTree(string dir, Dictionary<string, FolderBucket> buckets, FolderSnapshot snap)
    {
        var name = Path.GetFileName(PathRules.TrimEnd(dir));
        var root = Path.GetPathRoot(dir) ?? "";
        var skip = name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
            || (PathRules.Same(Path.GetDirectoryName(PathRules.TrimEnd(dir)) ?? "", root)
                && name.Equals("Recovery", StringComparison.OrdinalIgnoreCase));
        if (!skip)
            return false;

        if (!buckets.ContainsKey(dir))
        {
            var (level, reason) = SafetyClassifier.Classify(dir, snap);
            buckets.Add(dir, new FolderBucket
            {
                Path = dir,
                Advice = level,
                Reason = reason + " This folder was not opened. Its size is on the Junk and caches tab when it is the Recycle Bin.",
                Bytes = -1,
                Files = -1
            });
        }

        return true;
    }

    static string BucketFor(string file, FolderSnapshot snap)
    {
        var full = Path.GetFullPath(file);
        if (PathRules.IsUnder(full, snap.Windows))
            return snap.Windows;
        foreach (var personal in snap.Personal)
        {
            if (!string.IsNullOrWhiteSpace(personal) && PathRules.IsUnder(full, personal))
                return Path.GetFullPath(personal);
        }

        if (TryChild(full, snap.ProgramFiles, out var child))
            return child;
        if (!PathRules.Same(snap.ProgramFiles, snap.ProgramFilesX86) && TryChild(full, snap.ProgramFilesX86, out child))
            return child;
        if (TryChild(full, snap.ProgramData, out child))
            return child;
        if (TryChild(full, snap.LocalAppData, out child))
            return child;
        if (TryChild(full, snap.RoamingAppData, out child))
            return child;
        return TopOrSelf(full);
    }

    static bool TryChild(string full, string root, out string child)
    {
        child = "";
        if (string.IsNullOrWhiteSpace(root) || !PathRules.IsUnder(full, root) || PathRules.Same(full, root))
            return false;
        var relative = full[PathRules.TrimEnd(Path.GetFullPath(root)).Length..].TrimStart(Path.DirectorySeparatorChar);
        var first = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first))
            return false;
        child = Path.Combine(root, first);
        return true;
    }

    static string TopOrSelf(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var relative = full[root.Length..];
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return full;
        return Path.Combine(root, parts[0]);
    }

    public static List<BrowseEntry> ListLevel(string path, CancellationToken ct)
    {
        var entries = new List<BrowseEntry>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            ct.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(entry); }
            catch { continue; }

            var link = (attributes & FileAttributes.ReparsePoint) != 0;
            var directory = (attributes & FileAttributes.Directory) != 0;
            var classified = link
                ? (SafetyLevel.Review, "This is a link or junction. It was not opened, so the target is not mixed in with this folder.")
                : SafetyClassifier.ClassifyPath(directory ? entry : entry);

            long bytes = -2;
            if (!directory && !link)
            {
                try { bytes = new FileInfo(entry).Length; }
                catch { bytes = -1; }
            }

            entries.Add(new BrowseEntry
            {
                Name = Path.GetFileName(entry),
                FullPath = entry,
                IsDirectory = directory,
                Bytes = bytes,
                Level = classified.Item1,
                Reason = classified.Item2,
                BlockedLink = link
            });
            if (entries.Count >= 8000)
                break;
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static string Shorten(string path) => path.Length <= 120 ? path : "…" + path[^117..];
}
