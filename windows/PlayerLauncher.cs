using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace AnimeWitcherDesktop;
public record PlaybackSession(VideoServer Stream, string Title, double Position, Anime Anime, Episode Episode, DateTime? StartedUtc = null);
internal static class PlayerLauncher
{
    public static async Task Open(PlaybackSession session)
    {
        // Contain native codec failures in the player process, keeping browsing alive.
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The app executable could not be located.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--player");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The video player could not start.");
        // Do not put temporary media tokens in command lines or disk files.
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(session));
        process.StandardInput.Close();
    }
}
