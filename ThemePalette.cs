namespace DontBlink;

public record ThemePalette(string Background, string Surface, string Raised, string Text, string Muted, string Accent, string Selection, string Danger)
{
    private System.Text.Json.JsonElement Tokens { get; init; }
    public string Token(string path, string fallback) { var value=Tokens;foreach(var part in path.Split('.'))if(value.ValueKind!=System.Text.Json.JsonValueKind.Object||!value.TryGetProperty(part,out value))return fallback;return value.GetString()??fallback; }
    public static readonly Dictionary<string, ThemePalette> All = Load();
    public static string Migrate(string name) => name switch { "Dark" => "Steel", "System" => "Steel", "Cyber" => "Galaxy", "darkgold" or "dark-gold" or "blackgold" or "black-gold" => "Gold", "bluesteel" or "blue-steel" => "Steel", "highsecurity" or "high-security" => "Security", _ => All.ContainsKey(name) ? name : "Steel" };
    public static ThemePalette Resolve(string name) => SystemInformation.HighContrast ? SystemPalette() : All[Migrate(name)];
    private static ThemePalette SystemPalette() => new(ColorTranslator.ToHtml(SystemColors.Window),ColorTranslator.ToHtml(SystemColors.Control),ColorTranslator.ToHtml(SystemColors.Control),ColorTranslator.ToHtml(SystemColors.WindowText),ColorTranslator.ToHtml(SystemColors.GrayText),ColorTranslator.ToHtml(SystemColors.Highlight),ColorTranslator.ToHtml(SystemColors.Highlight),ColorTranslator.ToHtml(SystemColors.Control));
    private static Dictionary<string,ThemePalette> Load()
    {
        using var stream=typeof(ThemePalette).Assembly.GetManifestResourceStream("DontBlink.ThemeTokens.json")!;
        using var doc=System.Text.Json.JsonDocument.Parse(stream);var palettes=new Dictionary<string,ThemePalette>();
        foreach(var theme in doc.RootElement.GetProperty("themes").EnumerateArray()){
            var c=theme.GetProperty("colors");string Get(string name)=>c.GetProperty(name).GetString()!;
            palettes.Add(theme.GetProperty("name").GetString()!,new(Get("background"),Get("surface"),Get("surface_raised"),Get("text"),Get("text_muted"),Get("accent"),Get("selection_background"),Get("surface")){Tokens=theme.GetProperty("components").Clone()});
        }return palettes;
    }
    public Color ColorOf(string hex) => ColorTranslator.FromHtml(hex);
}
