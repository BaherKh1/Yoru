using System.Windows;
using System.Windows.Threading;
namespace AnimeWitcherDesktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        AppIdentity.Initialize();
        base.OnStartup(e);
        Locale.Current.Initialize();
        if (e.Args.Length == 2 && e.Args[0] == "--v93-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V93Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v92-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V92Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v91-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V91Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v9-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V9Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--startup-profile")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await StartupProfileDiagnostics.Run(e.Args[1],e.Args[2])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v81-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(V81Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v8-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V8Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v7-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V7Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--v6-checks")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await V6Diagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length is 2 or 3 && e.Args[0] == "--playback-profile")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PlaybackPerformanceDiagnostics.Run(e.Args[1],e.Args.Length==3 && e.Args[2]=="--software")); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--performance-profile")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PerformanceDiagnostics.Run(e.Args[1])); return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--icon-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await IconDiagnostics.Run());
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--design-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var result = await DesignDiagnostics.Run(e.Args[1]);
            Shutdown(result);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--overlay-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var result = await PlayerOverlayDiagnostics.Run();
            Shutdown(result);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] is "--player" or "--session-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try {
                var json = await Console.In.ReadToEndAsync();
                var session = System.Text.Json.JsonSerializer.Deserialize<PlaybackSession>(json) ?? throw new InvalidOperationException("Missing playback session");
                if (e.Args[0] == "--session-smoke") { Shutdown(session.Stream.Direct && Data.WebUrl(session.Stream.Link) ? 0 : 1); return; }
                var history = new HistoryStore();
                var player = new PlayerWindow(session.Stream, session.Title, session.Position);
                MainWindow = player;
                player.ProgressSaved += seconds => history.Save(session.Anime, session.Episode, seconds, player.DurationSeconds, session.StartedUtc);
                player.PlaybackClosed += () => Shutdown(0);
                player.Show();
            } catch (Exception error) { ShowPlaybackError(error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--engine-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var result = await Task.Run(() => PlaybackDiagnostics.Run(e.Args[1]));
            Shutdown(result);
            return;
        }
        var smoke = e.Args.Contains("--ui-smoke");
        var window = new MainWindow(!smoke);
        MainWindow = window;
        if (smoke)
        {
            // Exercise XAML creation, layout and binding without opening a visible window.
            window.Measure(new Size(1360, 880));
            window.Arrange(new Rect(0, 0, 1360, 880));
            window.UpdateLayout();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => Shutdown(0)));
        }
        else window.Show();
    }
    private static void ShowPlaybackError(Exception error)
    {
        var dialog = new Window { Title = L.T("PlaybackUnavailable"), Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)Current.FindResource("SurfaceBrush"), Foreground = (System.Windows.Media.Brush)Current.FindResource("TextBrush"), FlowDirection = Locale.Current.Direction };
        var content = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = L.Error(error), TextWrapping = TextWrapping.Wrap });
        var close = new System.Windows.Controls.Button { Content = L.T("Close"), IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,20,0,0) };
        close.Click += (_, _) => dialog.Close(); content.Children.Add(close); dialog.Content = content; dialog.SourceInitialized += (_, _) => WindowAppearance.DarkCaption(dialog); dialog.ShowDialog();
    }
}
