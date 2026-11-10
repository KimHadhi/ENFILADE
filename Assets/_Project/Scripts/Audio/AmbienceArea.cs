using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One area's background sound (user, 2026-11-10: "구역별로"): a box, turned and scaled with its object, inside which
/// the map's own ambience (MapAmbience, the scene's row in Tables/Ambience) gives way to the area's: a shop's room tone,
/// an alley's bins, the street under the rooftops. <see cref="Sound"/> is a row id in Tables/Ambience, so which files
/// play there and how loud is set in the table, not in the scene. Where boxes overlap the higher priority wins, then
/// the smaller box, as with ReverbArea. The chapter builder (GreyboxBuilder.Ambience) places them from the layout code.
/// </summary>
[AddComponentMenu("Audio/Ambience Area"), DisallowMultipleComponent]
public sealed class AmbienceArea : MonoBehaviour
{
    [SerializeField, Tooltip("Middle of the box, in this object's space.")]
    private Vector3 center = Vector3.zero;
    [SerializeField, Tooltip("Size of the box, in this object's space.")]
    private Vector3 size = new Vector3(10f, 4f, 10f);
    [SerializeField, Tooltip("The row in Tables/Ambience that plays inside the box.")]
    private string sound = "";
    [SerializeField, Tooltip("Where boxes overlap the higher priority wins; between equal ones, the smaller box.")]
    private int priority;

    private static readonly List<AmbienceArea> areas = new List<AmbienceArea>();
    private static AmbienceArea chosen;
    private static int chosenFrame = -1;

    /// <summary>The Tables/Ambience row heard inside the box.</summary>
    public string Sound => sound;

    /// <summary>Every area that is on now.</summary>
    public static IReadOnlyList<AmbienceArea> All => areas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        areas.Clear();
        chosen = null;
        chosenFrame = -1;
    }

    private void OnEnable()
    {
        areas.Add(this);
        chosenFrame = -1;
    }

    private void OnDisable()
    {
        areas.Remove(this);
        chosenFrame = -1;
    }

    private void OnValidate() => size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

    /// <summary>Whether a world point is inside the box.</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point) - center, half = size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private float Volume
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Abs(size.x * scale.x * size.y * scale.y * size.z * scale.z);
        }
    }

    /// <summary>The area the ears are in (null for none), worked out once a frame.</summary>
    public static AmbienceArea Chosen(Vector3 ears)
    {
        if (chosenFrame == Time.frameCount) return chosen;
        chosenFrame = Time.frameCount;
        chosen = null;
        foreach (var area in areas)
        {
            if (area == null || string.IsNullOrEmpty(area.sound) || !area.Contains(ears)) continue;
            if (chosen == null || area.priority > chosen.priority || (area.priority == chosen.priority && area.Volume < chosen.Volume))
                chosen = area;
        }
        return chosen;
    }

    private void OnDrawGizmos() => DrawBox(.04f, .35f);

    private void OnDrawGizmosSelected() => DrawBox(.1f, 1f);

    private void DrawBox(float fill, float line)
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        var color = new Color(.55f, 1f, .55f, fill);
        Gizmos.color = color;
        Gizmos.DrawCube(center, size);
        color.a = line;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
    }
}
