using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Theme;

/// <summary>Typefaces per language (DESIGN section 6).</summary>
internal static class AppFonts
{
    public const string UiFontKey = "AppFontFamily";
    public const string DisplayFontKey = "AppDisplayFontFamily";
    public const string SymbolFontKey = "AppSymbolFontFamily";

    public static FontFamily Symbols { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static FontFamily Ui(string languageCode) => new($"Segoe UI Variable Text, Segoe UI, {CjkFallback(languageCode)}");

    public static FontFamily Display(string languageCode) =>
        new($"Segoe UI Variable Display, Segoe UI, {CjkFallback(languageCode)}");

    /// <summary>Numeric display face: Segoe UI digits are tabular by default.</summary>
    public static FontFamily Numeric { get; } = new("Segoe UI Variable Display, Segoe UI");

    public static XmlLanguage XmlLanguageFor(string languageCode) =>
        XmlLanguage.GetLanguage(LanguageResolver.CultureFor(languageCode).IetfLanguageTag);

    public static void Apply(ResourceDictionary resources, string languageCode)
    {
        resources[UiFontKey] = Ui(languageCode);
        resources[DisplayFontKey] = Display(languageCode);
        resources[SymbolFontKey] = Symbols;
    }

    private static string CjkFallback(string languageCode) => languageCode switch
    {
        LanguageCodes.ZhHans => "Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI",
        LanguageCodes.Ja => "Yu Gothic UI, Meiryo UI, Microsoft JhengHei UI",
        LanguageCodes.En => "Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI",
        _ => "Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI",
    };
}
