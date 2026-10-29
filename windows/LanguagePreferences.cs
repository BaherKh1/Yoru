using System.IO;
using System.Text.Json;
namespace AnimeWitcherDesktop;
public sealed class LanguagePreferences
{
    public string FilePath { get; }
    public LanguagePreferences(string? directory = null) => FilePath = Path.Combine(directory ?? AppStorage.DirectoryPath, "preferences.json");
    public string Read()
    {
        try { using var data = JsonDocument.Parse(File.ReadAllText(FilePath)); return data.RootElement.TryGetProperty("language", out var language) && language.GetString() == "ar" ? "ar" : "en"; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return "en"; }
    }
    public void Save(string language)
    {
        if (language is not "en" and not "ar") throw new ArgumentException("Unsupported language", nameof(language));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new { language })); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
