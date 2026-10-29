using AnimeWitcherDesktop;
namespace Yoru.Core;
public sealed class AppState : IDisposable
{
    public PlaybackRequest? Playback{get;set;}
    public WitcherApi Api{get;}=new();public ApiDataCache Cache{get;}public MobileStore Store{get;}public StartupCatalogStore HomeStore{get;}public EpisodeLibrary Episodes{get;}
    public ServerValidation Servers{get;}
    public SearchPage? Home{get;private set;}public string? HomeError{get;private set;}public bool Ready{get;private set;}public event Action? Changed;
    private readonly Dictionary<string,CatalogSession> sessions=new();private Task? startup;
    public AppState(string directory){AppStorage.Configure(directory);Store=new(directory);Cache=new(Api);Episodes=new((id,ct)=>Cache.Episodes(id,ct));HomeStore=new(directory);Servers=new(async(anime,episode,ct)=>{await Cache.Initialize(ct);return await Api.Servers(anime,episode,ct);},Api.Resolve,new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=true,AutomaticDecompression=System.Net.DecompressionMethods.All,ConnectTimeout=TimeSpan.FromSeconds(15),PooledConnectionLifetime=TimeSpan.FromMinutes(5)}){Timeout=Timeout.InfiniteTimeSpan});}
    public Task Bootstrap(bool retry=false){if(startup is null || retry && startup.IsCompleted)startup=LoadHome();return startup;}
    private async Task LoadHome(){
        HomeError=null;Ready=false;
        var cached=await HomeStore.Read();if(cached is not null){Home=cached;Changed?.Invoke();}
        try{await Cache.Initialize();var fresh=await Cache.Search("",0);if(!StartupCatalogStore.Equivalent(Home,fresh)){Home=fresh;Changed?.Invoke();}await HomeStore.Save(fresh);}
        catch(Exception e){HomeError=Home is null?L.Error(e):L.T("MobileOffline");}finally{Ready=true;Changed?.Invoke();}
    }
    public CatalogSession Catalog(string query){if(!sessions.TryGetValue(query,out var session)){session=new(query,async(q,p,ct)=>{await Cache.Initialize(ct);return await Cache.Search(q,p,ct);});if(sessions.Count>=12)sessions.Remove(sessions.Keys.First());sessions[query]=session;}return session;}
    public Anime? FindAnime(string id)=>(Home?.Items.AsEnumerable()??Enumerable.Empty<Anime>()).Concat(Store.Favorites).Concat(Store.History.Select(item=>item.Anime)).Concat(sessions.Values.SelectMany(session=>session.Items)).FirstOrDefault(anime=>anime.Id==id);
    public void Dispose(){Servers.Dispose();Api.Dispose();}
}

public sealed record PlaybackRequest(Anime Anime,Episode Episode,IReadOnlyList<Episode> Episodes,string? ServerLabel=null,string? OfflinePath=null,ValidatedServer? Validated=null);
