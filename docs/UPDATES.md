# Companion updates

`MoreCars.Companion.csproj` supplies the version reported by the EXE. Each published update needs a new three-part version.

## Publish

1. Increment the project version and add the newest entry to `release-notes.json`.
2. Build `native/build-runtime.ps1`, run both companion targets and their self-tests, and publish with `scripts/publish.ps1`.
3. In the Cars server workspace, generate the versioned downloads and `companion-release.json` with matching sizes, SHA-256 hashes, and release notes. Deploy the feed with the files.

The website checks the feed on entry, when visible, every five minutes, and before downloads. An online, paired older companion can show an update prompt. **Later** dismisses it for that visit.

The companion also checks after its selected Trackmania process starts. Its desktop window offers verified companion and managed-car updates. Matching owned files are adopted; unknown edits go to the website for review. **Not now** defers the notice until another game launch. Car files may load on the next game start; a companion update does not restart the game.

The Windows download embeds a single-use pairing code and its API origin. The installed copy removes that metadata. See [download format](DOWNLOAD_FORMAT.md). New companions negotiate service capabilities so unsupported commands stay unavailable while the server deployment catches up.
