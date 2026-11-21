# External device tools

The Windows release includes the .NET runtime and all application/NuGet dependencies. Vendor device tools are handled separately because their SDK terms and installation steps are controlled by their owners.

- **ADB (Android TV / Google TV / Fire TV):** run `scripts/Install-DeviceTools.ps1`. The script downloads the official Platform-Tools package directly from Google after the user explicitly accepts the Android SDK terms. It installs to `%LOCALAPPDATA%\TVManager\tools\android\platform-tools`.
- **SDB (Samsung Tizen):** install Tizen Studio and the required SDK tools using Samsung's official installer/package manager, accept the vendor license there, and let TV Manager detect `sdb.exe` from standard locations, `TIZEN_STUDIO`, or `PATH`.
- **LG webOS:** discovery uses SSDP/UPnP implemented inside TV Manager; no external CLI is required for network discovery. For later authenticated developer operations, install the official webOS CLI from LG.

Do not copy SDK binaries from another person's computer into the repository unless the vendor license explicitly permits redistribution.

USB drivers are hardware- and vendor-specific and are not installed automatically. Use Windows Update or the TV/USB chipset manufacturer official driver package if Windows does not enumerate the interface. TV Manager does not install unsigned or third-party driver bundles.
