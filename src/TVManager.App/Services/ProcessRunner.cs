using System.ComponentModel;
using System.IO;
using System.Diagnostics;

namespace TVManager.Services;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult?> RunAsync(string fileName, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask, false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            if (cancellationToken.IsCancellationRequested) throw;
            return new ProcessResult(-1, string.Empty, "Превышено время ожидания процесса.", true);
        }
    }
}
