using TMPro;
using UnityEngine;

/// <summary>
/// Text that arrives instead of just appearing. Decode: every letter flickers through random marks and they lock into
/// the real text one after another, left to right, like a code being cracked. Type: the letters come out one by one
/// with a block cursor at the end, like a teleprinter. Plain text only (no rich-text tags). Unscaled time.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitScramble : MonoBehaviour
{
    private const string Marks = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789#/<>+=";
    // How often the flickering marks change (s).
    private const float FlickerStep = .045f;

    private TMP_Text text;
    private string target = "";
    private float startAt, seconds;
    private bool typing;

    /// <summary>The scrambler on <paramref name="label"/>, added the first time.</summary>
    public static KitScramble On(TMP_Text label)
        => label.TryGetComponent(out KitScramble scramble) ? scramble : label.gameObject.AddComponent<KitScramble>();

    /// <summary>Decodes into <paramref name="value"/> after <paramref name="delay"/> over <paramref name="length"/> (s).</summary>
    public void Decode(string value, float delay = 0f, float length = .5f) => Begin(value, delay, length, false);

    /// <summary>Types <paramref name="value"/> out after <paramref name="delay"/>, <paramref name="perSecond"/> letters a second.</summary>
    public void Type(string value, float delay = 0f, float perSecond = 60f)
        => Begin(value, delay, (value?.Length ?? 0) / Mathf.Max(1f, perSecond), true);

    /// <summary>Shows the whole text at once, stopping whatever was running.</summary>
    public void Settle()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        if (text != null) text.text = target;
        enabled = false;
    }

    private void Begin(string value, float delay, float length, bool type)
    {
        if (text == null) text = GetComponent<TMP_Text>();
        target = value ?? "";
        startAt = Time.unscaledTime + delay;
        seconds = Mathf.Max(.01f, length);
        typing = type;
        enabled = true;
        Apply(0f);
    }

    private void Update() => Apply(Time.unscaledTime - startAt);

    private void Apply(float since)
    {
        if (text == null) { enabled = false; return; }
        float k = since / seconds;
        if (k >= 1f) { Settle(); return; }
        // Nothing shows until it starts.
        if (since <= 0f) { text.text = ""; return; }
        int locked = Mathf.FloorToInt(k * target.Length);
        if (typing)
        {
            // The cursor blinks at the end while it types.
            bool cursor = Mathf.Repeat(Time.unscaledTime, .2f) < .12f;
            text.text = target.Substring(0, locked) + (cursor ? "_" : "");
            return;
        }
        int tick = Mathf.FloorToInt(Time.unscaledTime / FlickerStep);
        var letters = target.ToCharArray();
        for (int i = locked; i < letters.Length; i++)
        {
            if (char.IsWhiteSpace(letters[i])) continue;
            letters[i] = Marks[(int)((uint)(tick * 7919 + i * 104729) % (uint)Marks.Length)];
        }
        text.text = new string(letters);
    }
}
