using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the throwing knife (PlayerKnives, ThrownKnife) looks and sounds like, at Resources/Combat/ThrowingKnife: the Low
/// Poly Weapon Bundle's Combat Knife_A, and [MW] Quick Throwing Knife's sounds (GMod 3650480912, kept in
/// Art/Audio/Library/ThrowingKnife): drawing it (scan_knife/pull_out), the throw (scan_knife/throw, blade and cloth
/// together as the addon plays them), a stab into flesh (fc4_tk/knife_stab) and a hit on anything else (fc4_tk/knife_hit).
/// Only the model's mesh and materials are used. Any slot left empty is quiet, and without a model the knife is built
/// from boxes; a clip dropped into a slot here is heard at once.
/// </summary>
[CreateAssetMenu(fileName = "ThrowingKnife", menuName = "ENFILADE/Combat/Throwing Knife")]
public sealed class ThrowingKnifeLibrary : ScriptableObject
{
    public const string ResourcePath = "Combat/ThrowingKnife";

    [Tooltip("The knife's model. Its longest side is taken as the blade's length and its thinnest as the flat of the blade.")]
    public GameObject model;
    [Tooltip("The blade drawn into the off hand (the key pressed), and pulled out of a wall or a body to take it back.")]
    public AudioClip[] draw = new AudioClip[0];
    [Tooltip("The cloth under the draw.")]
    public AudioClip[] drawCloth = new AudioClip[0];
    [Tooltip("The throw's swing (0.1 s after the key is let go).")]
    public AudioClip[] swing = new AudioClip[0];
    [Tooltip("The cloth under the swing.")]
    public AudioClip[] swingCloth = new AudioClip[0];
    [Tooltip("The blade going into a body.")]
    public AudioClip[] hitFlesh = new AudioClip[0];
    [Tooltip("The blade meeting anything else: sticking in, or glancing off and clattering down.")]
    public AudioClip[] hitWorld = new AudioClip[0];
    [Tooltip("Taking a knife up off the floor.")]
    public AudioClip[] pickup = new AudioClip[0];

    private static ThrowingKnifeLibrary instance;
    private static bool looked;
    private readonly Dictionary<AudioClip[], int> last = new Dictionary<AudioClip[], int>();

    /// <summary>The library, or null when the asset is missing.</summary>
    public static ThrowingKnifeLibrary Instance
    {
        get
        {
            if (looked) return instance;
            looked = true;
            return instance = Resources.Load<ThrowingKnifeLibrary>(ResourcePath);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() { instance = null; looked = false; }

    /// <summary>One of these clips, never the same twice running when there are more; null with none.</summary>
    public AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int pick = Random.Range(0, clips.Length);
        if (clips.Length > 1 && last.TryGetValue(clips, out int before) && pick == before) pick = (pick + 1) % clips.Length;
        last[clips] = pick;
        return clips[pick];
    }

    /// <summary>Every clip here, so their data can be read in ahead of the first throw.</summary>
    public IEnumerable<AudioClip> All()
    {
        foreach (var set in new[] { draw, drawCloth, swing, swingCloth, hitFlesh, hitWorld, pickup })
        {
            if (set == null) continue;
            foreach (var clip in set) if (clip != null) yield return clip;
        }
    }
}
