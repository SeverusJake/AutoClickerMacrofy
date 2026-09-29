$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & dotnet publish src/Macrofy.App/Macrofy.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:NuGetLockFilePath=packages.publish-win-x64.lock.json -p:RestoreLockedMode=true -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Macrofy UI publish failed.' }
    $appExecutable = Join-Path $projectRoot 'artifacts/win-x64/Macrofy.exe'
    if (!(Test-Path -LiteralPath $appExecutable)) { throw 'Macrofy executable missing.' }
    $probeExecutable = Join-Path $projectRoot 'artifacts/compatibility-probe/win-x64/Macrofy.CompatibilityProbe.exe'
    if (Test-Path -LiteralPath $probeExecutable) {
        $probeFolder = Join-Path $projectRoot 'artifacts/win-x64/CompatibilityProbe'
        New-Item -ItemType Directory -Path $probeFolder -Force | Out-Null
        Copy-Item -LiteralPath $probeExecutable -Destination $probeFolder
    }
    Get-Item -LiteralPath $appExecutable | Select-Object FullName,Length
} finally { Pop-Location }
