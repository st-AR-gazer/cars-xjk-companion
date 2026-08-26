# More Cars Companion

The source on `main` targets the `0.3.1` release. Windows builds are
intentionally unsigned; read [WINDOWS_WARNING.md](WINDOWS_WARNING.md) for the
plain-language reason, current certificate pricing, and safer alternatives.

This Windows and Linux helper gives `cars.xjk.yt` a narrow, auditable bridge to
a Trackmania installation without loading code into the game or depending on
Openplanet. Linux installations may point at a Trackmania root inside a Wine or
Proton prefix; the companion does not assume a particular launcher or prefix
layout.

Read [SECURITY.md](SECURITY.md) before distributing or running a published
build. It inventories the executable's filesystem, network, persistence, and
privacy behavior, including current limitations such as the unsigned Windows
binary and locally stored bearer token.

The public review repository is
<https://github.com/st-AR-gazer/cars-xjk-companion>.

The project uses [The Unlicense](LICENSE). Review [PRIVACY.md](PRIVACY.md) for
the exact local and network data contract. GitHub Actions publishes and tests
the self-contained Windows and Linux artifacts from the reviewed repository
source; a locally built EXE is never substituted for the website download.

The companion registers the per-user `morecars://` protocol through the Windows
registry or an XDG desktop entry on Linux. Pairing does not require an account:
an unsigned browser uses a random local identity, while a signed-in browser may
link the companion to its xjk account. Protocol URIs carry only a single-use
pairing secret or an opaque command ID. They never carry a filesystem path,
download URL, or executable input.

Managed files are installed only under `GameData/Vehicles`. The canonical
ownership ledger is `<Trackmania>/.morecars/ownership-v1.json`. Cleanup removes
only current bytes that match the ledger's release file or recorded skin
overlay, preserves modifications, and prunes only empty descendants beneath
`GameData/Vehicles`.

The companion never closes Trackmania or waits for the game process to exit. It
uses transactional replacements while the game is running. If the operating
system reports a locked archive, the operation rolls back and can be retried
later without leaving a partial installation.

## Per-user installation

On Windows the executable, settings, and cache live beneath
`%LOCALAPPDATA%\Xjk\MoreCarsCompanion`. On Linux the executable uses
`$XDG_DATA_HOME`, settings use `$XDG_CONFIG_HOME`, and cached vehicle archives
use `$XDG_CACHE_HOME`, with the standard `~/.local/share`, `~/.config`, and
`~/.cache` fallbacks.

The Linux build uses `zenity` or `kdialog` for the Trackmania folder picker. If
neither is installed, save the folder during the initial terminal installation:

```bash
./MoreCarsCompanion --install --trackmania-root "/path/to/Trackmania"
```

`MORECARS_TRACKMANIA_ROOT` can also supply the folder non-interactively.

## Uninstall

Remove the installed companion, `morecars://` registration, settings, and cache
from Windows PowerShell:

```powershell
& "$env:LOCALAPPDATA\Xjk\MoreCarsCompanion\MoreCarsCompanion.exe" --uninstall
```

On Linux:

```bash
"${XDG_DATA_HOME:-$HOME/.local/share}/Xjk/MoreCarsCompanion/MoreCarsCompanion" --uninstall
```

Append `--keep-data` to either command to preserve pairing settings and cache
while removing the executable and protocol handler.

For automated verification or managed scripts, append `--quiet` to `--install`
or `--uninstall`. This suppresses informational dialogs but does not change the
files, settings, or protocol-registration behavior. On Windows,
`MORECARS_DATA_HOME` can override the local application-data base directory;
the companion always appends its owned `Xjk\MoreCarsCompanion` directory.

Uninstalling the app intentionally leaves managed Trackmania cars alone. Use
**Remove the managed cars** on `cars.xjk.yt` before uninstalling when those files
should also be removed.

Build locally with:

```powershell
dotnet restore MoreCars.Companion.csproj --ignore-failed-sources
dotnet build MoreCars.Companion.csproj -c Release -f net8.0-windows --no-restore
dotnet build MoreCars.Companion.csproj -c Release -f net8.0 --no-restore
```

The release publisher uses `publish.ps1` to create a self-contained, single-file
Windows executable or a Linux tarball under `artifacts/`. The self-contained
runtime packs must be available during publish:

```powershell
.\publish.ps1 -Runtime win-x64
.\publish.ps1 -Runtime linux-x64
```

Official Windows releases are built by GitHub Actions after CI has exercised
the real install, protocol-registration, keep-data, and full-uninstall
lifecycle. The Windows EXE remains unsigned and is published with its exact
SHA-256. Locally published artifacts are development builds and must not be
substituted for the CI-built website download.
