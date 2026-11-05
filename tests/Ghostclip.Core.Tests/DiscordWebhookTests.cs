using System.Net;
using System.Text;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Sending a clip to a Discord channel. This is the one place a clip leaves the PC, so where it may go is checked first.
public class DiscordWebhookTests
{
    // Made up, in the form Discord uses.
    private const string Hook = "https://discord.com/api/webhooks/123456789012345678/abc-DEF_123";

    [Theory]
    [InlineData(Hook)]
    [InlineData("https://discordapp.com/api/webhooks/123456789012345678/abc-DEF_123")]
    [InlineData("https://ptb.discord.com/api/webhooks/123456789012345678/abc-DEF_123")]
    [InlineData("https://canary.discord.com/api/webhooks/123456789012345678/abc-DEF_123")]
    [InlineData("https://discord.com/api/v10/webhooks/123456789012345678/abc-DEF_123")]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abc-DEF_123/")]
    public void A_webhook_address_from_Discord_is_accepted(string url) => Assert.True(DiscordWebhook.IsValid(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://discord.com/api/webhooks/123456789012345678/abc")]                 // Not encrypted.
    [InlineData("https://evil.example/api/webhooks/123456789012345678/abc")]               // Someone else's.
    [InlineData("https://discord.com.evil.example/api/webhooks/123456789012345678/abc")]   // Looks like it.
    [InlineData("https://evildiscord.com/api/webhooks/123456789012345678/abc")]
    [InlineData("https://discord.com@evil.example/api/webhooks/123456789012345678/abc")]   // Discord as a user name.
    [InlineData("https://user:pw@discord.com/api/webhooks/123456789012345678/abc")]
    [InlineData("https://discord.com:8443/api/webhooks/123456789012345678/abc")]
    [InlineData("https://discord.com/channels/123/456")]                                   // A channel's address.
    [InlineData("https://discord.com/api/webhooks/123456789012345678")]                    // No token.
    [InlineData("https://discord.com/api/webhooks/abc/def")]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abc/slack")]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abc?x=1")]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abc#x")]
    public void Anything_else_is_refused(string? url) => Assert.False(DiscordWebhook.IsValid(url));

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("https://evil.example/api/webhooks/1/a", null)]
    [InlineData("  " + Hook + " ", Hook)]
    public void Settings_keep_only_a_valid_address(string? typed, string? kept)
    {
        Assert.Equal(kept, new GhostclipConfig { DiscordWebhook = typed }.Normalize().DiscordWebhook);
    }

    [Fact]
    public void It_is_off_until_chosen_and_doesnt_touch_recording()
    {
        Assert.Null(new GhostclipConfig().DiscordWebhook);
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { DiscordWebhook = Hook }));
    }

    [Fact]
    public void The_address_stays_out_of_the_diagnostics()
    {
        var report = DiagnosticsReport.Build("1.0", "Windows", [], new GhostclipConfig { DiscordWebhook = Hook }, []);
        Assert.DoesNotContain("webhooks", report);
    }

    [Theory]
    [InlineData(1000, 2000, 1, "h264", false, false)]  // Fits and plays as it is.
    [InlineData(1000, 1000, 1, "h264", false, false)]  // Exactly the limit.
    [InlineData(1001, 1000, 1, "h264", false, true)]   // Too big.
    [InlineData(1000, 2000, 2, "h264", false, true)]   // Discord plays only the first track, so they get mixed.
    [InlineData(1000, 2000, 0, "h264", false, false)]  // No sound at all.
    [InlineData(1000, 2000, 1, "hevc", false, true)]   // Discord may not play it.
    [InlineData(1000, 2000, 1, "H264", false, false)]
    [InlineData(1000, 2000, 1, "h264", true, true)]    // Tracks or volumes were chosen.
    public void A_copy_is_made_only_when_the_clip_wont_do_as_it_is(long bytes, long limit, int tracks, string codec, bool chosen, bool copy)
    {
        Assert.Equal(copy, DiscordWebhook.NeedsCopy(bytes, limit, tracks, codec, chosen));
    }

    private sealed class FakeDiscord(HttpStatusCode status, Action<HttpResponseMessage>? extra = null, Exception? fail = null) : HttpMessageHandler
    {
        public Uri? Url;
        public HttpMethod? Method;
        public string Body = "";
        public string? UserAgent;
        public string? FileContentType;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (fail is not null) throw fail;
            Url = request.RequestUri;
            Method = request.Method;
            UserAgent = request.Headers.UserAgent.ToString();
            if (request.Content is MultipartFormDataContent parts)
            {
                FileContentType = parts.Last().Headers.ContentType?.MediaType;
            }
            Body = Encoding.UTF8.GetString(await request.Content!.ReadAsByteArrayAsync(ct));
            var response = new HttpResponseMessage(status);
            extra?.Invoke(response);
            return response;
        }
    }

    private static async Task<string> WithFile(string name, Func<string, Task> run)
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-discord-").FullName;
        try
        {
            var path = Path.Combine(dir, name);
            await File.WriteAllTextAsync(path, "the-clip-bytes");
            await run(path);
            return path;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task The_file_goes_to_the_webhook_and_mentions_nobody()
    {
        var fake = new FakeDiscord(HttpStatusCode.OK);
        using var http = new HttpClient(fake);
        await WithFile("G_2026-10-30_21-14-02.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, default));

        Assert.Equal(HttpMethod.Post, fake.Method);
        Assert.Equal(Hook + "?wait=true", fake.Url!.ToString());
        Assert.Contains("name=\"files[0]\"", fake.Body.Replace("name=files[0]", "name=\"files[0]\""));
        Assert.Contains("G_2026-10-30_21-14-02.mp4", fake.Body);
        Assert.Contains("the-clip-bytes", fake.Body);
        Assert.Contains("""{"allowed_mentions":{"parse":[]}}""", fake.Body);
        Assert.Equal("video/mp4", fake.FileContentType);
        Assert.StartsWith("Ghostclip", fake.UserAgent);
    }

    [Theory]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.gif", "image/gif")]
    public async Task Each_kind_of_file_says_what_it_is(string name, string type)
    {
        var fake = new FakeDiscord(HttpStatusCode.OK);
        using var http = new HttpClient(fake);
        await WithFile(name, p => DiscordWebhook.SendAsync(http, Hook, p, default));
        Assert.Equal(type, fake.FileContentType);
    }

    [Fact]
    public async Task An_address_that_isnt_Discords_is_never_called()
    {
        var fake = new FakeDiscord(HttpStatusCode.OK);
        using var http = new HttpClient(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, "https://evil.example/api/webhooks/1/a", p, default)));
        Assert.Null(fake.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too big|grande demais|demasiado grande")]
    [InlineData(HttpStatusCode.NotFound, "no longer works|não funciona mais|ya no funciona")]
    [InlineData(HttpStatusCode.Unauthorized, "no longer works|não funciona mais|ya no funciona")]
    [InlineData(HttpStatusCode.Forbidden, "no longer works|não funciona mais|ya no funciona")]
    [InlineData(HttpStatusCode.BadGateway, "502")]
    public async Task A_refusal_says_why_and_never_shows_the_address(HttpStatusCode status, string says)
    {
        using var http = new HttpClient(new FakeDiscord(status));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, default)));
        // In whatever language the test runs under.
        Assert.Contains(says.Split('|'), error.Message.Contains);
        Assert.DoesNotContain("webhooks", error.Message);
        Assert.DoesNotContain("abc-DEF_123", error.Message);
    }

    [Fact]
    public async Task Too_many_at_once_says_how_long_to_wait()
    {
        var fake = new FakeDiscord(HttpStatusCode.TooManyRequests, r => r.Headers.TryAddWithoutValidation("Retry-After", "42"));
        using var http = new HttpClient(fake);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, default)));
        Assert.Contains("42", error.Message);
    }

    [Fact]
    public async Task A_failed_connection_never_shows_the_address()
    {
        using var http = new HttpClient(new FakeDiscord(HttpStatusCode.OK, fail: new HttpRequestException($"could not connect to {Hook}")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, default)));
        Assert.DoesNotContain("webhooks", error.Message);
    }

    [Fact]
    public async Task A_timeout_never_shows_the_address()
    {
        using var http = new HttpClient(new FakeDiscord(HttpStatusCode.OK, fail: new TaskCanceledException("timeout")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, default)));
        Assert.DoesNotContain("webhooks", error.Message);
    }

    [Fact]
    public async Task Closing_the_window_cancels_rather_than_reporting_an_error()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var http = new HttpClient(new FakeDiscord(HttpStatusCode.OK, fail: new OperationCanceledException(cancelled.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WithFile("a.mp4", p => DiscordWebhook.SendAsync(http, Hook, p, cancelled.Token)));
    }
}
