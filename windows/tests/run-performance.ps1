$ErrorActionPreference = 'Stop'
$target = Split-Path -Parent $PSScriptRoot
$baseline = Join-Path (Split-Path -Parent $target) 'baseline\Yoru'
$evidence = Join-Path $target 'evidence'
# Run serially: a concurrent build/test would contaminate CPU and frame timings.
foreach ($variant in @(@{ Name='v5'; Root=$baseline }, @{ Name='v6'; Root=$target })) {
    Push-Location $variant.Root
    try {
        dotnet build -c Release --no-restore -p:NuGetAudit=false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Profiler build failed' }
        $exe = Join-Path $PWD 'bin\Release\net8.0-windows\Yoru.exe'
        foreach ($run in 1..3) {
            $process = Start-Process -FilePath $exe -ArgumentList '--performance-profile','evidence' -WindowStyle Hidden -PassThru -Wait
            if ($process.ExitCode -ne 0) { throw 'UI profile failed' }
            Copy-Item -LiteralPath 'evidence\performance.json' -Destination (Join-Path $evidence "performance-$($variant.Name)-$run.json") -Force
            Write-Output "$($variant.Name) UI profile $run complete"
        }
        foreach ($mode in @('automatic','software')) {
            $arguments = @('--playback-profile','tests/performance-av-1080p.mp4')
            if ($mode -eq 'software') { $arguments += '--software' }
            $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
            if ($process.ExitCode -ne 0) { throw 'Playback profile failed' }
            Copy-Item -LiteralPath 'bin\Release\net8.0-windows\playback-performance.json' -Destination (Join-Path $evidence "playback-$($variant.Name)-$mode.json") -Force
            Write-Output "$($variant.Name) $mode playback profile complete"
        }
    } finally { Pop-Location }
}
