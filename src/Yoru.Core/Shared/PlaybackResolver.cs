using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AnimeWitcherDesktop;

public sealed class PlaybackException(string key, params object[] args) : Exception { public override string Message => L.T(key, args); }

// Resolves media, never launches a page or executes remotely supplied JavaScript.
public sealed class PlaybackResolver(HttpClient http, Func<string, CancellationToken, Task<JsonObject?>> configuration)
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AnimeWitcherDesktop/0.2";
    public async Task<VideoServer> Resolve(VideoServer server, CancellationToken ct)
    {
        if (!Data.WebUrl(server.Link)) throw new PlaybackException("UnsupportedLink");
        var original = new Uri(server.Link);
        var current = original;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        JsonObject? words = null;
        var configLoaded = false;
        for (var hop = 0; hop < 5; hop++)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(current.AbsoluteUri)) break;
            // PixelDrain's documented byte-range endpoint serves file data, not its viewer page.
            if (IsHost(current, "pixeldrain.com") && current.AbsolutePath.StartsWith("/u/"))
            {
                var id = current.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                if (id is null || !id.All(char.IsAsciiLetterOrDigit)) throw new PlaybackException("PixelDrainSingle");
                current = new Uri($"https://pixeldrain.com/api/file/{id}");
                visited.Add(current.AbsoluteUri);
            }
            var resource = await Read(current, original, ct);
            if (resource.Media) return server with { Link = resource.Url.AbsoluteUri, Direct = true, Browser = false, Referer = original.AbsoluteUri, MimeType = resource.MimeType };
            var html = resource.Text;
            var next = FindPageMedia(html, resource.Url);
            if (next is null && !configLoaded)
            {
                configLoaded = true;
                try { words = await configuration(server.Name, ct); }
                catch (HttpRequestException) { /* HTML media tags may still resolve without configuration. */ }
            }
            if (next is null && words is not null)
            {
                if (server.Name == "ST") next = ParseStreamtape(html, resource.Url);
                var first = words.Text(hop > 0 && server.Name == "MF" ? "word3" : "word1");
                var last = words.Text(hop > 0 && server.Name == "MF" ? "word4" : "word2");
                var value = Between(html, first, last);
                if (next is null && value is not null)
                {
                    value = WebUtility.HtmlDecode(value).Trim().Trim('"', '\'');
                    if (server.Name is "KF" or "MF" && !value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("//")) value = "https://" + value;
                    next = WebAddress(value, resource.Url);
                }
            }
            // Streamtape's fixed assignment can be parsed even if backend delimiters have changed.
            next ??= ParseStreamtape(html, resource.Url);
            if (next is null) throw new PlaybackException("NoPlayable", server.Name);
            current = next;
        }
        throw new PlaybackException("RedirectNoMedia", server.Name);
    }
    private async Task<Resource> Read(Uri uri, Uri referer, CancellationToken ct)
    {
        var response = await Get(uri, referer, true, ct);
        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.RequestedRangeNotSatisfiable or HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotImplemented)
        {
            response.Dispose();
            response = await Get(uri, referer, false, ct);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var key = response.StatusCode switch {
                    HttpStatusCode.NotFound or HttpStatusCode.Gone => "RemovedVideo",
                    HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "HostDenied",
                    HttpStatusCode.TooManyRequests => "HostLimit", _ => "HostHttp" };
                throw new PlaybackException(key, (int)response.StatusCode);
            }
            var final = response.RequestMessage?.RequestUri ?? uri;
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var data = new MemoryStream();
            var chunk = new byte[8192];
            var mediaType = IsMediaType(type);
            while (data.Length < (mediaType ? 4096 : 4_000_000))
            {
                var count = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, (mediaType ? 4096 : 4_000_000) - data.Length)), ct);
                if (count == 0) break;
                data.Write(chunk, 0, count);
                if (data.Length >= 512 && !type.Contains("html") && LooksLikeMedia(data.GetBuffer().AsSpan(0, (int)data.Length))) break;
            }
            var bytes = data.ToArray();
            var text = Encoding.UTF8.GetString(bytes);
            var html = Regex.IsMatch(text, @"^\s*(?:<!doctype\s+html|<html|<head|<body)", RegexOptions.IgnoreCase);
            if (!html && (mediaType || LooksLikeMedia(bytes))) return new(final, true, "", text.TrimStart().StartsWith("#EXTM3U") || type.Contains("mpegurl") ? "application/x-mpegURL" : type);
            if (!type.Contains("html") && text.TrimStart().StartsWith('{')) throw new PlaybackException("HostError");
            // If HTML honored the small range, fetch the bounded full page for link parsing.
            if (response.StatusCode == HttpStatusCode.PartialContent && (type.Contains("html") || html))
            {
                using var fullRequest = new HttpRequestMessage(HttpMethod.Get, final);
                fullRequest.Headers.UserAgent.ParseAdd(UserAgent); fullRequest.Headers.Referrer = referer; fullRequest.Headers.Accept.ParseAdd("*/*");
                using var full = await http.SendAsync(fullRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!full.IsSuccessStatusCode) throw new PlaybackException("HostPageFailed");
                await using var fullStream = await full.Content.ReadAsStreamAsync(ct);
                data.SetLength(0);
                while (data.Length < 4_000_000)
                {
                    var count = await fullStream.ReadAsync(chunk, ct); if (count == 0) break; data.Write(chunk, 0, count);
                }
                text = Encoding.UTF8.GetString(data.ToArray());
            }
            return new(final, false, text);
        }
    }
    private async Task<HttpResponseMessage> Get(Uri uri, Uri referer, bool ranged, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.Referrer = referer;
        if (ranged) request.Headers.Range = new RangeHeaderValue(0, 4095);
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch { request.Dispose(); throw; }
    }
    public static bool IsMediaType(string type) => type.StartsWith("video/") || type.StartsWith("audio/") || type is "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "application/dash+xml";
    public static bool LooksLikeMedia(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) return false;
        return bytes.Slice(4, 4).SequenceEqual("ftyp"u8) || bytes.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })
            || bytes.StartsWith("#EXTM3U"u8) || (bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("AVI "u8))
            || (bytes[0] == 0x47 && bytes.Length > 188 && bytes[188] == 0x47);
    }
    public static Uri? FindPageMedia(string html, Uri page)
    {
        foreach (Match tag in Regex.Matches(html, @"<(?:a|video|source|meta)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attributes = Attributes(tag.Value);
            string? value = null;
            if (tag.Value.StartsWith("<a", StringComparison.OrdinalIgnoreCase) && attributes.GetValueOrDefault("id") == "downloadButton") value = attributes.GetValueOrDefault("href");
            if (tag.Value.StartsWith("<source", StringComparison.OrdinalIgnoreCase) || tag.Value.StartsWith("<video", StringComparison.OrdinalIgnoreCase)) value = attributes.GetValueOrDefault("src");
            if (attributes.GetValueOrDefault("property") is "og:video" or "og:video:url" or "og:video:secure_url") value = attributes.GetValueOrDefault("content");
            var uri = WebAddress(value, page); if (uri is not null) return uri;
        }
        // Only literal media URLs in common player configurations; never eval script.
        foreach (Match match in Regex.Matches(html, "(?:file|src|url)\\s*[:=]\\s*[\"'](?<url>https?:[^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            var uri = WebAddress(match.Groups["url"].Value.Replace("\\/", "/"), page);
            if (uri is not null && Regex.IsMatch(uri.AbsolutePath, @"\.(?:mp4|m3u8|mpd|mkv|webm|avi)$", RegexOptions.IgnoreCase)) return uri;
        }
        return null;
    }
    public static Uri? ParseStreamtape(string html, Uri page)
    {
        var assignment = Regex.Match(html, @"getElementById\(['"" ]*(?:no)?robotlink['"" ]*\)\.innerHTML\s*=\s*(?<expr>[^;]+);", RegexOptions.IgnoreCase);
        if (!assignment.Success) return null;
        var expression = assignment.Groups["expr"].Value;
        var result = new StringBuilder();
        var position = 0;
        // Recognize only string literals, +, parentheses, whitespace and substring(integer).
        while (position < expression.Length)
        {
            while (position < expression.Length && (char.IsWhiteSpace(expression[position]) || expression[position] is '+' or '(' or ')')) position++;
            if (position == expression.Length) break;
            var part = Regex.Match(expression[position..], "^['\"](?<text>[^'\"]*)['\"]");
            if (!part.Success) return null;
            var value = part.Groups["text"].Value;
            position += part.Length;
            while (true)
            {
                var sub = Regex.Match(expression[position..], @"^\s*\)?\s*\.substring\(\s*(?<n>\d+)\s*\)");
                if (!sub.Success) break;
                if (!int.TryParse(sub.Groups["n"].Value, out var n) || n > value.Length) return null;
                value = value[n..]; position += sub.Length;
            }
            result.Append(value);
        }
        return WebAddress(result.ToString(), page);
    }
    private static Dictionary<string, string> Attributes(string tag) => Regex.Matches(tag, "(?<key>[\\w:-]+)\\s*=\\s*(?<quote>['\"])(?<value>.*?)\\k<quote>", RegexOptions.Singleline)
        .Cast<Match>().GroupBy(x => x.Groups["key"].Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
    private static Uri? WebAddress(string? value, Uri page)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value).Trim();
        return Uri.TryCreate(page, value, out var uri) && Data.WebUrl(uri.AbsoluteUri) ? uri : null;
    }
    private static string? Between(string html, string first, string last)
    {
        if (first.Length == 0 || last.Length == 0) return null;
        var start = html.IndexOf(first, StringComparison.Ordinal); if (start < 0) return null; start += first.Length;
        var end = html.IndexOf(last, start, StringComparison.Ordinal); return end < 0 ? null : html[start..end];
    }
    private static bool IsHost(Uri uri, string host) => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    private record Resource(Uri Url, bool Media, string Text, string MimeType = "");
}
