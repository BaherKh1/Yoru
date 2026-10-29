using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using AnimeWitcherDesktop;
namespace Yoru.Core;

public sealed class ServersUnavailable(bool network=false):Exception("Servers unavailable"){public bool Network{get;}=network;}
public sealed record ValidatedServer(VideoServer Source,VideoServer Media,IReadOnlyList<VideoServer> Servers,DateTime Checked,IReadOnlyList<DownloadChoice>? Choices=null,DownloadPlan? Plan=null){public bool Fresh=>DateTime.UtcNow-Checked<TimeSpan.FromSeconds(45);}

// One service for popup validation, watch preflight and player transitions. Small
// headers/manifests only: never save/download full episodes to test availability.
public sealed class ServerValidation(
    Func<string,string,CancellationToken,Task<List<VideoServer>>> discover,
    Func<VideoServer,CancellationToken,Task<VideoServer>> resolve,HttpClient http):IDisposable
{
    private readonly object gate=new();private readonly Dictionary<string,ValidatedServer> cache=new();
private static int QualityHeight(VideoServer server){var quality=ServerGroups.Quality(server.Quality);return int.TryParse(quality.TrimEnd('p','P'),out var height)?height:0;}
    private readonly Dictionary<string,(DateTime Time,bool Network)> rejected=new();
    private readonly Dictionary<string,(DateTime Time,IReadOnlyList<VideoServer> Items)> lists=new();
    public TimeSpan SourceTimeout{get;init;}=TimeSpan.FromSeconds(12);
    public TimeSpan SearchTimeout{get;init;}=TimeSpan.FromSeconds(60);
    public TimeSpan DiscoveryTimeout{get;init;}=TimeSpan.FromSeconds(20);
    private static string Scope(string anime,string episode)=>anime+"\0"+episode+"\0";
    // Transport/TLS/DNS/handler failures are temporary, not proof that a host has no
    // source. Only an explicit HTTP refusal (or unusable media) is a source failure.
    public static bool IsNetwork(Exception e)=>e is ServersUnavailable {Network:true}
        || e is OperationCanceledException || e is HttpRequestException || e is IOException || e is TimeoutException
        || e is WebException || e is DownloadFailure {Key:"DownloadNetwork" or "DownloadRateLimited"}
        || e is SocketException socket && socket.SocketErrorCode is SocketError.NetworkDown or SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.TimedOut
        || e.GetType().FullName is string name && (name.StartsWith("Java.Net.",StringComparison.Ordinal) || name.StartsWith("Javax.Net.",StringComparison.Ordinal))
        || e.InnerException is not null && IsNetwork(e.InnerException);
    public async Task<IReadOnlyList<VideoServer>> Discover(string anime,string episode,CancellationToken ct,bool refresh=false){
        var key=Scope(anime,episode);lock(gate)if(!refresh && lists.TryGetValue(key,out var entry) && entry.Items.Count>0 && DateTime.UtcNow-entry.Time<TimeSpan.FromSeconds(45))return entry.Items;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(DiscoveryTimeout);
        try{
            var found=await discover(anime,episode,deadline.Token).WaitAsync(deadline.Token);
            var result=Array.AsReadOnly(found.DistinctBy(ServerGroups.Key).ToArray());ct.ThrowIfCancellationRequested();
            // An empty answer is never cached: a transient backend hiccup must not make
            // the next tap report a permanent "no servers" result.
            lock(gate){if(result.Count>0){if(lists.Count>=8)lists.Remove(lists.Keys.First());lists[key]=(DateTime.UtcNow,result);}else lists.Remove(key);}
            return result;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){Console.WriteLine($"[YORU] Discovery TIMEOUT anime={anime} episode={episode}");throw new ServersUnavailable(true);}
        catch(Exception e) when(e is not OperationCanceledException){Console.WriteLine($"[YORU] Discovery FAIL anime={anime} episode={episode}: {e.GetType().Name}: {e.Message}");throw new ServersUnavailable(true);}
    }
    public void Invalidate(string anime,string episode){var prefix=Scope(anime,episode);lock(gate){foreach(var key in cache.Keys.Where(key=>key.StartsWith(prefix,StringComparison.Ordinal)).ToArray())cache.Remove(key);foreach(var key in rejected.Keys.Where(key=>key.StartsWith(prefix,StringComparison.Ordinal)).ToArray())rejected.Remove(key);lists.Remove(prefix);}}
    public async Task<ValidatedServer> Select(string anime,string episode,bool download,CancellationToken ct,VideoServer? preferred=null,IReadOnlyList<VideoServer>? servers=null,string? preferredChoice=null,ISet<string>? exclude=null,bool exclusivePreferred=false){
        servers??=await Discover(anime,episode,ct);var candidates=servers.Where(server=>exclude?.Contains(ServerGroups.Key(server))!=true).ToList();
        Console.WriteLine($"[YORU] Select anime={anime} episode={episode} download={download} servers={servers.Count} candidates={candidates.Count}");
        if(exclusivePreferred && preferred is not null){
            var exclusiveIndex=candidates.FindIndex(server=>ServerGroups.Key(server)==ServerGroups.Key(preferred));
            candidates=exclusiveIndex>=0?new List<VideoServer>{candidates[exclusiveIndex]}:new List<VideoServer>();
        }else if(preferred is not null){
            var index=candidates.FindIndex(server=>ServerGroups.Key(server)==ServerGroups.Key(preferred));
            if(index>=0){var first=candidates[index];var rest=candidates.Where((_,i)=>i!=index).OrderByDescending(QualityHeight).ThenBy(server=>server.Name).ToList();candidates=new List<VideoServer>{first};candidates.AddRange(rest);}
            else candidates=candidates.OrderByDescending(QualityHeight).ThenBy(server=>server.Name).ToList();
        }else{
            candidates=candidates.OrderByDescending(QualityHeight).ThenBy(server=>server.Name).ToList();
        }
        if(candidates.Count==0)throw new ServersUnavailable();
        // Every remaining host must get its own bounded attempt. Derive the overall
        // budget from the number of two-wide rounds instead of a fixed cut-off that
        // could leave later qualities unchecked on a slow mobile connection.
        var rounds=(candidates.Count+1)/2;
        var budgetTicks=Math.Min(TimeSpan.FromMinutes(2).Ticks,Math.Max(SearchTimeout.Ticks,SourceTimeout.Ticks*(rounds+1)+TimeSpan.FromSeconds(5).Ticks));
        using var search=CancellationTokenSource.CreateLinkedTokenSource(ct);search.CancelAfter(TimeSpan.FromTicks(budgetTicks));var network=false;
        async Task<ValidatedServer?> Try(VideoServer server){try{var result=await Check(anime,episode,server,servers,download,search.Token,preferredChoice);Console.WriteLine($"[YORU] Check OK server={server.Name} quality={server.Quality}");return result;}catch(OperationCanceledException) when(!ct.IsCancellationRequested){Console.WriteLine($"[YORU] Check TIMEOUT server={server.Name} quality={server.Quality}");network=true;return null;}catch(Exception e) when(e is not OperationCanceledException){Console.WriteLine($"[YORU] Check FAIL server={server.Name} quality={server.Quality}: {e.GetType().Name}: {e.Message}");if(IsNetwork(e))network=true;return null;}}
        // Give the preferred entry first refusal, then test at most two remaining
        // hosts concurrently. A slow failed host cannot serialize the whole list.
        var initial=await Try(candidates[0]);ct.ThrowIfCancellationRequested();if(initial is not null)return initial;
        var pending=new List<Task<ValidatedServer?>>();var cursor=1;
        try{
            while(!search.IsCancellationRequested && (cursor<candidates.Count || pending.Count>0)){
                while(pending.Count<2 && cursor<candidates.Count){var attempt=Try(candidates[cursor++]);if(attempt.IsCompletedSuccessfully && attempt.Result is { } cached){search.Cancel();return cached;}pending.Add(attempt);}
                var task=await Task.WhenAny(pending);pending.Remove(task);var value=await task;ct.ThrowIfCancellationRequested();if(value is not null){search.Cancel();return value;}
            }
            ct.ThrowIfCancellationRequested();throw new ServersUnavailable(network);
        }finally{search.Cancel();foreach(var task in pending)try{await task;}catch(OperationCanceledException){}}
    }
    public async Task<ValidatedServer> Check(string anime,string episode,VideoServer server,IReadOnlyList<VideoServer> servers,bool download,CancellationToken ct,string? preferredChoice=null){
        var key=Scope(anime,episode)+(download?"download":"stream")+"\0"+ServerGroups.Key(server);
        lock(gate){if(cache.TryGetValue(key,out var ready) && ready.Fresh && (preferredChoice is null || ready.Plan?.Choice.Id==preferredChoice))return ready with{Servers=servers};if(rejected.TryGetValue(key,out var failed) && DateTime.UtcNow-failed.Time<TimeSpan.FromSeconds(3))throw new ServersUnavailable(failed.Network);}
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(SourceTimeout);
        try{
            var media=await resolve(server,timeout.Token).WaitAsync(timeout.Token);ValidatedServer result;
            Console.WriteLine($"[YORU] Resolve OK server={server.Name} quality={server.Quality} host={(Uri.TryCreate(media.Link,UriKind.Absolute,out var mediaUri)?mediaUri.Host:"?")} mime={media.MimeType}");
            if(download){
                var sources=new DownloadSources(http);var options=await sources.Inspect(media,timeout.Token);DownloadPlan? plan=null;Exception? failure=null;
                foreach(var choice in options.OrderBy(choice=>choice.Id==preferredChoice?0:1)){
                    try{var candidate=await sources.Prepare(choice,media.Referer,timeout.Token);if(candidate.Archive?.Objects.FirstOrDefault() is { } first)await Probe(first.Url,media.Referer,timeout.Token,false);plan=candidate;break;}
                    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch(Exception e){failure=e;/* Another actual rendition may be downloadable. */}
                }
                if(plan is null)throw new ServersUnavailable(IsNetwork(failure??new ServersUnavailable()));result=new(server,media,servers,DateTime.UtcNow,options,plan);
            }else{
                // resolve() already read the first bytes and verified direct media.
                // Probe again only for playlists and unverified responses; a second
                // request to an already-confirmed MP4 only adds a mobile failure point.
                if(NeedsProbe(media))await Probe(media.Link,media.Referer,timeout.Token,true);
                result=new(server,media,servers,DateTime.UtcNow);
            }
            ct.ThrowIfCancellationRequested();lock(gate){foreach(var stale in cache.Where(entry=>!entry.Value.Fresh).Select(entry=>entry.Key).ToArray())cache.Remove(stale);if(cache.Count>=8)cache.Remove(cache.Keys.First());if((result.Plan?.Archive?.Objects.Count??0)<2000)cache[key]=result;rejected.Remove(key);}return result;
        }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception e){lock(gate){if(rejected.Count>=32)rejected.Remove(rejected.Keys.First());rejected[key]=(DateTime.UtcNow,IsNetwork(e));}if(IsNetwork(e))throw new ServersUnavailable(true);throw new ServersUnavailable();}
    }
    public async Task<DownloadPlan> Quality(ValidatedServer ready,DownloadChoice choice,CancellationToken ct){
        if(ready.Choices?.Any(option=>option.Id==choice.Id && option.Url==choice.Url)!=true)throw new ServersUnavailable();
        if(ready.Fresh && ready.Plan?.Choice.Id==choice.Id)return ready.Plan;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(SourceTimeout);
        try{var plan=await new DownloadSources(http).Prepare(choice,ready.Media.Referer,timeout.Token);if(plan.Archive?.Objects.FirstOrDefault() is { } first)await Probe(first.Url,ready.Media.Referer,timeout.Token,false);return plan;}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception e){throw new ServersUnavailable(IsNetwork(e));}
    }
    private static bool NeedsProbe(VideoServer media){
        // PlaybackResolver sets Referer only after it has read and signature-checked
        // the media body. Unverified direct links (API metadata only) still get probed.
        var verified=media.Direct && media.Referer.Length>0;
        if(!verified || media.MimeType.Length==0)return true;
        if(media.MimeType.Contains("mpegurl",StringComparison.OrdinalIgnoreCase) || media.MimeType.Contains("dash",StringComparison.OrdinalIgnoreCase))return true;
        return media.Link.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase) || media.Link.EndsWith(".mpd",StringComparison.OrdinalIgnoreCase);
    }
    private async Task<HttpResponseMessage> Open(string url,string referer,CancellationToken ct){
        HttpResponseMessage response;
        try{response=await SendProbe(url,referer,true,ct);}
        catch(OperationCanceledException){throw;}
        catch(Exception e) when(IsNetwork(e)){response=await SendProbe(url,referer,false,ct);}
        if(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.RequestedRangeNotSatisfiable or HttpStatusCode.BadRequest or HttpStatusCode.NotImplemented){response.Dispose();response=await SendProbe(url,referer,false,ct);}
        try{DownloadSources.Check(response);return response;}catch{response.Dispose();throw;}
    }
    private async Task<HttpResponseMessage> SendProbe(string url,string referer,bool ranged,CancellationToken ct){
        var request=DownloadSources.Request(url,referer);if(ranged)request.Headers.Range=new RangeHeaderValue(0,4095);
        try{return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);}catch{request.Dispose();throw;}
    }
    private async Task Probe(string url,string referer,CancellationToken ct,bool playlist,int depth=0,bool encrypted=false){
        if(depth>3)throw new ServersUnavailable();using var response=await Open(url,referer,ct);
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var memory=new MemoryStream();var buffer=new byte[4096];
        while(memory.Length<4096){var count=await stream.ReadAsync(buffer.AsMemory(0,4096-(int)memory.Length),ct);if(count==0)break;memory.Write(buffer,0,count);}var data=memory.ToArray();
        var text=Encoding.UTF8.GetString(data).TrimStart('\uFEFF',' ','\r','\n');
        if(playlist && (text.StartsWith("#EXTM3U") || Regex.IsMatch(text,@"<(?:\w+:)?MPD\b"))){
            var total=response.Content.Headers.ContentRange?.Length??response.Content.Headers.ContentLength??(data.Length<4096?data.Length:long.MaxValue);
            if(total>data.Length)text=await Manifest(url,referer,ct);
        }
        if(playlist && Regex.IsMatch(text,@"<(?:\w+:)?MPD\b")){
            if(text.Contains("<!DOCTYPE",StringComparison.OrdinalIgnoreCase) || text.Contains("ContentProtection",StringComparison.Ordinal))throw new ServersUnavailable();
            await Dash(text,response.RequestMessage?.RequestUri??new Uri(url),referer,ct,depth);return;
        }
        if(text.StartsWith('<') || text.StartsWith('{') || data.Length<12)throw new ServersUnavailable();
        if(text.StartsWith("#EXTM3U")){
            if(!playlist)throw new ServersUnavailable();var lines=text.Replace("\r","").Split('\n').Select(line=>line.Trim()).ToArray();
            var variants=lines.Select((line,index)=>(line,index)).Where(pair=>pair.line.StartsWith("#EXT-X-STREAM-INF:")).Select(pair=>lines.Skip(pair.index+1).FirstOrDefault(line=>line.Length>0 && !line.StartsWith('#'))).Where(line=>line is not null).ToArray();
            var next=variants.Length>0?variants:lines.Where(line=>line.Length>0 && !line.StartsWith('#')).Take(1).ToArray();
            var isEncrypted=lines.Any(line=>line.StartsWith("#EXT-X-KEY:") && !line.Contains("METHOD=NONE",StringComparison.Ordinal));
            foreach(var item in next){try{var address=DownloadSources.Address(item!,response.RequestMessage?.RequestUri??new Uri(url));await Probe(address.AbsoluteUri,referer,ct,variants.Length>0,depth+1,isEncrypted);return;}catch(Exception e) when(e is not OperationCanceledException && !IsNetwork(e)){}}
            throw new ServersUnavailable();
        }
        if(PlaybackResolver.LooksLikeMedia(data))return;
        if(data.Length>=8 && Encoding.ASCII.GetString(data,4,4) is "styp" or "moof" or "sidx" or "emsg" || data[0]==255 && (data[1]&240)==240 || data.AsSpan(0,3).SequenceEqual("ID3"u8))return;
        // AES-encrypted HLS segments remain a streaming concern; downloads reject
        // encrypted playlists before this probe. A segment still must be non-HTML.
        if(depth>0 && !playlist && encrypted && data.Length>=16)return;
        throw new ServersUnavailable();
    }
    private async Task<string> Manifest(string url,string referer,CancellationToken ct){
        using var response=await Open(url,referer,ct);
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var memory=new MemoryStream();var buffer=new byte[8192];
        while(memory.Length<=2_000_000){var count=await stream.ReadAsync(buffer,ct);if(count==0)return Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');memory.Write(buffer,0,count);}throw new ServersUnavailable();
    }
    private async Task Dash(string text,Uri manifest,string referer,CancellationToken ct,int depth){
        var document=System.Xml.Linq.XDocument.Parse(text);if(document.Root?.Name.LocalName!="MPD" || !document.Descendants().Any(node=>node.Name.LocalName=="Period"))throw new ServersUnavailable();
        var representations=document.Descendants().Where(node=>node.Name.LocalName=="Representation").ToArray();if(representations.Length==0)throw new ServersUnavailable();
        string Type(System.Xml.Linq.XElement node)=>node.AncestorsAndSelf().Select(element=>(string?)element.Attribute("mimeType")??(string?)element.Attribute("contentType")).FirstOrDefault(value=>!string.IsNullOrEmpty(value))??"";
        var video=representations.Where(node=>Type(node).Contains("video",StringComparison.OrdinalIgnoreCase) || node.Attribute("height") is not null).ToArray();if(video.Length==0)video=representations;
        async Task Representation(System.Xml.Linq.XElement node){
            var address=manifest;foreach(var element in node.AncestorsAndSelf().Reverse()){var part=element.Elements().FirstOrDefault(child=>child.Name.LocalName=="BaseURL")?.Value.Trim();if(!string.IsNullOrEmpty(part))address=DownloadSources.Address(part,address);}
            var templates=node.AncestorsAndSelf().SelectMany(element=>element.Elements().Where(child=>child.Name.LocalName=="SegmentTemplate")).ToArray();
            string? Attribute(string name)=>templates.Select(element=>(string?)element.Attribute(name)).FirstOrDefault(value=>!string.IsNullOrEmpty(value));
            string Expand(string value){
                var number=Attribute("startNumber")??"1";var time=templates.SelectMany(element=>element.Descendants().Where(child=>child.Name.LocalName=="S")).Select(element=>(string?)element.Attribute("t")).FirstOrDefault(value=>value is not null)??"0";
                value=value.Replace("$$","\u0001",StringComparison.Ordinal);
                value=Regex.Replace(value,@"\$(RepresentationID|Bandwidth|Number|Time)(?:%0(\d{1,2})d)?\$",match=>{
                    var replacement=match.Groups[1].Value switch{"RepresentationID"=>(string?)node.Attribute("id"),"Bandwidth"=>(string?)node.Attribute("bandwidth"),"Number"=>number,_=>time}??throw new ServersUnavailable();
                    if(match.Groups[2].Success){if(!long.TryParse(replacement,out var integer) || !int.TryParse(match.Groups[2].Value,out var digits) || digits>12)throw new ServersUnavailable();replacement=integer.ToString("D"+digits,System.Globalization.CultureInfo.InvariantCulture);}return replacement;
                });if(value.Contains('$'))throw new ServersUnavailable();return value.Replace('\u0001','$');
            }
            string? initialization=null,media=null;
            if(templates.Length>0){var init=Attribute("initialization");if(init is not null)initialization=Expand(init);var value=Attribute("media");if(value is null)throw new ServersUnavailable();media=Expand(value);}
            else{
                var list=node.AncestorsAndSelf().SelectMany(element=>element.Elements()).FirstOrDefault(child=>child.Name.LocalName=="SegmentList");
                if(list is not null){initialization=(string?)list.Elements().FirstOrDefault(child=>child.Name.LocalName=="Initialization")?.Attribute("sourceURL");media=(string?)list.Elements().FirstOrDefault(child=>child.Name.LocalName=="SegmentURL")?.Attribute("media");if(media is null)throw new ServersUnavailable();}
                else if(address!=manifest)media=address.AbsoluteUri;
            }
            if(media is null)throw new ServersUnavailable();if(initialization is not null)await Probe(DownloadSources.Address(initialization,address).AbsoluteUri,referer,ct,false,depth+1);
            await Probe(DownloadSources.Address(media,address).AbsoluteUri,referer,ct,false,depth+1);
        }
        async Task First(IEnumerable<System.Xml.Linq.XElement> candidates){foreach(var node in candidates){try{await Representation(node);return;}catch(Exception e) when(e is not OperationCanceledException && !IsNetwork(e)){}}throw new ServersUnavailable();}
        await First(video);var audio=representations.Where(node=>Type(node).Contains("audio",StringComparison.OrdinalIgnoreCase)).ToArray();if(audio.Length>0)await First(audio);
    }
    public void Dispose()=>http.Dispose();
}
