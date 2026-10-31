using System.Globalization;

namespace Ghostclip.Core;

/// <summary>
/// The language Ghostclip's texts show in. Each text carries both translations
/// where it is used, <c>L("Save clip", "Salvar clipe")</c>, so a new text can't
/// be added in one language only. Logs stay in English.
/// </summary>
public static class UiText
{
    private static volatile string _code = Resolve(UiLanguage.Auto, CultureInfo.CurrentUICulture);

    /// <summary>"en" or "pt": what the library page is told to show.</summary>
    public static string Code => _code;

    public static bool Portuguese => _code == "pt";

    public static void Use(UiLanguage language) => _code = Resolve(language, CultureInfo.CurrentUICulture);

    public static string Resolve(UiLanguage language, CultureInfo windows) => language switch
    {
        UiLanguage.English => "en",
        UiLanguage.Portuguese => "pt",
        _ => windows.TwoLetterISOLanguageName == "pt" ? "pt" : "en",
    };

    public static string L(string en, string pt) => Portuguese ? pt : en;
}
