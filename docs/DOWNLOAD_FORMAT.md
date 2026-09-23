# Personalized Windows downloads

The browser names a download `MoreCarsCompanion-<version>-<8 hex characters>.exe`. The short ID is a label; the full single-use pairing secret stays inside the file until it is deleted.

After checking the published EXE size and SHA-256, the browser appends UTF-8 `morecars.download.v1` JSON, a four-byte little-endian length, and `MORECARS-SETUP-V1`. The JSON carries the version, pairing code, API origin, and canonical EXE hash. The EXE accepts at most 4096 metadata bytes, validates the origin and version, and checks the canonical prefix. Installation removes this trailer.

The shipped origin is HTTPS `cars.xjk.yt`; HTTP loopback is allowed for development. Changing origin discards the previous token. Settings keep only a hash of the last claimed pairing code so reopening a download does not redeem it again.

The current Windows build is unsigned. A future signed build needs a different trailer strategy because the .NET [bundle marker](https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/bundle_marker.h) is not located by scanning from EOF. The companion negotiates service capabilities before sending its heartbeat; a legacy 404 response uses the fixed older capability set.
