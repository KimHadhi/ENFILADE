using UnityEngine;

/// <summary>
/// A hall's palm scanner in the ENFILADE tower (the user: "입구 앞에는 손을 꾹 대고 있는 스캐너가 바닥에 솟아 있어. 누군가가
/// 이 스캐너에 손바닥을 꾹 대는 순간 사람들한테 10초 카운트다운이 시작해"). It rises out of the floor beside a lane's door
/// when the hall's lanes are to be chosen; whoever keeps a palm on it ([F] held) when the count runs out goes through
/// that door (TowerRun decides, every PC shows). In the world's story the tower is an army training block whose
/// killhouses open to a trainee's palm, one trainee to a lane.
/// Its plate says what it is doing: breathing white, free; pink, someone's palm is on it; flickering pink with a line
/// running up it, this PC's own palm being read; steady bright pink, its lane was chosen. Sunk, it shows nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerScanner : MonoBehaviour
{
    public enum Look : byte { Down, Free, Held, Mine, Chosen }

    // How far under the floor it waits (m) and how fast it comes up (share of the way a second).
    private const float Sink = 1.3f, RiseSpeed = 1f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly Color White = new Color(.85f, .9f, 1f), Pink = new Color(1f, .31f, .47f);

    [SerializeField, Tooltip("The stretch whose hall it stands in (0 the lobby).")]
    private int segment;
    [SerializeField, Tooltip("The lane whose door it opens (0-3).")]
    private int lane;
    [SerializeField, Tooltip("The plate the palm goes on.")]
    private Renderer plate;
    [SerializeField, Tooltip("The light strip down its front.")]
    private Renderer strip;
    [SerializeField, Tooltip("The bar that runs up the plate while this PC's palm is read.")]
    private Transform scanLine;

    private Vector3 up, lineFrom;
    private float shown;
    private Look look = Look.Down;
    private MaterialPropertyBlock block;

    public int Segment => segment;
    public int Lane => lane;
    /// <summary>Where the palm goes: reach is measured from it.</summary>
    public Vector3 Palm => plate != null ? plate.transform.position : transform.position + Vector3.up * 1.05f;
    /// <summary>Wanted up (the hall is choosing, or its lane was chosen).</summary>
    public bool Raised { get; set; }
    /// <summary>All the way up: a palm can go on it.</summary>
    public bool Ready => Raised && shown >= .98f;

    public void Show(Look value) => look = value;

    private void Awake()
    {
        up = transform.localPosition;
        transform.localPosition = up + Vector3.down * Sink;
        if (scanLine != null) lineFrom = scanLine.localPosition;
    }

    private void Update()
    {
        shown = Mathf.MoveTowards(shown, Raised ? 1f : 0f, RiseSpeed * Time.deltaTime);
        float eased = 1f - (1f - shown) * (1f - shown);
        transform.localPosition = up + Vector3.down * (Sink * (1f - eased));
        Colour();
    }

    private void Colour()
    {
        float t = Time.time;
        Color plateColour, stripColour;
        float plateGlow, stripGlow;
        switch (!Raised ? Look.Down : look)
        {
            case Look.Free:
                plateColour = White; stripColour = White;
                plateGlow = .7f + .35f * Mathf.Sin(t * 2.4f); stripGlow = plateGlow * .8f;
                break;
            case Look.Held:
                plateColour = stripColour = Pink;
                plateGlow = 1.6f; stripGlow = 1.4f;
                break;
            case Look.Mine:
                plateColour = stripColour = Pink;
                plateGlow = 1.8f + .9f * Mathf.Abs(Mathf.Sin(t * 17f)); stripGlow = 2.2f;
                break;
            case Look.Chosen:
                plateColour = stripColour = Pink;
                plateGlow = 2.6f; stripGlow = 2.6f;
                break;
            default:
                plateColour = stripColour = new Color(.08f, .08f, .1f);
                plateGlow = stripGlow = 0f;
                break;
        }
        Paint(plate, plateColour, plateGlow);
        Paint(strip, stripColour, stripGlow);
        if (scanLine != null)
        {
            bool reading = Raised && look == Look.Mine;
            if (scanLine.gameObject.activeSelf != reading) scanLine.gameObject.SetActive(reading);
            if (reading) scanLine.localPosition = lineFrom + Vector3.forward * (Mathf.PingPong(t * 1.6f, 1f) - .5f) * .26f;
        }
    }

    private void Paint(Renderer target, Color colour, float glow)
    {
        if (target == null) return;
        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetColor(BaseColor, glow > 0f ? colour : colour * .5f);
        block.SetColor(EmissionColor, colour * glow);
        target.SetPropertyBlock(block);
    }
}
