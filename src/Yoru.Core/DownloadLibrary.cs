using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Yoru;

namespace Yoru.Core;
public enum DownloadStatus {Queued,Downloading,Completed,Failed,Cancelled,Interrupted}
public sealed record EpisodeDownload(string Id,Anime Anime,Episode Episode,int EpisodeOrder,string ServerName,string ServerQuality,string ServerLink,string ChoiceId,string Quality,DownloadStatus Status,long Bytes=0,long? Total=null,int? Percent=null,string Error="",DateTime Updated=default,string Attempt=""){
    public bool Active=>Status is DownloadStatus.Queued or DownloadStatus.Downloading;
}
public sealed record DownloadResult(string FileName,string MimeType,long Bytes,string[] Files);

public sealed class DownloadLibrary
{
    private readonly object gate=new();private readonly string root;private EpisodeDownload[] items=Array.Empty<EpisodeDownload>();
    public event Action? Changed;
    public DownloadLibrary(string directory){
        root=Path.GetFullPath(directory);try{Directory.CreateDirectory(root);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){/* Report storage failure on download, not as an Application startup crash. */}items=Read("library.json")??Read("library.bak")??Array.Empty<EpisodeDownload>();
        items=items.Where(item=>item is not null && item.Anime is not null && item.Episode is not null && item.Id==Key(item.Anime.Id,item.Episode.Id)).DistinctBy(item=>item.Id).ToArray();
        var recovered=items.Select(item=>item.Active?TryResult(item.Id) is { } result?item with{Status=DownloadStatus.Completed,Bytes=result.Bytes,Percent=100,Error=""}:item with{Status=DownloadStatus.Interrupted,Error="DownloadInterrupted"}:item.Status==DownloadStatus.Completed && TryResult(item.Id) is null?item with{Status=DownloadStatus.Failed,Error="DownloadMissing"}:item).ToArray();
        if(!items.SequenceEqual(recovered)){items=recovered;try{Write();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){/* A full disk must not prevent app startup. */}}
    }
    public IReadOnlyList<EpisodeDownload> Items=>Array.AsReadOnly(Volatile.Read(ref items));
    public static string Key(string anime,string episode)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(anime+"\0"+episode))).ToLowerInvariant();
    public EpisodeDownload? Find(string id)=>Items.FirstOrDefault(item=>item.Id==id);
    public EpisodeDownload? Find(string anime,string episode)=>Find(Key(anime,episode));
    public string DirectoryFor(string id,bool partial=false){if(id.Length!=64 || !id.All(char.IsAsciiHexDigit))throw new ArgumentException("Invalid download ID");return Path.Combine(root,id+(partial?".partial":""));}
    public string? LocalPath(EpisodeDownload item)=>item.Status==DownloadStatus.Completed && TryResult(item.Id) is { } result?Path.Combine(DirectoryFor(item.Id),result.FileName):null;
    public DownloadResult? TryResult(string id){
        try{var folder=DirectoryFor(id);var marker=Path.Combine(folder,"complete.json");if(!File.Exists(marker) || new FileInfo(marker).Length>2_000_000)return null;
            var result=JsonSerializer.Deserialize<DownloadResult>(File.ReadAllText(marker));
            if(result is null || result.Bytes<=0 || result.Files is null || result.Files.Length>20002 || !result.Files.Contains(result.FileName) || result.Files.Any(name=>name.Length==0 || name!=Path.GetFileName(name) || name.Contains('\\')) || !File.Exists(Path.Combine(folder,result.FileName)) || new FileInfo(Path.Combine(folder,result.FileName)).Length==0)return null;
            return result;
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){return null;}
    }
    public Task<EpisodeDownload> Enqueue(Anime anime,Episode episode,int order,VideoServer server,DownloadChoice choice)=>Task.Run(()=>{
        EpisodeDownload created;lock(gate){var id=Key(anime.Id,episode.Id);var existing=items.FirstOrDefault(item=>item.Id==id);if(existing is {Active:true} || existing?.Status==DownloadStatus.Completed)throw new DownloadFailure("DownloadDuplicate");
            if(items.Length>=10000 && existing is null)throw new DownloadFailure("DownloadLibraryFull");
            created=new(id,anime,episode,order,server.Name,server.Quality,server.Link,choice.Id,choice.Label,DownloadStatus.Queued,Updated:DateTime.UtcNow,Attempt:Guid.NewGuid().ToString("N"));
            var previous=items;items=items.Where(item=>item.Id!=id).Append(created).ToArray();try{Write();}catch{items=previous;throw;}}
        Changed?.Invoke();return created;
    });
    public void Update(string id,Func<EpisodeDownload,EpisodeDownload> update){lock(gate){var previous=items;items=items.Select(item=>{if(item.Id!=id)return item;var changed=update(item);return changed==item?item:changed with{Updated=DateTime.UtcNow};}).ToArray();if(items.SequenceEqual(previous))return;try{Write();}catch{items=previous;throw;}}Changed?.Invoke();}
    public void UpdateAttempt(string id,string attempt,Func<EpisodeDownload,EpisodeDownload> update)=>Update(id,item=>item.Attempt==attempt?update(item):item);
    public void Cancel(string id)=>Update(id,item=>item.Active?item with{Status=DownloadStatus.Cancelled,Error="",Percent=null}:item);
    public void InterruptActive(string error="DownloadInterrupted"){lock(gate){if(!items.Any(item=>item.Active))return;items=items.Select(item=>item.Active?item with{Status=DownloadStatus.Interrupted,Error=error,Updated=DateTime.UtcNow}:item).ToArray();try{Write();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){/* Keep safe in-memory state when storage cannot persist it. Startup recovers old active entries. */}}Changed?.Invoke();}
    public Task Remove(string id)=>Task.Run(()=>{lock(gate){var item=items.FirstOrDefault(item=>item.Id==id);if(item is {Active:true})throw new DownloadFailure("DownloadBusy");
        Clean(id);var previous=items;items=items.Where(item=>item.Id!=id).ToArray();try{Write();}catch{items=previous;throw;}}Changed?.Invoke();});
    public void Clean(string id){foreach(var partial in new[]{true,false}){var directory=DirectoryFor(id,partial);if(Directory.Exists(directory))Directory.Delete(directory,true);}}
    private EpisodeDownload[]? Read(string name){try{var file=Path.Combine(root,name);return File.Exists(file) && new FileInfo(file).Length<=64*1024*1024?JsonSerializer.Deserialize<EpisodeDownload[]>(File.ReadAllText(file)):null;}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return null;}}
    private void Write(){Directory.CreateDirectory(root);var file=Path.Combine(root,"library.json");var temp=file+".tmp";try{using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,items);stream.Flush(true);}if(File.Exists(file))File.Copy(file,Path.Combine(root,"library.bak"),true);File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
