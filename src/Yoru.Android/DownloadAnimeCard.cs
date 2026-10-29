using Android.Content.Res;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AnimeWitcherDesktop;
using Yoru.Core;
using Orientation=Android.Widget.Orientation;
namespace Yoru.AndroidApp;

internal sealed class DownloadAnimeCard:LinearLayout
{
    private readonly MainActivity owner;private readonly ImageView poster;private readonly TextView title,meta,state,percent;private readonly ImageView chevron;private readonly LinearLayout progressLine;private readonly ProgressBar bar;
    private string id="";private CancellationTokenSource? artwork;private int lastProgress=-1;
    private int D(float value)=>Ui.Dp(owner,value);
    public DownloadAnimeCard(MainActivity owner):base(owner){
        this.owner=owner;Orientation=Orientation.Vertical;SetPadding(D(12),D(12),D(12),D(12));
        Background=Ui.OutlinedShape(Color.ParseColor("#C21A1B2B"),D(18),Color.ParseColor("#398B5CF6"));
        var line=new LinearLayout(owner){Orientation=Orientation.Horizontal,LayoutDirection=LayoutDirection.Ltr};line.SetGravity(GravityFlags.CenterVertical);
        var art=new FrameLayout(owner){Background=Ui.Shape(Ui.Background,D(10)),ClipToOutline=true};poster=new ImageView(owner);poster.SetScaleType(ImageView.ScaleType.CenterCrop);art.AddView(poster,new FrameLayout.LayoutParams(-1,-1));line.AddView(art,new LinearLayout.LayoutParams(D(72),D(108)));
        var text=Ui.Vertical(owner);text.LayoutDirection=L.Language=="ar"?LayoutDirection.Rtl:LayoutDirection.Ltr;text.SetPadding(D(14),0,D(6),0);
        title=Ui.Label(owner,"",15);title.SetTypeface(null,TypefaceStyle.Bold);title.SetMaxLines(2);title.Ellipsize=Android.Text.TextUtils.TruncateAt.End;text.AddView(title);
        meta=Ui.Label(owner,"",11,Ui.Muted);meta.SetMaxLines(3);text.AddView(meta,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(7)});
        state=Ui.Label(owner,"",11,Ui.Selected);state.SetMaxLines(3);text.AddView(state,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(5)});line.AddView(text,new LinearLayout.LayoutParams(0,-2,1));
        chevron=StreamActions.Icon(owner,"Chevron",Ui.Muted,20);line.AddView(chevron,new LinearLayout.LayoutParams(D(20),D(24)));AddView(line);
        progressLine=Ui.Vertical(owner);bar=new ProgressBar(owner,null,Android.Resource.Attribute.ProgressBarStyleHorizontal){Max=1000,ProgressTintList=ColorStateList.ValueOf(Ui.Accent),IndeterminateTintList=ColorStateList.ValueOf(Ui.Accent),ProgressBackgroundTintList=ColorStateList.ValueOf(Color.ParseColor("#39304E"))};
        progressLine.AddView(bar,new LinearLayout.LayoutParams(-1,D(3)));percent=Ui.Label(owner,"",11,Ui.Selected);progressLine.AddView(percent,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(6)});AddView(progressLine,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(12)});
    }
    public void Bind(AnimeDownloads group,bool expanded){
        title.Text=group.Anime.Name;meta.Text=L.T("DownloadCompletedCount",group.Completed)+"\n"+(group.Bytes>0?DownloadText.Size(group.Bytes):"0 MB");
        var statuses=new List<string>();if(group.Active>0)statuses.Add(L.T("DownloadActiveCount",group.Active));if(group.Failed>0)statuses.Add(L.T("DownloadFailedCount",group.Failed));if(group.Interrupted>0)statuses.Add(L.T("DownloadInterruptedCount",group.Interrupted));if(group.Cancelled>0)statuses.Add(L.T("DownloadCancelledCount",group.Cancelled));
        state.Text=string.Join(" · ",statuses);state.Visibility=statuses.Count>0?ViewStates.Visible:ViewStates.Gone;chevron.Rotation=expanded?270:90;
        progressLine.Visibility=group.Active>0?ViewStates.Visible:ViewStates.Gone;bar.Indeterminate=group.Percent is null;
        if(group.Percent is { } value){var next=value*10;if(next!=lastProgress){if(lastProgress<0 || next<lastProgress || !Android.Animation.ValueAnimator.AreAnimatorsEnabled())bar.Progress=next;else bar.SetProgress(next,true);}lastProgress=next;percent.Text=value+"%";}
        else{lastProgress=-1;percent.Text=L.T("DownloadStatusDownloading");}
        if(id!=group.Anime.Id){artwork?.Cancel();artwork?.Dispose();id=group.Anime.Id;poster.SetImageDrawable(null);artwork=new();_=YoruApp.Images.Load(poster,AnimeMetadata.Art(group.Anime,false),D(72),D(108),artwork.Token);}
    }
    public void Release(){artwork?.Cancel();artwork?.Dispose();artwork=null;id="";lastProgress=-1;}
}
