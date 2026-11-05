# Compile-checks Assembly-CSharp and its Editor assembly without Unity, from the csproj files Unity last wrote.
# Each run builds in its own temporary folder, so several sessions can run it at once. Keep this file ASCII:
# Windows PowerShell 5.1 reads it as ANSI.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Check-Compile.ps1
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Work = (Join-Path $env:TEMP ('enfilade-compile-' + [guid]::NewGuid().ToString('N')))
)
$scripts = Join-Path $Root 'Assets\_Project'
$dlls = Join-Path $Root 'Library\ScriptAssemblies'
New-Item -ItemType Directory -Force $Work | Out-Null

function Convert-Project([string]$name, [bool]$keepRuntimeReference) {
    $text = [IO.File]::ReadAllText((Join-Path $Root "$name.csproj"))
    $text = [regex]::Replace($text, '(<(?:Compile|None|Analyzer|AdditionalFiles|Content|EmbeddedResource) Include=")(?![A-Za-z]:)', { param($m) $m.Groups[1].Value + $Root + '\' })
    $text = [regex]::Replace($text, '<HintPath>(?![A-Za-z]:)', "<HintPath>$Root\")
    $text = [regex]::Replace($text, '<(?:RuleSet|CodeAnalysisRuleSet)>[^<]*</(?:RuleSet|CodeAnalysisRuleSet)>', '')

    $names = New-Object System.Collections.Generic.List[string]
    $text = [regex]::Replace($text, '<ProjectReference Include="([^"]+)\.csproj">?[\s\S]*?(?:</ProjectReference>|/>)', {
        param($m)
        $reference = $m.Groups[1].Value
        if ($keepRuntimeReference -and $reference -eq 'Assembly-CSharp') { return '<ProjectReference Include="Assembly-CSharp.csproj" />' }
        $names.Add($reference); ''
    })
    $extra = ($names | Where-Object { Test-Path (Join-Path $dlls "$_.dll") } | ForEach-Object {
        "<Reference Include=`"$_`"><HintPath>$dlls\$_.dll</HintPath></Reference>"
    }) -join "`n"

    # Unity references every package assembly by itself; the csproj can lag behind a newly installed package.
    $services = Get-ChildItem $dlls -Filter 'Unity.Services.*.dll' | Where-Object { $_.Name -notmatch 'Editor' -and $text -notmatch [regex]::Escape("Include=`"$($_.BaseName)`"") } |
        ForEach-Object { "<Reference Include=`"$($_.BaseName)`"><HintPath>$($_.FullName)</HintPath></Reference>" }
    $extra += "`n" + ($services -join "`n")

    # Files Unity hasn't listed yet are added, files it lists that are gone are dropped.
    $listed = @{}
    foreach ($m in [regex]::Matches($text, '<Compile Include="([^"]+)"')) { $listed[$m.Groups[1].Value.ToLowerInvariant()] = $true }
    $text = [regex]::Replace($text, '\s*<Compile Include="([^"]+)"\s*/>', { param($m) if (Test-Path -LiteralPath $m.Groups[1].Value) { $m.Value } else { '' } })
    $folder = if ($name -eq 'Assembly-CSharp') { 'Scripts' } else { 'Editor' }
    $new = Get-ChildItem (Join-Path $scripts $folder) -Recurse -Filter *.cs | Where-Object { -not $listed.ContainsKey($_.FullName.ToLowerInvariant()) } |
        ForEach-Object { "<Compile Include=`"$($_.FullName)`" />" }
    if ($new) { "added to ${name}: $(@($new).Count)" }
    $extra += "`n" + ($new -join "`n")
    $text = $text.Replace('</Project>', "<ItemGroup>`n$extra`n</ItemGroup>`n</Project>")
    [IO.File]::WriteAllText((Join-Path $Work "$name.csproj"), $text, (New-Object Text.UTF8Encoding($false)))
}

try {
    Convert-Project 'Assembly-CSharp' $false
    Convert-Project 'Assembly-CSharp-Editor' $true
    Push-Location $Work
    $output = & dotnet build 'Assembly-CSharp-Editor.csproj' --no-incremental -nologo -v q -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
    $exit = $LASTEXITCODE
    Pop-Location
    $errors = $output | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
    $warnings = $output | Where-Object { $_ -match ': warning CS' -and $_ -match '_Project' } | Sort-Object -Unique
    "errors: $($errors.Count), project warnings: $($warnings.Count), exit: $exit"
    $errors | Select-Object -First 40 | ForEach-Object { $_ -replace [regex]::Escape($Root + '\'), '' -replace '\s*\[[^\]]*\]$', '' }
    $warnings | Select-Object -First 12 | ForEach-Object { $_ -replace [regex]::Escape($Root + '\'), '' -replace '\s*\[[^\]]*\]$', '' }
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $Work
}
