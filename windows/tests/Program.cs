using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AnimeWitcherDesktop;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
StorageMigrationChecks.Run();
await StartupCacheChecks.Run();
var nested = JsonNode.Parse("""{"mapValue":{"fields":{"episodes":{"arrayValue":{"values":[{"mapValue":{"fields":{"doc_id":{"stringValue":"001"},"filler":{"booleanValue":false}}}}]}},"n":{"integerValue":"42"}}}}""");
var decoded = Data.Decode(nested)!;
Check(decoded.Number("n") == 42 && decoded["episodes"]![0].Text("doc_id") == "001", "Firestore nested values failed");
Check(!Data.WebUrl("file:///C:/Windows/notepad.exe") && !Data.WebUrl("javascript:alert(1)"), "Unsafe URI accepted");
Check(Anime.Parse(JsonNode.Parse("""{"objectID":"a","name":"Title","poster":{"medium":"https://example.com/p.jpg"}}""")!.AsObject()).Id == "a", "Algolia model mapping failed");

var handler = new FixtureHandler();
using (var api = new WitcherApi(handler))
{
    await api.Initialize();
    var page = await api.Search("hunter & dragon", 2);
    Check(page.Items.Single().Id == "anime-1" && page.Page == 2 && page.Pages == 3, "Search parsing failed");
    Check(handler.LastQuery.Contains("hunter%20%26%20dragon") && handler.LastQuery.Contains("page=2"), "Search escaping failed");
    var arabicQuery = "\u062c\u0648\u062c\u0648\u062a\u0633\u0648"; await api.Search(arabicQuery); Check(handler.LastQuery.Contains(Uri.EscapeDataString(arabicQuery)), "Arabic search encoding failed");
    Check((await api.Details("anime-1")).Story == "Synopsis", "Details mapping failed");
    Check((await api.Episodes("anime-1")).Single().Id == "001", "Episode summary failed");
    Check((await api.Episodes("fallback")).Count == 2, "Episode pagination fallback failed");
    var servers = await api.Servers("anime-1", "001");
    Check(servers.Count == 1 && servers[0].Direct, "Server summary failed");
    Check((await api.Resolve(servers[0])).Link == servers[0].Link, "Direct URL changed");
    Check((await api.Resolve(new("PD", "720p", "https://video.example/page", false, false))).Link == "https://video.example/movie.mp4", "Delimiter resolver failed");
    try { await api.Document("denied"); throw new Exception("Access denial ignored"); }
    catch (HttpRequestException e) { Check(e.StatusCode == HttpStatusCode.Forbidden && !e.Message.Contains("secret"), "Denial handling leaked response"); }
}
Console.WriteLine("PASS: nested Firestore values, URL safety, models, search escaping/pagination, details, episode summary/fallback pagination, server summary, resolution, access-denial handling.");
await ResolverChecks.Run();
var historyScratch = Path.Combine(Path.GetTempPath(), "witcher-history-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(historyScratch);
try {
    await File.WriteAllTextAsync(Path.Combine(historyScratch, "history.json"), """[{"AnimeId":"old-title","AnimeName":"Existing title","Poster":"","EpisodeId":"001","EpisodeName":"Episode 1","Seconds":75,"Updated":"2026-10-24T12:00:00Z"}]""");
    var history = new HistoryStore(historyScratch);
    Check(history.Entries.Single().Seconds == 75 && history.Entries[0].Duration == 0, "Legacy watch history was not preserved");
    var anime = Anime.Parse(JsonNode.Parse("""{"name":"Existing title","cover_uri":"https://example.com/cover.jpg"}""")!.AsObject(), "old-title");
    history.Save(anime, Episode.Parse(JsonNode.Parse("""{"doc_id":"001","name":"Episode 1"}""")!.AsObject()), 90, 1440);
    var saved = new HistoryStore(historyScratch).Entries.Single();
    Check(saved.Seconds == 90 && saved.Duration == 1440 && saved.Cover.EndsWith("cover.jpg"), "Playback progress/duration persistence failed");
    Console.WriteLine("PASS: legacy history compatibility and real runtime/cover persistence.");
    var episode = Episode.Parse(JsonNode.Parse("""{"doc_id":"001","name":"Episode 1"}""")!.AsObject());
    var other = Anime.Parse(new JsonObject { ["name"] = "Other" }, "other");
    history.Save(other, episode, 50, 600); var oldSession = DateTime.UtcNow.AddMinutes(-1);
    history.RemoveAnime(anime.Id);
    Check(new HistoryStore(historyScratch).Position(anime.Id, episode.Id) == 0 && new HistoryStore(historyScratch).Entries.Single().AnimeId == "other", "Individual removal affected other history or retained progress");
    history.Save(anime, episode, 100, 1440, oldSession);
    Check(new HistoryStore(historyScratch).Entries.Count == 1, "Deleted entry resurrected from stale player");
    history.Clear(); history.Save(other, episode, 100, 600, oldSession);
    Check(new HistoryStore(historyScratch).Entries.Count == 0, "Cleared progress resurrected");
    history.Save(anime, episode, 1, 1440, DateTime.UtcNow.AddSeconds(1));
    Check(new HistoryStore(historyScratch).Entries.Count == 1, "Explicit new playback did not restore history");
    var prefs = new LanguagePreferences(historyScratch); Check(prefs.Read() == "en", "First launch is not English"); prefs.Save("ar"); Check(new LanguagePreferences(historyScratch).Read() == "ar", "Arabic preference did not persist"); prefs.Save("en"); Check(prefs.Read() == "en", "English preference did not persist");
    foreach (var pair in L.Entries) { Check(pair.Value.Length == 2 && pair.Value.All(x => !string.IsNullOrWhiteSpace(x)), "Missing translation: " + pair.Key); foreach (var language in new[] { "en", "ar" }) { L.Language = language; _ = L.T(pair.Key, 42, 42); } }
    L.Language = "en";
    Console.WriteLine("PASS: persistent individual/bulk deletion, unrelated progress retention, stale-player suppression, new playback, language defaults/persistence and all translation format strings.");

} finally { foreach (var file in Directory.GetFiles(historyScratch)) File.Delete(file); Directory.Delete(historyScratch); }

// V6: shared API work, cancellation isolation, retry/expiry/eviction and command ordering.
var cacheHandler=new FixtureHandler(); using(var client=new WitcherApi(cacheHandler)) {
    var readCache=new ApiDataCache(client); await readCache.Initialize(); await readCache.Search("cached",0); var count=cacheHandler.Requests;
    await readCache.Search("cached",0); Check(cacheHandler.Requests==count,"Repeated catalog page caused an API request");
    await readCache.Details("anime-1"); await readCache.Episodes("anime-1"); count=cacheHandler.Requests; await readCache.Details("anime-1"); await readCache.Episodes("anime-1");
    Check(cacheHandler.Requests==count,"Repeated details/episodes caused API requests");
    await client.Servers("anime-1","001"); await client.Servers("anime-1","001"); Check(cacheHandler.Requests==count+2,"Server lists were incorrectly cached");
}
var cache = new AsyncResultCache<string,int>(2,TimeSpan.FromMilliseconds(80));
int cacheCalls=0; var pendingCache=new TaskCompletionSource<int>();
using(var consumer=new CancellationTokenSource()) {
    var one=cache.Get("shared",()=> { Interlocked.Increment(ref cacheCalls); return pendingCache.Task; },consumer.Token);
    var two=cache.Get("shared",()=> { Interlocked.Increment(ref cacheCalls); return Task.FromResult(99); });
    consumer.Cancel(); try { await one; throw new Exception("Consumer cancellation ignored"); } catch(OperationCanceledException) { }
    pendingCache.SetResult(42); Check(await two==42 && cacheCalls==1,"Shared cache request was duplicated or cancelled for another view");
}
await Task.Delay(100); Check(await cache.Get("shared",()=>Task.FromResult(7))==7,"Cache did not expire");
await cache.Get("b",()=>Task.FromResult(2)); await cache.Get("c",()=>Task.FromResult(3));
Check(await cache.Get("shared",()=>Task.FromResult(8))==8,"Cache LRU eviction failed");
try { await cache.Get("failure",()=>Task.FromException<int>(new IOException("test"))); } catch(IOException) { }
Check(await cache.Get("failure",()=>Task.FromResult(4))==4,"Cache retained a failed response"); cache.Clear();
Check(await cache.Get("failure",()=>Task.FromResult(5))==5,"Reconnect did not invalidate cache");
var queue=new PlaybackCommands(); var seeks=new List<double>();
await Task.WhenAll(Enumerable.Range(0,50).Select(i=>queue.Seek(i,value=>seeks.Add(value))));
Check(seeks.Count==1 && seeks[0]==49,"Seek storm was not coalesced to the newest target");
var order=new List<int>(); await queue.Enqueue(()=>order.Add(1)); await queue.Enqueue(()=>order.Add(2));
Check(order.SequenceEqual(new[]{1,2}),"Native commands were reordered"); await queue.Complete(); await queue.Enqueue(()=>order.Add(3)); Check(order.Count==2,"Native command ran after close");
var guardScratch=Path.Combine(Path.GetTempPath(),"witcher-decoder-check-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(guardScratch);
try {
    using(var guard=new DecoderGuard(guardScratch)) { Check(!guard.SoftwareRequired,"Clean decoder session forced software"); }
    Check(!Directory.GetFiles(guardScratch,"player-*.json").Any(),"Normal close left a crash marker");
    File.WriteAllText(Path.Combine(guardScratch,"player-99999999.json"),System.Text.Json.JsonSerializer.Serialize(new { ProcessId=99999999,StartedUtc=DateTime.UtcNow,CreatedUtc=DateTime.UtcNow,Mode="automatic" }));
    using var fallback=new DecoderGuard(guardScratch); Check(fallback.SoftwareRequired,"Dead native-player marker did not select compatibility decoding");
} finally { foreach(var file in Directory.GetFiles(guardScratch)) File.Delete(file); Directory.Delete(guardScratch); }
Console.WriteLine("PASS: V6 bounded API caching/deduplication, cancellation isolation, expiry/eviction, failed-response retry, reconnect invalidation, seek coalescing, command ordering/close, decoder crash fallback.");

if (args.Contains("--node-live"))
{
    using var live = new WitcherApi(new NodeHttpHandler());
    await live.Initialize();
    var episodes = await live.Episodes("Jujutsu Kaisen");
    var servers = await live.Servers("Jujutsu Kaisen", episodes[0].Id);
    Console.WriteLine($"LIVE regression: Jujutsu Kaisen S1, episode {episodes[0].Id}, {servers.Count} servers.");
    foreach (var server in args.Contains("--all-servers") ? servers : servers.GroupBy(s => s.Name).Select(g => g.First()))
    {
        try {
            var stream = await live.Resolve(server);
            Console.WriteLine($"LIVE {server.Name} {server.Quality}: resolved and media-validated; browser={stream.Browser}, host={new Uri(stream.Link).Host}");
            if (args.Contains("--save-playback-input") && server.Name == "MF")
                await File.WriteAllTextAsync("tests/playback-input.local.json", System.Text.Json.JsonSerializer.Serialize(stream));
            if (args.Contains("--save-pd-input") && server.Name == "PD")
                await File.WriteAllTextAsync("tests/pd-playback.local.json", System.Text.Json.JsonSerializer.Serialize(stream));
        }
        catch (PlaybackException ex) { Console.WriteLine($"LIVE {server.Name} {server.Quality}: unavailable — {ex.Message}"); }
    }
}

if (args.Contains("--live"))
{
    using var live = new WitcherApi();
    await live.Initialize();
    var page = await live.Search("", 0);
    Check(page.Items.Count > 0, "Live catalog was empty");
    var search = await live.Search("Naruto", 0);
    Console.WriteLine($"LIVE: catalog {page.Items.Count} rows / {page.Total} total; search {search.Items.Count} rows.");
    var candidate = search.Items.FirstOrDefault() ?? page.Items[0];
    var detail = await live.Details(candidate.Id);
    var eps = await live.Episodes(candidate.Id);
    Console.WriteLine($"LIVE: detail loaded; {eps.Count} episodes.");
    if (eps.Count > 0)
    {
        var servers = await live.Servers(candidate.Id, eps[0].Id);
        Console.WriteLine($"LIVE: {servers.Count} servers; direct={servers.Count(x => x.Direct)}, browser={servers.Count(x => x.Browser)}. Media playback requires interactive validation.");
    }
}

sealed class FixtureHandler : HttpMessageHandler
{
    public string LastQuery { get; private set; } = "";
    public int Requests;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Requests);
        var uri = request.RequestUri!;
        string json;
        if (uri.AbsolutePath.EndsWith("Settings/constants"))
            json = """{"fields":{"search_settings":{"mapValue":{"fields":{"app_id_v4":{"stringValue":"TESTAPP"},"api_key":{"stringValue":"test-key"}}}},"load_servers_type":{"stringValue":"summary"}}}""";
        else if (uri.Host.Contains("algolia") && uri.AbsolutePath.EndsWith("/query"))
        {
            LastQuery = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct)).Text("params");
            json = """{"hits":[{"objectID":"anime-1","name":"Title"}],"nbPages":3,"nbHits":61}""";
        }
        else if (uri.Host.Contains("algolia")) json = """{"objectID":"anime-1","name":"Title","story":"Synopsis"}""";
        else if (uri.AbsolutePath.EndsWith("fallback/episodes_summery/summery")) return Response("{}", HttpStatusCode.NotFound);
        else if (uri.AbsolutePath.EndsWith("episodes_summery/summery")) json = """{"fields":{"episodes":{"arrayValue":{"values":[{"mapValue":{"fields":{"doc_id":{"stringValue":"001"},"name":{"stringValue":"Episode 1"}}}}]}}}}""";
        else if (uri.AbsolutePath.EndsWith("fallback/episodes")) json = uri.Query.Contains("pageToken")
            ? """{"documents":[{"name":"episodes/002","fields":{"name":{"stringValue":"Episode 2"}}}]}"""
            : """{"documents":[{"name":"episodes/001","fields":{"name":{"stringValue":"Episode 1"}}}],"nextPageToken":"next"}""";
        else if (uri.AbsolutePath.EndsWith("servers2/all_servers")) json = """{"fields":{"servers":{"arrayValue":{"values":[{"mapValue":{"fields":{"name":{"stringValue":"Direct"},"link":{"stringValue":"https://video.example/movie.mp4"},"direct_link":{"booleanValue":true}}}}]}}}}""";
        else if (uri.AbsolutePath.EndsWith("servers/PD")) json = """{"fields":{"word1":{"stringValue":"START"},"word2":{"stringValue":"END"}}}""";
        else if (uri.Host == "video.example" && uri.AbsolutePath.EndsWith(".mp4"))
        {
            var media = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(new byte[] { 0,0,0,24,102,116,121,112,105,115,111,109,0,0,0,0 }) };
            media.Content.Headers.ContentType = new("video/mp4"); return media;
        }
        else if (uri.Host == "video.example") json = "STARThttps://video.example/movie.mp4END";
        else if (uri.AbsolutePath.EndsWith("denied")) return Response("{\"secret\":\"do not expose\"}", HttpStatusCode.Forbidden);
        else throw new Exception("Unexpected fixture route: " + uri.AbsolutePath);
        return Response(json);
    }
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
