# Why Windows warns about the download

`MoreCarsCompanion.exe` is intentionally distributed without a commercial code
signing certificate. Windows Defender SmartScreen therefore sees a new,
unsigned downloaded executable with no established publisher reputation and
may show **Windows protected your PC** or **Unknown publisher**.

That warning is useful, but it is not a malware verdict. It says that Windows
cannot connect these exact bytes to a paid, identity-validated publisher. A
signature would identify the publisher and detect changes made after signing;
it would not audit the source, prove that the program is harmless, or guarantee
that a brand-new executable avoids a reputation warning.

## Why it is not signed

Commercial certificates are recurring products aimed mostly at businesses.
As checked on 2026-08-26, [CodeSignCert](https://codesigncert.com/) advertises
its cheapest listed traditional option at **$226.10 per year**, with other
listed products reaching **$507.33 per year** before any applicable tax,
shipping, or hardware choice. Prices can change; the linked seller is the
source of the current figures, not an endorsement.

This is a small, free companion that installs per user and does not need a code
signing certificate to function. Paying hundreds every year would change the
publisher label, not the permissions the program receives or what its source
code does. That is not a sensible recurring expense for this project. So yes:
Windows may show the extra screen. `¯\_(ツ)_/¯` 🤷

Microsoft's [SmartScreen reputation documentation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
explains the distinction: publisher reputation and file-hash reputation are
separate signals, and even a newly signed binary can initially be described as
unrecognized.

## Do not trust a mystery EXE

If the warning makes you uncomfortable, do not run the downloaded executable.
That is a reasonable choice. Instead you can:

1. Review every source and build file in this repository.
2. Build and run the companion from source with the .NET 8 SDK.
3. Compare the downloaded file's SHA-256 with the value displayed by
   `cars.xjk.yt` and the successful GitHub Actions build.
4. Remove the companion later with the copy-pasteable commands in
   [Uninstall](README.md#uninstall).

The detailed filesystem, network, persistence, and cleanup boundaries are in
[SECURITY.md](SECURITY.md). The information sent to the Cars service is listed
in [PRIVACY.md](PRIVACY.md).
