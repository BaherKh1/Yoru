using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yoru;

// Only the public first-page catalog metadata is persisted. No credentials,
// episode streams, server responses or resolved media URLs are stored here.
public sealed class StartupCatalogStore
{
    private const int Limit=8*1024*1024;
    private readonly string path;
    private readonly SemaphoreSlim writes=new(1);
    private sealed record Snapshot(int Version,DateTime SavedUtc,SearchPage Page);
    public StartupCatalogStore(string? directory=null) => path=Path.Combine(directory ?? AppStorage.DirectoryPath,"home-catalog-v9.json");
    public Task<SearchPage?> Read(CancellationToken ct=default) => Task.Run(async () => {
        try {
            if(!File.Exists(path) || new FileInfo(path).Length is <=0 or >Limit) return null;
            var bytes=await File.ReadAllBytesAsync(path,ct).ConfigureAwait(false);
            if(bytes.Length>Limit) return null;
            var snapshot=JsonSerializer.Deserialize<Snapshot>(bytes);
            return snapshot is {Version:1} && Valid(snapshot.Page)?snapshot.Page:null;
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    },ct);
    public Task Save(SearchPage page,CancellationToken ct=default) => Task.Run(async () => {
        if(!Valid(page)) return;
        await writes.WaitAsync(ct).ConfigureAwait(false);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new Snapshot(1,DateTime.UtcNow,page));
            if(bytes.Length>Limit) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temp,bytes,ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); File.Move(temp,path,true);
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        finally { try { if(File.Exists(temp)) File.Delete(temp); } catch(IOException) { } catch(UnauthorizedAccessException) { } writes.Release(); }
    },ct);
    private static bool Valid(SearchPage? page) => page is {Page:0,Pages:>0,Total:>0,Items.Count:>0 and <=100} && page.Items.All(item=>item is not null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name) && item.Raw is not null && SafeMap(item.Raw,"poster") && SafeMap(item.Raw,"details") && SafeMap(item.Raw,"rating")) && page.Items.Select(item=>item.Id).Distinct().Count()==page.Items.Count;
    private static bool SafeMap(JsonObject raw,string key) => raw[key] is null or JsonObject;
    public static bool Equivalent(SearchPage? first,SearchPage second) => first is not null && first.Page==second.Page && first.Pages==second.Pages && first.Total==second.Total && first.Items.Count==second.Items.Count && first.Items.Zip(second.Items).All(pair=>SameAnime(pair.First,pair.Second));
    public static bool SameAnime(Anime first,Anime second) => first.Id==second.Id && first.Name==second.Name && first.Poster==second.Poster && first.Story==second.Story && first.Meta==second.Meta && JsonNode.DeepEquals(first.Raw,second.Raw);
}
