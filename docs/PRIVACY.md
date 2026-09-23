# Privacy policy

More Cars Companion has no advertising or third-party analytics. While paired and running, it polls `cars.xjk.yt` for commands about every two seconds and sends a heartbeat about every five seconds. It also checks release notes and managed files when the selected Trackmania process starts. Quit the companion to stop those requests.

## Data sent

Pairing sends a 30-minute single-use code, random installation ID, and computer name for the device label. Later requests use a stored bearer token. Command reports include an ID, status, short progress text, and, after a skin change, the downloaded livery's SHA-256 and size.

Heartbeats report the companion version, capabilities, setup stage, game process state, installation completeness, and a bounded inventory of **owned** vehicle paths relative to `GameData/Vehicles`, package IDs, expected sizes, and verification results. They also include up to 16 detected absolute Trackmania folder paths and opaque IDs so the paired browser/account can confirm the right installation.

Game files, image bytes, unrelated directory listings, cache contents, native addresses, and arbitrary local data are not uploaded. Downloads come from the configured API origin, normally `https://cars.xjk.yt`. Alternate HTTPS and loopback HTTP origins are available through local development settings.

## Data stored locally

Per-user `settings.json` holds the installation ID, device token, API origin, and selected game path. The cache holds vehicle and skin archives and may briefly hold a verified companion update. The game root holds `.morecars/ownership-v1.json` and temporary recovery journals. Each installed car has factory, current, and alias DDS icons beside its ZIP; selected skin details and hashes are recorded in the ownership ledger.

On Windows, the game DLL is extracted to the companion's `native/<SHA-256>/` folder. A skin transaction may stage archives, backups, and `.morecars/native-pending.json` until verified completion. The companion also keeps pending command receipts, a lock file, and a per-user named pipe. The personalized Windows download temporarily contains its pairing code; the installed EXE does not.

Linux restricts `settings.json` to the current user (`0600`). Windows uses the user profile ACL; the token is not separately DPAPI-encrypted. Anyone with that token can act as the paired device until it is replaced. See [security notes](SECURITY.md) for exact platform paths and limits.

## Choice and removal

Pairing is optional; manual car downloads avoid these companion requests. `MoreCarsCompanion --uninstall` removes the installed app, settings, cache, and protocol registration while keeping cars. Add `--keep-data` to retain settings and cache. To remove managed cars, choose that option on the website first. Exit Trackmania before uninstalling if the Windows game DLL is loaded.

The service ties device records to a random browser identity, or to an xjk account if paired while signed in. The companion does not receive the browser identity key. The xjk service operator governs server-side retention and account deletion. Report privacy concerns privately without including a token or pairing code.
