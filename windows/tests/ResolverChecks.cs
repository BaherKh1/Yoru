using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AnimeWitcherDesktop;

static class ResolverChecks
{
    public static async Task Run()
    {
        using var http = new HttpClient(new HostFixtures());
        var resolver = new PlaybackResolver(http, (name, ct) => Task.FromResult<JsonObject?>(new JsonObject { ["word1"] = "START", ["word2"] = "END" }));
        foreach (var server in new[] {
            new VideoServer("MF", "720p", "https://www.mediafire.com/file/abc", false, false),
            new VideoServer("KF", "720p", "https://krakenfiles.com/view/abc/file.html", false, false),
            new VideoServer("PD", "720p", "https://pixeldrain.com/u/abc123", false, true),
            new VideoServer("ST", "720p", "https://streamtape.com/v/abc", false, false),
            new VideoServer("Direct", "1080p", "https://video.example/movie.mp4", true, false),
            new VideoServer("HLS", "1080p", "https://video.example/movie.m3u8", true, true),
        })
        {
            var result = await resolver.Resolve(server, default);
            Assert(result.Direct && !result.Browser && result.Referer == server.Link, server.Name + " did not resolve to the app player");
        }
        foreach (var path in new[] { "deleted", "denied", "html.mp4", "script.mp4" })
        {
            try { await resolver.Resolve(new("MF", "720p", "https://video.example/" + path, true, true), default); throw new Exception("Page/error accepted as media: " + path); }
            catch (PlaybackException) { }
        }
        var candidate = PlaybackResolver.FindPageMedia("<a class='x' href='https://cdn.example/a.mp4?x=1&amp;y=2' id='downloadButton'>", new Uri("https://mediafire.com/file/a"));
        Assert(candidate?.Query == "?x=1&y=2", "Download link entity parsing failed");
        Assert(PlaybackResolver.FindPageMedia("<a href='javascript:alert(1)' id='downloadButton'>", new Uri("https://mediafire.com/file/a")) is null, "Script URL accepted");
        Assert(PlaybackResolver.ParseStreamtape("document.getElementById('norobotlink').innerHTML = evil();", new Uri("https://streamtape.com")) is null, "Arbitrary JS accepted");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await resolver.Resolve(new("MF", "", "https://video.example/movie.mp4", true, false), cancel.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Console.WriteLine("PASS: MF/KF/PD/ST, browser-flag override, download attachments, HLS, media signatures, removed/denied/HTML rejection, URL safety, cancellation.");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class HostFixtures : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("deleted")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (uri.AbsolutePath.EndsWith("denied")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            string? html = null;
            if (uri.Host == "www.mediafire.com") html = "<html><a href='https://download.mediafire.com/movie.mp4' id='downloadButton'>Download</a></html>";
            else if (uri.Host == "krakenfiles.com") html = "<html><video><source src=\"https://video.example/movie.mp4\" type=\"video/mp4\" /></video></html>";
            else if (uri.AbsolutePath.StartsWith("/v/")) html = "<html><script>document.getElementById('norobotlink').innerHTML = '//streamtape.com/get_video?id=abc&token=' + ('xxxTOKEN').substring(1).substring(2);</script></html>";
            else if (uri.AbsolutePath.EndsWith("html.mp4")) html = "<!doctype html><html>Download page, not video</html>";
            else if (uri.AbsolutePath.EndsWith("script.mp4")) html = "<script>alert('not video')</script>";
            if (html is not null) response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            else if (uri.AbsolutePath.EndsWith(".m3u8")) response.Content = new StringContent("#EXTM3U\n#EXTINF:2\nsegment.ts\n", Encoding.UTF8, "application/vnd.apple.mpegurl");
            else
            {
                response.Content = new ByteArrayContent(new byte[] { 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0 });
                response.Content.Headers.ContentType = new("application/octet-stream");
                response.Content.Headers.ContentDisposition = new("attachment");
            }
            return Task.FromResult(response);
        }
    }
}
