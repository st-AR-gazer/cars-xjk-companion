# Security notes

The Windows EXE is unsigned and may trigger SmartScreen. Review the source and compare downloaded SHA-256 hashes with those published by `cars.xjk.yt`. A matching hash confirms identical bytes, not safety; see the [Windows warning](WINDOWS_WARNING.md).

The companion runs with the current user's permissions, with these limits:

| Area | Behavior |
| --- | --- |
| Commands | Accepts registered car, skin, install, cleanup, inspection, game-selection, and Windows game-launch actions. Website commands use IDs, not arbitrary paths or shell input. |
| Game files | Writes only under the selected `GameData/Vehicles` plus its ownership ledger and temporary journal. Downloads and existing owned files are checked by size and SHA-256. |
| Local app files | Installs per user under `%LOCALAPPDATA%\Xjk\MoreCarsCompanion` on Windows or standard XDG data, config, and cache paths on Linux. |
| Cleanup | Removes a managed file only when its current bytes match the recorded factory file or skin overlay. Unknown edits remain. |
| Network | Uses `https://cars.xjk.yt` by default. Download paths come from pinned releases and registered IDs; commands cannot supply URLs. |
| Game process | The Windows runtime verifies the exact executable build and game structures before attaching. Its callback runs on the game thread and is restored after the command. The companion never stops or restarts Trackmania. |
| Persistence | Registers the per-user `morecars://` protocol. It installs no service, driver, scheduled task, startup entry, or Openplanet plugin. |

A selected-game startup check reads same-origin release information and checks managed files. Installing a companion or car update requires a click. Local edits go to the website for review. The Windows game DLL can remain mapped until Trackmania exits, so uninstall may require closing the game.

On Linux, native dialogs use fixed `zenity` or `kdialog` commands and protocol registration uses `xdg-mime`. On Windows, uninstall uses a fixed hidden PowerShell cleanup command with validated owned paths. Browser commands cannot choose shell fragments or invoke that cleanup path.

The device bearer token, selected game path, and API origin are stored in per-user settings. Linux uses mode `0600`; Windows relies on profile ACLs and does not additionally encrypt the token. Possession of the token permits device impersonation until re-pairing. Editing the local API origin changes which server is trusted. See the [privacy policy](PRIVACY.md) for transferred fields.

## Build and review

Companion code is in `src/`; the independent Windows game runtime and its tests are in [`native/`](../native/README.md). From the repository root:

```powershell
.\native\build-runtime.ps1
dotnet build MoreCars.Companion.csproj -f net8.0-windows
dotnet build MoreCars.Companion.csproj -f net8.0
dotnet run --project MoreCars.Companion.csproj -f net8.0 -- --self-test
```

Compare published binaries with the tagged source revision and release hashes. These notes describe the implementation; they are not a third-party audit. Report vulnerabilities privately without sharing an active exploit publicly.
