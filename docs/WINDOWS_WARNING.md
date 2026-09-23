# Why Windows warns about the download

`MoreCarsCompanion.exe` is unsigned, so Windows SmartScreen may show **Windows protected your PC** or **Unknown publisher**. That warning is about publisher and file reputation; it is not a malware verdict. A signature would identify the publisher and detect later changes, but would not audit the code or guarantee a new download avoids a warning. See [Microsoft's SmartScreen documentation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

Commercial signing certificates have a recurring cost. This free project does not currently buy one. You can [review the source](https://github.com/st-AR-gazer/cars-xjk-companion), build it with .NET 8, and compare the downloaded file's SHA-256 with the hash on `cars.xjk.yt`. If you are uncomfortable with the warning, do not run the EXE.

The [security notes](SECURITY.md) describe its permissions and [privacy policy](PRIVACY.md) lists transferred data. See [uninstall](../README.md#uninstall) for removal.
