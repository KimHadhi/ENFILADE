using TMPro;
using UnityEngine;

/// <summary>
/// The line at the top of the screen that says something new (the draft's top line, in the addon's words): a small
/// spaced label, the words under it decoding letter by letter, and a thin line opening out under them from the middle;
/// it stands for Notice.seconds and fades. A new objective comes here (in the place of the old card that flew into the
/// bar, ObjectiveAlert). Something to fear would take the accent.
/// </summary>
public sealed class MkNotice : MkPiece
{
    private readonly MkShapes shapes;
    private readonly TextMeshProUGUI label, words;
    private string shownLabel = "", shownWords = "";
    private float born = -99f;
    private bool alert;

    public MkNotice(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Notice", new Vector2(.5f, 1f))
    {
        shapes = MkShapes.Make("Shapes", Frame);
        label = MkText.Make("Label", Frame, Look, .5f, 0f);
        words = MkText.Make("Words", Frame, Look, .5f, 0f);
    }

    /// <summary>Says <paramref name="text"/> under <paramref name="head"/>; an alert in the accent.</summary>
    public void Show(string head, string text, bool isAlert)
    {
        shownLabel = head ?? "";
        shownWords = text ?? "";
        alert = isAlert;
        born = Time.unscaledTime;
    }

    public override void Tick(float dt)
    {
        var n = Look.Line;
        var c = Look.Colour;
        float age = Time.unscaledTime - born, until = n.seconds;
        float shown = age < 0f ? 0f : Mathf.Clamp01((until + n.fadeOut - age) / Mathf.Max(.01f, n.fadeOut));
        Group.alpha = shown;
        shapes.Clear();
        if (shown <= 0f)
        {
            MkText.Say(label, ""); MkText.Say(words, "");
            shapes.Show();
            return;
        }
        float x = 960f, y = n.y;
        MkText.Label(label, Look, MkText.Spaced(shownLabel.ToUpperInvariant()));
        MkText.At(label, x, y);
        label.color = c.tag;
        MkText.Size(words, Look, Look.Text.history * n.size);
        MkText.Say(words, MkText.Scramble(shownWords, Mathf.Clamp01(age / Mathf.Max(.01f, n.decode)), Look.Taken.chars));
        MkText.At(words, x, y + n.textRise);
        Color ink = alert ? c.accent : c.text;
        words.color = ink;
        float wide = n.lineWidth * Mathf.Clamp01(age / Mathf.Max(.01f, n.lineGrow));
        shapes.Box(x - wide * .5f, y + n.lineRise, wide, n.lineThick, MkText.Fade(ink, n.lineAlpha));
        shapes.Show();
    }
}
