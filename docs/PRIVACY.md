# Privacy policy

More Cars Companion has no advertising or third-party analytics/crash-reporting SDK.
While the companion is running and paired, it polls
`cars.xjk.yt` for website commands about every two seconds and sends a heartbeat
about every five seconds. Quit the companion to end these requests.
When the selected Trackmania process starts, it also fetches the same-origin
companion release notes and verifies managed car files. If you choose Install
in the desktop notice, it downloads the verified companion executable or
managed car release from the configured origin. Local edits open the website
for review only when you choose that action.

## Information transferred

Pairing sends:

- a thirty-minute, single-use pairing code;
- a random installation ID; and
- the computer name, used as the device display name.

Command requests send the stored device bearer token. Status reports contain
the command ID, status, bounded progress text, and, after applying a skin, the
downloaded livery's SHA-256 and byte size.

Heartbeats include the companion version, setup stage, supported actions,
whether the selected game process is running, installation completeness, and a
bounded inventory of managed vehicle paths relative to `GameData/Vehicles`,
their package IDs, expected sizes, and verification states. These are the
owned release files, not a general directory listing. The website shows them
in its managed-file inspector.

For folder review, heartbeats also send up to 16 detected absolute Trackmania
folder paths and their opaque IDs to the paired website. The list and selected
ID are available only to the paired browser/account.

The companion does not upload game files,
unrelated directory listings, cache contents, or arbitrary local data. Release manifests,
release blobs, and selected skin ZIPs are downloaded from the configured API
origin. The shipped origin is `https://cars.xjk.yt`; loopback HTTP and alternate
HTTPS origins exist only as editable local development configuration.

## Local storage

The companion stores a random installation ID, device token, API origin, and
selected Trackmania path in its per-user `settings.json`. It caches downloaded
vehicle and skin archives, and temporarily stores a verified companion update
executable, in its per-user cache. The selected Trackmania root
contains `.morecars/ownership-v1.json` and may temporarily contain a recovery
journal. See `SECURITY.md` for exact Windows and Linux paths.
For each installed car, three managed DDS thumbnails sit beside the vehicle ZIP
under `GameData/Vehicles/Skins`. Current and the simple icon alias may contain
the downloaded livery's `Icon.dds`; their hashes and the selected skin ID are
recorded locally in the ownership ledger. The image bytes are not uploaded.
On Windows, the bundled game runtime is extracted under the companion's
`native/<SHA-256>/` folder. Skin transactions temporarily stage verified archives,
backups, and `.morecars/native-pending.json` in the selected installation. The
local journal includes a command ID, game process identity, expiry, and file
hashes; it is removed after verified completion. Game-thread observations and
native addresses are not uploaded. Command progress can say that a skin is
waiting for map/editor exit or that its game files were refreshed.
It also stores a local command completion receipt until the service accepts it
and uses an exclusive lock file and per-user named pipe for resident-process
coordination. The one-use pairing secret is embedded in the personalized Windows
download and briefly appears in the installed process's startup arguments;
it is not a reusable device token. Expired/redeemed secrets cannot pair again.

Linux restricts `settings.json` to the current user (`0600`). Windows relies on
the current user's profile ACL; the device token is not additionally encrypted
with DPAPI.

## Choice and removal

Remote pairing is optional. Users who do not want the companion to transfer the
information above can use the manually downloadable car release instead.

Run `MoreCarsCompanion --uninstall` to remove the protocol registration,
installed companion, settings, and cache. Add `--keep-data` to retain settings
and cache while removing the executable and protocol handler. Uninstalling the
companion does not remove managed Trackmania cars; request **Remove the managed
cars** from the paired website before uninstalling if those files should also be
removed.
When the companion's native runtime is loaded in Trackmania, exit the game
before uninstalling so the runtime file can be removed. Quitting the companion
restores its game callback; the inert DLL remains mapped until the game exits.

The `cars.xjk.yt` service associates paired-device and command records with a
random browser identity by default. Signing in is optional; pairing while signed
in associates the device with that xjk account instead. The companion never
receives the browser identity key. Server-side retention and account deletion
are governed by the xjk service operator. Open a private security report in this
repository for security or privacy concerns; do not include a device token or
pairing code.

## Changes

Material changes to data transfer or storage must update this policy in the same
reviewed pull request as the code change. Releases are governed by the policy in
their tagged source revision.
