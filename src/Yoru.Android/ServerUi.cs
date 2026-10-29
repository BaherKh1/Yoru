using Android.App;
using Android.Net;
using Android.Views;
using Android.Widget;
using AnimeWitcherDesktop;
using Yoru.Core;
namespace Yoru.AndroidApp;

internal static class ServerUi
{
    // Real internet access, not merely an enabled Wi-Fi/mobile interface. The
    // monitor's decided state is authoritative so dialogs cannot contradict the
    // offline navigation; otherwise fall back to platform capabilities.
    public static bool Connected(Android.Content.Context context){
        var manager=(ConnectivityManager?)context.GetSystemService(Android.Content.Context.ConnectivityService);
        var network=manager?.ActiveNetwork;if(network is null)return false;
        var capabilities=manager?.GetNetworkCapabilities(network);
        if(capabilities?.HasCapability(NetCapability.Internet)!=true)return false;
        if(ConnectivityMonitor.Decided)return ConnectivityMonitor.Online;
        // Some networks block the platform connectivity check while traffic works;
        // trust the monitor's own probe for those instead of refusing to stream.
        return capabilities.HasCapability(NetCapability.Validated) || ConnectivityMonitor.LastProbe==true;
    }
    public static string Error(Android.Content.Context context,Exception error)=>L.T(!Connected(context) || ServerValidation.IsNetwork(error)?"DownloadNetwork":"ServerUnavailable");
    public static void Groups(Activity owner,LinearLayout rows,IReadOnlyList<VideoServer> servers,VideoServer? selected,Action<VideoServer> choose){
        foreach(var group in ServerGroups.Group(servers)){
            var active=selected is not null && StringComparer.OrdinalIgnoreCase.Equals(ServerGroups.Quality(selected.Quality),group.Quality);
            var heading=Ui.Label(owner,group.Quality.Length>0?group.Quality:L.T("ServerSourceQuality"),12,active?Ui.Selected:Ui.Muted);heading.SetTypeface(null,Android.Graphics.TypefaceStyle.Bold);
            rows.AddView(heading,new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,12),BottomMargin=Ui.Dp(owner,4),MarginStart=Ui.Dp(owner,4)});
            foreach(var server in group.Servers){var candidate=server;PlayerGlass.Row(owner,rows,"Server",candidate.Name,"",()=>choose(candidate),selected is not null && ServerGroups.Key(candidate)==ServerGroups.Key(selected));}
        }
    }
    public static Dialog Checking(Activity owner){return PlayerGlass.Show(owner,L.T("CheckingServers"),(rows,_)=>{
        var line=new LinearLayout(owner);line.SetGravity(GravityFlags.CenterVertical);line.AddView(new ProgressBar(owner){IndeterminateTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Selected)},new LinearLayout.LayoutParams(Ui.Dp(owner,24),Ui.Dp(owner,24)){MarginEnd=Ui.Dp(owner,12)});line.AddView(Ui.Label(owner,L.T("CheckingServerMessage"),13,Ui.Muted),new LinearLayout.LayoutParams(0,-2,1));rows.AddView(line);
    });}
    public static Dialog Unavailable(Activity owner,Action retry,Action cancel,bool network=false){
        var retried=false;
        var dialog=PlayerGlass.Show(owner,L.T(network?"NetworkError":"NoServersTitle"),(rows,close)=>{
            rows.AddView(Ui.Label(owner,L.T(network?"DownloadNetwork":"NoServersMessage"),14));
            var again=Ui.Button(owner,L.T("DownloadRetry"),()=>{retried=true;close();retry();},true);rows.AddView(again,new LinearLayout.LayoutParams(-1,Ui.Dp(owner,48)){TopMargin=Ui.Dp(owner,14)});
        });dialog.DismissEvent+=(_,_)=>{if(!retried)cancel();};return dialog;
    }
}
