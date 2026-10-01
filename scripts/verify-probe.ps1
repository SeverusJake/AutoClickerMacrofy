param([switch]$NoPopup)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

# Native tests move the real cursor and type into dedicated receiver windows. Warn before they
# start and report the result; each popup times out so unattended runs do not hang.
function Show-Popup([string]$text, [int]$seconds, [int]$type) {
    if ($NoPopup -or -not [Environment]::UserInteractive) { return -1 }
    return (New-Object -ComObject WScript.Shell).Popup($text, $seconds, 'Macrofy verification', $type + 4096)
}

Push-Location -LiteralPath $projectRoot
try {
    & dotnet restore Macrofy.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build Macrofy.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $start = Show-Popup ("Tests will move the mouse and type into test windows for about 30 seconds.`n`n" +
        "Do not touch the mouse or keyboard until the finish popup appears.`n`n" +
        "OK = start now, Cancel = abort. Starts automatically in 10 seconds.") 10 49
    if ($start -eq 2) { throw 'Verification cancelled before native tests.' }
    # Test projects run one at a time: parallel assemblies move the shared cursor under each other.
    & dotnet test Macrofy.sln -c Release --no-build --no-restore -m:1
    $passed = $LASTEXITCODE -eq 0
    if ($passed) { [void](Show-Popup 'All tests passed. You can use the mouse and keyboard again.' 30 64) }
    else { [void](Show-Popup 'Tests failed. You can use the mouse and keyboard again. See the console output.' 30 16) }
    if (-not $passed) { throw 'Probe tests failed.' }
} finally { Pop-Location }
