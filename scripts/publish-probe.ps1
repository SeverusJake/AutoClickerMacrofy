$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & dotnet publish tools/Macrofy.CompatibilityProbe/Macrofy.CompatibilityProbe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:NuGetLockFilePath=packages.publish-win-x64.lock.json -p:RestoreLockedMode=true -o artifacts/compatibility-probe/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Probe publish failed.' }
    $probeExecutable = Join-Path $projectRoot 'artifacts/compatibility-probe/win-x64/Macrofy.CompatibilityProbe.exe'
    if (!(Test-Path -LiteralPath $probeExecutable)) { throw 'Probe executable missing.' }
    Get-Item -LiteralPath $probeExecutable | Select-Object FullName,Length
} finally { Pop-Location }
