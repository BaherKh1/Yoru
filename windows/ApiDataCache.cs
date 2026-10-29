namespace AnimeWitcherDesktop;

// UI data only. Server lists and resolved/signed playback URLs are never cached.
public sealed class ApiDataCache(WitcherApi api)
{
    private readonly AsyncResultCache<string,SearchPage> searches=new(32,TimeSpan.FromMinutes(2));
    private readonly AsyncResultCache<string,Anime> details=new(64,TimeSpan.FromMinutes(5));
    private readonly AsyncResultCache<string,List<Episode>> episodes=new(16,TimeSpan.FromMinutes(5));
    private readonly AsyncResultCache<string,bool> initialization=new(1,TimeSpan.FromHours(1));
    public async Task Initialize(CancellationToken ct=default) => _=await initialization.Get("settings",async()=> { await api.Initialize(); return true; },ct);
    public Task<SearchPage> Search(string query,int page=0,CancellationToken ct=default)=>searches.Get(query+"\0"+page,()=>api.Search(query,page),ct);
    public Task<Anime> Details(string id,CancellationToken ct=default)=>details.Get(id,()=>api.Details(id),ct);
    public Task<List<Episode>> Episodes(string id,CancellationToken ct=default)=>episodes.Get(id,()=>api.Episodes(id),ct);
    public void Clear() { searches.Clear(); details.Clear(); episodes.Clear(); initialization.Clear(); }
}

public sealed class AsyncResultCache<TKey,TValue>(int capacity,TimeSpan lifetime) where TKey:notnull
{
    private sealed record Entry(Task<TValue> Task,DateTime Expires,long Stamp);
    private readonly Dictionary<TKey,Entry> values=new();
    private readonly object sync=new();
    private long stamp;
    public Task<TValue> Get(TKey key,Func<Task<TValue>> fetch,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested(); Task<TValue> task;
        lock(sync) {
            if(values.TryGetValue(key,out var cached) && cached.Expires>DateTime.UtcNow && !cached.Task.IsFaulted && !cached.Task.IsCanceled) { values[key]=cached with { Stamp=++stamp }; task=cached.Task; }
            else {
                // Parsing/network continuations stay off the dispatcher even on cache misses.
                task=Task.Run(fetch); values[key]=new(task,DateTime.UtcNow+lifetime,++stamp);
                while(values.Count>capacity) values.Remove(values.MinBy(pair=>pair.Value.Stamp).Key);
            }
        }
        // Cancelling one view does not abort another consumer's shared request.
        return task.WaitAsync(ct);
    }
    public void Clear() { lock(sync) values.Clear(); }
}
