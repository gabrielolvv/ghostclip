using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using static Ghostclip.Core.UiText;

namespace Ghostclip.Core;

/// <summary>
/// Sends a clip to a Discord channel through a webhook people created themselves. It's the one place a clip
/// leaves the PC, and only when they ask: the address must be Discord's, and being a password in all but name,
/// it stays out of logs, diagnostics and error messages.
/// </summary>
public static partial class DiscordWebhook
{
    public static bool IsValid(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.Host.ToLowerInvariant() is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com"
            && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && WebhookPath().IsMatch(uri.AbsolutePath);
    }

    /// <summary>
    /// Whether a clip needs a copy made for Discord first: too big, more than one track when Discord plays only
    /// the first, a codec it may not play, or tracks or volumes people chose.
    /// </summary>
    public static bool NeedsCopy(long bytes, long limitBytes, int audioTracks, string videoCodec, bool tracksChosen) =>
        bytes > limitBytes || audioTracks > 1 || tracksChosen || !string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase);

    /// <summary>Posts the file. Throws <see cref="InvalidOperationException"/> with a message to show, which never has the address in it.</summary>
    public static async Task SendAsync(HttpClient http, string url, string filePath, CancellationToken ct)
    {
        if (!IsValid(url)) throw new InvalidOperationException(NotAWebhook());

        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
        using var form = new MultipartFormDataContent();
        // Nobody gets pinged by whatever a file's name or a future message says.
        form.Add(new StringContent("""{"allowed_mentions":{"parse":[]}}"""), "payload_json");
        var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType(filePath));
        form.Add(content, "files[0]", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "?wait=true") { Content = form };
        request.Headers.UserAgent.ParseAdd("Ghostclip (https://github.com/gabrielolvv/ghostclip)");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        // The exceptions' own text can name the address, so it isn't passed along.
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(L("Couldn't reach Discord. Is the internet up?", "Não foi possível falar com o Discord. A internet está funcionando?", "No se pudo contactar con Discord. ¿Funciona internet?"));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(L("Discord took too long to answer. Try again.", "O Discord demorou demais para responder. Tente de novo.", "Discord tardó demasiado en responder. Inténtalo de nuevo."));
        }
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            throw new InvalidOperationException(Refusal(response));
        }
    }

    public static string NotAWebhook() => L(
        "That isn't a Discord webhook address. In Discord: channel settings > Integrations > Webhooks > Copy Webhook URL.",
        "Esse não é um endereço de webhook do Discord. No Discord: configurações do canal > Integrações > Webhooks > Copiar URL do webhook.",
        "Esa no es una dirección de webhook de Discord. En Discord: ajustes del canal > Integraciones > Webhooks > Copiar URL de webhook.");

    private static string Refusal(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        switch (response.StatusCode)
        {
            case HttpStatusCode.RequestEntityTooLarge:
                return L("Discord says the file is too big for this channel. Lower the Discord size limit in Settings.",
                    "O Discord diz que o arquivo é grande demais para este canal. Diminua o limite de tamanho do Discord em Configurações.",
                    "Discord dice que el archivo es demasiado grande para este canal. Baja el límite de tamaño de Discord en Configuración.");
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound:
                return L("This webhook no longer works: it may have been deleted in Discord. Paste a new one in Settings.",
                    "Este webhook não funciona mais: pode ter sido apagado no Discord. Cole um novo em Configurações.",
                    "Este webhook ya no funciona: puede que se haya eliminado en Discord. Pega uno nuevo en Configuración.");
            case HttpStatusCode.TooManyRequests:
                var wait = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                return wait is { } seconds
                    ? L($"Discord asks to wait {seconds:0} s before sending more.", $"O Discord pede para esperar {seconds:0} s antes de enviar mais.", $"Discord pide esperar {seconds:0} s antes de enviar más.")
                    : L("Discord asks to wait a little before sending more.", "O Discord pede para esperar um pouco antes de enviar mais.", "Discord pide esperar un poco antes de enviar más.");
            default:
                return L($"Discord didn't take the file (error {code}).", $"O Discord não aceitou o arquivo (erro {code}).", $"Discord no aceptó el archivo (error {code}).");
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        _ => "video/mp4",
    };

    [GeneratedRegex(@"^/api(/v\d+)?/webhooks/\d+/[\w-]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookPath();
}
