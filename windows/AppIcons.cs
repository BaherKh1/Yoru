using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeWitcherDesktop;

public static class AppIcons
{
    public const string IconResource = "Assets/yoru-v9.ico";
    private static readonly Lazy<BitmapFrame> image = new(() => {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/" + IconResource), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
        frame.Freeze(); return frame;
    });
    private static readonly Lazy<BitmapImage> original = new(() => {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/yoru-original.png"); bitmap.EndInit();
        bitmap.Freeze(); return bitmap;
    });
    private static readonly Lazy<DrawingImage> mark = new(() => BrandArtwork.CreateSmall(OriginalLogo));
    private static readonly Lazy<DrawingImage> logo = new(() => BrandArtwork.CreateFull(OriginalLogo));
    public static BitmapFrame Image => image.Value;
    public static DrawingImage Mark => mark.Value;
    public static DrawingImage Logo => logo.Value;
    public static BitmapImage OriginalLogo => original.Value;
}
