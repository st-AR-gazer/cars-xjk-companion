# More Cars Companion

This is the Windows and Linux companion for [cars.xjk.yt](https://cars.xjk.yt). It pairs with the website, installs and repairs classic Trackmania cars, manages skins, and checks for updates when the selected game starts. Car ZIPs, manifests, and website downloads are generated in the Cars server workspace; this repository contains the companion source.

Version `0.6.5` is a development preview. The Windows skin runtime supports the exact Trackmania `2026.2.2.1751` build; other builds and Linux use the closed-game path. Native archive replacement and FID refresh passed in the real game. Visible next-map skin changes and a clean-game run without Openplanet still need acceptance testing. See [game runtime status](docs/GAME_RUNTIME.md).

## Use

Download the companion from the Cars website, pair it, select your Trackmania folder, then choose which cars to install. Downloads and installs require explicit clicks. The companion keeps a per-user tray process for website commands; it does not install a service or startup entry.

Each managed car has a vehicle ZIP and three DDS icons beside it: `<Car>_Icon_Factory.dds`, `<Car>_Icon_Current.dds`, and `<Car>_Icon.dds`. The current icon follows a supported selected livery and falls back to the factory render. Modified files are preserved for review.

After Trackmania starts, an available companion or car update appears in a desktop window with release notes and an install choice. Car changes may need the next game start; a companion update does not restart Trackmania. Use `MoreCarsCompanion.exe --preview-update-flow` to inspect a simulation that makes no changes.

The Windows EXE is unsigned. Read the [Windows warning](docs/WINDOWS_WARNING.md), [security notes](docs/SECURITY.md), and [privacy policy](docs/PRIVACY.md) before installing.

## Repository

| Path | Contents |
| --- | --- |
| `src/` | Companion application |
| `tests/` | Built-in `--self-test` checks |
| `native/` | Windows game runtime, probe, and tests |
| `docs/` | Release, security, and runtime details |
| `scripts/` | Artifact publisher |

`MoreCars.Companion.csproj` and `release-notes.json` stay at the root for the build and Cars server release process.

Build and test from the repository root with .NET 8 and, for the Windows runtime, MSVC x64 build tools:

```powershell
.\native\build-runtime.ps1
dotnet build MoreCars.Companion.csproj -c Release -f net8.0-windows
dotnet build MoreCars.Companion.csproj -c Release -f net8.0
dotnet run --project MoreCars.Companion.csproj -c Release -f net8.0 -- --self-test
```

Publish local artifacts under `artifacts/`:

```powershell
.\scripts\publish.ps1 -Runtime win-x64
.\scripts\publish.ps1 -Runtime linux-x64
```

See [update publication](docs/UPDATES.md), [personalized download format](docs/DOWNLOAD_FORMAT.md), and [skin resets](docs/SKIN_RESETS.md) for those workflows. The project uses [The Unlicense](LICENSE).

## Uninstall

The website's **Manage** tab can uninstall the companion and optionally remove its managed cars. On Windows, the command below removes the companion, settings, cache, and `morecars://` registration while keeping cars:

```powershell
& "$env:LOCALAPPDATA\Xjk\MoreCarsCompanion\MoreCarsCompanion.exe" --uninstall
```

On Linux, run the installed `MoreCarsCompanion --uninstall`. Add `--keep-data` to retain settings and cache. Exit Trackmania first if its Windows native runtime is loaded.
