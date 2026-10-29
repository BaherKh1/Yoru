param([string]$JavaSdkDirectory = $env:JAVA_HOME)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$toolchain=Join-Path $root 'toolchain'
New-Item -ItemType Directory -Path $toolchain -Force | Out-Null
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {throw 'Python 3 is needed for the checksum-verified offline setup. Install Python, or use the standard SDK instructions in BUILD.md.'}
Push-Location $root
try {
 foreach($name in @('sdk-download.json','workload-packages.json','android-downloads.json')){Copy-Item -LiteralPath (Join-Path $root "scripts/$name") -Destination $toolchain -Force}
 $sdk=Join-Path $toolchain 'dotnet/dotnet.exe'
 if(-not (Test-Path -LiteralPath $sdk)) {
  python scripts/install_dotnet.py
  if($LASTEXITCODE -ne 0){throw 'SDK setup failed'}
 }
 python scripts/fetch_workload.py
 if($LASTEXITCODE -ne 0){throw 'Android workload setup failed'}
 python scripts/fetch_android_sdk.py
 if($LASTEXITCODE -ne 0){throw 'Android SDK setup failed'}
 python scripts/fetch_packages.py
 if($LASTEXITCODE -ne 0){throw 'Application dependencies setup failed'}
 Write-Output 'Android toolchain installed in toolchain/. Windows SDK/project settings were not changed.'
 Write-Output 'Use build.ps1 -JavaSdkDirectory <JDK 21 folder> to create the APK.'
} finally {Pop-Location}
