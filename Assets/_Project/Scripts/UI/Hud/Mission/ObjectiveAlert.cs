using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The new-objective alert at the top of the screen, in the look picked in Settings → HUD → 목표 알림
/// (GameSettings.objectiveAlert; the user, 2026-11-14, of the two drafts: "A랑 B 둘다 마음에 들어서 설정에서 선택할 수
/// 있었으면 좋겠어 일단 기본 A, 심플은 B"):
/// 기본, the card (ObjectiveAlert.Card.cs): a white line is drawn across, a dark card opens up and down from it, NEW
/// OBJECTIVE and 새 목표 come in on a chip, the objective's words land letter by letter, a pink line runs under them, a
/// soft light passes; then the card flies off into the objective bar at the top left, which lights up as it lands
/// (MissionBarLook.Land).
/// 심플, the ribbon (ObjectiveAlert.Ribbon.cs): a band opens from the middle with a pink head, NEW OBJECTIVE and its
/// breathing arrows, and the words are typed onto it after a pink caret; then the band closes.
/// Nothing blinks: what draws the eye breathes slowly (the old alert flashed on and off, and the drafts' fast blink nearly
/// gave the user a panic attack). Built in code on the HUD prefab's ObjectiveAlert, the place at the top centre; MissionHUD
/// says what, how wide it may be (clear of the bar), plays its sound and makes it give way to the Tab screen. Real time.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ObjectiveAlert : MonoBehaviour
{
    // Each letter lands this long after the one before, over this long (s), from this many times its size.
    private const float LetterStep = .035f, LetterSeconds = .3f, LetterFrom = 2f;
    private static readonly Color BodyColor = new Color(.07f, .06f, .08f, .96f);

    private RectTransform root;
    private ObjectiveAlertLook shown;
    private RectTransform target;
    private MissionBarLook landOn;
    private float clock = -1f;
    private bool holding;

    /// <summary>An alert is on screen.</summary>
    public bool Playing => clock >= 0f;

    /// <summary>
    /// Announces <paramref name="words"/>, at most <paramref name="widest"/> px wide, in the look the player picked. The
    /// card flies into <paramref name="bar"/> at its end and lights it up (<paramref name="barLook"/>).
    /// </summary>
    public void Play(string words, float widest, RectTransform bar, MissionBarLook barLook)
    {
        root = (RectTransform)transform;
        shown = GameSettings.Current.objectiveAlert;
        target = bar;
        landOn = barLook;
        clock = 0f;
        gameObject.SetActive(true);
        if (shown == ObjectiveAlertLook.Ribbon) StartRibbon(words, widest);
        else StartCard(words, widest);
        Pose();
    }

    /// <summary>ENFILADE → HUD → 겹침 검사: holds the alert fully open while it measures.</summary>
    public void Hold(bool hold) => holding = hold;

    public void Stop()
    {
        clock = -1f;
        if (card != null) card.gameObject.SetActive(false);
        if (ribbon != null) ribbon.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (clock < 0f) return;
        clock = holding ? (shown == ObjectiveAlertLook.Ribbon ? RibbonHeld : CardHeld) : clock + Time.unscaledDeltaTime;
        if (clock >= (shown == ObjectiveAlertLook.Ribbon ? RibbonLength : CardLength)) { Stop(); return; }
        Pose();
    }

    private void Pose()
    {
        if (shown == ObjectiveAlertLook.Ribbon) PoseRibbon(clock);
        else PoseCard(clock);
    }

    private static RectTransform Stretched(string label, RectTransform parent)
    {
        var rect = FeelUI.Rect(label, parent);
        KitStyle.Stretch(rect);
        return rect;
    }

    private static Image Fill(RectTransform rect, Color color)
    {
        var image = KitStyle.Fill(rect, color);
        image.raycastTarget = false;
        return image;
    }


    // A tiled pattern's uvRect for a rect this size: one tile every tile px.
    private static void Tile(RawImage image, float tile)
    {
        var size = image.rectTransform.rect.size;
        image.uvRect = new Rect(0f, 0f, Mathf.Max(1f, size.x / tile), Mathf.Max(1f, size.y / tile));
    }

    private static TextMeshProUGUI Words(RectTransform rect, float size, Color color, TextAlignmentOptions alignment)
    {
        var text = KitStyle.Flat(rect, "", size, alignment, color);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }
}
