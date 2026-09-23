# Independent game runtime boundary

The requested end state is one downloadable EXE, a separate companion process,
and an independently owned hook inside Trackmania. Openplanet and Closedplanet
must not be runtime dependencies. The website owns the user interface; the
companion owns downloads and filesystem transactions; the game hook owns work
that must execute on Trackmania's game thread.

**The 0.4.0 development preview implements the resident companion and website
flow. It does not contain the independent game hook and is not a complete port
of MoreCars. Do not release it as full plugin parity.**

The same game-integration limitation remains in the packaged `0.5.2` preview.
The [native adapter prototype](native/README.md) now contains an
exact-build reader and an independent C++ after-main-loop/FID experiment.
Read-only editor/menu/FID-tree validation passed on the installed game. The
user's external observer run also verified actual callbacks on one game thread,
with no faults and successful restoration. Native attachment remains blocked
by this session's Windows process-access permissions. The subsequent external
fixed FID-refresh test also passed on that same game thread, without faults
and with successful callback restoration. The
0.6.0 source now connects the native transaction to apply/reset/reset-all and
embeds the runtime in the Windows EXE. The file transaction, recovery, and builds
pass local checks. The external `SkinCommit` test also passed: an owned Snow
archive and the ownership record were replaced with identical verified bytes,
FIDs refreshed on the game thread, and the callback restored. See the
[skin-commit evidence](native/evidence/skin-commit-2026-09-10.json).
Visible next-map skins and a run without Openplanet are still pending. The website
offers 0.6.0 as a development preview; this does not mark those checks as passed
or claim full plugin parity.

## Source parity audit

The supplied MoreCars source identifies itself as version 0.1.14. These are
distinct requirements, not interchangeable meanings of "game control":

| MoreCars feature                            | Preview implementation                                                              | Remaining game integration                                                            |
| ------------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| Verified release installation and repair    | Resident native command runner, pinned manifests and hashes                         | Notify the game after successful writes                                               |
| Individual car installation/removal         | Dependency-aware install; removal retains other packages and modified files         | Invalidate cached vehicle wrappers                                                    |
| Community skin search/download              | Website search and native ZIP composition                                           | Apply only at a verified safe menu state and refresh game assets                      |
| Factory skin restoration                    | Native file replacement                                                             | Refresh the loaded skin                                                               |
| Managed-file inspection                     | Hash verification and relative file inventory                                       | No game API required                                                                  |
| Website operation feedback                  | Pairing-bound startup, heartbeats, progress, durable completion receipts            | Publish hook health and actual game state                                             |
| Garage selection                            | Website car/skin management                                                         | Read/write the actual selected player model                                           |
| Map contract checks                         | **Not implemented**                                                                 | Map identity and effective vehicle contract capture                                   |
| Controlled-player runtime identity          | **Not implemented**                                                                 | Runtime item/visual identity and transform-gate behavior                              |
| Compatibility enforcement and menu ejection | **Not implemented**                                                                 | Reproduce the validator, editor/spectator exceptions, and game-thread menu transition |
| Asset-tree rescanning                       | Native game-thread FID refresh passed after replacing an owned skin archive          | Visible changed-livery acceptance                                                     |
| Queued skins during driving                 | 0.6.0 queues a native archive/ledger commit until Editor and Playground are absent    | Actual map/editor exit, visible next-map appearance, and clean-game acceptance         |

The principal source references are `src/cars/data/sources/map_contract.as`,
`src/cars/data/sources/runtime_vehicle.as`, `src/cars/safety/validator.as`,
`src/cars/safety/eject.as`, `src/cars/skins/session.as`,
`src/cars/skins/runtime.as`, and `src/cars/assets/activation.as` in MoreCars.

Closedplanet was inspected as a bootstrap reference. Its README describes
native bootstrap and core script hosting, with the broader Trackmania APIs
still outside that implementation. Reusing or renaming its current bootstrap
would not supply this plugin's game functionality.

## Requirements for the separate hook

1. Own the loader/hook and its IPC independently. Do not overwrite an existing
   Openplanet or Closedplanet loader or connect to their control channels.
2. Identify the selected Trackmania process and exact executable build before
   attaching. A process name or successful DLL load is not proof of compatible
   game layouts or callable functions.
3. Introduce a small versioned native adapter for the required map, player,
   vehicle, FID, menu, and skin operations. Resolve and validate addresses for
   each supported build; reject unsupported builds without memory writes.
4. Schedule game-object reads/writes on the actual game thread and report
   lifetime changes. Never call these operations from HTTP polling threads.
5. Authenticate local IPC to the owning user and process. Expose registered
   car/skin IDs and fixed actions, not pointers, shell commands, or arbitrary
   filesystem paths received from websites.
6. Advertise a live capability only after its game adapter passes validation.
   A file hash, game process, heartbeat, or completed download cannot stand in
   for an equipped car, loaded skin, or enabled compatibility enforcement.

Acceptance requires clean-game runs without Openplanet, safe-menu skin reloads,
restarts, map transitions, transform gates, editor and spectator exceptions,
unknown builds, mismatched contracts, hook disconnect/reconnect, and rollback.
The current executable smoke test uses a synthetic installation directory and
tests the companion/service protocol only. It does not satisfy these game gates.
