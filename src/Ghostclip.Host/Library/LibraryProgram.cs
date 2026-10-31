using System.IO.Pipes;
using System.Text;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>
/// "Ghostclip.exe --library [clip]": the clip library as a process of its own,
/// started on demand and gone when its window closes. A second launch hands
/// its clip to the open window and exits.
/// </summary>
internal static partial class LibraryProgram
{
    public const string MutexName = @"Local\Ghostclip.Library";
    public const string PipeName = "Ghostclip.Library";

    /// <summary>Opens the library on its settings page: "Ghostclip.exe --library --settings".</summary>
    public const string SettingsArgument = "--settings";

    public static int Run(string? clip)
    {
        using var single = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            return Forward(clip) ? 0 : 1;
        }

        var (paths, config, _, _) = Program.Start("library");
        try
        {
            ApplicationConfiguration.Initialize();
            if (!LibraryWindow.RuntimeAvailable())
            {
                Log.Warning("WebView2 Runtime not found; the clip library can't open");
                var answer = MessageBox.Show(L("The clip library needs the Microsoft Edge WebView2 Runtime. Download it now?",
                        "A biblioteca de clipes precisa do Microsoft Edge WebView2 Runtime. Baixar agora?"),
                    "Ghostclip", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
                }
                return 1;
            }

            using var window = new LibraryWindow(paths, config, Log.ForContext<LibraryWindow>(), clip);
            using var stop = new CancellationTokenSource();
            _ = ListenAsync(window, stop.Token);
            Application.Run(window);
            stop.Cancel();
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static bool Forward(string? clip)
    {
        // This launch came from you (a click, the tray), so it may hand the foreground to the open window;
        // without this Windows only flashes its taskbar button.
        _ = AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(clip ?? "");
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    // One line per connection: a clip path to show, or empty to just come to the front.
    private static async Task ListenAsync(LibraryWindow window, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (window.IsHandleCreated)
                {
                    window.BeginInvoke(() => window.ShowClip(string.IsNullOrWhiteSpace(line) ? null : line));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }
}
