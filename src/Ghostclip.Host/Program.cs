using Ghostclip.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ghostclip.Host;

internal static class Program
{
    /// <summary>Set by "Ghostclip.exe --save" to make the running instance save a clip (Stream Deck, macros).</summary>
    public const string SaveEventName = @"Local\Ghostclip.Save";

    [STAThread]
    private static void Main(string[] args)
    {
        // One host per user session. A second launch only forwards --save.
        using var single = new Mutex(initiallyOwned: true, @"Local\Ghostclip.Host", out var first);
        if (!first)
        {
            if (args.Contains("--save", StringComparer.OrdinalIgnoreCase) &&
                EventWaitHandle.TryOpenExisting(SaveEventName, out var save))
            {
                using (save) save.Set();
            }
            return;
        }

        var paths = GhostclipPaths.Default;
        paths.EnsureCreated();
        var config = ConfigStore.Load(paths.ConfigFile, out var problem);
        var level = new LoggingLevelSwitch(ToSerilog(config.LogLevel));
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(level)
            .WriteTo.File(
                Path.Combine(paths.LogsDir, "host-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        if (problem is not null) Log.Warning("{Problem}", problem);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");

        try
        {
            Log.Information("Ghostclip {Version} starting", typeof(Program).Assembly.GetName().Version);
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp(paths, config, problem, level));
        }
        finally
        {
            Log.Information("Ghostclip stopped");
            Log.CloseAndFlush();
        }
    }

    public static LogEventLevel ToSerilog(GhostclipLogLevel level) => level switch
    {
        GhostclipLogLevel.Debug => LogEventLevel.Debug,
        GhostclipLogLevel.Warning => LogEventLevel.Warning,
        GhostclipLogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };
}
