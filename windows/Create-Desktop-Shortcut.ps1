$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'YORU.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Extract the complete YORU package before creating its shortcut.' }
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'YORU.lnk'))
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $PSScriptRoot
$icon = Join-Path $PSScriptRoot 'Assets\yoru-v9.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw 'The versioned YORU icon is missing. Extract the complete package.' }
$shortcut.IconLocation = "$icon,0"
$shortcut.Description = 'YORU'
$shortcut.Save()
# Notify Explorer about this shortcut without deleting icon caches or restarting it.
if (-not ('YoruShortcutShell' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class YoruShortcutShell {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern void SHChangeNotify(uint eventId,uint flags,string item,IntPtr other);
}
'@
}
[YoruShortcutShell]::SHChangeNotify(0x00002000,0x0005,(Join-Path $desktop 'YORU.lnk'),[IntPtr]::Zero)
Write-Output (Join-Path $desktop 'YORU.lnk')
