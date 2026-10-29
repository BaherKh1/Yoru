using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AnimeWitcherDesktop;

internal static class V9Diagnostics
{
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory); var checks=new List<string>(); string? error=null;
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-startup-check-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        MainWindow? window=null;
        using var host=new HwndSource(new HwndSourceParameters("Hidden V9 startup checks"){Width=1366,Height=768,WindowStyle=unchecked((int)0x80000000)});
        try {
            Locale.Current.Initialize(scratch); Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var records=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(record=>Anime.Parse(record)).ToList();
            var page=new SearchPage(records,0,235,7048);
            
            using(var cache=new ArtworkCache(Path.Combine(scratch,"artwork"))) {
                var missing="https://fixture.invalid/never-downloaded.jpg";
                Check(!await cache.PrimeDisk(missing,256,256),"Priming attempted to use missing artwork");
                var file=Directory.GetFiles(Path.Combine(directory,"design-assets"),"*.img").First();
                Check(await cache.PrimeDisk(file,256,256) && cache.TryReady(file,128,128,out var ready),"Cached artwork was not primed at sufficient resolution");
                var image=new Controls.RoundedImage {Cache=cache,ArtworkSource=file,Width=100,Height=100};
                host.RootVisual=image;image.Measure(new Size(100,100));image.Arrange(new Rect(0,0,100,100));image.UpdateLayout();
                Check(image.Source is System.Windows.Media.Imaging.BitmapSource,"Primed artwork did not display during first layout");
                using var stats=JsonDocument.Parse(JsonSerializer.Serialize(cache.Snapshot()));
                Check(stats.RootElement.GetProperty("downloads").GetInt64()==0,"Disk-only priming made a network request");host.RootVisual=null;
            }
            
            checks.Add("Artwork priming is disk-only, bounded by the shared cache, and paints a ready image in the initial layout");
            async Task Layout() {var size=new Size(1366,768); window!.Measure(size);window.Arrange(new Rect(size));var root=(FrameworkElement)window.Content;root.Measure(size);root.Arrange(new Rect(size));window.ApplyResponsive(size);root.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);root.UpdateLayout();}
            void Close() {host.RootVisual=null;window?.Close();window=null;}
            MainWindow Create(StartupCatalogStore store,Func<CancellationToken,Task<SearchPage>> fetch) {var result=new MainWindow(false,new LibraryStore(scratch),new HistoryStore(scratch),store,fetch);host.RootVisual=(System.Windows.Media.Visual)result.Content;return result;}
            var coldStore=new StartupCatalogStore(Path.Combine(scratch,"cold")); var response=new TaskCompletionSource<SearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
            window=Create(coldStore,ct=>{calls++;return response.Task.WaitAsync(ct);});
            
            var pending=window.StartHomeForDiagnostics(); var duplicate=window.StartHomeForDiagnostics(); await Layout();
            Check(ReferenceEquals(pending,duplicate) && calls==1,"Repeated Loaded/start calls duplicated the network fetch");
            Check(((FrameworkElement)window.FindName("HeroSkeleton")).Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("HeroLive")).Visibility==Visibility.Collapsed && ((ItemsControl)window.FindName("HomePopularSkeleton")).Items.Count==8,"Cold first frame contains empty live controls rather than aligned skeletons");
            
            Locale.Current.SetForDiagnostics("ar");await Layout();
            Check(((FrameworkElement)window.FindName("HeroSkeleton")).Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("HeroLive")).Visibility==Visibility.Collapsed,"RTL cold state exposed incomplete live controls");
            Locale.Current.SetForDiagnostics("en");
            response.SetResult(page);await pending;await Layout();
            Check(window.Featured is not null && ((ItemsControl)window.FindName("HomePopular")).Items.Count==8 && window.HomeCommitCount==1,"Cold Home metadata did not commit as one batch");
            Check(!((FrameworkElement)window.FindName("Hero")).HasAnimatedProperties,"Initial Home commit added a fade delay");
            Check(StartupCatalogStore.Equivalent(await coldStore.Read(),page),"Successful startup did not persist the snapshot");
            checks.Add("Cold: first-frame skeletons, hidden live buttons, exactly one shared fetch/commit, no first-load fade, persistent snapshot"); Close();

            response=new(TaskCreationOptions.RunContinuationsAsynchronously);calls=0;
            window=Create(coldStore,ct=>{calls++;return response.Task.WaitAsync(ct);});pending=window.StartHomeForDiagnostics();await Layout();
            Check(window.Featured is not null && ((FrameworkElement)window.FindName("HeroLive")).Visibility==Visibility.Visible && calls==1 && !pending.IsCompleted,"Warm snapshot was not visible before background refresh");
            var source=((ItemsControl)window.FindName("HomePopular")).ItemsSource;var featured=window.Featured;
            response.SetResult(page);await pending;await Layout();
            Check(window.HomeCommitCount==1 && ReferenceEquals(source,((ItemsControl)window.FindName("HomePopular")).ItemsSource) && ReferenceEquals(featured,window.Featured),"Unchanged response rebuilt cards or featured data");
            
            checks.Add("Warm: cached data before refresh completion; unchanged metadata preserves card source/feature identity and avoids a second commit");Close();

            window=Create(coldStore,_=>Task.FromException<SearchPage>(new System.Net.Http.HttpRequestException("Offline")));await window.StartHomeForDiagnostics();await Layout();
            Check(window.Featured is not null && ((FrameworkElement)window.FindName("EmptyState")).Visibility==Visibility.Collapsed && StartupCatalogStore.Equivalent(await coldStore.Read(),page),"Offline refresh hid or overwrote last-known-good Home data");
            checks.Add("Warm offline: cached Home remains available and the last successful snapshot is preserved");Close();

            response=new(TaskCreationOptions.RunContinuationsAsynchronously);
            window=Create(coldStore,ct=>response.Task.WaitAsync(ct));pending=window.StartHomeForDiagnostics();await Layout();
            window.SetDesignPaging(records,235);await Layout();
            var catalog=(ListBox)window.FindName("Catalog");source=catalog.ItemsSource;
            var changed=records.Select((anime,index)=>index==0?anime with {Name=anime.Name+" updated"}:anime).ToList();
            response.SetResult(page with {Items=changed});await pending;await Layout();
            Check(window.HomeCommitCount==2 && ((FrameworkElement)window.FindName("ExplorePage")).Visibility==Visibility.Visible && ReferenceEquals(catalog.ItemsSource,source),"Late Home refresh replaced active navigation or catalog rows");
            Check(((IEnumerable<AnimeCardModel>)((ItemsControl)window.FindName("HomePopular")).ItemsSource).Any(card=>card.Name.EndsWith(" updated")),"Changed snapshot was not applied to Home");
            checks.Add("Changed refresh updates Home once while preserving the active Explore view and row source");Close();

            var corruptDir=Path.Combine(scratch,"corrupt");Directory.CreateDirectory(corruptDir);File.WriteAllText(Path.Combine(corruptDir,"home-catalog-v9.json"),"broken");
            window=Create(new StartupCatalogStore(corruptDir),_=>Task.FromException<SearchPage>(new System.Net.Http.HttpRequestException("Offline")));await window.StartHomeForDiagnostics();await Layout();
            Check(window.Featured is null && ((FrameworkElement)window.FindName("HeroLive")).Visibility==Visibility.Collapsed && ((FrameworkElement)window.FindName("EmptyState")).Visibility==Visibility.Visible,"Corrupt cache / cold failure exposed invalid actions or no error state");
            checks.Add("Corrupt/cold offline: safe fallback skeleton and clear error state; invalid featured actions stay hidden");Close();

            response=new(TaskCreationOptions.RunContinuationsAsynchronously);window=Create(coldStore,ct=>response.Task.WaitAsync(ct));pending=window.StartHomeForDiagnostics();await Layout();var closedWindow=window;var before=window.HomeCommitCount;
            Close();response.TrySetResult(page);await pending;
            Check(closedWindow.HomeCommitCount==before,"Closed window accepted a late startup response");
            checks.Add("Closing cancels startup consumers and prevents late UI commits");
        } catch(Exception exception) {error=exception.ToString();}
        finally {host.RootVisual=null;window?.Close();Artwork.PreviewDirectory=null;Locale.Current.Dispose();}
        File.WriteAllText(Path.Combine(directory,"v9-startup-checks.json"),JsonSerializer.Serialize(new {success=error is null,checks,error},new JsonSerializerOptions {WriteIndented=true}));
        return error is null?0:1;
    }
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
}
