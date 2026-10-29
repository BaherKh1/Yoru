using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeWitcherDesktop
{
    // Shared by the app and the asset builder: use original image pixels and
    // exclude the wordmark from small icons without redrawing the illustration.
    public static class BrandArtwork
    {
        public static DrawingImage CreateSmall(BitmapSource source)
        {
            DrawingGroup group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
                new RectangleGeometry(new Rect(0, 0, 256, 256))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(12, 10, 25)),
                new Pen(new SolidColorBrush(Color.FromRgb(114, 57, 215)), 4),
                new RectangleGeometry(new Rect(8, 8, 240, 240), 48, 48)));
            CroppedBitmap portrait = new CroppedBitmap(source, new Int32Rect(200, 130, 900, 680));
            portrait.Freeze();
            ImageBrush artwork = new ImageBrush(portrait);
            artwork.Stretch = Stretch.Uniform;
            artwork.AlignmentX = AlignmentX.Center;
            artwork.AlignmentY = AlignmentY.Center;
            group.Children.Add(new GeometryDrawing(artwork, null,
                new RectangleGeometry(new Rect(16, 16, 224, 224), 40, 40)));
            DrawingImage result = new DrawingImage(group);
            result.Freeze();
            return result;
        }

        public static DrawingImage CreateFull(BitmapSource source)
        {
            DrawingGroup group = new DrawingGroup();
            group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 1072, 1072), 242, 242);
            group.Children.Add(new ImageDrawing(source, new Rect(-92, -90, source.PixelWidth, source.PixelHeight)));
            DrawingImage result = new DrawingImage(group);
            result.Freeze();
            return result;
        }
    }
}
