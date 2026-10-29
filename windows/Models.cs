using System.Text.Json.Nodes;
namespace AnimeWitcherDesktop;

public static class Data
{
    public static string Text(this JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;
    public static bool Flag(this JsonNode? node, string key) => bool.TryParse(node.Text(key), out var value) && value;
    public static double Number(this JsonNode? node, string key) => double.TryParse(node.Text(key), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    public static string Id(string value) => value.Split('/').Last();
    public static JsonObject Fields(JsonNode? fields)
    {
        var result = new JsonObject();
        if (fields is JsonObject obj) foreach (var field in obj) result[field.Key] = Decode(field.Value);
        return result;
    }
    public static JsonNode? Decode(JsonNode? value)
    {
        if (value is not JsonObject obj) return null;
        if (obj.ContainsKey("mapValue")) return Fields(obj["mapValue"]?["fields"]);
        if (obj.ContainsKey("arrayValue")) return new JsonArray((obj["arrayValue"]?["values"] as JsonArray ?? new JsonArray()).Select(Decode).ToArray());
        foreach (var type in new[] { "stringValue", "booleanValue", "doubleValue", "integerValue", "timestampValue", "referenceValue" })
            if (obj.TryGetPropertyValue(type, out var leaf)) return leaf?.DeepClone();
        return null;
    }
    public static bool WebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");
}
public record Anime(string Id, string Name, string Poster, string Story, string Meta, JsonObject Raw)
{
    private static string PosterUrl(JsonObject raw)
    {
        var value = raw["poster"].Text("medium", raw["poster"].Text("large", raw.Text("poster_uri")));
        return Data.WebUrl(value) ? value : "";
    }
    public static Anime Parse(JsonObject raw, string id = "") => new(
        Data.Id(string.IsNullOrEmpty(id) ? raw.Text("objectID", raw.Text("docId", raw.Text("doc_id"))) : id),
        raw.Text("name", "Untitled"),
        PosterUrl(raw),
        raw.Text("story"),
        string.Join("  ·  ", new[] { raw.Text("type"), raw["details"].Text("year"), raw["details"].Text("season") }.Where(x => !string.IsNullOrWhiteSpace(x))), raw);
}
public record Episode(string Id, string Name, string Subtitle, JsonObject Raw)
{
    public static Episode Parse(JsonObject raw, string id = "") => new(Data.Id(string.IsNullOrEmpty(id) ? raw.Text("doc_id", raw.Text("objectID")) : id), raw.Text("name", "Episode"), raw["title_translated"] is JsonObject ? raw["title_translated"].Text("ar") : raw.Text("title_translated", raw.Text("note")), raw);
}
public record VideoServer(string Name, string Quality, string Link, bool Direct, bool Browser, string Referer = "")
{
    public string Label => string.IsNullOrWhiteSpace(Quality) ? Name : $"{Name} · {Quality}";
    public static VideoServer Parse(JsonObject raw) => new(raw.Text("name", "Server"), raw.Text("quality"), raw.Text("link"), raw.Flag("direct_link"), raw.Flag("open_browser"));
}
public record SearchPage(List<Anime> Items, int Page, int Pages, int Total);
