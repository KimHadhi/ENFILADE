# Runs the project's code tools (Tools/CodeTools) without Unity: builds them when their source is newer, then hands the
# arguments on. With no arguments it is the compile check: the game's and the editor's scripts as the editor compiles
# them, and the game's again as a player build does. A check writes nothing into the project.
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Check-Code.ps1
#   ... Tools\Check-Code.ps1 dead        (what nothing uses: Logs\CodeTools\dead.txt)
#   ... Tools\Check-Code.ps1 dupes       (copies of one method: Logs\CodeTools\dupes.txt)
# The commands are listed in Tools\CodeTools\README.md. Keep this file ASCII: Windows PowerShell 5.1 reads it as ANSI.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$tool = Join-Path $PSScriptRoot 'CodeTools'
$output = Join-Path $tool 'bin\Release'
function Get-Built {
    if (!(Test-Path -LiteralPath $output)) { return $null }
    Get-ChildItem -LiteralPath $output -Recurse -Filter 'Enf.dll' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
}
$built = Get-Built
$newest = (Get-ChildItem -LiteralPath $tool -File | Where-Object { $_.Extension -eq '.cs' -or $_.Extension -eq '.csproj' } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if (!$built -or $built.LastWriteTimeUtc -lt $newest) {
    & dotnet build (Join-Path $tool 'Enf.csproj') -c Release -v q -clp:NoSummary | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The code tools did not build.' }
    $built = Get-Built
}
$arguments = if ($args.Count -gt 0) { $args } else { @('check', '--player') }
& dotnet $built.FullName @arguments
exit $LASTEXITCODE
