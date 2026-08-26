# Privacy policy

More Cars Companion has no advertising, analytics, crash-reporting SDK, or
background telemetry. It contacts `cars.xjk.yt` only when the user explicitly
pairs a computer or requests an install, cleanup, skin, or restore operation
through the website.

## Information transferred

Pairing sends:

- a five-minute, single-use pairing code;
- a random installation ID; and
- the computer name, used as the device display name.

Command requests send the stored device bearer token. Status reports contain
the command ID, status, bounded progress text, and, after applying a skin, the
downloaded livery's SHA-256 and byte size.

The companion does not upload the selected Trackmania path, game files,
directory listings, cache contents, or arbitrary local data. Release manifests,
release blobs, and selected skin ZIPs are downloaded from the configured API
origin. The shipped origin is `https://cars.xjk.yt`; loopback HTTP and alternate
HTTPS origins exist only as editable local development configuration.

## Local storage

The companion stores a random installation ID, device token, API origin, and
selected Trackmania path in its per-user `settings.json`. It caches downloaded
vehicle and skin archives in its per-user cache. The selected Trackmania root
contains `.morecars/ownership-v1.json` and may temporarily contain a recovery
journal. See `SECURITY.md` for exact Windows and Linux paths.

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
