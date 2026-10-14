using System.Text.Json;

namespace Witcher3FontTool;

/// <summary>Small, local UI state store. It contains paths and selection state only.</summary>
internal sealed class UserPreferences
{
    static bool Preview => (typeof(UserPreferences).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "").Contains('-');
    static string FileName => Preview ? "settings-beta.json" : "settings.json";

    public string? GamePath { get; init; }
    public string? FontPath { get; init; }
    public string? LatinFontPath { get; init; }
    public FontCoverage Coverage { get; init; } = FontCoverage.SimplifiedChinese | FontCoverage.English;
    public bool ChineseInterface { get; init; }

    static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Witcher3FontTool");
    static string SettingsPath => Path.Combine(SettingsDirectory, FileName);

    public static UserPreferences Load()
        => LoadFromPath(Preview && !File.Exists(SettingsPath) ? Path.Combine(SettingsDirectory, "settings.json") : SettingsPath);

    internal static UserPreferences LoadFromPath(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UserPreferences();
            var saved = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path));
            return saved is not null && IsValidCoverage(saved.Coverage) ? saved : new UserPreferences();
        }
        catch (IOException) { return new UserPreferences(); }
        catch (JsonException) { return new UserPreferences(); }
        catch (UnauthorizedAccessException) { return new UserPreferences(); }
    }

    public static void Save(string gamePath, string fontPath, FontCoverage coverage, bool chineseInterface, string? latinFontPath = null)
        => SaveToPath(SettingsPath, gamePath, fontPath, coverage, chineseInterface, latinFontPath);

    internal static void SaveToPath(string path, string gamePath, string fontPath, FontCoverage coverage, bool chineseInterface, string? latinFontPath = null)
    {
        if (!IsValidCoverage(coverage)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var settings = new UserPreferences
            {
                GamePath = gamePath,
                FontPath = fontPath,
                LatinFontPath = latinFontPath,
                Coverage = coverage,
                ChineseInterface = chineseInterface
            };
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool IsValidCoverage(FontCoverage coverage) =>
        (coverage & ~FontCharacterSets.All) == 0;
}
