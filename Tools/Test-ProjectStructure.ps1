# Fast checks without Unity, Python, or an imported Library.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $projectRoot 'Assets'
$problems = [System.Collections.Generic.List[string]]::new()
$docs = @(Get-Item -LiteralPath (Join-Path $projectRoot 'README.md'))
$docs += @(Get-ChildItem -LiteralPath (Join-Path $assetRoot '_Project') -Recurse -File -Filter '*.md')
foreach ($doc in $docs) {
    $body = Get-Content -LiteralPath $doc.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($body, '\]\(([^)]+)\)')) {
        $link = $match.Groups[1].Value.Trim('<', '>')
        if ($link -match '^([a-z][a-z0-9+.-]*:|#)') { continue }
        $relativePath = [uri]::UnescapeDataString(($link -split '#', 2)[0])
        $target = Join-Path $doc.DirectoryName $relativePath
        if (!(Test-Path -LiteralPath $target)) { $problems.Add("Broken link: $($doc.FullName) -> $link") }
    }
}

$guids = @{}
$assetCount = 0
foreach ($item in Get-ChildItem -LiteralPath $assetRoot -Recurse -Force) {
    if ($item.Extension -eq '.meta' -and !$item.PSIsContainer) {
        $assetPath = $item.FullName.Substring(0, $item.FullName.Length - 5)
        if (!(Test-Path -LiteralPath $assetPath)) { $problems.Add("Orphan metadata: $($item.FullName)") }
        $match = [regex]::Match((Get-Content -LiteralPath $item.FullName -Raw), '(?m)^guid: ([a-f0-9]{32})\s*$')
        if (!$match.Success) { $problems.Add("Invalid metadata GUID: $($item.FullName)"); continue }
        $guid = $match.Groups[1].Value
        if ($guids.ContainsKey($guid)) { $problems.Add("Duplicate GUID: $($item.FullName) and $($guids[$guid])") }
        else { $guids[$guid] = $item.FullName }
    }
    else {
        $assetCount++
        if (!(Test-Path -LiteralPath ($item.FullName + '.meta'))) { $problems.Add("Missing metadata: $($item.FullName)") }
    }
}
if ($problems.Count -gt 0) {
    $problems | Write-Output
    throw "Project structure checks failed: $($problems.Count) problem(s)."
}
Write-Output "PROJECT_STRUCTURE_CHECKS_PASSED: $($docs.Count) documents, $assetCount assets/folders, $($guids.Count) unique GUIDs."
