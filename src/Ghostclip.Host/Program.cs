using System.Globalization;
using Ghostclip.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ghostclip.Host;

internal static class Program
{
    /// <summary>Set by "Ghostclip.exe --save" to make the running instance save a clip (Stream Deck, macros).</summary>
    public const string SaveEventName = @"Local\Ghostclip.Save";

    /// <summary>
    /// Ghostclip.exe                         tray app
    /// Ghostclip.exe --save                  the running instance saves a clip
    /// Ghostclip.exe --trim clip start end   trimmed copy by stream copy, then exits
    /// Ghostclip.exe --compress clip         copy that fits the Discord limit, then exits
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : null;
        if (command is "--trim" or "--compress")
        {
            return RunTool(command, args);
        }

        // One host per user session. A second launch only forwards --save.
        using var single = new Mutex(initiallyOwned: true, @"Local\Ghostclip.Host", out var first);
        if (!first)
        {
            if (command == "--save" && EventWaitHandle.TryOpenExisting(SaveEventName, out var save))
            {
                using (save) save.Set();
            }
            return 0;
        }

        var (paths, config, problem, level) = Start("host");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");

        try
        {
            Log.Information("Ghostclip {Version} starting", typeof(Program).Assembly.GetName().Version);
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp(paths, config, problem, level));
            return 0;
        }
        finally
        {
            Log.Information("Ghostclip stopped");
            Log.CloseAndFlush();
        }
    }

    // Command-line tools: no tray, no worker. Result and errors go to the log.
    private static int RunTool(string command, string[] args)
    {
        var (paths, config, _, _) = Start("tools");
        try
        {
            var tools = new ClipTools(paths, () => config, Log.ForContext<ClipTools>());
            if (command == "--trim" && args.Length >= 4)
            {
                var start = double.Parse(args[2], CultureInfo.InvariantCulture);
                var end = double.Parse(args[3], CultureInfo.InvariantCulture);
                tools.TrimAsync(args[1], start, end).GetAwaiter().GetResult();
                return 0;
            }
            if (command == "--compress" && args.Length >= 2)
            {
                tools.CompressAsync(args[1]).GetAwaiter().GetResult();
                return 0;
            }
            Log.Error("Usage: --trim <clip> <start> <end> | --compress <clip>");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException or UnauthorizedAccessException)
        {
            Log.Error("{Command} failed: {Error}", command, ex.Message);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static (GhostclipPaths, GhostclipConfig, string?, LoggingLevelSwitch) Start(string logName)
    {
        var paths = GhostclipPaths.Default;
        paths.EnsureCreated();
        var config = ConfigStore.Load(paths.ConfigFile, out var problem);
        var level = new LoggingLevelSwitch(ToSerilog(config.LogLevel));
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(level)
            .WriteTo.File(
                Path.Combine(paths.LogsDir, logName + "-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        if (problem is not null) Log.Warning("{Problem}", problem);
        return (paths, config, problem, level);
    }

    public static LogEventLevel ToSerilog(GhostclipLogLevel level) => level switch
    {
        GhostclipLogLevel.Debug => LogEventLevel.Debug,
        GhostclipLogLevel.Warning => LogEventLevel.Warning,
        GhostclipLogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };
}
