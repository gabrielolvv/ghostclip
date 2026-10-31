using System.Diagnostics;

namespace Ghostclip.Core;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>First non-empty stderr line: usually the one that explains a failure.</summary>
    public string FirstError =>
        StdErr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ??
        (TimedOut ? "timed out" : $"exit code {ExitCode}");
}

/// <summary>Runs a short-lived tool (probe, remux) and collects its output.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        TimeSpan timeout,
        ProcessPriorityClass priority = ProcessPriorityClass.Normal,
        CancellationToken ct = default)
    {
        // Cancelled between two steps (probe, then encode): don't start a process only to kill it.
        ct.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException(UiText.L($"Could not start {exe}.", $"Não foi possível iniciar {exe}."));
        if (priority != ProcessPriorityClass.Normal)
        {
            try { process.PriorityClass = priority; } catch (InvalidOperationException) { /* already exited */ }
        }
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            timedOut = true;
        }
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), sw.Elapsed, timedOut);
    }
}
