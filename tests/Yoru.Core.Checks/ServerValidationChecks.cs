using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text;
using AnimeWitcherDesktop;
using Yoru.Core;

internal static class ServerValidationChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static VideoServer Server(string name,string quality,string resource)=>new(name,quality,"https://server.fixture/"+resource,true,false,MimeType:resource.EndsWith("m3u8")?"application/x-mpegURL":"video/mp4");
    private static async Task Unavailable(Func<Task> action,bool network=false){try{await action();throw new Exception("Unavailable server was accepted");}catch(ServersUnavailable e){Check(e.Network==network,"Server/network failure classification changed");}}
    public static async Task Run(){
        var bad=Server("KF","1080p","bad.mp4");var good=Server("MF","720p","good.mp4");var slow=Server("PD","720p","slow.mp4");var other=Server("KF","480p","other.mp4");
        var grouped=ServerGroups.Group(new[]{good,bad,other,slow,good,Server("AutoHost","Auto","master.m3u8"),Server("Unknown","","good.mp4")});
        Check(grouped.Select(group=>group.Quality).SequenceEqual(new[]{"1080p","720p","480p","","Auto"}),"Quality headings are invented or ordered incorrectly");
        Check(grouped.Single(group=>group.Quality=="720p").Servers.Select(server=>server.Name).SequenceEqual(new[]{"MF","PD"}),"Server grouping lost or duplicated entries");
        Check(ServerGroups.Quality("720 P")=="720p" && ServerGroups.Quality("480p / 720p")=="480p / 720p" && ServerGroups.Quality("2000 kb/s")=="2000 kb/s","Quality normalization invented a resolution");
        var handler=new Headers();var resolves=new Dictionary<string,int>();var apiCalls=0;var active=0;var peak=0;
        async Task<VideoServer> Resolve(VideoServer source,CancellationToken ct){
            lock(resolves){resolves[source.Link]=resolves.GetValueOrDefault(source.Link)+1;}var current=Interlocked.Increment(ref active);peak=Math.Max(peak,current);
            try{if(source.Link.EndsWith("slow.mp4"))await Task.Delay(10000,ct);if(source.Link.EndsWith("network.mp4"))throw new HttpRequestException("Offline",new SocketException((int)SocketError.NetworkUnreachable));return source;}
            finally{Interlocked.Decrement(ref active);}
        }
        using var validator=new ServerValidation((_,_,_)=>{apiCalls++;return Task.FromResult(new List<VideoServer>{bad,good,slow});},Resolve,new HttpClient(handler)){SourceTimeout=TimeSpan.FromMilliseconds(150),SearchTimeout=TimeSpan.FromSeconds(1)};
        var selected=await validator.Select("anime","1",false,CancellationToken.None);
        Check(selected.Source==good && selected.Media.Link==good.Link,"A non-playable default was selected instead of a working alternative");
        Check(peak<=2,"Fallback probes exceeded two concurrent hosts");var resolvedGood=resolves[good.Link];var requests=handler.Requests.Count;
        var again=await validator.Select("anime","1",false,CancellationToken.None);Check(again.Source==good && apiCalls==1 && resolves[good.Link]==resolvedGood && handler.Requests.Count==requests,"Fresh playback handoff caused repeated API/validation requests");
        var preferred=await validator.Select("anime","1",false,CancellationToken.None,other,new[]{good,other});Check(preferred.Source==other,"An explicitly selected working server/quality was ignored");
        var excluded=await validator.Select("anime","1",false,CancellationToken.None,servers:new[]{good,other},exclude:new HashSet<string>{ServerGroups.Key(good)});Check(excluded.Source==other,"Runtime-failed source was retried indefinitely");
        var beforeRefresh=resolves[good.Link];validator.Invalidate("anime","1");await validator.Select("anime","1",false,CancellationToken.None,good);Check(apiCalls==2 && resolves[good.Link]>beforeRefresh,"Retry did not refresh stale discovery and media links");
        await Unavailable(async()=>{await validator.Select("anime","empty",false,CancellationToken.None,servers:Array.Empty<VideoServer>());});
        await Unavailable(async()=>{await validator.Select("anime","bad",false,CancellationToken.None,servers:new[]{bad,Server("Dead","480p","missing.mp4")});});
        var networkServer=Server("Offline","720p","network.mp4");await Unavailable(async()=>{await validator.Check("anime","network",networkServer,new[]{networkServer},false,CancellationToken.None);},true);
        await Unavailable(async()=>{await validator.Check("anime","network",networkServer,new[]{networkServer},false,CancellationToken.None);},true);
        using(var cancel=new CancellationTokenSource(20)){
            try{await validator.Select("anime","cancel",false,cancel.Token,servers:new[]{slow});throw new Exception("Cancelled watch preflight completed");}catch(OperationCanceledException){}
        }
        var never=new TaskCompletionSource<VideoServer>();using(var timed=new ServerValidation((_,_,_)=>Task.FromResult(new List<VideoServer>{slow}),(_,_)=>never.Task,new HttpClient(new Headers())){SourceTimeout=TimeSpan.FromMilliseconds(25),SearchTimeout=TimeSpan.FromMilliseconds(100)}){
            var clock=System.Diagnostics.Stopwatch.StartNew();await Unavailable(async()=>{await timed.Select("anime","timeout",false,CancellationToken.None);},true);Check(clock.Elapsed<TimeSpan.FromSeconds(2),"Uncooperative resolver defeated bounded source timeouts");
        }
        var protectedHls=Server("Encrypted","1080p","encrypted.m3u8");var compatible=Server("HLS","720p","media.m3u8");
        var downloadable=await validator.Select("anime","download",true,CancellationToken.None,servers:new[]{protectedHls,compatible,good});
        Check(downloadable.Plan is not null && downloadable.Choices?.Count>0 && downloadable.Source!=protectedHls,"Download default did not validate actual compatibility");
        var checkedHls=await validator.Check("anime","vod",compatible,new[]{compatible},true,CancellationToken.None);Check(checkedHls.Plan?.Archive?.Objects.Count==1,"Compatible HLS was rejected");
        var mediaRequests=handler.Requests.Count;var plan=await validator.Quality(checkedHls,checkedHls.Plan!.Choice,CancellationToken.None);Check(handler.Requests.Count==mediaRequests && plan==checkedHls.Plan,"Selected ready quality was redundantly probed");
        await Unavailable(async()=>{await validator.Quality(checkedHls,checkedHls.Plan!.Choice with{Url="https://another.fixture/video.mp4"},CancellationToken.None);});
        await Unavailable(async()=>{await validator.Check("anime","broken-hls",Server("Broken","720p","broken.m3u8"),new[]{compatible},true,CancellationToken.None);});
        var adaptive=Server("Adaptive","Auto","master.m3u8");await validator.Check("anime","adaptive",adaptive,new[]{adaptive},false,CancellationToken.None);
        var encryptedStream=await validator.Check("anime","aes",protectedHls,new[]{protectedHls},false,CancellationToken.None);Check(encryptedStream.Source==protectedHls,"Ordinary AES HLS streaming was accidentally disabled by download restrictions");
        var dash=Server("DASH","720p","manifest.mpd") with{MimeType="application/dash+xml"};await validator.Check("anime","dash",dash,new[]{dash},false,CancellationToken.None);
        await validator.Check("anime","dash-template",dash with{Link="https://server.fixture/template.mpd"},new[]{dash},false,CancellationToken.None);
        await Unavailable(async()=>{await validator.Check("anime","dash-broken",dash with{Link="https://server.fixture/invalid.mpd"},new[]{dash},false,CancellationToken.None);});
        Check(handler.Requests.Where(entry=>entry.Url.EndsWith("good.mp4") || entry.Url.EndsWith("other.mp4") || entry.Url.EndsWith("part.ts")).All(entry=>entry.Range is not null),"Validation requested a full video instead of bounded headers");
        Check(handler.VideoBytes<160*1024 && !handler.Requests.Any(entry=>entry.Url.Contains("secret.key")),"Preflight downloaded too much media or fetched encryption keys");
        foreach(var key in new[]{"ServerUnavailable","NoServersTitle","NoServersMessage","CheckingServers","CheckingServerMessage","ServerSourceQuality"})Check(L.Entries[key][1].Any(c=>c>='\u0600'&&c<='\u06ff'),"Missing server-state Arabic translation");
        Check(L.Entries["ServerUnavailable"][0]=="Server unavailable. Please select another server.","Unavailable server message differs from requested copy");
        Check(L.Entries["NoServersMessage"][0]=="No streaming servers are currently available for this episode. Please try again later.","No-servers message differs from requested copy");
        // Discovery failures are temporary API problems, never proof that the episode
        // has no servers. They must also never poison the next tap with an empty cache.
        using(var unreachable=new ServerValidation((_,_,_)=>Task.FromException<List<VideoServer>>(new HttpRequestException("offline")),(s,_)=>Task.FromResult(s),new HttpClient(new Headers()))){
            await Unavailable(async()=>{await unreachable.Select("anime","discovery",false,CancellationToken.None);},true);
        }
        using(var stalled=new ServerValidation(async(_,_,ct)=>{await Task.Delay(10000,ct);return new List<VideoServer>();},(s,_)=>Task.FromResult(s),new HttpClient(new Headers())){DiscoveryTimeout=TimeSpan.FromMilliseconds(25)}){
            var clock=System.Diagnostics.Stopwatch.StartNew();await Unavailable(async()=>{await stalled.Select("anime","discovery-timeout",false,CancellationToken.None);},true);Check(clock.Elapsed<TimeSpan.FromSeconds(2),"Discovery timeout was not bounded");
        }
        var discoveryCalls=0;
        using(var transient=new ServerValidation((_,_,_)=>Task.FromResult(++discoveryCalls==1?new List<VideoServer>():new List<VideoServer>{good}),(s,_)=>Task.FromResult(s with{Direct=true,MimeType="video/mp4"}),new HttpClient(new Headers()))){
            await Unavailable(async()=>{await transient.Select("anime","empty-refresh",false,CancellationToken.None);});
            var recovered=await transient.Select("anime","empty-refresh",false,CancellationToken.None);Check(recovered.Source==good && discoveryCalls==2,"An empty discovery result was cached as permanent");
        }
        Check(ServerValidation.IsNetwork(new HttpRequestException("tls",new IOException("reset"))) && ServerValidation.IsNetwork(new IOException("socket")) && ServerValidation.IsNetwork(new TimeoutException()),"Transport failures are still reported as missing sources");
        Check(!ServerValidation.IsNetwork(new PlaybackException("RemovedVideo",404)),"A removed source was misclassified as a network failure");
        // resolve() already verified these bytes; a redundant second request must not
        // turn a confirmed direct source into "unavailable" on a flaky connection.
        using(var noSecondRequest=new ServerValidation((_,_,_)=>Task.FromResult(new List<VideoServer>{good}),(s,_)=>Task.FromResult(s with{Direct=true,MimeType="video/mp4",Referer=s.Link}),new HttpClient(new Throwing()))){
            var confirmed=await noSecondRequest.Check("anime","direct",good,new[]{good},false,CancellationToken.None);Check(confirmed.Media.Direct,"Confirmed direct media was rejected");
        }
        using(var flakyProbe=new ServerValidation((_,_,_)=>Task.FromResult(new List<VideoServer>{good}),(s,_)=>Task.FromResult(s with{Direct=false,MimeType=""}),new HttpClient(new Throwing()))){
            await Unavailable(async()=>{await flakyProbe.Check("anime","probe-network",good,new[]{good},false,CancellationToken.None);},true);
        }
        var attempts=new Dictionary<string,int>();var failing=new[]{"A","B","C","D"}.Select((name,index)=>Server(name,"1080p","attempt-"+index+".mp4")).ToArray();
        using(var exhaustive=new ServerValidation((_,_,_)=>Task.FromResult(failing.ToList()),(s,_)=>{lock(attempts)attempts[s.Link]=attempts.GetValueOrDefault(s.Link)+1;return Task.FromException<VideoServer>(new PlaybackException("RemovedVideo",404));},new HttpClient(new Headers()))){
            await Unavailable(async()=>{await exhaustive.Select("anime","all-fail",false,CancellationToken.None);});
            Check(attempts.Count==failing.Length && attempts.Values.All(count=>count==1),"Not every server was attempted exactly once");
        }
        using(var rangeFallback=new ServerValidation((_,_,_)=>Task.FromResult(new List<VideoServer>{good}),(s,_)=>Task.FromResult(s with{Direct=false,MimeType=""}),new HttpClient(new RejectsRange()))){
            var recovered=await rangeFallback.Check("anime","range",good,new[]{good},false,CancellationToken.None);Check(recovered.Media.Link==good.Link,"A host rejecting Range was treated as unavailable");
        }
        Console.WriteLine("SERVERS: API qualities grouped; unavailable defaults skipped; cached handoff; timeout/cancel/retry; compatible HLS vs protected downloads; HTML headers rejected; media probes bounded to headers.");
    }
    private sealed class Throwing:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromException<HttpResponseMessage>(new HttpRequestException("transport",new IOException("connection reset")));
    }
    private sealed class RejectsRange:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            if(request.Headers.Range is not null){var rejected=new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable){RequestMessage=request,Content=new ByteArrayContent(Array.Empty<byte>())};return Task.FromResult(rejected);}
            var bytes=new byte[]{0,0,0,24,102,116,121,112,105,115,111,109,0,0,0,0};
            var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)};response.Content.Headers.ContentType=new MediaTypeHeaderValue("video/mp4");return Task.FromResult(response);
        }
    }
    private sealed class Headers:HttpMessageHandler
    {
        public readonly List<(string Url,string? Range)> Requests=new();public long VideoBytes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            ct.ThrowIfCancellationRequested();lock(Requests)Requests.Add((request.RequestUri!.AbsoluteUri,request.Headers.Range?.ToString()));
            var file=request.RequestUri!.AbsolutePath;var mime="video/mp4";byte[] bytes;var status=HttpStatusCode.OK;
            var vod="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:1,\npart.ts\n#EXT-X-ENDLIST\n";
            if(file.EndsWith("bad.mp4")){bytes=Encoding.UTF8.GetBytes("<html>This server is unavailable</html>");}
            else if(file.EndsWith("missing.mp4")){bytes=Array.Empty<byte>();status=HttpStatusCode.NotFound;}
            else if(file.EndsWith("master.m3u8")){bytes=Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720\nbroken.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nmedia.m3u8\n");mime="application/x-mpegURL";}
            else if(file.EndsWith("encrypted.m3u8")){bytes=Encoding.UTF8.GetBytes(vod.Replace("#EXTINF:","#EXT-X-KEY:METHOD=AES-128,URI=\"secret.key\"\n#EXTINF:").Replace("part.ts","opaque.ts"));mime="application/x-mpegURL";}
            else if(file.EndsWith("broken.m3u8")){bytes=Encoding.UTF8.GetBytes(vod.Replace("part.ts","bad.mp4"));mime="application/x-mpegURL";}
            else if(file.EndsWith("media.m3u8")){bytes=Encoding.UTF8.GetBytes(vod);mime="application/x-mpegURL";}
            else if(file.EndsWith("manifest.mpd")){bytes=Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT2S\"><Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"1\" height=\"720\"><BaseURL>good.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>");mime="application/dash+xml";}
            else if(file.EndsWith("template.mpd")){bytes=Encoding.UTF8.GetBytes("<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\"><Period><AdaptationSet mimeType=\"video/mp4\"><SegmentTemplate initialization=\"init-$RepresentationID$.mp4\" media=\"seg-$Number%05d$.m4s\" startNumber=\"7\"/><Representation id=\"video\" height=\"720\"/></AdaptationSet></Period></MPD>");mime="application/dash+xml";}
            else if(file.EndsWith("invalid.mpd")){bytes=Encoding.UTF8.GetBytes("<MPD><Period><AdaptationSet><Representation id=\"1\"/></AdaptationSet></Period></MPD>");mime="application/dash+xml";}
            else if(file.EndsWith("init-video.mp4"))bytes=File.ReadAllBytes(Path.Combine("tests","fixtures","downloads","720-init.mp4"));
            else if(file.EndsWith("seg-00007.m4s"))bytes=File.ReadAllBytes(Path.Combine("tests","fixtures","downloads","720fragment000.m4s"));
            else if(file.EndsWith("opaque.ts")){bytes=Enumerable.Repeat((byte)125,1024).ToArray();mime="video/mp2t";}
            else if(file.EndsWith("part.ts")){bytes=File.ReadAllBytes(Path.Combine("tests","fixtures","downloads","720part000.ts"));mime="video/mp2t";}
            else{bytes=new byte[2*1024*1024];File.ReadAllBytes(Path.Combine("tests","fixtures","downloads","720.mp4")).CopyTo(bytes,0);}
            var original=bytes.Length;var range=request.Headers.Range?.Ranges.FirstOrDefault();
            if(range is not null && status==HttpStatusCode.OK){bytes=bytes.Take((int)Math.Min(original,(range.To??original-1)+1)).ToArray();status=HttpStatusCode.PartialContent;}
            var isVideo=!mime.Contains("mpegURL") && !mime.Contains("dash");var stream=new Metered(bytes,count=>{if(isVideo)Interlocked.Add(ref VideoBytes,count);});
            var response=new HttpResponseMessage(status){RequestMessage=request,Content=new StreamContent(stream)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(mime);response.Content.Headers.ContentLength=bytes.Length;
            if(status==HttpStatusCode.PartialContent)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(0,bytes.Length-1,original);return Task.FromResult(response);
        }
        private sealed class Metered(byte[] bytes,Action<int> count):MemoryStream(bytes){public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){var read=await base.ReadAsync(buffer,ct);count(read);return read;}}
    }
}
