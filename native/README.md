# More Cars native adapter prototype

This directory contains the independent Windows skin runtime and its diagnostic
harness. Companion 0.6.0 source now bundles it in the single EXE and uses it for
skin application and default resets. The website offers 0.6.0 as a development
preview. Visible next-map skin changes and clean-game acceptance remain pending.

The C# probe and C++ component have no Openplanet or Closedplanet runtime dependency.
The initial investigation used the supplied MoreCars source, preserved reflection
metadata, and read-only inspection of Trackmania to identify the game-owned
structures. No Openplanet function, loader, or control channel is used by this code.

## Confirmed on the installed game

The exact supported executable is Trackmania `2026.2.2.1751`, SHA-256
`3fc7d8cda542beda131c44306b123f4004d07d7e22f512b46b762afc29f6edda`.
Other builds are rejected before attachment. RVAs are relative to the loaded
game image; no process-specific pointer is persisted.

| Engine fact | Verified location |
| --- | --- |
| Application singleton | game RVA `0x20b7b60` |
| Application vtable | game RVA `0x1cf7cb8` |
| Editor / playground / root map fields | app offsets `0x7d8` / `0x3d8` / `0x360` |
| Filesystem singleton / game root | game RVA `0x1fbbee0`, then offset `0x60` |
| Child folders / count / name | folder offsets `0x38` / `0x40` / `0x58` |
| Native folder refresh | vtable slot `0xf8`, game function RVA `0x92b600` |
| After-main-loop command | app `+0x848`, then `+0x10` |
| Command function / argument | command `+0x28` / `+0x30` |
| Original after-main-loop function | game RVA `0xb4b010` |

Live read-only samples correctly distinguished the user's editor and main menu.
The reader resolves `GameData/Vehicles` from the game-owned root and validates
reflection offsets, native folder implementations, executable identity, and the
refresh function's code prefix. It rejects inconsistent snapshots, unknown
classes, excessive counts, ambiguous folder names, and corrupted strings.

The scheduling candidate is constructed by the game itself: its command factory
at RVA `0x2f11d0` receives the after-main-loop function, the app pointer, and
context index `15`. The command constructor at `0x2d4450` stores the callback and
argument at `+0x28` and `+0x30`. Both fields matched in the running game.
An external run on 2026-09-10 verified actual callback invocation: the count
advanced from 31 to 152 on one thread, with no faults or mixed-thread calls.
The original callback was restored and the observer stopped successfully.
See [the recorded evidence](evidence/frame-observation-2026-09-10.json).

## Build and test

Requires .NET 8+ and MSVC x64 build tools. From this folder:

```powershell
.\build-native.ps1
.\run-probe.ps1 -Mode Inspect
```

The build generates the C++ profile from `GameProfile.cs`, builds the native DLL,
and runs the C# and C++ checks. The C++ checks use isolated memory inside their
own test process; they do not attach to Trackmania.

`Inspect` opens the selected game with query/read rights only. It does not load
a DLL, call a game function, suspend a thread, or change game memory or files.

## Native observation and refresh experiments

With exactly one supported Trackmania instance at the main menu:

```powershell
.\run-probe.ps1 -Mode Observe
```

`Observe` is an explicit native attachment experiment, not a read-only probe.
It loads the project-built `MoreCars.FrameProbe.dll` into the selected game,
atomically substitutes one verified game-owned command callback, calls the
original function on every invocation, records the invoking thread and state,
then restores the original callback. It never substitutes an existing foreign
callback. This mode does not request a FID refresh or change vehicle files.

The inert diagnostic DLL deliberately remains mapped until Trackmania exits.
The harness does not unload code that a game thread might still have fetched.
Do not overwrite the loaded DLL with another build; use a later game session.

Only after actual observation succeeds, the separate fixed-operation experiment is:

```powershell
.\run-probe.ps1 -Mode Refresh
```

This queues one `GameData` folder refresh without files, reacquires the folder
objects, then refreshes `GameData/Vehicles` with files. Calls execute in the native
after-main-loop callback, never in the injection thread. An editor or playground
keeps the request waiting. Mixed-thread observations, a changed function prefix,
invalid folders, or native faults prevent success. Stopping cancels pending work.
This mode still does not replace any skin archives.

Each run writes its JSON result under `artifacts/`. Acceptance requires advancing
frame counts on one thread, no faults, successful restoration of the original
callback, and (for Refresh) completion on that same thread. Process exit, a timeout,
or a failed native access request is not success.

## Current acceptance status

- C# reader checks: 10 passed.
- C++ isolated callback checks: 12 passed, including competing requests and
  requests that are still being prepared.
- Supported-build and live editor/menu/FID-tree inspection: passed.
- Native attachment from the Codex session: blocked by Windows process-access
  permissions before allocation, DLL loading, or callback modification.
- Actual native frame observation: passed in the user's external run; 121
  additional callbacks on one thread, no faults, and successful restoration.
- Actual native FID refresh: passed in the user's external run. One refresh
  completed on the observed game thread, with no faults; callback restoration
  and stop both succeeded. See [the recorded refresh evidence](evidence/fid-refresh-2026-09-10.json).
- Clean-game acceptance with Openplanet absent: pending.
- Companion integration: implemented and compiled for Windows and Linux. Windows
  embeds `MoreCars.GameRuntime.dll`; Linux retains the closed-game path.
- Isolated native file transactions: 10 scenarios passed, including rename-handle
  release before refresh, hashes, locked archives, expiry, and rollback.
- Managed staging/recovery: 7 scenarios passed. Recovery preserves unknown files
  and refuses interrupted transaction recovery while the game is running.
- Native queue/lifecycle: 12 checks passed, including map/editor blocking,
  cancellation, process identity, expiry, and refusing to stop during a commit.
- Actual archive replacement and FID refresh: passed in the user's external
  `SkinCommit` run on 2026-09-10. Snow's 5,510,142-byte archive and ownership record
  were replaced with identical bytes; verification and FID refresh completed on
  thread 50608, followed by callback restoration and runtime stop. The archive hash
  remained unchanged and the transaction journal/partial were removed. See
  [the recorded result](evidence/skin-commit-2026-09-10.json).
- A visibly changed livery after map/editor exit and clean-game acceptance:
  pending. The identical-byte test does not establish those results.

The 0.6.0 development download bundles this runtime. Official release acceptance
remains incomplete; the exact-build and menu-state gates remain enabled.

## Skin transaction and acceptance test

```powershell
.\build-runtime.ps1
.\run-probe.ps1 -Mode SkinCommit
```

Run `SkinCommit` externally with the supported game at the main menu. It selects
one owned archive, verifies its installed hash, prepares identical archive and
ownership bytes, and submits a fixed 1104-byte native request. It leaves the
selected appearance unchanged. The result must confirm verified files, FID
refresh, the same game thread, callback restoration, and stopped runtime.
The test lasts at most two minutes; if started on a map/editor, exit to the menu.
Use `build-runtime.ps1 -TestsOnly` while this DLL is mapped; do not overwrite it.
If a test fails with a retained journal, close Trackmania and run
`run-probe.ps1 -Mode Recover -GamePath '<full path to Trackmania.exe>'` before
retrying. Recovery verifies all targets/backups and preserves unknown changes.

The real companion composes/downloads archives outside the game thread, then
stages them under `.morecars/native/<random ID>`. A journal records before/after
hashes, ownership bytes, and process identity. The game-thread callback waits for
both Editor and Playground to be null, verifies and locks all inputs, replaces
the registered archive paths and ownership ledger, releases rename handles, and
refreshes FIDs before returning to the game. No browser-provided path or pointer
is accepted. File hashing can cause a short pause at the menu for large batches.

Commands have an owner-process handle and a native lifetime capped at 29 minutes
(and never beyond the server command expiry). Cancellation drops a queued change;
an already executing transaction must finish before callback restoration. Failed
or uncertain commits preserve recovery data and require game exit before retry.
The inert DLL stays mapped until game exit, including after companion shutdown.
Uninstall asks for game exit when that loaded DLL would prevent full removal.

Acceptance remaining before considering the development preview fully verified:

1. A changed ManiaPark livery queues during a map/editor and appears on the next
   map after returning to the menu; verify both individual and all-default reset.
2. Repeat without Openplanet loaded, then close/relaunch the game and companion.

For the next website test, run the existing candidate at
`../artifacts/win-x64/MoreCarsCompanion.exe` outside the Codex
sandbox. It upgrades the local companion using the saved pairing. While driving
a managed More Cars vehicle, choose a visibly different livery for that same
vehicle on the website. It must remain queued until the map/editor is exited,
then report that it refreshed in Trackmania. Reopen the map and check the actual
appearance. The website's normal development download supplies 0.6.0.
