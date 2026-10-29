using Yoru;
namespace Yoru.Core;
public sealed class CatalogSession
{
    private readonly Func<string,int,CancellationToken,Task<SearchPage>> fetch;
    private readonly SemaphoreSlim loadGate=new(1);
    private readonly HashSet<string> ids=new();private readonly List<Anime> items=new();
    public string Query {get;}public int NextPage{get;private set;}public int Total{get;private set;}public int Pages{get;private set;}=1;
    public bool Loading{get;private set;}public bool Complete=>NextPage>=Pages;public string? Error{get;private set;}public int ScrollPosition{get;set;}
    public IReadOnlyList<Anime> Items=>items;
    public CatalogSession(string query,Func<string,int,CancellationToken,Task<SearchPage>> fetch){Query=query;this.fetch=fetch;}
    public async Task<bool> Load(CancellationToken ct=default) {
        if(Complete || !await loadGate.WaitAsync(0,ct))return false;
        try {if(Complete)return false;Loading=true;Error=null;var page=await fetch(Query,NextPage,ct);ct.ThrowIfCancellationRequested();
            foreach(var anime in page.Items)if(ids.Add(anime.Id))items.Add(anime);
            Total=page.Total;Pages=page.Pages;NextPage=page.Page+1;return true;
        }catch(OperationCanceledException){throw;}catch(Exception e){Error=L.Error(e);return false;}finally{Loading=false;loadGate.Release();}
    }
}
public static class AnimeMetadata
{
    public static string[] Art(Anime anime,bool banner=false) {
        var raw=anime.Raw;var urls=(banner?new[]{raw.Text("cover_uri"),raw.Text("banner_uri"),raw["poster"].Text("large"),anime.Poster,raw.Text("poster_uri")}:new[]{anime.Poster,raw["poster"].Text("large"),raw.Text("poster_uri"),raw.Text("cover_uri")}).Where(Data.WebUrl).Distinct();
        var trailer=raw.Text("youtube_video_id");if(trailer.Length==11 && trailer.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-'))urls=urls.Append("https://i.ytimg.com/vi/"+trailer+"/hqdefault.jpg");return urls.ToArray();
    }
    public static string Year(Anime anime)=>anime.Raw["details"].Text("year");
    public static string[] Genres(Anime anime)=>(anime.Raw["tags"] as System.Text.Json.Nodes.JsonArray??new()).Select(value=>value?.ToString()??"").Where(value=>value.Length>0).ToArray();
    public static double Rating(Anime anime)=>Math.Max(anime.Raw["details"].Number("mal_mean"),anime.Raw["rating"].Number("rate"));
}
