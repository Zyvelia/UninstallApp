using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace SafeUninstall;

sealed record AppUpdate(Version Version, string Notes, Uri Download, Uri Checksums);

static class UpdateService
{
    const string AssetName = "SafeUninstall-win-x64.zip";
    const string ReleaseRoot = "https://github.com/Zyvelia/UninstallApp/releases/download/";
    static readonly HttpClient Client = CreateClient();
    public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SafeUninstall-Updater/1.2");
        return client;
    }

    public static Version? ParseVersion(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var version)
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build)) : null;

    static Uri AssetUri(JsonElement asset)
    {
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        if (!url.StartsWith(ReleaseRoot, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidDataException("The release contains an unexpected download address.");
        return uri;
    }

    public static async Task<AppUpdate?> CheckAsync(CancellationToken ct)
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/Zyvelia/UninstallApp/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        var version = ParseVersion(release.GetProperty("tag_name").GetString() ?? "");
        if (version == null || version <= ParseVersion(CurrentVersion.ToString()))
            return null;
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var zip = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == AssetName);
        var sums = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
        if (zip.ValueKind == JsonValueKind.Undefined || sums.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The new release is missing its update ZIP or checksum. Open GitHub Releases to check it.");
        return new AppUpdate(version, release.GetProperty("body").GetString() ?? "", AssetUri(zip), AssetUri(sums));
    }

    public static bool ChecksumMatches(string manifest, string actual)
    {
        var lines = manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var matches = lines.Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == AssetName).ToArray();
        return matches.Length == 1 && matches[0][0].Length == 64
            && matches[0][0].Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> DownloadAsync(AppUpdate update, IProgress<string> progress, CancellationToken ct)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeUninstall", "Updates", update.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var zipPath = Path.Combine(folder, "update.zip");
            progress.Report("Downloading update…");
            using (var response = await Client.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zipPath);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > 600L * 1024 * 1024) throw new InvalidDataException("Update download exceeds the size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress.Report($"Downloaded {total / 1024 / 1024} MB…");
                }
            }
            progress.Report("Verifying update…");
            var manifest = await Client.GetStringAsync(update.Checksums, ct);
            await using (var stream = File.OpenRead(zipPath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (!ChecksumMatches(manifest, hash)) throw new InvalidDataException("Update checksum did not match. Nothing was installed.");
            }
            progress.Report("Extracting update…");
            var install = Path.Combine(folder, "app");
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                if (zip.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Extracted update exceeds the size limit.");
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = Path.GetFullPath(Path.Combine(install, entry.FullName));
                    if (!PathRules.IsStrictlyInside(destination, install))
                        throw new InvalidDataException("Update contains an unsafe archive path.");
                    if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, overwrite: false);
                    }
                }
            }, ct);
            var exe = Path.Combine(install, "SafeUninstall.exe");
            if (!File.Exists(exe)) throw new InvalidDataException("Update does not contain SafeUninstall.exe.");
            File.Delete(zipPath);
            return exe;
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort staging cleanup */ }
            throw;
        }
    }
}
