using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Yoru;
using Yoru.Core;

namespace Yoru.AndroidApp;

internal sealed class FloatingNavigation : FrameLayout
{
    private readonly View indicator;
    private readonly ImageView frost;
    private readonly LinearLayout items;
    private readonly Dictionary<string,(LinearLayout Cell,StreamIcon Icon,TextView Label)> tabs=new();
    private readonly Dictionary<string,Color> colors=new();
    private readonly HashSet<string> disabled=new();
    private ValueAnimator? colorAnimation;
    private string selected="Home";
    private Bitmap? snapshot;
    private int captureVersion;
    private bool backdropRequested;
    public FloatingNavigation(Context context,Action<string> navigate):base(context) {
        Background=Ui.OutlinedShape(Color.ParseColor("#5C1C1C28"),Ui.Dp(context,24),Color.ParseColor("#387E748E"));ClipToOutline=true;Elevation=Ui.Dp(context,8);
        if(OperatingSystem.IsAndroidVersionAtLeast(28)){SetOutlineAmbientShadowColor(Color.ParseColor("#60000000"));SetOutlineSpotShadowColor(Color.ParseColor("#80000000"));}
        frost=new ImageView(context){Alpha=.38f};frost.SetScaleType(ImageView.ScaleType.FitXy);AddView(frost,new FrameLayout.LayoutParams(-1,-1));
        if(OperatingSystem.IsAndroidVersionAtLeast(31))frost.SetRenderEffect(RenderEffect.CreateBlurEffect(Ui.Dp(context,10),Ui.Dp(context,10),Shader.TileMode.Clamp!));
        AddView(new View(context){Background=Ui.Shape(Color.ParseColor("#20181723"),0)},new FrameLayout.LayoutParams(-1,-1));
        indicator=new View(context){Background=Ui.Shape(Color.ParseColor("#4D8B5CF6"),Ui.Dp(context,18))};AddView(indicator,new FrameLayout.LayoutParams(1,Ui.Dp(context,MobileLayout.NavigationHeight(context.Resources!.Configuration!.FontScale)-18),GravityFlags.Top|GravityFlags.Left){TopMargin=Ui.Dp(context,9)});
        items=new LinearLayout(context){Orientation=Orientation.Horizontal};items.SetPadding(Ui.Dp(context,5),0,Ui.Dp(context,5),0);AddView(items,new FrameLayout.LayoutParams(-1,-1));
        foreach(var name in MobileNavigation.Tabs) {
            var label=L.T(name=="More"?"MobileMore":name=="History"?"MobileHistoryShort":name);
            var cell=Ui.Vertical(context);cell.SetGravity(GravityFlags.Center);cell.Clickable=true;cell.Focusable=true;cell.ContentDescription=L.T(name=="More"?"MobileMore":name);
            cell.Background=new RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#228B5CF6")),null,null);
            var icon=new StreamIcon(name,Ui.Muted);var image=new ImageView(context);image.SetImageDrawable(icon);cell.AddView(image,new LinearLayout.LayoutParams(Ui.Dp(context,25),Ui.Dp(context,25)));
            var text=Ui.Label(context,label,10,Ui.Muted);text.Gravity=GravityFlags.Center;text.SetMaxLines(1);text.Ellipsize=Android.Text.TextUtils.TruncateAt.End;cell.AddView(text,new LinearLayout.LayoutParams(-1,Ui.Dp(context,19*Math.Clamp(context.Resources!.Configuration!.FontScale,1,2))){TopMargin=Ui.Dp(context,3)});
            cell.Click+=(_,_)=>navigate(name);items.AddView(cell,new LinearLayout.LayoutParams(0,-1,1));tabs[name]=(cell,icon,text);colors[name]=Ui.Muted;
        }
    }
    // Offline mode dims the sections that need a connection while keeping the
    // floating bar itself unchanged. Taps are still delivered so the activity can
    // explain why the section is unavailable.
    public void SetEnabled(string name,bool enabled) {
        if(!tabs.ContainsKey(name))return;
        var changed=enabled?disabled.Remove(name):disabled.Add(name);
        if(!changed)return;
        tabs[name].Cell.Alpha=enabled?1f:.38f;
    }
    public void Select(string name) {
        if(!tabs.ContainsKey(name))name="More";
        if(selected==name && Width>0)return;
        selected=name;
        colorAnimation?.Cancel();colorAnimation?.Dispose();
        var from=colors.ToDictionary(pair=>pair.Key,pair=>pair.Value);var destination=name;
        colorAnimation=ValueAnimator.OfFloat(0,1);colorAnimation!.SetDuration(225);colorAnimation.SetInterpolator(new AccelerateDecelerateInterpolator());
        colorAnimation.Update+=(_,e)=>{var fraction=(float)e.Animation.AnimatedValue!;foreach(var pair in tabs){var a=from[pair.Key];var b=pair.Key==destination?Ui.Selected:Ui.Muted;var color=new Color((int)(a.R+(b.R-a.R)*fraction),(int)(a.G+(b.G-a.G)*fraction),(int)(a.B+(b.B-a.B)*fraction));colors[pair.Key]=color;pair.Value.Icon.Tint(color);pair.Value.Label.SetTextColor(color);}};
        colorAnimation.Start();
        Post(()=>MoveIndicator(true));
    }
    protected override void OnLayout(bool changed,int left,int top,int right,int bottom) {
        base.OnLayout(changed,left,top,right,bottom);if(changed){colorAnimation?.Cancel();foreach(var pair in tabs){var color=pair.Key==selected?Ui.Selected:Ui.Muted;colors[pair.Key]=color;pair.Value.Icon.Tint(color);pair.Value.Label.SetTextColor(color);}MoveIndicator(false);}
    }
    private void MoveIndicator(bool animate) {
        if(!tabs.TryGetValue(selected,out var tab) || tab.Cell.Width==0)return;
        var target=items.Left+tab.Cell.Left+Ui.Dp(Context!,3);var width=tab.Cell.Width-Ui.Dp(Context!,6);
        if(indicator.LayoutParameters!.Width!=width){indicator.LayoutParameters.Width=width;indicator.RequestLayout();}
        indicator.Animate()!.Cancel();
        if(animate && IsLaidOut && ValueAnimator.AreAnimatorsEnabled())indicator.Animate()!.TranslationX(target).SetDuration(225).SetInterpolator(new AccelerateDecelerateInterpolator()).Start();else indicator.TranslationX=target;
    }
    // Retain the subtle glass surface for this window. Scrolling, paging
    // and scroll-idle events never hide, recapture or restyle it.
    public void EnsureBackdrop(RecyclerView source) {
        if(backdropRequested)return;
        backdropRequested=true;
        var version=++captureVersion;
        PostDelayed(()=>{if(version!=captureVersion)return;
            if(Visibility!=ViewStates.Visible || !IsAttachedToWindow || Width<=0 || source.Width<=0){backdropRequested=false;return;}
            var w=Math.Max(1,Width/5);var h=Math.Max(1,Height/5);
            if(snapshot is null || snapshot.Width!=w || snapshot.Height!=h){frost.SetImageDrawable(null);snapshot?.Dispose();snapshot=Bitmap.CreateBitmap(w,h,Bitmap.Config.Argb8888!)!;}
            using(var canvas=new Canvas(snapshot)){canvas.DrawColor(Ui.Background);canvas.Scale(w/(float)Width,h/(float)Height);canvas.Translate(-Left,-Top);source.Draw(canvas);}
            if(OperatingSystem.IsAndroidVersionAtLeast(31))frost.SetImageBitmap(snapshot);
            else{var pixels=new int[w*h];snapshot.GetPixels(pixels,0,w,0,0,w,h);_=SoftwareFrost(pixels,w,h,version);}
        },100);
    }
    private async Task SoftwareFrost(int[] pixels,int width,int height,int version){
        var blurred=await Task.Run(()=>BackdropBlur.Apply(pixels,width,height,2)).ConfigureAwait(false);
        Post(()=>{if(version!=captureVersion || !IsAttachedToWindow || snapshot is null || snapshot.Width!=width || snapshot.Height!=height)return;snapshot.SetPixels(blurred,0,width,0,0,width,height);frost.SetImageBitmap(snapshot);});
    }
    public void Release(){captureVersion++;indicator.Animate()!.Cancel();colorAnimation?.Cancel();colorAnimation?.Dispose();colorAnimation=null;frost.SetImageDrawable(null);snapshot?.Dispose();snapshot=null;}
}
