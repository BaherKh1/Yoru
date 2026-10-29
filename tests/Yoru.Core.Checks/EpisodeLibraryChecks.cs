using System.Text.Json.Nodes;
using Yoru;
using Yoru.Core;

internal static class EpisodeLibraryChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static Episode Episode(string id)=>new(id,"Episode "+id,"",new JsonObject());
    public static async Task Run()
    {
        // Exercise the real shared cache: opening/reopening a view must never
        // mutate its List<Episode>, the original regression's ownership failure.
        using(var api=new CatalogApi(new FixtureHandler())){
            var cache=new ApiDataCache(api);await cache.Initialize();
            var library=new EpisodeLibrary((id,ct)=>cache.Episodes(id,ct));
            var apiList=await cache.Episodes("anime-1");var expected=apiList.Count;
            IReadOnlyList<Episode> view=await library.Load("anime-1");
            Check(expected>0 && view.Count==expected,"Initial episodes are missing");
            var denied=false;try{((ICollection<Episode>)view).Clear();}catch(NotSupportedException){denied=true;}
            Check(denied,"A view can clear a cached episode snapshot");
            view=Array.Empty<Episode>(); // Leave the page; discard only the view's reference.
            view=library.Cached("anime-1");
            Check(view.Count==expected && (await library.Load("anime-1")).Count==expected && apiList.Count==expected,"Reopening anime erased the shared episode list");
        }

        var first=new List<Episode>{Episode("A1"),Episode("A2")};
        var second=new List<Episode>{Episode("B1")};var fail=false;
        var snapshots=new EpisodeLibrary((id,_)=>fail?Task.FromException<List<Episode>>(new HttpRequestException("offline")):Task.FromResult(id=="A"?first:second));
        var a=await snapshots.Load("A");await snapshots.Load("B");
        Check(snapshots.Cached("A").Count==2 && snapshots.Cached("B")[0].Id=="B1","Anime caches are not isolated");
        first.Clear();
        Check(a.Count==2 && (await snapshots.Load("A")).Count==2,"Empty refresh erased known episodes or snapshot aliases API list");
        fail=true;try{await snapshots.Load("A");throw new Exception("Expected fetch failure");}catch(HttpRequestException){}
        Check(snapshots.Cached("A").Count==2,"Failed refresh erased warm episodes");

        var delayed=new TaskCompletionSource<List<Episode>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests=0;var pending=new EpisodeLibrary((id,_)=>{requests++;return id=="A"?delayed.Task:Task.FromResult(second);});
        using(var canceled=new CancellationTokenSource()){
            var old=pending.Load("A",canceled.Token);canceled.Cancel();await pending.Load("B");delayed.SetResult(new(){Episode("A3")});
            var observed=false;try{await old;}catch(OperationCanceledException){observed=true;}
            Check(observed && pending.Cached("A").Count==0 && pending.Cached("B")[0].Id=="B1","Late canceled request changed another anime's episodes");
            Check((await pending.Load("A"))[0].Id=="A3","Reopening after cancellation failed");
            var previous=requests;try{await pending.Load("A",canceled.Token);}catch(OperationCanceledException){}
            Check(previous==requests,"Already-canceled navigation issued another request");
        }

        var bounded=new EpisodeLibrary((id,_)=>Task.FromResult(new List<Episode>{Episode(id)}),2);
        await bounded.Load("A");await bounded.Load("B");bounded.Cached("A");await bounded.Load("C");
        Check(bounded.Cached("A").Count==1 && bounded.Cached("B").Count==0 && bounded.Cached("C").Count==1,"Bounded episode cache did not preserve recently viewed anime");
    }
}
