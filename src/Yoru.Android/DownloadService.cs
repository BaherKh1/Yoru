using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AnimeWitcherDesktop;
using Yoru.Core;
using DownloadStatus=Yoru.Core.DownloadStatus;
using OperationCanceledException=System.OperationCanceledException;

namespace Yoru.AndroidApp;

[Service(Exported=false,ForegroundServiceType=ForegroundService.TypeDataSync)]
public sealed class DownloadService:Service
{
    private const string Channel="yoru_episode_downloads";private const int Notice=9101;
    private readonly CancellationTokenSource lifetime=new();private CancellationTokenSource? current;private Task? worker;private string? currentId;private static volatile DownloadService? active;
    private readonly object gate=new();private bool stopping,running;private Android.OS.PowerManager.WakeLock? wake;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,TaskCompletionSource> writers=new();
    public static Task WaitForWriter(string id)=>writers.TryGetValue(id,out var done)?done.Task:Task.CompletedTask;
    private static DownloadLibrary Library=>YoruApp.Downloads;
    public override IBinder? OnBind(Intent? intent)=>null;
    public override void OnCreate(){base.OnCreate();active=this;var notifications=(NotificationManager)GetSystemService(NotificationService)!;notifications.CreateNotificationChannel(new NotificationChannel(Channel,L.T("Downloads"),NotificationImportance.Low));}
    public static void Start(Context context){var intent=new Intent(context,typeof(DownloadService));context.StartForegroundService(intent);}
    public static Task Cancel(string id)=>Task.Run(()=>{Library.Cancel(id);var service=active;if(service is null)return;lock(service.gate)if(service.currentId==id)service.current?.Cancel();});
    public static async Task Delete(string id){
        await Cancel(id);var running=WaitForWriter(id);
        // A cancelled writer must finish cleanup before deleting its directory/index.
        try{await running;}catch(OperationCanceledException){}
        await Library.Remove(id);
    }
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId){
        if(intent?.Action=="yoru.cancel-download"){
            var id=intent.GetStringExtra("id");if(id is not null)_=Cancel(id);
            if(worker is null){StopSelf();return StartCommandResult.NotSticky;}
            return StartCommandResult.NotSticky;
        }
        StartForeground(Notice,Notification(L.T("DownloadQueued"),null,null));
        if(!running){running=true;stopping=false;worker=Task.Run(RunQueue);}
        return StartCommandResult.NotSticky;
    }
    private async Task RunQueue(){
        var fatal=false;
        try{
            using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};var sources=new DownloadSources(http);
            wake=((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial,"YORU:episode-download")!;wake.Acquire(6*60*60*1000L);
            while(!lifetime.IsCancellationRequested){
                var job=Library.Items.FirstOrDefault(item=>item.Status==DownloadStatus.Queued);if(job is null)break;
                var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);if(!writers.TryAdd(job.Id,done)){await WaitForWriter(job.Id);continue;}
                void UpdateJob(Func<EpisodeDownload,EpisodeDownload> update)=>Library.UpdateAttempt(job.Id,job.Attempt,update);
                CancellationToken token;lock(gate){current?.Dispose();current=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);currentId=job.Id;token=current.Token;}
                try{
                    UpdateJob(item=>item.Status==DownloadStatus.Queued?item with{Status=DownloadStatus.Downloading,Error=""}:item);var claimed=Library.Find(job.Id);if(claimed?.Status!=DownloadStatus.Downloading || claimed.Attempt!=job.Attempt)continue;
                    Notify(job,null);await YoruApp.State.Cache.Initialize(token);
                    var servers=await YoruApp.State.Api.Servers(job.Anime.Id,job.Episode.Id,token);
                    var matching=servers.Where(server=>server.Name==job.ServerName && server.Quality==job.ServerQuality).ToArray();
                    var server=matching.FirstOrDefault(server=>server.Link==job.ServerLink)??(matching.Length==1?matching[0]:null);if(server is null)throw new DownloadFailure("DownloadServerGone");
                    var resolved=await YoruApp.State.Api.Resolve(server,token);
                    using var inspectTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);inspectTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                    var choices=await sources.Inspect(resolved,inspectTimeout.Token);var choice=choices.FirstOrDefault(choice=>choice.Id==job.ChoiceId);if(choice is null)throw new DownloadFailure("DownloadQualityGone");
                    var plan=await sources.Prepare(choice,resolved.Referer,inspectTimeout.Token);token.ThrowIfCancellationRequested();
                    Library.Clean(job.Id);var engine=new EpisodeDownloader(http,()=>{using var storage=new StatFs(FilesDir!.AbsolutePath);return storage.AvailableBytes;});var nextReport=DateTime.MinValue;
                    var result=await engine.Run(plan,Library.DirectoryFor(job.Id,true),Library.DirectoryFor(job.Id),progress=>{
                        if(DateTime.UtcNow<nextReport)return;nextReport=DateTime.UtcNow.AddSeconds(1);
                        UpdateJob(item=>item.Status==DownloadStatus.Downloading?item with{Bytes=progress.Bytes,Total=progress.Total,Percent=progress.Percent}:item);Notify(job,progress);
                    },token);
                    token.ThrowIfCancellationRequested();UpdateJob(item=>item.Status==DownloadStatus.Downloading?item with{Status=DownloadStatus.Completed,Bytes=result.Bytes,Total=result.Bytes,Percent=100,Error=""}:item);
                    if(Library.Find(job.Id)?.Status!=DownloadStatus.Completed)Library.Clean(job.Id);
                }catch(OperationCanceledException){
                    UpdateJob(item=>item.Active?item with{Status=token.IsCancellationRequested?DownloadStatus.Interrupted:DownloadStatus.Failed,Error=token.IsCancellationRequested?"DownloadInterrupted":"DownloadNetwork"}:item);Library.Clean(job.Id);
                }catch(Exception e){
                    var key=e is DownloadFailure failure?failure.Key:e is IOException?"DownloadStorage":e is PlaybackException?"DownloadResolveFailed":"DownloadNetwork";
                    UpdateJob(item=>item.Active?item with{Status=DownloadStatus.Failed,Error=key,Percent=null}:item);Library.Clean(job.Id);
                }finally{lock(gate){currentId=null;current?.Dispose();current=null;}writers.TryRemove(job.Id,out _);done.TrySetResult();}
            }
        }catch(Exception e){
            fatal=true;Library.InterruptActive(e is IOException or UnauthorizedAccessException?"DownloadStorage":"DownloadInterrupted");
        }finally{
            if(wake?.IsHeld==true)wake.Release();wake?.Dispose();wake=null;
            // Stop on the main looper, recheck the queue: a new UI request may have
            // arrived while the worker was finishing its last item.
            new Handler(Looper.MainLooper!).Post(()=>{if(IsDestroyedService)return;if(!fatal && Library.Items.Any(item=>item.Status==DownloadStatus.Queued) && !lifetime.IsCancellationRequested){worker=Task.Run(RunQueue);return;}running=false;stopping=true;StopForeground(StopForegroundFlags.Remove);StopSelf();});
        }
    }
    private bool IsDestroyedService=>lifetime.IsCancellationRequested;
    private Notification Notification(string text,DownloadProgress? progress,string? id){
        var open=new Intent(this,typeof(MainActivity)).AddFlags(ActivityFlags.ClearTop|ActivityFlags.SingleTop);open.PutExtra("page","Downloads");
        var pending=PendingIntent.GetActivity(this,9100,open,PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
        var builder=new Notification.Builder(this,Channel).SetSmallIcon(Resource.Drawable.ic_download_notice).SetContentTitle("YORU · "+L.T("Downloads")).SetContentText(text).SetColor(Ui.Accent).SetOnlyAlertOnce(true).SetOngoing(true).SetContentIntent(pending).SetProgress(100,progress?.Percent??0,progress?.Percent is null);
        if(id is not null){var cancel=new Intent(this,typeof(DownloadService));cancel.SetAction("yoru.cancel-download");cancel.PutExtra("id",id);builder.AddAction(new Notification.Action.Builder(null,L.T("MobileCancel"),PendingIntent.GetService(this,9102,cancel,PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable)).Build());}
        return builder.Build();
    }
    private void Notify(EpisodeDownload job,DownloadProgress? progress){if(!lifetime.IsCancellationRequested)((NotificationManager)GetSystemService(NotificationService)!).Notify(Notice,Notification(job.Anime.Name+" · "+job.Episode.Name+(progress?.Percent is { } percent?" · "+percent+"%":""),progress,job.Id));}
    public override void OnTimeout(int startId,ForegroundService fgsType){lifetime.Cancel();lock(gate)current?.Cancel();StopSelf();}
    public override void OnDestroy(){if(active==this)active=null;lifetime.Cancel();lock(gate)current?.Cancel();if(!stopping)try{Library.InterruptActive();}catch(IOException){}base.OnDestroy();}
}
