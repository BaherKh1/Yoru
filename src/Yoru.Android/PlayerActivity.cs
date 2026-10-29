using Orientation = Android.Widget.Orientation;
using OperationCanceledException = System.OperationCanceledException;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.UI;
using AnimeWitcherDesktop;
using Yoru.Core;
using MediaFailure=AndroidX.Media3.Common.PlaybackException;
namespace Yoru.AndroidApp;
[Activity(Label="YORU",Exported=false,ScreenOrientation=ScreenOrientation.SensorLandscape,ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.ScreenLayout|ConfigChanges.SmallestScreenSize)]
public sealed class PlayerActivity : LocalizedActivity
{
    private AppState State=>YoruApp.State;private LinearLayout chrome=null!;private PlayerView view=null!;private IExoPlayer? player;private TextView title=null!,status=null!;private Button serversButton=null!;private Handler handler=null!;private Java.Lang.Runnable tick=null!;
    private List<VideoServer> servers=new();private PlaybackRequest? request;private string? offlineId;private Dialog? menu;private CancellationTokenSource loading=new();private DateTime started=DateTime.UtcNow;private double position;private bool foreground,ended,resumePlayback=true,immersive=true;private string? selectedServer;private string lastSavedEpisode="";private double lastSavedSeconds=-1;
    private int loadRevision,playerRevision;private bool checking;private string failureEpisode="";private readonly HashSet<string> failedSources=new();private VideoServer? currentSource;
    private ProgressBar validationSpinner=null!;
    protected override void OnCreate(Bundle? savedInstanceState){
        base.OnCreate(savedInstanceState);request=State.Playback;offlineId=Intent?.GetStringExtra("download");if(offlineId is not null)request=YoruApp.Downloads.Find(offlineId) is { } offline?DownloadMenus.Offline(offline):null;position=savedInstanceState?.GetDouble("position")??0;selectedServer=savedInstanceState?.GetString("server")??request?.ServerLabel;
        var root=new FrameLayout(this);root.SetBackgroundColor(Android.Graphics.Color.Black);root.SetOnApplyWindowInsetsListener(new InsetsListener());SetContentView(root);
        view=new PlayerView(this){UseController=true,ControllerShowTimeoutMs=2500,KeepScreenOn=true};view.SetShowBuffering(PlayerView.ShowBufferingWhenPlaying);root.AddView(view,new FrameLayout.LayoutParams(-1,-1));
        chrome=Ui.Vertical(this);chrome.LayoutDirection=L.Language=="ar"?LayoutDirection.Rtl:LayoutDirection.Ltr;
        chrome.Background=new Android.Graphics.Drawables.GradientDrawable(Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,new[]{Android.Graphics.Color.ParseColor("#9E090C14").ToArgb(),Android.Graphics.Color.Transparent.ToArgb()});
        var top=new LinearLayout(this){Orientation=Orientation.Horizontal};top.SetGravity(GravityFlags.CenterVertical);top.SetPadding(D(12),D(8),D(12),D(8));
        var back=PlayerGlass.Icon(this,"Back",L.T("Back"),Finish);if(L.Language=="ar")back.ScaleX=-1;top.AddView(back,new LinearLayout.LayoutParams(D(48),D(48)){MarginEnd=D(10)});
        title=Ui.Label(this,"YORU",14);title.SetMaxLines(1);title.Ellipsize=Android.Text.TextUtils.TruncateAt.End;top.AddView(title,new LinearLayout.LayoutParams(0,D(46),1));
        serversButton=PlayerGlass.Action(this,"Server",L.T("MobileServers"),ChooseServer);serversButton.TextSize=11;top.AddView(serversButton,new LinearLayout.LayoutParams(D(150),D(48)){MarginStart=D(8),MarginEnd=D(8)});
        var more=PlayerGlass.Icon(this,"More",L.T("MobileMore"),PlaybackMenu);more.Rotation=90;top.AddView(more,new LinearLayout.LayoutParams(D(48),D(48)));chrome.AddView(top,new LinearLayout.LayoutParams(-1,D(64)));
        status=Ui.Label(this,L.T("MobileLoading"),12,Ui.Muted);status.SetPadding(D(12),0,D(12),D(6));var statusRow=new LinearLayout(this){Orientation=Orientation.Horizontal};statusRow.SetGravity(GravityFlags.CenterVertical);validationSpinner=new ProgressBar(this){IndeterminateTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Selected)};statusRow.AddView(validationSpinner,new LinearLayout.LayoutParams(D(18),D(18)){MarginStart=D(12)});statusRow.AddView(status,new LinearLayout.LayoutParams(0,-2,1));chrome.AddView(statusRow,new LinearLayout.LayoutParams(-1,-2));root.AddView(chrome,new FrameLayout.LayoutParams(-1,-2,GravityFlags.Top));
        view.ControllerVisibility+=(_,e)=>chrome.Visibility=status.Visibility==ViewStates.Visible?ViewStates.Visible:(ViewStates)e.P0;
        handler=new Handler(Looper.MainLooper!);tick=new Java.Lang.Runnable(()=>{if(foreground && player is not null){SaveProgress();handler.PostDelayed(tick,5000);}});
        OnBackPressedDispatcher.AddCallback(this,new BackAction(Finish));ApplyBars();
    }
    private int D(float value)=>Ui.Dp(this,value);
    protected override void OnStart(){base.OnStart();foreground=true;_=RestoreAndPlay();}
    protected override void OnResume(){base.OnResume();var wasForeground=foreground;foreground=true;if(!wasForeground){if(player is not null){player.PlayWhenReady=resumePlayback;handler.RemoveCallbacksAndMessages(null);handler.PostDelayed(tick,5000);}else _=RestoreAndPlay();}}
    protected override void OnPause(){foreground=false;loading.Cancel();if(player is not null){resumePlayback=player.PlayWhenReady;SaveProgress();player.Pause();}base.OnPause();}
    protected override void OnStop(){foreground=false;menu?.Dismiss();menu?.Dispose();menu=null;loading.Cancel();handler.RemoveCallbacksAndMessages(null);SaveProgress();ReleasePlayer();base.OnStop();}
    protected override void OnDestroy(){loading.Cancel();loading.Dispose();tick?.Dispose();handler?.Dispose();ReleasePlayer();base.OnDestroy();}
    protected override void OnSaveInstanceState(Bundle state){state.PutDouble("position",player is null?position:Math.Max(0,player.CurrentPosition/1000d));state.PutString("server",selectedServer);base.OnSaveInstanceState(state);}
    public override void OnConfigurationChanged(Configuration configuration){base.OnConfigurationChanged(configuration);ApplyBars();}
    private void Rotate(){immersive=!immersive;RequestedOrientation=immersive?ScreenOrientation.SensorLandscape:ScreenOrientation.SensorPortrait;ApplyBars();}
    private void ApplyBars(){
        AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window!,false);
        var controller=AndroidX.Core.View.WindowCompat.GetInsetsController(Window!,Window!.DecorView)!;
        if(immersive){controller.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());controller.SystemBarsBehavior=AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;}
        else controller.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
    }
    private async Task RestoreAndPlay(){
        try {
            if(offlineId is not null && request is null)throw new DownloadFailure("DownloadMissing");
            if(request is null){await State.Cache.Initialize();var anime=await State.Cache.Details(Intent?.GetStringExtra("anime")??"");var episodes=await State.Cache.Episodes(anime.Id);var episode=episodes.FirstOrDefault(item=>item.Id==Intent?.GetStringExtra("episode"))??episodes.First();request=new(anime,episode,episodes);}
            if(position<=0)position=State.Store.Position(request.Anime.Id,request.Episode.Id);
            if(foreground)await LoadSource(selectedServer,position);
        }catch(Exception e){if(!IsDestroyed)ShowError(e is DownloadFailure failure?L.T(failure.Key):L.Error(e));}
    }
    private async Task LoadSource(string? label,double resume,PlaybackRequest? target=null,VideoServer? preferred=null,bool refresh=false,bool manualServer=false){
        loading.Cancel();loading.Dispose();loading=new();var token=loading.Token;var revision=++loadRevision;var candidate=target??request;
        checking=true;validationSpinner.Visibility=ViewStates.Visible;chrome.Visibility=ViewStates.Visible;status.Text=L.T("CheckingServers");status.Visibility=ViewStates.Visible;serversButton.Enabled=false;
        try {
            if(candidate is null){if(offlineId is not null)throw new DownloadFailure("DownloadMissing");return;}var current=candidate;VideoServer playable;ValidatedServer? selection=null;
            if(current.OfflinePath is not null){
                var download=YoruApp.Downloads.Find(current.Anime.Id,current.Episode.Id);var local=download is null?null:YoruApp.Downloads.LocalPath(download);if(local is null)throw new DownloadFailure("DownloadMissing");
                var result=YoruApp.Downloads.TryResult(download!.Id)!;playable=new VideoServer("Offline","",Android.Net.Uri.FromFile(new Java.IO.File(local))!.ToString()??throw new DownloadFailure("DownloadMissing"),true,false,MimeType:result.MimeType);selectedServer=L.T("DownloadOffline");serversButton.Visibility=ViewStates.Gone;
            }else{
                if(!ServerUi.Connected(this))throw new ServersUnavailable(true);
                var scope=current.Anime.Id+"\0"+current.Episode.Id;var excluded=scope==failureEpisode?failedSources:new HashSet<string>();
                if(!refresh && current.Validated is {Fresh:true} ready && excluded.Count==0 && (label is null || label==ready.Source.Label))selection=ready;
                else{
                    var available=await State.Servers.Discover(current.Anime.Id,current.Episode.Id,token,refresh);
                    var wanted=preferred??available.FirstOrDefault(server=>server.Label==label);
                    selection=await State.Servers.Select(current.Anime.Id,current.Episode.Id,false,token,wanted,available,exclude:excluded,exclusivePreferred:manualServer);
                }
                playable=selection.Media;current=current with{ServerLabel=selection.Source.Label,Validated=selection};
            }
            token.ThrowIfCancellationRequested();if(!foreground || IsDestroyed || revision!=loadRevision)return;
            var changed=request?.Anime.Id!=current.Anime.Id || request?.Episode.Id!=current.Episode.Id;
            if(player is not null && !changed){resume=Math.Max(0,player.CurrentPosition/1000d);resumePlayback=player.PlayWhenReady;}
            SaveProgress();
            ReleasePlayer();
            request=current;State.Playback=current;Intent?.PutExtra("episode",current.Episode.Id);
            if(current.OfflinePath is not null){offlineId=DownloadLibrary.Key(current.Anime.Id,current.Episode.Id);Intent?.PutExtra("download",offlineId);}
            if(changed){started=DateTime.UtcNow;ended=false;lastSavedEpisode="";lastSavedSeconds=-1;resumePlayback=true;failedSources.Clear();}
            failureEpisode=current.Anime.Id+"\0"+current.Episode.Id;
            if(selection is not null){servers=selection.Servers.ToList();currentSource=selection.Source;selectedServer=selection.Source.Label;serversButton.Visibility=ViewStates.Visible;}
            var http=new DefaultHttpDataSource.Factory();http.SetUserAgent(PlaybackResolver.UserAgent);http.SetConnectTimeoutMs(15000);http.SetReadTimeoutMs(25000);http.SetAllowCrossProtocolRedirects(true);
            if(Data.WebUrl(playable.Referer))http.SetDefaultRequestProperties(new Dictionary<string,string>{{"Referer",playable.Referer}});
            var data=new DefaultDataSource.Factory(this,http);var source=new DefaultMediaSourceFactory(data);
            var controlBuilder=new DefaultLoadControl.Builder();controlBuilder.SetBufferDurationsMsForStreaming(15000,45000,1500,3000);var control=controlBuilder.Build()!;
            var audioBuilder=new AudioAttributes.Builder();audioBuilder.SetUsage(1);audioBuilder.SetContentType(3);var audio=audioBuilder.Build()!;
            var builder=new ExoPlayerBuilder(this);builder.SetMediaSourceFactory(source);builder.SetLoadControl(control);builder.SetAudioAttributes(audio,true);builder.SetHandleAudioBecomingNoisy(true);
            var native=builder.Build()??throw new InvalidOperationException("PlayerStartFailed");player=native;
            native.AddListener(new PlaybackListener(this,playerRevision));view.Player=native;ended=false;position=Math.Max(0,resume);
            var mediaBuilder=new MediaItem.Builder();mediaBuilder.SetUri(playable.Link);if(playable.MimeType.Length>0)mediaBuilder.SetMimeType(playable.MimeType);var item=mediaBuilder.Build()!;
            native.SetMediaItem(item,(long)(position*1000));native.Prepare();native.PlayWhenReady=resumePlayback;
            title.Text=current.Anime.Name+" \u00B7 "+current.Episode.Name;serversButton.Text=selectedServer;status.Text="";status.Visibility=ViewStates.Gone;
            handler.RemoveCallbacksAndMessages(null);handler.PostDelayed(tick,5000);
        }catch(OperationCanceledException){}catch(Exception e){
            if(token.IsCancellationRequested || revision!=loadRevision || !foreground || IsDestroyed)return;
            if(candidate?.OfflinePath is not null)ShowError(e is DownloadFailure failure?L.T(failure.Key):L.Error(e));
            else if(candidate is not null && !ServerUi.Connected(this)){status.Text=L.T("OfflinePlaybackUnavailable");ShowError(L.T("OfflinePlaybackUnavailable"));}
            else if(candidate is not null){status.Text=ServerUi.Error(this,e);menu?.Dismiss();menu=ServerUi.Unavailable(this,()=>{State.Servers.Invalidate(candidate.Anime.Id,candidate.Episode.Id);failedSources.Clear();_=LoadSource(label,resume,candidate,preferred,true);},()=>{status.Visibility=ViewStates.Gone;if(player is null || player.PlaybackState==1)Finish();},ServerValidation.IsNetwork(e));}
        }finally{if(revision==loadRevision){checking=false;if(!IsDestroyed){serversButton.Enabled=true;serversButton.Visibility=request?.OfflinePath is null && ServerUi.Connected(this)?ViewStates.Visible:ViewStates.Gone;validationSpinner.Visibility=ViewStates.Gone;}}}
    }
    private void PlaybackMenu(){
        menu?.Dismiss();menu?.Dispose();
        menu=PlayerGlass.Show(this,title.Text??"YORU",(rows,close)=>{
            var index=request?.Episodes.ToList().FindIndex(item=>item.Id==request.Episode.Id)??-1;
            void EpisodeRow(int delta,string label,string icon){var target=EpisodeNavigation.Target(index,request?.Episodes.Count??0,delta);var detail=target is { } i?L.T("MobileEpisodeNumber",i+1)+" · "+request!.Episodes[i].Name:"";
                PlayerGlass.Row(this,rows,icon,L.T(label),detail,()=>{close();ChangeEpisode(delta);},enabled:target is not null);}
            EpisodeRow(-1,"MobilePrevious","Previous");EpisodeRow(1,"MobileNext","Next");
            PlayerGlass.Row(this,rows,"Fullscreen",L.T("MobileFullscreen"),"",()=>{close();Rotate();});
        });
    }
    private void ChooseServer(){
        if(request is null)return;
        if(servers.Count==0){if(!ServerUi.Connected(this)){ShowError(L.T("OfflinePlaybackUnavailable"));return;}_=LoadSource(selectedServer,position);return;}
        menu?.Dismiss();menu?.Dispose();menu=PlayerGlass.Show(this,L.T("MobileServers"),(rows,close)=>ServerUi.Groups(this,rows,servers,currentSource,server=>{
            close();if(currentSource is not null && ServerGroups.Key(server)==ServerGroups.Key(currentSource))return;
            var resume=player is null?position:Math.Max(0,player.CurrentPosition/1000d);resumePlayback=player?.PlayWhenReady??resumePlayback;_=LoadSource(server.Label,resume,preferred:server,manualServer:true);
        }));
    }
    private void ChangeEpisode(int delta){
        if(request is null)return;var target=EpisodeNavigation.Target(request.Episodes.ToList().FindIndex(item=>item.Id==request.Episode.Id),request.Episodes.Count,delta);if(target is not { } index)return;
        var next=request.Episodes[index];string? offlinePath=null;
        if(request.OfflinePath is not null){var download=YoruApp.Downloads.Find(request.Anime.Id,next.Id);offlinePath=download is null?null:YoruApp.Downloads.LocalPath(download);if(offlinePath is null){ShowError(L.T("DownloadMissing"));return;}}
        var candidate=request with{Episode=next,ServerLabel=null,OfflinePath=offlinePath,Validated=null};_=LoadSource(null,State.Store.Position(request.Anime.Id,next.Id),candidate);
    }
    private void SaveProgress(){
        if(request is null || player is null)return;position=ended?0:Math.Max(0,player.CurrentPosition/1000d);var duration=Math.Max(0,player.Duration/1000d);if(lastSavedEpisode==request.Episode.Id && Math.Abs(lastSavedSeconds-position)<.5)return;lastSavedEpisode=request.Episode.Id;lastSavedSeconds=position;_=State.Store.SaveProgress(request.Anime,request.Episode,position,duration,started);
    }
    private void ReleasePlayer(){playerRevision++;if(player is null)return;view.Player=null;player.Release();player.Dispose();player=null;}
    private void ShowError(string text){if(IsDestroyed || !foreground)return;chrome.Visibility=ViewStates.Visible;status.Text=text;status.Visibility=ViewStates.Visible;var dialog=Ui.Dialog(this,"YORU");dialog.SetMessage(text);dialog.SetNegativeButton(L.T("MobileCancel"),(_,_)=>{});if(offlineId is not null)dialog.SetPositiveButton(L.T("DownloadRetry"),(_,_)=>_=LoadSource(null,position));else if(ServerUi.Connected(this))dialog.SetPositiveButton(L.T("MobileServers"),(_,_)=>ChooseServer());dialog.Show();}
    private sealed class PlaybackListener(PlayerActivity owner,int revision):Java.Lang.Object,IPlayerListener {
        public void OnPlayerError(MediaFailure? error){if(revision!=owner.playerRevision || !owner.foreground)return;if(owner.checking){owner.view.Post(()=>{if(!owner.checking)OnPlayerError(null);});return;}if(owner.request?.OfflinePath is not null){owner.ShowError(L.T("MobilePlaybackFailed"));return;}
            if(owner.request is not { } current || owner.currentSource is null)return;owner.failedSources.Add(ServerGroups.Key(owner.currentSource));owner.State.Servers.Invalidate(current.Anime.Id,current.Episode.Id);_=owner.LoadSource(null,owner.position,refresh:true);
        }
        public void OnPlaybackStateChanged(int state){if(revision!=owner.playerRevision)return;if(state==4){owner.ended=true;owner.SaveProgress();}else if(state==3)owner.ended=false;}
    }
}
