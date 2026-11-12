using UnityEngine;

/// <summary>
/// The hub's stage, where a chapter's results play over the players themselves (memo 37, ResultsShow): they stand in a
/// row across it (its local x) at its spot, facing its forward, the best in the middle; the camera stands out in front
/// at <see cref="cameraAt"/> (local) and backs off for a longer row. The builder (HubBuilder) puts one on the stage.
/// </summary>
[DisallowMultipleComponent]
public sealed class HubStage : MonoBehaviour
{
    [SerializeField, Min(1f), Tooltip("How long the row may get (m).")]
    private float width = 12f;
    [SerializeField, Min(.5f), Tooltip("How far apart the players stand (m), closer when the row would be longer than the stage.")]
    private float spacing = 1.3f;
    [SerializeField, Tooltip("Where the camera stands for one player, on the stage's own axes (m): up, and out in front along its forward.")]
    private Vector3 cameraAt = new Vector3(0f, 1.35f, 6.3f);
    [SerializeField, Min(10f), Tooltip("The camera's field of view (°).")]
    private float fieldOfView = 38f;

    public float FieldOfView => fieldOfView;

    /// <summary>The gap between neighbours for a row of <paramref name="count"/>.</summary>
    public float Spacing(int count) => count <= 1 ? 0f : Mathf.Min(spacing, width / (count - 1));

    /// <summary>Where seat <paramref name="index"/> of a row of <paramref name="count"/> stands, left to right as the camera sees it.</summary>
    public Vector3 Slot(int index, int count)
        => transform.TransformPoint(new Vector3(-(index - (count - 1) * .5f) * Spacing(count), 0f, 0f));

    /// <summary>Which way the row faces: toward the camera.</summary>
    public float Yaw => Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up).eulerAngles.y;

    /// <summary>Where the camera stands for a row of <paramref name="count"/>, far enough back that the whole row fits.</summary>
    public Vector3 CameraPoint(int count, float aspect)
    {
        float row = Spacing(count) * Mathf.Max(0, count - 1) + 1.4f;
        float half = Mathf.Atan(Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad) * Mathf.Max(.5f, aspect));
        float back = Mathf.Max(cameraAt.z, row * .5f / Mathf.Tan(half) + .4f);
        return transform.TransformPoint(new Vector3(cameraAt.x, cameraAt.y, back));
    }

    /// <summary>Where the camera looks: the row's middle a little under waist height, so the players stand in the screen's
    /// upper two thirds and their names and titles fit under their feet.</summary>
    public Vector3 Focus => transform.TransformPoint(new Vector3(0f, .95f, 0f));

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, .31f, .47f, .8f);
        for (int i = 0; i < 4; i++) Gizmos.DrawWireSphere(Slot(i, 4) + Vector3.up * .9f, .3f);
        Gizmos.DrawLine(CameraPoint(4, 16f / 9f), Focus);
    }
}
