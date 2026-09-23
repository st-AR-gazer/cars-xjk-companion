# More Cars Companion

This source targets the `0.6.5` development preview. It includes a
resident companion, browser setup, and an independent Windows skin runtime.
This GitHub repository contains companion source and its bundled native runtime
source. Car release manifests, vehicle assets, manual ZIPs, and website
downloads are generated in the Cars server workspace.
Native archive replacement and FID refresh have passed in the real game. The
website offers this as a development build; visible next-map skin changes and
clean-game acceptance remain pending. See [GAME_RUNTIME.md](GAME_RUNTIME.md) for evidence and remaining
plugin parity work. Windows builds are
intentionally unsigned; read [WINDOWS_WARNING.md](WINDOWS_WARNING.md) for the
plain-language reason, current certificate pricing, and safer alternatives.

This Windows and Linux helper gives `cars.xjk.yt` a narrow, auditable bridge to
a Trackmania installation without depending on Openplanet. Windows embeds its
own native DLL for the supported game build; Linux uses the closed-game file
path. Linux installations may point at a Trackmania root inside a Wine or
Proton prefix; the companion does not assume a particular launcher or prefix
layout.

Read [SECURITY.md](SECURITY.md) before distributing or running a published
build. It inventories the executable's filesystem, network, persistence, and
privacy behavior, including current limitations such as the unsigned Windows
binary and locally stored bearer token.

The public review repository is
<https://github.com/st-AR-gazer/cars-xjk-companion>.

The project uses [The Unlicense](LICENSE). Review [PRIVACY.md](PRIVACY.md) for
the exact local and network data contract. GitHub Actions builds and tests
Windows and Linux artifacts from reviewed source; the Cars server publishes
its own versioned website downloads with exact hashes.
The 0.6.5 preview uses separate `-0.6.5-dev` download filenames;
it does not replace the preserved CI-built 0.3.1 artifacts. The preview's
matching source archive is available beside it. Official release still requires
real-game visual acceptance.

The companion remains running in the Windows tray and polls its authenticated
command queue. Every browser uses this same transport; browser filesystem APIs
are not an alternative installation path. Closing the browser does not stop a
running operation. Quit from the tray or run `--stop` to end the connection.

After the selected Trackmania process starts, the paired companion checks the
same-origin release feed and verifies its managed car files. A taskbar window
shows release notes for a newer companion or explains missing/changed cars.
**Not now** dismisses that game launch's notice and leaves the game running.
**Install companion update** downloads the newer same-origin Windows executable,
checks its published size and SHA-256, starts it, and confirms when replacement
is complete. **Install car update** installs verified managed files directly
when no local edits block them and then reports the result. Newly installed car
assets may require the next Trackmania start to appear; the companion update
does not require restarting the game. If owned car files have local edits,
**Review modified files** opens the website and preserves those edits.
Run `MoreCarsCompanion.exe --preview-update-window` to see the same desktop
window using a read-only check of the selected game. Both buttons only close
this test preview; it never opens the website or changes car files.
Run `MoreCarsCompanion.exe --preview-update-flow` to preview an example car
update and its completion message. This simulation never downloads or installs.

The catalogue can reset one or all installed car skins to their packaged
defaults. See [SKIN_RESETS.md](SKIN_RESETS.md) for scope, recovery, and the
required service update for version 0.5.1.
No service or startup entry is installed.

The Windows website download is named `MoreCarsCompanion-<version>-<8-character ID>.exe`.
Its full, single-use pairing secret is embedded in the download and expires after
30 minutes. Running that file copies the
executable to its normal per-user name, starts the resident process, claims the
pairing, reports startup, and finds the game. Every wizard step shows loading
feedback according to the setup stage. A single available companion is selected
automatically. The user reviews the detected path, clicks **Use this folder**,
and then **Continue**. Step 3 requires a separate **Install cars** click; neither
pairing, navigation, nor a timer queues downloads. Installation completion stays
visible until **Continue**. Launching the game is also an explicit action.
Renaming the download or adding a browser duplicate suffix does not affect pairing.
Opening it twice reuses its successful connection. If the secret expired, download
again or use **Reconnect companion** in the website. Linux uses that explicit connection step
after extracting and running its archive.

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

The companion never closes Trackmania. On the supported Windows game build,
prepared skins wait until both the map and editor have been exited. The native
game-thread callback commits the archive and ownership record, then refreshes
FIDs before returning to the game. Unsupported or inaccessible game sessions and
Linux retain the closed-game fallback. The website marks a selection installed
only after a verified completion receipt. Expired or cancelled queued changes
cannot commit. Other file operations retain their existing transactional path.

The browser previews the actual ManiaPark DDS textures before sending a skin
command. It separately tracks the previewed and installed skin. Exact model/game
filters select each More Cars vehicle's own ManiaPark liveries; native 2020 bodies
remain view-only. Skin selection, resetting one skin, and resetting all skins use
the same native refresh path. The current game profile is Trackmania 2026.2.2.1751.

## Per-user installation

On Windows the executable, settings, and cache live beneath
`%LOCALAPPDATA%\Xjk\MoreCarsCompanion`. On Linux the executable uses
`$XDG_DATA_HOME`, settings use `$XDG_CONFIG_HOME`, and cached vehicle archives
use `$XDG_CACHE_HOME`, with the standard `~/.local/share`, `~/.config`, and
`~/.cache` fallbacks.

Windows setup checks saved, configured, and standard Ubisoft/Steam folders.
Every valid candidate is shown on the website with its absolute path and an
opaque ID. Detection never opens a native dialog or changes game files. The user
must confirm the desired folder before an install, repair, file recovery, or
launch can run. Only an explicit Browse action opens the native picker;
cancelling keeps the prior selection. A manual picker selection is itself an
explicit folder choice, and setup still displays it for review.

The confirmation is stored locally. Heartbeats include the bounded candidate
list and confirmed folder ID, alongside connection state and owned relative
file inventory. These details are visible only to the paired browser/account.
Website commands carry an ID from that list, never a filesystem path. The
companion rechecks the selected folder before accepting it. Installation jobs
from the wizard are bound to the same selected ID. Command receipts remain
persistent so lost responses do not repeat completed writes.

The Linux build uses `zenity` or `kdialog` for the Trackmania folder picker. If
neither is installed, save the folder during the initial terminal installation:

```bash
./MoreCarsCompanion --install --trackmania-root "/path/to/Trackmania"
```

`MORECARS_TRACKMANIA_ROOT` can also supply the folder non-interactively.

## Factory skins

The website checks the running companion's version and displays a release-notes
modal when a newer EXE is available. See [UPDATES.md](UPDATES.md) for the version
and release publication workflow.

The current source installs `legacy-cars-2026-09-23.4`, whose eleven classic car
archives include the website's solid factory colours. Each car also gets three
256×256 DDS files beside its vehicle ZIP: `<Car>_Icon_Factory.dds` is the
packaged render, `<Car>_Icon_Current.dds` is the selected livery's `Icon.dds`
when available, and `<Car>_Icon.dds` mirrors Current for simple cup lookups.
All three start with the factory render. If a downloaded livery has no supported
icon, Current and the alias use that factory render. **Restore factory skin**
resets both files to it. After updating the EXE, use **Repair files** to add the
sidecars to an existing installation; verified community liveries are kept.
The new release also pins the eleven camera tunings tested with original Cam 1,
2, and 3 plus Stadium Alt Cam 1 and 2. Matching local camera files are adopted
without replacement; unknown edits still require review. Trackmania loads
changed tuning assets on its next start.

## Uninstall

The website's **Manage** tab offers **Uninstall companion** with an optional
**Also remove managed cars** checkbox. Keeping cars is the default. Removing
cars stops on modified-file conflicts so the companion remains available for
repair. Full companion removal deletes its app directory, settings, cache, and
protocol registration. A temporary cleanup process waits for the host to exit,
verifies removal, then reports completion and removes itself. The API revokes
that device's pairing only after the completion report. Merely going offline
is not treated as a successful uninstall.

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

The CLI keeps managed Trackmania cars. On the website, select **Also remove
managed cars** when those files should be removed too.

Build locally with:

```powershell
.\native\build-runtime.ps1
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

GitHub Actions builds and tests Windows and Linux artifacts from this source.
The Cars server publishes its own versioned website downloads with exact
sizes and SHA-256 hashes. The Windows EXE remains unsigned.
