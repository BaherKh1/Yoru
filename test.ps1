$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 python scripts/run_dotnet.py restore tests/Yoru.Core.Checks/Yoru.Core.Checks.csproj --source toolchain/offline-feed -p:NuGetAudit=false
 if($LASTEXITCODE -ne 0){throw 'Test restore failed'}
 python scripts/run_dotnet.py run --project tests/Yoru.Core.Checks/Yoru.Core.Checks.csproj -c Release --no-restore
 if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
} finally {Pop-Location}
