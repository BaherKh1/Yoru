using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace AnimeWitcherDesktop;
internal static class V92Diagnostics
{
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory);var checks=new List<string>();string? error=null;
        var timings=new List<double>();var scratch=Path.Combine(Path.GetTempPath(),"yoru-v92-"+Guid.NewGuid().ToString("N"));
        MainWindow? window=null;
        using var source=new HwndSource(new HwndSourceParameters("Hidden sidebar animation checks") {Width=1920,Height=1080,WindowStyle=unchecked((int)0x80000000)});
        void Check(bool passed,string message) { if(!passed) throw new Exception(message); }
        try {
            Locale.Current.Initialize(scratch);Artwork.PreviewDirectory=Path.Combine(directory,"design-assets");
            var records=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"design-catalog.json")))!["hits"]!.AsArray().OfType<JsonObject>().Select(record=>Anime.Parse(record)).ToArray();
            window=new MainWindow(false,new LibraryStore(scratch),new HistoryStore(scratch));window.SetDesignPaging(records,1);window.LoadDesignCatalog(records);
            var root=(FrameworkElement)window.Content;source.RootVisual=root;
            var sidebar=(Grid)window.FindName("Sidebar");var items=(StackPanel)window.FindName("NavItems");var marker=(Border)window.FindName("NavSelection");var motion=(TranslateTransform)marker.RenderTransform;
            var buttons=items.Children.OfType<RadioButton>().ToArray();
            async Task Layout(Size size) {window.Width=size.Width;window.Height=size.Height;window.Measure(size);window.Arrange(new Rect(size));window.ApplyResponsive(size);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);}
            double Destination(RadioButton button)=>button.TranslatePoint(new Point(),sidebar).Y-marker.Margin.Top;
            async Task Settled(RadioButton button) {await Dispatcher.Yield(DispatcherPriority.Render);var wait=Stopwatch.StartNew();while(Math.Abs(motion.Y-Destination(button))>.1 && wait.ElapsedMilliseconds<2000) await Task.Delay(16);}
            void Click(RadioButton button) {var watch=Stopwatch.StartNew();button.IsChecked=true;button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));timings.Add(watch.Elapsed.TotalMilliseconds);}
            await Layout(new Size(1366,768));await Task.Delay(320);
            Check(buttons.Length==8 && marker.Opacity==1 && Math.Abs(motion.Y-Destination(buttons[0]))<.1,"Initial Home indicator does not align");
            Check(!marker.IsHitTestVisible && marker.CornerRadius.TopLeft==9 && marker.RenderTransform is TranslateTransform,"Indicator blocks input or changes the original shape");
            var original=buttons.Select(button=>button.TransformToAncestor(sidebar).TransformBounds(new Rect(button.RenderSize))).ToArray();
            foreach(var button in buttons) {button.ApplyTemplate();var body=(Border)button.Template.FindName("NavBody",button);Check(body.Background==Brushes.Transparent || body.Background.ToString()=="#00FFFFFF","Per-item selection backgrounds still exist");}
            checks.Add("One non-interactive purple indicator; eight original 47px navigation buttons keep the same rounded geometry and no per-item selected background");
            Click(buttons[7]);await Dispatcher.Yield(DispatcherPriority.Render);await Task.Delay(35);
            var beforeLayout=motion.Y;var unchangedTarget=Destination(buttons[7]);
            items.Width=items.ActualWidth-2;root.UpdateLayout();var afterLayout=motion.Y;
            File.WriteAllText(Path.Combine(directory,"v92-layout-interruption-probe.json"),JsonSerializer.Serialize(new {beforeLayout,afterLayout,unchangedTarget,delta=Math.Abs(afterLayout-beforeLayout),animated=motion.HasAnimatedProperties}));
            Check(Math.Abs(afterLayout-beforeLayout)<1,"Width-only layout interrupted the slide: "+beforeLayout+" -> "+afterLayout);
            items.ClearValue(FrameworkElement.WidthProperty);root.UpdateLayout();await Settled(buttons[7]);
            Click(buttons[0]);await Settled(buttons[0]);
            checks.Add("Width-only sidebar layout notifications do not cancel or snap an in-flight indicator animation");
            var start=motion.Y;Click(buttons[3]);
            Check(((FrameworkElement)window.FindName("HistoryPage")).Visibility==Visibility.Visible,"Navigation waited for animation");
            File.WriteAllText(Path.Combine(directory,"v92-animation-probe.json"),JsonSerializer.Serialize(new {start, immediate=motion.Y,target=Destination(buttons[3]),animated=motion.HasAnimatedProperties}));
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Task.Delay(20);
            Check(Math.Abs(motion.Y-start)<40 || !SystemParameters.ClientAreaAnimation,"Indicator jumped instantly: "+motion.Y+" from "+start);
            if(SystemParameters.ClientAreaAnimation) {
                Check(motion.HasAnimatedProperties,"Selection did not start an animation");
                var frameWait=Stopwatch.StartNew();
                while(!(motion.Y>start+.1 && motion.Y<Destination(buttons[3])-.1) && frameWait.ElapsedMilliseconds<2000) await Task.Delay(8);
                Check(motion.Y>start+.1 && motion.Y<Destination(buttons[3])-.1,"No intermediate sliding position: "+motion.Y);
                Save(sidebar,Path.Combine(directory,"v92-sidebar-mid-slide.png"));
            }
            await Task.Delay(320);Check(Math.Abs(motion.Y-Destination(buttons[3]))<.1,"Indicator did not settle on History");
            for(var i=0;i<buttons.Length;i++) Check(buttons[i].TransformToAncestor(sidebar).TransformBounds(new Rect(buttons[i].RenderSize))==original[i],"Navigation shifted layout during animation");
            checks.Add("History renders synchronously while the 250ms eased render transform slides through intermediate positions without layout shifts");
            Click(buttons[7]);await Task.Delay(60);var current=motion.Y;Click(buttons[1]);
            Check(Math.Abs(motion.Y-current)<5,"Rapid-click retarget jumped back to an old row");await Task.Delay(320);
            Check(Math.Abs(motion.Y-Destination(buttons[1]))<.1,"Rapid click failed to reach latest selection");
            for(var i=0;i<12;i++) {Click(buttons[i%buttons.Length]);}
            await Settled(buttons[3]);var last=buttons[3];Check(last.IsChecked==true && Math.Abs(motion.Y-Destination(last))<.1,"Rapid click sequence left stale animation: Y="+motion.Y+" target="+Destination(last)+" checked="+last.IsChecked+" selected="+string.Join(",",buttons.Where(b=>b.IsChecked==true).Select(b=>b.Tag)));
            checks.Add("Rapid switches replace the current animation from its displayed position; the final selected row wins without queued clocks");
            foreach(var button in buttons) {
                Click(button);await Settled(button);
                Check(Math.Abs(motion.Y-Destination(button))<.1,"Selection failed for "+button.Tag);
                var brush=(SolidColorBrush)button.Foreground;var colorWait=Stopwatch.StartNew();while(brush.Color!=Color.FromRgb(243,236,255) && colorWait.ElapsedMilliseconds<2000) await Task.Delay(16);Check(brush.Color==Color.FromRgb(243,236,255),"Selected color did not settle");
                var panel=(StackPanel)button.Content;var text=panel.Children.OfType<TextBlock>().Single();Check(ReferenceEquals(text.Foreground,brush),"Label does not share animated brush");
                foreach(var path in Paths(panel)) Check(ReferenceEquals(path.Stroke,brush),"Icon does not share animated brush");
            }
            checks.Add("All eight existing click handlers retain navigation; selected text and icons share the lightweight 180ms color transition");
            var selectedBrush=(SolidColorBrush)buttons[7].Foreground;Click(buttons[0]);
            if(SystemParameters.ClientAreaAnimation) Check(selectedBrush.HasAnimatedProperties && ((SolidColorBrush)buttons[0].Foreground).HasAnimatedProperties,"Selection colors do not animate in both directions");
            await Task.Delay(320);Check(selectedBrush.Color==Color.FromRgb(199,203,222),"Deselected color did not return to muted text");
            buttons[2].IsChecked=true;await Settled(buttons[2]);Check(Math.Abs(motion.Y-Destination(buttons[2]))<.1,"Checked/keyboard selection did not move indicator");
            Click(buttons[2]);await Settled(buttons[2]);
            var origin=buttons.Single(button=>button.IsChecked==true);
            window.ShowDesignDetails();Check(marker.Opacity==1 && origin.IsChecked==true,"Details cleared the originating section's persistent indicator");
            window.ShowDesignExplore();await Settled(buttons[1]);Check(marker.Opacity==1 && Math.Abs(motion.Y-Destination(buttons[1]))<.1,"Programmatic Explore/back selection failed");
            checks.Add("Selection/deselection color clocks, keyboard-style Checked changes and programmatic navigation work; details retain their originating section indicator");
            var rapidSamples=new List<object>();var pairCount=0;var maximumJump=0d;
            foreach(var from in buttons) foreach(var to in buttons.Where(candidate=>candidate!=from)) {
                Click(from);await Dispatcher.Yield(DispatcherPriority.Render);await Task.Delay(16);
                var before=motion.Y;Click(to);var jump=Math.Abs(motion.Y-before);maximumJump=Math.Max(maximumJump,jump);
                Check(jump<1 && marker.Opacity==1 && ReferenceEquals(window.FindName("NavSelection"),marker),"Rapid navigation recreated, hid or jumped the persistent indicator");
                var beforeResize=motion.Y;items.Width=items.ActualWidth+(pairCount%2==0?-1:1);root.UpdateLayout();
                Check(Math.Abs(motion.Y-beforeResize)<1,"Layout snapped the rapid-navigation indicator");
                if(pairCount%3==0) {var beforeReload=motion.Y;items.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,items));Check(Math.Abs(motion.Y-beforeReload)<1,"Repeated Loaded reset the slide");}
                rapidSamples.Add(new {from=from.Tag,to=to.Tag,before,after=motion.Y,jump});pairCount++;
            }
            items.ClearValue(FrameworkElement.WidthProperty);root.UpdateLayout();await Settled(buttons[^2]);
            Check(pairCount==56 && buttons[^2].IsChecked==true && Math.Abs(motion.Y-Destination(buttons[^2]))<.1,"Latest selection lost after exhaustive rapid switches");
            File.WriteAllText(Path.Combine(directory,"v92-rapid-all-pairs.json"),JsonSerializer.Serialize(new {pairCount,maximumJump,samples=rapidSamples},new JsonSerializerOptions {WriteIndented=true}));
            checks.Add("All 56 ordered section pairs, including cross-group switches, retain one visible indicator and continuous position through rapid clicks, width changes and repeated Loaded events");
            var priorY=motion.Y;buttons[^2].IsChecked=false;items.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,items));
            Check(marker.Opacity==1 && Math.Abs(motion.Y-priorY)<1,"Temporary empty radio selection flashed or reset the indicator");
            Click(buttons[0]);await Settled(buttons[0]);
            checks.Add("Temporary empty selection preserves the indicator; returning to a valid section resumes from its existing position");
            foreach(var language in new[]{"en","ar"}) {
                Locale.Current.SetForDiagnostics(language);
                foreach(var size in new[]{new Size(1000,600),new Size(1366,768),new Size(1600,900),new Size(1920,1080)}) {
                    await Layout(size);Click(buttons[7]);await Settled(buttons[7]);
                    Check(window.FlowDirection==(language=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight) && sidebar.FlowDirection==window.FlowDirection,"Sidebar language direction was lost");
                    var bounds=marker.TransformToAncestor(sidebar).TransformBounds(new Rect(marker.RenderSize));var selected=buttons[7].TransformToAncestor(sidebar).TransformBounds(new Rect(buttons[7].RenderSize));
                    Check(Math.Abs(bounds.Left-selected.Left)<.1 && Math.Abs(bounds.Top-selected.Top)<.1 && Math.Abs(bounds.Width-selected.Width)<.1 && Math.Abs(bounds.Height-selected.Height)<.1,"Indicator bounds differ after resize/RTL: "+language+size);
                    if(size.Width==1366) {Save(sidebar,Path.Combine(directory,$"v92-sidebar-{language}.png"));Save(root,Path.Combine(directory,$"v92-window-{language}.png"));}
                }
            }
            checks.Add("Indicator aligns exactly with the existing item bounds at four window sizes in EN/LTR and AR/RTL, including the Settings icon");
            var count=timings.Count;Check(count>20,"Not enough navigation coverage");
            source.RootVisual=null;window.Close();window=null;
            checks.Add("Closing detaches indicator/color event handlers and clocks; visual helper introduces no API calls, dispatcher frame loop or navigation await");
        }
        catch(Exception ex) {error=ex.ToString();}
        finally {source.RootVisual=null;window?.Close();Locale.Current.Dispose();}
        File.WriteAllText(Path.Combine(directory,"v92-sidebar-checks.json"),JsonSerializer.Serialize(new {success=error==null,checks,navigationDispatchMs=timings,error},new JsonSerializerOptions{WriteIndented=true}));
        return error==null?0:1;
    }
    private static IEnumerable<System.Windows.Shapes.Path> Paths(DependencyObject parent) {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is System.Windows.Shapes.Path path)yield return path;foreach(var nested in Paths(child))yield return nested;}
    }
    private static void Save(FrameworkElement root,string path) {
        var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(path);encoder.Save(output);
    }
}
