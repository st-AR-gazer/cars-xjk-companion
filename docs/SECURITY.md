# More Cars Companion security and transparency

Do not trust `MoreCarsCompanion` merely because this document calls it safe.
Review the source, build it yourself if practical, and compare the published
artifact's SHA-256 with the value shown by `cars.xjk.yt`.

The current Windows build is **not digitally signed**. Windows SmartScreen may
therefore show an unknown-publisher warning. A SHA-256 proves that two copies
contain the same bytes; it does not prove that those bytes are harmless.
[WINDOWS_WARNING.md](WINDOWS_WARNING.md) explains the warning, recurring
certificate cost, and build-from-source alternative without presenting signing
as a security audit.

## What the companion can do

The executable has the same filesystem and network permissions as the user who
runs it. Its code deliberately narrows those permissions as follows:

| Area                | Implemented behavior                                                                                                                                                                                                                                                                             |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Commands            | Accepts only `install-release`, `cleanup-managed`, `install-car`, `remove-car`, `apply-skin`, `restore-skin`, `restore-all-skins`, `inspect-installation`, `choose-game`, and Windows `launch-game`. Commands use registered IDs, never arbitrary paths or executable input.                                          |
| Trackmania writes   | Release and skin files must resolve beneath `GameData/Vehicles`. The companion also writes `.morecars/ownership-v1.json` and a short-lived transaction journal at the selected Trackmania root.                                                                                                  |
| Per-user writes     | Copies itself and stores settings/cache below `%LOCALAPPDATA%\Xjk\MoreCarsCompanion` on Windows or the XDG data/config/cache directories on Linux. An operator may override the Windows base with an absolute `MORECARS_DATA_HOME`; the owned `Xjk\MoreCarsCompanion` suffix is always appended. |
| Persistence         | Registers the per-user `morecars://` protocol. No service, scheduled task, startup entry, driver, or Openplanet plugin. The Windows skin runtime stays mapped in an attached game until game exit. Uninstall requires that exit before removing a loaded runtime file. |
| Network             | The shipped configuration uses `https://cars.xjk.yt`. Download paths are derived locally from a pinned release, known hashes, registered car IDs, and skin IDs; commands cannot supply a URL.                                                                                                    |
| Installation safety | Verifies the pinned release manifest, package manifests, downloaded file sizes, and SHA-256 hashes before replacement. It uses partial files, backups, and a recovery journal.                                                                                                                   |
| Cleanup             | Deletes a managed vehicle file only when its current size and SHA-256 match the recorded factory file or recorded skin overlay. Modified and unknown files are retained.                                                                                                                         |
| Game process        | Checks selected executable path, SHA-256, loaded headers, process identity, native fields and callback. Windows loads the embedded, hash-verified DLL and replaces one verified game callback for a skin command; the original callback still runs and is restored afterward. It does not stop or restart Trackmania. |

After each selected Trackmania launch, the paired companion reads the same-origin
release feed and verifies its managed car files. Its desktop notice offers an
explicit install choice. A companion update is downloaded from that origin only
after the choice, checked against the feed's bounded size and SHA-256, then run
through the existing per-user installer. A car install uses the pinned release
manifest and existing file verification. Local edits are preserved and sent to
the website for review instead of being replaced from the notice.

On Linux the companion may launch `zenity` or `kdialog` for native dialogs and
uses `xdg-mime` plus, when available, `update-desktop-database` to register the
protocol handler. These executable names and their argument shapes are fixed in
the source; browser commands cannot choose an executable or shell fragment.
On Windows, `--uninstall` starts a hidden, fixed PowerShell cleanup command after
validating all deletion targets against the companion's owned per-user
directories. Paths are Base64-encoded as data rather than interpolated as shell
syntax. Browser commands cannot start this uninstall path.

## Information sent to the service

Pairing sends the single-use pairing code, a random installation ID, and the
computer name used as the device display name. Later requests use the stored
device bearer token and report command status, progress text, and—after applying
a skin—the downloaded livery's SHA-256 and byte size.

While running, the resident host sends authenticated heartbeats and polls for
commands. Heartbeats report version, capabilities, setup stage, process state,
installation completeness, and verification facts for owned relative paths.
Heartbeats also include up to 16 detected absolute game-folder paths and their
opaque IDs for review by the paired browser/account. Only a locally discovered
ID can be confirmed; the command never supplies a filesystem path.
The companion does **not** upload game files,
unrelated directory listings, or arbitrary local data. It downloads release manifests,
release blobs, and selected skin ZIPs from the configured API origin.

The device token and selected Trackmania path are stored in `settings.json`.
Linux restricts that file to the current user (`0600`). Windows relies on the
current user's inherited profile ACL; the token is not additionally encrypted
with DPAPI. Anyone who obtains the bearer token can impersonate that paired
device until the device is re-paired and the token is replaced.

`ApiOrigin` is also local configuration. The default is `https://cars.xjk.yt`;
the code permits another HTTPS origin or loopback HTTP for development. Editing
the local settings file changes which server the companion trusts.

## Review and build it yourself

The relevant implementation is intentionally small:

- `Program.cs` installs/starts/stops the resident process and accepts protocol activations.
- `CompanionHost.cs` owns serial command execution, startup, heartbeat, and per-user named-pipe activation.
- `CompanionCommandRunner.cs` dispatches fixed capabilities and persists completion receipts.
- `CompanionGame.cs` owns native game selection, selected-process detection, and explicit game launch.
- `CompanionPlatform.cs` owns per-user installation, dialogs, and protocol registration.
- `CompanionApi.cs` defines every network request and enforces one origin.
- `ReleaseInstaller.cs` owns path validation, verification, installation, rollback, and cleanup.
- `SkinArchiveComposer.cs` validates skin ZIP/DDS data and composes a replacement archive locally.
- `CompanionNativeSkins.cs` extracts the embedded runtime, attempts exact-build attachment, and preserves the closed-game fallback.
- `native/` contains the fixed native protocol, game-thread queue, archive/ledger transaction, and recovery implementation. See its README for the evidence and pending clean-game acceptance.

The [privacy policy](PRIVACY.md) describes every transferred field and local
storage location. [WINDOWS_WARNING.md](WINDOWS_WARNING.md) describes why the
Windows build is unsigned and how to verify or rebuild it.

Build checks:

```powershell
.\native\build-runtime.ps1
dotnet build MoreCars.Companion.csproj -f net8.0-windows
dotnet build MoreCars.Companion.csproj -f net8.0
dotnet run --project MoreCars.Companion.csproj -f net8.0 -- --self-test
```

Published binaries should be built from a tagged revision in
<https://github.com/st-AR-gazer/cars-xjk-companion>. Reviewers should compare
the release tag and published artifact hashes rather than assuming the current
default branch still matches an older binary.

## Scope of this review

This document describes the source in this repository. It is not a
formal third-party security audit, a sandbox, or a guarantee that no defect
exists. Report suspected vulnerabilities through the repository's private
security-reporting channel when available; avoid publishing active exploit
details before a fix can be prepared.
