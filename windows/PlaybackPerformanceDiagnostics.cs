using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Interop;
using LibVLCSharp.Shared;

namespace Yoru;

internal static class PlaybackPerformanceDiagnostics
{
    internal static async Task<int> Run(string path,bool software=false)
    {
        using var output = new HwndSource(new HwndSourceParameters("Hidden native playback profiler") { Width=960, Height=540, WindowStyle=unchecked((int)0x80000000) });
        var handle = output.Handle;
        var result = await Task.Run(async () => {
            var measures = new Dictionary<string,object>(); string? error=null;
            try {
                PlayerWindow.InitializeEngine();
                using var engine = new LibVLC(PlayerWindow.EngineOptions("--aout=dummy","--no-video-title-show"));
                var gpuObserved=0; var gpuErrors=0; var lateMessages=0;
                engine.Log += (_, log) => { if(log.FormattedLog.Contains("Using D3D11VA",StringComparison.OrdinalIgnoreCase)||log.FormattedLog.Contains("Using DXVA2",StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref gpuObserved); if(log.Level==LogLevel.Error && ((log.Module??"").Contains("d3d11",StringComparison.OrdinalIgnoreCase)||(log.Module??"").Contains("dxva",StringComparison.OrdinalIgnoreCase))) Interlocked.Increment(ref gpuErrors); if(log.FormattedLog.Contains("late",StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref lateMessages); };
                using var player = new LibVLCSharp.Shared.MediaPlayer(engine) { Hwnd=handle, EnableHardwareDecoding=!software };
                var commandType=typeof(PlayerWindow).Assembly.GetType("Yoru.PlaybackCommands");
                var commands=commandType is null?null:Activator.CreateInstance(commandType);
                async Task Apply(Action action) { if(commands is null) action(); else await (Task)commandType!.GetMethod("Enqueue")!.Invoke(commands,new object[] { action })!; }
                measures["queuedNativeCommands"]=commands is not null;
                using var media = new Media(engine,new Uri(Path.GetFullPath(path)));
                var bufferEvents=0; player.Buffering += (_,e) => { if(e.Cache<100) Interlocked.Increment(ref bufferEvents); };
                using var process=Process.GetCurrentProcess(); var cpu=process.TotalProcessorTime; var watch=Stopwatch.StartNew();
                if(!player.Play(media)) throw new Exception("Native profiler could not start fixture");
                while(player.Time<400 && watch.Elapsed.TotalSeconds<10 && player.State!=VLCState.Error) await Task.Delay(50);
                measures["requestedDecoder"]=software?"software":"automatic";
                measures["startupMs"]=watch.Elapsed.TotalMilliseconds;
                if(player.State==VLCState.Error) throw new Exception("Native decoder failed before first playback timestamp");
                var gaps=new List<double>(); long previousTime=player.Time; var lastAdvance=watch.Elapsed.TotalMilliseconds; var freezes=0; bool paused=false,sought=false; long pauseDrift=0;
                while(watch.Elapsed.TotalSeconds<9 && player.State is not (VLCState.Ended or VLCState.Error)) {
                    await Task.Delay(100); var now=watch.Elapsed.TotalMilliseconds; var time=player.Time;
                    if(time!=previousTime) { if(time>previousTime) gaps.Add(now-lastAdvance); lastAdvance=now; previousTime=time; }
                    if(now-lastAdvance>800 && player.State==VLCState.Playing) { freezes++; lastAdvance=now; }
                    if(!paused && now>2500) { await Apply(()=>player.SetPause(true)); await Task.Delay(150); var before=player.Time; await Task.Delay(350); pauseDrift=Math.Abs(player.Time-before); await Apply(()=>player.SetPause(false)); paused=true; lastAdvance=watch.Elapsed.TotalMilliseconds; }
                    if(!sought && now>4800) { if(commands is null) player.Time=3000; else await (Task)commandType!.GetMethod("Seek")!.Invoke(commands,new object[] { 3d,new Action<double>(position=> { player.SetAudioDelay(0); player.Time=(long)(position*1000); }) })!; sought=true; lastAdvance=watch.Elapsed.TotalMilliseconds; previousTime=player.Time; }
                }
                var stats=media.Statistics;
                measures["durationMs"]=watch.Elapsed.TotalMilliseconds; measures["cpuMs"]=(process.TotalProcessorTime-cpu).TotalMilliseconds;
                measures["decodedVideo"]=stats.DecodedVideo; measures["displayedPictures"]=stats.DisplayedPictures; measures["lostPictures"]=stats.LostPictures;
                measures["decodedAudio"]=stats.DecodedAudio; measures["playedAudioBuffers"]=stats.PlayedAudioBuffers; measures["lostAudioBuffers"]=stats.LostAudioBuffers;
                measures["bufferingEvents"]=bufferEvents; measures["unexpectedClockStalls"]=freezes; measures["pauseClockDriftMs"]=pauseDrift; measures["configuredAudioDelayUs"]=player.AudioDelay;
                measures["gpuDecodeObserved"]=gpuObserved>0; measures["gpuErrorMessages"]=gpuErrors; measures["lateMessages"]=lateMessages;
                measures["clockAdvanceGapP95Ms"]=gaps.Count==0?0:gaps.Order().ElementAt((int)((gaps.Count-1)*0.95));
                measures["state"]=player.State.ToString(); measures["privateBytes"]=process.PrivateMemorySize64;
                measures["note"]="Local generated 1080p30 H.264 + AAC fixture; native hidden video output and dummy audio output. Measures decoder/render counters and playback-clock behavior, not physical speaker/display lip-sync.";
                if((stats.DecodedVideo)<30 || (stats.DecodedAudio)<10) throw new Exception("Fixture did not decode both video and audio tracks");
                if(commands is not null) await (Task)commandType!.GetMethod("Complete")!.Invoke(commands,null)!;
                player.Stop();
            } catch(Exception exception) { error=exception.ToString(); }
            return new { success=error is null, measures, error };
        });
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"playback-performance.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented=true }));
        return result.success?0:1;
    }
}
