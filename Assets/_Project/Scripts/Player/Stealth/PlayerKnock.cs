using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

/// <summary>
/// The knock key (Player/Knock, H unless picked otherwise in Settings → 조작): a knuckle rap on the wall right in front
/// of the player, the 2026-11-11 stealth memo's item 47 ("벽을 두드려 근처 좀비를 모퉁이로 끌어오기"). It sounds where
/// the knuckles meet the wall and is a lure (Noise.Lure), as a thrown stone's clack is: every zombie that isn't awake and
/// hears it turns to look that way a while, and the nearest walks over to see what knocked, so a player waiting round
/// the corner can take it from behind as it looks about. It never says who knocked nor makes any zombie surer someone is
/// there. With no wall within reach nothing happens. Every PC hears it (PlayerNetworkCombat's KnockRpc) and the host's is
/// the one its zombies hear. The sound is Advanced Stealth's knock (StealthSounds.knocks), else one made here. On every
/// player (PlayerNetworkCombat adds it, and StealthView on the local one); only the local player's reads the key.
/// Numbers: Stealth.csv Knock.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerKnock : MonoBehaviour, ITableBound
{
    public const string TableName = "Stealth", TableRow = "Knock";
    private const int Rate = 44100;
    private static AudioClip made;
    private static StealthSounds sounds;
    private static bool soundsLooked;

    [SerializeField, Min(.3f), Tooltip("How near a wall has to be, straight ahead of the eyes (m).")]
    private float reach = 1.4f;
    [SerializeField, Min(0f), Tooltip("How far the knock carries to a zombie (m), as a stone's clack does (Throw.noise).")]
    private float noise = 10f;
    [SerializeField, Min(0f), Tooltip("Seconds between knocks.")]
    private float cooldown = 1.2f;
    [SerializeField, Range(0f, 1f), Tooltip("The knock's volume.")]
    private float volume = .9f;

    /// <summary>This PC's player knocked: where on the wall and the wall's facing, for friends' PCs (PlayerNetworkCombat).</summary>
    public event Action<Vector3, Vector3> Knocked;

    private PlayerMovement movement;
    private InputAction key;
    private float readyAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sounds = null;
        soundsLooked = false;
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        GameTables.Bind(TableName, TableRow, this);
    }

    void ITableBound.OnTableApplied() { }

    private void Update()
    {
        if (movement == null || !movement.IsLocal || !movement.InputAvailable || movement.IsDead || movement.IsHeld
            || movement.CombatInputLocked || !InputFocus.TakesInput()) return;
        if (key == null) key = KeyBindings.Find("Player/Knock");
        if (!KeyBindings.Pressed(key) || Time.time < readyAt) return;
        var eye = Camera.main;
        if (eye == null) return;
        // A wall (or a door, a pillar, a crate's side) straight ahead and near; the floor or a ceiling doesn't count.
        if (!Physics.Raycast(eye.transform.position, eye.transform.forward, out RaycastHit wall, reach, ThrownStone.WorldMask,
                QueryTriggerInteraction.Ignore) || Mathf.Abs(wall.normal.y) > .6f) return;
        readyAt = Time.time + cooldown;
        Rap(wall.point, wall.normal);
        GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(-.6f, 0f, Random.value < .5f ? -.4f : .4f));
        Knocked?.Invoke(wall.point, wall.normal);
    }

    /// <summary>A friend's knock, heard here: the same sound, and the host's zombies hear it.</summary>
    internal void ShowKnock(Vector3 point, Vector3 normal) => Rap(point, normal);

    // The rap itself: heard where it was, and a lure a hand's width off the wall so the wall itself doesn't muffle it.
    private void Rap(Vector3 point, Vector3 normal)
    {
        PlayAt(Clip(), point, volume, Random.Range(.94f, 1.06f));
        Noise.Make(point + normal * .12f, noise, gameObject, true);
    }

    private static void PlayAt(AudioClip clip, Vector3 at, float level, float pitch)
    {
        if (clip == null || level <= 0f) return;
        var host = new GameObject("Knock Sound");
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "Audio");
        host.transform.position = at;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1.5f;
        source.maxDistance = 35f;
        source.dopplerLevel = 0f;
        source.pitch = pitch;
        GameAudio.Bind(source, AudioChannel.Combat);
        source.PlayOneShot(clip, Mathf.Clamp01(level));
        Destroy(host, clip.length / Mathf.Max(.1f, pitch) + .1f);
    }

    // Advanced Stealth's knock (Resources/Stealth/StealthSounds), else two knuckle raps made here.
    private static AudioClip Clip()
    {
        if (!soundsLooked) { soundsLooked = true; sounds = Resources.Load<StealthSounds>(StealthSounds.ResourcePath); }
        var picked = sounds != null ? sounds.PickKnock() : null;
        return picked != null ? picked : made != null ? made : made = Make();
    }

    // Two dull raps on wood: a thud of low noise with a short hollow ring, the second a little softer.
    private static AudioClip Make()
    {
        var data = new float[(int)(Rate * .42f)];
        var random = new System.Random(91);
        float[] starts = { 0f, .19f }, levels = { 1f, .8f };
        float held = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .25f;
            float sample = 0f;
            for (int rap = 0; rap < starts.Length; rap++)
            {
                float u = t - starts[rap];
                if (u < 0f) continue;
                sample += levels[rap] * (held * Mathf.Exp(-u / .006f) * .9f + Mathf.Sin(2f * Mathf.PI * 210f * u) * Mathf.Exp(-u / .03f) * .6f
                    + Mathf.Sin(2f * Mathf.PI * 520f * u) * Mathf.Exp(-u / .012f) * .25f);
            }
            data[i] = sample;
        }
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f) for (int i = 0; i < data.Length; i++) data[i] *= .8f / top;
        var clip = AudioClip.Create("Knock", data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
