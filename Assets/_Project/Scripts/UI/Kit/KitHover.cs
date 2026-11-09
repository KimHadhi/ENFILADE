using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The menus' hover: four small corner brackets close in round whatever the pointer is over and the thing itself leans
/// a few pixels to the right; both leave when the pointer goes. Add it to anything with a raycast target (a card, a
/// button). The brackets are made on first need and again whenever they are gone (a script reload during Play keeps
/// the component but forgets what it made), so it never fails on a missing one.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // How long the arms are, how far out they start and where they settle (px), how fast they close in (s), and how far
    // the hovered thing leans right (px).
    private const float Arm = 10f, From = 9f, To = 3f, Seconds = .1f, Lean = 7f;

    /// <summary>Something with a hover was pointed at (KitRail follows it).</summary>
    public static event System.Action<RectTransform> Entered;

    private RectTransform[] corners;
    private RectTransform rect;
    private Vector2 home;
    private float since = float.PositiveInfinity, lean;
    private bool over, homeKnown, leaning;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!EnsureCorners()) return;
        // Where it rests is read again whenever it is not leaning, so a move by its owner is kept.
        if (!leaning) { home = rect.anchoredPosition; homeKnown = true; }
        over = true;
        since = 0f;
        foreach (var corner in corners) corner.gameObject.SetActive(true);
        Entered?.Invoke(rect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        over = false;
        if (corners == null) return;
        foreach (var corner in corners) if (corner != null) corner.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (over) OnPointerExit(null);
        if (leaning && rect != null) rect.anchoredPosition = home;
        lean = 0f;
        leaning = false;
    }

    private void Update()
    {
        if (!homeKnown || rect == null) return;
        float dt = Time.unscaledDeltaTime;
        lean = Mathf.MoveTowards(lean, over ? 1f : 0f, dt / Seconds);
        if (lean > 0f || leaning)
        {
            rect.anchoredPosition = home + new Vector2(Lean * KitReveal.EaseOut(lean), 0f);
            leaning = lean > 0f;
        }
        if (!over || since > Seconds || !EnsureCorners()) return;
        since += dt;
        float gap = Mathf.Lerp(From, To, KitReveal.EaseOut(Mathf.Clamp01(since / Seconds)));
        for (int i = 0; i < 4; i++)
        {
            bool right = i % 2 == 1, bottom = i >= 2;
            corners[i].anchoredPosition = new Vector2(right ? gap : -gap, bottom ? -gap : gap);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Entered = null;

    // Makes the brackets if there are none (or any went missing). False if this has no rect to put them on.
    private bool EnsureCorners()
    {
        if (rect == null) rect = transform as RectTransform;
        if (rect == null) return false;
        if (corners != null && corners.Length == 4 && corners[0] != null && corners[1] != null && corners[2] != null && corners[3] != null)
            return true;
        if (corners != null) foreach (var old in corners) if (old != null) Destroy(old.gameObject);
        corners = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            bool right = i % 2 == 1, bottom = i >= 2;
            var corner = FeelUI.Rect("Corner", transform);
            corner.anchorMin = corner.anchorMax = new Vector2(right ? 1f : 0f, bottom ? 0f : 1f);
            corner.pivot = new Vector2(0f, 1f);
            corner.sizeDelta = Vector2.zero;
            // One top-left bracket, mirrored into the other corners.
            corner.localScale = new Vector3(right ? -1f : 1f, bottom ? -1f : 1f, 1f);
            KitStyle.Bar("Across", corner, Vector2.zero, new Vector2(Arm, KitStyle.LineWidth), KitStyle.Accent);
            KitStyle.Bar("Down", corner, Vector2.zero, new Vector2(KitStyle.LineWidth, Arm), KitStyle.Accent);
            corner.gameObject.SetActive(false);
            corners[i] = corner;
        }
        return true;
    }
}
