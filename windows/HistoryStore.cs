using System.IO;
using System.Text.Json;
namespace Yoru;
public record WatchEntry(string AnimeId, string AnimeName, string Poster, string EpisodeId, string EpisodeName, double Seconds, DateTime Updated, double Duration = 0, string Cover = "");
public sealed class HistoryStore
{
    private readonly string path;
    public List<WatchEntry> Entries { get; private set; } = new();
    public HistoryStore(string? storageDirectory = null)
    {
        path = Path.Combine(storageDirectory ?? AppStorage.DirectoryPath, "history.json");
        Reload();
    }
    public string DirectoryPath => Path.GetDirectoryName(path)!;
    public void Reload() { try { Entries = File.Exists(path) ? JsonSerializer.Deserialize<List<WatchEntry>>(File.ReadAllText(path)) ?? new() : new(); var markers = Markers(); Entries.RemoveAll(x => markers.TryGetValue("*", out var all) && x.Updated <= all || markers.TryGetValue(x.AnimeId, out var removed) && x.Updated <= removed); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Entries = new(); } }
    private Dictionary<string, DateTime> Markers() { var file = path + ".deleted"; return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(file)) ?? new() : new(); }
    public void RemoveAnime(string id) => Delete(id);
    public void Clear() => Delete("*");
    private void Delete(string id)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var mutex = new Mutex(false, "Local\\YORUHistoryV5"); mutex.WaitOne();
        try { Reload(); var markers = Markers(); markers[id] = DateTime.UtcNow; Write(path + ".deleted", markers); Entries.RemoveAll(x => id == "*" || x.AnimeId == id); Write(path, Entries); }
        finally { mutex.ReleaseMutex(); }
    }
    private static void Write<T>(string file, T data) { var temp = file + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllText(temp, JsonSerializer.Serialize(data)); File.Move(temp, file, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    public double Position(string anime, string episode) => Entries.FirstOrDefault(x => x.AnimeId == anime && x.EpisodeId == episode)?.Seconds ?? 0;
    public void Save(Anime anime, Episode episode, double seconds, double duration = 0, DateTime? sessionStarted = null)
    {
        try {
            Directory.CreateDirectory(DirectoryPath);
            using var mutex = new Mutex(false, "Local\\YORUHistoryV5"); mutex.WaitOne();
            try {
                Reload(); var markers = Markers(); var started = sessionStarted ?? DateTime.UtcNow;
                if (markers.TryGetValue("*", out var all) && started <= all || markers.TryGetValue(anime.Id, out var removed) && started <= removed) return;
                Entries.RemoveAll(x => x.AnimeId == anime.Id && x.EpisodeId == episode.Id);
                Entries.Insert(0, new(anime.Id, anime.Name, anime.Poster, episode.Id, episode.Name, Math.Max(0, seconds), DateTime.UtcNow, Math.Max(0, duration), anime.Raw.Text("cover_uri", anime.Poster)));
                Entries = Entries.Take(100).ToList(); Write(path, Entries);
            } finally { mutex.ReleaseMutex(); }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
