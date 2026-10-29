using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
namespace Yoru;
internal static class V93Diagnostics
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;public override string ToString()=>$"{Left},{Top} - {Right},{Bottom}";}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var checks=new List<string>();var snapshots=new List<object>();string? error=null;PlayerWindow? player=null;
        async Task Pump(){await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Task.Delay(180);}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        try {
            foreach(var scale in new[]{1d,1.25,1.5,2,2.5}) {
                var width=(int)(1920*scale);var height=(int)(1080*scale);var taskbarThickness=(int)(40*scale);
                foreach(var origin in new[]{(X:0,Y:0),(X:-width,Y:-height),(X:width,Y:0)}) {
                    var mon=new PlayerWindowBounds.Rectangle(origin.X,origin.Y,origin.X+width,origin.Y+height);
                    var cases=new[]{new PlayerWindowBounds.Rectangle(origin.X,origin.Y,origin.X+width,origin.Y+height-taskbarThickness),new PlayerWindowBounds.Rectangle(origin.X,origin.Y+taskbarThickness,origin.X+width,origin.Y+height),new PlayerWindowBounds.Rectangle(origin.X+taskbarThickness,origin.Y,origin.X+width,origin.Y+height),new PlayerWindowBounds.Rectangle(origin.X,origin.Y,origin.X+width-taskbarThickness,origin.Y+height)};
                    foreach(var work in cases) {
                        var limits=new PlayerWindowBounds.MinMaxInfo {MinTrackSize=new(700,460),MaxTrackSize=new(10000,10000)};
                        Check(PlayerWindowBounds.SetMaximizedBounds(ref limits,mon,work),"Work bounds rejected");
                        Check(limits.MaxPosition.X==work.Left-mon.Left && limits.MaxPosition.Y==work.Top-mon.Top && limits.MaxSize.X==work.Right-work.Left && limits.MaxSize.Y==work.Bottom-work.Top,"Taskbar edge/monitor-origin geometry failed");
                        Check(limits.MinTrackSize.X==700 && limits.MinTrackSize.Y==460 && limits.MaxTrackSize.X==10000,"Working-area calculation changed normal resize tracking");
                    }
                }
            }
            checks.Add("60 synthetic work-area cases cover all taskbar edges, positive/negative monitor origins and 100/125/150/200/250% native-pixel scales without changing resize tracking");
            var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","tests","performance-av-1080p.mp4"));
            player=new PlayerWindow(new VideoServer("Fixture","1080p",new Uri(fixture).AbsoluteUri,true,false),"Window work area check",0) {ShowActivated=false,ShowInTaskbar=false,Left=80,Top=80};
            var hwnd=new WindowInteropHelper(player).EnsureHandle();var cloak=1;_=DwmSetWindowAttribute(hwnd,13,ref cloak,sizeof(int));player.Show();await Pump();
            var frame=(Border)player.Content;var layout=(Grid)frame.Child;var bar=(Controls.PlayerTitleBar)layout.Children[0];
            var overlay=(PlayerOverlay)typeof(PlayerWindow).GetField("overlay",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;
            var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};Check(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor),"Monitor lookup failed");
            object Capture(string state) {
                overlay.Reveal();
                GetWindowRect(hwnd,out var bounds);
                var control=(FrameworkElement)overlay.FindName("ControlSurface");
                var rootTop=frame.PointToScreen(new Point());var rootBottom=frame.PointToScreen(new Point(frame.ActualWidth,frame.ActualHeight));
                var controlTop=control.PointToScreen(new Point());var controlBottom=control.PointToScreen(new Point(control.ActualWidth,control.ActualHeight));
                var owned=Application.Current.Windows.Cast<Window>().Where(window=>window!=player).Select(window=> {var handle=new WindowInteropHelper(window).Handle;GetWindowRect(handle,out var rectangle);return new{type=window.GetType().FullName,rectangle=rectangle.ToString()};}).ToArray();
                return new {state,window=bounds.ToString(),monitor=monitor.Monitor.ToString(),work=monitor.Work.ToString(),frameTop=rootTop.ToString(),frameBottom=rootBottom.ToString(),controlsTop=controlTop.ToString(),controlsBottom=controlBottom.ToString(),owned};
            }
            void CheckMaximized() {
                Check(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor),"Current monitor lookup failed");
                var start=frame.PointToScreen(new Point());var end=frame.PointToScreen(new Point(frame.ActualWidth,frame.ActualHeight));
                Check(start.X>=monitor.Work.Left-1 && start.Y>=monitor.Work.Top-1 && end.X<=monitor.Work.Right+1 && end.Y<=monitor.Work.Bottom+1,"Maximized content escaped working area");
                var foreground=Window.GetWindow(overlay);Check(foreground is not null,"Native foreground window missing");
                Check(GetWindowRect(new WindowInteropHelper(foreground).Handle,out var nativeOverlay),"Overlay rectangle lookup failed");
                Check(nativeOverlay.Left>=monitor.Work.Left-1 && nativeOverlay.Top>=monitor.Work.Top-1 && nativeOverlay.Right<=monitor.Work.Right+1 && nativeOverlay.Bottom<=monitor.Work.Bottom+1,"Native video-control window overlaps taskbar");
            }
            snapshots.Add(Capture("normal"));
            GetWindowRect(hwnd,out var normal);
            var storage=Marshal.AllocHGlobal(Marshal.SizeOf<PlayerWindowBounds.MinMaxInfo>());
            try {
                Marshal.StructureToPtr(new PlayerWindowBounds.MinMaxInfo {MaxSize=new(1920,1080),MinTrackSize=new(140,40),MaxTrackSize=new(32767,32767)},storage,false);
                SendMessage(hwnd,PlayerWindowBounds.GetMinMaxInfo,IntPtr.Zero,storage);
                var limits=Marshal.PtrToStructure<PlayerWindowBounds.MinMaxInfo>(storage);
                Check(limits.MinTrackSize.X>=player.MinWidth && limits.MinTrackSize.Y>=player.MinHeight,"WPF minimum tracking limits were bypassed");
                Check(limits.MaxSize.X==monitor.Work.Right-monitor.Work.Left && limits.MaxSize.Y==monitor.Work.Bottom-monitor.Work.Top,"Native maximize message did not select working area");
            } finally {Marshal.FreeHGlobal(storage);}
            checks.Add("Actual WM_GETMINMAXINFO uses current monitor work bounds and still runs WPF minimum-size tracking");
            ((Button)bar.FindName("MaximizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            snapshots.Add(Capture("maximized"));CheckMaximized();
            var bottom=frame.PointToScreen(new Point(frame.ActualWidth,frame.ActualHeight));var top=frame.PointToScreen(new Point());
            Check(top.X>=monitor.Work.Left-1 && top.Y>=monitor.Work.Top-1 && bottom.X<=monitor.Work.Right+1 && bottom.Y<=monitor.Work.Bottom+1,"Maximized player content extends outside monitor working area");
            var surface=(FrameworkElement)overlay.FindName("ControlSurface");var controlEdge=surface.PointToScreen(new Point(surface.ActualWidth,surface.ActualHeight));
            Check(controlEdge.Y<=monitor.Work.Bottom+1 && controlEdge.X<=monitor.Work.Right+1,"Playback controls extend behind taskbar");
            checks.Add("Native maximized frame and playback controls stay within the actual monitor working area");
            ((Button)bar.FindName("MaximizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            GetWindowRect(hwnd,out var restored);Check(restored.Left==normal.Left && restored.Top==normal.Top && restored.Right==normal.Right && restored.Bottom==normal.Bottom,"Restore changed the normal window rectangle");
            SendMessage(hwnd,0x0112,new IntPtr(0xF030),IntPtr.Zero);await Pump();CheckMaximized();
            SendMessage(hwnd,0x00A3,new IntPtr(2),IntPtr.Zero);await Pump();Check(player.WindowState==WindowState.Normal,"Titlebar double-click restore failed");
            SendMessage(hwnd,0x00A3,new IntPtr(2),IntPtr.Zero);await Pump();CheckMaximized();
            checks.Add("Custom maximize/restore, native SC_MAXIMIZE and caption double-click all respect the working area and restore the original window rectangle");
            player.ToggleFullScreen();await Pump();snapshots.Add(Capture("fullscreen"));
            GetWindowRect(hwnd,out var full);
            Check(Math.Abs(full.Left-monitor.Monitor.Left)<=1 && Math.Abs(full.Top-monitor.Monitor.Top)<=1 && Math.Abs(full.Right-monitor.Monitor.Right)<=1 && Math.Abs(full.Bottom-monitor.Monitor.Bottom)<=1,"Fullscreen no longer fills the monitor");
            var fullContent=frame.PointToScreen(new Point(frame.ActualWidth,frame.ActualHeight));Check(Math.Abs(fullContent.X-monitor.Monitor.Right)<=1 && Math.Abs(fullContent.Y-monitor.Monitor.Bottom)<=1,"Fullscreen content did not fill the monitor");
            player.ToggleFullScreen();await Pump();snapshots.Add(Capture("fullscreen-to-maximized"));CheckMaximized();
            bottom=frame.PointToScreen(new Point(frame.ActualWidth,frame.ActualHeight));Check(player.WindowState==WindowState.Maximized && bottom.Y<=monitor.Work.Bottom+1,"Fullscreen exit did not restore safe maximized bounds");
            checks.Add("Fullscreen fills the monitor and restores working-area maximize on exit");
            var closed=new TaskCompletionSource();player.PlaybackClosed+=()=>closed.TrySetResult();player.Close();await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));player=null;
        }
        catch(Exception ex){error=ex.ToString();}
        finally {player?.Close();}
        File.WriteAllText(Path.Combine(directory,"v93-window-checks.json"),JsonSerializer.Serialize(new{success=error==null,checks,snapshots,error},new JsonSerializerOptions{WriteIndented=true}));
        return error==null?0:1;
    }
}
