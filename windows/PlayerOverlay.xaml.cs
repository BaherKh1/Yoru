using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AnimeWitcherDesktop;

public partial class PlayerOverlay : UserControl, IDisposable
{
    public event Action? TogglePlayback;
    public event Action? ToggleFullscreen;
    public event Action<double>? SeekRequested;
    public event Action<int>? VolumeRequested;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private TimeSpan lastActivity, pendingSeekUntil;
    private bool isPlaying, seekDragging, settingValues, hidden, keyboardMode, isFullscreen, controlHovered;
    private double duration, restoreVolume = 70, latestPosition;
    private bool latestSeekable, disposed;
    private int fadeGeneration;
    internal int FadeTransitionCount { get; private set; }
    private static readonly CubicEase FadeEase = CreateFadeEase();
    private static CubicEase CreateFadeEase() { var easing=new CubicEase { EasingMode=EasingMode.EaseOut }; easing.Freeze(); return easing; }
    private int displayedSecond=-1,displayedDuration=-1;
    private static readonly Geometry PlayGeometry = Geometry.Parse("M7,4 L20,12 L7,20 Z");
    private static readonly Geometry PauseGeometry = Geometry.Parse("M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z");
    private static readonly Geometry ExpandGeometry = Geometry.Parse("M8,3 L3,3 L3,8 M16,3 L21,3 L21,8 M3,16 L3,21 L8,21 M21,16 L21,21 L16,21");
    private static readonly Geometry CompressGeometry = Geometry.Parse("M3,8 L8,8 L8,3 M21,8 L16,8 L16,3 M8,21 L8,16 L3,16 M16,21 L16,16 L21,16");
    private static readonly Geometry SoundGeometry = Geometry.Parse("M3,9 L7,9 L12,5 L12,19 L7,15 L3,15 Z M16,8 C18,10 18,14 16,16 M19,5 C23,9 23,15 19,19");
    private static readonly Geometry MuteGeometry = Geometry.Parse("M3,9 L7,9 L12,5 L12,19 L7,15 L3,15 Z M16,9 L22,15 M22,9 L16,15");
    public PlayerOverlay()
    {
        InitializeComponent(); Locale.Current.Changed += LanguageChanged;
        idle.Tick += (_, _) => UpdateVisibility();
        PreviewMouseMove += (_, _) => { keyboardMode = false; Reveal(); };
        PreviewMouseDown += (_, e) => {
            keyboardMode = false; Reveal();
            if (e.OriginalSource == InputSurface && e.ChangedButton == MouseButton.Left) { Focus(); if (e.ClickCount == 2) ToggleFullscreen?.Invoke(); }
        };
        PreviewMouseWheel += (_, _) => Reveal();
        PreviewTouchDown += (_, _) => { keyboardMode = false; Reveal(); };
        PreviewTouchMove += (_, _) => Reveal();
        PreviewStylusDown += (_, _) => Reveal();
        PreviewStylusMove += (_, _) => Reveal();
        PreviewKeyDown += HandleKey;
        ControlSurface.MouseEnter += (_, _) => { controlHovered = true; Reveal(); };
        ControlSurface.MouseLeave += (_, _) => { controlHovered = false; lastActivity = clock.Elapsed; };
        ControlSurface.GotKeyboardFocus += (_, _) => Reveal();
        Seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { seekDragging = true; Reveal(); }));
        Seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) => { seekDragging = false; if (!e.Canceled) { pendingSeekUntil=clock.Elapsed+TimeSpan.FromMilliseconds(500); SeekRequested?.Invoke(Seek.Value); } Reveal(); }));
        Seek.ValueChanged += (_, _) => { UpdateTime(Seek.Value); if (!settingValues && !seekDragging) { pendingSeekUntil=clock.Elapsed+TimeSpan.FromMilliseconds(500); Reveal(); SeekRequested?.Invoke(Seek.Value); } };
        Volume.ValueChanged += (_, _) => { if (!settingValues) { UpdateVolume(); Reveal(); VolumeRequested?.Invoke((int)Volume.Value); } };
        Unloaded += (_, _) => idle.Stop();
        Loaded += (_, _) => Start();
    }
    public void Start() { if(disposed) return; Reveal(); idle.Start(); }
    public void SetTitle(string title, string quality) { VideoTitle.Text = title; QualityLabel.Text = string.IsNullOrWhiteSpace(quality) ? L.T("Video") : quality.ToUpperInvariant(); }
    public void SetPlayback(bool playing, bool seekable, double seconds, double total, string message = "")
    {
        latestPosition=Math.Max(0,seconds); latestSeekable=seekable; duration=Math.Max(0,total);
        if(isPlaying!=playing) { isPlaying=playing; UpdatePlaybackIcon(); Reveal(); }
        PlaybackStatus.Text=message;
        PlaybackStatus.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        // Keep fresh native state in fields; a hidden slider must not invalidate
        // layout/composition four times per second. Reveal applies it immediately.
        if(!hidden) ApplyPlaybackValues();
        if(!playing) Reveal();
    }
    private void UpdatePlaybackIcon()
    {
        PlayIcon.Data=isPlaying?PauseGeometry:PlayGeometry;
        PlayPause.ToolTip=L.T(isPlaying?"PauseTip":"PlayTip");
        AutomationProperties.SetName(PlayPause,L.T(isPlaying?"Pause":"Play"));
    }
    private void ApplyPlaybackValues()
    {
        settingValues=true;
        try {
            Seek.IsEnabled=latestSeekable && duration>0;
            Seek.Maximum=Math.Max(1,duration);
            if(!seekDragging && clock.Elapsed>=pendingSeekUntil) Seek.Value=Math.Clamp(latestPosition,0,Seek.Maximum);
        } finally { settingValues=false; }
        UpdateTime(seekDragging || clock.Elapsed<pendingSeekUntil?Seek.Value:latestPosition);
    }
    public void SetFullscreen(bool fullscreen)
    {
        isFullscreen = fullscreen;
        FullscreenIcon.Data = fullscreen ? CompressGeometry : ExpandGeometry;
        var name = fullscreen ? L.T("ExitFullscreen") : L.T("Fullscreen");
        Fullscreen.ToolTip = name + " (F)"; AutomationProperties.SetName(Fullscreen, name); Reveal();
    }
    public void Reveal()
    {
        if(disposed) return;
        lastActivity=clock.Elapsed;
        // hidden describes the target state, not the intermediate opacity. Mouse
        // move/stylus events during a reveal must never restart its animation.
        if(!hidden) return;
        hidden=false; Chrome.IsHitTestVisible=true; Cursor=Cursors.Arrow;
        ApplyPlaybackValues();
        AnimateOpacity(1,75,Math.Max(0.35,Chrome.Opacity));
    }
    private void UpdateVisibility()
    {
        if (!isPlaying || seekDragging || controlHovered || ControlSurface.IsMouseOver || ControlSurface.IsMouseCaptureWithin || ControlSurface.AreAnyTouchesOver || ControlSurface.AreAnyTouchesCapturedWithin || (keyboardMode && ControlSurface.IsKeyboardFocusWithin))
        { if (hidden) Reveal(); return; }
        if (!hidden && clock.Elapsed - lastActivity >= TimeSpan.FromSeconds(2.7))
        {
            hidden = true; Chrome.IsHitTestVisible = false; Cursor = Cursors.None; AnimateOpacity(0,160);
        }
    }
    private void AnimateOpacity(double target,int milliseconds,double? start=null)
    {
        var generation=++fadeGeneration;
        var from=start??Chrome.Opacity;
        Chrome.BeginAnimation(OpacityProperty,null);
        Chrome.Opacity=from;
        var animation=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction=FadeEase };
        animation.Completed+=(_,_)=> {
            if(disposed || generation!=fadeGeneration) return;
            // Remove completed clocks instead of retaining an idle animation.
            Chrome.Opacity=target; Chrome.BeginAnimation(OpacityProperty,null);
        };
        FadeTransitionCount++;
        Chrome.BeginAnimation(OpacityProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private void UpdateTime(double position)
    {
        var second=(int)Math.Max(0,position); var total=(int)duration;
        if(second==displayedSecond && total==displayedDuration) return;
        displayedSecond=second; displayedDuration=total;
        TimeLabel.Text=FormatTime(second)+" / "+(duration>0?FormatTime(duration):"--:--");
    }
    private static string FormatTime(double seconds) { var time = TimeSpan.FromSeconds(seconds); return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss"); }
    private void UpdateVolume()
    {
        var muted = Volume.Value < 1;
        VolumeIcon.Data = muted ? MuteGeometry : SoundGeometry;
        Mute.ToolTip = muted ? L.T("UnmuteTip") : L.T("MuteTip");
        AutomationProperties.SetName(Mute, muted ? L.T("Unmute") : L.T("Mute"));
    }
    private void PlayPause_Click(object sender, RoutedEventArgs e) { Reveal(); TogglePlayback?.Invoke(); }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) { Reveal(); ToggleFullscreen?.Invoke(); }
    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void ToggleMute() { if (Volume.Value > 0) { restoreVolume = Volume.Value; Volume.Value = 0; } else Volume.Value = Math.Max(1, restoreVolume); }
    public void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        keyboardMode = true; Reveal();
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.F) { ToggleFullscreen?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.Escape && isFullscreen) { ToggleFullscreen?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.M) { ToggleMute(); e.Handled = true; }
        else if (e.Key == Key.Space && e.OriginalSource is not Button && !ControlSurface.IsKeyboardFocusWithin) { TogglePlayback?.Invoke(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && !ControlSurface.IsKeyboardFocusWithin && Seek.IsEnabled)
        { Seek.Value=Math.Clamp(Seek.Value+(e.Key==Key.Left?-5:5),0,duration); e.Handled=true; }
    }
    private void LanguageChanged(object? sender, EventArgs e) { UpdateVolume(); SetFullscreen(isFullscreen); UpdatePlaybackIcon(); }
    public void Dispose() { disposed=true; fadeGeneration++; Locale.Current.Changed -= LanguageChanged; idle.Stop(); Chrome.BeginAnimation(OpacityProperty, null); }
}
