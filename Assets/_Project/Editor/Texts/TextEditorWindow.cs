using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// ENFILADE → 글자 → 글자 편집기: every word the game shows, from Data/Resources/Tables/Core/Texts.csv, to read and rewrite in one place
/// (the user, 2026-11-12: "네가 쓴 대사나 문구나 설정 같은 것들 너무 AI 같아서 내가 수정하려고 하는데 어디서 해? 모으면 다
/// 수정할 수 있어?"). Search by id, words or note and edit one language at a time with references for the selected line, where each
/// line is used. Saving writes only the changed cells, in place (TextSheet), warns before a line loses a hole the code
/// fills ({0}) or a '|' a screen splits at, and during Play swaps the words on labels already up (TextLiveSwap). A
/// callout line's stand-in voice is marked when its words changed and can be made again for just those lines.
/// The 검사 tab is the 하드코딩 검사 (TextScan).
/// </summary>
internal sealed class TextEditorWindow : EditorWindow
{
    [Serializable]
    private sealed class Edit
    {
        public string id, column, words;
        public int occurrence;
    }

    private const float RowHeight = 20f, IdWidth = .24f, NoteWidth = .22f, ReferenceWidth = .23f;
    private const float MinListHeight = 120f, MinDetailHeight = 140f, SplitterHeight = 6f;
    private static readonly string[] Tabs = { "글자", "하드코딩 검사" };

    // Kept through script reloads (other work compiling while the user writes) and Play: unsaved edits survive them.
    [SerializeField] private List<Edit> pending = new List<Edit>();
    [SerializeField] private int tab;
    [SerializeField] private string search = "";
    [SerializeField] private int area;
    [SerializeField] private bool onlyChanged, onlyProblems, onlyMissing, excelNote;
    [SerializeField] private bool showHelp = true;
    [SerializeField] private string editLanguage = Texts.Korean;
    [SerializeField] private string selectedId = "";
    [SerializeField] private int selectedOccurrence;
    [SerializeField] private float detailHeight = 300f;

    private TextSheet sheet;
    private string loadError;
    private List<TextSheet.Row> shown = new List<TextSheet.Row>();
    private string shownKey;
    private Vector2 listScroll, detailScroll, checkScroll;
    private Dictionary<string, List<(string path, int line, string what)>> usage;
    private List<TextScan.Finding> findings;
    private readonly HashSet<TextScan.Kind> open = new HashSet<TextScan.Kind> { TextScan.Kind.CodeWords, TextScan.Kind.MissingId, TextScan.Kind.BrokenRow };
    private Dictionary<string, List<string>> calloutLines;
    private Dictionary<string, string> spoken;
    private bool dragging, addingLanguage, resetEditing;
    private string newLanguage = "";
    private Rect wordsArea;

    private int EditingColumn => sheet.Column(editLanguage);
    private GUIStyle wrap, changedId, muted;

    [MenuItem("ENFILADE/글자/글자 편집기", priority = 0)]
    private static void OpenEditor() => Open(0);

    [MenuItem("ENFILADE/글자/하드코딩 검사", priority = 1)]
    private static void OpenCheck() => Open(1);

    private static void Open(int tab)
    {
        var window = GetWindow<TextEditorWindow>("글자 편집기");
        window.minSize = new Vector2(760f, 480f);
        window.tab = tab;
        window.resetEditing = true;
        if (tab == 1) window.findings = null;
        window.Show();
    }

    private void OnEnable()
    {
        saveChangesMessage = "Texts.csv에 저장하지 않은 글자가 있어요. 저장할까요?";
        Load();
    }

    private void OnFocus()
    {
        // Another tool, session or a hand edit saved the file: take its rows, keeping the edits here.
        if (sheet != null && sheet.ChangedOnDisk)
        {
            try { sheet.Refresh(); loadError = null; } catch (IOException error) { loadError = error.Message; }
            resetEditing = true;
            Changed();
        }
    }

    private void Load()
    {
        try
        {
            sheet = TextSheet.Load();
            loadError = null;
        }
        catch (IOException error)
        {
            sheet = null;
            loadError = error.Message;
            return;
        }
        foreach (var edit in pending)
        {
            var row = sheet.Find(edit.id, edit.occurrence);
            int column = sheet.Column(edit.column);
            if (row != null && column > 0) row.Words[column] = edit.words;
        }
        resetEditing = true;
        Changed();
    }

    // The edits mirrored into the serialized list, the list of rows to show rebuilt, the unsaved mark set.
    private void Changed()
    {
        pending.Clear();
        if (sheet != null)
            foreach (var row in sheet.Rows.Where(r => r.Dirty))
                for (int column = 1; column < sheet.Columns.Count; column++)
                    if (row.Changed(column))
                        pending.Add(new Edit { id = row.Id, occurrence = row.Occurrence, column = sheet.Columns[column], words = row.Words[column] });
        hasUnsavedChanges = pending.Count > 0;
        shownKey = null;
        calloutLines = null;
        Repaint();
    }

    public override void SaveChanges()
    {
        Save();
        if (!hasUnsavedChanges) base.SaveChanges();
    }

    private void OnGUI()
    {
        if (resetEditing)
        {
            EndTextEditing();
            resetEditing = false;
        }
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        changedId ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, .45f, .65f) } };
        muted ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, richText = false, clipping = TextClipping.Clip };
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.S && (Event.current.control || Event.current.command))
        {
            Save();
            Event.current.Use();
            RestartGUI();
        }
        int pickedTab = GUILayout.Toolbar(tab, Tabs, EditorStyles.toolbarButton);
        if (pickedTab != tab)
        {
            tab = pickedTab;
            RestartGUI();
        }
        if (loadError != null || sheet == null)
        {
            EditorGUILayout.HelpBox("Texts.csv를 읽지 못했어요: " + loadError + "\n파일을 잠근 프로그램(엑셀 등)을 닫고 다시 읽어 주세요.", MessageType.Error);
            if (GUILayout.Button("다시 읽기")) Load();
            return;
        }
        if (tab == 0) DrawWords();
        else DrawCheck();
    }

    // ---- The words tab ----

    private void DrawWords()
    {
        DrawLanguageToolbar();
        DrawFilters();
        if (showHelp)
            EditorGUILayout.HelpBox(
                "1. 편집 언어를 고르고 검색해요(id·모든 언어·메모·구역 제목). 목록의 어느 칸이든 누르면 아래에 편집란이 열려요.\n" +
                "2. 아래에서 선택한 언어의 문구와 '어디' 메모를 고쳐요. 원문·참고 번역은 읽기 전용이에요. Enter로 줄을 나눠요.\n" +
                "3. 저장 버튼이나 Ctrl+S로 모든 언어의 고친 칸을 함께 저장해요. 줄·언어를 옮겨도 수정은 남고, 자동 저장되지는 않아요.\n" +
                "{0}·{1}·{Knight.coverCooldown}은 코드가 채우는 자리라 남겨요. [F]는 설정한 키로 표시돼요.\n" +
                "고르기 칩·슬라이더 양 끝의 |는 칸 구분이라 개수를 맞춰요. <b>·<color=…> 같은 꾸밈 태그도 짝을 맞춰요.", MessageType.Info);
        Filter();
        if (sheet.ChangedOnDisk)
            EditorGUILayout.HelpBox("다른 곳에서 Texts.csv가 바뀌었어요. 창을 다시 누르면 새 줄을 가져와요(고친 글자는 남아요).", MessageType.Info);
        TextSheet.Row row = sheet.Find(selectedId, selectedOccurrence);
        // A layout rect takes the space left by the toolbars and help boxes. The scroll view then uses that exact viewport.
        Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type != EventType.Layout && Event.current.type != EventType.Used) wordsArea = area;
        if (row == null)
        {
            DrawList(wordsArea);
            EditorGUILayout.HelpBox("목록에서 고칠 줄을 누르세요. 아래 편집란에서 문구와 메모를 고칠 수 있어요.", MessageType.None);
        }
        else
        {
            float maxHeight = Mathf.Max(0f, wordsArea.height - RowHeight - MinListHeight - SplitterHeight);
            float minHeight = Mathf.Min(MinDetailHeight, maxHeight);
            float height = Mathf.Clamp(detailHeight, minHeight, maxHeight);
            Rect list = wordsArea;
            list.height = Mathf.Max(0f, list.height - height - SplitterHeight);
            DrawList(list);

            var splitter = new Rect(list.x, list.yMax, list.width, SplitterHeight);
            DrawSplitter(splitter, height, minHeight, maxHeight);
            var detail = new Rect(splitter.x, splitter.yMax, splitter.width, height);
            using (new GUILayout.AreaScope(detail))
                DrawDetail(row, height);
        }
        DrawFooter();
    }


    private void DrawLanguageToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("편집 언어", EditorStyles.miniBoldLabel);
            var columns = sheet.LanguageColumns().ToArray();
            if (columns.Length > 0)
            {
                int index = Array.IndexOf(columns, EditingColumn);
                if (index < 0) index = Mathf.Max(0, Array.IndexOf(columns, sheet.Korean));
                string[] names = columns.Select(indexInSheet => LanguageLabel(sheet.Columns[indexInSheet])).ToArray();
                int columnIndex = columns[EditorGUILayout.Popup(index, names, EditorStyles.toolbarPopup, GUILayout.Width(145f))];
                string picked = sheet.Columns[columnIndex];
                if (picked != editLanguage)
                {
                    editLanguage = picked;
                    detailScroll = Vector2.zero;
                    shownKey = null;
                    RestartGUI();
                }
            }
            if (GUILayout.Button("언어 추가", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                addingLanguage = !addingLanguage;
                RestartGUI();
            }
            bool help = GUILayout.Toggle(showHelp, new GUIContent("사용법", "편집·저장 방법과 문구에 쓰는 특수 기호를 설명해요."), EditorStyles.toolbarButton, GUILayout.Width(54f));
            if (help != showHelp)
            {
                showHelp = help;
                RestartGUI();
            }
            GUILayout.FlexibleSpace();
            int column = EditingColumn;
            if (column > 0)
            {
                int count = sheet.Rows.Count(row => !string.IsNullOrWhiteSpace(row.Words[column]));
                GUILayout.Label($"작성 {count} / {sheet.Rows.Count}줄", EditorStyles.miniLabel);
            }
        }
        if (!addingLanguage) return;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("언어 코드", EditorStyles.miniLabel);
            GUI.SetNextControlName("texts.newLanguage");
            newLanguage = EditorGUILayout.TextField(newLanguage, GUILayout.Width(100f));
            GUILayout.Label("예  ja, fr, pt-br", EditorStyles.miniLabel);
            if (GUILayout.Button("추가", EditorStyles.toolbarButton, GUILayout.Width(50f))) AddLanguage();
            GUILayout.FlexibleSpace();
        }
    }

    private void AddLanguage()
    {
        if (!TextSheet.TryLanguageCode(newLanguage, out string code))
        {
            ShowNotification(new GUIContent("ja, fr, pt-br 같은 언어 코드를 입력해 주세요."), 3f);
            return;
        }
        try
        {
            bool written = sheet.AddLanguage(code);
            editLanguage = code;
            addingLanguage = false;
            newLanguage = "";
            detailScroll = Vector2.zero;
            Changed();
            if (!written) ShowNotification(new GUIContent("파일 잠금이 풀리면 새 언어 열이 저장돼요."), 3f);
            RestartGUI();
        }
        catch (IOException error)
        {
            EditorUtility.DisplayDialog("언어 추가", error.Message, "확인");
        }
    }

    private static string LanguageLabel(string code)
    {
        switch (code.ToLowerInvariant())
        {
            case Texts.Korean: return "한국어 (KO)";
            case Texts.English: return "English (EN)";
            case "ja": return "日本語 (JA)";
            default: return code.ToUpperInvariant();
        }
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUI.SetNextControlName("texts.search");
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
            var areas = new[] { "구역: 전체" }.Concat(TextSheet.Areas.Select(a => a.name)).Concat(new[] { "기타" }).ToArray();
            area = EditorGUILayout.Popup(area, areas, EditorStyles.toolbarPopup, GUILayout.Width(120f));
            onlyMissing = GUILayout.Toggle(onlyMissing, new GUIContent("미번역만", "선택한 언어가 저장된 파일에서 비어 있는 줄. 입력 중인 줄은 저장할 때까지 남아요."), EditorStyles.toolbarButton);
            onlyChanged = GUILayout.Toggle(onlyChanged, new GUIContent("고친 줄만", "언어·메모 중 하나라도 저장하지 않은 수정이 있는 줄이에요."), EditorStyles.toolbarButton);
            onlyProblems = GUILayout.Toggle(onlyProblems, new GUIContent("문제 있는 줄만", "문구의 필수 기호에 오류가 있거나 다른 곳의 수정과 충돌한 줄이에요."), EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{shown.Count}줄", EditorStyles.miniLabel);
        }
    }

    private void Filter()
    {
        string key = $"{search}\n{area}\n{editLanguage}\n{onlyMissing}\n{onlyChanged}\n{onlyProblems}";
        if (key == shownKey) return;
        shownKey = key;
        string areaName = area <= 0 ? null : area <= TextSheet.Areas.Length ? TextSheet.Areas[area - 1].name : "기타";
        int column = EditingColumn;
        string query = search.Trim();
        shown = sheet.Rows.Where(row =>
        {
            if (areaName != null && TextSheet.AreaOf(row.Id) != areaName) return false;
            if (onlyChanged && !row.Dirty) return false;
            // Keep a row in the missing list while its first translation is still being typed.
            if (onlyMissing && (column < 1 || !string.IsNullOrWhiteSpace(row.Saved[column]))) return false;
            if (onlyProblems && !sheet.Problems(row).Any(p => p.error) && row.Conflict == null) return false;
            if (query.Length == 0) return true;
            return row.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || row.Words.Skip(1).Any(w => (w ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                   || row.Section.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }).ToList();
    }


    private void DrawList(Rect bounds)
    {
        int column = EditingColumn, note = sheet.Note;
        int english = sheet.Column(Texts.English);
        int reference = column == english ? sheet.Korean : english;
        bool showReference = reference > 0;
        var viewport = new Rect(bounds.x, bounds.y + RowHeight, bounds.width, Mathf.Max(0f, bounds.height - RowHeight));
        float contentHeight = Mathf.Max(RowHeight, shown.Count * RowHeight);
        float contentWidth = viewport.width;
        if (contentHeight > viewport.height)
        {
            GUIStyle scrollbar = GUI.skin.verticalScrollbar;
            contentWidth = Mathf.Max(0f, contentWidth - scrollbar.fixedWidth - scrollbar.margin.left);
        }
        var header = new Rect(bounds.x, bounds.y, contentWidth, RowHeight);
        Columns(header, showReference, out var idRect, out var wordsRect, out var referenceRect, out var noteRect);
        GUI.Label(idRect, "id", EditorStyles.miniBoldLabel);
        if (column > 0) GUI.Label(wordsRect, LanguageLabel(sheet.Columns[column]), EditorStyles.miniBoldLabel);
        if (showReference) GUI.Label(referenceRect, column == english ? "한국어 원문" : "영어 참고", EditorStyles.miniBoldLabel);
        if (note > 0) GUI.Label(noteRect, "어디 (#어디)", EditorStyles.miniBoldLabel);

        bool clickedList = Event.current.type == EventType.MouseDown && Event.current.button == 0
                           && viewport.Contains(Event.current.mousePosition);
        listScroll = GUI.BeginScrollView(viewport, listScroll, new Rect(0f, 0f, contentWidth, contentHeight));
        if (shown.Count == 0) GUI.Label(new Rect(4f, 0f, contentWidth, RowHeight), "조건에 맞는 줄이 없어요.", muted);
        int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / RowHeight));
        int last = Mathf.Min(shown.Count - 1, first + Mathf.CeilToInt(viewport.height / RowHeight));
        for (int i = first; i <= last; i++)
        {
            var row = shown[i];
            var rect = new Rect(0f, i * RowHeight, contentWidth, RowHeight);
            bool selected = row.Id == selectedId && row.Occurrence == selectedOccurrence;
            if (selected) EditorGUI.DrawRect(rect, new Color(.24f, .48f, .9f, .25f));
            else if (row.Conflict != null) EditorGUI.DrawRect(rect, new Color(1f, .3f, .2f, .15f));
            Columns(rect, showReference, out idRect, out wordsRect, out referenceRect, out noteRect);
            // Visible rows are recycled as the list scrolls. Draw labels without per-row controls, so the number
            // of visible rows cannot change the detail fields' control IDs and move their shared edit buffer.
            var e = Event.current;
            if (clickedList && e.type == EventType.MouseDown && rect.Contains(e.mousePosition)
                && GUIUtility.hotControl == 0)
            {
                EndTextEditing();
                e.Use();
                Select(row);
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            GUI.Label(idRect, new GUIContent(row.Id, row.Id + (row.Dirty ? " · 저장하지 않은 수정이 있어요." : "")), row.Dirty ? changedId : EditorStyles.label);
            if (column > 0) ListLabel(wordsRect, row.Words[column]);
            if (showReference) ListLabel(referenceRect, row.Words[reference]);
            if (note > 0) ListLabel(noteRect, row.Words[note]);
        }
        GUI.EndScrollView();
    }

    private void ListLabel(Rect rect, string value) =>
        GUI.Label(rect, new GUIContent(string.IsNullOrWhiteSpace(value) ? "–" : value, value), muted);

    private static void Columns(Rect rect, bool showReference, out Rect id, out Rect words, out Rect reference, out Rect note)
    {
        float idWidth = rect.width * IdWidth;
        float noteWidth = rect.width * NoteWidth;
        float referenceWidth = showReference ? rect.width * ReferenceWidth : 0f;
        float wordsWidth = rect.width - idWidth - noteWidth - referenceWidth;
        id = new Rect(rect.x + 2f, rect.y + 1f, idWidth - 4f, rect.height - 2f);
        words = new Rect(id.xMax + 4f, id.y, wordsWidth - 4f, id.height);
        reference = new Rect(words.xMax + 4f, id.y, Mathf.Max(0f, referenceWidth - 4f), id.height);
        note = new Rect(words.xMax + 4f + referenceWidth, id.y, noteWidth - 6f, id.height);
    }

    private void Select(TextSheet.Row row)
    {
        if (row.Id == selectedId && row.Occurrence == selectedOccurrence) return;
        selectedId = row.Id;
        selectedOccurrence = row.Occurrence;
        detailScroll = Vector2.zero;
        // Stop this pass: DrawWords still holds the old row, and the next row needs a fresh text editor.
        RestartGUI();
    }

    private void DrawSplitter(Rect bar, float height, float minHeight, float maxHeight)
    {
        EditorGUI.DrawRect(new Rect(bar.x, bar.y + 2f, bar.width, 1f), new Color(0f, 0f, 0f, .4f));
        EditorGUIUtility.AddCursorRect(bar, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition))
        {
            dragging = true;
            e.Use();
        }
        if (dragging && e.type == EventType.MouseDrag)
        {
            detailHeight = Mathf.Clamp(height - e.delta.y, minHeight, maxHeight);
            e.Use();
            Repaint();
        }
        if (e.rawType == EventType.MouseUp) dragging = false;
    }

    private void DrawDetail(TextSheet.Row row, float height)
    {
        int column = EditingColumn, ko = sheet.Korean, en = sheet.Column(Texts.English);
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(height));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.SelectableLabel(row.Id, EditorStyles.boldLabel, GUILayout.Height(18f));
            GUILayout.Label($"{TextSheet.AreaOf(row.Id)}{(row.Section.Length > 0 ? " · " + row.Section : "")} · {row.Line}번째 줄", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            // Keep the button in the control sequence even before the first edit, so typing cannot move focus.
            using (new EditorGUI.DisabledScope(!row.Dirty))
            {
                if (GUILayout.Button(new GUIContent("이 줄 되돌리기", "이 줄의 모든 언어와 메모를 마지막으로 읽거나 저장한 값으로 돌려요."), GUILayout.Width(100f)))
                {
                    Array.Copy(row.Saved, row.Words, row.Words.Length);
                    row.Conflict = null;
                    Changed();
                    RestartGUI();
                }
            }
            if (GUILayout.Button("상세 닫기", GUILayout.Width(80f)))
            {
                selectedId = "";
                detailScroll = Vector2.zero;
                RestartGUI();
            }
        }
        GUILayout.Label($"편집 중: {LanguageLabel(editLanguage)} · {(row.Dirty ? "이 줄에 저장하지 않은 수정이 있어요" : "저장된 상태")}", EditorStyles.miniLabel);
        if (row.Conflict != null) EditorGUILayout.HelpBox(row.Conflict, MessageType.Warning);
        if (column > 0) Area(row, column, LanguageLabel(sheet.Columns[column]));
        if (ko > 0 && column != ko) DrawReference("한국어 원문", row.Words[ko]);
        if (column != en) DrawReference("영어 참고 (EN)", en > 0 ? row.Words[en] : "");
        int note = sheet.Note;
        if (note > 0)
        {
            GUI.SetNextControlName(CellControl(row, note));
            EditorGUI.BeginChangeCheck();
            string now = EditorGUILayout.TextField(new GUIContent("어디 (메모)", "이 문구가 나오는 화면·상황을 적어요. 모든 언어가 함께 쓰는 메모이며 게임에는 표시되지 않아요."), row.Words[note] ?? "");
            if (EditorGUI.EndChangeCheck() && now != row.Words[note]) { row.Words[note] = now; Changed(); }
        }
        var problems = sheet.Problems(row);
        foreach (var (error, what) in problems) EditorGUILayout.HelpBox(what, error ? MessageType.Error : MessageType.Warning);
        if (ko > 0 && column == ko) DrawVoice(row);
        DrawUsage(row);
        EditorGUILayout.EndScrollView();
    }

    private void DrawReference(string label, string value)
    {
        GUILayout.Label(label, EditorStyles.miniBoldLabel);
        if (string.IsNullOrWhiteSpace(value))
        {
            GUILayout.Label("아직 문구가 없어요.", muted);
            return;
        }
        string words = value.Replace("\\n", "\n");
        float height = wrap.CalcHeight(new GUIContent(words), position.width - 36f);
        EditorGUILayout.SelectableLabel(words, wrap, GUILayout.Height(height));
    }

    // Multi-line words: a line break here is \n in the file.
    private void Area(TextSheet.Row row, int column, string label)
    {
        GUILayout.Label(label + "  · Enter로 줄바꿈" + (row.Changed(column) ? "  (고침 · 원래: " + Short(row.Saved[column]) + ")" : ""), EditorStyles.miniBoldLabel);
        string shownWords = (row.Words[column] ?? "").Replace("\\n", "\n");
        GUI.SetNextControlName(CellControl(row, column));
        EditorGUI.BeginChangeCheck();
        string typed = EditorGUILayout.TextArea(shownWords, GUILayout.MinHeight(38f));
        if (!EditorGUI.EndChangeCheck() || typed == shownWords) return;
        row.Words[column] = typed.Replace("\r\n", "\n").Replace("\n", "\\n");
        Changed();
    }

    private void DrawVoice(TextSheet.Row row)
    {
        calloutLines ??= CalloutVoices.LinesByText();
        if (!calloutLines.TryGetValue(row.Id, out var lines)) return;
        spoken ??= CalloutVoices.SpokenWords();
        int ko = sheet.Korean;
        foreach (string line in lines)
        {
            var state = CalloutVoices.StateOf(line, row.Saved[ko], spoken);
            string text = $"콜아웃 {line} · {CalloutVoices.Describe(state)}";
            if (row.Changed(ko)) text += "\n저장하면 바뀐 글자로 목소리를 다시 만들어야 해요.";
            EditorGUILayout.HelpBox(text, state == CalloutVoices.State.Fresh && !row.Changed(ko) ? MessageType.Info : MessageType.Warning);
        }
        DrawRemake();
    }

    private void DrawRemake()
    {
        using (new EditorGUI.DisabledScope(CalloutVoices.Busy || hasUnsavedChanges))
        {
            string label = CalloutVoices.Busy ? "목소리를 만드는 중…" : hasUnsavedChanges ? "먼저 저장하세요" : "바뀐 콜아웃 목소리 다시 만들기";
            if (GUILayout.Button(new GUIContent(label, "글자가 바뀌었거나 목소리가 없는 콜아웃만 Windows 한국어 음성으로 다시 만들어요(Tools/Audio/callout_voices.py --changed). 녹음 파일이 있는 줄은 건드리지 않아요.")))
                CalloutVoices.RemakeChanged(() => { spoken = null; Repaint(); });
        }
    }

    private void DrawUsage(TextSheet.Row row)
    {
        GUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("쓰는 곳", EditorStyles.miniBoldLabel);
            if (usage == null && GUILayout.Button("찾기 (코드·표 전체, 몇 초)", GUILayout.Width(170f))) usage = TextScan.Usage(sheet);
            else if (usage != null && GUILayout.Button("다시 찾기", GUILayout.Width(80f))) usage = TextScan.Usage(sheet);
            GUILayout.FlexibleSpace();
        }
        if (usage == null) return;
        if (!usage.TryGetValue(row.Id, out var places) || places.Count == 0)
        {
            GUILayout.Label("코드나 표에서 이 id를 통째로 쓰는 곳은 못 찾았어요(코드가 id를 조각으로 만들면 안 보여요). 메모의 '어디'를 보세요.", wrap);
            return;
        }
        foreach (var (path, line, what) in places)
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("열기", GUILayout.Width(44f))) Reveal(path, line);
                GUILayout.Label($"{path.Replace("Assets/_Project/", "")}:{line}{(what.Length > 0 ? "  " + what : "")}", muted);
            }
    }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int count = sheet.Rows.Count(r => r.Dirty);
            if (GUILayout.Button(new GUIContent("다시 읽기", "파일의 최신 값을 가져와요. 이 창에서 고친 내용은 남기고 같은 칸의 외부 수정은 충돌로 표시해요."), EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                try { sheet.Refresh(); loadError = null; } catch (IOException error) { loadError = error.Message; }
                usage = null;
                Changed();
                RestartGUI();
            }
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("모두 되돌리기", EditorStyles.toolbarButton, GUILayout.Width(90f))
                    && EditorUtility.DisplayDialog("글자 편집기", $"고친 {count}줄을 모두 저장 전으로 되돌릴까요?", "되돌리기", "취소"))
                {
                    foreach (var row in sheet.Rows) { for (int c = 0; c < row.Words.Length; c++) row.Words[c] = row.Saved[c]; row.Conflict = null; }
                    Changed();
                    RestartGUI();
                }
                if (GUILayout.Button(new GUIContent(count > 0 ? $"저장 ({count}줄)" : "저장", "Ctrl+S · 필터와 현재 편집 언어에 관계없이 모든 언어·메모의 고친 칸을 저장해요."), EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    Save();
                    RestartGUI();
                }
            }
            GUILayout.Label($"저장 안 함 {count}줄 · {pending.Count}칸", EditorStyles.miniLabel);
            excelNote = GUILayout.Toggle(excelNote, "엑셀로 열면 안 되는 까닭", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            if (Application.isPlaying) GUILayout.Label("Play 중: 저장하면 떠 있는 글자도 바로 바뀌어요", EditorStyles.miniLabel);
        }
        if (excelNote)
            EditorGUILayout.HelpBox(
                "Texts.csv를 엑셀로 열어 저장하면 표가 망가질 수 있어요.\n" +
                "· 엑셀은 한글을 CP949로 바꾸거나 UTF-8 표시(BOM)를 지워 저장해서, 유니티가 글자를 깨진 채 읽을 수 있어요.\n" +
                "· +, -, = 로 시작하는 칸(+{0}%)을 수식으로, 1-2를 날짜로, 0.10을 0.1로 멋대로 바꿔요.\n" +
                "· 쉼표·따옴표·줄바꿈이 든 칸의 따옴표를 다시 쓰고, 칸 안 줄바꿈이 진짜 줄바꿈이 되면 줄이 둘로 쪼개져요(\\n으로 써야 해요).\n" +
                "· 열어 둔 동안 파일을 잠가서 유니티 도구가 표를 쓰지 못하고 기다려요.\n" +
                "· 다른 작업이 같은 표에 줄을 더하는 중이면, 통째로 다시 저장할 때 그 줄을 지울 수 있어요. 이 창은 고친 칸만 그 자리에 써요.\n" +
                "그래서 이 창에서 고치거나, 꼭 파일로 고칠 땐 VS Code처럼 UTF-8을 그대로 두는 편집기를 써요.", MessageType.Info);
    }

    private void Save()
    {
        resetEditing = true;
        if (sheet == null || !sheet.Rows.Any(r => r.Dirty)) return;
        var errors = sheet.Rows.Where(r => r.Dirty).SelectMany(r => sheet.Problems(r).Where(p => p.error).Select(p => r.Id + ": " + p.what)).ToList();
        if (errors.Count > 0 && !EditorUtility.DisplayDialog("글자 편집기",
                "저장하면 화면 글자가 깨질 수 있는 곳이 있어요:\n\n" + string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? $"\n…외 {errors.Count - 12}개" : "") +
                "\n\n그래도 저장할까요?", "그래도 저장", "취소"))
            return;
        // What labels show now, before the tables read the new words (for the labels already up during Play).
        var before = new Dictionary<string, string>();
        if (Application.isPlaying)
            foreach (var row in sheet.Rows.Where(r => r.Dirty && r.Occurrence == 0)) before[row.Id] = Texts.Get(row.Id);
        TextSheet.SaveResult result;
        try { result = sheet.Save(); }
        catch (IOException error)
        {
            EditorUtility.DisplayDialog("글자 편집기", "저장하지 못했어요: " + error.Message, "확인");
            return;
        }
        int swapped = 0;
        if (Application.isPlaying && !result.Waiting)
            swapped = TextLiveSwap.Run(before.Select(pair => (pair.Value, Texts.Get(pair.Key))));
        var callouts = CalloutVoices.LinesByText();
        bool calloutChanged = result.Changes.Any(c => c.column == Texts.Korean && callouts.ContainsKey(c.id));
        Changed();
        spoken = null;
        string message = result.Waiting ? "다른 프로그램(엑셀 등)이 파일을 잡고 있어요. 닫으면 저절로 저장돼요."
            : $"저장했어요 ({result.Written}칸){(swapped > 0 ? $" · 화면 글자 {swapped}개 바꿈" : "")}";
        if (result.Conflicts > 0) message += $" · {result.Conflicts}칸은 다른 곳에서 먼저 바뀌어 남겨 뒀어요(빨간 줄)";
        ShowNotification(new GUIContent(message), 3f);
        if (calloutChanged)
            Debug.Log("[글자] 콜아웃 대사를 고쳤어요. 글자 편집기에서 그 줄을 누르고 '바뀐 콜아웃 목소리 다시 만들기'로 임시 목소리를 다시 만들 수 있어요.");
    }

    // ---- The check tab ----

    private void DrawCheck()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(findings == null ? "검사하기" : "다시 검사", EditorStyles.toolbarButton, GUILayout.Width(80f))) RunCheck();
            using (new EditorGUI.DisabledScope(findings == null))
                if (GUILayout.Button("보고서 복사", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    EditorGUIUtility.systemCopyBuffer = TextScan.Report(findings);
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.HelpBox("플레이어가 읽는 글자는 모두 Texts.csv에 둬요. 코드는 id만 부르고(Texts.Get·Format), 표의 글자 칸은 Texts id를 담아요(Texts.Cell). " +
                                "개발자만 보는 글자(콘솔 대답·로그·인스펙터 설명·오브젝트 이름)는 코드에 둬도 되고, 그런 줄은 끝에 // dev-text를 달면 검사가 넘어가요.", MessageType.None);
        if (findings == null) return;
        checkScroll = EditorGUILayout.BeginScrollView(checkScroll);
        foreach (TextScan.Kind kind in Enum.GetValues(typeof(TextScan.Kind)))
        {
            var group = findings.Where(f => f.Kind == kind).ToList();
            bool expanded = EditorGUILayout.Foldout(open.Contains(kind), $"{TextScan.Title(kind)} · {group.Count}", true);
            if (expanded) open.Add(kind); else open.Remove(kind);
            if (!expanded) continue;
            foreach (var finding in group.Take(400))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("열기", GUILayout.Width(44f))) Reveal(finding.Path, finding.Line);
                    GUILayout.Label($"{finding.Path.Replace("Assets/_Project/", "")}:{finding.Line}  {finding.What}{(finding.Why.Length > 0 ? "  [" + finding.Why + "]" : "")}", muted);
                }
            if (group.Count > 400) GUILayout.Label($"…외 {group.Count - 400}개 (보고서 복사로 모두 볼 수 있어요)", muted);
        }
        EditorGUILayout.EndScrollView();
    }

    private void RunCheck()
    {
        try
        {
            EditorUtility.DisplayProgressBar("하드코딩 검사", "코드·표·프리팹을 읽는 중…", .5f);
            sheet.Refresh();
            resetEditing = true;
            Changed();
            findings = TextScan.Run(sheet);
        }
        catch (IOException error)
        {
            loadError = error.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---- Shared ----

    private string CellControl(TextSheet.Row row, int column) => $"texts.cell.{row.Id}\n{row.Occurrence}\n{sheet.Columns[column]}";

    private static void EndTextEditing()
    {
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        EditorGUIUtility.editingTextField = false;
    }

    // Changing a row, language or layout replaces text controls. End their shared edit session and start a fresh
    // GUI pass before drawing the replacement; otherwise Unity can keep the old cell's text for the new control.
    private void RestartGUI()
    {
        EndTextEditing();
        Repaint();
        GUIUtility.ExitGUI();
    }

    // Code opens at the line in the script editor; a table, prefab or scene is shown in the Project window (a table
    // opened by the system could land in Excel, which this window warns about).
    private static void Reveal(string path, int line)
    {
        var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (asset == null) return;
        if (path.EndsWith(".cs")) AssetDatabase.OpenAsset(asset, Mathf.Max(1, line));
        else
        {
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }
    }

    private static string Short(string words)
    {
        words ??= "";
        return words.Length <= 40 ? words : words.Substring(0, 40) + "…";
    }
}
