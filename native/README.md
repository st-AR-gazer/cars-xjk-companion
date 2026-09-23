# Windows game runtime

This module contains the C# diagnostic probe and the C++ game-thread runtime bundled in the Windows companion. It has no Openplanet or Closedplanet runtime dependency. It accepts only Trackmania `2026.2.2.1751`, SHA-256 `3fc7d8cda542beda131c44306b123f4004d07d7e22f512b46b762afc29f6edda`. `GameProfile.cs` holds the validated offsets; other builds are rejected.

## Build and test

Requires .NET 8 and MSVC x64 build tools. From this directory:

```powershell
.\build-runtime.ps1
.\build-native.ps1
```

The scripts generate `GameProfile.g.h`, build the DLLs, and run isolated C# and C++ checks. The checks cover transactions, recovery, callback queueing, and game-memory validation without opening Trackmania. Outputs are under ignored `bin/` and `artifacts/`.

## Game tests

With exactly one supported Trackmania instance, run `run-probe.ps1` in the desired mode:

| Mode | Action |
| --- | --- |
| `Inspect` | Read-only process and game-structure inspection. |
| `Observe` | Attach a diagnostic DLL and record game-thread callbacks. |
| `Refresh` | Queue a fixed FID refresh at the menu. |
| `SkinCommit` | Replace an owned archive and ledger with identical verified bytes, then refresh FIDs. |
| `Recover -GamePath <Trackmania.exe>` | Recover a retained transaction after the game closes. |

`Observe`, `Refresh`, and `SkinCommit` change the running game. The diagnostic DLL remains mapped until game exit; do not overwrite it during that session. `build-runtime.ps1 -TestsOnly` runs checks without replacing the mapped runtime.

The external game tests confirmed [same-thread callbacks](evidence/frame-observation-2026-09-10.json), [FID refresh](evidence/fid-refresh-2026-09-10.json), and an [identical-byte skin commit](evidence/skin-commit-2026-09-10.json). The archive and ownership hashes matched after commit, recovery files were removed, and the original callback was restored. These results do not prove that a visibly different livery appears on the next map.

## Runtime limits

A skin request waits until both editor and playground are absent. The callback verifies inputs, commits owned archives and the ownership ledger, releases rename handles, refreshes FIDs, and returns to the game. Cancellation drops queued work; an executing commit finishes before the callback is restored. Failed or uncertain commits retain recovery data and require game exit before retry.

Acceptance still needs a visibly changed ManiaPark livery across map/editor exit, individual and all-factory resets, and a repeat without Openplanet loaded. Unknown game builds and invalid state must continue to fail without writes.
