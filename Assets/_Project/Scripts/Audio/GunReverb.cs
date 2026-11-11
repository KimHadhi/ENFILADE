using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The room's answer to every gunshot, ported from Accurate Gun Reverb (L4D2 workshop 3629393613, reverb.nut). Where
/// the shooter stands, nine rays (up, the four sides, four slanting upward) measure the room once per spot; each shot
/// then adds the tail recorded for that gun in that kind of room: a small room, a large room, outdoors, or a big hall
/// (the shotgun's large-room tail, for every gun). One's own tails play flat like one's own shots; someone else's come
/// from where they stand. Tails are Guns bank keys Reverb/&lt;gun&gt;/&lt;IndoorsClose|IndoorsDistant|OutdoorsClose&gt;, and
/// they skip reverb zones, being reverb already. Numbers: Tables/Reverb.csv row Gun.
/// Unity forces three differences from the original: it keeps one measurement per navigation area, which Unity doesn't
/// number, so this keeps one per cell; its rays stop at people too, who would then stay frozen into the measurement,
/// so these pass through them; and only so many tails sound at once, the oldest giving way, as Source's channels do.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunReverb : MonoBehaviour, ITableBound
{
    public const string TableName = "Reverb", TableRow = "Gun";
    private const string GunBank = "Guns", TailPrefix = "Reverb/", HallGun = "Heavy";
    // The original's numbers are Source units, inches.
    private const float Inch = .0254f;

    [SerializeField, Min(1f), Tooltip("How far each of the nine rays looks (m); the original's 2000 units.")]
    private float rayLength = 2000f * Inch;
    [SerializeField, Min(0f), Tooltip("Open overhead further than this means outdoors (m); the original's 500 units.")]
    private float outdoorHeight = 500f * Inch;
    [SerializeField, Min(0f), Tooltip("Rooms measuring under this get the small-room tail (m); the original's 150 units.")]
    private float closeSize = 150f * Inch;
    [SerializeField, Min(0f), Tooltip("Rooms measuring under this get the large-room tail; bigger ones the outdoor tail, or "
        + "indoors the big hall's (m); the original's 300 units.")]
    private float distantSize = 300f * Inch;
    [SerializeField, Min(.25f), Tooltip("Shots fired within one cell of this size share one measurement (m).")]
    private float cellSize = 2f;
    [SerializeField, Min(0f), Tooltip("How far below the shot the floor under the shooter's feet is looked for (m).")]
    private float floorSearch = 3f;
    [SerializeField, Min(0f), Tooltip("The rays start this far above that floor (m).")]
    private float footLift = .05f;
    [SerializeField, Range(1, 32), Tooltip("Tails of one's own shots sounding at once; the oldest gives way.")]
    private int ownVoices = 8;
    [SerializeField, Range(1, 32), Tooltip("Tails of other players' shots sounding at once; the oldest gives way.")]
    private int otherVoices = 8;
    [SerializeField, Range(0f, 2f), Tooltip("Loudness of a tail against its shot.")]
    private float volume = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the tail behind a suppressed shot, against the same gun's open one: a "
        + "suppressor kills the muzzle blast the room would ring with, and the open tail at full size buried the suppressed "
        + "shot under a plain gunshot's echo (user, 2026-11-11: \"소음기 꼈는데 소음기 소리가 안 남\"). 0: no tail.")]
    private float suppressedVolume = .15f;

    private enum Room { IndoorsClose, IndoorsDistant, OutdoorsClose, Hall }

    private struct Measure
    {
        public float Size;
        public bool Outside;
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public float StartedAt;
    }

    // Up, ±X, ±Z, then the four upward diagonals: the original's Source directions with its Z up as Unity's Y.
    private static readonly Vector3[] Directions =
    {
        Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        new Vector3(1f, 1f, 1f).normalized, new Vector3(-1f, 1f, 1f).normalized,
        new Vector3(1f, 1f, -1f).normalized, new Vector3(-1f, 1f, -1f).normalized,
    };

    private static GunReverb instance;
    private readonly float[] distances = new float[9];
    private readonly Dictionary<Vector3Int, Measure> measured = new Dictionary<Vector3Int, Measure>();
    private readonly List<Voice> own = new List<Voice>(), others = new List<Voice>();
    private SoundBank bank;

    /// <summary>
    /// The room's tail for a shot: the player's own (shooter null, heard flat) or someone else's at their gun. A suppressed
    /// shot's tail is only Suppressed Volume of the open one.
    /// </summary>
    public static void Tail(GunSoundProfile gun, Vector3? shooter, float shotVolume, float reach = 60f, bool suppressed = false)
    {
        if (!Application.isPlaying || shotVolume <= 0f) return;
        var self = Get();
        if (suppressed) shotVolume *= self.suppressedVolume;
        if (shotVolume > 0f) self.Play(gun, shooter, shotVolume, reach);
    }

    /// <summary>Builds the voices and loads the tails at map load rather than on the first shot.</summary>
    public static void Prepare()
    {
        if (!Application.isPlaying) return;
        Get().Bank();
    }

    private static GunReverb Get()
    {
        if (instance != null) return instance;
        instance = FeelAssets.CreateRuntimeRoot("Gun Reverb", groupName: "Audio").AddComponent<GunReverb>();
        GameTables.Bind(TableName, TableRow, instance);
        instance.Build();
        return instance;
    }

    void ITableBound.OnTableApplied() => measured.Clear();

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Play(GunSoundProfile gun, Vector3? shooter, float shotVolume, float reach)
    {
        var tails = Bank();
        if (tails == null) return;
        string key = Key(gun, Classify(Around(Feet(shooter ?? Listening()))), tails);
        var clip = key != null ? tails.Pick(key) : null;
        if (clip == null) return;
        var settings = tails.SettingsOf(key);
        var voice = Free(shooter.HasValue ? others : own);
        var source = voice.Source;
        source.Stop();
        if (shooter.HasValue)
        {
            source.transform.position = shooter.Value;
            source.maxDistance = Mathf.Max(source.minDistance + .1f, reach);
        }
        source.clip = clip;
        source.pitch = settings.RandomPitch();
        GameAudio.SetGain(source, shotVolume * settings.Volume * volume);
        source.Play();
        voice.StartedAt = Time.unscaledTime;
    }

    // The original: a big room indoors takes the shotgun's large-room tail whatever the gun; a gun without tails of its own
    // takes the rifle's.
    private static string Key(GunSoundProfile gun, Room room, SoundBank tails)
    {
        string kind = room == Room.Hall ? nameof(Room.IndoorsDistant) : room.ToString();
        string key = TailPrefix + (room == Room.Hall ? HallGun : gun.ToString()) + "/" + kind;
        if (tails.TryGet(key, out _)) return key;
        key = TailPrefix + GunSoundProfile.Rifle + "/" + kind;
        return tails.TryGet(key, out _) ? key : null;
    }

    private Room Classify(Measure room)
    {
        if (room.Size < closeSize) return Room.IndoorsClose;
        if (room.Size < distantSize) return Room.IndoorsDistant;
        return room.Outside ? Room.OutdoorsClose : Room.Hall;
    }

    // The original's getRoomSize, measured once per cell.
    private Measure Around(Vector3 feet)
    {
        var cell = Vector3Int.FloorToInt(feet / cellSize);
        if (measured.TryGetValue(cell, out var room)) return room;
        room.Outside = false;
        for (int i = 0; i < Directions.Length; i++)
        {
            Cast(feet, Directions[i], rayLength, out _, out distances[i]);
            // The original has no way to tell the sky, so open far enough above counts as outdoors.
            if (i == 0 && distances[i] > outdoorHeight) room.Outside = true;
        }
        System.Array.Sort(distances);
        float sum = 0f;
        for (int i = 2; i < distances.Length - 2; i++) sum += distances[i];
        // As in the original: the five middle distances summed over seven. Its thresholds were tuned to this.
        room.Size = sum / (distances.Length - 2);
        measured[cell] = room;
        return room;
    }

    // The original measures from the shooter's feet (their origin): the floor below the shot, or the shot itself.
    private Vector3 Feet(Vector3 point)
        => Cast(point, Vector3.down, floorSearch, out var floor, out _) ? floor + Vector3.up * footLift : point;

    // The first surface along the ray that isn't a person: where and how far. Nothing within the length: false, and the length.
    private static bool Cast(Vector3 origin, Vector3 direction, float length, out Vector3 point, out float distance)
    {
        float travelled = 0f;
        for (int guard = 0; guard < 8; guard++)
        {
            if (!Physics.Raycast(origin + direction * travelled, direction, out var hit, length - travelled,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                break;
            if (!IsPerson(hit.collider))
            {
                distance = travelled + hit.distance;
                point = hit.point;
                return true;
            }
            travelled += hit.distance + .02f;
            if (travelled >= length) break;
        }
        distance = length;
        point = origin + direction * length;
        return false;
    }

    private static bool IsPerson(Collider collider)
        => collider is CharacterController || collider.GetComponentInParent<PlayerMovement>() != null
           || collider.GetComponentInParent<EnemyHealth>() != null || collider.GetComponentInParent<RagdollCorpse>() != null;

    // The player's own shots are measured where they hear from.
    private static Vector3 Listening()
    {
        var listener = GameAudio.Listener;
        return listener != null ? listener.transform.position : Vector3.zero;
    }

    // A voice that has finished, else the one that started longest ago.
    private static Voice Free(List<Voice> pool)
    {
        Voice oldest = pool[0];
        foreach (var voice in pool)
        {
            if (!voice.Source.isPlaying) return voice;
            if (voice.StartedAt < oldest.StartedAt) oldest = voice;
        }
        return oldest;
    }

    private void Build()
    {
        // The table can write any count; at least one voice each.
        for (int i = 0; i < Mathf.Max(1, ownVoices); i++) own.Add(new Voice { Source = NewSource("Own Tail " + (i + 1), false) });
        for (int i = 0; i < Mathf.Max(1, otherVoices); i++) others.Add(new Voice { Source = NewSource("Other Tail " + (i + 1), true) });
    }

    private AudioSource NewSource(string sourceName, bool spatial)
    {
        var source = new GameObject(sourceName).AddComponent<AudioSource>();
        source.transform.SetParent(transform, false);
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = spatial ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        // A tail is the room's reverb already.
        source.bypassReverbZones = true;
        GameAudio.Bind(source, AudioChannel.Combat);
        return source;
    }

    private SoundBank Bank() => bank != null ? bank : bank = SoundBank.Load(GunBank);
}
