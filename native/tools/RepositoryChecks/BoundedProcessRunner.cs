using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RepositoryChecks;

internal sealed record BoundedProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

internal static class BoundedProcessRunner
{
    internal const int MaximumOutputCharacters = 256 * 1024;
    internal const string TruncatedOutputMarker = "\n[process output truncated]";
    internal const string OutputDrainTimedOutMarker = "\n[process output drain timed out]";

    private static readonly TimeSpan TerminationBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutputDrainBudget = TimeSpan.FromSeconds(2);

    private sealed record CapturedTranscript(
        string Content,
        bool Truncated,
        bool Complete);

    internal static BoundedProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var outputCancellation = new CancellationTokenSource();
        var standardOutput = CaptureBoundedAsync(
            process.StandardOutput,
            outputCancellation.Token);
        var standardError = CaptureBoundedAsync(
            process.StandardError,
            outputCancellation.Token);
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            if (!HasExited(process))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or NotSupportedException
                        or Win32Exception)
                {
                    // A bounded wait below still prevents cleanup from hanging.
                }

                if (!HasExited(process))
                {
                    using var terminationSource = new CancellationTokenSource(TerminationBudget);
                    try
                    {
                        process.WaitForExitAsync(terminationSource.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // The process did not terminate inside the fixed cleanup budget.
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException
                            or NotSupportedException
                            or Win32Exception)
                    {
                        // Timeout cleanup is best effort and caller return stays bounded.
                    }
                }
            }

            outputCancellation.Cancel();
            var timedOutOutput = CompleteOutput(
                standardOutput,
                standardError,
                outputCancellation);
            return new BoundedProcessResult(
                -1,
                timedOutOutput.StandardOutput,
                timedOutOutput.StandardError,
                TimedOut: true);
        }

        var completedOutput = CompleteOutput(
            standardOutput,
            standardError,
            outputCancellation);
        return new BoundedProcessResult(
            completedOutput.Complete ? process.ExitCode : -1,
            completedOutput.StandardOutput,
            completedOutput.StandardError,
            TimedOut: !completedOutput.Complete);
    }

    internal static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken) =>
        RenderTranscript(await CaptureBoundedAsync(reader, cancellationToken).ConfigureAwait(false));

    private static async Task<CapturedTranscript> CaptureBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        var complete = false;
        try
        {
            while (await reader
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false) is var count && count > 0)
            {
                var remaining = MaximumOutputCharacters - output.Length;
                if (count > remaining)
                {
                    truncated = true;
                }

                if (remaining > 0)
                {
                    output.Append(buffer, 0, Math.Min(remaining, count));
                }
            }

            complete = true;
        }
        catch (OperationCanceledException)
        {
            // Return the bounded partial transcript after timeout cleanup.
        }

        return new CapturedTranscript(output.ToString(), truncated, complete);
    }

    private static (string StandardOutput, string StandardError, bool Complete) CompleteOutput(
        Task<CapturedTranscript> standardOutput,
        Task<CapturedTranscript> standardError,
        CancellationTokenSource cancellation)
    {
        var combined = Task.WhenAll(standardOutput, standardError);
        try
        {
            combined.WaitAsync(OutputDrainBudget).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (IsOutputCompletionFailure(exception))
        {
            cancellation.Cancel();
            try
            {
                combined.WaitAsync(OutputDrainBudget).GetAwaiter().GetResult();
            }
            catch (Exception retryException) when (IsOutputCompletionFailure(retryException))
            {
                ObserveLater(standardOutput);
                ObserveLater(standardError);
            }
        }

        var output = CompletedTranscript(standardOutput);
        var error = CompletedTranscript(standardError);
        return (
            RenderTranscript(output),
            RenderTranscript(error),
            output.Complete && error.Complete);
    }

    private static bool IsOutputCompletionFailure(Exception exception) =>
        exception is TimeoutException
            or OperationCanceledException
            or IOException
            or ObjectDisposedException;

    private static CapturedTranscript CompletedTranscript(Task<CapturedTranscript> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return new CapturedTranscript("[process output read failed]", false, false);
        }

        return new CapturedTranscript(string.Empty, false, false);
    }

    private static string RenderTranscript(CapturedTranscript transcript)
    {
        var suffix = string.Empty;
        if (transcript.Truncated)
        {
            suffix += TruncatedOutputMarker;
        }

        if (!transcript.Complete)
        {
            suffix += OutputDrainTimedOutMarker;
        }

        if (suffix.Length == 0)
        {
            return transcript.Content;
        }

        var contentLength = Math.Min(
            transcript.Content.Length,
            MaximumOutputCharacters - suffix.Length);
        return transcript.Content[..contentLength] + suffix;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            return false;
        }
    }

    private static void ObserveLater(Task<CapturedTranscript> task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
