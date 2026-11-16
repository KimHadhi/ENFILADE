"""Apply ENFILADE's text editor update in the current Unity checkout."""
import codecs
import csv
import io
import os
from pathlib import Path
import subprocess
import tempfile

BASE = "dcd347b57fce84c686d31f0428c701d76b146fc4"
UPDATE = "27cdcfc51ecbf85d29dfd8dec02983c559c154b7"
CATALOG = "Assets/_Project/Data/Resources/Tables/Core/Texts.csv"
SCRIPTS = (
    "Assets/_Project/Editor/Texts/TextEditorWindow.cs",
    "Assets/_Project/Editor/Texts/TextSheet.cs",
    "Assets/_Project/Scripts/Core/Texts/Texts.cs",
    "Assets/_Project/Scripts/Core/Settings/GameSettings.cs",
    "Assets/_Project/Scripts/UI/Settings/SettingsMenuView.cs",
)
LANGUAGE_SETTINGS = (
    "settings.screen.language",
    "settings.screen.language.restart",
    "settings.screen.language.names",
)


def git(*args, cwd=None, check=True):
    return subprocess.run(
        ["git", *args], cwd=cwd, check=check, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )


def records(data):
    source = data.decode("utf-8-sig")
    lines = io.StringIO(source, newline="").readlines()
    reader = csv.reader(lines, strict=True)
    start = 0
    for cells in reader:
        yield cells, "".join(lines[start:reader.line_num])
        start = reader.line_num


def cell(row, index):
    return row[index] if index < len(row) else ""


def set_cell(row, index, value):
    row.extend([""] * (index + 1 - len(row)))
    row[index] = value


def encode_row(row, ending):
    output = io.StringIO(newline="")
    csv.writer(output, lineterminator="\r\n").writerow(row)
    return output.getvalue()[:-2] + ending


def line_ending(raw):
    if raw.endswith("\r\n"):
        return "\r\n"
    if raw.endswith("\n"):
        return "\n"
    return "\r" if raw.endswith("\r") else ""


def merge_catalog(current, incoming):
    local = records(current)
    reference = records(incoming)
    header, header_text = next(local)
    reference_header, _ = next(reference)
    original_header = header.copy()
    keys = [name.strip().casefold() for name in header]
    if not keys or keys[0] != "id" or "ko" not in keys or len(set(keys)) != len(keys):
        raise ValueError("The local Texts.csv needs a unique id header and a ko column.")
    ref_keys = [name.strip().casefold() for name in reference_header]
    ref_by_id = {
        row[0].strip().casefold(): dict(zip(ref_keys, row))
        for row, _ in reference
        if row and row[0].strip() and not row[0].strip().startswith("#")
    }

    for language in ("en", "ja"):
        if language not in keys:
            keys.append(language)
            header.append(language)
    ko, en = keys.index("ko"), keys.index("en")
    ending = line_ending(header_text) or "\n"
    output = [header_text if header == original_header else encode_row(header, ending)]
    seen = set()
    filled = 0
    for row, raw in local:
        if len(row) > len(original_header) and any(value.strip() for value in row[len(original_header):]):
            raise ValueError("A Texts.csv record has more values than its header.")
        if row:
            key = row[0].strip().casefold()
            ref = ref_by_id.get(key)
            seen.add(key)
            if (ref and not cell(row, en).strip() and ref.get("en", "").strip()
                    and cell(row, ko).strip() == ref.get("ko", "").strip()):
                set_cell(row, en, ref["en"])
                raw = encode_row(row, line_ending(raw))
                filled += 1
        output.append(raw)

    for key in LANGUAGE_SETTINGS:
        if key in seen:
            continue
        if not line_ending(output[-1]):
            output.append(ending)
        ref = ref_by_id[key]
        output.append(encode_row([ref.get(name, "") for name in keys], ending))

    bom = codecs.BOM_UTF8 if current.startswith(codecs.BOM_UTF8) else b""
    return bom + "".join(output).encode("utf-8"), filled


def merge_script(current, base, incoming, relative, scratch):
    def lf(raw):
        return raw.replace(b"\r\n", b"\n")

    working = lf(current)
    base = lf(base)
    incoming = lf(incoming)
    if working == incoming:
        return current
    if working == base:
        merged = incoming
    else:
        inputs = []
        for name, content in (("working", working), ("base", base), ("update", incoming)):
            path = scratch / name
            path.write_bytes(content)
            inputs.append(str(path))
        result = git("merge-file", "-p", *inputs, check=False)
        if result.returncode != 0:
            raise ValueError(f"Merge conflict in {relative}. The update needs that local file.")
        merged = result.stdout
    return merged.replace(b"\n", b"\r\n") if b"\r\n" in current else merged


def replace_file(path, content):
    with tempfile.NamedTemporaryFile(dir=path.parent, suffix=".tmp", delete=False) as temporary:
        source = Path(temporary.name)
        try:
            temporary.write(content)
            temporary.close()
            os.chmod(source, path.stat().st_mode)
            os.replace(source, path)
        finally:
            if source.exists():
                source.unlink()


def apply():
    root = Path(os.fsdecode(git("rev-parse", "--show-toplevel").stdout.rstrip(b"\r\n")))
    paths = (*SCRIPTS, CATALOG)
    if git("ls-files", "-u", "--", *paths, cwd=root).stdout:
        raise ValueError("An existing Git merge has conflicts in the text editor files.")
    originals = {path: (root / path).read_bytes() for path in paths}
    with tempfile.TemporaryDirectory(prefix="enfilade-text-editor-merge-") as directory:
        scratch = Path(directory)
        changes = {
            path: merge_script(
                originals[path],
                git("show", f"{BASE}:{path}", cwd=root).stdout,
                git("show", f"{UPDATE}:{path}", cwd=root).stdout,
                path,
                scratch,
            )
            for path in SCRIPTS
        }
    changes[CATALOG], filled = merge_catalog(
        originals[CATALOG], git("show", f"{UPDATE}:{CATALOG}", cwd=root).stdout
    )
    changes = {path: data for path, data in changes.items() if data != originals[path]}
    if not changes:
        print("ENFILADE text editor update is already applied.")
        return

    backup = Path(tempfile.mkdtemp(prefix="enfilade-text-editor-backup-"))
    for path in changes:
        target = backup / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(originals[path])

    applied = []
    try:
        for path, data in changes.items():
            target = root / path
            if target.read_bytes() != originals[path]:
                raise ValueError(f"{path} changed while the update was being prepared.")
            replace_file(target, data)
            applied.append(path)
    except (OSError, ValueError):
        for path in reversed(applied):
            try:
                target = root / path
                if target.read_bytes() == changes[path]:
                    replace_file(target, originals[path])
            except OSError:
                pass
        print(f"Original files are in {backup}")
        raise

    print(f"Updated {len(changes)} files. Filled {filled} blank English cells.")
    print(f"Original files are in {backup}")


if __name__ == "__main__":
    try:
        apply()
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.stderr.decode("utf-8", errors="replace").strip() or str(error))
    except (OSError, ValueError, csv.Error, StopIteration) as error:
        raise SystemExit(str(error) or "Texts.csv is empty.")
