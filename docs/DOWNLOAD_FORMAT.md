# Personalised Windows downloads

Version 0.5.2 uses `MoreCarsCompanion-<version>-<8 hex characters>.exe`. The ID is
a display label, not a shorter authentication secret. The browser fetches the
published EXE, verifies its size and SHA-256, then appends UTF-8 JSON, its
four-byte little-endian length, and the ASCII marker `MORECARS-SETUP-V1`.

The `morecars.download.v1` JSON contains the companion version, full one-use
pairing code, originating API URL, and canonical EXE SHA-256. The EXE reads at
most 4096 metadata bytes, validates the origin and version, and verifies the
canonical prefix before installation. The installed copy omits the trailer.
Accepted origins are the HTTPS More Cars site and HTTP loopback development
servers. Changing origin discards the previous origin's bearer token.

This format uses the existing unsigned Windows preview. The .NET bundle keeps
its header offset in the apphost rather than finding it at EOF; see the
[runtime bundle marker](https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/bundle_marker.h).
Future signed downloads need a signing-aware distribution format.

The update modal shows the personalised file's SHA-256 once prepared. The setup
page labels the canonical release hash as the published EXE hash. Downloaded
files still contain the one-use secret until deleted, even though their names
do not. Settings retain only a hash of the last successfully claimed code so a
second launch does not try to redeem it again.

The companion reads `/api/v1/companion/protocol` before publishing a heartbeat.
A pre-negotiation service returning 404 uses the fixed 0.4.0 capability set.
The full EXE version is still reported. Protocol support is checked again after
one minute so restarting the service enables its newer commands automatically.

`update-smoke.mjs` exercises the downloaded EXE's real startup and replacement
of a running 0.4.0 installation against an older strict service. Its explicit
`MORECARS_SKIP_PROTOCOL=1` plus separate `MORECARS_DATA_HOME` keep the user's
protocol registration untouched. It verifies repeat and renamed launches,
canonical installed bytes, heartbeat compatibility, and zero car commands.
