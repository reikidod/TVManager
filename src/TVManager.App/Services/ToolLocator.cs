using System.IO;

namespace TVManager.Services;

internal static class ToolLocator
{
    public static string? FindOnPath(string executable)
    {
        foreach (var candidate in GetKnownCandidates(executable))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), executable + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKnownCandidates(string executable)
    {
        var appDirectory = AppContext.BaseDirectory;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (executable.Equals("adb", StringComparison.OrdinalIgnoreCase))
        {
            var roots = new List<string>
            {
                Path.Combine(appDirectory, "tools", "android", "platform-tools"),
                Path.Combine(localAppData, "TVManager", "tools", "android", "platform-tools"),
                Path.Combine(localAppData, "Android", "Sdk", "platform-tools")
            };

            foreach (var sdkRoot in new[]
                     {
                         Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                         Environment.GetEnvironmentVariable("ANDROID_HOME")
                     })
            {
                if (!string.IsNullOrWhiteSpace(sdkRoot))
                    roots.Add(Path.Combine(sdkRoot, "platform-tools"));
            }

            foreach (var directory in roots)
                yield return Path.Combine(directory, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        }
        else if (executable.Equals("sdb", StringComparison.OrdinalIgnoreCase))
        {
            var tizenRoot = Environment.GetEnvironmentVariable("TIZEN_STUDIO");
            var roots = new[]
            {
                Path.Combine(appDirectory, "tools", "samsung"),
                Path.Combine(localAppData, "TVManager", "tools", "samsung"),
                string.IsNullOrWhiteSpace(tizenRoot) ? string.Empty : Path.Combine(tizenRoot, "tools"),
                string.IsNullOrWhiteSpace(tizenRoot) ? string.Empty : Path.Combine(tizenRoot, "tools", "ide", "bin"),
                Path.Combine(programFiles, "tizen-studio", "tools"),
                Path.Combine(programFilesX86, "tizen-studio", "tools"),
                Path.Combine("C:\\", "tizen-studio", "tools")
            };

            foreach (var root in roots)
            {
                if (!string.IsNullOrWhiteSpace(root))
                    yield return Path.Combine(root, OperatingSystem.IsWindows() ? "sdb.exe" : "sdb");
            }
        }
    }
}
