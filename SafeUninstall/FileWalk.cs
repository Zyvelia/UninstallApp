namespace SafeUninstall;

static class FileWalk
{
    public sealed class WalkResult
    {
        public long Bytes { get; set; }
        public long Files { get; set; }
        public bool RootDenied { get; set; }
        public bool Partial { get; set; }
    }

    public static WalkResult Measure(string root, CancellationToken ct, bool includeSubdirectories, string[]? patterns)
    {
        var result = new WalkResult();
        if (PathRules.GetReparse(root) != false)
        {
            result.RootDenied = PathRules.GetReparse(root) == null;
            return result;
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        var first = true;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            if (PathRules.HasUnsafeAncestry(dir))
            {
                if (first) result.RootDenied = true;
                result.Partial = true;
                first = false;
                continue;
            }
            IEnumerator<string>? enumerator = null;
            try
            {
                enumerator = Directory.EnumerateFileSystemEntries(dir, "*", options).GetEnumerator();
                while (true)
                {
                    string entry;
                    try
                    {
                        if (!enumerator.MoveNext())
                            break;
                        entry = enumerator.Current;
                    }
                    catch (Exception ex) when (IsAccess(ex))
                    {
                        result.Partial = true;
                        break;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch
                    {
                        result.Partial = true;
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (includeSubdirectories && depth < 80)
                            stack.Push((entry, depth + 1));
                        else if (includeSubdirectories)
                            result.Partial = true;
                        continue;
                    }

                    if (!NamePattern.Matches(entry, patterns))
                        continue;

                    try
                    {
                        result.Bytes += new FileInfo(entry).Length;
                        result.Files++;
                    }
                    catch
                    {
                        result.Partial = true;
                    }
                }
            }
            catch (Exception ex) when (IsAccess(ex))
            {
                if (first)
                    result.RootDenied = true;
                else
                    result.Partial = true;
            }
            finally
            {
                enumerator?.Dispose();
            }

            first = false;
        }

        return result;
    }

    static bool IsAccess(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
