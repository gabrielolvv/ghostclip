using System.ComponentModel;
using System.Diagnostics;

namespace Ghostclip.Worker;

/// <summary>Finds the Discord desktop app, whose sound gets a track of its own.</summary>
internal static class DiscordProcess
{
    private static readonly string[] Names = ["Discord", "DiscordPTB", "DiscordCanary"];

    /// <summary>
    /// The app's first process, the root of its tree: process loopback follows the
    /// tree down to whichever child plays the call. 0 when Discord isn't running.
    /// Discord in a browser tab is the browser's sound and stays on the system track.
    /// </summary>
    public static uint Find()
    {
        uint root = 0;
        var earliest = DateTime.MaxValue;
        foreach (var name in Names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        if (p.StartTime < earliest)
                        {
                            earliest = p.StartTime;
                            root = (uint)p.Id;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        // Exited meanwhile.
                    }
                }
            }
        }
        return root;
    }
}
