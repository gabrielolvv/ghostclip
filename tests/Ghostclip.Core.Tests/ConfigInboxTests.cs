using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Settings saved in the tray reach the recorder loop, however many times the recorder restarted before.
public class ConfigInboxTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static GhostclipConfig WithFps(int fps) => new() { Fps = fps };

    [Fact]
    public async Task A_run_that_ended_with_its_read_open_does_not_swallow_the_next_config()
    {
        var ct = TestContext.Current.CancellationToken;
        var inbox = new ConfigInbox();
        // FFmpeg exited: nobody looks at this one again.
        _ = inbox.NextAsync(ct);
        var nextRun = inbox.NextAsync(ct);
        inbox.Post(WithFps(30));
        Assert.Equal(30, (await nextRun.WaitAsync(Patience, ct)).Fps);
    }

    [Fact]
    public async Task A_config_saved_while_the_recorder_is_down_reaches_the_next_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var inbox = new ConfigInbox();
        _ = inbox.NextAsync(ct);
        inbox.Post(WithFps(30));
        var nextRun = inbox.NextAsync(ct);
        Assert.Equal(30, (await nextRun.WaitAsync(Patience, ct)).Fps);
    }

    [Fact]
    public async Task Waiting_after_a_failure_does_not_queue_behind_the_ended_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var inbox = new ConfigInbox();
        _ = inbox.NextAsync(ct);
        var waiting = inbox.TakeAsync(ct);
        inbox.Post(WithFps(30));
        Assert.Equal(30, (await waiting.WaitAsync(Patience, ct)).Fps);
    }

    [Fact]
    public async Task Configs_arrive_in_the_order_they_were_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        var inbox = new ConfigInbox();
        inbox.Post(WithFps(30));
        inbox.Post(WithFps(60));
        Assert.Equal(30, (await inbox.TakeAsync(ct).WaitAsync(Patience, ct)).Fps);
        Assert.Equal(60, (await inbox.TakeAsync(ct).WaitAsync(Patience, ct)).Fps);
    }

    [Fact]
    public async Task A_taken_config_is_not_seen_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var inbox = new ConfigInbox();
        inbox.Post(WithFps(30));
        await inbox.TakeAsync(ct).WaitAsync(Patience, ct);
        var next = inbox.NextAsync(ct);
        Assert.False(next.IsCompleted);
        inbox.Post(WithFps(60));
        Assert.Equal(60, (await next.WaitAsync(Patience, ct)).Fps);
    }
}
