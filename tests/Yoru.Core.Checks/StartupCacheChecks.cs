using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeWitcherDesktop;

internal static class StartupCacheChecks
{
    internal static async Task Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"yoru-home-cache-"+Guid.NewGuid().ToString("N"));
        var store=new StartupCatalogStore(directory);
        Check(await store.Read() is null,"Cold cache should be absent");
        var anime=Anime.Parse(new JsonObject { ["name"]="Captured title",["objectID"]="retained-id",["poster_uri"]="https://example.invalid/poster.jpg",["views"]=5 });
        var page=new SearchPage(new(){anime},0,2,50);
        await store.Save(page);
        var loaded=await new StartupCatalogStore(directory).Read();
        Check(StartupCatalogStore.Equivalent(loaded,page),"Persistent catalog changed metadata or paging");
        var reordered=anime with { Raw=new JsonObject { ["views"]=5,["poster_uri"]="https://example.invalid/poster.jpg",["objectID"]="retained-id",["name"]="Captured title" } };
        Check(StartupCatalogStore.Equivalent(loaded,page with { Items=new(){reordered} }),"JSON property order caused an unnecessary refresh");
        Check(!StartupCatalogStore.Equivalent(loaded,page with { Total=51 }),"Changed catalog totals were ignored");
        await store.Save(page with { Items=new(){anime,anime} });
        Check(StartupCatalogStore.Equivalent(await store.Read(),page),"Invalid duplicate snapshot overwrote the last good cache");
        using(var cancellation=new CancellationTokenSource()) {
            cancellation.Cancel(); try { await store.Save(page with {Total=52},cancellation.Token); throw new Exception("Cancelled cache write succeeded"); } catch(OperationCanceledException) { }
        }
        Check(StartupCatalogStore.Equivalent(await store.Read(),page),"Cancelled save lost the last good cache");
        await Task.WhenAll(store.Save(page),store.Save(page with {Total=60}));
        Check((await store.Read()) is {Total:50 or 60} && !Directory.GetFiles(directory,"*.tmp").Any(),"Concurrent writes were non-atomic or left temporary files");
        var path=Path.Combine(directory,"home-catalog-v9.json");
        var malformed=anime with {Raw=new JsonObject { ["details"]="invalid map" }};
        File.WriteAllText(path,JsonSerializer.Serialize(new {Version=1,SavedUtc=DateTime.UtcNow,Page=page with {Items=new(){malformed}}}));
        Check(await store.Read() is null,"Malformed metadata maps were accepted");
        File.WriteAllText(path,"{broken"); Check(await store.Read() is null,"Corrupt cache did not fail safely");
        File.WriteAllText(path,JsonSerializer.Serialize(new {Version=99,SavedUtc=DateTime.UtcNow,Page=page})); Check(await store.Read() is null,"Unsupported cache schema was accepted");
        using(var large=File.Create(path)) large.SetLength(8*1024*1024+1);
        Check(await store.Read() is null,"Oversized cache was not bounded");
        await store.Save(page); Check(StartupCatalogStore.Equivalent(await store.Read(),page),"Valid refresh did not repair invalid cache");
        Console.WriteLine("PASS: V9 persistent Home metadata, equality without property-order sensitivity, bounded/schema-safe reads, atomic writes, cancellation and corrupt-cache recovery.");
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
