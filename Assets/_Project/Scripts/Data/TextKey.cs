using TMPro;
using UnityEngine;

/// <summary>
/// Gives a text made in a prefab or scene its words from Texts.csv, so a label laid out in the editor is rewritten in
/// the one file like every other line (Tools → 글자 → 글자 편집기; the user, 2026-11-12: every line in one place). It writes
/// the words of its id when it wakes and again when the tables reload (a save during Play). Only for labels no code
/// writes: a script that sets the label later has the last word, as before. The 하드코딩 검사 lists Korean prefab and
/// scene texts that have no TextKey.
/// </summary>
[DisallowMultipleComponent]
public sealed class TextKey : MonoBehaviour
{
    [Tooltip("The Texts.csv id whose words this label shows (plain words work too).")]
    public string id = "";

    private void Awake() => Write();

    private void OnEnable() => GameTables.Reloaded += Write;

    private void OnDisable() => GameTables.Reloaded -= Write;

    private void Write()
    {
        if (string.IsNullOrWhiteSpace(id) || !TryGetComponent(out TMP_Text label)) return;
        label.text = Texts.Cell(id);
    }
}
