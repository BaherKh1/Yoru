using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;

namespace Yoru;

internal static class PlayerOverlayDiagnostics
{
    public static async Task<int> Run()
    {
        Locale.Current.SetForDiagnostics("en");
        var checks = new List<string>();
        string? error = null;
        var revealTimings=new Dictionary<string,double>();
        using var overlay = new PlayerOverlay();
        using var source = new HwndSource(new HwndSourceParameters("Hidden player overlay checks") { Width = 1100, Height = 640, WindowStyle = unchecked((int)0x80000000) });
        try
        {
            var host = new Grid { Width = 1100, Height = 640, Background = new LinearGradientBrush(Color.FromRgb(35, 39, 67), Color.FromRgb(8, 10, 20), 40) };
            host.Children.Add(overlay);
            source.RootVisual = host;
            host.Measure(new Size(1100, 640)); host.Arrange(new Rect(0, 0, 1100, 640)); host.UpdateLayout();
            overlay.SetTitle("Jujutsu Kaisen · Episode 1", "1080p");
            overlay.Start();
            overlay.SetPlayback(true, true, 342, 1450);
            var chrome = (Grid)overlay.FindName("Chrome");
            var controls = (Grid)overlay.FindName("ControlSurface");
            var input = (Grid)overlay.FindName("InputSurface");
            var seek = (Slider)overlay.FindName("Seek");
            var volume = (Slider)overlay.FindName("Volume");
            void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
            await Task.Delay(200);
            var snapshot = new RenderTargetBitmap(1100, 640, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(snapshot));
            using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "player-overlay-preview.png"))) encoder.Save(file);
            await Task.Delay(3100);
            Check(chrome.Opacity < 0.05 && !chrome.IsHitTestVisible, "Playing: idle fade completes and invisible controls stop taking clicks");
            var revealSeekRequests=0; overlay.SeekRequested+=_=>revealSeekRequests++;
            var oldSeek=seek.Value;
            for(int i=0;i<40;i++) overlay.SetPlayback(true,true,361+i,1450);
            Check(seek.Value==oldSeek,"Hidden playback updates retain fresh state without invalidating the progress bar");
            var transitions=overlay.FadeTransitionCount; var revealWatch=System.Diagnostics.Stopwatch.StartNew();
            overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            revealTimings["synchronousRevealMs"]=revealWatch.Elapsed.TotalMilliseconds;
            Check(chrome.Opacity>=0.35 && chrome.IsHitTestVisible,"Mouse movement makes controls visible and clickable synchronously");
            Check(seek.Value==400 && revealSeekRequests==0,"Reveal applies the latest hidden playback position without a feedback seek");
            for(int i=0;i<1000;i++) overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent=UIElement.PreviewMouseMoveEvent });
            Check(overlay.FadeTransitionCount==transitions+1,"A burst of 1,000 mouse events starts only one reveal animation");
            var allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++) overlay.Reveal();
            revealTimings["visibleActivityAllocatedBytes"]=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(overlay.FadeTransitionCount==transitions+1,"Visible pointer activity does not create or restart fade clocks");
            await Task.Delay(200);
            Check(!chrome.HasAnimatedProperties,"Completed reveal releases its opacity animation clock");
            Check(chrome.Opacity > 0.95, "Mouse movement reveals controls");
            overlay.SetPlayback(false, true, 342, 1450, "Paused");
            await Task.Delay(3200);
            Check(chrome.Opacity > 0.95, "Paused: controls stay visible beyond idle delay");
            overlay.SetPlayback(true, true, 342, 1450);
            controls.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            await Task.Delay(3200);
            Check(chrome.Opacity > 0.95, "Hovered controls stay visible beyond idle delay");
            controls.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            await Task.Delay(3200);
            Check(chrome.Opacity < 0.05, "Leaving controls restarts idle hiding");
            var device = new DiagnosticTouchDevice();
            overlay.RaiseEvent(new TouchEventArgs(device, Environment.TickCount) { RoutedEvent = UIElement.PreviewTouchDownEvent });
            await Task.Delay(200);
            Check(chrome.Opacity > 0.95, "Touch reveals controls");
            double seekResult = -1;
            var seekEvents = 0;
            overlay.SeekRequested += seconds => { seekResult = seconds; seekEvents++; };
            seek.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            seek.Value = 500;
            await Task.Delay(3200);
            Check(chrome.Opacity > 0.95 && seekEvents == 0, "Dragging scrubber pins controls and defers the seek");
            seek.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(seekResult == 500 && seekEvents == 1, "Releasing scrubber commits the requested position once");
            overlay.SetPlayback(true, true, 501, 1450);
            Check(seekEvents == 1, "Backend time updates do not cause seek requests");
            await Task.Delay(3200);
            var fullscreen = false;
            overlay.ToggleFullscreen += () => { fullscreen = !fullscreen; overlay.SetFullscreen(fullscreen); };
            overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.F) { RoutedEvent = UIElement.PreviewKeyDownEvent });
            await Task.Delay(200);
            Check(chrome.Opacity > 0.95 && fullscreen, "Keypress reveals controls and fullscreen shortcut works");
            Check(AutomationProperties.GetName((Button)overlay.FindName("Fullscreen")) == "Exit fullscreen", "Fullscreen toggle updates its accessible label");
            overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
            Check(!fullscreen, "Escape exits fullscreen from the foreground overlay");
            var requestedVolume = -1;
            overlay.VolumeRequested += value => requestedVolume = value;
            volume.Value = 0;
            Check(requestedVolume == 0 && AutomationProperties.GetName((Button)overlay.FindName("Mute")) == "Unmute", "Mute updates the volume and icon label");
            ((Button)overlay.FindName("Mute")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(volume.Value > 0, "Unmute restores volume");
            overlay.SetPlayback(false, true, 501, 1450, "Paused");
            Check(AutomationProperties.GetName((Button)overlay.FindName("PlayPause")) == "Play", "Paused icon toggles back to play");
            var originalSeek=seek.Value;
            for(int i=0;i<10;i++) overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Right) { RoutedEvent=UIElement.PreviewKeyDownEvent });
            Check(Math.Abs(seek.Value-originalSeek-50)<0.01,"Repeated seek keys accumulate their targets before native seek coalescing");
            overlay.SetPlayback(false,true,originalSeek,1450,L.T("Paused"));
            Check(Math.Abs(seek.Value-originalSeek-50)<0.01,"A late native clock update does not overwrite the pending seek target");
            Locale.Current.SetForDiagnostics("ar"); await Task.Delay(50);
            Check(overlay.FlowDirection == FlowDirection.RightToLeft && AutomationProperties.GetName((Button)overlay.FindName("PlayPause")) == L.T("Play") && seek.FlowDirection == FlowDirection.LeftToRight, "Arabic player switches immediately while seek direction remains standard");
            Locale.Current.SetForDiagnostics("en");
            Check(AutomationProperties.GetName((Button)overlay.FindName("PlayPause")) == "Play", "Player switches back to English immediately");
            Check(((SolidColorBrush)input.Background).Color.A > 0, "Hidden chrome retains native pointer and touch capture surface");
        }
        catch (Exception exception) { error = exception.ToString(); }
        source.RootVisual = null;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "overlay-checks.json"), JsonSerializer.Serialize(new { success = error is null, checks, revealTimings, error }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
    private sealed class DiagnosticTouchDevice() : TouchDevice(42)
    {
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo) => new(this, new Point(50, 50), new Rect(45, 45, 10, 10), TouchAction.Down);
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo) => new() { GetTouchPoint(relativeTo) };
    }
}
