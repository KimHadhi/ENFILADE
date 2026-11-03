param(
    [Parameter(Mandatory)] [string] $Gma,
    [Parameter(Mandatory)] [string] $Pattern,
    [Parameter(Mandatory)] [string] $OutDir
)
# Reads a Garry's Mod .gma archive (GMAD v3) and writes the files whose path matches $Pattern into $OutDir.
$ErrorActionPreference = 'Stop'
$bytes = [System.IO.File]::ReadAllBytes($Gma)
$pos = 0
function Read-CString {
    $start = $script:pos
    while ($bytes[$script:pos] -ne 0) { $script:pos++ }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $start, $script:pos - $start)
    $script:pos++
    return $text
}
$magic = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4)
if ($magic -ne 'GMAD') { throw "Not a GMA archive: $Gma" }
$version = $bytes[4]
$pos = 5 + 8 + 8
if ($version -gt 1) { while ((Read-CString) -ne '') { } }
$null = Read-CString; $null = Read-CString; $null = Read-CString
$pos += 4
$entries = @()
while ($true) {
    $number = [BitConverter]::ToUInt32($bytes, $pos); $pos += 4
    if ($number -eq 0) { break }
    $name = Read-CString
    $size = [BitConverter]::ToInt64($bytes, $pos); $pos += 8
    $pos += 4
    $entries += [pscustomobject]@{ Name = $name; Size = $size }
}
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($entry in $entries) {
    if ($entry.Name -match $Pattern) {
        $target = Join-Path $OutDir ([System.IO.Path]::GetFileName($entry.Name))
        $data = New-Object byte[] $entry.Size
        [Array]::Copy($bytes, $pos, $data, 0, $entry.Size)
        [System.IO.File]::WriteAllBytes($target, $data)
        Write-Output ("{0} {1}" -f $entry.Size, $entry.Name)
    }
    $pos += $entry.Size
}
