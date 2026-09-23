# Factory skin resets

`restore-skin` and `restore-all-skins` use the factory archives pinned by the current companion EXE. They reset owned cars only; unowned cars and downloaded skins in the library remain untouched. The website confirms the selected computer, game folder, and car count before queuing a reset.

The companion checks every target before writing. Unknown edits stop the batch; missing owned archives can be restored. A reset waits for Trackmania to close before replacing files. The website reports completion only after verification.

Original archives and ownership state remain in `.morecars-reset.backup` files and `.morecars/skin-reset.json` until the batch commits. The next operation recovers an interruption after checking hashes, preserving unknown changes. If recovery cannot resolve a conflict, keep those files and review the installation before retrying.
