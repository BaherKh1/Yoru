using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yoru.Controls;

namespace Yoru;

internal static class V7Diagnostics
{
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory);
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-v7-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var checks=new List<string>(); string? error=null; MainWindow? window=null;
        using var host=new HwndSource(new HwndSourceParameters("Hidden V7 regressions") { Width=1920,Height=1080,WindowStyle=unchecked((int)0x80000000) });
        try {
            Locale.Current.Initialize(scratch); Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var records=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(x=>Anime.Parse(x)).ToList();
            var arcane=Anime.Parse(JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"v7-arcane.json")))!["hits"]!.AsArray().OfType<JsonObject>().First(x=>x.Text("name")=="Arcane"));
            var solo=records.First(x=>x.Name=="Ore dake Level Up na Ken");
            window=new MainWindow(false,new LibraryStore(scratch),new HistoryStore(scratch)); window.LoadDesignCatalog(records);
            var root=(FrameworkElement)window.Content; host.RootVisual=root;
            async Task Layout(Size size) {
                window.Width=size.Width; window.Height=size.Height; window.Measure(size); window.Arrange(new Rect(size));
                root.Measure(size); root.Arrange(new Rect(size)); window.ApplyResponsive(size); root.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(100); root.UpdateLayout();
            }
            var episodes=Enumerable.Range(1,120).Select(i=>new Episode(i.ToString("000"),"Episode "+i,"Episode subtitle",new JsonObject())).ToList();
            foreach(var lang in new[] {"en","ar"}) foreach(var size in new[] {new Size(1000,600),new Size(1366,768),new Size(1600,900),new Size(1920,1080)}) {
                Locale.Current.SetForDiagnostics(lang); window.ShowDesignDetails(episodes);
                var detailBanner=(Grid)window.FindName("DetailHero");
                detailBanner.DataContext=new AnimeCardModel(records.First(x=>x.Name.Contains("Arise from the Shadow")));
                await Layout(size);
                var page=(Grid)window.FindName("DetailsPage"); var list=(ListBox)window.FindName("EpisodeList");
                var header=(Grid)window.FindName("EpisodeHeader");
                var scroll=Descendants<ScrollViewer>(list).First();
                Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
                void InPage(FrameworkElement element) { var bounds=Bounds(element); Check(bounds.Left>=-1 && bounds.Top>=-1 && bounds.Right<=page.ActualWidth+1 && bounds.Bottom<=page.ActualHeight+1,$"{element.Name} outside fixed details viewport: {bounds}"); }
                Check(Descendants<ScrollViewer>(page).All(view=>list.IsAncestorOf(view) || ((TextBox)window.FindName("EpisodeFilter")).IsAncestorOf(view)),"An outer details ScrollViewer remains");
                Check(scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto && scroll.ScrollableHeight>100,"Episode list is not independently scrollable");
                Check(VirtualizingPanel.GetScrollUnit(list)==ScrollUnit.Pixel && double.IsPositiveInfinity(list.MaxHeight),"Episode list retains an estimated height or logical scrolling");
                var heroBounds=Bounds(detailBanner); var headerBounds=Bounds(header); var listBounds=Bounds(list);
                Check(listBounds.Top>=headerBounds.Bottom && headerBounds.Top>=heroBounds.Bottom,"Fixed header and episodes overlap");
                Check(Math.Abs(listBounds.Bottom-(page.ActualHeight-12))<1 && list.ActualHeight>=120,"Episode list does not fill the remaining window height");
                foreach(var name in new[]{"DetailHero","DetailTitle","DetailMetadata","DetailSynopsis","DetailActions","EpisodeHeader","EpisodeFilter","EpisodeList"}) InPage((FrameworkElement)window.FindName(name));
                var actions=(StackPanel)window.FindName("DetailActions");
                foreach(var button in actions.Children.OfType<Button>()) InPage(button);
                Check(((TextBlock)window.FindName("DetailTitle")).ActualHeight>40 || size.Width>1000,"Long title lost its second line in the minimum window");
                var synopsis=(TextBlock)window.FindName("DetailSynopsis");
                Check(synopsis.ActualHeight>=20,"Short window removed the synopsis");
                var content=(Grid)window.FindName("DetailContent");
                foreach(var name in new[]{"DetailTitle","DetailMetadata","DetailSynopsis","DetailActions"}) {
                    var element=(FrameworkElement)window.FindName(name);
                    var bounds=element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                    Check(bounds.Top>=-1 && bounds.Bottom<=content.ActualHeight+1,$"Upper details content clips {name}");
                }
                scroll.ScrollToTop(); await Layout(size);
                detailBanner.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=Mouse.MouseWheelEvent });
                await Layout(size); Check(scroll.VerticalOffset==0 && Bounds(detailBanner)==heroBounds,"Wheel over the fixed section scrolls content");
                scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=Mouse.MouseWheelEvent });
                await Layout(size); Check(scroll.VerticalOffset>0,"Wheel did not scroll the episode viewport");
                list.ScrollIntoView(episodes[^1]); scroll.ScrollToEnd(); await Layout(size);
                var last=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(episodes[^1]);
                Check(last is not null,"Last episode was not realized");
                var viewport=Descendants<ScrollContentPresenter>(list).First();
                var rowBounds=last!.TransformToAncestor(viewport).TransformBounds(new Rect(last.RenderSize));
                Check(rowBounds.Top>=-1 && rowBounds.Bottom<=viewport.ActualHeight+1,$"Last episode clipped: {rowBounds}, viewport={viewport.ActualHeight}");
                Check(Bounds(detailBanner)==heroBounds && Bounds(header)==headerBounds,"Upper section or episode search moves with episode scrolling");
                var bars=Descendants<ScrollBar>(page).Where(bar=>bar.IsVisible).ToArray();
                Check(bars.Length==1,"Details has missing or double scrollbars");
                var bar=bars.Single(); Check(list.IsAncestorOf(bar) && bar.ActualWidth<=7.1,"Visible scrollbar is not the thin episode scrollbar");
                var barBounds=bar.TransformToAncestor(list).TransformBounds(new Rect(bar.RenderSize));
                Check(page.FlowDirection==FlowDirection.RightToLeft ? Math.Abs(barBounds.Left)<1 : Math.Abs(barBounds.Right-list.ActualWidth)<1,"Episode scrollbar is not at the physical right edge");
                var offset=scroll.VerticalOffset; ScrollBar.PageUpCommand.Execute(null,bar); await Layout(size);
                Check(scroll.VerticalOffset<offset,"Episode scrollbar page-up command no longer scrolls");
                var filter=(TextBox)window.FindName("EpisodeFilter"); filter.Text="120"; await Layout(size);
                Check(list.Items.Count==1 && ((Episode)list.Items[0]).Id=="120","Episode search stopped filtering");
                Check(((FrameworkElement)window.FindName("EpisodeFilterHint")).Visibility==Visibility.Collapsed && filter.ActualHeight<=36,"Compact search/hint sizing failed");
                Check(Bounds(detailBanner)==heroBounds && Bounds(header)==headerBounds,"Filtering shifts the fixed upper section");
                filter.Clear(); await Layout(size); list.ScrollIntoView(episodes[^1]); scroll.ScrollToEnd(); await Layout(size);
                Save(root,Path.Combine(directory,$"v7-details-{lang}-{size.Width}x{size.Height}.png"),size);
                checks.Add($"Fixed details {lang} {size}: long title/poster/summary/genres/actions and search stay visible and static; remaining-height episode viewport; wheel and thin scrollbar work; last row fully visible; no outer/double scrolling");
            }
            Locale.Current.SetForDiagnostics("en");
            window.LoadDesignCatalog(new[]{solo,arcane}.Concat(records)); await Layout(new Size(1366,768));
            for(var i=0;i<10 && window.Featured?.Id!=solo.Id;i++) typeof(MainWindow).GetMethod("HeroNext_Click",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            Check(window.Featured?.Id==solo.Id,"Solo Leveling was not selected for the hero regression");
            await Layout(new Size(1366,768));
            await Task.Delay(700);
            var hero=(Grid)window.FindName("Hero");
            Check(Descendants<RoundedImage>(hero).First().Source is BitmapSource,"Solo Leveling hero fallback is blank");
            Save(root,Path.Combine(directory,"v7-home-fallback.png"),new Size(1366,768));
            window.ShowDesignExplore(); await Layout(new Size(1366,768)); await Task.Delay(700);
            var arcaneCard=Descendants<AnimeCard>((ListBox)window.FindName("Catalog")).First(card=>((AnimeCardModel)card.DataContext).Id==arcane.Id);
            Check(Descendants<RoundedImage>(arcaneCard).First().Source is BitmapSource,"Arcane's record-based artwork fallback is blank");
            Save(root,Path.Combine(directory,"v7-arcane-fallback.png"),new Size(1366,768));
            checks.Add("Captured real records: Solo Leveling hero and Arcane card render available same-anime fallback artwork");
            Artwork.PreviewDirectory=null;
            var imageBytes=File.ReadAllBytes(Directory.GetFiles(Path.Combine(directory,"design-assets"),"*.img").First());
            var cacheDirectory=Path.Combine(scratch,"corrupt-cache"); Directory.CreateDirectory(cacheDirectory);
            const string url="https://fixture.invalid/image.jpg";
            var filename=Path.Combine(cacheDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()+".img");
            File.WriteAllText(filename,"<html>cached error</html>");
            using(var cache=new ArtworkCache(cacheDirectory,new ImageHandler(imageBytes))) {
                Check(await cache.Get(url,160,240) is BitmapSource,"Corrupt cache did not recover in the same request");
                Check(!File.ReadAllBytes(filename).AsSpan().StartsWith("<html>"u8),"Corrupt disk bytes survived replacement");
            }
            checks.Add("Corrupt cached artwork is evicted and replaced immediately in the same load");
            var sourceDir=Path.Combine(scratch,"response-cache");
            using(var cache=new ArtworkCache(sourceDir,new ImageHandler(imageBytes))) {
                Check(await cache.Get("https://fixture.invalid/html",160,240) is null,"HTML response accepted as artwork");
                Check(!Directory.Exists(sourceDir) || !Directory.GetFiles(sourceDir,"*.img").Any(),"HTML response poisoned persistent cache");
                var image=new RoundedImage { Cache=cache,ArtworkSource="https://fixture.invalid/slow",FallbackSources=new[]{"https://fixture.invalid/slow",url},Width=160,Height=240 };
                var surface=new Grid(); surface.Children.Add(image); host.RootVisual=surface;
                surface.Measure(new Size(160,240)); surface.Arrange(new Rect(0,0,160,240)); surface.UpdateLayout();
                await Task.Delay(450);
                Check(image.Source is BitmapSource,"Slow preferred source left the fallback invisible");
                var sourceBefore=image.Source; image.ArtworkSource="https://fixture.invalid/new"; image.FallbackSources=new[]{"https://fixture.invalid/new"};
                await Task.Delay(1400);
                Check(image.Source is BitmapSource && !ReferenceEquals(image.Source,sourceBefore),"Rebinding retained stale fallback artwork");
                host.RootVisual=null;
            }
            checks.Add("HTTP 200 HTML never cached; fallback paints before slow source completes; rebinding cancels stale work");
            Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var webp=Anime.Parse(new JsonObject { ["poster"]=new JsonObject { ["medium"]="https://cdn.myanimelist.net/images/anime/9/39495.webp" } });
            Artwork.PreviewDirectory=null;
            Check(Artwork.Alternatives(webp).First().EndsWith("39495.jpg"),"MAL WebP JPEG counterpart missing");
            checks.Add("MAL WebP records offer their JPEG counterpart without requiring an OS image extension");
        } catch(Exception exception) { error=exception.ToString(); }
        finally { host.RootVisual=null; window?.Close(); Artwork.PreviewDirectory=null; Locale.Current.Dispose(); }
        File.WriteAllText(Path.Combine(directory,"v7-regression-checks.json"),JsonSerializer.Serialize(new {success=error is null,checks,error},new JsonSerializerOptions {WriteIndented=true}));
        return error is null?0:1;
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found) yield return found;foreach(var item in Descendants<T>(child))yield return item;} }
    private static void Save(Visual root,string path,Size size) {var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}
    private sealed class ImageHandler(byte[] bytes):HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            if(request.RequestUri!.AbsolutePath=="/slow")await Task.Delay(1100,ct);
            var html=request.RequestUri.AbsolutePath=="/html";
            return new(HttpStatusCode.OK) {Content=html?new StringContent("<html>Not an image</html>"):new ByteArrayContent(bytes)};
        }
    }
}
