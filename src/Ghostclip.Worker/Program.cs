using System.IO.Pipes;
using Ghostclip.Core;
using Ghostclip.Worker;
using Serilog;
using Serilog.Core;
using Serilog.Events;

// Capture worker: headless, no UI, no network. Owns capture, encoder and audio.
// Started by the host with --pipe <name>; exits when the host closes the pipe.

var pipeName = args.SkipWhile(a => a != "--pipe").Skip(1).FirstOrDefault();
if (pipeName is null)
{
    return 2;
}

var paths = GhostclipPaths.Default;
paths.EnsureCreated();
var level = new LoggingLevelSwitch(LogEventLevel.Information);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(level)
    .WriteTo.File(
        Path.Combine(paths.LogsDir, "worker-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 10,
        buffered: true,
        flushToDiskInterval: TimeSpan.FromSeconds(2),
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Worker {Version} starting (pid {Pid})", typeof(Program).Assembly.GetName().Version, Environment.ProcessId);
    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(10_000);
    await using var channel = new IpcChannel(pipe);
    using var cts = new CancellationTokenSource();
    await using var supervisor = new CaptureSupervisor(paths, channel, level, Log.Logger);
    var run = supervisor.RunAsync(cts.Token);

    try
    {
        while (await channel.ReceiveAsync(cts.Token) is { } message)
        {
            if (message.Type == IpcType.Shutdown)
            {
                Log.Information("Shutdown requested");
                break;
            }
            if (message.Type == IpcType.Config && message.Config is { } config)
            {
                supervisor.ApplyConfig(config.Normalize());
            }
        }
    }
    catch (IOException)
    {
        // Host died: same as a closed pipe.
    }

    Log.Information("Stopping");
    await cts.CancelAsync();
    await run;
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
