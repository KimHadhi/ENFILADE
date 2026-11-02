using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Where the downed are: name, distance and the time they have left over each one, sliding to the edge of the screen and
/// pointing the way when they're out of view. It takes its colour from their DownedAura, and steps aside up close, where
/// the prompt to get them up takes over. The marks are made up front and only move.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeammateMarkerHUD : MonoBehaviour
{
    private const int MarkCount = 9;
    // Tags sit this far in from the screen edge; the mark points at the chest; up close it hides (px at 1080p, m).
    private const float EdgeMargin = 90f, ChestHeight = .4f, HiddenWithin = 2.2f, BarWidth = 90f, BarHeight = 3f, Pointer = 11f;

    private sealed class Mark
    {
        public RectTransform root, bar, pointer;
        public Image barFill, pointerFill;
        public TextMeshProUGUI label;
    }

    private readonly List<Mark> marks = new List<Mark>();
    private PlayerMovement player;
    private RectTransform canvas;
    private Camera view;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Start()
    {
        var host = new GameObject("Teammate Marks", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupOverlay(host, 26);
        canvas = host.GetComponent<RectTransform>();
        for (int i = 0; i < MarkCount; i++) marks.Add(Build());
    }

    private Mark Build()
    {
        var mark = new Mark { root = FeelUI.Rect("Mark", canvas) };
        mark.label = FeelUI.ScoreLabel("Label", mark.root, 16f, TextAlignmentOptions.Center);
        mark.label.rectTransform.sizeDelta = new Vector2(220f, 22f);
        mark.label.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        mark.bar = FeelUI.Rect("Bar", mark.root);
        mark.bar.anchoredPosition = new Vector2(0f, 12f);
        mark.barFill = mark.bar.gameObject.AddComponent<Image>();
        mark.barFill.raycastTarget = false;
        mark.pointer = FeelUI.Rect("Pointer", mark.root);
        mark.pointer.sizeDelta = new Vector2(Pointer, Pointer);
        mark.pointer.localRotation = Quaternion.Euler(0f, 0f, 45f);
        mark.pointerFill = mark.pointer.gameObject.AddComponent<Image>();
        mark.pointerFill.raycastTarget = false;
        mark.root.gameObject.SetActive(false);
        return mark;
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        if (view == null) view = GetComponentInChildren<Camera>();
        int used = 0;
        if (view != null)
            foreach (var other in PlayerRoster.All)
            {
                if (used >= marks.Count) break;
                if (other == player || !other.IsInitialized || !other.IsDowned || !other.TryGetComponent(out DownedAura aura)) continue;
                float distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance < HiddenWithin) continue;
                Place(marks[used++], other, aura, distance);
            }
        for (int i = used; i < marks.Count; i++) if (marks[i].root.gameObject.activeSelf) marks[i].root.gameObject.SetActive(false);
    }

    private void Place(Mark mark, PlayerMovement other, DownedAura aura, float distance)
    {
        mark.root.gameObject.SetActive(true);
        Vector3 point = view.WorldToViewportPoint(other.transform.position + Vector3.up * ChestHeight);
        Vector2 offset = new Vector2(point.x - .5f, point.y - .5f);
        // Behind the camera the projection mirrors, and the way to turn is the other side.
        if (point.z < 0f) offset = -offset;
        Vector2 size = canvas.rect.size, half = size * .5f - new Vector2(EdgeMargin, EdgeMargin), position = Vector2.Scale(offset, size);
        float reach = Mathf.Max(Mathf.Abs(position.x) / half.x, Mathf.Abs(position.y) / half.y);
        // A mark straight behind has no direction left: send it down, the way a turn would bring them in.
        if (point.z < 0f && position.sqrMagnitude < 1f) position = Vector2.down * half.y;
        else if (point.z < 0f || reach > 1f) position /= Mathf.Max(reach, .001f);
        mark.root.anchoredPosition = position;

        Color color = aura.Color;
        string name = other.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : other.name;
        mark.label.text = name + "  " + Mathf.RoundToInt(distance) + "m";
        mark.label.color = color;
        mark.barFill.color = color;
        mark.pointerFill.color = color;
        mark.bar.sizeDelta = new Vector2(BarWidth * (1f - aura.Urgency), BarHeight);
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
