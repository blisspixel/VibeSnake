# Shell-free native commands with bounded execution, output, and pipe cleanup.
Set-StrictMode -Version Latest
if (-not ("VibeSnake.NativeProcessPolicy" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace VibeSnake {
    public sealed class NativeProcessResult {
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public bool Truncated { get; set; }
        public bool OutputComplete { get; set; }
        public string StandardOutput { get; set; } = "";
        public string StandardError { get; set; } = "";
    }
    public static class NativeProcessPolicy {
        public const int MaximumOutputCharacters = 262144;
        private sealed class Capture {
            public readonly StringBuilder Text = new StringBuilder();
            public bool Truncated;
            public bool Complete;
        }
        private static async Task Drain(StreamReader reader, Capture capture, CancellationToken token) {
            var buffer = new char[4096];
            try {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0) {
                    lock (capture) {
                        int remaining = MaximumOutputCharacters - capture.Text.Length;
                        if (count > remaining) capture.Truncated = true;
                        if (remaining > 0) capture.Text.Append(buffer, 0, Math.Min(remaining, count));
                    }
                }
                capture.Complete = true;
            } catch (OperationCanceledException) { }
              catch (IOException) { }
              catch (ObjectDisposedException) { }
        }
        public static NativeProcessResult Run(string executable, string[] arguments, string directory, int timeoutMilliseconds) {
            var start = new ProcessStartInfo(executable) {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using (var process = new Process { StartInfo = start }) {
                if (!process.Start()) throw new InvalidOperationException("Native process could not start.");
                using (var cancellation = new CancellationTokenSource()) {
                    var output = new Capture();
                    var error = new Capture();
                    var drains = Task.WhenAll(Drain(process.StandardOutput, output, cancellation.Token), Drain(process.StandardError, error, cancellation.Token));
                    bool timedOut = !process.WaitForExit(timeoutMilliseconds);
                    if (timedOut) {
                        try { process.Kill(true); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (NotSupportedException) { }
                        process.WaitForExit(5000);
                    }
                    if (!drains.Wait(2000)) {
                        cancellation.Cancel();
                        if (!drains.Wait(2000)) {
                            _ = drains.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        }
                    }
                    var result = new NativeProcessResult { TimedOut = timedOut };
                    lock (output) { result.StandardOutput = output.Text.ToString(); result.Truncated = output.Truncated; }
                    lock (error) { result.StandardError = error.Text.ToString(); result.Truncated |= error.Truncated; }
                    result.OutputComplete = drains.IsCompletedSuccessfully && output.Complete && error.Complete;
                    result.ExitCode = !timedOut && result.OutputComplete && !result.Truncated ? process.ExitCode : -1;
                    return result;
                }
            }
        }
    }
}
'@
}

function Invoke-BoundedNativeProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter()][AllowEmptyCollection()][string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][ValidateRange(1, 3600000)][int]$TimeoutMilliseconds,
        [Parameter(Mandatory)][string]$Operation
    )
    $processExecutable = $Executable
    $processArguments = $Arguments
    if ([IO.Path]::GetExtension($Executable) -ieq ".ps1") {
        $processExecutable = (Get-Process -Id $PID).Path
        $processArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $Executable) + $Arguments
    }
    $result = [VibeSnake.NativeProcessPolicy]::Run($processExecutable, $processArguments, $WorkingDirectory, $TimeoutMilliseconds)
    if ($result.TimedOut -or $result.Truncated -or -not $result.OutputComplete) {
        if ($result.StandardOutput) { Write-Host $result.StandardOutput }
        if ($result.StandardError) { Write-Host $result.StandardError }
        $reason = if ($result.TimedOut) { "Timeout" } elseif ($result.Truncated) { "OutputLimit" } else { "IncompleteOutput" }
        Write-Host "NativeProcessFailure=$reason"
        throw "$Operation failed bounded process qualification: $reason."
    }
    return $result
}
