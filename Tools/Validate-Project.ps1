# Run the working copy's checks in a fresh, disposable Unity project.
[CmdletBinding()]
param(
    [ValidateSet('EditMode', 'Slide', 'Defense', 'Execution', 'Ranged', 'GunFeel', 'DamageFeedback', 'Ammo', 'Console', 'Ultimate', 'Reload')]
    [string[]]$Checks = @('EditMode'),
    [switch]$All,
    [string]$UnityEditor,
    [ValidateRange(60, 7200)]
    [int]$TimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
$version = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '(?m)^m_EditorVersion: (\S+)').Groups[1].Value
if (!$version) { throw 'Unity version is missing from ProjectVersion.txt.' }
if (!$UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (!(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity $version was not found. Supply -UnityEditor with its absolute executable path." }

$catalog = [ordered]@{
    EditMode = @('ProjectChecks.RunEditMode', 'PROJECT_EDIT_MODE_CHECKS_PASSED', $true)
    Slide = @('SlideCombatPlayChecks.RunBatch', 'SLIDE_COMBAT_PLAY_CHECKS_PASSED', $false)
    Defense = @('PerfectDefensePlayChecks.RunBatch', 'PERFECT_DEFENSE_PLAY_CHECKS_PASSED', $false)
    Execution = @('ExecutionCrowdPlayChecks.RunBatch', 'EXECUTION_CROWD_PLAY_CHECKS_PASSED', $false)
    Ranged = @('RangedCombatPlayChecks.RunBatch', 'RANGED_COMBAT_PLAY_CHECKS_PASSED', $false)
    GunFeel = @('GunFeelPlayChecks.RunBatch', 'GUN_FEEL_PLAY_CHECKS_PASSED', $false)
    DamageFeedback = @('PlayerDamageFeedbackPlayChecks.RunBatch', 'PLAYER_DAMAGE_FEEDBACK_PLAY_CHECKS_PASSED', $false)
    Ammo = @('AmmoHUDChecks.RunBatch', 'AMMO_HUD_CHECKS_PASSED', $false)
    Console = @('ConsoleSpawnPlayChecks.RunBatch', 'CONSOLE_SPAWN_PLAY_CHECKS_PASSED', $false)
    Ultimate = @('PlayerConsoleChecks.RunBatch', 'PLAYER_CONSOLE_CHECKS_PASSED', $false)
    Reload = @('RuntimeReloadChecks.RunBatch', 'RUNTIME_RELOAD_CHECKS_PASSED', $false)
}
if ($All) { $Checks = @($catalog.Keys) }
$Checks = @($Checks | Select-Object -Unique)

# A new directory per run avoids stale assets and never deletes the source or an older run.
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot ".validation/$runName"
$copyRoot = Join-Path $runRoot 'Project'
$logRoot = Join-Path $runRoot 'Logs'
New-Item -ItemType Directory -Path $copyRoot, $logRoot -Force | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $copyRoot $folder) /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder (robocopy exit $LASTEXITCODE)." }
}

$revision = $null
$workingChanges = @()
if (Get-Command git -ErrorAction SilentlyContinue) {
    $revision = & git -C $projectRoot rev-parse HEAD
    $workingChanges = @(& git -C $projectRoot status --short)
}
$report = [ordered]@{
    startedAt = (Get-Date).ToString('o')
    source = $projectRoot
    revision = $revision
    workingChanges = $workingChanges
    unityVersion = $version
    validationProject = $copyRoot
    results = @()
}
$resultPath = Join-Path $runRoot 'results.json'
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
Write-Output "Validation copy: $copyRoot"
$failed = $false
foreach ($name in $Checks) {
    $entry = $catalog[$name]
    $logPath = Join-Path $logRoot "$name.log"
    $arguments = '-batchmode -projectPath "{0}" -executeMethod {1} -logFile "{2}"' -f $copyRoot, $entry[0], $logPath
    if ($entry[2]) { $arguments += ' -quit' }
    Write-Output "Running $name (first import may take several minutes)..."
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $nextNotice = 30
    try {
        while (!$process.WaitForExit(1000)) {
            if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                $process.Kill()
                $process.WaitForExit()
                break
            }
            if ($clock.Elapsed.TotalSeconds -ge $nextNotice) {
                Write-Output "Waiting for $name ($([int]$clock.Elapsed.TotalSeconds)s); log: $logPath"
                $nextNotice += 30
            }
        }
        $process.Refresh()
        $exitCode = $process.ExitCode
    }
    finally {
        # Ctrl+C or a script error must not leave this script's hidden editor running.
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    [string]$log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
    $passed = !$timedOut -and $exitCode -eq 0 -and $log.Contains($entry[1])
    $report.results += [ordered]@{
        check = $name; method = $entry[0]; passed = $passed; exitCode = $exitCode
        timedOut = $timedOut; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); log = $logPath
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if (!$passed) {
        $failed = $true
        Write-Warning "$name FAILED (exit $exitCode, timed out: $timedOut). See $logPath"
        Select-String -LiteralPath $logPath -Pattern 'Exception:|error CS|Shader error|Aborting|executeMethod.*threw' -ErrorAction SilentlyContinue |
            Select-Object -First 8 -ExpandProperty Line | Write-Output
    }
    else { Write-Output "$name PASSED ($([int]$clock.Elapsed.TotalSeconds)s)." }
}
Write-Output "Results: $resultPath"
if ($failed) { throw 'One or more checks failed. Inspect results.json and the individual logs.' }
