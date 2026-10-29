using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Yoru;

namespace Yoru.Core;

public sealed class DownloadFailure(string key):Exception(key){public string Key{get;}=key;}
public sealed record DownloadChoice(string Id,string Label,string Url,string MimeType,bool Hls,long? Bytes=null,int Height=0,long Bitrate=0);
public sealed record DownloadObject(string Url,string Name);
public sealed record HlsArchive(string Playlist,IReadOnlyList<DownloadObject> Objects,double Duration);
public sealed record DownloadPlan(DownloadChoice Choice,HlsArchive? Archive,long? Bytes,bool Estimated,string Referer);

// Downloads accept public HTTPS media returned by the existing resolver. No script,
// cookie/account extraction, keys, licenses, DRM, decryption or restriction bypasses.
public sealed class DownloadSources(HttpClient http)
{
    internal static Uri Address(string url,Uri? parent=null){
        if(!Uri.TryCreate(parent,url,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0)throw new DownloadFailure("DownloadUnsupported");return uri;
    }
    public static HttpRequestMessage Request(string url,string referer){
        var request=new HttpRequestMessage(HttpMethod.Get,Address(url));request.Headers.UserAgent.ParseAdd(PlaybackResolver.UserAgent);
        if(Uri.TryCreate(referer,UriKind.Absolute,out var uri) && uri.Scheme=="https")request.Headers.Referrer=uri;
        return request;
    }
    internal static void Check(HttpResponseMessage response){
        if(!response.IsSuccessStatusCode)throw new DownloadFailure(response.StatusCode switch{
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized=>"DownloadDenied",HttpStatusCode.Gone or HttpStatusCode.NotFound=>"DownloadExpired",HttpStatusCode.TooManyRequests=>"DownloadRateLimited",_=>"DownloadNetwork"});
        if(response.RequestMessage?.RequestUri is { } uri && uri.Scheme!="https")throw new DownloadFailure("DownloadUnsupported");
    }
    public async Task<IReadOnlyList<DownloadChoice>> Inspect(VideoServer resolved,CancellationToken ct){
        Address(resolved.Link);
        if(resolved.MimeType.Contains("dash",StringComparison.OrdinalIgnoreCase) || new Uri(resolved.Link).AbsolutePath.EndsWith(".mpd",StringComparison.OrdinalIgnoreCase))throw new DownloadFailure("DownloadUnsupported");
        using var request=Request(resolved.Link,resolved.Referer);request.Headers.Range=new RangeHeaderValue(0,16383);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);Check(response);
        var bytes=await ReadBounded(response,16384,ct);var text=Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF',' ','\r','\n');
        var mime=response.Content.Headers.ContentType?.MediaType??resolved.MimeType;
        if(text.StartsWith("#EXTM3U",StringComparison.Ordinal)){
            var complete=bytes.Length<16384 && (response.Content.Headers.ContentRange?.Length??response.Content.Headers.ContentLength??bytes.Length)<=bytes.Length;
            var (playlist,url)=complete?(text,response.RequestMessage?.RequestUri?.AbsoluteUri??resolved.Link):await ReadPlaylist(resolved.Link,resolved.Referer,ct);
            if(playlist.Contains("#EXT-X-ALLOW-CACHE:NO",StringComparison.Ordinal))throw new DownloadFailure("DownloadDenied");
            if(playlist.Contains("#EXT-X-SESSION-KEY",StringComparison.Ordinal))throw new DownloadFailure("DownloadProtected");
            var choices=Variants(playlist,new Uri(url));
            if(choices.Count>0)return choices;
            ParseMedia(playlist,new Uri(url));
            return new[]{new DownloadChoice("hls-source",Quality(resolved.Quality),url,"application/x-mpegURL",true)};
        }
        if(!PlaybackResolver.LooksLikeMedia(bytes) || mime.Contains("html",StringComparison.OrdinalIgnoreCase))throw new DownloadFailure("DownloadUnsupported");
        var total=response.Content.Headers.ContentRange?.Length??(response.StatusCode==HttpStatusCode.OK?response.Content.Headers.ContentLength:null);
        if(mime.Length==0 || mime=="application/octet-stream")mime=new Uri(resolved.Link).AbsolutePath.EndsWith(".webm",StringComparison.OrdinalIgnoreCase)?"video/webm":"video/mp4";
        return new[]{new DownloadChoice("file-source",Quality(resolved.Quality),response.RequestMessage?.RequestUri?.AbsoluteUri??resolved.Link,mime,false,total)};
    }
    private static string Quality(string quality)=>string.IsNullOrWhiteSpace(quality) || quality.Equals("auto",StringComparison.OrdinalIgnoreCase)?"":quality;
    public async Task<DownloadPlan> Prepare(DownloadChoice choice,string referer,CancellationToken ct){
        if(!choice.Hls)return new(choice,null,choice.Bytes,false,referer);
        var (text,url)=await ReadPlaylist(choice.Url,referer,ct);var archive=ParseMedia(text,new Uri(url));
        var estimate=choice.Bitrate>0?(long?)(archive.Duration*choice.Bitrate/8):null;
        return new(choice,archive,estimate,true,referer);
    }
    private async Task<(string,string)> ReadPlaylist(string url,string referer,CancellationToken ct){
        using var request=Request(url,referer);using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);Check(response);
        var bytes=await ReadBounded(response,2_000_001,ct);if(bytes.Length>2_000_000)throw new DownloadFailure("DownloadUnsupported");
        var text=Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');if(!text.StartsWith("#EXTM3U",StringComparison.Ordinal))throw new DownloadFailure("DownloadUnsupported");
        return(text,response.RequestMessage?.RequestUri?.AbsoluteUri??url);
    }
    private static async Task<byte[]> ReadBounded(HttpResponseMessage response,int maximum,CancellationToken ct){
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var memory=new MemoryStream();var buffer=new byte[8192];
        while(memory.Length<maximum){var size=await stream.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,maximum-memory.Length)),ct);if(size==0)break;memory.Write(buffer,0,size);}return memory.ToArray();
    }
    private static Dictionary<string,string> Attributes(string text)=>Regex.Matches(text,"(?<key>[A-Z0-9-]+)=(?:\"(?<value>[^\"]*)\"|(?<value>[^,]*))").Cast<Match>().ToDictionary(m=>m.Groups["key"].Value,m=>m.Groups["value"].Value,StringComparer.Ordinal);
    private static string[] Lines(string text)=>text.Replace("\r","").Split('\n').Select(line=>line.Trim()).Where(line=>line.Length>0).ToArray();
    public static IReadOnlyList<DownloadChoice> Variants(string text,Uri parent){
        var lines=Lines(text);var result=new List<DownloadChoice>();var externalGroups=new HashSet<string>(StringComparer.Ordinal);
        foreach(var line in lines.Where(line=>line.StartsWith("#EXT-X-MEDIA:",StringComparison.Ordinal))){var attrs=Attributes(line);if(attrs.ContainsKey("URI") && attrs.TryGetValue("GROUP-ID",out var group))externalGroups.Add(group);}
        var sawVariant=false;
        for(var i=0;i<lines.Length;i++){
            if(!lines[i].StartsWith("#EXT-X-STREAM-INF:",StringComparison.Ordinal))continue;sawVariant=true;
            var attrs=Attributes(lines[i]);if(new[]{"AUDIO","VIDEO","SUBTITLES"}.Any(key=>attrs.TryGetValue(key,out var group)&&externalGroups.Contains(group)))continue;
            if(i+1>=lines.Length || lines[i+1].StartsWith('#'))throw new DownloadFailure("DownloadUnsupported");
            var url=Address(lines[++i],parent);var resolution=attrs.GetValueOrDefault("RESOLUTION","").Split('x');var height=resolution.Length==2 && int.TryParse(resolution[1],out var h)?h:0;
            var bitrate=long.TryParse(attrs.GetValueOrDefault("AVERAGE-BANDWIDTH",attrs.GetValueOrDefault("BANDWIDTH","")),out var rate)?rate:0;
            if(attrs.GetValueOrDefault("HDCP-LEVEL","NONE")!="NONE")continue;
            var label=height>0?height+"p":bitrate>0?(bitrate/1_000_000d).ToString("0.##",CultureInfo.InvariantCulture)+" Mb/s":"";
            // Ignore expiring query tokens for a rendition, never fall back to another resolution.
            var id=height+":"+bitrate+":"+attrs.GetValueOrDefault("CODECS","")+":"+attrs.GetValueOrDefault("FRAME-RATE","")+(height==0 && bitrate==0?":"+url.AbsolutePath:"");
            result.Add(new(id,label,url.AbsoluteUri,"application/x-mpegURL",true,Height:height,Bitrate:bitrate));
        }
        if(sawVariant && result.Count==0)throw new DownloadFailure("DownloadUnsupported");
        return result.DistinctBy(choice=>choice.Id).OrderByDescending(choice=>choice.Height).ThenByDescending(choice=>choice.Bitrate).ToArray();
    }
    public static HlsArchive ParseMedia(string text,Uri parent){
        var lines=Lines(text);if(lines.Length==0 || lines[0]!="#EXTM3U")throw new DownloadFailure("DownloadUnsupported");
        if(!lines.Contains("#EXT-X-ENDLIST"))throw new DownloadFailure("DownloadLive");
        if(lines.Any(line=>line.StartsWith("#EXT-X-KEY:") && Attributes(line).GetValueOrDefault("METHOD")!="NONE" || line.StartsWith("#EXT-X-SESSION-KEY")))throw new DownloadFailure("DownloadProtected");
        if(lines.Any(line=>line.StartsWith("#EXT-X-ALLOW-CACHE:NO")))throw new DownloadFailure("DownloadDenied");
        if(lines.Any(line=>line.StartsWith("#EXT-X-BYTERANGE") || line.StartsWith("#EXT-X-STREAM-INF") || line.StartsWith("#EXT-X-GAP") || line.StartsWith("#EXT-X-I-FRAMES-ONLY") || line.StartsWith("#EXT-X-DEFINE")))throw new DownloadFailure("DownloadUnsupported");
        var output=new StringBuilder("#EXTM3U\n");var objects=new List<DownloadObject>();double duration=0;var pendingSegment=false;var segments=0;
        string Local(string url,bool map){var uri=Address(url,parent);var extension=Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();if(extension is not ".ts" and not ".m4s" and not ".mp4" and not ".aac")extension=map?".mp4":".ts";var name="part"+objects.Count.ToString("D5")+extension;objects.Add(new(uri.AbsoluteUri,name));return name;}
        foreach(var line in lines.Skip(1)){
            if(line.StartsWith("#EXT-X-MAP:")){var attrs=Attributes(line);if(attrs.ContainsKey("BYTERANGE") || !attrs.TryGetValue("URI",out var map))throw new DownloadFailure("DownloadUnsupported");output.AppendLine("#EXT-X-MAP:URI=\""+Local(map,true)+"\"");}
            else if(line.StartsWith("#EXTINF:")){if(pendingSegment)throw new DownloadFailure("DownloadUnsupported");if(!double.TryParse(line[8..].Split(',')[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) || !double.IsFinite(seconds) || seconds<=0)throw new DownloadFailure("DownloadUnsupported");duration+=seconds;pendingSegment=true;output.AppendLine(line);}
            else if(!line.StartsWith('#')){if(!pendingSegment || ++segments>20000)throw new DownloadFailure("DownloadUnsupported");output.AppendLine(Local(line,false));pendingSegment=false;}
            else if(line.StartsWith("#EXT-X-TARGETDURATION:") || line.StartsWith("#EXT-X-VERSION:") || line.StartsWith("#EXT-X-MEDIA-SEQUENCE:") || line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE:") || line is "#EXT-X-DISCONTINUITY" or "#EXT-X-INDEPENDENT-SEGMENTS" or "#EXT-X-ENDLIST")output.AppendLine(line);
            // Deliberately don't retain unknown URI-bearing tags in the offline playlist.
        }
        if(pendingSegment || segments==0)throw new DownloadFailure("DownloadUnsupported");return new(output.ToString(),objects.AsReadOnly(),duration);
    }
}
