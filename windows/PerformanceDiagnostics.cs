using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Yoru;

// The same harness runs against the unoptimized V5 copy and V6. Hidden WPF
// composition cadence is a scheduling proxy, not a claim about monitor FPS.
internal static class PerformanceDiagnostics
{
    internal static async Task<int> Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        var output = Path.Combine(directory, "performance.json");
        var scratch = Path.Combine(Path.GetTempPath(), "yoru-perf-" + Guid.NewGuid().ToString("N"));
        var measures = new Dictionary<string, object>(); string? error = null;
        MainWindow? window = null;
        using var source = new HwndSource(new HwndSourceParameters("Hidden WPF performance profiling") { Width = 1366, Height = 768, WindowStyle = unchecked((int)0x80000000) });
        try
        {
            Locale.Current.Initialize(scratch); Artwork.PreviewDirectory = Path.Combine(directory, "design-assets");
            var records = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(x => Anime.Parse(x)).ToList();
            var large = Enumerable.Range(0, 5000).Select(i => records[i % records.Count] with { Id = "stress-" + i }).ToList();
            Directory.CreateDirectory(scratch);
            File.WriteAllText(Path.Combine(scratch,"history.json"),JsonSerializer.Serialize(large.Take(100).Select(anime=>new WatchEntry(anime.Id,anime.Name,anime.Poster,"001","Episode 1",75,DateTime.UtcNow,1440,anime.Raw.Text("cover_uri",anime.Poster)))));
            window = new MainWindow(false, new LibraryStore(scratch), new HistoryStore(scratch));
            var root = (FrameworkElement)window.Content; window.Content = null; source.RootVisual = root;
            void Layout() {
                window.ApplyResponsive(new Size(1366,768)); root.Measure(new Size(1366,768)); root.Arrange(new Rect(0,0,1366,768)); root.UpdateLayout();
                var list = (ListBox)window.FindName("Catalog"); var viewport = Descendant<ScrollViewer>(list);
                // Hidden hosting can suppress the first viewport-size notification.
                // Settle both versions to the intended V4 column count before sampling.
                if (viewport?.ViewportWidth > 1 && list.Items.Count > 1 && ((System.Collections.IList)list.Items[0]).Count != ResponsiveCardPanel.ColumnsFor(viewport.ViewportWidth)) {
                    typeof(MainWindow).GetMethod("ReflowCatalog",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[] { false }); root.UpdateLayout();
                }
            }
            var watch = Stopwatch.StartNew(); window.LoadDesignCatalog(large); Layout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            measures["initial5000Ms"] = watch.Elapsed.TotalMilliseconds;
            var catalog = (ListBox)window.FindName("Catalog");
            var samples = new List<double>(); var intervals = new List<double>(); var renderedIntervals = new List<double>();
            TimeSpan lastRender = default;
            EventHandler rendering = (_, args) => { var time = ((RenderingEventArgs)args).RenderingTime; if (lastRender != default && time > lastRender) renderedIntervals.Add((time-lastRender).TotalMilliseconds); lastRender = time; };
            CompositionTarget.Rendering += rendering;
            try
            {
                watch.Restart(); window.ShowDesignExplore(); Layout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                measures["firstExploreMs"] = watch.Elapsed.TotalMilliseconds;
                await Task.Delay(700); Layout();
                var scroll = Descendant<ScrollViewer>(catalog)!;
                measures["catalogViewport"] = new { width=scroll.ViewportWidth, height=scroll.ViewportHeight, actualWidth=catalog.ActualWidth, panelWidth=Descendant<ResponsiveCardPanel>(catalog)?.ActualWidth, cardsPerRow = catalog.Items.Cast<System.Collections.IList>().FirstOrDefault()?.Count, scrollWidth=scroll.ActualWidth };
                renderedIntervals.Clear(); lastRender=default;
                var allocated = GC.GetTotalAllocatedBytes(); var frameClock = Stopwatch.StartNew(); var prior = frameClock.Elapsed.TotalMilliseconds;
                for (int i = 0; i < 120; i++)
                {
                    watch.Restart(); scroll.ScrollToVerticalOffset((i * 107) % Math.Max(1, scroll.ExtentHeight - scroll.ViewportHeight));
                    Layout(); await Dispatcher.Yield(DispatcherPriority.Render); samples.Add(watch.Elapsed.TotalMilliseconds);
                    await Task.Delay(16); var now = frameClock.Elapsed.TotalMilliseconds; intervals.Add(now-prior); prior = now;
                }
                measures["scrollUiWorkMs"] = Summary(samples); measures["scrollLoopIntervalsMs"] = Summary(intervals);
                measures["scrollAllocatedBytes"] = GC.GetTotalAllocatedBytes() - allocated;
                measures["observedHiddenCompositionIntervalsMs"] = Summary(renderedIntervals);
                measures["observedHiddenCompositionCadenceHz"] = renderedIntervals.Count == 0 ? 0 : 1000 / renderedIntervals.Average();
                measures["realizedRows"] = Descendant<VirtualizingStackPanel>(catalog)?.Children.Count ?? -1;
                measures["realizedImages"] = Descendants<Image>(catalog).Count();
                measures["visibleDecodedPixelBytes"] = Descendants<Image>(catalog).Select(x => x.Source).OfType<BitmapSource>().Distinct().Sum(x => (long)x.PixelWidth*x.PixelHeight*4);
                measures["imageState"] = Descendants<Image>(catalog).Take(3).Select(image=>new { source=image.GetType().GetProperty("ArtworkSource")?.GetValue(image)?.ToString(), width=image.ActualWidth,height=image.ActualHeight,slot=LayoutInformation.GetLayoutSlot(image).ToString(),loaded=image.IsLoaded,visible=image.IsVisible,bitmap=image.Source?.GetType().Name }).ToArray();
                var cacheType=typeof(MainWindow).Assembly.GetType("Yoru.ArtworkCache"); var cache=cacheType?.GetProperty("Shared")?.GetValue(null); measures["artworkCache"] = cacheType?.GetMethod("Snapshot")?.Invoke(cache,null)??new {};

            }
            finally { CompositionTarget.Rendering -= rendering; }
            var navigation = new Dictionary<string, object>();
            foreach (var page in new[] { "HomeNav", "TrendingNav", "HistoryNav", "GenresNav", "SeasonalNav", "ListNav", "SettingsNav", "Explore" })
            {
                samples.Clear(); var allocations = GC.GetTotalAllocatedBytes();
                for (int i = 0; i < 5; i++)
                {
                    ((ButtonBase)window.FindName(page=="HomeNav"?"SettingsNav":"HomeNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Layout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    watch.Restart();
                    if (page == "Explore") window.ShowDesignExplore(); else ((ButtonBase)window.FindName(page)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Layout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); samples.Add(watch.Elapsed.TotalMilliseconds);
                }
                navigation[page] = new { latencyMs = Summary(samples), allocatedBytes = GC.GetTotalAllocatedBytes()-allocations };
            }
            measures["navigation"] = navigation;
            window.SetDesignPaging(large.Take(3000), 20); window.CatalogFetch = (_, number) => Task.FromResult(new SearchPage(large.Skip(3000+(number-1)*30).Take(30).ToList(),number,20,5000)); Layout();
            samples.Clear(); var appendAllocation = GC.GetTotalAllocatedBytes();
            for (int i = 0; i < 8; i++) { watch.Restart(); await window.LoadNextForDiagnostics(); Layout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); samples.Add(watch.Elapsed.TotalMilliseconds); }
            measures["append30To3000Ms"] = Summary(samples); measures["appendAllocatedBytes"] = GC.GetTotalAllocatedBytes()-appendAllocation;
            // Exercise exactly the properties consumed by card bindings without disk/network.
            var models = large.Select(x => new AnimeCardModel(x)).ToList(); var modelAlloc = GC.GetTotalAllocatedBytes(); watch.Restart(); long count=0;
            foreach (var model in models) for (int i=0;i<5;i++) count += model.Genres.Count + model.Metadata.Count + model.Rating.Length + model.Year.Length;
            measures["modelPropertyReadsMs"] = watch.Elapsed.TotalMilliseconds; measures["modelPropertyAllocatedBytes"] = GC.GetTotalAllocatedBytes()-modelAlloc; measures["modelChecksum"] = count;
            watch.Restart(); var snapshot = new RenderTargetBitmap(1366,768,96,96,PixelFormats.Pbgra32); snapshot.Render(root); measures["softwareSnapshotRenderMs"] = watch.Elapsed.TotalMilliseconds;
            measures["managedBytes"] = GC.GetTotalMemory(false); measures["managedCollectedBytes"] = GC.GetTotalMemory(true); using var process = Process.GetCurrentProcess(); measures["privateBytes"] = process.PrivateMemorySize64; measures["workingSetBytes"] = process.WorkingSet64;
            measures["wpfRenderTier"] = RenderCapability.Tier >> 16;
            measures["environment"] = new { dotnet = Environment.Version.ToString(), processors = Environment.ProcessorCount, logicalViewport = "1366x768", stressTitles = 5000, stressHistory = 100, note = "Synthetic IDs reuse 30 real captured records/artwork; hidden HwndSource, not visible monitor FPS. Baseline and V6 use the identical harness." };
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { source.RootVisual = null; window?.Close(); Artwork.PreviewDirectory = null; Locale.Current.Dispose(); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { success = error is null, measures, error }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
    private static object Summary(List<double> data) { var sorted = data.Order().ToArray(); return new { count = sorted.Length, mean = sorted.Length == 0 ? 0 : sorted.Average(), median = sorted.Length == 0 ? 0 : sorted[sorted.Length/2], p95 = sorted.Length == 0 ? 0 : sorted[(int)((sorted.Length-1)*0.95)], max = sorted.Length == 0 ? 0 : sorted[^1] }; }
    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject => Descendants<T>(root).FirstOrDefault();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T item) yield return item; foreach(var nested in Descendants<T>(child)) yield return nested; } }
}
