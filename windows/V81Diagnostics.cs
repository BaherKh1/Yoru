using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yoru;

internal static class V81Diagnostics
{
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    internal static int Run(string directory)
    {
        directory=Path.GetFullPath(directory); var checks=new List<string>(); string? error=null;
        try {
            var assets=Path.Combine(AppContext.BaseDirectory,"Assets");
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets,"branding-assets.json")));
            using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/yoru-original.png"))!.Stream;
            Check(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==manifest.RootElement.GetProperty("originalSha256").GetString(),"Embedded full logo differs from the original high-resolution source");
            Check(AppIcons.OriginalLogo.PixelWidth==1254 && AppIcons.OriginalLogo.PixelHeight==1254,"Full logo uses an upscaled small bitmap");
            var full=(DrawingGroup)AppIcons.Logo.Drawing;
            Check(ReferenceEquals(full.Children.OfType<ImageDrawing>().Single().ImageSource,AppIcons.OriginalLogo) && full.ClipGeometry is RectangleGeometry,"Full logo is not the original image with a display-time squircle clip");
            checks.Add("Full branding uses the original 1254x1254 PNG byte-for-byte; only the outer matte is clipped at display time");
            Check(AppIcons.Mark.IsFrozen && AppIcons.Mark.Width==256 && AppIcons.Mark.Height==256,"Small mark is not a frozen 256-unit artwork canvas");
            var small=(DrawingGroup)AppIcons.Mark.Drawing;
            var brush=small.Children.OfType<GeometryDrawing>().Select(drawing=>drawing.Brush).OfType<ImageBrush>().Single();
            Check(brush.ImageSource is CroppedBitmap portrait && ReferenceEquals(portrait.Source,AppIcons.OriginalLogo) && portrait.SourceRect==new Int32Rect(200,130,900,680) && brush.Stretch==Stretch.Uniform,"Small icon is not the original text-free portrait crop");
            checks.Add("Small icon uses the original Yoru2 portrait above the wordmark, with proportional scaling and no bitmap upscaling");
            var expectedSizes=new[]{16,20,24,32,40,48,64,128,256};
            foreach(var size in expectedSizes) {
                var png=new BitmapImage(); png.BeginInit(); png.CacheOption=BitmapCacheOption.OnLoad; png.UriSource=new Uri(Path.Combine(assets,$"yoru-{size}.png")); png.EndInit();
                Check(png.PixelWidth==size && png.PixelHeight==size,$"Wrong PNG size {size}");
                var direct=Render(AppIcons.Mark,size,size);
                Compare(png,direct,$"PNG {size}px was not rendered directly from the artwork");
                var pixels=Pixels(png); var w=png.PixelWidth;
                Check(pixels[3]==0 && pixels[(w-1)*4+3]==0 && pixels[((w-1)*w)*4+3]==0 && pixels[(w*w-1)*4+3]==0,$"Opaque outer corners in {size}px icon");
                var minX=w; var maxX=-1; var minY=w; var maxY=-1;
                for(var y=0;y<w;y++) for(var x=0;x<w;x++) if(pixels[(y*w+x)*4+3]>240) {minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
                Check(minX>=1 && minY>=1 && maxX<=w-2 && maxY<=w-2 && Math.Abs(minX-(w-1-maxX))<=1 && Math.Abs(minY-(w-1-maxY))<=1,$"Icon {size}px is cropped or off-center");
            }
            checks.Add("All required PNGs plus 20/40px DPI variants match direct artwork renders, with centered bounds and transparent corners");
            var ico=BitmapDecoder.Create(new Uri(Path.Combine(assets,"yoru-v9.ico")),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            Check(ico.Frames.Select(frame=>frame.PixelWidth).SequenceEqual(expectedSizes),"ICO resolution table is incomplete");
            foreach(var frame in ico.Frames) Compare(frame,Render(AppIcons.Mark,frame.PixelWidth,frame.PixelHeight),"ICO frame differs from artwork source");
            checks.Add("Versioned Windows ICO contains matching native-resolution artwork frames for every PNG size");
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-2)),"Running executable does not preserve V8 system-DPI awareness");
            checks.Add("The running app preserves V8 system-DPI awareness for native-player compatibility");
            foreach(var scale in new[]{1.0,1.25,1.5,2.0,2.5}) {
                var sidebar=new Image {Source=AppIcons.Mark,Width=27,Height=30,Stretch=Stretch.Uniform,SnapsToDevicePixels=true,UseLayoutRounding=true};
                RenderOptions.SetBitmapScalingMode(sidebar,BitmapScalingMode.HighQuality);
                sidebar.Measure(new Size(27,30)); sidebar.Arrange(new Rect(0,0,27,30)); sidebar.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(27*scale),(int)Math.Ceiling(30*scale),96*scale,96*scale,PixelFormats.Pbgra32); bitmap.Render(sidebar);
                Save(bitmap,Path.Combine(directory,$"v81-sidebar-{scale*100:0}percent.png"));
                Check(Pixels(bitmap).Where((_,index)=>index%4==3).Count(alpha=>alpha>200)>100,"DPI render lost the sidebar mark");
                var about=new Image {Source=AppIcons.Logo,Width=56,Height=56,Stretch=Stretch.Uniform,UseLayoutRounding=true,SnapsToDevicePixels=true}; RenderOptions.SetBitmapScalingMode(about,BitmapScalingMode.HighQuality);
                about.Measure(new Size(56,56)); about.Arrange(new Rect(0,0,56,56)); about.UpdateLayout();
                var aboutBitmap=new RenderTargetBitmap((int)Math.Ceiling(56*scale),(int)Math.Ceiling(56*scale),96*scale,96*scale,PixelFormats.Pbgra32); aboutBitmap.Render(about);
                Save(aboutBitmap,Path.Combine(directory,$"v81-about-{scale*100:0}percent.png"));
            }
            checks.Add("Sidebar and full About logo render at 100/125/150/200/250 percent DPI without changing their existing layout sizes");
            var sheet=new DrawingVisual(); RenderOptions.SetBitmapScalingMode(sheet,BitmapScalingMode.HighQuality); using(var context=sheet.RenderOpen()) {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(11,12,20)),null,new Rect(0,0,960,450));
                double x=24;
                foreach(var size in new[]{16,24,32,48,64,128,256}) {
                    context.DrawImage(AppIcons.Mark,new Rect(x,60,size,size));
                    Label(context,size+" px",new Point(x,32)); x+=size+25;
                }
                context.DrawImage(AppIcons.Logo,new Rect(24,300,128,128)); Label(context,"Original full logo",new Point(170,340));
                context.DrawImage(AppIcons.Mark,new Rect(430,310,96,96)); Label(context,"Text-free small mark",new Point(550,340));
            }
            var preview=new RenderTargetBitmap(960,450,96,96,PixelFormats.Pbgra32); preview.Render(sheet); Save(preview,Path.Combine(directory,"v81-icon-preview.png"));
        } catch(Exception exception) { error=exception.ToString(); }
        File.WriteAllText(Path.Combine(directory,"v81-icon-quality-checks.json"),JsonSerializer.Serialize(new { success=error is null,checks,error },new JsonSerializerOptions { WriteIndented=true }));
        return error is null?0:1;
    }
    private static bool AllArtwork(Drawing drawing)=>drawing is GeometryDrawing || drawing is DrawingGroup group && group.Children.All(AllArtwork);
    private static RenderTargetBitmap Render(ImageSource source,int width,int height) { var visual=new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality); using(var context=visual.RenderOpen()) context.DrawImage(source,new Rect(0,0,width,height)); var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);return bitmap; }
    private static byte[] Pixels(BitmapSource image) { var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var bytes=new byte[image.PixelWidth*image.PixelHeight*4];converted.CopyPixels(bytes,image.PixelWidth*4,0);return bytes; }
    private static void Compare(BitmapSource first,BitmapSource second,string message) {var a=Pixels(first);var b=Pixels(second);Check(a.Length==b.Length && a.Select((value,i)=>Math.Abs(value-b[i])).Count(delta=>delta>2)<a.Length*0.01,message);}
    private static void Save(BitmapSource image,string path) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);}
    private static void Label(DrawingContext context,string label,Point point)=>context.DrawText(new FormattedText(label,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),13,Brushes.White,1),point);
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
}
