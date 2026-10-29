using System.Net;
using System.Text;
using System.Text.Json;
using Yoru;

namespace Yoru.Core;
public sealed record DownloadProgress(long Bytes,long? Total,int? Percent);

// Sequential streaming writes bound RAM usage; only atomically committed archives are playable.
public sealed class EpisodeDownloader(HttpClient http,Func<long> freeBytes)
{
    private const long Reserve=32L*1024*1024;
    public async Task<DownloadResult> Run(DownloadPlan plan,string partial,string destination,Action<DownloadProgress> progress,CancellationToken ct){
        if(freeBytes()<Reserve+(plan.Bytes??0))throw new DownloadFailure("DownloadStorage");
        if(Directory.Exists(partial))Directory.Delete(partial,true);Directory.CreateDirectory(partial);long bytes=0,lastSpaceCheck=0;var files=new List<string>();
        try{
            var archive=plan.Archive;var objects=archive?.Objects??new[]{new DownloadObject(plan.Choice.Url,"video"+Extension(plan.Choice.MimeType,plan.Choice.Url))};
            for(var i=0;i<objects.Count;i++){
                var resource=objects[i];if(resource.Name!=Path.GetFileName(resource.Name) || resource.Name.Contains('\\'))throw new DownloadFailure("DownloadUnsupported");
                using var request=DownloadSources.Request(resource.Url,plan.Referer);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);DownloadSources.Check(response);
                if(response.StatusCode==HttpStatusCode.PartialContent)throw new DownloadFailure("DownloadIncomplete");
                var type=response.Content.Headers.ContentType?.MediaType??"";if(type.Contains("html",StringComparison.OrdinalIgnoreCase) || type.Contains("json",StringComparison.OrdinalIgnoreCase))throw new DownloadFailure("DownloadUnsupported");
                var size=response.Content.Headers.ContentLength;if(freeBytes()<Reserve+(size??0))throw new DownloadFailure("DownloadStorage");
                var path=Path.Combine(partial,resource.Name);long received=0;var first=true;var buffer=new byte[64*1024];
                await using(var input=await response.Content.ReadAsStreamAsync(timeout.Token))
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,true)){
                    using var header=new MemoryStream();
                    while(true){timeout.CancelAfter(TimeSpan.FromSeconds(45));int count;try{count=await input.ReadAsync(buffer,timeout.Token);}catch(IOException){throw new DownloadFailure("DownloadNetwork");}if(count==0)break;
                        if(bytes-lastSpaceCheck>=4*1024*1024){if(freeBytes()<Reserve+count)throw new DownloadFailure("DownloadStorage");lastSpaceCheck=bytes;}
                        if(first){header.Write(buffer,0,Math.Min(count,4096-(int)header.Length));if(header.Length>=512 || (size is { } small && header.Length>=small)){
                            if(!MediaHeader(header.ToArray(),archive is not null))throw new DownloadFailure("DownloadUnsupported");first=false;
                        }}
                        await output.WriteAsync(buffer.AsMemory(0,count),ct);received+=count;bytes+=count;
                        var percent=archive is null?size>0?(int?)Math.Min(99,received*100/size.Value):null:(int?)Math.Min(99,(i+(size>0?Math.Min(1,received/(double)size.Value):0))*100/objects.Count);
                        progress(new(bytes,archive is null?size:plan.Bytes,percent));
                    }
                    if(received==0 || size is { } expected && received!=expected)throw new DownloadFailure("DownloadIncomplete");if(first && !MediaHeader(header.ToArray(),archive is not null))throw new DownloadFailure("DownloadUnsupported");output.Flush(true);
                }
                RejectProtectedMp4(path);
                files.Add(resource.Name);if(archive is not null)progress(new(bytes,plan.Bytes,(i+1)*100/objects.Count));
            }
            var name=archive is null?files[0]:"index.m3u8";if(archive is not null){await File.WriteAllTextAsync(Path.Combine(partial,name),archive.Playlist,ct);files.Add(name);}
            var result=new DownloadResult(name,plan.Choice.MimeType,bytes,files.ToArray());
            using(var marker=new FileStream(Path.Combine(partial,"complete.json"),FileMode.CreateNew,FileAccess.Write)){JsonSerializer.Serialize(marker,result);marker.Flush(true);}
            ct.ThrowIfCancellationRequested();if(Directory.Exists(destination))Directory.Delete(destination,true);Directory.Move(partial,destination);return result;
        }catch{if(Directory.Exists(partial))Directory.Delete(partial,true);throw;}
    }
    private static string Extension(string mime,string url)=>mime switch{"video/webm"=>".webm","video/x-matroska"=>".mkv","video/mp2t"=>".ts","video/x-msvideo"=>".avi",_=>new Uri(url).AbsolutePath.EndsWith(".mkv",StringComparison.OrdinalIgnoreCase)?".mkv":".mp4"};
    private static bool MediaHeader(byte[] bytes,bool hls){
        var text=Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF',' ','\r','\n');if(text.StartsWith('<') || text.StartsWith('{') || text.StartsWith("#EXTM3U"))return false;
        if(PlaybackResolver.LooksLikeMedia(bytes))return true;
        return hls && (bytes.Length>=8 && Encoding.ASCII.GetString(bytes,4,4) is "styp" or "moof" or "sidx" or "emsg" || bytes.Length>=2 && bytes[0]==255 && (bytes[1]&240)==240 || bytes.Length>=3 && bytes.AsSpan(0,3).SequenceEqual("ID3"u8));
    }
    private static void RejectProtectedMp4(string path){
        using var file=File.OpenRead(path);Span<byte> header=stackalloc byte[16];
        if(file.Length<8)return;file.ReadExactly(header[..8]);file.Position=0;
        if(Encoding.ASCII.GetString(header.Slice(4,4)) is not ("ftyp" or "styp" or "moof" or "sidx" or "emsg")){
            if(header[0]==0x47 && file.Length%188!=0)throw new DownloadFailure("DownloadIncomplete");return;
        }
        while(file.Position+8<=file.Length){file.ReadExactly(header[..8]);long size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[..4]);var kind=Encoding.ASCII.GetString(header.Slice(4,4));var prefix=8;
            if(size==1){if(file.Position+8>file.Length)throw new DownloadFailure("DownloadIncomplete");file.ReadExactly(header[8..]);var extended=System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(header[8..]);if(extended>long.MaxValue)throw new DownloadFailure("DownloadIncomplete");size=(long)extended;prefix=16;}
            if(size==0)size=file.Length-file.Position+prefix;if(size<prefix || size-prefix>file.Length-file.Position)throw new DownloadFailure("DownloadIncomplete");
            if(kind=="pssh")throw new DownloadFailure("DownloadProtected");
            if(kind=="moov"){
                if(size-prefix>32*1024*1024)throw new DownloadFailure("DownloadUnsupported");var metadata=new byte[(int)size-prefix];file.ReadExactly(metadata);
                foreach(var box in new[]{"pssh","encv","enca"})if(metadata.AsSpan().IndexOf(Encoding.ASCII.GetBytes(box))>=0)throw new DownloadFailure("DownloadProtected");
            }else file.Position+=size-prefix;
        }
        if(file.Position!=file.Length)throw new DownloadFailure("DownloadIncomplete");
    }
}
