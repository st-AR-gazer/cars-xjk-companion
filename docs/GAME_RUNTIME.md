# Windows game runtime status

The companion uses its own Windows native runtime. It does not load through Openplanet or Closedplanet. The website handles user choices, the companion verifies files and commands, and the game-thread callback handles archive replacement and FID refresh when Trackmania is in a safe menu state. Linux keeps the closed-game file path.

The runtime accepts only Trackmania `2026.2.2.1751` with the pinned executable hash. It validates game structures and callback identity before attachment; unsupported or inaccessible sessions use the closed-game fallback.

## Verified

- Read-only editor, menu, and FID-tree inspection passed on the supported game.
- External tests observed callbacks on one game thread, restored the original callback, and refreshed FIDs.
- A Snow archive and ownership record were replaced with identical verified bytes, then refreshed on the game thread. See the [skin-commit evidence](../native/evidence/skin-commit-2026-09-10.json).
- Isolated file transaction, recovery, and queue tests pass in the [native module](../native/README.md).

These checks do **not** establish that a visibly changed skin appears on the next map. A run without Openplanet loaded also remains pending.

## Remaining parity

| Feature | Status |
| --- | --- |
| Verified installs, repair, individual car changes, skin downloads, factory reset, file inspection, and website progress | Implemented in the companion and website |
| Safe-menu skin commit and FID refresh | Implemented for the pinned Windows game build; visible changed-skin acceptance pending |
| Actual player model selection, map contracts, transform gates, compatibility enforcement, and menu ejection | Not implemented |
| Clean-game behavior without Openplanet and unknown-build rejection in real sessions | Needs acceptance testing |

The separate hook must continue validating exact game builds and run game-object work on the game thread. It must expose fixed actions and registered IDs through authenticated local control, never website-supplied pointers, paths, or shell commands. Full parity requires tests across game restarts, map transitions, editor and spectator states, hook reconnects, and rollback.
