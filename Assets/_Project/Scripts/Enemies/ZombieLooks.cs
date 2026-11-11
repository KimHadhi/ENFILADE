using System;
using UnityEngine;

/// <summary>
/// What a zombie can look like and the clips it moves with (multiplayer plan stage 4A): the bought Synty City Zombies
/// characters, and the humanoid clips of Advanced Zombie AI Pro's demo set (only the clips; its AI scripts stay unused,
/// ZombieBody plays them). Resources/Enemies/ZombieLooks.asset. Take a character out of the list to stop it spawning,
/// or drag another humanoid clip onto a slot to try it.
/// Variety (2026-11-11, the user: "there are so few animations"): most slots also have a list of alternatives. Each
/// zombie picks one per slot from the slot's clip and its list, from its network id, so a crowd no longer walks, idles
/// and swings as one, and every PC picks alike. The lists hold the Nephilite pack's clips and fill themselves from the
/// folders under Art/Animations/Zombies (ZombieClipFolders: drop a clip into Walk/ and it joins walks). Calm zombies
/// may also sit, eat or crouch (calmPoses) and get up as they notice someone.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Zombie Looks", fileName = "ZombieLooks")]
public sealed class ZombieLooks : ScriptableObject
{
    public const string ResourcePath = "Enemies/ZombieLooks";

    [Tooltip("One is picked per zombie, the same one on every PC (from its network id).")]
    public GameObject[] characters = new GameObject[0];

    [Header("Clips (humanoid)")]
    [Tooltip("Standing still. Loops.")]
    public AnimationClip idle;
    [Tooltip("Slow walk. Loops. Its own root speed sets the walking pace the feet keep.")]
    public AnimationClip walk;
    [Tooltip("Run. Loops. Its own root speed sets the running pace the feet keep.")]
    public AnimationClip run;
    [Tooltip("The swing. Its AnimationEvent_AttackHit event marks the strike, which lands on the windup's hit.")]
    public AnimationClip attack;
    [Tooltip("A short flinch, played when an attack is interrupted (a headshot, enough damage, a parry).")]
    public AnimationClip flinch;
    [Tooltip("Reeling while the posture is broken (the execution window). Held on its last frame if shorter.")]
    public AnimationClip stagger;
    [Header("Moods (ZombieMind)")]
    [Tooltip("Standing calm, unaware of anyone. Loops.")]
    public AnimationClip calmIdle;
    [Tooltip("Standing alert: stopped to look, or looking round where it searched. Loops.")]
    public AnimationClip alertIdle;
    [Tooltip("Walking over to search. Loops. Its own root speed sets the pace its feet keep.")]
    public AnimationClip searchWalk;
    [Tooltip("Once, as a calm zombie notices something.")]
    public AnimationClip notice;
    [Tooltip("Once, as a zombie woken from calm rouses before it charges (ZombieSenses rouseSeconds).")]
    public AnimationClip rouse;

    [Tooltip("A runner's bite at the neck, fatal if it lands (stage 4D-2a). Its AnimationEvent_AttackHit marks the bite.")]
    public AnimationClip bite;
    [Tooltip("A shambler reaching to grab (G&B-1). Its strike is where a hand reaches furthest forward, measured once.")]
    public AnimationClip grab;
    [Tooltip("A shambler holding on to a player. Played round and round while it holds.")]
    public AnimationClip grabHold;
    [Tooltip("Kneeling down to eat a player lying on the ground: the windup of that attack.")]
    public AnimationClip feedStart;
    [Tooltip("Kneeling over a player lying on the ground, eating them. Played round and round while it holds them.")]
    public AnimationClip feed;

    [Header("Runner (G&B-2: ZombieRunner, ZombieBody.Leap.cs)")]
    [Tooltip("A runner in the air, leaping. Loops; the body rises and falls over the leap by itself.")]
    public AnimationClip leap;
    [Tooltip("A runner landing from a leap that caught nobody. Once.")]
    public AnimationClip leapLand;

    [Header("Lost arms (ZombieBody.Arms.cs)")]
    [Tooltip("Swings to choose from when an arm is gone: the one striking with the arm left (measured once each) replaces attack.")]
    public AnimationClip[] oneArmAttacks = new AnimationClip[0];
    [Tooltip("With both arms gone it bites (or butts) instead. Its AnimationEvent_AttackHit marks the strike, else its reach.")]
    public AnimationClip noArmsAttack;

    [Header("Crawling (lost a leg: ZombieBody.Crawl.cs)")]
    [Tooltip("Lying face down, pushing up onto its arms to crawl: it plays as its getting up after the fall. In place.")]
    public AnimationClip toCrawl;
    [Tooltip("Crawling, still. Loops.")]
    public AnimationClip crawlIdle;
    [Tooltip("Crawling forward. Loops. Its own root speed sets the pace its arms keep.")]
    public AnimationClip crawl;
    [Tooltip("Crawling, a swing at the legs. Its strike lands on the windup's hit (a third of the way in, without an AttackHit event).")]
    public AnimationClip crawlAttack;
    [Tooltip("Crawling, a flinch.")]
    public AnimationClip crawlFlinch;

    [Header("Knockdown")]
    [Tooltip("Getting up after a knockdown, lying face down. In place: the body must not travel.")]
    public AnimationClip riseFaceDown;
    [Tooltip("Getting up after a knockdown, lying face up. In place: the body must not travel.")]
    public AnimationClip riseFaceUp;

    // Each list adds to its slot: a zombie picks one of the slot's clip and the list's (ZombieBody, Pick). The folders
    // under Art/Animations/Zombies with the same names fill them (ZombieClipFolders); remove an entry to stop it.
    [Header("Variety: one per zombie from the slot's clip and these (Art/Animations/Zombies/<folder> adds)")]
    [Tooltip("Folder Idle. Standing still. Loops.")]
    public AnimationClip[] idles = new AnimationClip[0];
    [Tooltip("Folder Walk. Slow walks. Loops; each one's own root speed sets the pace its feet keep.")]
    public AnimationClip[] walks = new AnimationClip[0];
    [Tooltip("Folder Run. Runs. Loops; each one's own root speed sets the pace its feet keep.")]
    public AnimationClip[] runs = new AnimationClip[0];
    [Tooltip("Folder Attack. Swings. The strike is an AnimationEvent_AttackHit, else where a hand reaches furthest forward.")]
    public AnimationClip[] attacks = new AnimationClip[0];
    [Tooltip("Folder Flinch. Short flinches.")]
    public AnimationClip[] flinches = new AnimationClip[0];
    [Tooltip("Folder Stagger. Reels while the posture is broken.")]
    public AnimationClip[] staggers = new AnimationClip[0];
    [Tooltip("Folder CalmIdle. Standing calm. Loops.")]
    public AnimationClip[] calmIdles = new AnimationClip[0];
    [Tooltip("Folder AlertIdle. Standing alert. Loops.")]
    public AnimationClip[] alertIdles = new AnimationClip[0];
    [Tooltip("Folder SearchWalk. Walking over to search. Loops.")]
    public AnimationClip[] searchWalks = new AnimationClip[0];
    [Tooltip("Folder Notice. Once, as a calm zombie notices something.")]
    public AnimationClip[] notices = new AnimationClip[0];
    [Tooltip("Folder Rouse. Once, as a zombie rouses before it charges.")]
    public AnimationClip[] rouses = new AnimationClip[0];
    [Tooltip("Folder CrawlAttack. Crawling swings at the legs.")]
    public AnimationClip[] crawlAttacks = new AnimationClip[0];
    [Tooltip("Folder CalmPose (X.fbx with X_getup.fbx). Some calm zombies hold one of these instead of standing, the share " +
             "in Tables/ZombieClips poseShare, and get up with its getUp as they notice someone or wake.")]
    public CalmPose[] calmPoses = new CalmPose[0];

    /// <summary>A pose a calm zombie lies low in, and the way it gets up from it.</summary>
    [Serializable]
    public struct CalmPose
    {
        [Tooltip("Held while calm, round and round: sitting, eating, crouched. In place.")]
        public AnimationClip loop;
        [Tooltip("Getting up from it, once, ending on its feet. Empty: the zombie's notice clip.")]
        public AnimationClip getUp;
    }

    private static ZombieLooks loaded;

    /// <summary>The project's looks, or null when the asset is missing (enemies then stay capsules).</summary>
    public static ZombieLooks Instance => loaded != null ? loaded : loaded = Resources.Load<ZombieLooks>(ResourcePath);

    /// <summary>
    /// One of <paramref name="main"/> and <paramref name="more"/>, each as likely, the same for the same seed and slot
    /// (ZombieBody seeds it with the network id, so every PC picks alike). Empty entries don't count; null only when
    /// there is nothing at all.
    /// </summary>
    public static AnimationClip Pick(AnimationClip main, AnimationClip[] more, uint seed, uint slot)
    {
        int count = main != null ? 1 : 0;
        if (more != null)
            foreach (var clip in more)
                if (clip != null) count++;
        if (count == 0) return null;
        int index = (int)(Mix(seed, slot) % (uint)count);
        if (main != null && index-- == 0) return main;
        foreach (var clip in more)
            if (clip != null && index-- == 0) return clip;
        return main;
    }

    /// <summary>Whether this zombie lies low while calm (<paramref name="share"/> of them do), and in which pose.</summary>
    public static bool PickPose(CalmPose[] poses, uint seed, uint slot, float share, out CalmPose pose)
    {
        pose = default;
        if (poses == null || share <= 0f || Fraction(seed, slot) >= share) return false;
        int count = 0;
        foreach (var each in poses)
            if (each.loop != null) count++;
        if (count == 0) return false;
        int index = (int)(Mix(seed, slot + 1u) % (uint)count);
        foreach (var each in poses)
            if (each.loop != null && index-- == 0) { pose = each; return true; }
        return false;
    }

    /// <summary>A number from 0 to 1 for this seed and slot: where in its loops a zombie starts, so a crowd isn't in step.</summary>
    public static float Fraction(uint seed, uint slot) => (Mix(seed, slot) & 0xFFFFFF) / 16777216f;

    // A small integer hash (lowbias32): neighbouring network ids land far apart.
    private static uint Mix(uint seed, uint slot)
    {
        unchecked
        {
            uint hash = seed ^ (slot * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
