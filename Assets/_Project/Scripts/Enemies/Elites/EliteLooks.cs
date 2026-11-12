using System;
using UnityEngine;

/// <summary>
/// What the elites look and sound like, at Resources/Enemies/EliteLooks: the memo's (2026-11-11: ninja, candyman,
/// breadbag; 2026-11-12: reaper) and the brute, exploder and charger. At the user's word of 2026-11-12 ("엘리트 좀비들은
/// 눈에 띄게 특정 좀비 외형으로만 고정하자") a row's body is that kind's one fixed look, worn on every PC (ZombieBody.Pick),
/// and ZombieLooks.characters leaves those bodies out so no plain zombie wears them: the brute a riot cop, the exploder
/// the yellow hazmat suit, the charger a footballer, the candyman a clown, the reaper a firefighter with an axe and its
/// own chop. A row without a body (ninja, breadbag) wears one of the pack's, a little bigger or smaller. Each memo elite
/// has a light of its own colour and something in its hands that a few boxes stand in for (ZombieElite builds them);
/// drop a prop for the right hand or one for the head into its row and the placeholder gives way. Sounds left empty are
/// made in code.
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
        [Tooltip("Worn on its head (the breadbag's bag). Centred on the head bone. Empty: a placeholder, or nothing (none " +
                 "over a body of its own).")]
        public GameObject headProp;
        [Tooltip("Its swing, in place of one of the pack's (ZombieLooks.attacks): the reaper's axe chop. Empty: the pack's.")]
        public AnimationClip swing;
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

    [Header("Reaper (ZombieReaper)")]
    [Tooltip("As it draws its scythe back to charge: a blade's ring, played low. One is picked each time. Empty: none.")]
    public AudioClip[] reaperCharge = new AudioClip[0];
    [Tooltip("The scythe going round, played low so a light swing sounds heavy. One is picked each time. Empty: made in code.")]
    public AudioClip[] reaperSwing = new AudioClip[0];

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
