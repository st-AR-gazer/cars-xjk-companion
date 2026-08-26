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

| Area | Implemented behavior |
| --- | --- |
| Commands | Accepts only `install-release`, `cleanup-managed`, `apply-skin`, and `restore-skin`. Protocol URLs contain only a single-use pairing secret or an opaque command ID. |
| Trackmania writes | Release and skin files must resolve beneath `GameData/Vehicles`. The companion also writes `.morecars/ownership-v1.json` and a short-lived transaction journal at the selected Trackmania root. |
| Per-user writes | Copies itself and stores settings/cache below `%LOCALAPPDATA%\Xjk\MoreCarsCompanion` on Windows or the XDG data/config/cache directories on Linux. An operator may override the Windows base with an absolute `MORECARS_DATA_HOME`; the owned `Xjk\MoreCarsCompanion` suffix is always appended. |
| Persistence | Registers the per-user `morecars://` protocol. It does not install a service, scheduled task, startup entry, driver, Openplanet plugin, or code inside Trackmania. `--uninstall` removes the registration and installed app. |
| Network | The shipped configuration uses `https://cars.xjk.yt`. Download paths are derived locally from a pinned release, known hashes, registered car IDs, and skin IDs; commands cannot supply a URL. |
| Installation safety | Verifies the pinned release manifest, package manifests, downloaded file sizes, and SHA-256 hashes before replacement. It uses partial files, backups, and a recovery journal. |
| Cleanup | Deletes a managed vehicle file only when its current size and SHA-256 match the recorded factory file or recorded skin overlay. Modified and unknown files are retained. |
| Game process | Does not inject into, inspect, stop, or restart Trackmania. A locked file causes rollback and an error. |

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

The companion does **not** upload the selected Trackmania path, game files,
directory listings, or arbitrary local data. It downloads release manifests,
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

- `Program.cs` accepts protocol activations and dispatches the four commands.
- `CompanionPlatform.cs` owns per-user installation, dialogs, and protocol registration.
- `CompanionApi.cs` defines every network request and enforces one origin.
- `ReleaseInstaller.cs` owns path validation, verification, installation, rollback, and cleanup.
- `SkinArchiveComposer.cs` validates skin ZIP/DDS data and composes a replacement archive locally.

The [privacy policy](PRIVACY.md) describes every transferred field and local
storage location. [WINDOWS_WARNING.md](WINDOWS_WARNING.md) describes why the
Windows build is unsigned and how to verify or rebuild it.

Build checks:

```powershell
dotnet build MoreCars.Companion.csproj -f net8.0-windows
dotnet build MoreCars.Companion.csproj -f net8.0
dotnet run --project MoreCars.Companion.csproj -f net8.0 -- --self-test
```

Published binaries should be built from a tagged revision in
<https://github.com/st-AR-gazer/cars-xjk-companion>. Reviewers should compare
the release tag and published artifact hashes rather than assuming the current
default branch still matches an older binary.

## Scope of this review

This document describes the source currently in this directory. It is not a
formal third-party security audit, a sandbox, or a guarantee that no defect
exists. Report suspected vulnerabilities through the repository's private
security-reporting channel when available; avoid publishing active exploit
details before a fix can be prepared.
