using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Yoru;

internal static class V6Diagnostics
{
    internal static async Task<int> Run(string directory)
    {
        var checks=new List<string>(); string? error=null; MainWindow? window=null;
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-v6-check-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try {
            void Check(bool value,string message) { if(!value) throw new Exception(message); }
            var raw=Enumerable.Repeat((byte)210,1024*512*4).ToArray(); for(int i=3;i<raw.Length;i+=4) raw[i]=255;
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1024,512,96,96,PixelFormats.Bgra32,null,raw,1024*4)));
            using var png=new MemoryStream(); encoder.Save(png); var content=png.ToArray();
            var handler=new Images(content,30);
            using(var cache=new ArtworkCache(Path.Combine(scratch,"images"),handler,131072)) {
                var loaded=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>cache.Get("https://fixture.invalid/a.png",100,100)));
                Check(handler.Calls==1 && loaded.All(image=>ReferenceEquals(image,loaded[0])),"Duplicate image download/decoding was not coalesced");
                Check(loaded[0] is { IsFrozen:true,PixelWidth:256,PixelHeight:128 },"Poster was not decoded to a sufficient display-sized frozen bitmap");
                await cache.Get("https://fixture.invalid/b.png",256,256);
                using var snapshot=JsonDocument.Parse(JsonSerializer.Serialize(cache.Snapshot()));
                Check(snapshot.RootElement.GetProperty("decodedBytes").GetInt64()<=131072,"Decoded artwork cache exceeded its byte budget");
            }
            var diskHandler=new Images(content,0);
            using(var disk=new ArtworkCache(Path.Combine(scratch,"images"),diskHandler)) { Check(await disk.Get("https://fixture.invalid/a.png",100,100) is not null && diskHandler.Calls==0,"Disk cache did not survive cache-instance restart"); }
            checks.Add("Posters: concurrent deduplication, display-sized frozen decode, memory budget and persistent disk hits");
            using(var broken=new ArtworkCache(Path.Combine(scratch,"bad"),new Images(new byte[]{1,2,3},0))) Check(await broken.Get("https://fixture.invalid/broken",100,100) is null,"Invalid poster bytes were not rejected");
            var repairedHandler=new Images(content,0);
            using(var repaired=new ArtworkCache(Path.Combine(scratch,"bad"),repairedHandler)) Check(await repaired.Get("https://fixture.invalid/broken",100,100) is not null && repairedHandler.Calls==1,"Corrupt compressed cache was retained instead of refetched");
            checks.Add("Corrupt poster payloads are discarded and a later valid response can recover");
            var cancelledHandler=new Images(content,80);
            using(var cache=new ArtworkCache(Path.Combine(scratch,"cancel"),cancelledHandler))
            using(var cancel=new CancellationTokenSource()) {
                var first=cache.Get("https://fixture.invalid/c.png",100,100,cancel.Token); var survivor=cache.Get("https://fixture.invalid/c.png",100,100);
                await Task.Delay(10); cancel.Cancel(); try { await first; throw new Exception("Cancelled poster wait survived"); } catch(OperationCanceledException) { }
                Check(await survivor is not null,"Cancelling a recycled card broke another consumer's image");
            }
            var queuedHandler=new Images(content,80);
            using(var cache=new ArtworkCache(Path.Combine(scratch,"queue"),queuedHandler))
            using(var cancel=new CancellationTokenSource()) {
                var active=Enumerable.Range(0,4).Select(i=>cache.Get("https://fixture.invalid/active"+i,100,100)).ToArray();
                var queued=cache.Get("https://fixture.invalid/not-needed",100,100,cancel.Token); cancel.Cancel();
                try { await queued; throw new Exception("Queued cancellation ignored"); } catch(OperationCanceledException) { }
                await Task.WhenAll(active); Check(queuedHandler.Calls==4 && queuedHandler.MaxActive<=4,"Recycled queued poster caused unnecessary requests or exceeded concurrency limit");
            }
            checks.Add("Posters: recycled-consumer cancellation is isolated; queued obsolete work makes no request; four-worker limit");
            var capture=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(x=>Anime.Parse(x)).ToList();
            var row=new Controls.CatalogRowView { DataContext=new CatalogRow(capture.Take(6).Select(x=>new AnimeCardModel(x)),false) };
            var controls=row.Children.Cast<Controls.AnimeCard>().ToArray(); row.DataContext=new CatalogRow(capture.Skip(6).Take(6).Select(x=>new AnimeCardModel(x)),false);
            Check(controls.Select((card,i)=>ReferenceEquals(card,row.Children[i]) && ((AnimeCardModel)card.DataContext).Id==capture[i+6].Id).All(value=>value),"Recycling recreated poster controls or retained stale anime data");
            checks.Add("Recycled rows preserve their poster-control instances and bind the new anime correctly");
            Locale.Current.Initialize(scratch); Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            window=new MainWindow(false,new LibraryStore(scratch),new HistoryStore(scratch)); window.LoadDesignCatalog(capture); window.SetDesignPaging(capture.Take(12),3);
            async Task Layout() { var size=new Size(1366,768); window.Measure(size); window.Arrange(new Rect(size)); var root=(FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); window.ApplyResponsive(size); root.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
            await Layout(); var catalog=(ListBox)window.FindName("Catalog"); var source=catalog.ItemsSource; var firstRow=catalog.Items[0]; var resets=0;
            ((INotifyCollectionChanged)source).CollectionChanged+=(_,e)=> { if(e.Action==NotifyCollectionChangedAction.Reset) resets++; };
            var response=new TaskCompletionSource<SearchPage>(); window.CatalogFetch=(_,_)=>response.Task;
            var pending=window.LoadNextForDiagnostics(); Check(ReferenceEquals(firstRow,catalog.Items[0]),"Showing a loading indicator rebuilt rows");
            response.SetResult(new SearchPage(capture.Skip(12).Take(12).ToList(),1,3,30)); await pending; await Layout();
            Check(ReferenceEquals(source,catalog.ItemsSource) && ReferenceEquals(firstRow,catalog.Items[0]) && resets==0 && catalog.Items.Cast<CatalogRow>().Sum(x=>x.Count)==24,"Appending a batch reset existing catalog rows");
            checks.Add("Appending pages preserves ItemsSource and existing row identity; loading indicator causes no reset");
            response=new(); window.CatalogFetch=(_,_)=>response.Task; pending=window.LoadNextForDiagnostics();
            ((ButtonBase)window.FindName("TrendingNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Layout();
            response.SetResult(new SearchPage(capture,2,3,30)); await pending;
            Check(catalog.Items.Cast<CatalogRow>().Sum(x=>x.Count)==30,"An inactive Explore request corrupted Trending");
            ((ButtonBase)window.FindName("ExploreNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Layout();
            Check(ReferenceEquals(source,catalog.ItemsSource) && catalog.Items.Cast<CatalogRow>().Sum(x=>x.Count)==24,"Navigation failed to restore the cached Explore view");
            checks.Add("Navigation restores cached browsing state and ignores late results from a different section");
            await Task.Delay(300);
            Check(Descendants<Controls.RoundedImage>(catalog).Count(image=>image.Source is BitmapSource)>=5,"Asynchronous real poster loading regressed to blank cards");
            checks.Add("Captured real artwork finishes decoding and renders after asynchronous loading");
            File.WriteAllText(Path.Combine(scratch,"history.json"),JsonSerializer.Serialize(Enumerable.Range(0,100).Select(i=>new WatchEntry("history-"+i,capture[i%capture.Count].Name,capture[i%capture.Count].Poster,"001","Episode 1",75,DateTime.UtcNow,1440,capture[i%capture.Count].Raw.Text("cover_uri",capture[i%capture.Count].Poster)))));
            ((ButtonBase)window.FindName("HistoryNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Layout();
            var historyList=(ListBox)window.FindName("HistoryCards"); var historyPanel=Descendants<VirtualizingStackPanel>(historyList).First();
            Check(historyPanel.Children.Count<15 && historyList.Items.OfType<ContinueRow>().Sum(row=>row.Count)==100,"History lost entries or failed to virtualize 100 watches");
            var preview=new RenderTargetBitmap(1366,768,96,96,PixelFormats.Pbgra32); preview.Render((FrameworkElement)window.Content); var previewEncoder=new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(preview)); using(var previewFile=File.Create(Path.Combine(directory,"v6-history-preview.png"))) previewEncoder.Save(previewFile);
            var historyScroll=Descendants<ScrollViewer>(historyList).First(); historyScroll.ScrollToVerticalOffset(700); await Layout();
            var header=(FrameworkElement)window.FindName("HistoryHeader");
            if(historyList.IsAncestorOf(header)) Check(header.TransformToAncestor(historyList).Transform(new Point()).Y<0,"History header no longer scrolls with its content");
            var savedHistoryOffset=historyScroll.VerticalOffset;
            ((ButtonBase)window.FindName("HomeNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Layout();
            ((ButtonBase)window.FindName("HistoryNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Layout();
            Check(Math.Abs(historyScroll.VerticalOffset-savedHistoryOffset)<1,"History scroll position was lost during navigation");
            checks.Add("Continue Watching virtualizes all 100 entries while retaining the scrolling header and card layout");
            Check(PlaybackHealth.Classify(default,new MediaStats(),true,VLCState.Playing)=="source-or-network-starvation","Network starvation classification failed");
            Check(PlaybackHealth.Classify(default,Counter(nameof(MediaStats.LostPictures),1),false,VLCState.Playing)=="decoder-or-output-pressure","Decoder-pressure classification failed");
            Check(PlaybackHealth.Classify(default,Counter(nameof(MediaStats.DemuxDiscontinuity),1),false,VLCState.Playing)=="source-timestamp-or-data-discontinuity","Source-timestamp classification failed");
            checks.Add("Playback diagnostics distinguish starvation, decoder/output pressure and timestamp discontinuities");
        } catch(Exception exception) { error=exception.ToString(); }
        finally { window?.Close(); Artwork.PreviewDirectory=null; Locale.Current.Dispose(); }
        File.WriteAllText(Path.Combine(directory,"v6-regression-checks.json"),JsonSerializer.Serialize(new { success=error is null,checks,error },new JsonSerializerOptions { WriteIndented=true }));
        return error is null?0:1;
    }
    private static MediaStats Counter(string field,int value)
    {
        var bytes=new byte[System.Runtime.InteropServices.Marshal.SizeOf<MediaStats>()];
        BitConverter.GetBytes(value).CopyTo(bytes,(int)System.Runtime.InteropServices.Marshal.OffsetOf<MediaStats>(field));
        return System.Runtime.InteropServices.MemoryMarshal.Read<MediaStats>(bytes);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T found) yield return found; foreach(var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class Images(byte[] content,int delay):HttpMessageHandler
    {
        public int Calls,MaxActive; private int active;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Interlocked.Increment(ref Calls); var current=Interlocked.Increment(ref active); int observed;
            do { observed=MaxActive; if(current<=observed) break; } while(Interlocked.CompareExchange(ref MaxActive,current,observed)!=observed);
            try { await Task.Delay(delay,ct); return new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(content) }; }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
