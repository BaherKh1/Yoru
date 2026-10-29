using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnimeWitcherDesktop;

// Shared unchanged with the isolated V8.1 baseline. Only this test harness
// models two 250ms network stages; production startup contains no delay.
internal static class StartupProfileDiagnostics
{
    internal static async Task<int> Run(string directory,string scenario)
    {
        directory=Path.GetFullPath(directory); var warm=scenario=="warm";
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-startup-profile-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch); string? error=null; var metrics=new Dictionary<string,object>(); MainWindow? window=null;
        var modern=typeof(MainWindow).GetMethod("StartHomeForDiagnostics",BindingFlags.Instance|BindingFlags.NonPublic);
        var version=modern is null?"v81":"v9";
        using var host=new HwndSource(new HwndSourceParameters("Hidden startup profile") {Width=1366,Height=768,WindowStyle=unchecked((int)0x80000000)});
        try {
            Locale.Current.Initialize(scratch); Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var capture=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!;
            var records=capture["hits"]!.AsArray().OfType<JsonObject>().Select(record=>Anime.Parse(record)).ToList();
            var page=new SearchPage(records,0,Math.Max(1,((int)capture.Number("total")+29)/30),Math.Max(records.Count,(int)capture.Number("total")));
            var store=new StartupCatalogStore(scratch); if(warm) await store.Save(page);
            var calls=0; var networkComplete=false;
            Func<CancellationToken,Task<SearchPage>> fetch=async ct=> {calls++; await Task.Delay(250,ct); await Task.Delay(250,ct); networkComplete=true; return page;};
            var watch=Stopwatch.StartNew();
            var ctor=typeof(MainWindow).GetConstructors().FirstOrDefault(value=>value.GetParameters().Length==5);
            window=ctor is not null?(MainWindow)ctor.Invoke(new object[]{false,new LibraryStore(scratch),new HistoryStore(scratch),store,fetch}):new MainWindow(false,new LibraryStore(scratch),new HistoryStore(scratch));
            metrics["constructorMs"]=watch.Elapsed.TotalMilliseconds;
            double? ready=window.Featured is null?null:watch.Elapsed.TotalMilliseconds;
            async Task Legacy() {await fetch(CancellationToken.None); window.LoadDesignCatalog(records);}
            var pending=modern is not null?(Task)modern.Invoke(window,null)!:Legacy();
            var root=(FrameworkElement)window.Content; host.RootVisual=root;
            void Layout() {var size=new Size(1366,768);window.Measure(size);window.Arrange(new Rect(size));root.Measure(size);root.Arrange(new Rect(size));window.ApplyResponsive(size);root.UpdateLayout();}
            Layout(); await Dispatcher.Yield(DispatcherPriority.Render); Layout();
            var firstData=window.Featured is not null && ((ItemsControl)window.FindName("HomePopular")).Items.Count>0;
            metrics["firstPresentationMs"]=watch.Elapsed.TotalMilliseconds;
            metrics["firstPresentationHasData"]=firstData;
            metrics["firstPresentationHasSkeleton"]=window.FindName("HeroSkeleton") is FrameworkElement skeleton && skeleton.Visibility==Visibility.Visible;
            metrics["firstPresentationBeforeNetwork"]=!networkComplete;
            metrics["firstPresentationPosters"]=Descendants<Controls.RoundedImage>((ItemsControl)window.FindName("HomePopular")).Count(image=>image.Source is BitmapSource);
            metrics["firstPresentationHeroImage"]=Descendants<Controls.RoundedImage>((FrameworkElement)window.FindName("Hero")).Any(image=>image.Source is BitmapSource);
            if(firstData && ready is null) ready=watch.Elapsed.TotalMilliseconds;
            Save(root,Path.Combine(directory,$"startup-{version}-{scenario}-initial.png"));
            while(!pending.IsCompleted) {if(window.Featured is not null && ready is null) ready=watch.Elapsed.TotalMilliseconds; await Task.Delay(1);}
            await pending; Layout(); ready ??=watch.Elapsed.TotalMilliseconds;
            metrics["metadataReadyMs"]=ready.Value; metrics["refreshCompletedMs"]=watch.Elapsed.TotalMilliseconds; metrics["catalogFetches"]=calls;
            metrics["popularCards"]=((ItemsControl)window.FindName("HomePopular")).Items.Count;
            metrics["homeCommits"]=typeof(MainWindow).GetProperty("HomeCommitCount",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(window)??1;
            metrics["scope"]="Fresh process; 1366x768 hidden WPF presentation; real captured metadata/artwork; modeled 2x250ms network stages only in harness. Not physical monitor or live-network latency.";
            if(calls!=1 || window.Featured is null || (int)metrics["popularCards"]!=8) throw new Exception("Startup failed to produce one coherent catalog batch");
            if(modern is not null && warm && !firstData) throw new Exception("Warm Home missed the initial presentation");
            if(modern is not null && !warm && !(bool)metrics["firstPresentationHasSkeleton"]) throw new Exception("Cold Home exposed empty live controls");
        } catch(Exception exception) {error=exception.ToString();}
        finally {host.RootVisual=null;window?.Close();Artwork.PreviewDirectory=null;Locale.Current.Dispose();}
        File.WriteAllText(Path.Combine(directory,$"startup-{version}-{scenario}.json"),JsonSerializer.Serialize(new {success=error is null,metrics,error},new JsonSerializerOptions {WriteIndented=true}));
        return error is null?0:1;
    }
    private static void Save(Visual root,string path) {var image=new RenderTargetBitmap(1366,768,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item;}}
}
