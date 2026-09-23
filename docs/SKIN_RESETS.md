# Default skin resets

Version 0.5.1 adds `restore-all-skins`. Both it and `restore-skin` resolve the
factory archive from the release pinned by the current EXE. A reset works even
when no community overlay exists, or the installation still owns an older
factory archive. It does not install unowned cars.

The catalogue options menu opens a confirmation with the computer, game folder,
and installed car count. Individual packaged cars offer the same reset. The
confirmation binds the command to the selected computer and game identity.
The website waits for completion before showing the defaults as installed.
Downloaded community skins remain in the library.

The companion checks every selected archive before staging any replacements.
Unknown modifications stop the batch; built-in 2020 car archives and unowned
files are outside its scope. Missing owned archives can be restored. The entire
batch waits for Trackmania to close before replacing files; map-transition
refresh is not implemented.

`.morecars/skin-reset.json` records the original and resulting ownership state.
Original archives remain in `.morecars-reset.backup` files until every archive
and the ownership record have committed. An interrupted operation is recovered
before the next install, reset, skin change, removal, or explicit inspection.
Recovery checks recorded file hashes and preserves unknown edits. The host's
background inventory remains read-only. Keep recovery files if an external
change prevents automatic recovery, and review the installation before retrying.

The service must be restarted with the new `restore-all-skins` capability before
distributing the new EXE. `factory-release-smoke.mjs` verifies old-default reset,
all eleven archives, conflict preflight, interrupted rollback, missing owned
archives, and preservation of native/unowned files in an isolated installation.
