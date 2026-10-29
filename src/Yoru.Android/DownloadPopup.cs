using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Yoru;
using Yoru.Core;
using DownloadStatus=Yoru.Core.DownloadStatus;
using OperationCanceledException=System.OperationCanceledException;

namespace Yoru.AndroidApp;

internal static class DownloadText
{
    public static string Quality(DownloadChoice choice)=>choice.Label.Length>0?choice.Label:L.T("DownloadSourceQuality");
    public static string Size(long? bytes,bool estimated=false)=>bytes is >0?(estimated?"≈ ":"")+(bytes.Value/1048576d).ToString("0.#",System.Globalization.CultureInfo.CurrentCulture)+" MB":"";
    public static string Status(EpisodeDownload job)=>L.T("DownloadStatus"+job.Status)+(job.Percent is { } percent && job.Status==DownloadStatus.Downloading?" · "+percent+"%":"")+(job.Bytes>0?" · "+Size(job.Bytes):"")+(job.Error.Length>0?"\n"+L.T(job.Error):"");
    public static string Info(EpisodeDownload job)=>job.ServerName+" · "+(job.Quality.Length>0?job.Quality:L.T("DownloadSourceQuality"))+"\n"+Status(job);
    public static string Error(Exception e)=>L.T(e is DownloadFailure failure?failure.Key:e is IOException?"DownloadStorage":"DownloadNetwork");
}

internal sealed class DownloadPopup
{
    private readonly MainActivity owner;private readonly Anime anime;private readonly Episode episode;private readonly int order;private readonly EpisodeDownload? previous;
    private readonly CancellationTokenSource lifetime=new();private CancellationTokenSource? lookup;private ValidatedServer? validated;
    private Dialog? dialog,serverDialog;private LinearLayout body=null!;private IReadOnlyList<VideoServer> servers=Array.Empty<VideoServer>();private IReadOnlyList<DownloadChoice> choices=Array.Empty<DownloadChoice>();
    private VideoServer? server;private VideoServer? resolved;private DownloadChoice? choice;private DownloadPlan? plan;private bool busy=true,starting,closed;private string error="";private int revision;
    public DownloadPopup(MainActivity owner,Anime anime,Episode episode,int order){this.owner=owner;this.anime=anime;this.episode=episode;this.order=order;previous=YoruApp.Downloads.Find(anime.Id,episode.Id);}
    public void Show(){
        dialog=PlayerGlass.Show(owner,L.T("DownloadEpisode"),(rows,_)=>{body=rows;Render();});
        dialog.DismissEvent+=(_,_)=>{if(closed)return;closed=true;lifetime.Cancel();lookup?.Cancel();serverDialog?.Dismiss();};
        owner.TrackDownloadDialog(dialog);_=Discover();
    }
    private bool Current(int version)=>!closed && !owner.IsDestroyed && revision==version;
    private async Task Discover(){
        if(starting)return;lookup?.Cancel();lookup?.Dispose();lookup=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var ct=lookup.Token;var version=++revision;busy=true;error="";plan=null;choice=null;server=null;validated=null;choices=Array.Empty<DownloadChoice>();Render();
        try{
            if(!ServerUi.Connected(owner))throw new ServersUnavailable(true);
            var available=await YoruApp.State.Servers.Discover(anime.Id,episode.Id,ct);if(!Current(version))return;servers=available;Render();
            var same=previous is null?Array.Empty<VideoServer>():servers.Where(value=>value.Name==previous.ServerName && value.Quality==previous.ServerQuality).ToArray();
            var preferred=same.FirstOrDefault(value=>value.Link==previous?.ServerLink)??(same.Length==1?same[0]:null);
            var ready=await YoruApp.State.Servers.Select(anime.Id,episode.Id,true,ct,preferred,servers,previous?.ChoiceId);if(!Current(version))return;Apply(ready);
        }catch(OperationCanceledException){}catch(Exception e){if(Current(version)){busy=false;error=ServerUi.Error(owner,e);Render();}}
    }
    private void Apply(ValidatedServer ready){validated=ready;server=ready.Source;resolved=ready.Media;choices=ready.Choices??Array.Empty<DownloadChoice>();plan=ready.Plan;choice=plan?.Choice;busy=false;error="";Render();}
    private async Task SelectServer(VideoServer selected){
        if(starting)return;
        if(server==selected && plan is not null && error.Length==0)return;
        lookup?.Cancel();lookup?.Dispose();lookup=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var ct=lookup.Token;var version=++revision;
        server=selected;choice=null;plan=null;resolved=null;validated=null;choices=Array.Empty<DownloadChoice>();busy=true;error="";Render();
        try{
            if(!ServerUi.Connected(owner))throw new ServersUnavailable(true);
            var ready=await YoruApp.State.Servers.Check(anime.Id,episode.Id,selected,servers,true,ct,previous?.ChoiceId);if(!Current(version))return;Apply(ready);
        }catch(OperationCanceledException){}catch(Exception e){if(Current(version)){busy=false;error=ServerUi.Error(owner,e);Render();}}
    }
    private async Task SelectQuality(DownloadChoice selected){
        var ready=validated;if(starting || ready is null || server is null || ServerGroups.Key(ready.Source)!=ServerGroups.Key(server) || !choices.Contains(selected))return;
        if(choice==selected && plan is not null && error.Length==0)return;
        lookup?.Cancel();lookup?.Dispose();lookup=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);lookup.CancelAfter(TimeSpan.FromSeconds(45));var ct=lookup.Token;var version=++revision;
        choice=selected;plan=null;busy=true;error="";Render();
        try{var prepared=await YoruApp.State.Servers.Quality(ready,selected,ct);if(!Current(version))return;plan=prepared;busy=false;Render();}
        catch(OperationCanceledException){}catch(Exception e){if(Current(version)){busy=false;error=ServerUi.Error(owner,e);Render();}}
    }
    private void ServerPicker(){
        if(starting || servers.Count==0)return;serverDialog?.Dismiss();
        serverDialog=PlayerGlass.Show(owner,L.T("MobileServers"),(rows,close)=>ServerUi.Groups(owner,rows,servers,server,candidate=>{close();_=SelectServer(candidate);}));
    }
    private void Render(){
        if(closed || owner.IsDestroyed)return;body.RemoveAllViews();
        var episodeLabel=Ui.Label(owner,anime.Name+" · "+episode.Name,12,Ui.Muted);episodeLabel.SetMaxLines(3);body.AddView(episodeLabel);
        body.AddView(Ui.Label(owner,L.T("MobileServers"),13),new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,14)});
        var select=PlayerGlass.Action(owner,"Server",server?.Label??L.T(busy?"CheckingServers":"MobileServers"),ServerPicker,server is not null);select.Enabled=servers.Count>0 && !starting;body.AddView(select,new LinearLayout.LayoutParams(-1,Ui.Dp(owner,48)){TopMargin=Ui.Dp(owner,6)});
        body.AddView(Ui.Label(owner,L.T("DownloadQuality"),13),new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,14)});
        foreach(var option in choices){var selected=option;var size=option==choice && plan is not null?DownloadText.Size(plan.Bytes,plan.Estimated):DownloadText.Size(option.Bytes);
            var detail=string.Join(" · ",new[]{option.Bitrate>0?(option.Bitrate/1_000_000d).ToString("0.##",System.Globalization.CultureInfo.CurrentCulture)+" Mb/s":"",size}.Where(value=>value.Length>0));
            PlayerGlass.Row(owner,body,"Play",DownloadText.Quality(option),detail,()=>{_=SelectQuality(selected);},option==choice,enabled:!starting);
        }
        if(busy){var loading=new LinearLayout(owner);loading.SetGravity(GravityFlags.CenterVertical);loading.AddView(new ProgressBar(owner){IndeterminateTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Selected)},new LinearLayout.LayoutParams(Ui.Dp(owner,20),Ui.Dp(owner,20)){MarginEnd=Ui.Dp(owner,10)});loading.AddView(Ui.Label(owner,L.T("MobileLoading"),12,Ui.Muted));body.AddView(loading,new LinearLayout.LayoutParams(-1,Ui.Dp(owner,42)));}
        if(error.Length>0){var message=Ui.Label(owner,error,12,Color.ParseColor("#ECA6B9"));body.AddView(message,new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,10)});
            var retry=PlayerGlass.Action(owner,"Download",L.T("DownloadRetry"),()=>{YoruApp.State.Servers.Invalidate(anime.Id,episode.Id);_=Discover();});retry.Enabled=!starting;body.AddView(retry);}
        var download=Ui.Button(owner,L.T("DownloadAction"),()=>{_=Start();},true);StreamActions.ButtonIcon(download,"Download");download.Enabled=!busy && !starting && plan is not null;download.Alpha=download.Enabled?1:.4f;
        body.AddView(download,new LinearLayout.LayoutParams(-1,Ui.Dp(owner,50)){TopMargin=Ui.Dp(owner,14)});
    }
    private async Task Start(){
        if(starting || busy || choice is null || server is null || plan is null)return;var chosenServer=server;var chosenQuality=choice;starting=true;Render();EpisodeDownload? job=null;
        try{
            if(OperatingSystem.IsAndroidVersionAtLeast(33) && owner.CheckSelfPermission(Android.Manifest.Permission.PostNotifications)!=Permission.Granted){var preferences=owner.GetSharedPreferences("download-notices",FileCreationMode.Private)!;if(!preferences.GetBoolean("asked",false)){preferences.Edit()!.PutBoolean("asked",true)!.Apply();owner.RequestPermissions(new[]{Android.Manifest.Permission.PostNotifications},773);}}
            await DownloadService.WaitForWriter(DownloadLibrary.Key(anime.Id,episode.Id));
            if(closed || owner.IsDestroyed)return;
            job=await YoruApp.Downloads.Enqueue(anime,episode,order,chosenServer,chosenQuality);
            if(owner.IsDestroyed)throw new DownloadFailure("DownloadInterrupted");DownloadService.Start(owner);dialog?.Dismiss();Toast.MakeText(owner,L.T("DownloadQueued"),ToastLength.Short)?.Show();
        }catch(Exception e){if(job is not null)await Task.Run(()=>YoruApp.Downloads.Update(job.Id,item=>item with{Status=DownloadStatus.Failed,Error="DownloadStartFailed"}));if(!closed){starting=false;error=DownloadText.Error(e);Render();}}
    }
}

internal static class DownloadMenus
{
    public static Dialog Show(MainActivity owner,EpisodeDownload item){
        return PlayerGlass.Show(owner,item.Episode.Name,(rows,close)=>{
            rows.AddView(Ui.Label(owner,item.Anime.Name+"\n"+DownloadText.Info(item),12,Ui.Muted));
            if(item.Status==DownloadStatus.Completed)PlayerGlass.Row(owner,rows,"Play",L.T("DownloadPlayOffline"),"",()=>{close();owner.PlayDownloaded(item);});
            if(item.Active)PlayerGlass.Row(owner,rows,"Remove",L.T("DownloadCancel"),"",()=>{close();_=Cancel(owner,item);});
            else{
                if(item.Status!=DownloadStatus.Completed)PlayerGlass.Row(owner,rows,"Download",L.T("DownloadRetry"),"",()=>{close();owner.OpenDownload(item.Anime,item.Episode,item.EpisodeOrder);});
                PlayerGlass.Row(owner,rows,"Remove",L.T("DownloadDelete"),"",()=>{close();ConfirmDelete(owner,item);});
            }
        });
    }
    private static async Task Cancel(MainActivity owner,EpisodeDownload item){try{await DownloadService.Cancel(item.Id);}catch(Exception e){Toast.MakeText(owner,DownloadText.Error(e),ToastLength.Long)?.Show();}}
    private static void ConfirmDelete(MainActivity owner,EpisodeDownload item){
        var dialog=PlayerGlass.Show(owner,L.T("DownloadDelete"),(rows,close)=>{
            rows.AddView(Ui.Label(owner,L.T("DownloadDeleteConfirm"),14));var button=Ui.Button(owner,L.T("DownloadDelete"),()=>{close();_=Delete(owner,item);},true);rows.AddView(button,new LinearLayout.LayoutParams(-1,Ui.Dp(owner,48)){TopMargin=Ui.Dp(owner,14)});
        });owner.TrackDownloadDialog(dialog);
    }
    private static async Task Delete(MainActivity owner,EpisodeDownload item){try{await DownloadService.Delete(item.Id);}catch(Exception e){Toast.MakeText(owner,DownloadText.Error(e),ToastLength.Long)?.Show();}}
    public static PlaybackRequest? Offline(EpisodeDownload item){
        var path=YoruApp.Downloads.LocalPath(item);if(path is null)return null;
        var episodes=YoruApp.Downloads.Items.Where(value=>value.Anime.Id==item.Anime.Id && value.Status==DownloadStatus.Completed).OrderBy(value=>value.EpisodeOrder).ThenBy(value=>value.Episode.Id,StringComparer.Ordinal).Select(value=>value.Episode).ToArray();
        return new(item.Anime,item.Episode,episodes,OfflinePath:path);
    }
}
