using System.Runtime.InteropServices;
namespace Yoru;

internal static class AppIdentity
{
    internal const string TaskbarId = "YORU.Desktop";
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string value);
    internal static void Initialize() => _ = SetCurrentProcessExplicitAppUserModelID(TaskbarId);
}
