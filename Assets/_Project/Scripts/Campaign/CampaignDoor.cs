using UnityEngine;

/// <summary>
/// Stage 5D: a rest stop's door. It starts open; anyone presses [F] by it to shut or open it (CampaignRun: the host
/// decides, a guest asks, everyone sees it through the run's status). A rest stop's checkpoint is reached only with
/// everyone inside and its doors shut, and while that lasts the director sends nobody (CampaignDirector); opening it
/// again is the way on. It slides into the wall beside the doorway. Shut, it blocks players with its collider and
/// enemies with a carving NavMeshObstacle; the walk mesh was baked without it (GreyboxBuilder.Door).
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignDoor : MonoBehaviour
{
    // How fast it slides, as a share of its way a second.
    private const float Speed = 2.5f;

    [SerializeField, Tooltip("The rest stop's spot (CampaignSteps.spot of its Checkpoint step) this door shuts.")]
    private string spot;
    [SerializeField, Tooltip("How it slides from shut to open (m, in its parent's space).")]
    private Vector3 openOffset = new Vector3(2.5f, 0f, 0f);

    private Vector3 shut;
    private float shown = 1f;

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

    private void Update()
    {
        shown = Mathf.MoveTowards(shown, Closed ? 0f : 1f, Speed * Time.deltaTime);
        transform.localPosition = shut + openOffset * shown;
    }
}
