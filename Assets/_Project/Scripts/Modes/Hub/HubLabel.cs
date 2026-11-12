using TMPro;
using UnityEngine;

/// <summary>
/// Words standing in the hub (memo 46): the stations' signs (사격장, 지도, 클래스 ...) and the range's distance marks. The
/// builder (HubBuilder) sets the words: a Texts id, or the words themselves (a distance such as "25 m"). They read from
/// in front, which is where this object's forward points; a mark on the floor lies with its forward pointing up.
/// </summary>
[DisallowMultipleComponent]
public sealed class HubLabel : MonoBehaviour
{
    [SerializeField, Tooltip("A Texts id, or the words themselves.")]
    private string words = "";
    [SerializeField, Min(.05f), Tooltip("How tall the letters are (m).")]
    private float height = .4f;
    [SerializeField, Min(.1f), Tooltip("How wide the line may get (m).")]
    private float width = 4f;
    [SerializeField, Tooltip("In the one pink instead of white.")]
    private bool accent;

    private void Start()
    {
        string text = Texts.Has(words) ? Texts.Get(words) : words;
        var board = HubDisplays.Board(transform, "Words", new Vector2(width, height * 1.5f), Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
        HubDisplays.Words(board, text, height * HubDisplays.PixelsPerMetre, accent ? KitStyle.Accent : KitStyle.Ice, TextAlignmentOptions.Center)
            .textWrappingMode = TextWrappingModes.NoWrap;
    }
}
