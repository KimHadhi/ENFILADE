using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guns' shots in a bought pack's looks, on every PC (Realistic Gunslinger): the Desperado's own (the user, 2026-11-13: "총알
/// 이펙트도 데스페라도용으로 멋진 거 쓸 만한데?"), and every other gun's by its class (the user, 2026-11-12, as the manager
/// put it: snipers and DMRs in Piercing Shot, pistols and SMGs in Light, shotguns in Shotgun). A gun reads a row of
/// Tables/PackFx.csv (RowOf): its own, named after the gun, else its class's. Its class is its own `class` row, else what
/// its Weapons.csv numbers say (ClassOf): more than one pellet a Shotgun, a single-shot gun reaching marksmanRange a
/// Marksman, one whose penetration is no more than lightPenetration a Light, anything else a Rifle, which has no row and
/// keeps the gun's own flame and tracer. So a gun added to Weapons.csv takes its class's look by itself. At each shot:
/// - its muzzle use at the muzzle, once a shot: the flash, rings and sparks stand in for the gun's own flame, which
///   MuzzleFlash then leaves out (Covers), so not with a suppressor or the muzzle effects off, as the flame isn't;
/// - its shot use at the muzzle, once a shot, along the aim: a shotgun's spray of pellets;
/// - its round use, a glowing round with a wake, flown from the muzzle to where the shot struck at the row's speed, in
///   place of MuzzleEffects' tracer. From this PC's own muzzle it starts a step out (selfAhead), so it never glares in the eye;
/// - its hit use where it lands on a foe, once a foe a shot however many pellets struck it, and its miss use (smaller)
///   where it lands on anything else; the same again where the bullet went on through (a pierced body, a wall) or off.
/// It rides WeaponController.ShotFired, which a friend's shot raises on this PC too (ShowShot), so the same look plays
/// everywhere. Uses with a `pool` keep their copies to play again (PackFx), as a fast gun's every shot would otherwise make
/// and destroy them.
/// </summary>
public sealed class PackShots : MonoBehaviour
{
    public const string Shotgun = "Shotgun", Marksman = "Marksman", Light = "Light", Rifle = "Rifle";
    private const string ClassRow = "class";
    private const int MostRounds = 32;
    private static PackShots instance;
    // Each gun's row (its own or its class's), worked out once a table load.
    private static readonly Dictionary<WeaponData, string> rows = new Dictionary<WeaponData, string>();

    private struct Landing
    {
        public string use;
        public Vector3 at, way;
    }

    private sealed class Round
    {
        public GameObject look;
        public int play;
        public Vector3 from, way, end;
        public float distance, flown, speed;
        public string landing;
        public Landing[] later;
    }

    private readonly Dictionary<WeaponController, Action<WeaponShot>> heard = new Dictionary<WeaponController, Action<WeaponShot>>();
    private readonly List<WeaponController> gone = new List<WeaponController>();
    private readonly List<Round> rounds = new List<Round>();
    // The foes a gun's hit look went off on this frame: a shotgun's pellets in one foe throw it once.
    private readonly HashSet<(WeaponController, Component)> landedOn = new HashSet<(WeaponController, Component)>();
    private readonly List<Landing> later = new List<Landing>();
    private int landedFrame = -1;
    private float nextLook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        rows.Clear();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => rows.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Pack Shots");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<PackShots>();
    }

    /// <summary>Whether <paramref name="gun"/>'s shots wear a pack's look (RowOf): its own flame and tracer then step aside.</summary>
    public static bool Covers(WeaponData gun) => RowOf(gun) != null;

    /// <summary>
    /// The Tables/PackFx.csv row <paramref name="gun"/>'s shots read: its own when it has a round or a shot there, else its
    /// class's (ClassOf); null when neither has, or its own `on` is 0.
    /// </summary>
    public static string RowOf(WeaponData gun)
    {
        if (gun == null) return null;
        if (rows.TryGetValue(gun, out string known)) return known;
        string row = null;
        if (PackFx.Number(gun.name, "on", 1f) >= .5f)
        {
            if (Looks(gun.name)) row = gun.name;
            else
            {
                string kind = ClassOf(gun);
                if (PackFx.Number(kind, "on", 1f) >= .5f && Looks(kind)) row = kind;
            }
        }
        rows[gun] = row;
        return row;
    }

    /// <summary>
    /// The class a gun's look goes by: its own `class` row in Tables/PackFx.csv, else from its Weapons.csv numbers
    /// (Shotgun, Marksman, Light or Rifle; the thresholds are the `class` rows).
    /// </summary>
    public static string ClassOf(WeaponData gun)
    {
        string own = PackFx.Text(gun.name, ClassRow);
        if (!string.IsNullOrEmpty(own)) return own;
        if (gun.Pellets > 1) return Shotgun;
        if (gun.FireMode == WeaponFireMode.SemiAutomatic && gun.Range >= PackFx.Number(ClassRow, "marksmanRange", 120f)) return Marksman;
        if (gun.Penetration <= PackFx.Number(ClassRow, "lightPenetration", 14f)) return Light;
        return Rifle;
    }

    private static bool Looks(string row) => !string.IsNullOrEmpty(PackFx.Text(row, "round")) || !string.IsNullOrEmpty(PackFx.Text(row, "shot"));

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
            if (Own(round)) PackFx.Finish(round.look);
        rounds.Clear();
    }

    private void Shot(WeaponController controller, WeaponShot shot)
    {
        var gun = controller != null ? controller.EquippedData : null;
        string row = RowOf(gun);
        if (row == null) return;
        bool self = controller.GetComponentInParent<PlayerMovement>() == PlayerRoster.Local;
        // Where it struck: the first thing it met, else as far as it reaches.
        bool struck = shot.Trace.HasHit && shot.Trace.Hit.collider != null;
        Vector3 end = struck ? shot.Trace.Hit.point : shot.Trace.End;
        Vector3 path = end - shot.Origin;
        float distance = path.magnitude;
        Vector3 way = distance > .01f ? path / distance : shot.Direction.normalized;
        if (way.sqrMagnitude < .5f) return;
        var facing = Quaternion.LookRotation(way);
        if (shot.Pellet == 0)
        {
            // The flash, rings and sparks, once a shot however many pellets, in place of the gun's own flame: so not where
            // that would be left out (MuzzleFlash), a suppressor or the muzzle effects off or a gun with no flame.
            if (!controller.Suppressed && GunFeelSettings.Instance.muzzleEffects && gun.MuzzleFlashScale > 0f)
                PackFx.Play(PackFx.Text(row, "muzzle"), shot.Origin, facing, self);
            // A shotgun's spray, once a shot, along where it was aimed.
            Vector3 aim = shot.Direction.sqrMagnitude > 1e-6f ? shot.Direction.normalized : way;
            PackFx.Play(PackFx.Text(row, "shot"), shot.Origin, Quaternion.LookRotation(aim), self);
        }
        string landing = struck ? LandingOf(row, controller, shot.Trace.Hit.collider) : "";
        var onward = Onward(row, controller, shot);
        // From this PC's own muzzle the round starts a step out, never glaring at the eye.
        float ahead = self ? Mathf.Min(PackFx.Number(row, "selfAhead", 2.5f), distance * .5f) : 0f;
        Vector3 from = shot.Origin + way * ahead;
        var look = distance - ahead > .2f ? PackFx.Play(PackFx.Text(row, "round"), from, facing, self) : null;
        if (look == null)
        {
            Land(landing, end, way);
            Land(onward);
            return;
        }
        if (rounds.Count >= MostRounds) Arrive(0);
        rounds.Add(new Round
        {
            look = look, play = PackFx.PlayOf(look), from = from, way = way, end = end, distance = distance - ahead,
            speed = Mathf.Max(20f, PackFx.Number(row, "speed", 220f)), landing = landing, later = onward,
        });
    }

    // Its hit look on a foe, once a foe this frame for this gun; its miss look on anything else.
    private string LandingOf(string row, WeaponController controller, Collider struck)
    {
        if (struck == null) return "";
        var foe = struck.GetComponentInParent<ICombatFoe>() as Component;
        if (foe == null) return PackFx.Text(row, "miss");
        if (landedFrame != Time.frameCount)
        {
            landedFrame = Time.frameCount;
            landedOn.Clear();
        }
        return landedOn.Add((controller, foe)) ? PackFx.Text(row, "hit") : "";
    }

    // Where the bullet went on through (a pierced body, a wall) or off, and what it met there: their looks go off with
    // the first's.
    private Landing[] Onward(string row, WeaponController controller, WeaponShot shot)
    {
        if (shot.Later == null || shot.Later.Length == 0) return null;
        later.Clear();
        foreach (var leg in shot.Later)
        {
            if (!leg.Trace.HasHit || leg.Trace.Hit.collider == null) continue;
            string use = LandingOf(row, controller, leg.Trace.Hit.collider);
            if (!string.IsNullOrEmpty(use)) later.Add(new Landing { use = use, at = leg.Trace.Hit.point, way = leg.Onward.Direction });
        }
        return later.Count > 0 ? later.ToArray() : null;
    }

    private void Fly(float dt)
    {
        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            var round = rounds[i];
            // Its copy ended (its life ran out) or was played again for another shot: let it be.
            if (!Own(round))
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

    private static bool Own(Round round) => round.look != null && round.play != 0 && PackFx.PlayOf(round.look) == round.play;

    // The round is there: it goes, and its landings go off where it struck and where it went on.
    private void Arrive(int index)
    {
        var round = rounds[index];
        rounds.RemoveAt(index);
        if (Own(round)) PackFx.Finish(round.look);
        Land(round.landing, round.end, round.way);
        Land(round.later);
    }

    private static void Land(string use, Vector3 at, Vector3 way)
    {
        if (!string.IsNullOrEmpty(use)) PackFx.Play(use, at, Quaternion.LookRotation(-way));
    }

    private static void Land(Landing[] landings)
    {
        if (landings == null) return;
        foreach (var landing in landings) Land(landing.use, landing.at, landing.way);
    }
}
