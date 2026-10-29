using Yoru;

namespace Yoru.Core;

// View-owned, immutable snapshots. Never give navigation a mutable API-cache list.
public sealed class EpisodeLibrary(Func<string,CancellationToken,Task<List<Episode>>> fetch,int capacity=16)
{
    private sealed record Entry(IReadOnlyList<Episode> Episodes,long Stamp);
    private readonly Dictionary<string,Entry> snapshots=new(StringComparer.Ordinal);
    private readonly object gate=new();
    private long stamp;
    public IReadOnlyList<Episode> Cached(string animeId)
    {
        lock(gate){if(!snapshots.TryGetValue(animeId,out var entry))return Array.Empty<Episode>();snapshots[animeId]=entry with{Stamp=++stamp};return entry.Episodes;}
    }
    public async Task<IReadOnlyList<Episode>> Load(string animeId,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var fetched=await fetch(animeId,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
        lock(gate){
            // A transient empty refresh must not erase a previously playable list.
            if(fetched.Count==0 && snapshots.TryGetValue(animeId,out var previous))return previous.Episodes;
            IReadOnlyList<Episode> owned=Array.AsReadOnly(fetched.ToArray());
            snapshots[animeId]=new(owned,++stamp);
            while(snapshots.Count>Math.Max(1,capacity))snapshots.Remove(snapshots.MinBy(pair=>pair.Value.Stamp).Key);
            return owned;
        }
    }
}
