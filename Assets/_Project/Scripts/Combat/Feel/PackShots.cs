using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gun's shots in a bought pack's looks, on every PC: the Desperado's, in Realistic Gunslinger's (the user,
/// 2026-11-13: "총알 이펙트도 데스페라도용으로 멋진 거 쓸 만한데?"). A gun with a row of its own in Tables/PackFx.csv (the
/// row's id is the gun's id) throws, at each shot:
/// - its muzzle use at the muzzle: rings and sparks, the gun's own flame left to MuzzleFlash;
/// - its round use, a glowing round with a wake, flown from the muzzle to where the shot struck at the row's speed. From
///   this PC's own muzzle it starts a step out (selfAhead), so it never glares in the eye;
/// - its hit use where it lands on a foe, its miss use (smaller) where it lands on anything else.
/// It rides WeaponController.ShotFired, which a friend's shot raises on this PC too (ShowShot), so the same look plays
/// everywhere. The gun's own tracer is MuzzleEffects' (guns'); Covers(gun) says which guns the pack's look has taken
/// over, should it step aside for them.
/// </summary>
public sealed class PackShots : MonoBehaviour
{
    private const int MostRounds = 16;
    private static PackShots instance;

    private sealed class Round
    {
        public GameObject look;
        public Vector3 from, way, end;
        public float distance, flown, speed;
        public string landing;
    }

    private readonly Dictionary<WeaponController, Action<WeaponShot>> heard = new Dictionary<WeaponController, Action<WeaponShot>>();
    private readonly List<WeaponController> gone = new List<WeaponController>();
    private readonly List<Round> rounds = new List<Round>();
    private float nextLook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Pack Shots");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<PackShots>();
    }

    /// <summary>Whether <paramref name="gun"/>'s shots wear a pack's look: a row of its own in Tables/PackFx.csv with a round.</summary>
    public static bool Covers(WeaponData gun)
        => gun != null && PackFx.Number(gun.name, "on", 1f) > .5f && !string.IsNullOrEmpty(PackFx.Text(gun.name, "round"));

    private void Update()
    {
        if (Time.unscaledTime >= nextLook)
        {
            nextLook = Time.unscaledTime + .5f;
            Listen();
        }
        Fly(Time.deltaTime);
    }

    // Every player's gun is listened to, a friend's copy too; a gun gone with its player is let go.
    private void Listen()
    {
        gone.Clear();
        foreach (var pair in heard)
            if (pair.Key == null) gone.Add(pair.Key);
        foreach (var dead in gone) heard.Remove(dead);
        foreach (var player in PlayerRoster.All)
        {
            if (player == null) continue;
            var controller = player.GetComponentInChildren<WeaponController>(true);
            if (controller == null || heard.ContainsKey(controller)) continue;
            var listening = controller;
            Action<WeaponShot> handler = shot => Shot(listening, shot);
            heard[controller] = handler;
            controller.ShotFired += handler;
        }
    }

    private void OnDestroy()
    {
        foreach (var pair in heard)
            if (pair.Key != null) pair.Key.ShotFired -= pair.Value;
        heard.Clear();
        foreach (var round in rounds)
            if (round.look != null) Destroy(round.look);
        rounds.Clear();
    }

    private void Shot(WeaponController controller, WeaponShot shot)
    {
        var gun = controller != null ? controller.EquippedData : null;
        if (!Covers(gun)) return;
        string row = gun.name;
        bool self = controller.GetComponentInParent<PlayerMovement>() == PlayerRoster.Local;
        // Where it struck: the first thing it met, else as far as it reaches.
        bool struck = shot.Trace.HasHit && shot.Trace.Hit.collider != null;
        Vector3 end = struck ? shot.Trace.Hit.point : shot.Trace.End;
        Vector3 path = end - shot.Origin;
        float distance = path.magnitude;
        Vector3 way = distance > .01f ? path / distance : shot.Direction.normalized;
        if (way.sqrMagnitude < .5f) return;
        var facing = Quaternion.LookRotation(way);
        // The muzzle's rings and sparks, once a shot however many pellets.
        if (shot.Pellet == 0) PackFx.Play(PackFx.Text(row, "muzzle"), shot.Origin, facing, self);
        bool foe = struck && shot.Trace.Hit.collider.GetComponentInParent<ICombatFoe>() != null;
        string landing = struck ? PackFx.Text(row, foe ? "hit" : "miss") : "";
        // From this PC's own muzzle the round starts a step out, never glaring at the eye.
        float ahead = self ? Mathf.Min(PackFx.Number(row, "selfAhead", 2.5f), distance * .5f) : 0f;
        Vector3 from = shot.Origin + way * ahead;
        var look = distance - ahead > .2f ? PackFx.Play(PackFx.Text(row, "round"), from, facing, self) : null;
        if (look == null)
        {
            Land(landing, end, way);
            return;
        }
        if (rounds.Count >= MostRounds) Arrive(0);
        rounds.Add(new Round
        {
            look = look, from = from, way = way, end = end, distance = distance - ahead,
            speed = Mathf.Max(20f, PackFx.Number(row, "speed", 220f)), landing = landing,
        });
    }

    private void Fly(float dt)
    {
        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            var round = rounds[i];
            if (round.look == null)
            {
                rounds.RemoveAt(i);
                continue;
            }
            round.flown += round.speed * dt;
            if (round.flown >= round.distance)
            {
                Arrive(i);
                continue;
            }
            round.look.transform.position = round.from + round.way * round.flown;
        }
    }

    // The round is there: it goes, and its landing goes off where it struck.
    private void Arrive(int index)
    {
        var round = rounds[index];
        rounds.RemoveAt(index);
        if (round.look != null) Destroy(round.look);
        Land(round.landing, round.end, round.way);
    }

    private static void Land(string use, Vector3 at, Vector3 way)
    {
        if (!string.IsNullOrEmpty(use)) PackFx.Play(use, at, Quaternion.LookRotation(-way));
    }
}
