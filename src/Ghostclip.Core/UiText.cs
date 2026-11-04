using System.Globalization;

namespace Ghostclip.Core;

/// <summary>
/// The language Ghostclip's texts show in. Each text carries both translations
/// where it is used, <c>L("Save clip", "Salvar clipe", "Guardar clip")</c>, so a new
/// text can't be added in one language only. Logs stay in English.
/// </summary>
public static class UiText
{
    private static volatile string _code = Resolve(UiLanguage.Auto, CultureInfo.CurrentUICulture);

    /// <summary>"en", "pt" or "es": what the library page is told to show.</summary>
    public static string Code => _code;

    public static void Use(UiLanguage language) => _code = Resolve(language, CultureInfo.CurrentUICulture);

    public static string Resolve(UiLanguage language, CultureInfo windows) => language switch
    {
        UiLanguage.English => "en",
        UiLanguage.Portuguese => "pt",
        UiLanguage.Spanish => "es",
        _ => windows.TwoLetterISOLanguageName is "pt" or "es" ? windows.TwoLetterISOLanguageName : "en",
    };

    /// <summary>What Windows was found to speak, for a process that can't tell by itself.</summary>
    public static UiLanguage Current => _code switch { "pt" => UiLanguage.Portuguese, "es" => UiLanguage.Spanish, _ => UiLanguage.English };

    public static string L(string en, string pt, string es) => _code switch { "pt" => pt, "es" => es, _ => en };
}
