using System;
using UnityEngine;

/// <summary>
/// What the memo's elites (2026-11-11: ninja, candyman, breadbag) look and sound like, at Resources/Enemies/EliteLooks.
/// None has a model of its own yet: each wears one of the pack's bodies (ZombieBody) a little bigger or smaller, with a
/// light of its own colour and something in its hands that a few boxes stand in for (ZombieElite builds them). Drop a
/// humanoid body, a prop for the right hand or one for the head into its row and the placeholder gives way; sounds left
/// empty are made in code.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Elite Looks", fileName = "EliteLooks")]
public sealed class EliteLooks : ScriptableObject
{
    public const string ResourcePath = "Enemies/EliteLooks";

    /// <summary>One elite's own looks; empty slots keep the placeholders.</summary>
    [Serializable]
    public struct Look
    {
        public ZombieKind kind;
        [Tooltip("A humanoid body of its own, worn instead of one of the pack's (as ZombieLooks.characters). Empty: the pack's.")]
        public GameObject character;
        [Tooltip("Held in its right hand: the ninja's blade, the candyman's hook, the breadbag's chainsaw. Pointing along +Z, " +
                 "the grip at its origin. Empty: a placeholder made of boxes.")]
        public GameObject handProp;
        [Tooltip("Worn on its head (the breadbag's bag). Centred on the head bone. Empty: a placeholder, or nothing.")]
        public GameObject headProp;
    }

    public Look[] looks = new Look[0];

    [Header("Ninja (ZombieNinja)")]
    [Tooltip("As it vanishes into smoke. One is picked each time.")]
    public AudioClip[] ninjaVanish = new AudioClip[0];
    [Tooltip("As it steps out of the smoke behind someone: the warning to turn. One is picked each time.")]
    public AudioClip[] ninjaAppear = new AudioClip[0];

    [Header("Breadbag (ZombieBreadbag)")]
    [Tooltip("Its chainsaw ticking over, looped while it lives. Empty: made in code.")]
    public AudioClip chainsawIdle;
    [Tooltip("Its chainsaw revved up: the warning, and the cut. Looped. Empty: made in code.")]
    public AudioClip chainsawRev;

    [Header("Candyman (ZombieCandyman)")]
    [Tooltip("The bees about it, looped while it lives. Empty: made in code.")]
    public AudioClip candymanBuzz;

    private static EliteLooks loaded;
    private static bool looked;

    /// <summary>The project's elite looks, or null without the asset (everything is then made in code).</summary>
    public static EliteLooks Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<EliteLooks>(ResourcePath); }
            return loaded;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        loaded = null;
        looked = false;
    }

    /// <summary>This kind's row (default when it has none).</summary>
    public static Look For(ZombieKind kind)
    {
        var assets = Instance;
        if (assets != null && assets.looks != null)
            foreach (var look in assets.looks)
                if (look.kind == kind) return look;
        return new Look { kind = kind };
    }

    /// <summary>One of these clips, or null with none.</summary>
    public static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[UnityEngine.Random.Range(0, clips.Length)];
    }
}
