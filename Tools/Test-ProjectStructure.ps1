# Fast checks without Unity, Python, or an imported Library.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $projectRoot 'Assets'
$problems = [System.Collections.Generic.List[string]]::new()
$docs = @(Get-Item -LiteralPath (Join-Path $projectRoot 'README.md'))
$docs += @(Get-ChildItem -LiteralPath (Join-Path $assetRoot '_Project') -Recurse -File -Filter '*.md')
# The documents outside Assets: the guides, the mods folder's guide, the tools' and the Blender workspaces' notes.
foreach ($folder in 'Docs', 'Mods', 'Tools', 'ArtSource') {
    $path = Join-Path $projectRoot $folder
    if (Test-Path -LiteralPath $path) { $docs += @(Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.md') }
}
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

# Tables are found by file name alone (GameTables), so a name may stand in one folder of Data/Resources/Tables only.
$tables = @{}
foreach ($table in Get-ChildItem -LiteralPath (Join-Path $assetRoot '_Project\Data\Resources\Tables') -Recurse -File -Filter '*.csv') {
    if ($tables.ContainsKey($table.BaseName)) { $problems.Add("Two tables named $($table.BaseName): $($table.FullName) and $($tables[$table.BaseName])") }
    else { $tables[$table.BaseName] = $table.FullName }
}

# What the game finds by name lies in the drawer of its kind (ProjectFolders.cs): pictures, sounds, shaders and clips
# in Art/Resources, tables and lists in Data/Resources, prefabs in Prefabs/Resources. No other Resources folder, and
# one name from the drawers means one file of a kind (Resources.Load takes the path without the extension).
$drawers = @{
    'Art\Resources' = @('.png', '.jpg', '.jpeg', '.tga', '.psd', '.hdr', '.exr', '.ogg', '.mp3', '.wav', '.fbx', '.shader', '.hlsl', '.mat', '.controller', '.anim', '.shadergraph')
    'Data\Resources' = @('.asset', '.csv', '.json', '.txt')
    'Prefabs\Resources' = @('.prefab')
}
$project = Join-Path $assetRoot '_Project'
$byName = @{}
foreach ($folder in Get-ChildItem -LiteralPath $project -Recurse -Directory -Filter 'Resources') {
    $relative = $folder.FullName.Substring($project.Length + 1)
    if (!$drawers.ContainsKey($relative)) { $problems.Add("A Resources folder outside the three drawers: $($folder.FullName)"); continue }
    foreach ($file in Get-ChildItem -LiteralPath $folder.FullName -Recurse -File) {
        if ($file.Extension -eq '.meta') { continue }
        $extension = $file.Extension.ToLowerInvariant()
        if ($drawers[$relative] -notcontains $extension) { $problems.Add("Wrong drawer for its kind ($relative): $($file.FullName)") }
        $name = $file.FullName.Substring($folder.FullName.Length + 1)
        $name = $name.Substring(0, $name.Length - $file.Extension.Length).ToLowerInvariant() + '|' + $extension
        if ($byName.ContainsKey($name)) { $problems.Add("One name, two files: $($file.FullName) and $($byName[$name])") }
        else { $byName[$name] = $file.FullName }
    }
}

$guids = @{}
$assetCount = 0
foreach ($item in Get-ChildItem -LiteralPath $assetRoot -Recurse -Force) {
    # Unity skips names starting with '.' or ending with '~' (and everything inside such folders) and gives them no
    # metadata, e.g. the Poiyomi shader cache's .thrylock.
    $relative = $item.FullName.Substring($assetRoot.Length).TrimStart('\', '/')
    if ($relative -split '[\\/]' | Where-Object { $_.StartsWith('.') -or $_.EndsWith('~') }) { continue }
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
Write-Output "PROJECT_STRUCTURE_CHECKS_PASSED: $($docs.Count) documents, $($tables.Count) tables, $assetCount assets/folders, $($guids.Count) unique GUIDs."
