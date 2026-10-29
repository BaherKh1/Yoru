using System.IO;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace Yoru;

public sealed class PlaybackHealth
{
    private MediaStats previous;
    private bool sampled;
    private int transition,writing;
    private long sampledAt;
    public void MarkTransition()=>Interlocked.Exchange(ref transition,1);
    public static string Classify(MediaStats before,MediaStats now,bool buffering,VLCState state)
    {
        if(state==VLCState.Error) return "source-or-decoder-error";
        if(state==VLCState.Paused) return "paused";
        if(buffering && now.ReadBytes<=before.ReadBytes) return "source-or-network-starvation";
        if(now.LostPictures>before.LostPictures || now.LostAudioBuffers>before.LostAudioBuffers) return "decoder-or-output-pressure";
        if(now.DemuxDiscontinuity>before.DemuxDiscontinuity || now.DemuxCorrupted>before.DemuxCorrupted) return "source-timestamp-or-data-discontinuity";
        return buffering?"buffering":"healthy";
    }
    public void Sample(MediaStats stats,VLCState state,bool buffering,bool software,long audioDelayUs=0)
    {
        if(Interlocked.Exchange(ref writing,1)!=0) return;
        try {
        var timestamp=System.Diagnostics.Stopwatch.GetTimestamp(); var seconds=sampled?(timestamp-sampledAt)/(double)System.Diagnostics.Stopwatch.Frequency:0;
        var displayedFps=seconds>0?Math.Max(0,stats.DisplayedPictures-previous.DisplayedPictures)/seconds:0;
        var decodedFps=seconds>0?Math.Max(0,stats.DecodedVideo-previous.DecodedVideo)/seconds:0;
        var intentionalTransition=Interlocked.Exchange(ref transition,0)!=0;
        var diagnosis=sampled?Classify(previous,stats,buffering,state):"starting"; if(intentionalTransition && state!=VLCState.Error) diagnosis="seeking-or-resuming"; previous=stats; sampled=true; sampledAt=timestamp;
        var record=new { capturedUtc=DateTime.UtcNow, state=state.ToString(), decoder=software?"software":"automatic", diagnosis, audioDelayUs, displayedFps, decodedFps, stats.DecodedVideo,stats.DisplayedPictures,stats.LostPictures,stats.DecodedAudio,stats.PlayedAudioBuffers,stats.LostAudioBuffers,stats.ReadBytes,stats.InputBitrate,stats.DemuxCorrupted,stats.DemuxDiscontinuity };
        // No URLs, tokens, anime titles, or raw LibVLC logs are persisted.
        var directory=AppStorage.DirectoryPath;
        try { Directory.CreateDirectory(directory); var file=Path.Combine(directory,"playback-health.json"); var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp"; try { File.WriteAllText(temp,JsonSerializer.Serialize(record)); File.Move(temp,file,true); } finally { if(File.Exists(temp)) File.Delete(temp); } }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
        } finally { Volatile.Write(ref writing,0); }
    }
}
