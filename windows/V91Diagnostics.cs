using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
namespace AnimeWitcherDesktop;
internal static class V91Diagnostics
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks=new List<string>(); string? error=null; PlayerWindow? player=null;
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-v91-"+Guid.NewGuid().ToString("N"));
        using var host=new HwndSource(new HwndSourceParameters("Hidden V9.1 badge checks") { Width=1920,Height=1080,WindowStyle=unchecked((int)0x80000000) });
        void Check(bool pass,string message) { if(!pass) throw new Exception(message); }
        async Task Pump() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
        try {
            Locale.Current.Initialize(scratch);
            Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var records=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(record=>Anime.Parse(record)).ToArray();
            foreach(var language in new[]{"en","ar"}) {
                Locale.Current.SetForDiagnostics(language);
                foreach(var width in new[]{110d,150d,190d,240d}) {
                    foreach(var genre in new[]{"Action","Slice of Life","\u0645\u063a\u0627\u0645\u0631\u0627\u062a \u0648\u062e\u064a\u0627\u0644 \u0639\u0644\u0645\u064a"}) {
                        var raw=new JsonObject { ["details"]=new JsonObject {["year"]="2024"}, ["tags"]=new JsonArray(genre) };
                        var card=new Controls.AnimeCard { DataContext=new AnimeCardModel(new Anime("badge","Anime", "", "", "",raw)),FlowDirection=Locale.Current.Direction };
                        host.RootVisual=card; var size=new Size(width,width+62);card.Measure(size);card.Arrange(new Rect(size));card.UpdateLayout();await Pump();
                        var year=(Border)card.FindName("YearBadge");var category=(Border)card.FindName("GenreBadge");var row=(Grid)card.FindName("BadgeRow");
                        Check(year.Padding==category.Padding && year.CornerRadius==category.CornerRadius && Math.Abs(year.ActualHeight-category.ActualHeight)<.1,"Badges differ in padding, rounding or height");
                        Check(((TextBlock)year.Child).FontSize==((TextBlock)category.Child).FontSize,"Badge text sizes differ");
                        Check(category.HorizontalAlignment==HorizontalAlignment.Left,"Genre stretches");
                        Check(category.ActualWidth <= category.DesiredSize.Width+.1,"Genre fills unused width");
                        var bounds=category.TransformToAncestor(row).TransformBounds(new Rect(category.RenderSize));
                        Check(bounds.Left>=-.1 && bounds.Right<=row.ActualWidth+.1,"Genre escapes card bounds");
                        Check(Math.Abs(year.TranslatePoint(new Point(0,0),row).Y-category.TranslatePoint(new Point(0,0),row).Y)<.1,"Badges are not aligned");
                        var poster=(Border)card.FindName("PosterBody");Check(Math.Abs(poster.ActualHeight-width)<.1,"Poster dimensions changed");
                        if(width==190 && genre=="Action") Save(card,Path.Combine(directory,$"v91-badges-{language}.png"));
                    }
                }
                checks.Add($"{language}: compact badges fit 110/150/190/240px cards, matching height/padding/font/corners and unchanged poster geometry");
            }
            host.RootVisual=null;
            var fixture=Path.Combine(AppContext.BaseDirectory,"..","tests","performance-av-1080p.mp4");
            player=new PlayerWindow(new VideoServer("Local fixture","1080p",new Uri(Path.GetFullPath(fixture)).AbsoluteUri,true,false),"Jujutsu Kaisen \u2014 Episode 1 / \u546a\u8853\u5efb\u6226",0) { ShowActivated=false,ShowInTaskbar=false,Left=-30000,Top=-30000 };
            // Cloak only the diagnostic HWND: exercise real native chrome/state without covering the user's desktop.
            var hwnd=new WindowInteropHelper(player).EnsureHandle();var cloak=1;_ = DwmSetWindowAttribute(hwnd,13,ref cloak,sizeof(int));
            player.Show();await Pump();
            var native=(LibVLCSharp.Shared.MediaPlayer)typeof(PlayerWindow).GetField("player",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;
            var playbackWait=System.Diagnostics.Stopwatch.StartNew();
            while(native.Time<400 && playbackWait.Elapsed.TotalSeconds<10) await Pump();
            var media=(LibVLCSharp.Shared.Media)typeof(PlayerWindow).GetField("currentMedia",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;
            Check(native.Time>=400 && media.Statistics.DecodedVideo>0 && media.Statistics.DecodedAudio>0,"Native player failed to decode local audio/video with custom chrome");
            checks.Add("Custom-chrome PlayerWindow decodes and advances the local H.264/AAC fixture through the existing VideoView");
            var frame=(Border)player.Content;var layout=(Grid)frame.Child;var bar=(Controls.PlayerTitleBar)layout.Children[0];
            Check(player.WindowStyle==WindowStyle.None && !player.AllowsTransparency,"Player still has system chrome or transparent native video");
            Check(WindowChrome.GetWindowChrome(player)?.CaptionHeight==48,"Native drag/resize chrome missing");
            Check(((Grid)bar.Content).Background.ToString()=="#FF090C14","Incorrect title bar color");
            Check(frame.BorderBrush.ToString()=="#FF302744" && frame.CornerRadius.TopLeft==8,"Dark purple frame missing");
            foreach(var size in new[]{new Size(700,460),new Size(1100,720),new Size(1366,768),new Size(1600,900),new Size(1920,1080)}) {
                player.Width=size.Width;player.Height=size.Height;await Pump();player.UpdateLayout();
                var close=(Button)bar.FindName("CloseButton");var max=(Button)bar.FindName("MaximizeButton");var min=(Button)bar.FindName("MinimizeButton");
                Check(close.ActualWidth==46 && max.ActualWidth==46 && min.ActualWidth==46,"Caption controls are cropped");
                var caption=(TextBlock)bar.FindName("Caption");var textBounds=caption.TransformToAncestor(bar).TransformBounds(new Rect(caption.RenderSize));var controlsBounds=min.TransformToAncestor(bar).TransformBounds(new Rect(min.RenderSize));
                Check(textBounds.Right<=controlsBounds.Left,"Title overlaps window buttons");
            }
            player.Width=1100;player.Height=720;await Pump(); Save(bar,Path.Combine(directory,"v91-player-titlebar.png"));
            checks.Add("Native player: custom #090C14 title bar, purple rounded frame, draggable/resizable chrome and uncropped controls at five sizes");
            foreach(var language in new[]{"en","ar"}) {
                Locale.Current.SetForDiagnostics(language);await Pump();
                Check(System.Windows.Automation.AutomationProperties.GetName((Button)bar.FindName("CloseButton"))==L.T("Close"),"Caption controls are not translated");
                Check(bar.FlowDirection==FlowDirection.LeftToRight,"Window controls should stay on the standard right edge");
            }
            checks.Add("EN/AR caption labels update immediately; window controls keep consistent right-edge placement");
            var maximize=(Button)bar.FindName("MaximizeButton");maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            Check(player.WindowState==WindowState.Maximized && frame.Margin.Left==0,"Maximize did not respect the work area");
            var restoreGlyph=((System.Windows.Shapes.Path)bar.FindName("MaximizeGlyph")).Data.ToString();
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            Check(player.WindowState==WindowState.Normal && frame.Margin.Left==0,"Restore failed");
            Check(((System.Windows.Shapes.Path)bar.FindName("MaximizeGlyph")).Data.ToString()!=restoreGlyph,"Maximize/restore icon does not toggle");
            ((Button)bar.FindName("MinimizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();Check(player.WindowState==WindowState.Minimized,"Minimize failed");
            SystemCommands.RestoreWindow(player);await Pump();
            checks.Add("Custom buttons minimize, maximize and restore, with correct icon and frame insets");
            foreach(var previous in new[]{WindowState.Normal,WindowState.Maximized}) {
                player.WindowState=previous;await Pump();player.ToggleFullScreen();await Pump();
                Check(bar.Visibility==Visibility.Collapsed && layout.RowDefinitions[0].ActualHeight==0 && frame.BorderThickness.Left==0 && frame.Margin.Left==0,"Fullscreen leaves caption space or borders");
                Check(WindowChrome.GetWindowChrome(player)==null && player.ResizeMode==ResizeMode.NoResize,"Fullscreen still has resize handles");
                player.ToggleFullScreen();await Pump();
                Check(player.WindowState==previous && player.WindowStyle==WindowStyle.None && bar.Visibility==Visibility.Visible && WindowChrome.GetWindowChrome(player)!=null,"Fullscreen exit loses custom chrome or previous state");
            }
            checks.Add("Fullscreen removes title/borders and restores normal/maximized custom chrome without reintroducing a system title bar");
            var closed=new TaskCompletionSource();player.PlaybackClosed+=()=>closed.TrySetResult();
            ((Button)bar.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));player=null;
            checks.Add("Custom close button completes native player disposal and normal playback-close notifications");
        }
        catch(Exception ex) { error=ex.ToString(); }
        finally { host.RootVisual=null;player?.Close();Locale.Current.Dispose(); }
        File.WriteAllText(Path.Combine(directory,"v91-ui-checks.json"),JsonSerializer.Serialize(new {success=error==null,checks,error},new JsonSerializerOptions{WriteIndented=true}));
        return error==null?0:1;
    }
    private static void Save(FrameworkElement element,string path)
    {
        var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
    }
}
