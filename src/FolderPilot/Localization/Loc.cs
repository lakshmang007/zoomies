using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;

namespace FolderPilot.Localization;

/// <summary>
/// UI text lookup. English is built in; a translation is a JSON file with the same keys
/// (Strings/es.json, Strings/hi.json…). Missing keys fall back to English, so partial translations are fine.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static void Init(string language)
    {
        Culture = language is "auto" or "" ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(language);
        var strings = Load("en") ?? new Dictionary<string, string>(StringComparer.Ordinal);
        // Most specific last: "pt" then "pt-BR".
        foreach (string name in new[] { Culture.TwoLetterISOLanguageName, Culture.Name }.Distinct())
        {
            if (name == "en" || Load(name) is not { } overrides) continue;
            foreach (var (k, v) in overrides) strings[k] = v;
        }
        _strings = strings;
    }

    public static string T(string key) => _strings.TryGetValue(key, out var value) ? value : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    private static Dictionary<string, string>? Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"FolderPilot.Strings.{name}.json");
        if (stream is null) return null;
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
        return map;
    }
}

/// <summary>XAML: Text="{l:T Nav.Home}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
