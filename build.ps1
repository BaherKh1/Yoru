param([string]$JavaSdkDirectory = $env:JAVA_HOME,[string]$AndroidSdkDirectory)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$toolchain=Join-Path $root 'toolchain'
$dotnet=Join-Path $toolchain 'dotnet/dotnet.exe'
if(-not (Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
if(-not $AndroidSdkDirectory){$AndroidSdkDirectory=Join-Path $toolchain 'android-sdk'}
if(-not $JavaSdkDirectory -and (Test-Path -LiteralPath 'C:/Program Files/Java/jdk-21.0.10')){$JavaSdkDirectory='C:/Program Files/Java/jdk-21.0.10'}
if(-not $JavaSdkDirectory -or -not (Test-Path -LiteralPath (Join-Path $JavaSdkDirectory 'bin/java.exe'))){throw 'Pass -JavaSdkDirectory with an installed JDK 21 folder. See BUILD.md.'}
if(-not (Test-Path -LiteralPath (Join-Path $AndroidSdkDirectory 'platforms/android-36/android.jar'))){throw 'Android API 36/build-tools 36.0.0 are missing. Run setup.ps1 or follow BUILD.md.'}
$env:DOTNET_CLI_HOME=Join-Path $toolchain 'cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $toolchain 'packages'
if(Test-Path -LiteralPath (Join-Path $toolchain 'dotnet/dotnet.exe')){$env:DOTNET_ROOT=Join-Path $toolchain 'dotnet'}
Push-Location $root
try {
 $feed=Join-Path $toolchain 'offline-feed'
 if(Test-Path -LiteralPath $feed){& $dotnet restore src/Yoru.Android/Yoru.Android.csproj --source $feed -p:NuGetAudit=false}
 else{& $dotnet restore src/Yoru.Android/Yoru.Android.csproj --configfile NuGet.Config}
 if($LASTEXITCODE -ne 0){throw 'Restore failed'}
 & $dotnet build src/Yoru.Android/Yoru.Android.csproj -c Release -t:SignAndroidPackage --no-restore "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory" -m:1 -nr:false
 if($LASTEXITCODE -ne 0){throw 'APK build failed'}
 New-Item -ItemType Directory -Path artifacts -Force | Out-Null
 Copy-Item -LiteralPath src/Yoru.Android/bin/Release/net10.0-android/app.yoru.android-Signed.apk -Destination artifacts/YORU-Android-V1.0.apk -Force
 Write-Output 'APK: artifacts/YORU-Android-V1.0.apk (development certificate; see BUILD.md for production signing).'
} finally {Pop-Location}
