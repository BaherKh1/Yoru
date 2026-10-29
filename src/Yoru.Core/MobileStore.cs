using System.Text.Json;
using Yoru;
namespace Yoru.Core;
public record WatchProgress(Anime Anime,Episode Episode,double Seconds,double Duration,DateTime Updated);
public sealed class MobileStore
{
    private sealed record DiskState(List<Anime> Favorites,List<WatchProgress> History,Dictionary<string,DateTime> Deleted);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,object> Gates=new();
    private readonly object gate;private readonly string directory;private DiskState state=new(new(),new(),new());private DiskState published=new(new(),new(),new());
    public event Action? Changed;
    public MobileStore(string directory) {
        this.directory=Path.GetFullPath(directory);gate=Gates.GetOrAdd(this.directory,_=>new object());Directory.CreateDirectory(directory);
        lock(gate){state=ReadDisk()??state;published=state;}
        AppLocale.Apply(ReadLanguage(directory));
    }
    public static string ReadLanguage(string directory) {try {using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"preferences.json")));return json.RootElement.GetProperty("language").GetString()=="ar"?"ar":"en";}catch{return "en";}}
    public IReadOnlyList<Anime> Favorites=>Volatile.Read(ref published).Favorites.ToList();
    public IReadOnlyList<WatchProgress> History=>Volatile.Read(ref published).History.OrderByDescending(item=>item.Updated).DistinctBy(item=>item.Anime.Id).ToList();
    public bool IsFavorite(string id)=>Volatile.Read(ref published).Favorites.Any(item=>item.Id==id);
    public Task ToggleFavorite(Anime anime)=>Mutate(()=> {var exists=state.Favorites.Any(item=>item.Id==anime.Id);state.Favorites.RemoveAll(item=>item.Id==anime.Id);if(!exists)state.Favorites.Insert(0,anime);});
    private DiskState? ReadDisk(){
        try {var file=Path.Combine(directory,"library.json");if(!File.Exists(file) || new FileInfo(file).Length>64*1024*1024)return null;var loaded=JsonSerializer.Deserialize<DiskState>(File.ReadAllText(file));return loaded is {Favorites:not null,History:not null,Deleted:not null} && loaded.Favorites.All(item=>item is not null) && loaded.History.All(item=>item?.Anime is not null && item.Episode is not null)?loaded:null;}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    private Task Mutate(Action change)=>Task.Run(()=> {
        lock(gate) {state=ReadDisk()??new DiskState(new(state.Favorites),new(state.History),new(state.Deleted));change();Write("library.json",state);Volatile.Write(ref published,state);}
        Changed?.Invoke();
    });
    public Task RemoveHistory(string id)=>Mutate(()=> {state.Deleted[id]=DateTime.UtcNow;state.History.RemoveAll(item=>item.Anime.Id==id);});
    public Task ClearHistory()=>Mutate(()=> {state.Deleted["*"]=DateTime.UtcNow;state.History.Clear();});
    public double Position(string anime,string episode)=>Volatile.Read(ref published).History.FirstOrDefault(item=>item.Anime.Id==anime && item.Episode.Id==episode)?.Seconds??0;
    public Task SaveProgress(Anime anime,Episode episode,double seconds,double duration,DateTime started)=>Mutate(()=> {
        if(state.Deleted.TryGetValue("*",out var all) && started<=all || state.Deleted.TryGetValue(anime.Id,out var removed) && started<=removed)return;
        state.History.RemoveAll(item=>item.Anime.Id==anime.Id);state.History.Insert(0,new(anime,episode,Math.Max(0,seconds),Math.Max(0,duration),DateTime.UtcNow));
        if(state.History.Count>100)state.History.RemoveRange(100,state.History.Count-100);
    });
    public Task SetLanguage(string language) {language=language=="ar"?"ar":"en";return Task.Run(()=> {lock(gate)Write("preferences.json",new{language});AppLocale.Apply(language);Changed?.Invoke();});}
    private void Write<T>(string name,T value) {var file=Path.Combine(directory,name);var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,JsonSerializer.Serialize(value));File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
public static class AppLocale {public static void Apply(string language)=>L.Language=language=="ar"?"ar":"en";}
