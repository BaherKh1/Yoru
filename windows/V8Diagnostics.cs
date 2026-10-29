using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

internal static class V8Diagnostics
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr value);
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory); var checks=new List<string>(); string? error=null; MainWindow? window=null;
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-brand-check-"+Guid.NewGuid().ToString("N"));
        using var host=new HwndSource(new HwndSourceParameters("YORU hidden branding checks") { Width=1920,Height=1080,WindowStyle=unchecked((int)0x80000000) });
        try {
            Check(typeof(App).Assembly.GetName().Name=="YORU" && Path.GetFileName(Environment.ProcessPath)=="YORU.exe","Assembly or executable name is not YORU");
            var metadata=FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
            Check(metadata.ProductName=="YORU" && metadata.FileDescription=="YORU" && metadata.ProductVersion=="1.0.0","Windows executable metadata is not YORU V8");
            checks.Add("YORU.exe / YORU.dll and Windows product, description and V8 version metadata");
            Check(GetCurrentProcessExplicitAppUserModelID(out var id)==0,"Taskbar identity not available");
            try { Check(Marshal.PtrToStringUni(id)==AppIdentity.TaskbarId,"Old taskbar identity"); } finally { Marshal.FreeCoTaskMem(id); }
            checks.Add("Shell taskbar grouping identity is YORU.Desktop");
            var logo=new RenderTargetBitmap(1080,1080,96,96,PixelFormats.Pbgra32);
            var logoVisual=new DrawingVisual(); using(var context=logoVisual.RenderOpen()) context.DrawImage(AppIcons.Logo,new Rect(0,0,1080,1080)); logo.Render(logoVisual);
            var converted=new FormatConvertedBitmap(logo,PixelFormats.Bgra32,null,0); var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4]; converted.CopyPixels(pixels,converted.PixelWidth*4,0);
            byte Alpha(int x,int y)=>pixels[(y*converted.PixelWidth+x)*4+3];
            Check(AppIcons.Logo.IsFrozen && logo.PixelWidth==logo.PixelHeight,"Logo is not a frozen square raster");
            foreach(var (x,y) in new[]{(0,0),(converted.PixelWidth-1,0),(0,converted.PixelHeight-1),(converted.PixelWidth-1,converted.PixelHeight-1)}) Check(Alpha(x,y)==0,"Logo outer corner background is not transparent");
            Check(Alpha(converted.PixelWidth/2,converted.PixelHeight/2)>240,"Squircle interior was removed");
            checks.Add("Provided squircle logo has actual transparent outer corners and an intact opaque interior");
            foreach(var pair in L.Entries) foreach(var value in pair.Value) Check(!value.Contains("old-brand",StringComparison.OrdinalIgnoreCase) && !value.Contains("ويتشر"),"Old visible branding in translation "+pair.Key);
            checks.Add("All English/Arabic translation values are free of old brand text");
            var records=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(node=>Anime.Parse(node)).ToList();
            var legacy=Path.Combine(scratch,"Yoru"); Directory.CreateDirectory(legacy);
            var anime=records[0]; var episode=Episode.Parse(new JsonObject { ["doc_id"]="001",["name"]="Episode 1" });
            new LanguagePreferences(legacy).Save("ar"); new HistoryStore(legacy).Save(anime,episode,123.5,1440);
            File.WriteAllText(Path.Combine(legacy,"my-list.json"),JsonSerializer.Serialize(new[]{anime}));
            var migrated=AppStorage.Resolve(scratch); AppStorage.DiagnosticRoot=migrated;
            Locale.Current.Initialize();
            Check(L.Language=="ar" && new HistoryStore().Position(anime.Id,episode.Id)==123.5 && new LibraryStore().Contains(anime.Id),"Default stores cannot access the migrated user library");
            Artwork.PreviewDirectory=Path.Combine(directory,"design-assets"); window=new MainWindow(false); window.LoadDesignCatalog(records);
            var root=(FrameworkElement)window.Content; host.RootVisual=root;
            foreach(var lang in new[]{"en","ar"}) foreach(var size in new[]{new Size(1366,768),new Size(1920,1080)}) {
                Locale.Current.SetForDiagnostics(lang);
                window.Width=size.Width; window.Height=size.Height; window.Measure(size); window.Arrange(new Rect(size)); root.Measure(size); root.Arrange(new Rect(size)); window.ApplyResponsive(size); root.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(250); root.UpdateLayout();
                Check(window.Title=="YORU" && L.T("Brand")=="YORU" && L.T("BrandPlayer")=="YORU","Localized window/header/player branding is wrong");
                Check(((Image)window.FindName("BrandLogo")).Source==AppIcons.Mark && ((Image)window.FindName("AboutLogo")).Source==AppIcons.Logo,"Header/About logo is not the appropriate YORU artwork");
                Check(((Image)window.FindName("BrandLogo")).Stretch==Stretch.Uniform,"Logo is stretched");
                Check(((ItemsControl)window.FindName("HomeHistory")).Items.Count==1,"Migrated Continue Watching is missing from Home");
                Save(root,Path.Combine(directory,$"v8-home-{lang}-{size.Width}x{size.Height}.png"),size);
            }
            checks.Add("EN/AR at 1366x768 and 1920x1080: window/header/player identity, shared undistorted header/About logo, migrated Home history");
            ((ButtonBase)window.FindName("ListNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); root.UpdateLayout();
            Check(((ListBox)window.FindName("Catalog")).Items.Cast<CatalogRow>().SelectMany(row=>row).Any(card=>card.Id==anime.Id),"Migrated favorite is absent from My List");
            ((ButtonBase)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); root.UpdateLayout(); await Task.Delay(100);
            Save(root,Path.Combine(directory,"v8-settings-about.png"),new Size(1920,1080));
            Check(new HistoryStore().Entries.Single().Duration==1440 && new LanguagePreferences().Read()=="ar","UI startup lost migrated duration or persisted language");
            checks.Add("Normal MainWindow default stores load migrated favorites, history position/duration, saved language and Settings/About");
        } catch(Exception exception) { error=exception.ToString(); }
        finally { host.RootVisual=null; window?.Close(); Artwork.PreviewDirectory=null; AppStorage.DiagnosticRoot=null; Locale.Current.Dispose(); }
        File.WriteAllText(Path.Combine(directory,"v8-branding-checks.json"),JsonSerializer.Serialize(new { success=error is null,checks,error },new JsonSerializerOptions { WriteIndented=true }));
        return error is null?0:1;
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Save(Visual root,string path,Size size) { var image=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32); image.Render(root); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file=File.Create(path); encoder.Save(file); }
}
