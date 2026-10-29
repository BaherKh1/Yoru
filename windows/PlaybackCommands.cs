namespace AnimeWitcherDesktop;

// LibVLC seek/pause/stop can wait on demux/decoder work. Serialize away from WPF.
public sealed class PlaybackCommands
{
    private readonly object sync=new();
    private Task tail=Task.CompletedTask;
    private readonly CancellationTokenSource closed=new();
    private int seekVersion;
    public event Action<Exception>? Failed;
    public Task Enqueue(Action action)
    {
        lock(sync) return tail=tail.ContinueWith(_=> { if(!closed.IsCancellationRequested) try { action(); } catch(Exception error) { Failed?.Invoke(error); } },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
    }
    public async Task Seek(double seconds,Action<double> apply)
    {
        var version=Interlocked.Increment(ref seekVersion);
        try { await Task.Delay(75,closed.Token).ConfigureAwait(false); await Enqueue(()=> { if(version==Volatile.Read(ref seekVersion)) apply(seconds); }).ConfigureAwait(false); }
        catch(OperationCanceledException) { }
    }
    public async Task Complete()
    {
        closed.Cancel(); Task pending; lock(sync) pending=tail;
        try { await pending.ConfigureAwait(false); } catch(Exception) { /* Close still disposes native resources. */ }
    }
}
