using System.Runtime.InteropServices;
namespace AnimeWitcherDesktop;

// WM_GETMINMAXINFO and GetMonitorInfo use the same native coordinate space.
// Do not mix these bounds with primary-screen sizes or WPF/DIP taskbar estimates.
internal static class PlayerWindowBounds
{
    internal const int GetMinMaxInfo = 0x0024;
    [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle {public int Left,Top,Right,Bottom;public Rectangle(int left,int top,int right,int bottom){Left=left;Top=top;Right=right;Bottom=bottom;}}
    [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo {public Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public Rectangle Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    internal static void ApplyWorkingArea(IntPtr hwnd,IntPtr pointer)
    {
        if(pointer==IntPtr.Zero) return;
        var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info)) return;
        var limits=Marshal.PtrToStructure<MinMaxInfo>(pointer);
        if(!SetMaximizedBounds(ref limits,info.Monitor,info.Work)) return;
        Marshal.StructureToPtr(limits,pointer,false);
    }
    internal static bool SetMaximizedBounds(ref MinMaxInfo limits,Rectangle monitor,Rectangle work)
    {
        var width=work.Right-work.Left;var height=work.Bottom-work.Top;
        if(width<=0 || height<=0) return false;
        limits.MaxPosition=new Point(work.Left-monitor.Left,work.Top-monitor.Top);
        limits.MaxSize=new Point(width,height);
        // Preserve track sizes. The hook remains unhandled so WPF can still enforce
        // MinWidth/MinHeight and any normal resize limits after this adjustment.
        return true;
    }
}
