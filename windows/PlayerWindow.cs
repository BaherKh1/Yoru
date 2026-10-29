using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace Yoru;
public sealed class PlayerWindow : Window
{
    public event Action<double>? ProgressSaved;
    public event Action? PlaybackClosed;
    public double DurationSeconds => Math.Max(0, player.Length / 1000d);
    private readonly LibVLC engine;
    private readonly LibVLCSharp.Shared.MediaPlayer player;
    private readonly VideoView view;
    private readonly PlaybackCommands commands=new();
    private readonly DecoderGuard decoderGuard;
    private readonly PlaybackHealth health=new();
    private Media? currentMedia;
    private bool? requestedPause;
    private DateTime nextHealth;
    private bool buffering;

    private HwndSource? windowSource;
    private readonly Border windowFrame;
    private readonly RowDefinition captionRow;
    private readonly Controls.PlayerTitleBar titleBar;
    private readonly WindowChrome chrome = new() { CaptionHeight=48, ResizeBorderThickness=new Thickness(6), GlassFrameThickness=new Thickness(0), CornerRadius=new CornerRadius(8), UseAeroCaptionButtons=false };
    private readonly PlayerOverlay overlay;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool ended, started, fullscreen;
    private volatile bool closing;
    private WindowState restoreState;
    private readonly DateTime opened = DateTime.UtcNow;
    public PlayerWindow(VideoServer stream, string title, double start)
    {
        Icon = AppIcons.Image;
        Title = "YORU \u2014 " + title; Width = 1100; Height = 720; MinWidth = 700; MinHeight = 460; Background = Brushes.Black;
        WindowStyle = WindowStyle.None;
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, chrome);
        SourceInitialized += (_, _) => {
            windowSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            windowSource?.AddHook(WindowMessage);
            WindowAppearance.DarkCaption(this); WindowAppearance.PlayerCorners(this, true);
        };
        InitializeEngine();
        decoderGuard=new();
        try {
            engine = new LibVLC(EngineOptions("--no-video-title-show", "--no-snapshot-preview"));
            player = new LibVLCSharp.Shared.MediaPlayer(engine) { Volume = 70, EnableHardwareDecoding = !decoderGuard.SoftwareRequired };
        } catch { decoderGuard.Dispose(); throw; }
        player.Buffering+=(_,e)=>buffering=e.Cache<100;
        view = new VideoView { MediaPlayer = player };
        overlay = new PlayerOverlay();
        commands.Failed+=error=> { if(!closing) _=Dispatcher.BeginInvoke(new Action(()=>overlay.SetPlayback(false,false,0,0,L.T("PlayerHostFailed")))); };
        overlay.SetTitle(title, stream.Quality);
        overlay.SetPlayback(false, false, start, 0, L.T("OpeningVideo"));
        overlay.TogglePlayback += TogglePlayback;
        overlay.ToggleFullscreen += ToggleFullScreen;
        overlay.SeekRequested += seconds => {
            health.MarkTransition();
            if(!closing) _=commands.Seek(seconds,position=> { if(!closing && player.IsSeekable) { player.SetAudioDelay(0); player.Time=(long)(position*1000); } });
        };
        overlay.VolumeRequested += value => { if (!closing) player.Volume = value; };
        // VideoView.Content uses LibVLCSharp's foreground overlay, above the native video HWND.
        view.Content = overlay;
        titleBar = new Controls.PlayerTitleBar();
        captionRow = new RowDefinition { Height=new GridLength(48) };
        var layout = new Grid(); layout.RowDefinitions.Add(captionRow); layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(titleBar); Grid.SetRow(view,1); layout.Children.Add(view);
        windowFrame = new Border { Background=new SolidColorBrush(Color.FromRgb(9,12,20)), BorderBrush=new SolidColorBrush(Color.FromRgb(48,39,68)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Child=layout };
        Content = windowFrame;
        StateChanged += (_, _) => UpdateWindowFrame();
        PreviewKeyDown += overlay.HandleKey;
        Activated += (_, _) => overlay.Reveal();
        timer.Tick += (_, _) => UpdatePlayback();
        Loaded += (_, _) =>
        {
            var media = currentMedia = new Media(engine, new Uri(stream.Link));
            media.AddOption(":network-caching=1500");
            media.AddOption(":http-user-agent=" + PlaybackResolver.UserAgent);
            if (Data.WebUrl(stream.Referer)) media.AddOption(":http-referrer=" + stream.Referer);
            if (start > 0) media.AddOption(":start-time=" + start.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _=commands.Enqueue(()=> {
                if(!player.Play(media)) _=Dispatcher.BeginInvoke(new Action(()=>overlay.SetPlayback(false,false,0,0,L.T("PlayerStartFailed"))));
                else player.SetAudioDelay(0);
            });
            overlay.Start();
            timer.Start();
        };
        Closing += (_, _) =>
        {
            timer.Stop();
            overlay.Dispose();
            if (started) ProgressSaved?.Invoke(ended ? 0 : Math.Max(0, player.Time / 1000d));
            closing = true; decoderGuard.Dispose(); view.MediaPlayer = null;
        };
        Closed += async (_, _) =>
        {
            if(windowSource is {IsDisposed:false}) windowSource.RemoveHook(WindowMessage);
            // Network input shutdown can block; keep it off the UI dispatcher.
            await commands.Complete();
            await Task.Run(() => { player.Stop(); currentMedia?.Dispose(); player.Dispose(); engine.Dispose(); });
            view.Dispose();
            PlaybackClosed?.Invoke();
        };
    }
    public static void InitializeEngine()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "vlc");
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC");
        var directory = File.Exists(Path.Combine(bundled, "libvlc.dll")) ? bundled : installed;
        if (!File.Exists(Path.Combine(directory, "libvlc.dll"))) throw new PlaybackException(L.T("EngineMissing"));
        Core.Initialize(directory);
    }
    public static string[] EngineOptions(params string[] options)
    {
        // Keep certificate verification enabled; supplement OS roots for portable VLC.
        var roots = Path.Combine(AppContext.BaseDirectory, "certs");
        // LibVLCSharp selects per-player automatic or compatibility decoding.
        // Do not override that selection with a contradictory global decoder flag.
        var safe = options.AsEnumerable();
        return Directory.Exists(roots) ? safe.Append("--gnutls-dir-trust=" + roots).ToArray() : safe.ToArray();
    }
    private void UpdatePlayback()
    {
        if (closing) return;
        var state = player.State;
        var playing = state == VLCState.Playing;
        var position = Math.Max(0, player.Time / 1000d);
        var length = Math.Max(0, player.Length / 1000d);
        if (playing) started = true;
        if(requestedPause.HasValue && (state==VLCState.Paused)==requestedPause.Value) requestedPause=null;
        if(DateTime.UtcNow>=nextHealth && currentMedia is { } media) {
            nextHealth=DateTime.UtcNow.AddSeconds(5); var snapshot=media.Statistics; var bufferingNow=buffering; var audioDelay=player.AudioDelay;
            _=Task.Run(()=>health.Sample(snapshot,state,bufferingNow,decoderGuard.SoftwareRequired,audioDelay));
        }
        var message = state switch {
            VLCState.Error => L.T("PlayerHostFailed"),
            VLCState.Paused => L.T("Paused"),
            VLCState.Buffering => L.T("Buffering"),
            VLCState.Ended => L.T("Finished"),
            VLCState.Playing => "",
            _ => L.T("OpeningVideo") };
        if (state == VLCState.Ended && !ended) { ended = true; ProgressSaved?.Invoke(0); }
        if (!started && (DateTime.UtcNow - opened).TotalSeconds > 35)
            message = L.T("PlayerTimeout");
        overlay.SetPlayback(playing, player.IsSeekable, position, length, message);
    }
    private void TogglePlayback()
    {
        if (closing) return;
        health.MarkTransition();
        if(player.State==VLCState.Ended) { ended=false; requestedPause=false; _=commands.Enqueue(()=> { player.Stop(); player.Play(); player.SetAudioDelay(0); }); }
        else { var pause=!(requestedPause??player.State==VLCState.Paused); requestedPause=pause; _=commands.Enqueue(()=>player.SetPause(pause)); }

    }
    internal void ToggleFullScreen()
    {
        if (!fullscreen)
        {
            restoreState = WindowState;
            fullscreen = true;
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null);
            ResizeMode = ResizeMode.NoResize;
            UpdateWindowFrame();
            WindowState = WindowState.Maximized;
        }
        else
        {
            fullscreen = false;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, chrome);
            WindowState = restoreState;
            UpdateWindowFrame();
        }
        overlay.SetFullscreen(fullscreen);
    }
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==PlayerWindowBounds.GetMinMaxInfo && !fullscreen)
            PlayerWindowBounds.ApplyWorkingArea(hwnd,lParam);
        // Leave the message unhandled: WPF retains its native min/max tracking logic.
        return IntPtr.Zero;
    }
    private void UpdateWindowFrame()
    {
        var maximized = WindowState == WindowState.Maximized;
        captionRow.Height = new GridLength(fullscreen ? 0 : 48);
        titleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        titleBar.UpdateState(maximized);
        windowFrame.BorderThickness = new Thickness(fullscreen || maximized ? 0 : 1);
        windowFrame.CornerRadius = new CornerRadius(fullscreen || maximized ? 0 : 8);
        windowFrame.Margin = new Thickness(0);
        WindowAppearance.PlayerCorners(this, !fullscreen && !maximized);
    }
}
