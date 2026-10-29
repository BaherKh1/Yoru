using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Yoru;
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
        else if (uri.AbsolutePath.EndsWith("servers2/all_servers")) json = """{"fields":{"servers":{"arrayValue":{"values":[{"mapValue":{"fields":{"name":{"stringValue":"Direct"},"link":{"stringValue":"https://video.example/movie.mp4"},"direct_link":{"booleanValue":true}}}},{"mapValue":{"fields":{"server":{"stringValue":"Mirror"},"resolution":{"stringValue":"720"},"link":{"mapValue":{"fields":{"url":{"stringValue":"https://video.example/nested.mp4"}}}},"direct":{"booleanValue":true}}}}]}}}}""";
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
