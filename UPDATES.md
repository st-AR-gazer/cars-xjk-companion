# Versioned companion updates

`MoreCars.Companion.csproj` is the version source. `CompanionBuild.Version`
reads the assembly version and supplies both the heartbeat and HTTP user agent.
Every published native update must increment that three-part version; builds
with the same version are treated as the same release by the website.

To prepare the next companion source release:

1. Increment `<Version>` in the project.
2. Add an entry at the beginning of `release-notes.json`, with the same version,
   publication date, summary, and user-facing changes. Keep entries newest first.
3. Build the native runtime on Windows with `native/build-runtime.ps1`, then
   run the self-tests and publish the Windows and Linux builds in CI.
4. In the Cars server workspace, build the website downloads from this source
   revision and generate `companion-release.json` with the matching version,
   sizes, hashes, and release notes. Deploy that feed with the downloads.

`downloads/companion-release.json` contains the current version, release history,
platform filenames, sizes, and SHA-256 hashes. The website fetches it without a
browser cache on page entry, when the page becomes visible, every five minutes,
and immediately before a requested download. Filenames are constrained to the
current companion release on the website's own origin.

The update modal requires an online, paired companion reporting an older valid
version. Old offline records, unpaired browsers, current/newer EXEs, and active
uninstalls do not trigger a prompt. The car pack does not have to be installed:
the version check concerns the EXE. Open dialogs and ongoing operations defer
the automatic prompt. **Later** dismisses it for that page visit and leaves an
**Update available** button in Manage.

The resident companion also checks after each launch of its selected Trackmania
process. If this feed has a newer EXE version, its desktop window shows the
release notes and offers to install the verified Windows update directly. If
the pinned car release is missing, it offers to install the managed files.
Local edits that do not match the new release open the website for review; the
desktop action does not overwrite them. Files that exactly match the new pinned
release are adopted without replacing their bytes. **Not now** dismisses the
notice until a later game launch.
The read-only check has a 20-second limit. After a successful car install, the
window says that the files are on disk and may load on the next game start.
The newly installed companion confirms completion in its own taskbar window;
Trackmania can stay open. A new companion version is required when the pinned
car release changes.

Downloading creates a fresh pairing link but queues no car command. The modal
waits for the original computer, or the computer that claims that pairing, to
report the requested version or newer. It then confirms the connection. A new
visitor uses the same latest-version lookup through the normal installation
wizard without seeing an update prompt.

Regression coverage lives in `frontend/companion-updates.test.mjs`. The native
smoke test checks the heartbeat against the actual project version rather than
a hardcoded preview version.

From 0.5.2, the Windows browser prepares a verified personalised EXE with a
short filename. Its embedded full pairing code and originating API address are
read before installation and removed from the installed copy. Read
[DOWNLOAD_FORMAT.md](DOWNLOAD_FORMAT.md) for the format and checksums.
New companions negotiate service capabilities and can reconnect to the older
service while its deployment is pending; unsupported commands stay unavailable.
