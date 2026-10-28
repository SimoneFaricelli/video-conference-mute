using System.Globalization;
using System.Windows.Markup;

namespace Vcm.App.Localization;

/// <summary>
/// UI strings. English texts are the ones of the PowerToys v0.87.1 page; Italian is used when the Windows UI
/// language is Italian. Kept in code (no satellite assemblies) to keep the app small.
/// </summary>
internal static class Loc
{
    private static readonly bool Italian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

    public static string Get(string key) =>
        (Italian && Strings.It.TryGetValue(key, out var it)) ? it
        : Strings.En.TryGetValue(key, out var en) ? en
        : key;

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>XAML usage: <c>Text="{l:Loc Key}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
