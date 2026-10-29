using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Yoru;

internal static class IconDiagnostics
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    internal static async Task<int> Run()
    {
        var checks = new List<string>();
        string? error = null;
        MainWindow? main = null;
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/" + AppIcons.IconResource), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (!decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })) throw new Exception("Missing embedded icon resolutions");
            checks.Add("Embedded ICO loads all nine resolutions (16 through 256 pixels)");
            if (AppIcons.Image.PixelWidth != 256 || !AppIcons.Image.IsFrozen) throw new Exception("Window icon did not use the highest-resolution frame");
            checks.Add("Shared window icon uses the frozen 256px frame");
            var executable = Path.Combine(AppContext.BaseDirectory, "YORU.exe");
            if (ExtractIconEx(executable, 0, out var large, out var small, 1) == 0) throw new Exception("Windows could not extract the executable icon");
            try
            {
                foreach (var handle in new[] { large, small })
                {
                    if (handle == IntPtr.Zero) throw new Exception("Executable large/small icon is missing");
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    var converted = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
                    var reference=decoder.Frames.First(frame=>frame.PixelWidth==converted.PixelWidth);
                    var expected=new FormatConvertedBitmap(reference,System.Windows.Media.PixelFormats.Bgra32,null,0);
                    var pixels=new byte[bytes.Length]; expected.CopyPixels(pixels,expected.PixelWidth*4,0);
                    var visible=0; var matching=0;
                    for(var i=0;i<bytes.Length;i+=4) if(pixels[i+3]>200) {
                        visible++;
                        if(Math.Abs(bytes[i]-pixels[i])<10 && Math.Abs(bytes[i+1]-pixels[i+1])<10 && Math.Abs(bytes[i+2]-pixels[i+2])<10 && bytes[i+3]>200) matching++;
                    }
                    if(visible<20 || matching<visible*0.9) throw new Exception("Executable icon differs from the embedded YORU artwork");
                }
            }
            finally { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero) DestroyIcon(small); }
            checks.Add("Windows extracts matching YORU icons at both shell sizes from the EXE");
            main = new MainWindow(false);
            if (!ReferenceEquals(main.Icon, AppIcons.Image)) throw new Exception("Main window icon is missing");
            checks.Add("Main window references the shared application icon");
            var player = new PlayerWindow(new("Test", "1080p", "https://example.invalid/unused.mp4", true, false), "Icon check", 0);
            var closed = new TaskCompletionSource(); player.PlaybackClosed += () => closed.TrySetResult();
            var correct = ReferenceEquals(player.Icon, AppIcons.Image);
            // Never show the window or trigger its Loaded handler: no media/network request.
            player.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (!correct) throw new Exception("Video-player window icon is missing");
            checks.Add("Native video-player window references the same icon; player initializes and disposes without streaming");
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { main?.Close(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "icon-checks.json"), JsonSerializer.Serialize(new { success = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
}
