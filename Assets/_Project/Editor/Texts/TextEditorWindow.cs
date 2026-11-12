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
/// 수정할 수 있어?"). Search by id, words or note, filter by area, Korean and a second language side by side, where each
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

    private const float RowHeight = 20f, IdWidth = .24f, NoteWidth = .22f;
    private static readonly string[] Tabs = { "글자", "하드코딩 검사" };

    // Kept through script reloads (other work compiling while the user writes) and Play: unsaved edits survive them.
    [SerializeField] private List<Edit> pending = new List<Edit>();
    [SerializeField] private int tab;
    [SerializeField] private string search = "";
    [SerializeField] private int area;
    [SerializeField] private bool onlyChanged, onlyProblems, excelNote;
    [SerializeField] private string second = "en";
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
    private bool dragging;
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
            try { sheet.Refresh(); } catch (IOException error) { loadError = error.Message; }
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
        base.SaveChanges();
    }

    private void OnGUI()
    {
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        changedId ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, .45f, .65f) } };
        muted ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, richText = false };
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.S && (Event.current.control || Event.current.command))
        {
            Save();
            Event.current.Use();
        }
        tab = GUILayout.Toolbar(tab, Tabs, EditorStyles.toolbarButton);
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
        DrawFilters();
        Filter();
        if (sheet.ChangedOnDisk)
            EditorGUILayout.HelpBox("다른 곳에서 Texts.csv가 바뀌었어요. 창을 다시 누르면 새 줄을 가져와요(고친 글자는 남아요).", MessageType.Info);
        DrawList();
        DrawSplitter();
        DrawDetail();
        DrawFooter();
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            string typed = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(180f));
            if (typed != search) { search = typed; shownKey = null; }
            var areas = new[] { "구역: 전체" }.Concat(TextSheet.Areas.Select(a => a.name)).Concat(new[] { "기타" }).ToArray();
            int picked = EditorGUILayout.Popup(area, areas, EditorStyles.toolbarPopup, GUILayout.Width(120f));
            if (picked != area) { area = picked; shownKey = null; }
            bool changedOnly = GUILayout.Toggle(onlyChanged, "고친 줄만", EditorStyles.toolbarButton);
            bool problemsOnly = GUILayout.Toggle(onlyProblems, "문제 있는 줄만", EditorStyles.toolbarButton);
            if (changedOnly != onlyChanged || problemsOnly != onlyProblems) { onlyChanged = changedOnly; onlyProblems = problemsOnly; shownKey = null; }
            var languages = sheet.Languages.Where(l => l != Texts.Korean).ToArray();
            if (languages.Length > 1)
            {
                int index = Mathf.Max(0, Array.IndexOf(languages, second));
                second = languages[EditorGUILayout.Popup(index, languages, EditorStyles.toolbarPopup, GUILayout.Width(60f))];
            }
            else if (languages.Length == 1) second = languages[0];
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{shown.Count}줄 / {sheet.Rows.Count}", EditorStyles.miniLabel);
        }
    }

    private void Filter()
    {
        string key = $"{search}\n{area}\n{onlyChanged}\n{onlyProblems}";
        if (key == shownKey) return;
        shownKey = key;
        string areaName = area <= 0 ? null : area <= TextSheet.Areas.Length ? TextSheet.Areas[area - 1].name : "기타";
        string query = search.Trim();
        shown = sheet.Rows.Where(row =>
        {
            if (areaName != null && TextSheet.AreaOf(row.Id) != areaName) return false;
            if (onlyChanged && !row.Dirty) return false;
            if (onlyProblems && !sheet.Problems(row).Any(p => p.error) && row.Conflict == null) return false;
            if (query.Length == 0) return true;
            return row.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || row.Words.Skip(1).Any(w => (w ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                   || row.Section.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }).ToList();
    }

    private void DrawList()
    {
        int ko = sheet.Korean, other = sheet.Column(second), note = sheet.Note;
        float listHeight = Mathf.Max(80f, position.height - detailHeight - 92f);
        var header = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
        Columns(header, out var idRect, out var koRect, out var otherRect, out var noteRect);
        GUI.Label(idRect, "id", EditorStyles.miniBoldLabel);
        GUI.Label(koRect, "한국어 (ko)", EditorStyles.miniBoldLabel);
        GUI.Label(otherRect, other > 0 ? $"{second} (비면 한국어가 나와요)" : "", EditorStyles.miniBoldLabel);
        GUI.Label(noteRect, "어디 (#어디)", EditorStyles.miniBoldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(listHeight));
        var all = GUILayoutUtility.GetRect(0f, shown.Count * RowHeight, GUILayout.ExpandWidth(true));
        int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / RowHeight));
        int last = Mathf.Min(shown.Count - 1, first + Mathf.CeilToInt(listHeight / RowHeight) + 1);
        for (int i = first; i <= last; i++)
        {
            var row = shown[i];
            var rect = new Rect(all.x, all.y + i * RowHeight, all.width, RowHeight);
            bool selected = row.Id == selectedId && row.Occurrence == selectedOccurrence;
            if (selected) EditorGUI.DrawRect(rect, new Color(.24f, .48f, .9f, .25f));
            else if (row.Conflict != null) EditorGUI.DrawRect(rect, new Color(1f, .3f, .2f, .15f));
            Columns(rect, out idRect, out koRect, out otherRect, out noteRect);
            if (GUI.Button(idRect, row.Id, row.Dirty ? changedId : EditorStyles.label)) Select(row, true);
            if (ko > 0) Field(koRect, row, ko);
            if (other > 0) Field(otherRect, row, other);
            if (note > 0) GUI.Label(noteRect, new GUIContent(row.Words[note], row.Words[note]), muted);
        }
        GUILayout.EndScrollView();
    }

    private void Field(Rect rect, TextSheet.Row row, int column)
    {
        string now = EditorGUI.TextField(rect, row.Words[column] ?? "");
        if (now == row.Words[column]) return;
        row.Words[column] = now;
        // Typing stays in the field: the row is picked without taking the keyboard away.
        Select(row, false);
        Changed();
    }

    private static void Columns(Rect rect, out Rect id, out Rect ko, out Rect other, out Rect note)
    {
        float idWidth = rect.width * IdWidth, noteWidth = rect.width * NoteWidth, words = (rect.width - idWidth - noteWidth) / 2f;
        id = new Rect(rect.x + 2f, rect.y + 1f, idWidth - 4f, rect.height - 2f);
        ko = new Rect(id.xMax + 4f, rect.y + 1f, words - 4f, rect.height - 2f);
        other = new Rect(ko.xMax + 4f, rect.y + 1f, words - 4f, rect.height - 2f);
        note = new Rect(other.xMax + 4f, rect.y + 1f, noteWidth - 6f, rect.height - 2f);
    }

    private void Select(TextSheet.Row row, bool takeFocus)
    {
        if (row.Id == selectedId && row.Occurrence == selectedOccurrence) return;
        selectedId = row.Id;
        selectedOccurrence = row.Occurrence;
        if (takeFocus) GUI.FocusControl(null);
        detailScroll = Vector2.zero;
    }

    private void DrawSplitter()
    {
        var bar = GUILayoutUtility.GetRect(0f, 5f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(bar.x, bar.y + 2f, bar.width, 1f), new Color(0f, 0f, 0f, .4f));
        EditorGUIUtility.AddCursorRect(bar, MouseCursor.ResizeVertical);
        var e = Event.current;
        if (e.type == EventType.MouseDown && bar.Contains(e.mousePosition)) { dragging = true; e.Use(); }
        if (dragging && e.type == EventType.MouseDrag)
        {
            detailHeight = Mathf.Clamp(detailHeight - e.delta.y, 140f, position.height - 200f);
            e.Use();
            Repaint();
        }
        if (e.rawType == EventType.MouseUp) dragging = false;
    }

    private void DrawDetail()
    {
        var row = sheet.Find(selectedId, selectedOccurrence);
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(detailHeight));
        if (row == null)
        {
            EditorGUILayout.HelpBox("위 목록에서 id를 누르면 여기서 여러 줄로 고치고, 어디에 쓰이는지 볼 수 있어요. 줄은 Enter로 바꿔요(파일에는 \\n으로 들어가요). " +
                                    "이야기 카드는 줄마다 한 줄씩 나오고 줄 수는 자유예요.\n" +
                                    "{0} {1}은 코드가 채우는 자리. 체형의 고르기 칩·슬라이더 양 끝 글자만 | 로 칸을 나누고 그 수를 맞춰요. [F] 같은 대괄호 키는 플레이어가 고른 키로 보여요.", MessageType.None);
            EditorGUILayout.EndScrollView();
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.SelectableLabel(row.Id, EditorStyles.boldLabel, GUILayout.Height(18f));
            GUILayout.Label($"{TextSheet.AreaOf(row.Id)}{(row.Section.Length > 0 ? " · " + row.Section : "")} · {row.Line}번째 줄", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (row.Dirty && GUILayout.Button("이 줄 되돌리기", GUILayout.Width(100f)))
            {
                for (int c = 0; c < row.Words.Length; c++) row.Words[c] = row.Saved[c];
                row.Conflict = null;
                GUI.FocusControl(null);
                Changed();
            }
        }
        if (row.Conflict != null) EditorGUILayout.HelpBox(row.Conflict, MessageType.Warning);
        foreach (int column in sheet.LanguageColumns()) Area(row, column, sheet.Columns[column] == Texts.Korean ? "한국어" : sheet.Columns[column]);
        int note = sheet.Note;
        if (note > 0)
        {
            string now = EditorGUILayout.TextField("어디 (메모)", row.Words[note] ?? "");
            if (now != row.Words[note]) { row.Words[note] = now; Changed(); }
        }
        var problems = sheet.Problems(row);
        foreach (var (error, what) in problems) EditorGUILayout.HelpBox(what, error ? MessageType.Error : MessageType.Warning);
        DrawVoice(row);
        DrawUsage(row);
        EditorGUILayout.EndScrollView();
    }

    // Multi-line words: a line break here is \n in the file.
    private void Area(TextSheet.Row row, int column, string label)
    {
        GUILayout.Label(label + "  · Enter로 줄바꿈" + (row.Changed(column) ? "  (고침 · 원래: " + Short(row.Saved[column]) + ")" : ""), EditorStyles.miniBoldLabel);
        string shownWords = (row.Words[column] ?? "").Replace("\\n", "\n");
        string typed = EditorGUILayout.TextArea(shownWords, GUILayout.MinHeight(38f));
        if (typed == shownWords) return;
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
            if (GUILayout.Button("다시 읽기", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                try { sheet.Refresh(); } catch (IOException error) { loadError = error.Message; }
                usage = null;
                Changed();
            }
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("모두 되돌리기", EditorStyles.toolbarButton, GUILayout.Width(90f))
                    && EditorUtility.DisplayDialog("글자 편집기", $"고친 {count}줄을 모두 저장 전으로 되돌릴까요?", "되돌리기", "취소"))
                {
                    foreach (var row in sheet.Rows) { for (int c = 0; c < row.Words.Length; c++) row.Words[c] = row.Saved[c]; row.Conflict = null; }
                    GUI.FocusControl(null);
                    Changed();
                }
                if (GUILayout.Button(count > 0 ? $"저장 ({count}줄)" : "저장", EditorStyles.toolbarButton, GUILayout.Width(100f))) Save();
            }
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
