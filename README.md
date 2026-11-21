# TV Manager 0.1.2

Windows desktop tool for discovering Smart TVs and preparing platform-specific management connections. The current milestone focuses on device discovery; application removal and firmware flashing are not implemented yet.

## Supported discovery paths

| Platform | Discovery / connection | External tool |
|---|---|---|
| Android TV / Google TV | ADB device list and wireless-debugging mDNS | Android SDK Platform-Tools (`adb.exe`) |
| Amazon Fire TV / Fire OS | ADB device list and system properties | Android SDK Platform-Tools (`adb.exe`) |
| Samsung Tizen | SDB device list and network hint on TCP 26101 | Tizen Studio / TV SDK Tools (`sdb.exe`) |
| LG webOS | SSDP / UPnP network discovery | None for discovery; optional webOS CLI for later developer operations |

USB visibility depends on the TV model, USB port mode, cable and driver. An item found through Windows Plug and Play is a candidate, not proof that it is a Smart TV or that management is available.

## End-user package

The release pipeline creates a **self-contained Windows x64 package**. The .NET runtime and app/NuGet dependencies are included in `TVManager.exe`; users do not need to install the .NET Desktop Runtime separately. The output also contains this README, tool manifest and dependency setup script.

External vendor tools are not silently copied into the repository: their license terms and installation flows are controlled by their owners. The setup helper downloads ADB directly from Google only after the user explicitly confirms the Android SDK terms. Samsung SDB is detected after the user installs official Tizen Studio / TV SDK Tools and accepts Samsung's license. LG SSDP discovery is built into the app.

### Configure external tools

Run `scripts\Install-DeviceTools.cmd` from the extracted release folder (or run the `.ps1` file from PowerShell).

- **ADB:** the script asks for confirmation, downloads the official Android Platform-Tools archive from Google and installs it under `%LOCALAPPDATA%\TVManager\tools\android\platform-tools`.
- **SDB:** install Tizen Studio and the appropriate TV SDK Tools from Samsung's official site. TV Manager checks common install directories, `%TIZEN_STUDIO%` and `PATH`. Samsung requires Developer Mode on the TV and the computer's IP address to be registered on the TV.
- **LG webOS:** SSDP discovery is native. Install LG's webOS CLI only when a future authenticated developer workflow requires it.

Official vendor setup pages:

- Android Platform-Tools: https://developer.android.com/tools/releases/platform-tools
- Android SDK terms: https://developer.android.com/studio/terms
- Samsung Tizen Studio: https://developer.samsung.com/smarttv/develop/tools/tizen-studio.html
- Samsung TV device setup: https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html
- LG webOS CLI: https://webostv.developer.lge.com/develop/tools/cli-installation

## Build from source

Development requires Windows 10/11 x64, Visual Studio 2026 (or a compatible .NET 10 SDK installation), and internet access for NuGet restore. **The published package itself does not require the SDK.**

```powershell
dotnet restore .\TVManager.sln
dotnet build .\TVManager.sln -c Release
.\scripts\Publish-Windows.ps1
```

The output archive is created at `artifacts\TVManager-0.1.2-win-x64.zip`. To publish manually without creating the ZIP, run `scripts\Publish-Windows.ps1 -NoArchive`.

## Publish to GitHub

The repository setup helper uses the GitHub CLI authenticated by the user; it does not contain a token or password. Install Git for Windows and GitHub CLI (for example, with `winget install --id Git.Git -e` and `winget install --id GitHub.cli -e`), run `gh auth login`, then execute:

```powershell
.\scripts\Publish-To-GitHub.cmd
```

The default is a public repository named `TVManager`, so friends can view and download it. Public visibility alone does not grant a reuse license; no project license has been selected yet. For a private repository (invite friends as collaborators separately), run:

```powershell
.\scripts\Publish-To-GitHub.cmd -Private
```

GitHub Actions in `.github/workflows/build-windows.yml` restores, compiles and publishes the self-contained Windows package on pushes, pull requests and manual runs. The generated ZIP is available as a workflow artifact.

## Discovery safety and limitations

- Scanning is user-initiated and limited to local IPv4 interfaces. Broad networks are bounded to avoid unexpected large network sweeps.
- TCP ports are discovery hints only; an open port is not treated as proof of platform or successful authorization.
- TV Manager does not automatically pair, connect, remove packages, reset settings, reboot, or flash firmware.
- ADB/SDB device lists can include phones, boxes, emulators and non-TV devices; device classification remains evidence-based.
- Wireless ADB on modern Android versions can use mDNS and dynamic TLS ports. Pairing and authorization are always left to the user.
- Samsung requires Developer Mode and registration of the PC IP address. Some TV models and network policies may block discovery.
- LG SSDP responses may be filtered by routers, VLANs or firewalls. Failure to find a device does not prove that the TV is unsupported.

## Current implementation

- Dark WPF discovery dashboard with scan/cancel, device list, connection type, confidence, evidence and diagnostic notes.
- ADB / SDB discovery, LG SSDP/UPnP discovery, local network TCP hints and Windows USB/PnP inventory.
- Tool lookup from packaged folders, per-user TV Manager tool directory, known SDK install paths and `PATH`.
- Self-contained single-file Windows x64 publishing profile.
- Dependency setup script and GitHub Actions packaging workflow.

## Next milestones

1. Validate the new .NET 10 build on Windows and verify the packaged EXE on a clean Windows 10/11 machine.
2. Add a user-confirmed Connect / Authorize flow for Android, Fire OS, Samsung Tizen and LG webOS.
3. Read model, firmware/build, storage and installed application inventory.
4. Implement application management with accurate descriptions, dry-run preview, protected system packages and restore options.
5. Add firmware workflows only after model, region, board revision and recovery compatibility can be verified.
