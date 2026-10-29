$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    powershell -Sta -ExecutionPolicy Bypass -File Assets/build-icon.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
    dotnet restore --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet publish -c Release --no-restore -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $assetTarget = Join-Path $PSScriptRoot 'dist\Assets'
    New-Item -ItemType Directory -Path $assetTarget -Force | Out-Null
    Copy-Item -Path 'Assets/yoru-original.png','Assets/yoru-icon.png','Assets/yoru-[0-9]*.png','Assets/yoru-v9.ico','Assets/branding-assets.json' -Destination $assetTarget -Force
    $vlcSource = Join-Path $env:ProgramFiles 'VideoLAN\VLC'
    if (-not (Test-Path -LiteralPath (Join-Path $vlcSource 'libvlc.dll'))) { throw 'VLC 3 x64 must be available to bundle the native playback engine.' }
    $vlcTarget = Join-Path $PSScriptRoot 'dist\vlc'
    New-Item -ItemType Directory -Path $vlcTarget -Force | Out-Null
    foreach ($file in @('libvlc.dll', 'libvlccore.dll', 'COPYING.txt', 'AUTHORS.txt', 'THANKS.txt')) {
        Copy-Item -LiteralPath (Join-Path $vlcSource $file) -Destination $vlcTarget -Force
    }
    foreach ($folder in @('plugins', 'lua', 'hrtfs')) {
        Copy-Item -LiteralPath (Join-Path $vlcSource $folder) -Destination $vlcTarget -Recurse -Force
    }
    Copy-Item -LiteralPath 'README.md','VALIDATION.md','PERFORMANCE.md','V9-CHANGES.md','V9.1-CHANGES.md','V9.2-CHANGES.md','V9.3-CHANGES.md','BRANDING.md','STARTUP.md','Create-Desktop-Shortcut.ps1','THIRD-PARTY.md' -Destination dist -Force
} finally { Pop-Location }
