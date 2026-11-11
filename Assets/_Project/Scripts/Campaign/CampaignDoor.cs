using UnityEngine;

/// <summary>
/// Stage 5D: a rest stop's door. It starts open; anyone presses [F] by it to shut or open it (CampaignRun: the host
/// decides, a guest asks, everyone sees it through the run's status). A rest stop's checkpoint is reached only with
/// everyone inside and its doors shut, and while that lasts the director sends nobody (CampaignDirector); opening it
/// again is the way on. It slides into the wall beside the doorway. Shut, it blocks players with its collider and
/// enemies with a carving NavMeshObstacle; the walk mesh was baked without it (GreyboxBuilder.Door). A kick bursts it
/// open (Slam, through CampaignRun.KickOpen); it is never kicked off its runners, since the rest stop needs it to shut.
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignDoor : MonoBehaviour
{
    // How fast it slides, as a share of its way a second.
    private const float Speed = 2.5f;
    // Kicked open it slides this many times as fast (Beatrun flings a bashed door at four times its speed), and stays
    // open on this PC this long whatever an older word from the host says, until the host's own word comes (s).
    private const float SlamSpeed = 4f, SlamHold = .5f;

    [SerializeField, Tooltip("The rest stop's spot (CampaignSteps.spot of its Checkpoint step) this door shuts.")]
    private string spot;
    [SerializeField, Tooltip("How it slides from shut to open (m, in its parent's space).")]
    private Vector3 openOffset = new Vector3(2.5f, 0f, 0f);

    private Vector3 shut;
    private float shown = 1f, slammedUntil = float.NegativeInfinity;

    public string Spot => spot;
    /// <summary>Shut now (as the run says; it may still be sliding).</summary>
    public bool Closed { get; internal set; }
    /// <summary>The middle of the doorway, for how near someone must be to work it.</summary>
    public Vector3 Doorway => transform.parent != null ? transform.parent.TransformPoint(shut) : shut;

    private void Awake()
    {
        shut = transform.localPosition;
        transform.localPosition = shut + openOffset;
    }

    /// <summary>A kick flung it open (CampaignRun.KickOpen): it bursts open here at once, before the host's word.</summary>
    internal void Slam() => slammedUntil = Time.time + SlamHold;

    private void Update()
    {
        bool slammed = Time.time < slammedUntil;
        shown = Mathf.MoveTowards(shown, Closed && !slammed ? 0f : 1f, Speed * (slammed ? SlamSpeed : 1f) * Time.deltaTime);
        transform.localPosition = shut + openOffset * shown;
    }
}
