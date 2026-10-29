using System.IO;
using System.Text.Json;
using LibVLCSharp.Shared;
namespace Yoru;

internal static class PlaybackDiagnostics
{
    public static async Task<int> Run(string path)
    {
        var success = false;
        var decoded = 0;
        string? error = null;
        try
        {
            PlayerWindow.InitializeEngine();
            using var engine = new LibVLC(PlayerWindow.EngineOptions("--vout=dummy", "--aout=dummy", "--no-audio", "--no-video-title-show"));
            using var player = new LibVLCSharp.Shared.MediaPlayer(engine);
            var input = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Deserialize<VideoServer>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Missing stream fixture")
                : new VideoServer("fixture", "", new Uri(Path.GetFullPath(path)).AbsoluteUri, true, false);
            using var media = new Media(engine, new Uri(input.Link));
            media.AddOption(":http-user-agent=" + PlaybackResolver.UserAgent);
            if (Data.WebUrl(input.Referer)) media.AddOption(":http-referrer=" + input.Referer);
            if (!player.Play(media)) throw new InvalidOperationException("The test player could not start.");
            var end = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < end)
            {
                await Task.Delay(150);
                if (media.Statistics is { } stats) decoded = stats.DecodedVideo;
                if (decoded > 2) { success = true; break; }
                if (player.State == VLCState.Error) break;
            }
            player.Stop();
        }
        catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "playback-smoke.json"), JsonSerializer.Serialize(new { success, decodedFrames = decoded, error }));
        return success ? 0 : 1;
    }
}
