using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
namespace AnimeWitcherDesktop.Controls;
public sealed class RoundedImage : Image
{
    internal ArtworkCache Cache { get; set; } = ArtworkCache.Shared;
    public static readonly DependencyProperty ArtworkSourceProperty=DependencyProperty.Register(nameof(ArtworkSource),typeof(object),typeof(RoundedImage),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsMeasure,(owner,_)=>((RoundedImage)owner).Changed()));
    public object? ArtworkSource { get=>(object?)GetValue(ArtworkSourceProperty); set=>SetValue(ArtworkSourceProperty,value); }
    public static readonly DependencyProperty FallbackSourcesProperty=DependencyProperty.Register(nameof(FallbackSources),typeof(string[]),typeof(RoundedImage),new PropertyMetadata(null,(owner,args)=> {
        if(args.OldValue is string[] previous && args.NewValue is string[] next && previous.SequenceEqual(next)) return;
        ((RoundedImage)owner).Changed();
    }));
    public string[]? FallbackSources { get=>(string[]?)GetValue(FallbackSourcesProperty); set=>SetValue(FallbackSourcesProperty,value); }
    public double Radius { get; set; } = 10;
    private CancellationTokenSource? load;
    private int generation, requestedWidth, requestedHeight;
    private bool scheduled;
    private DateTime retryAfter;
    private DispatcherTimer? retryTimer;
    private RectangleGeometry? clip;
    private Size clipSize;
    public RoundedImage()
    {
        Loaded+=(_,_)=> { if(ArtworkSource is ImageSource image) Source=image; else if(!TryDisplayCached(RenderSize)) Schedule(); }; SizeChanged+=(_,_)=>Schedule(); IsVisibleChanged+=(_,_)=> { if(IsVisible) Schedule(); else { retryTimer?.Stop(); load?.Cancel(); generation++; requestedWidth=requestedHeight=0; } };
        Unloaded+=(_,_)=> { retryTimer?.Stop(); load?.Cancel(); generation++; requestedWidth=requestedHeight=0; Source=null; };
    }
    private void Changed()
    {
        retryTimer?.Stop(); load?.Cancel(); generation++; requestedWidth=requestedHeight=0; retryAfter=default;
        if(ArtworkSource is ImageSource image) { Source=image; return; }
        Source=null;
        Schedule();
    }
    private void Schedule()
    {
        if(scheduled || ArtworkSource is not string || (!IsLoaded || !IsVisible) && Artwork.PreviewDirectory is null) return;
        if(DateTime.UtcNow<retryAfter) { RetryLater(); return; }
        scheduled=true; _=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=> { scheduled=false; _=LoadArtwork(); }));
    }
    private bool TryDisplayCached(Size size)
    {
        // Match LoadArtwork's slot-based request. Image.RenderSize can extend
        // beyond that slot under UniformToFill and must not change cache choice.
        var slot=LayoutInformation.GetLayoutSlot(this);
        if(slot.Width>0 && slot.Height>0 && !double.IsInfinity(slot.Width) && !double.IsInfinity(slot.Height)) size=slot.Size;
        if(ArtworkSource is not string location || (!IsVisible && Artwork.PreviewDirectory is null) || size.Width<=0 || size.Height<=0 || double.IsInfinity(size.Width) || double.IsInfinity(size.Height)) return false;
        var dpi=VisualTreeHelper.GetDpi(this);
        var width=(Math.Min(3840,(int)Math.Ceiling(size.Width*dpi.DpiScaleX*1.25))+63)/64*64;
        var height=(Math.Min(3840,(int)Math.Ceiling(size.Height*dpi.DpiScaleY*1.25))+63)/64*64;
        var choices=FallbackSources is {Length:>0}?FallbackSources:new[]{location};
        for(var i=0;i<choices.Length;i++) if(Cache.TryReady(choices[i],width,height,out var bitmap)) {
            if(i==0 && (requestedWidth!=width || requestedHeight!=height)) {load?.Cancel();generation++;}
            if(!ReferenceEquals(Source,bitmap)) Source=bitmap;
            if(i!=0) return false;
            requestedWidth=width;requestedHeight=height;return true;
        }
        return false;
    }
    private async Task LoadArtwork()
    {
        if(ArtworkSource is not string location) return;
        if(string.IsNullOrEmpty(location)) location=FallbackSources?.FirstOrDefault() ?? "";
        if(string.IsNullOrEmpty(location)) return;
        var slot=LayoutInformation.GetLayoutSlot(this); var dpi=VisualTreeHelper.GetDpi(this);
        if(double.IsInfinity(slot.Width) || double.IsInfinity(slot.Height)) return;
        var width=(int)Math.Ceiling(slot.Width*dpi.DpiScaleX*1.25); var height=(int)Math.Ceiling(slot.Height*dpi.DpiScaleY*1.25);
        if(width<1 || height<1) return;
        width=(Math.Min(width,3840)+63)/64*64; height=(Math.Min(height,3840)+63)/64*64;
        if(width==requestedWidth && height==requestedHeight) return;
        requestedWidth=width; requestedHeight=height; load?.Cancel(); load?.Dispose(); load=new();
        var version=++generation; var token=load.Token;
        try {
            var choices=(FallbackSources ?? Array.Empty<string>()).Where(url=>!string.IsNullOrWhiteSpace(url)).Distinct().ToArray();
            // Prefer normalized display-compatible candidates (e.g. MAL JPEG).
            if(choices.Length==0) choices=new[] { location };
            if(Cache.TryReady(choices[0],width,height,out var cached)) {
                if(version==generation && !token.IsCancellationRequested) Source=cached;
                return;
            }
            var primary=Cache.Get(choices[0],width,height,token);
            async Task<System.Windows.Media.Imaging.BitmapSource?> Backup() {
                foreach(var alternative in choices.Skip(1)) {
                    var image=await Cache.Get(alternative,width,height,token);
                    if(image is not null) return image;
                }
                return null;
            }
            void Show(System.Windows.Media.Imaging.BitmapSource? image) { if(image is not null && version==generation && !token.IsCancellationRequested) Source=image; }
            foreach(var alternative in choices.Skip(1)) if(Cache.TryReady(alternative,width,height,out var preview)) {Show(preview);break;}
            await Task.WhenAny(primary,Task.Delay(150,token));
            token.ThrowIfCancellationRequested();
            if(primary.IsCompletedSuccessfully && primary.Result is not null) { Show(primary.Result); return; }
            var backup=Backup();
            var first=await Task.WhenAny(primary,backup); Show(await first);
            // A fast fallback fills the slot while the original banner finishes.
            // Observe both tasks on cancellation so recycled cards cannot leak faults.
            var results=await Task.WhenAll(primary,backup);
            Show(results[0] ?? results[1]);
            if(version==generation && Source is null) { retryAfter=DateTime.UtcNow.AddMinutes(1); requestedWidth=requestedHeight=0; RetryLater(); }
        }
        catch(OperationCanceledException) { }
    }
    private void RetryLater()
    {
        if(!IsLoaded || !IsVisible) return;
        if(retryTimer is null) {
            retryTimer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher);
            retryTimer.Tick+=(_,_)=> { retryTimer.Stop(); retryAfter=default; Schedule(); };
        }
        retryTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(100,(retryAfter-DateTime.UtcNow).TotalMilliseconds));
        retryTimer.Start();
    }
    protected override Size MeasureOverride(Size constraint)
    {
        var measured=base.MeasureOverride(constraint);
        // A source-less Image measures to zero and never receives SizeChanged.
        // Reserve its existing finite layout slot while the async image is pending.
        if(Source is null && ArtworkSource is string && !double.IsInfinity(constraint.Width) && !double.IsInfinity(constraint.Height)) return constraint;
        return measured;
    }
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result=base.ArrangeOverride(arrangeSize);
        if(!TryDisplayCached(arrangeSize)) Schedule();
        return Source is null && ArtworkSource is string?arrangeSize:result;
    }
    protected override void OnRender(DrawingContext context)
    {
        if(clip is null || clipSize!=RenderSize || clip.RadiusX!=Radius) { clipSize=RenderSize; clip=new RectangleGeometry(new Rect(RenderSize),Radius,Radius); clip.Freeze(); }
        context.PushClip(clip); base.OnRender(context); context.Pop();
    }
}
