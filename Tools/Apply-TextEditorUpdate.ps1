param(
    [string] $ProjectPath,
    [switch] $NoRefresh
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$baseRef = 'dcd347b57fce84c686d31f0428c701d76b146fc4'
$updateRef = '27cdcfc51ecbf85d29dfd8dec02983c559c154b7'
$raw = 'https://raw.githubusercontent.com/KimHadhi/ENFILADE'
$editorFile = 'Assets/_Project/Editor/Texts/TextEditorWindow.cs'
$catalogFile = 'Assets/_Project/Data/Resources/Tables/Core/Texts.csv'
$catalogBlob = '963a32d29a1778fdfae56cc092bcc03f24aca9b5'
$scriptBlobs = [ordered]@{
    'Assets/_Project/Editor/Texts/TextEditorWindow.cs' = @('ec9419d878831122c6bf096483366a65433f92bb', '9550905101a392f2a3710055cd23302358532137')
    'Assets/_Project/Editor/Texts/TextSheet.cs' = @('685316db849d89a3171e078f78da8421a9110871', '73906ad2a304d11756843aced0b7e467567f7bc4')
    'Assets/_Project/Scripts/Core/Texts/Texts.cs' = @('597ca531ebc9f25169d4ed1aee57cbc6d3fb49b8', '55fa77a1e4513868c5aa303afcbfba24ef20080e')
    'Assets/_Project/Scripts/Core/Settings/GameSettings.cs' = @('78c2638f33e0e28341f0cfca46ae2fdad198d1e4', '3d5cd66a1600f6c3ac033d69a8e9e5cd853ee219')
    'Assets/_Project/Scripts/UI/Settings/SettingsMenuView.cs' = @('573d2c4f7f2e0e2b2f5a88b299c530cf4b1bdc5a', '2a289e7bba846b4598d243e6ef4ec68216001a61')
}
$editorLog = Join-Path $env:LOCALAPPDATA 'Unity/Editor/Editor.log'

function Test-EnfiladeProject([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (
        (Test-Path -LiteralPath (Join-Path $Path 'ProjectSettings/ProjectVersion.txt') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path $editorFile) -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path $catalogFile) -PathType Leaf)
    )
}

function Get-OpenUnityProjects {
    try {
        $arguments = '(?i)(?:^|\s)-projectPath(?:[ \t]+|=)(?:"(?<path>[^"]+)"|(?<path>[^ \t]+))'
        foreach ($process in Get-CimInstance -ClassName Win32_Process -Filter "Name='Unity.exe'") {
            if ($process.CommandLine -match $arguments) {
                $path = $Matches['path']
                if (Test-EnfiladeProject $path) { (Resolve-Path -LiteralPath $path).ProviderPath }
            }
        }
    }
    catch {
        # The editor log and the folder picker also work where process queries are restricted.
    }
}

function Find-Project([string] $Requested) {
    if ($Requested) {
        if (-not (Test-EnfiladeProject $Requested)) { throw "ENFILADE project files are missing under $Requested" }
        return (Resolve-Path -LiteralPath $Requested).ProviderPath
    }

    $openProjects = @(Get-OpenUnityProjects | Sort-Object -Unique)
    if ($openProjects.Count -eq 1) { return $openProjects[0] }

    if ($openProjects.Count -eq 0 -and (Test-Path -LiteralPath $editorLog -PathType Leaf)) {
        $argsInLog = '(?im)^[ \t]*-projectPath[ \t]*(?:\r?\n[ \t]*|[ \t]+)(?<path>[^\r\n]+)'
        $source = Get-Content -LiteralPath $editorLog -Encoding UTF8 -Raw
        if ($source -match $argsInLog) {
            $recent = $Matches['path'].Trim().Trim('"')
            if (Test-EnfiladeProject $recent) { return (Resolve-Path -LiteralPath $recent).ProviderPath }
        }
    }
    if ($openProjects.Count -eq 0 -and (Test-EnfiladeProject (Get-Location).Path)) {
        return (Get-Location).Path
    }

    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $picker.Description = 'Select the ENFILADE folder that contains Assets and ProjectSettings.'
        $picker.ShowNewFolderButton = $false
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Project selection was cancelled.' }
        if (-not (Test-EnfiladeProject $picker.SelectedPath)) { throw 'The selected folder does not contain the ENFILADE project.' }
        return $picker.SelectedPath
    }
    finally { $picker.Dispose() }
}

function Get-BlobHash([byte[]] $Content) {
    [byte[]] $header = [Text.Encoding]::ASCII.GetBytes("blob $($Content.Length)" + [char]0)
    $sha = [Security.Cryptography.SHA1]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([byte[]]($header + $Content))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Read-ReleaseFile([string] $Ref, [string] $Relative, [string] $Blob) {
    $download = Join-Path $scratch 'download'
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 20 -Uri "$raw/$Ref/$Relative" -OutFile $download
    [byte[]] $content = [IO.File]::ReadAllBytes($download)
    if ((Get-BlobHash $content) -ne $Blob) { throw "The downloaded $Relative failed its content check." }
    return $utf8.GetString($content)
}

function Normalize-Code([string] $Code) {
    return $Code.TrimStart([char]0xFEFF).Replace(([string][char]13 + [char]10), ([string][char]10))
}

function Merge-Code([string] $Relative, [string] $Current, [string] $Before, [string] $After) {
    $working = Normalize-Code $Current
    $ancestor = Normalize-Code $Before
    $incoming = Normalize-Code $After
    if ($working -ceq $incoming) { return $Current }

    $merged = $incoming
    if ($working -cne $ancestor) {
        $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $git) { throw "Local code has also changed in $Relative. Its versions need a Git three-way merge." }
        $localPath = Join-Path $scratch 'working.cs'
        $basePath = Join-Path $scratch 'base.cs'
        $updatePath = Join-Path $scratch 'incoming.cs'
        [IO.File]::WriteAllText($localPath, $working, $utf8)
        [IO.File]::WriteAllText($basePath, $ancestor, $utf8)
        [IO.File]::WriteAllText($updatePath, $incoming, $utf8)
        & $git.Source merge-file $localPath $basePath $updatePath
        if ($LASTEXITCODE -ne 0) { throw "Overlapping local code changes in $Relative." }
        $merged = [IO.File]::ReadAllText($localPath, $utf8)
    }

    $crlf = [string][char]13 + [char]10
    if ($Current.Contains($crlf)) { $merged = $merged.Replace([string][char]10, $crlf) }
    if ($Current.Length -gt 0 -and $Current[0] -eq [char]0xFEFF) { $merged = [string][char]0xFEFF + $merged }
    return $merged
}

function Replace-File([string] $Path, [byte[]] $Content) {
    $stage = Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.enfilade-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllBytes($stage, $Content)
        [IO.File]::Replace($stage, $Path, [NullString]::Value)
    }
    finally {
        if ([IO.File]::Exists($stage)) { [IO.File]::Delete($stage) }
    }
}

function Read-NewCompileError([long] $Offset) {
    if (-not [IO.File]::Exists($editorLog)) { return }
    $stream = [IO.File]::Open($editorLog, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        if ($Offset -le $stream.Length) { $stream.Position = $Offset }
        $reader = [IO.StreamReader]::new($stream)
        try {
            $errorMatch = [regex]::Match($reader.ReadToEnd(), '(?m)^.*\berror CS\d{4}:[^\r\n]*')
            if ($errorMatch.Success) { return $errorMatch.Value.Trim() }
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Test-CompiledToolbar([string] $Root) {
    $assembly = Join-Path $Root 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
    if (-not [IO.File]::Exists($assembly)) { return $false }
    try {
        $metadata = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($assembly))
        return $metadata.Contains('DrawLanguageToolbar' + [char]0)
    }
    catch [System.IO.IOException] { return $false }
}

function Request-UnityRefresh([string] $Root, [bool] $FilesChanged) {
    $tempDir = Join-Path $Root 'Temp'
    $refreshWatcher = Join-Path $Root 'Assets/_Project/Editor/Dev/RefreshOnRequest.cs'
    $lockFile = Join-Path $tempDir 'UnityLockfile'
    if ((-not [IO.File]::Exists($lockFile)) -or (-not [IO.File]::Exists($refreshWatcher))) {
        Write-Host 'Open this project in Unity to compile. For an open editor without the watcher, select Assets > Refresh.'
        return
    }

    if ((-not $FilesChanged) -and (Test-CompiledToolbar $Root)) {
        Write-Host 'UNITY_EDITOR_ASSEMBLY_VERIFIED. Reopen the text editor from the ENFILADE menu.'
        return
    }

    $request = Join-Path $tempDir 'enfilade-refresh.request'
    $done = Join-Path $tempDir 'enfilade-refresh.done'
    $offset = 0
    if ([IO.File]::Exists($editorLog)) { $offset = (Get-Item -LiteralPath $editorLog).Length }
    $requested = [DateTime]::UtcNow
    [IO.File]::WriteAllText($request, '')
    Write-Host 'Waiting for the open Unity editor to reload scripts. Exit Play mode if it is running.'
    $deadline = $requested.AddSeconds(25)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        try {
            if ((-not [IO.File]::Exists($request)) -and [IO.File]::Exists($done) -and
                ([IO.File]::GetLastWriteTimeUtc($done) -ge $requested) -and
                [IO.File]::ReadAllText($done).StartsWith('loaded ') -and (Test-CompiledToolbar $Root)) {
                Write-Host 'UNITY_RELOADED. Reopen the text editor from the ENFILADE menu.'
                return
            }
            $compilerError = Read-NewCompileError $offset
            if ($compilerError) {
                Write-Host "UNITY_COMPILE_ERROR $compilerError"
                return
            }
        }
        catch [System.IO.IOException] {
            # Unity may be rewriting its log or reload marker. Check again on the next poll.
        }
    }
    Write-Host 'UNITY_RELOAD_PENDING. Open the text editor after Unity finishes compiling.'
}

$root = Find-Project $ProjectPath
Write-Host "ENFILADE project: $root"
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('enfilade-text-update-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)

try {
    if (-not ('EnfiladeCatalogUpdate' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public sealed class EnfiladeCatalogUpdate
{
    private sealed class Cell
    {
        public readonly int Start, End;
        public readonly string Value;

        public Cell(int start, int end, string value)
        {
            Start = start;
            End = end;
            Value = value;
        }
    }

    private sealed class Row
    {
        public readonly List<Cell> Cells = new List<Cell>();
        public string Get(int column) { return column < Cells.Count ? Cells[column].Value : ""; }
        public int End { get { return Cells[Cells.Count - 1].End; } }
    }

    private static readonly string[] Settings = {
        "settings.screen.language",
        "settings.screen.language.restart",
        "settings.screen.language.names"
    };

    public string Text { get; private set; }
    public int Filled { get; private set; }

    private static List<Row> Parse(string text)
    {
        var rows = new List<Row>();
        var row = new Row();
        var value = new StringBuilder();
        int start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        bool quoted = false, hasText = false;
        for (int i = start; i <= text.Length; i++)
        {
            bool eof = i == text.Length;
            char c = eof ? '\n' : text[i];
            if (quoted)
            {
                if (eof) throw new FormatException("An unterminated quoted cell was found in Texts.csv.");
                if (c != '"') value.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = false;
                continue;
            }
            if (c == '"') { quoted = true; hasText = true; continue; }
            if (c != ',' && c != '\n')
            {
                if (c != '\r') { value.Append(c); hasText = true; }
                continue;
            }

            if (c == ',') hasText = true;
            if (hasText)
            {
                int end = i;
                if (c == '\n' && end > start && text[end - 1] == '\r') end--;
                row.Cells.Add(new Cell(start, end, value.ToString()));
            }
            if (c == '\n' && hasText)
            {
                rows.Add(row);
                row = new Row();
                hasText = false;
            }
            start = i + 1;
            value.Clear();
        }
        return rows;
    }

    private static string Quote(string value)
    {
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static List<string> Columns(List<Row> rows)
    {
        if (rows.Count == 0) throw new FormatException("Texts.csv is empty.");
        var columns = rows[0].Cells.Select(c => c.Value.Trim().ToLowerInvariant()).ToList();
        if (columns[0] != "id" || !columns.Contains("ko") || columns.Distinct().Count() != columns.Count)
            throw new FormatException("Texts.csv must have an id header and a unique ko column.");
        return columns;
    }

    public static EnfiladeCatalogUpdate Merge(string current, string incoming)
    {
        var original = Parse(current);
        var reference = Parse(incoming);
        var columns = Columns(original);
        var translatedColumns = Columns(reference);
        int sourceIndex = translatedColumns.IndexOf("ko");
        int translationIndex = translatedColumns.IndexOf("en");
        if (translationIndex < 0) throw new FormatException("The reference English column is missing.");

        var translated = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in reference.Skip(1))
        {
            string id = row.Get(0).Trim();
            if (id.Length > 0 && !id.StartsWith("#", StringComparison.Ordinal)) translated[id] = row;
        }

        int oldColumnCount = columns.Count;
        var added = new[] { "en", "ja" }.Where(locale => !columns.Contains(locale)).ToArray();
        columns.AddRange(added);
        int ko = columns.IndexOf("ko"), en = columns.IndexOf("en");
        int next = original[0].End, filled = 0;
        var built = new StringBuilder(current.Length);
        built.Append(current, 0, next);
        foreach (string locale in added) built.Append(',').Append(locale);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in original.Skip(1))
        {
            if (row.Cells.Skip(oldColumnCount).Any(c => !string.IsNullOrWhiteSpace(c.Value)))
                throw new FormatException("A Texts.csv row has data past its last column: " + row.Get(0));
            string id = row.Get(0).Trim();
            Row source;
            seen.Add(id);
            if (!string.IsNullOrWhiteSpace(row.Get(en)) || !translated.TryGetValue(id, out source)
                || string.IsNullOrWhiteSpace(source.Get(translationIndex))
                || row.Get(ko).Trim() != source.Get(sourceIndex).Trim()) continue;

            int start = en < row.Cells.Count ? row.Cells[en].Start : row.End;
            built.Append(current, next, start - next);
            if (en >= row.Cells.Count) built.Append(',', en + 1 - row.Cells.Count);
            built.Append(Quote(source.Get(translationIndex)));
            next = en < row.Cells.Count ? row.Cells[en].End : row.End;
            filled++;
        }
        built.Append(current, next, current.Length - next);

        string ending = current.Contains("\r\n") ? "\r\n" : "\n";
        foreach (string id in Settings)
        {
            if (seen.Contains(id)) continue;
            Row source;
            if (!translated.TryGetValue(id, out source))
                throw new FormatException("Missing language setting in the reference: " + id);
            if (built.Length > 0 && built[built.Length - 1] != '\n') built.Append(ending);
            built.Append(string.Join(",", columns.Select(column => {
                int index = translatedColumns.IndexOf(column);
                return index < 0 ? "" : Quote(source.Get(index));
            }))).Append(ending);
        }

        return new EnfiladeCatalogUpdate { Text = built.ToString(), Filled = filled };
    }
}
'@
    }
    $originals = @{}
    $changes = [ordered]@{}
    foreach ($relative in $scriptBlobs.Keys) {
        $path = Join-Path $root $relative
        $originals[$relative] = [IO.File]::ReadAllBytes($path)
        $current = $utf8.GetString($originals[$relative])
        $hashes = $scriptBlobs[$relative]
        $after = Read-ReleaseFile $updateRef $relative $hashes[1]
        if ((Normalize-Code $current) -ceq (Normalize-Code $after)) { continue }
        $before = Read-ReleaseFile $baseRef $relative $hashes[0]
        $updated = Merge-Code $relative $current $before $after
        if ($updated -cne $current) { $changes[$relative] = $utf8.GetBytes($updated) }
    }

    $catalogPath = Join-Path $root $catalogFile
    $originals[$catalogFile] = [IO.File]::ReadAllBytes($catalogPath)
    $catalog = $utf8.GetString($originals[$catalogFile])
    $reference = Read-ReleaseFile $updateRef $catalogFile $catalogBlob
    $merge = [EnfiladeCatalogUpdate]::Merge($catalog, $reference)
    if ($merge.Text -cne $catalog) { $changes[$catalogFile] = $utf8.GetBytes($merge.Text) }

    $editor = $utf8.GetString($originals[$editorFile])
    if ($changes.Contains($editorFile)) { $editor = $utf8.GetString($changes[$editorFile]) }
    if (-not $editor.Contains('DrawLanguageToolbar();')) { throw 'The new language toolbar is missing from the merged editor code.' }

    if ($changes.Count -gt 0) {
        $backup = Join-Path ([IO.Path]::GetTempPath()) ('enfilade-text-backup-' + [guid]::NewGuid().ToString('N'))
        foreach ($relative in $changes.Keys) {
            $path = Join-Path $backup $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            [IO.File]::WriteAllBytes($path, $originals[$relative])
        }
        Write-Host "Original files: $backup"
        $applied = New-Object 'System.Collections.Generic.List[string]'
        try {
            foreach ($relative in $changes.Keys) {
                $path = Join-Path $root $relative
                if ((Get-BlobHash ([IO.File]::ReadAllBytes($path))) -ne (Get-BlobHash $originals[$relative])) {
                    throw "Another program edited $relative during this update."
                }
                Replace-File $path $changes[$relative]
                $applied.Add($relative)
            }
            foreach ($relative in $changes.Keys) {
                $path = Join-Path $root $relative
                if ((Get-BlobHash ([IO.File]::ReadAllBytes($path))) -ne (Get-BlobHash $changes[$relative])) {
                    throw "Verification failed for $relative."
                }
            }
        }
        catch {
            foreach ($relative in $applied) {
                $path = Join-Path $root $relative
                try {
                    if ((Get-BlobHash ([IO.File]::ReadAllBytes($path))) -eq (Get-BlobHash $changes[$relative])) {
                        Replace-File $path $originals[$relative]
                    }
                }
                catch { Write-Warning "Restore this file from the backup at $backup : $relative" }
            }
            throw
        }
    }
    Write-Host "FILES_VERIFIED. $($changes.Count) files changed. $($merge.Filled) blank English cells filled."
    if (-not $NoRefresh) { Request-UnityRefresh $root ($changes.Count -gt 0) }
}
finally {
    if ([IO.Directory]::Exists($scratch)) { [IO.Directory]::Delete($scratch, $true) }
}
