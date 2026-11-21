# Third-party components and tools

TV Manager itself does not bundle Android SDK Platform-Tools, Samsung Tizen Studio/SDB, or LG webOS CLI source packages in the source repository. Their installers and terms are controlled by their respective vendors.

- **Android SDK Platform-Tools (ADB):** downloaded directly from Google by the end user after explicit acknowledgement of the Android SDK terms. See https://developer.android.com/studio/terms and https://developer.android.com/tools/releases/platform-tools.
- **Samsung Tizen Studio / TV SDK Tools (SDB):** installed by the end user through Samsung's official installer and Package Manager after accepting the displayed license. See https://developer.samsung.com/smarttv/develop/tools/tizen-studio.html.
- **LG webOS CLI:** optional for later authenticated developer operations; installation is documented by LG at https://webostv.developer.lge.com/develop/tools/cli-installation. It is not required for the current built-in SSDP discovery.
- **System.Management NuGet package:** Microsoft package, MIT-licensed. Package reference and version are recorded in `src/TVManager.App/TVManager.App.csproj`.
- **.NET runtime:** self-contained publish includes the .NET runtime components subject to Microsoft's third-party notices and runtime licensing.

Do not remove third-party license notices from vendor tools. Do not redistribute vendor SDK binaries in this repository unless the applicable license explicitly allows it.
