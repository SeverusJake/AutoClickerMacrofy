$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & dotnet restore Macrofy.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build Macrofy.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    & dotnet test Macrofy.sln -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Probe tests failed.' }
} finally { Pop-Location }
