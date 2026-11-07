namespace SafeUninstall;

static class PathRules
{
    public static bool TryFull(string? path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string TrimEnd(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool Same(string a, string b)
    {
        if (!TryFull(a, out var left) || !TryFull(b, out var right))
            return false;
        return string.Equals(TrimEnd(left), TrimEnd(right), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnder(string path, string root)
    {
        if (!TryFull(path, out var full) || !TryFull(root, out var basePath))
            return false;
        if (Same(full, basePath))
            return true;
        var prefix = TrimEnd(basePath) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyInside(string path, string root) =>
        IsUnder(path, root) && !Same(path, root);

    public static bool IsDriveRoot(string path)
    {
        if (!TryFull(path, out var full))
            return false;
        var root = Path.GetPathRoot(full);
        return root != null && Same(full, root);
    }

    public static bool? GetReparse(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return null;
        }
    }

    // Check the full ancestry: a normal-looking child can live below a junction.
    public static bool HasUnsafeAncestry(string path)
    {
        if (!TryFull(path, out var full))
            return true;
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            if (GetReparse(current) != false)
                return true;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    public static bool AncestorIsReparseOrUnknown(string root, string path)
    {
        if (!TryFull(root, out var rootFull) || !TryFull(path, out var full))
            return true;

        var stop = TrimEnd(rootFull);
        var current = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(current))
        {
            var trimmed = TrimEnd(current);
            if (trimmed.Length < stop.Length)
                break;
            if (GetReparse(current) != false)
                return true;
            if (string.Equals(trimmed, stop, StringComparison.OrdinalIgnoreCase))
                return false;
            current = Path.GetDirectoryName(trimmed);
        }

        return false;
    }
}

static class NamePattern
{
    public static bool Matches(string filePath, string[]? patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return true;

        var name = Path.GetFileName(filePath);
        foreach (var pattern in patterns)
        {
            var star = pattern.IndexOf('*');
            if (star < 0)
            {
                if (name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            var prefix = pattern[..star];
            var suffix = pattern[(star + 1)..];
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && name.Length >= prefix.Length + suffix.Length)
                return true;
        }

        return false;
    }
}
