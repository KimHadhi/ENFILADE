using UnityEngine;

/// <summary>
/// What a zombie can look like and the clips it moves with (multiplayer plan stage 4A): the bought Synty City Zombies
/// characters, and the humanoid clips of Advanced Zombie AI Pro's demo set (only the clips; its AI scripts stay unused,
/// ZombieBody plays them). Resources/Enemies/ZombieLooks.asset. Take a character out of the list to stop it spawning,
/// or drag another humanoid clip onto a slot to try it.
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

    private static ZombieLooks loaded;

    /// <summary>The project's looks, or null when the asset is missing (enemies then stay capsules).</summary>
    public static ZombieLooks Instance => loaded != null ? loaded : loaded = Resources.Load<ZombieLooks>(ResourcePath);
}
