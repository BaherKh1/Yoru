using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Yoru;
internal static class WindowAppearance
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    public static void PlayerCorners(Window window, bool rounded)
    {
        var handle = new WindowInteropHelper(window).Handle; if(handle == IntPtr.Zero) return;
        var preference = rounded ? 2 : 1; // Windows 11 rounds the native frame without transparent video surfaces.
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }
    public static void DarkCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var enabled = 1; _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        var color = 0x00130B08; _ = DwmSetWindowAttribute(handle, 35, ref color, sizeof(int));
        var border = 0x00382724; _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }
}
