# SafeUninstall

A Windows desktop tool for reviewing installed apps, launching their registered uninstallers, inspecting possible leftovers, and cleaning selected cache folders. Built with Windows Forms and .NET 10.

## Version 1.1 improvements

- Cleanup checks every parent directory for links, junctions, and unreadable paths. This closes the case where the selected directory itself is ordinary but a parent redirects elsewhere. Directory ancestry is checked again during cleanup.
- Cache measurement now reports access failures instead of silently omitting inaccessible directories. Reaching the traversal depth limit marks the result partial.
- File reports use a Save As dialog with overwrite confirmation instead of automatically overwriting a Desktop file. Exported text is escaped for CSV and protected against common spreadsheet formula prefixes.
- Store app removal uses the system PowerShell path and terminating errors so a failed removal is less likely to be reported as success.
- Source-only repository, regression checks, and GitHub Actions builds with downloadable self-contained Windows x64 artifacts.

## Build and run

On Windows, install the .NET 10 SDK:

```powershell
dotnet run --project SafeUninstall/SafeUninstall.csproj
dotnet publish SafeUninstall/SafeUninstall.csproj -c Release -r win-x64 --self-contained true -o publish
```

Run `publish/SafeUninstall.exe`. The self-contained publish does not require the user to install .NET. GitHub Actions also produces a `SafeUninstall-win-x64` artifact under each successful workflow run. Download and extract the complete artifact before launching the executable.

## Verification

```sh
dotnet run --project RegressionTests/RegressionTests.csproj -c Release
```

These portable regression checks cover CSV escaping, ancestor links, unknown ancestry, filtered measurements, and cancellation. The filesystem link tests run on Linux in CI. On Linux, compilation of the Windows app requires `-p:EnableWindowsTargeting=true`; running the interface requires Windows.

The existing Windows-only diagnostic is available via `SafeUninstall.exe --self-test`. It creates and removes its own temporary test files and junction, inspects installed applications, and writes a report in the temporary directory. Run it on a disposable Windows test machine. Actual uninstall and Store removal, UAC, locked files, and the interface still require Windows validation.

## Behavior and limits

Cleanup permanently deletes selected files; it is not an undo system. Leftover name matches require human review and do not establish that files are unused. Directory checks reduce link traversal risk but do not provide handle-based protection against an adversarial process swapping directories between checks. Close affected apps before cleaning caches. See [ROADMAP.md](ROADMAP.md) for the next improvements.
