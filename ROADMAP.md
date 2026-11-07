# Recommended next additions

## Highest value

1. **Recoverable leftover cleanup.** Move reviewed leftovers to a per-user quarantine, record original paths and timestamps, and offer restore and explicit permanent deletion. Handle cross-volume moves and partial failures. Explain that this cannot undo an official uninstaller.
2. **Preview and exclusions.** Show the exact files eligible for cleanup, let users exclude paths and categories, and apply the same filters again at deletion time. Default temporary-file cleanup to files older than seven days to reduce interference with installers.
3. **Uninstall completion tracking.** Track the launched process, interpret MSI exit codes including reboot-required results, and rescan the app registry before suggesting leftovers. Account for uninstallers that hand off to another process.
4. **Action history.** Store local operation records with bytes actually removed, skipped paths, errors, cancellation, and timestamps. Show partial progress after cancellation and provide a user-controlled report export.
5. **Explain leftover confidence.** Show the evidence for a match: exact install path, publisher, registry entry, and folder name. Detect shared vendor folders and app data used by remaining apps. Never equate a name match with proof that deletion is safe.

## Usability and distribution

- Dashboard with installed-app count, reclaimable storage, scan age, and clearly identified incomplete results.
- Sortable app inventory with filters for publisher, size, Store versus desktop, and protected components; export inventory with the same spreadsheet protections.
- Keyboard and screen-reader testing, high contrast, dark theme, and 125–200% DPI testing.
- Signed installer and binaries, versioned releases, and opt-in updates that verify signatures.
- Optional Windows restore point before an uninstall, with clear reporting when System Protection is unavailable. A restore point is not a backup of personal files.

## Engineering work before broader release

- Automated Windows tests for UAC, redirected known folders, long paths, inaccessible files, locked files, junctions, and cancellation during deletion.
- Handle-based deletion with verified directory/file identities to address directory replacement races.
- Bound scan memory and batch grid updates for large disks; show meaningful progress and partial results.
- Resolve registered uninstall environment variables carefully, validate executable types, and display signature and publisher information. Review network uninstall paths and DLL-hosted uninstallers separately.
- Split filesystem operations and UI into separately testable components. Extend tests to cover the deletion engine directly, including its refusal rules.

Do not add a blanket registry cleaner or automated deletion of loosely matched leftovers. These would undermine the app's safety-focused purpose.
