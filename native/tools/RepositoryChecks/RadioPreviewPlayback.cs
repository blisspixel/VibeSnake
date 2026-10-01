using System.ComponentModel;
using System.Diagnostics;

namespace RepositoryChecks;

internal static class RadioPreviewPlayback
{
    internal interface ISession : IDisposable
    {
        void CheckResult();
    }

    internal static void Play(
        IReadOnlyList<RadioPreviewCheck.AvailableSample> samples,
        string executable,
        TextReader input,
        TextWriter output,
        Func<string, string, ISession>? start = null)
    {
        start ??= (player, path) => new PlayerSession(player, path);
        foreach (var sample in samples)
        {
            output.WriteLine("Press Enter to play " + sample.Station + ", or type q to stop:");
            output.Flush();
            var choice = input.ReadLine();
            if (choice is null || string.Equals(choice.Trim(), "q", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RadioPreviewCheck.RequireUnlinkedAncestors(Path.GetDirectoryName(sample.FullPath)!);
            if (!File.Exists(sample.FullPath)
                || new FileInfo(sample.FullPath).Length == 0
                || (File.GetAttributes(sample.FullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new RadioPreviewException("radio preview sample changed or is unavailable: " + sample.FileName);
            }

            using var session = start(executable, sample.FullPath);
            output.WriteLine("Playing " + sample.FileName + ". Press Enter to stop and continue, or type q to stop:");
            output.Flush();
            var next = input.ReadLine();
            session.CheckResult();
            if (next is null || string.Equals(next.Trim(), "q", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string path)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-nodisp", "-autoexit", "-nostats", "-loglevel", "error", "-i", path })
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal sealed class PlayerSession : ISession
    {
        private readonly object gate = new();
        private readonly Process process;
        private readonly Timer timer;
        private bool disposed;
        private bool timedOut;
        private Exception? cleanupFailure;

        internal PlayerSession(string executable, string path)
            : this(CreateStartInfo(executable, path), TimeSpan.FromMinutes(30))
        {
        }

        internal PlayerSession(ProcessStartInfo startInfo, TimeSpan timeout)
        {
            process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch
            {
                process.Dispose();
                throw;
            }

            timer = new Timer(_ => Timeout(), null, timeout, System.Threading.Timeout.InfiniteTimeSpan);
            Console.CancelKeyPress += Cancel;
        }

        public void CheckResult()
        {
            lock (gate)
            {
                if (cleanupFailure is not null)
                {
                    throw new RadioPreviewException("radio preview cleanup failed: " + cleanupFailure.Message);
                }

                if (timedOut)
                {
                    throw new RadioPreviewException("radio preview exceeded the 30-minute playback limit");
                }

                if (process.HasExited && process.ExitCode != 0)
                {
                    throw new RadioPreviewException("radio preview player exited with code " + process.ExitCode);
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                timer.Dispose();
                Console.CancelKeyPress -= Cancel;
                try
                {
                    Stop();
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private void Timeout()
        {
            lock (gate)
            {
                if (!disposed && !process.HasExited)
                {
                    timedOut = true;
                    try
                    {
                        Stop();
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                        cleanupFailure = exception;
                    }
                }
            }
        }

        private void Cancel(object? sender, ConsoleCancelEventArgs args)
        {
            try
            {
                Dispose();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                cleanupFailure = exception;
            }
        }

        private void Stop()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                    {
                        throw new RadioPreviewException("radio preview player did not stop within five seconds");
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                if (!process.HasExited)
                {
                    throw new RadioPreviewException("radio preview player could not be stopped: " + exception.Message);
                }
            }
        }
    }
}
