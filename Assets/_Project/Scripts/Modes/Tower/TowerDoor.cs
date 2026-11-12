using UnityEngine;

/// <summary>Which of a tower's doors this is: a hall's door into a lane (its scanner stands by it), a lane's door into the
/// next hall, or a room's way in and way out.</summary>
public enum TowerDoorKind : byte { Departure, Arrival, Entry, Exit }

/// <summary>
/// A steel door in an end wall of the ENFILADE tower. The run (TowerRun) says whether it stands open, every PC from the
/// host's word, and it slides into the wall beside the doorway. It never shuts on anyone: while someone stands in the
/// doorway it waits, so a door can't trap a player in its panel (it moves its collider, it doesn't push). The lamp over
/// it is red while it is shut and the HUD's pink once it opens, so a lane's way on reads from across the room. Shut, it
/// keeps zombies in with a carving NavMeshObstacle; the walk mesh was baked without it (TowerMap).
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerDoor : MonoBehaviour
{
    // How fast it slides, as a share of its way a second.
    private const float Speed = 2.2f;
    // Someone this near the doorway's middle along the wall, and this near across it (m), holds a shutting door open.
    private const float HoldAlong = 1.5f, HoldAcross = .85f;
    // An opening door is heard this far from this PC's player (m), and doors opening together are heard once.
    private const float HeardWithin = 22f, HeardApart = .35f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly Color ShutLight = new Color(1f, .12f, .1f), OpenLight = new Color(1f, .31f, .47f);
    private static float heardAt = float.NegativeInfinity;

    [SerializeField, Tooltip("The level of the tower its doorway is on (TowerLayout).")]
    private int level;
    [SerializeField, Tooltip("The lane it belongs to (0-3).")]
    private int lane;
    [SerializeField]
    private TowerDoorKind kind;
    [SerializeField, Tooltip("How it slides from shut to open (m, in its parent's space).")]
    private Vector3 openOffset = new Vector3(0f, 0f, 2.5f);
    [SerializeField, Tooltip("The lamp over it: red while shut, pink once open.")]
    private Renderer lamp;

    private Vector3 shut;
    private float shown;
    private bool wasOpen;
    private int litOpen = -1;
    private MaterialPropertyBlock block;

    public int Level => level;
    public int Lane => lane;
    public TowerDoorKind Kind => kind;
    /// <summary>Open now, as the run says (it may still be sliding, or held open by someone in the doorway).</summary>
    public bool Open { get; set; }
    /// <summary>The middle of the doorway.</summary>
    public Vector3 Doorway => transform.parent != null ? transform.parent.TransformPoint(shut) : shut;

    // The builder lays it shut, so that is where it starts.
    private void Awake() => shut = transform.localPosition;

    private void Update()
    {
        bool wanted = Open || (shown > 0f && Blocked());
        shown = Mathf.MoveTowards(shown, wanted ? 1f : 0f, Speed * Time.deltaTime);
        float eased = shown * shown * (3f - 2f * shown);
        transform.localPosition = shut + openOffset * eased;
        if (Open != wasOpen)
        {
            wasOpen = Open;
            if (Open) Heard();
        }
        Light(Open);
    }

    // Someone stands in the doorway (any player, on every PC alike: their positions are shared).
    private bool Blocked()
    {
        Vector3 middle = Doorway;
        Vector3 axis = transform.parent != null ? transform.parent.TransformDirection(openOffset) : openOffset;
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-4f) return false;
        axis.Normalize();
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !someone.IsInitialized || someone.IsDead) continue;
            Vector3 apart = someone.transform.position - middle;
            if (apart.y < -1.2f || apart.y > 2.6f) continue;
            float along = Vector3.Dot(apart, axis);
            Vector3 across = apart - axis * along;
            across.y = 0f;
            if (Mathf.Abs(along) < HoldAlong && across.magnitude < HoldAcross) return true;
        }
        return false;
    }

    private void Heard()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || Time.unscaledTime - heardAt < HeardApart) return;
        float distance = Vector3.Distance(mine.transform.position, Doorway);
        if (distance > HeardWithin) return;
        heardAt = Time.unscaledTime;
        KitSound.Play(KitSound.Cue.TowerOpen, Mathf.Lerp(1f, .25f, distance / HeardWithin));
    }

    private void Light(bool open)
    {
        if (lamp == null || litOpen == (open ? 1 : 0)) return;
        litOpen = open ? 1 : 0;
        block ??= new MaterialPropertyBlock();
        lamp.GetPropertyBlock(block);
        Color colour = open ? OpenLight : ShutLight;
        block.SetColor(BaseColor, colour);
        block.SetColor(EmissionColor, colour * (open ? 2.4f : 1.8f));
        lamp.SetPropertyBlock(block);
    }
}
