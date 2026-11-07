global using System.Text.Json;
using SafeUninstall;
int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
Check(CsvReport.Cell("=1+1") == "\"'=1+1\"", "Formula escaping");
Check(CsvReport.Cell("  @SUM(A1)") == "\"'  @SUM(A1)\"", "Whitespace formula escaping");
Check(CsvReport.Cell("a,\"b") == "\"a,\"\"b\"", "CSV quote escaping");
Check(CsvReport.Cell("\tvalue") == "\"'\tvalue\"", "Tab escaping");
Check(UpdateService.ParseVersion("v1.2.0") == new Version(1, 2, 0), "Release tag parsing");
Check(UpdateService.ParseVersion("1.2.0.0") == new Version(1, 2, 0), "Assembly version normalization");
Check(UpdateService.ParseVersion("v1.2.0-beta") == null, "Prerelease tag rejected");
var hash = new string('a', 64);
Check(UpdateService.ChecksumMatches(hash + "  SafeUninstall-win-x64.zip\r\n", hash.ToUpperInvariant()), "Checksum manifest match");
Check(!UpdateService.ChecksumMatches(hash + "  other.zip", hash), "Wrong asset checksum rejected");
Check(!UpdateService.ChecksumMatches(hash + "  SafeUninstall-win-x64.zip", new string('b', 64)), "Corrupt update rejected");
Check(!UpdateService.ChecksumMatches(hash + "  SafeUninstall-win-x64.zip\n" + hash + "  SafeUninstall-win-x64.zip", hash), "Ambiguous checksums rejected");
var root = Path.Combine(Path.GetTempPath(), "SafeUninstallRegression_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var actual = Directory.CreateDirectory(Path.Combine(root, "actual"));
    Directory.CreateDirectory(Path.Combine(actual.FullName, "child"));
    File.WriteAllText(Path.Combine(actual.FullName, "a.tmp"), "12345");
    File.WriteAllText(Path.Combine(actual.FullName, "b.txt"), "abc");
    Check(!PathRules.HasUnsafeAncestry(actual.FullName), "Ordinary ancestry");
    Check(PathRules.HasUnsafeAncestry(Path.Combine(root, "missing")), "Unknown ancestry fails closed");
    var link = Path.Combine(root, "link");
    Directory.CreateSymbolicLink(link, actual.FullName);
    Check(PathRules.HasUnsafeAncestry(Path.Combine(link, "child")), "Link above selected folder");
    var result = FileWalk.Measure(actual.FullName, CancellationToken.None, true, ["*.tmp"]);
    Check(result.Files == 1 && result.Bytes == 5 && !result.Partial, "Filtered measurement");
    var blocked = FileWalk.Measure(Path.Combine(link, "child"), CancellationToken.None, true, null);
    Check(blocked.RootDenied && blocked.Partial, "Scan through ancestor link blocked");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { FileWalk.Measure(actual.FullName, cts.Token, true, null); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { checks++; }
    Directory.Delete(link);
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Passed {checks} regression checks.");
