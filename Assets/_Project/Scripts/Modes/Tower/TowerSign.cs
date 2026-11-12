using TMPro;
using UnityEngine;

/// <summary>
/// A sign over a lane's door in an ENFILADE tower hall: the lane's letter and what its rooms hold this stretch, so the
/// party can choose at the scanners ("떼 · 닌자 · 떼"). The day's program decides the words (TowerPlan), so the run writes
/// them at play (TowerRun); the builder only places the sign. Drawn in the HUD's label face (FontRoles).
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerSign : MonoBehaviour
{
    [SerializeField, Tooltip("The stretch whose hall it hangs in (0 the lobby).")]
    private int segment;
    [SerializeField, Tooltip("The lane whose door it is over (0-3).")]
    private int lane;
    [SerializeField, Tooltip("How wide and tall the words may be (m).")]
    private Vector2 size = new Vector2(3.4f, 1.6f);

    private TextMeshPro text;
    private string shown;

    public int Segment => segment;
    public int Lane => lane;

    /// <summary>Puts these words on it (rich text), drawn the first time.</summary>
    public void Write(string words)
    {
        if (words == shown) return;
        shown = words;
        if (text == null)
        {
            text = gameObject.AddComponent<TextMeshPro>();
            FontRoles.Apply(text, FontRoles.Label);
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.fontSize = 3f;
            text.color = KitStyle.Ice;
        }
        text.text = words ?? "";
    }
}
