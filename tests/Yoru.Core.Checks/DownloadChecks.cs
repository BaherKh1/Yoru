using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yoru;
using Yoru.Core;

internal static class DownloadChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Fails(Func<Task> task,string key){try{await task();throw new Exception("Expected "+key);}catch(DownloadFailure e){Check(e.Key==key,"Expected "+key+", got "+e.Key);}}
    public static async Task Run(){
        var directory=Path.Combine(Path.GetTempPath(),"yoru-download-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            var fixture=new MediaFixtures();using var http=new HttpClient(fixture);var sources=new DownloadSources(http);var resolver=new PlaybackResolver(http,(_,_)=>Task.FromResult<JsonObject?>(null));
            var anime=Anime.Parse(new JsonObject{["objectID"]="offline-fixture",["name"]="Owned test fixture"});var episode=new Episode("e1","Episode 1","",new());var hlsEpisode=episode with{Id="e2",Name="Episode 2"};
            var serverA=new VideoServer("Fixture A","360p","https://download.fixture/server-a",false,false);var serverB=new VideoServer("Fixture B","720p","https://download.fixture/server-b",false,false);
            var a=await sources.Inspect(await resolver.Resolve(serverA,CancellationToken.None),CancellationToken.None);var mediaB=await resolver.Resolve(serverB,CancellationToken.None);var b=await sources.Inspect(mediaB,CancellationToken.None);
            Check(a.Count==1 && a[0].Label=="360p" && b.Count==1 && b[0].Label=="720p" && a[0].Url!=b[0].Url,"Direct qualities invented or selected server ignored");
            Check(b[0].Bytes==fixture.Video720.Length,"Actual direct file size lost");
            var unknown=await sources.Inspect(mediaB with{Quality=""},CancellationToken.None);Check(unknown.Count==1 && unknown[0].Label=="","Unknown source quality was replaced with a fake resolution");
            var library=new DownloadLibrary(directory);var job=await library.Enqueue(anime,episode,1,serverB,b[0]);
            await Fails(async()=>{await library.Enqueue(anime,episode,1,serverA,a[0]);},"DownloadDuplicate");
            Check(job.ServerName=="Fixture B" && job.ChoiceId==b[0].Id,"Server/quality selection was not persisted");
            var engine=new EpisodeDownloader(http,()=>4L*1024*1024*1024);var samples=new List<DownloadProgress>();library.Update(job.Id,item=>item with{Status=DownloadStatus.Downloading});
            var result=await engine.Run(await sources.Prepare(b[0],mediaB.Referer,CancellationToken.None),library.DirectoryFor(job.Id,true),library.DirectoryFor(job.Id),samples.Add,CancellationToken.None);
            library.Update(job.Id,item=>item with{Status=DownloadStatus.Completed,Bytes=result.Bytes,Total=result.Bytes,Percent=100});
            Check(File.ReadAllBytes(library.LocalPath(library.Find(job.Id)!)!).SequenceEqual(fixture.Video720),"Downloaded bytes do not correspond to chosen 720p server");
            Check(samples.Count>0 && samples.All(p=>p.Bytes<=fixture.Video720.Length && p.Percent is >=0 and <=99),"Direct progress is invalid");
            var master=new VideoServer("Fixture HLS","Auto","https://download.fixture/master.m3u8",true,false);
            var resolved=await resolver.Resolve(master,CancellationToken.None);var qualities=await sources.Inspect(resolved,CancellationToken.None);
            Check(qualities.Select(c=>c.Label).SequenceEqual(new[]{"720p","360p"}),"HLS choices do not reflect manifest resolutions");
            var choice=qualities.Single(c=>c.Height==720);var plan=await sources.Prepare(choice,resolved.Referer,CancellationToken.None);
            Check(plan.Estimated && plan.Bytes>0 && plan.Archive?.Duration>0,"HLS size estimate/duration missing");
            var hlsJob=await library.Enqueue(anime,hlsEpisode,2,master,choice);library.Update(hlsJob.Id,item=>item with{Status=DownloadStatus.Downloading});
            var archiveResult=await engine.Run(plan,library.DirectoryFor(hlsJob.Id,true),library.DirectoryFor(hlsJob.Id),_=>{},CancellationToken.None);
            // Simulate process death after atomic archive commit, before updating the library.
            var restarted=new DownloadLibrary(directory);var recovered=restarted.Find(hlsJob.Id)!;
            Check(recovered.Status==DownloadStatus.Completed,"A fully committed archive did not recover after process death");
            var local=restarted.LocalPath(recovered)!;var localText=await File.ReadAllTextAsync(local);
            Check(!localText.Contains("https:") && !localText.Contains("http:") && !localText.Contains("360"),"Offline playlist retains remote/incorrect rendition references");
            Check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(local)!,archiveResult.Files.First(name=>name.EndsWith(".ts")))).SequenceEqual(fixture.Segment720),"HLS downloaded a different quality");
            Check(archiveResult.Files.All(name=>File.Exists(Path.Combine(Path.GetDirectoryName(local)!,name))),"Offline archive is incomplete");
            // Export real playable H.264/AAC results for host decoder verification, not fake media.
            var exports=Path.Combine("evidence","offline-download-fixtures");Directory.CreateDirectory(exports);
            File.Copy(restarted.LocalPath(restarted.Find(job.Id)!)!,Path.Combine(exports,"selected-server-720.mp4"),true);
            var hlsExport=Path.Combine(exports,"selected-hls-720");Directory.CreateDirectory(hlsExport);foreach(var name in archiveResult.Files)File.Copy(Path.Combine(Path.GetDirectoryName(local)!,name),Path.Combine(hlsExport,name),true);
            var fragmented=resolved with{Link="https://download.fixture/720-fmp4.m3u8",Quality="720p"};var fmp4Choices=await sources.Inspect(fragmented,CancellationToken.None);var fmp4Plan=await sources.Prepare(fmp4Choices.Single(),fragmented.Referer,CancellationToken.None);
            var fmp4Folder=Path.Combine(exports,"selected-fmp4-720");if(Directory.Exists(fmp4Folder))Directory.Delete(fmp4Folder,true);
            var fmp4=await engine.Run(fmp4Plan,Path.Combine(directory,"fragmented.partial"),fmp4Folder,_=>{},CancellationToken.None);
            Check(fmp4.Files.Length==3 && File.ReadAllText(Path.Combine(fmp4Folder,"index.m3u8")).Contains("#EXT-X-MAP:URI=\"part00000.mp4\""),"Fragmented MP4 archive lost initialization/audio/video data");
            var queuedEpisode=episode with{Id="e3"};var queued=await restarted.Enqueue(anime,queuedEpisode,3,serverA,a[0]);
            var interrupted=new DownloadLibrary(directory);Check(interrupted.Find(queued.Id)?.Status==DownloadStatus.Interrupted,"Interrupted queued job lost status on restart");
            await interrupted.Remove(queued.Id);Check(new DownloadLibrary(directory).Find(queued.Id) is null,"Deleted job reappears after restart");
            var failed=await interrupted.Enqueue(anime,queuedEpisode,3,serverA,a[0]);interrupted.Update(failed.Id,item=>item with{Status=DownloadStatus.Failed,Error="DownloadExpired"});
            var retry=await interrupted.Enqueue(anime,queuedEpisode,3,serverB,b[0]);Check(retry.ServerName==serverB.Name && interrupted.Items.Count(item=>item.Id==retry.Id)==1,"Retry duplicated the episode or retained old selection");
            interrupted.UpdateAttempt(failed.Id,failed.Attempt,item=>item with{Status=DownloadStatus.Completed});Check(interrupted.Find(retry.Id)?.Status==DownloadStatus.Queued,"A stale attempt overwrote a newer retry");
            interrupted.Cancel(retry.Id);Check(new DownloadLibrary(directory).Find(retry.Id)?.Status==DownloadStatus.Cancelled,"Cancelled status did not persist");
            var races=await Task.WhenAll(Enumerable.Range(0,12).Select(async _=>{try{await interrupted.Enqueue(anime,episode with{Id="race"},4,serverA,a[0]);return true;}catch(DownloadFailure){return false;}}));Check(races.Count(success=>success)==1,"Concurrent duplicate downloads accepted");
            var baseUri=new Uri("https://download.fixture/hls/");var vod="#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\n720.ts\n#EXT-X-ENDLIST\n";
            void Reject(string text,string key){try{DownloadSources.ParseMedia(text,baseUri);throw new Exception("Unsafe playlist accepted");}catch(DownloadFailure e){Check(e.Key==key,"Wrong unsupported playlist reason");}}
            Reject(vod.Replace("#EXT-X-ENDLIST", ""),"DownloadLive");Reject(vod.Replace("#EXTINF:","#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n#EXTINF:"),"DownloadProtected");
            Reject(vod.Replace("#EXTINF:","#EXT-X-BYTERANGE:10@0\n#EXTINF:"),"DownloadUnsupported");Reject(vod.Replace("#EXTINF:","#EXT-X-ALLOW-CACHE:NO\n#EXTINF:"),"DownloadDenied");Reject(vod.Replace("720.ts","http://download.fixture/720.ts"),"DownloadUnsupported");
            var mapped=DownloadSources.ParseMedia(vod.Replace("#EXTINF:","#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:"),baseUri);Check(mapped.Objects.Count==2 && mapped.Playlist.Contains("part00000.mp4") && !mapped.Playlist.Contains("init.mp4"),"fMP4 initialization map not localized");
            try{DownloadSources.Variants("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=640x360,AUDIO=\"a\"\nvideo.m3u8",baseUri);throw new Exception("External audio silently omitted");}catch(DownloadFailure e){Check(e.Key=="DownloadUnsupported","External track rejection failed");}
            var masterText=MediaFixtures.Master;var refreshed=DownloadSources.Variants(masterText.Replace("720.m3u8","720.m3u8?new=token"),new Uri("https://other-cdn.fixture/"));Check(refreshed.Single(c=>c.Height==720).Id==choice.Id,"Expiring CDN query tokens changed rendition identity");
            await Fails(async()=>{await sources.Inspect(serverB with{Link="https://download.fixture/denied"},CancellationToken.None);},"DownloadDenied");
            await Fails(async()=>{await sources.Inspect(serverB with{Link="https://download.fixture/removed"},CancellationToken.None);},"DownloadExpired");
            await Fails(async()=>{await sources.Inspect(serverB with{Link="https://download.fixture/fake-video"},CancellationToken.None);},"DownloadUnsupported");
            var direct=await sources.Prepare(b[0],mediaB.Referer,CancellationToken.None);var work=Path.Combine(directory,"storage.partial");var dest=Path.Combine(directory,"storage");
            await Fails(async()=>{await new EpisodeDownloader(http,()=>0).Run(direct,work,dest,_=>{},CancellationToken.None);},"DownloadStorage");Check(!Directory.Exists(dest),"Low-storage failure exposed incomplete media");
            await Fails(async()=>{await engine.Run(direct with{Choice=b[0] with{Url="https://download.fixture/truncated"}},work,dest,_=>{},CancellationToken.None);},"DownloadIncomplete");Check(!Directory.Exists(work) && !Directory.Exists(dest),"Truncated transfer was not cleaned up");
            await Fails(async()=>{await engine.Run(direct with{Choice=b[0] with{Url="https://download.fixture/protected.mp4"}},work,dest,_=>{},CancellationToken.None);},"DownloadProtected");
            using(var cancellation=new CancellationTokenSource()){
                try{await engine.Run(direct,work,dest,_=>cancellation.Cancel(),cancellation.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
                Check(!Directory.Exists(work) && !Directory.Exists(dest),"Cancelled partial download became playable");
            }
            var requests=fixture.Requests.Count;foreach(var file in archiveResult.Files)Check(File.ReadAllBytes(Path.Combine(hlsExport,file)).Length>0,"Offline file could not be read");Check(fixture.Requests.Count==requests,"Offline reads fetched network media");
            Check(MobileNavigation.BackTarget("Downloads")=="More" && MobileNavigation.SelectedTab("Downloads")=="More","Downloads broke More navigation");
            foreach(var pair in L.Entries.Where(pair=>pair.Key.StartsWith("Download") || pair.Key=="MobileHintDownloads"))Check(pair.Value.Length==2 && pair.Value[1].Any(c=>c>='\u0600'&&c<='\u06ff'),"Missing Arabic download translation: "+pair.Key);
            await restarted.Remove(job.Id);Check(!Directory.Exists(restarted.DirectoryFor(job.Id)) && restarted.Find(job.Id) is null,"Delete left offline bytes behind");
            Console.WriteLine("DOWNLOADS: selected server/quality -> persistent queue -> direct/HLS transfer -> committed local files -> restart/offline read/delete; real H.264/AAC results exported for host decoding.");
        }finally{Directory.Delete(directory,true);}
    }

    private sealed class MediaFixtures:HttpMessageHandler
    {
        private readonly string directory=Path.Combine("tests","fixtures","downloads");
        public byte[] Video720=>File.ReadAllBytes(Path.Combine(directory,"720.mp4"));public byte[] Segment720=>File.ReadAllBytes(Path.Combine(directory,"720part000.ts"));
        public readonly List<string> Requests=new();
        public const string Master="#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.42c01e,mp4a.40.2\"\n360.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2400000,RESOLUTION=1280x720,CODECS=\"avc1.42c01f,mp4a.40.2\"\n720.m3u8\n";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            ct.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;Requests.Add(path);byte[] bytes;var mime="video/mp4";var status=HttpStatusCode.OK;var truncated=false;
            if(path=="/server-a" || path=="/server-b"){bytes=Encoding.UTF8.GetBytes("<html><video src=\"https://download.fixture/"+(path.EndsWith('a')?"360":"720")+".mp4\"></video></html>");mime="text/html";}
            else if(path=="/master.m3u8"){bytes=Encoding.UTF8.GetBytes(Master);mime="application/vnd.apple.mpegurl";}
            else if(path is "/360.m3u8" or "/720.m3u8" or "/720-fmp4.m3u8"){bytes=File.ReadAllBytes(Path.Combine(directory,path[1..]));mime="application/vnd.apple.mpegurl";}
            else if(path is "/360part000.ts" or "/720part000.ts"){bytes=File.ReadAllBytes(Path.Combine(directory,path[1..]));mime="video/mp2t";}
            else if(path=="/fake-video"){bytes=Encoding.UTF8.GetBytes("<html>not media</html>");}
            else if(path=="/denied"){bytes=Array.Empty<byte>();status=HttpStatusCode.Forbidden;}
            else if(path=="/removed"){bytes=Array.Empty<byte>();status=HttpStatusCode.Gone;}
            else if(path=="/truncated"){bytes=Video720;truncated=true;}
            else if(path=="/protected.mp4"){bytes=Video720.Concat(new byte[]{0,0,0,8,(byte)'p',(byte)'s',(byte)'s',(byte)'h'}).ToArray();}
            else bytes=File.ReadAllBytes(Path.Combine(directory,path[1..]));
            var length=bytes.Length;var range=request.Headers.Range?.Ranges.FirstOrDefault();
            if(range is not null && status==HttpStatusCode.OK){bytes=bytes.Take((int)Math.Min(length,(range.To??(length-1))+1)).ToArray();status=HttpStatusCode.PartialContent;}
            var response=new HttpResponseMessage(status){RequestMessage=request,Content=new ByteArrayContent(bytes)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(mime);
            if(status==HttpStatusCode.PartialContent)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(0,bytes.Length-1,length);
            if(truncated)response.Content.Headers.ContentLength=length+20;
            return Task.FromResult(response);
        }
    }
}
