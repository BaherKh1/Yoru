$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class YoruIconCacheCheck {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    public struct Info {
        public IntPtr Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Display;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path,uint attributes,out Info info,uint size,uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
}
'@
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'YORU.lnk'
$iconFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\Assets\yoru-v9.ico'
$info = New-Object YoruIconCacheCheck+Info
$size = [Runtime.InteropServices.Marshal]::SizeOf($info)
if ([YoruIconCacheCheck]::SHGetFileInfo($shortcutPath,0,[ref]$info,$size,0x100) -eq [IntPtr]::Zero) { throw 'Shell shortcut icon lookup failed' }
try {
    $actual = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon($info.Icon,[System.Windows.Int32Rect]::Empty,[System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
    $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$iconFile,[System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $expected = $decoder.Frames | Where-Object { $_.PixelWidth -eq $actual.PixelWidth } | Select-Object -First 1
    if ($null -eq $expected) { throw 'Shell chose an unexpected icon resolution' }
    $actual = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($actual,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
    $expected = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($expected,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
    $pixels = New-Object byte[] ($actual.PixelWidth * $actual.PixelHeight * 4)
    $reference = New-Object byte[] $pixels.Length
    $actual.CopyPixels($pixels,$actual.PixelWidth * 4,0)
    $expected.CopyPixels($reference,$expected.PixelWidth * 4,0)
    $compared = 0; $matching = 0
    for ($y=0; $y -lt $actual.PixelHeight; $y++) { for ($x=0; $x -lt $actual.PixelWidth; $x++) {
        # Windows adds its standard shortcut-arrow overlay in this quadrant.
        if ($x -le $actual.PixelWidth / 2 -and $y -ge $actual.PixelHeight / 2) { continue }
        $i = ($y * $actual.PixelWidth + $x) * 4
        if ($reference[$i+3] -gt 200 -and ($reference[$i+2] -gt 100 -or $reference[$i+1] -gt 100)) {
            $compared++
            if ([Math]::Abs([int]$pixels[$i]-[int]$reference[$i]) -lt 10 -and [Math]::Abs([int]$pixels[$i+1]-[int]$reference[$i+1]) -lt 10 -and [Math]::Abs([int]$pixels[$i+2]-[int]$reference[$i+2]) -lt 10) { $matching++ }
        }
    } }
    $preview = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $preview.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($actual))
    $previewPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'evidence\v81-shell-shortcut-icon.png'
    $previewStream = [IO.File]::Create($previewPath)
    try { $preview.Save($previewStream) } finally { $previewStream.Dispose() }
    if ($compared -lt 10 -or $matching / [double]$compared -lt 0.9) { throw "The shell shortcut icon differs: $matching / $compared matching glyph pixels" }
    [pscustomobject]@{success=$true;source='SHGetFileInfo desktop shortcut';width=$actual.PixelWidth;matchingGlyphPixels=$matching;comparedGlyphPixels=$compared} | ConvertTo-Json
} finally { if ($info.Icon -ne [IntPtr]::Zero) { [void][YoruIconCacheCheck]::DestroyIcon($info.Icon) } }
