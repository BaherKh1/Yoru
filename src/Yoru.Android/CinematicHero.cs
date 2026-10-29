using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Yoru;
using Yoru.Core;

namespace Yoru.AndroidApp;

internal sealed class CinematicHero : FrameLayout
{
    private readonly MainActivity owner;
    private readonly ImageView[] artwork;
    private readonly LinearLayout branding,copy;
    private readonly TextView title,genres,eyebrow;
    private readonly Button watch,save;
    private FeedRow? row;
    private CancellationTokenSource? loading;
    private int visible;
    private bool hasArtwork,swiping;
    private float downX,downY;
    private string artKey="",requestedKey="";
    public CinematicHero(MainActivity owner):base(owner) {
        this.owner=owner;SetBackgroundColor(Ui.Panel);Clickable=true;Focusable=true;
        artwork=new[]{new ImageView(owner),new ImageView(owner)};
        foreach(var image in artwork){image.SetScaleType(ImageView.ScaleType.CenterCrop);AddView(image,new FrameLayout.LayoutParams(-1,-1));}
        AddView(new View(owner){Background=new GradientDrawable(GradientDrawable.Orientation.TopBottom,new[]{Color.ParseColor("#66090C14").ToArgb(),Color.Transparent.ToArgb(),Color.ParseColor("#65090C14").ToArgb(),Ui.Background.ToArgb()})},new FrameLayout.LayoutParams(-1,-1));
        branding=new LinearLayout(owner){Orientation=Orientation.Horizontal,LayoutDirection=LayoutDirection.Ltr};branding.SetGravity(GravityFlags.CenterVertical);branding.SetPadding(D(20),0,D(16),0);
        var mark=new ImageView(owner);mark.SetImageResource(Resource.Drawable.yoru_mark);mark.SetScaleType(ImageView.ScaleType.FitCenter);branding.AddView(mark,new LinearLayout.LayoutParams(D(36),D(40)));
        var word=Ui.Label(owner,"YORU",22);word.LetterSpacing=.12f;word.SetTypeface(null,TypefaceStyle.Bold);branding.AddView(word,new LinearLayout.LayoutParams(0,-1,1){LeftMargin=D(9)});
        branding.AddView(StreamActions.Button(owner,"Search",L.T("MobileSearch"),owner.OpenSearch),new LinearLayout.LayoutParams(D(48),D(48)));
        AddView(branding,new FrameLayout.LayoutParams(-1,D(60),GravityFlags.Top));
        copy=Ui.Vertical(owner);copy.SetPadding(D(22),D(10),D(22),D(16));
        eyebrow=Ui.Label(owner,L.T("Featured"),10,Ui.Selected);eyebrow.LetterSpacing=.1f;copy.AddView(eyebrow,new LinearLayout.LayoutParams(-1,-2){BottomMargin=D(6)});
        title=Ui.Label(owner,"",26);title.SetTypeface(null,TypefaceStyle.Bold);title.SetMaxLines(3);title.Ellipsize=Android.Text.TextUtils.TruncateAt.End;title.SetShadowLayer(D(8),0,D(2),Color.Black);copy.AddView(title);
        genres=Ui.Label(owner,"",12,Color.ParseColor("#D7D3E4"));genres.SetMaxLines(1);genres.Ellipsize=Android.Text.TextUtils.TruncateAt.End;copy.AddView(genres,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(6),BottomMargin=D(16)});
        var actions=new LinearLayout(owner){Orientation=Orientation.Horizontal};
        watch=Ui.Button(owner,L.T("WatchNow"),()=>{if(row?.Anime is not null)owner.OpenAnime(row.Anime,true);},true);StreamActions.ButtonIcon(watch,"Play");
        save=Ui.Button(owner,L.T("MyList"),()=>{if(row?.Anime is not null)owner.ToggleFavorite(row.Anime);});save.Background=new RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#338B5CF6")),Ui.OutlinedShape(Color.ParseColor("#A3161826"),D(13),Color.ParseColor("#667D749B")),null);
        actions.AddView(watch,new LinearLayout.LayoutParams(0,D(48),1){MarginEnd=D(10)});actions.AddView(save,new LinearLayout.LayoutParams(0,D(48),1));copy.AddView(actions);
        AddView(copy,new FrameLayout.LayoutParams(-1,-2,GravityFlags.Bottom|GravityFlags.CenterHorizontal));
        Click+=(_,_)=>{if(row is not null)owner.HandleRow(row);};
    }
    private int D(float value)=>Ui.Dp(Context!,value);
    public void Resize(bool home) {
        var p=(FrameLayout.LayoutParams)branding.LayoutParameters!;p.TopMargin=home?owner.SafeTop:0;branding.LayoutParameters=p;
        branding.Visibility=home?ViewStates.Visible:ViewStates.Gone;
        var landscape=owner.Resources!.Configuration!.ScreenWidthDp>owner.Resources.Configuration.ScreenHeightDp;
        var compact=landscape || owner.HeroHeight<340;
        title.TextSize=compact?22:26;title.SetMaxLines(compact?2:3);
        var cp=(FrameLayout.LayoutParams)copy.LayoutParameters!;cp.Width=Math.Min(owner.ContentWidthPixels,D(720));copy.LayoutParameters=cp;
    }
    public void Bind(FeedRow next) {
        if(next.Key!="featured" && (row?.Anime?.Id!=next.Anime?.Id || row?.Key!=next.Key))ResetArtwork();
        row=next;Resize(next.Key=="featured");title.Text=next.Title;
        genres.Text=next.Anime is null?"":string.Join("  ·  ",new[]{AnimeMetadata.Year(next.Anime)}.Concat(AnimeMetadata.Genres(next.Anime).Take(3)).Where(s=>s.Length>0));
        eyebrow.Text=L.T("Featured");save.Text=L.T(next.Saved?"Saved":"MyList");StreamActions.ButtonIcon(save,next.Saved?"Check":"Add");save.ContentDescription=save.Text;
        ContentDescription=next.Title;
        if(next.Anime is null)return;
        var urls=AnimeMetadata.Art(next.Anime,true);var asset=FeaturedArtwork.ForPage(next.Key=="featured"?"Home":"Details",next.Anime.Name);var key=asset is null?string.Join("|",urls):"asset:"+asset;
        if(artKey==key && hasArtwork || requestedKey==key && loading is not null)return;
        Cancel();requestedKey=key;loading=new();var token=loading.Token;var target=hasArtwork?1-visible:visible;var old=visible;
        artwork[target].Animate()!.Cancel();artwork[target].Alpha=0;
        var width=Math.Min(owner.Resources!.DisplayMetrics!.WidthPixels,1440);var height=Ui.Dp(owner,owner.HeroHeight);
        void Loaded(){
            if(token.IsCancellationRequested)return;
            artwork[target].Animate()!.Alpha(1).SetDuration(220).Start();
            if(hasArtwork && old!=target)artwork[old].Animate()!.Alpha(0).SetDuration(220).Start();
            visible=target;hasArtwork=true;artKey=key;requestedKey="";
        }
        _=LoadArtwork();
        async Task LoadArtwork(){
            if(asset is not null && await YoruApp.Images.LoadAsset(artwork[target],owner.Assets!,asset,owner.ContentWidthPixels,height,token,Loaded))return;
            if(!token.IsCancellationRequested)await YoruApp.Images.Load(artwork[target],urls,width,height,token,Loaded);
        }
    }
    public void PrepareDetails(string animeId){if(row?.Key!="featured" && row?.Anime?.Id!=animeId)ResetArtwork();}
    private void ResetArtwork(){Cancel();foreach(var image in artwork){image.SetImageDrawable(null);image.Alpha=0;}hasArtwork=false;visible=0;artKey="";requestedKey="";}
    public void Cancel(){loading?.Cancel();loading?.Dispose();loading=null;requestedKey="";foreach(var image in artwork)image.Animate()!.Cancel();if(hasArtwork){artwork[visible].Alpha=1;artwork[1-visible].Alpha=0;}}
    public override bool DispatchTouchEvent(MotionEvent? e) {
        if(e is null)return false;
        if(e.ActionMasked==MotionEventActions.Down){downX=e.GetX();downY=e.GetY();swiping=false;}
        if(row?.Key=="featured" && e.ActionMasked==MotionEventActions.Move && Math.Abs(e.GetX()-downX)>D(28) && Math.Abs(e.GetX()-downX)>Math.Abs(e.GetY()-downY)*1.5f){swiping=true;Parent?.RequestDisallowInterceptTouchEvent(true);}
        if(swiping && e.ActionMasked==MotionEventActions.Up){using var cancel=MotionEvent.Obtain(e)!;cancel.Action=MotionEventActions.Cancel;base.DispatchTouchEvent(cancel);owner.CycleFeature((e.GetX()<downX?1:-1)*(L.Language=="ar"?-1:1));swiping=false;Parent?.RequestDisallowInterceptTouchEvent(false);return true;}
        if(e.ActionMasked==MotionEventActions.Cancel)swiping=false;
        return base.DispatchTouchEvent(e);
    }
    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info){base.OnInitializeAccessibilityNodeInfo(info);if(row?.Key=="featured" && info is not null){info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollForward);info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollBackward);}}
    public override bool PerformAccessibilityAction(Android.Views.Accessibility.Action action,Android.OS.Bundle? arguments){if(row?.Key=="featured" && action is Android.Views.Accessibility.Action.ScrollForward or Android.Views.Accessibility.Action.ScrollBackward){owner.CycleFeature(action==Android.Views.Accessibility.Action.ScrollForward?1:-1);return true;}return base.PerformAccessibilityAction(action,arguments);}
}
