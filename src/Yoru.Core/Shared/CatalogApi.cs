using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Yoru;

// Client-side API behavior reconstructed from the APK; no admin APIs or writes.
public sealed class CatalogApi : IDisposable
{
    public const string Project = "animewitcher-1c66d";
    public const string Documents = "https://firestore.googleapis.com/v1/projects/" + Project + "/databases/(default)/documents";
    private readonly HttpClient http;
    private readonly HttpClient media;
    private JsonObject? constants;
    private string appId = "", searchKey = "", configAppId = "", configKey = "";
    public CatalogApi(HttpMessageHandler? handler = null)
    {
        if (handler is null)
        {
            http = new HttpClient();
            // Media resolution/probing uses the managed transport consistently on
            // desktop and Android: redirects, decompression and Range/Referer behave
            // the same way instead of depending on the platform HTTP handler.
            media = new HttpClient(new SocketsHttpHandler {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        }
        else { http = new HttpClient(handler); media = http; }
        http.Timeout = TimeSpan.FromSeconds(30);
        media.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("YORU/0.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        if (!ReferenceEquals(media, http))
        {
            media.DefaultRequestHeaders.UserAgent.ParseAdd("YORU/0.1");
            media.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        }
    }
    public async Task Initialize(CancellationToken ct = default)
    {
        var settings = await Document("Settings/constants", ct);
        constants = settings;
        appId = settings["search_settings"].Text("app_id_v4");
        searchKey = settings["search_settings"].Text("api_key");
        configAppId = settings["search_settings2"].Text("app_id");
        configKey = settings["search_settings2"].Text("api_key");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(searchKey))
            throw new InvalidOperationException("The service did not provide search configuration. Try reconnecting later.");
    }
    public async Task<SearchPage> Search(string query, int page = 0, CancellationToken ct = default)
    {
        if (constants is null) await Initialize(ct);
        if (constants?["search_settings"]?["is_search_active"] is not null && !constants["search_settings"].Flag("is_search_active"))
            throw new InvalidOperationException("Search is temporarily disabled by the service.");
        var node = await Algolia("series", new JsonObject { ["params"] = $"query={Uri.EscapeDataString(query)}&page={page}&hitsPerPage=30&analytics=false&clickAnalytics=false" }, false, ct);
        var hits = node["hits"] as JsonArray ?? new();
        return new(hits.OfType<JsonObject>().Select(x => Anime.Parse(x)).Where(x => x.Id.Length > 0).ToList(), page, (int)node.Number("nbPages"), (int)node.Number("nbHits"));
    }
    public async Task<Anime> Details(string id, CancellationToken ct = default)
    {
        try { return Anime.Parse(await Algolia("series/" + Uri.EscapeDataString(Data.Id(id)), null, false, ct), id); }
        catch (HttpRequestException) { return Anime.Parse(await Document("anime_list/" + Segment(id), ct), id); }
    }
    public async Task<List<Episode>> Episodes(string id, CancellationToken ct = default)
    {
        var root = "anime_list/" + Segment(id);
        var summary = await TryDocument(root + "/episodes_summery/summery", ct);
        if (summary?["episodes"] is JsonArray episodes && episodes.Count > 0)
            return episodes.OfType<JsonObject>().Select(x => Episode.Parse(x)).Where(x => x.Id.Length > 0).ToList();
        var result = new List<Episode>();
        string? token = null;
        do
        {
            var url = Documents + "/" + root + "/episodes?pageSize=100&orderBy=doc_id";
            if (token is not null) url += "&pageToken=" + Uri.EscapeDataString(token);
            var json = await Send(new(HttpMethod.Get, url), ct);
            foreach (var doc in (json["documents"] as JsonArray ?? new()).OfType<JsonObject>())
                result.Add(Episode.Parse(Data.Fields(doc["fields"]), Data.Id(doc.Text("name"))));
            token = json.Text("nextPageToken");
        } while (!string.IsNullOrEmpty(token));
        return result;
    }
    public async Task<List<VideoServer>> Servers(string anime, string episode, CancellationToken ct = default)
    {
        if (constants is null) await Initialize(ct);
        var root = "anime_list/" + Segment(anime) + "/episodes/" + Segment(episode);
        if (constants.Text("load_servers_type") == "summary")
        {
            var summary = await TryDocument(root + "/servers2/all_servers", ct);
            if (summary?["servers"] is JsonArray rows)
            { var found = rows.OfType<JsonObject>().Select(VideoServer.Parse).Where(x => Data.WebUrl(x.Link)).ToList(); Console.WriteLine($"[YORU] Servers summary anime={anime} episode={episode} count={found.Count}"); return found; }
        }
        // Match loadAllServers2(): visible == true AND name != "", limit 20.
        var body = new JsonObject { ["structuredQuery"] = new JsonObject {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "servers" }),
            ["where"] = new JsonObject { ["compositeFilter"] = new JsonObject {
                ["op"] = "AND", ["filters"] = new JsonArray(
                    Filter("visible", "EQUAL", new JsonObject { ["booleanValue"] = true }),
                    Filter("name", "NOT_EQUAL", new JsonObject { ["stringValue"] = "" })) } },
            ["limit"] = 20 } };
        var json = await Send(new(HttpMethod.Post, Documents + "/" + root + ":runQuery") { Content = Content(body) }, ct);
        var result = (json as JsonArray ?? new()).Select(x => x?["document"]).OfType<JsonObject>()
            .Select(x => VideoServer.Parse(Data.Fields(x["fields"]))).Where(x => Data.WebUrl(x.Link)).ToList();
        Console.WriteLine($"[YORU] Servers anime={anime} episode={episode} count={result.Count}");
        return result;
    }
    public Task<VideoServer> Resolve(VideoServer server, CancellationToken ct = default)
        => new PlaybackResolver(media, ServerConfiguration).Resolve(server, ct);
    private async Task<JsonObject?> ServerConfiguration(string name, CancellationToken ct)
    {
        if (configAppId.Length > 0 && configKey.Length > 0)
        {
            try { return await Algolia("servers/" + Uri.EscapeDataString(name), null, true, ct); }
            catch (HttpRequestException) { /* APK falls back to Firestore. */ }
        }
        return await TryDocument("Settings/servers/servers/" + Segment(name), ct);
    }
    public async Task<JsonObject> Document(string path, CancellationToken ct = default)
    {
        var doc = await Send(new(HttpMethod.Get, Documents + "/" + path), ct);
        return Data.Fields(doc["fields"]);
    }
    private async Task<JsonObject?> TryDocument(string path, CancellationToken ct)
    {
        try { return await Document(path, ct); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private async Task<JsonObject> Algolia(string path, JsonObject? body, bool configuration, CancellationToken ct)
    {
        var id = configuration ? configAppId : appId;
        var key = configuration ? configKey : searchKey;
        if (id.Length == 0 || !id.All(char.IsAsciiLetterOrDigit)) throw new InvalidOperationException("Search configuration is unavailable. Reconnect first.");
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            $"https://{id}-dsn.algolia.net/1/indexes/{path}" + (body is null ? "" : "/query"));
        request.Headers.Add("X-Algolia-Application-Id", id);
        request.Headers.Add("X-Algolia-API-Key", key);
        if (body is not null) request.Content = Content(body);
        return (await Send(request, ct)) as JsonObject ?? throw new InvalidOperationException("Unexpected search response.");
    }
    private async Task<JsonNode> Send(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        using (var response = await http.SendAsync(request, ct))
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch {
                    HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "The service denied access. This endpoint may require an account or an official client.",
                    HttpStatusCode.NotFound => "This item is no longer available on the service.",
                    HttpStatusCode.TooManyRequests => "The service is busy. Please wait before trying again.",
                    _ => $"The service returned HTTP {(int)response.StatusCode}. Please try again later." };
                throw new HttpRequestException(message, null, response.StatusCode);
            }
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? throw new InvalidOperationException("The service returned an empty response.");
        }
    }
    private static StringContent Content(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
    private static string Segment(string value) => Uri.EscapeDataString(Data.Id(value));
    private static JsonObject Filter(string field, string op, JsonObject value) => new() { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = field }, ["op"] = op, ["value"] = value } };
    public void Dispose() { media.Dispose(); if(!ReferenceEquals(media,http)) http.Dispose(); }
}
