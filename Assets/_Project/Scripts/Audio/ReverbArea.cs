using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One area's reverb (backlog 611): a box, turned and scaled with its object, that gives everything heard while the
/// player's ears are inside it one of Unity's reverb presets (Room, Hallway, StoneCorridor, SewerPipe, Hangar, Cave…).
/// Where boxes overlap the higher priority wins, then the smaller box, so a room inside a street area takes over
/// there; preset Off keeps a spot dry; outside every box there is no reverb. Moving between areas crossfades over
/// Tables/Reverb.csv Area.fadeTime. Shots, their tails (GunReverb), confirm sounds and the UI stay out of it.
/// Place one with GameObject → Audio → Reverb Area and drag the box's faces in the Scene view.
/// It drives an AudioReverbZone of its own kept at the listener, closer for more of the preset, so nothing is added to
/// the listener's own filters (SlowMotionSound's muffling).
/// </summary>
[AddComponentMenu("Audio/Reverb Area"), DisallowMultipleComponent]
public sealed class ReverbArea : MonoBehaviour
{
    private const string TableName = "Reverb", TableRow = "Area";
    // The zone's full effect reaches just past the ears at full weight; at no weight they are at its edge.
    private const float Inner = .01f, Reach = 1f;

    [SerializeField, Tooltip("Middle of the box, in this object's space.")]
    private Vector3 center = Vector3.zero;
    [SerializeField, Tooltip("Size of the box, in this object's space.")]
    private Vector3 size = new Vector3(10f, 4f, 10f);
    [SerializeField, Tooltip("How the area sounds. Off keeps a spot inside a bigger area dry.")]
    private AudioReverbPreset preset = AudioReverbPreset.Room;
    [SerializeField, Range(0f, 1f), Tooltip("How much of the preset is heard (1 = all of it).")]
    private float strength = 1f;
    [SerializeField, Tooltip("Where boxes overlap the higher priority wins; between equal ones, the smaller box.")]
    private int priority;

    private static readonly List<ReverbArea> areas = new List<ReverbArea>();
    private static ReverbArea chosen;
    private static int chosenFrame = -1;
    private static float fadeTime = .5f;
    private static bool tableRead;

    private AudioReverbZone zone;
    private float weight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        areas.Clear();
        chosen = null;
        chosenFrame = -1;
        tableRead = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => tableRead = false;

    private void OnEnable()
    {
        areas.Add(this);
        weight = 0f;
        if (!Application.isPlaying) return;
        if (zone == null)
            zone = FeelAssets.CreateRuntimeRoot("Reverb Zone (" + name + ")", gameObject, "Audio").AddComponent<AudioReverbZone>();
        zone.reverbPreset = preset;
        zone.minDistance = Inner;
        zone.maxDistance = Reach;
        zone.enabled = false;
    }

    private void OnDisable()
    {
        areas.Remove(this);
        chosenFrame = -1;
        if (zone != null) zone.enabled = false;
    }

    private void OnDestroy()
    {
        if (zone != null) Destroy(zone.gameObject);
    }

    private void OnValidate()
    {
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        if (zone != null) zone.reverbPreset = preset;
    }

    private void LateUpdate()
    {
        var listener = GameAudio.Listener;
        if (zone == null || listener == null) return;
        Vector3 ears = listener.transform.position;
        float target = Chosen(ears) == this ? strength : 0f;
        float time = FadeTime();
        weight = time > 0f ? Mathf.MoveTowards(weight, target, Time.unscaledDeltaTime / time) : target;
        zone.enabled = weight > 0f;
        if (zone.enabled) zone.transform.position = ears + Vector3.up * (Inner + (1f - weight) * (Reach - Inner));
    }

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

    // The area the ears are in, worked out once a frame for every area.
    private static ReverbArea Chosen(Vector3 ears)
    {
        if (chosenFrame == Time.frameCount) return chosen;
        chosenFrame = Time.frameCount;
        chosen = null;
        foreach (var area in areas)
        {
            if (area == null || !area.Contains(ears)) continue;
            if (chosen == null || area.priority > chosen.priority || (area.priority == chosen.priority && area.Volume < chosen.Volume))
                chosen = area;
        }
        return chosen;
    }

    private static float FadeTime()
    {
        if (tableRead) return fadeTime;
        tableRead = true;
        if (GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue("fadeTime", out string raw)
            && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            fadeTime = Mathf.Max(0f, value);
        return fadeTime;
    }

    private void OnDrawGizmos() => DrawBox(.06f, .45f);

    private void OnDrawGizmosSelected() => DrawBox(.12f, 1f);

    private void DrawBox(float fill, float line)
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        var color = preset == AudioReverbPreset.Off ? new Color(.65f, .65f, .65f) : new Color(.35f, .75f, 1f);
        color.a = fill;
        Gizmos.color = color;
        Gizmos.DrawCube(center, size);
        color.a = line;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(ReverbArea)), UnityEditor.CanEditMultipleObjects]
    private sealed class AreaEditor : UnityEditor.Editor
    {
        private readonly UnityEditor.IMGUI.Controls.BoxBoundsHandle handle = new UnityEditor.IMGUI.Controls.BoxBoundsHandle();

        // The box's faces can be dragged in the Scene view.
        private void OnSceneGUI()
        {
            var area = (ReverbArea)target;
            var data = new UnityEditor.SerializedObject(area);
            var centerProperty = data.FindProperty(nameof(center));
            var sizeProperty = data.FindProperty(nameof(size));
            using (new UnityEditor.Handles.DrawingScope(new Color(.35f, .75f, 1f), area.transform.localToWorldMatrix))
            {
                handle.center = centerProperty.vector3Value;
                handle.size = sizeProperty.vector3Value;
                UnityEditor.EditorGUI.BeginChangeCheck();
                handle.DrawHandle();
                if (!UnityEditor.EditorGUI.EndChangeCheck()) return;
                centerProperty.vector3Value = handle.center;
                sizeProperty.vector3Value = handle.size;
                data.ApplyModifiedProperties();
            }
        }

        [UnityEditor.MenuItem("GameObject/Audio/Reverb Area", false, 10)]
        private static void Create(UnityEditor.MenuCommand command)
        {
            var host = new GameObject("Reverb Area");
            UnityEditor.GameObjectUtility.SetParentAndAlign(host, command.context as GameObject);
            var view = UnityEditor.SceneView.lastActiveSceneView;
            if (command.context == null && view != null) host.transform.position = view.pivot;
            host.AddComponent<ReverbArea>();
            UnityEditor.Undo.RegisterCreatedObjectUndo(host, "Create Reverb Area");
            UnityEditor.Selection.activeObject = host;
        }
    }
#endif
}
