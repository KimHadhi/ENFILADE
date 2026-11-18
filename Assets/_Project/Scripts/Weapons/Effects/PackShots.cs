using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A skill gun's shots in a bought pack's looks, on every PC (Realistic Gunslinger): a Schießhund's Desperado, a class's
/// own gun loaded only by its bounties (the user, 2026-11-13: "총알 이펙트도 데스페라도용으로 멋진 거 쓸 만한데?"). The pack
/// is for skills alone (the user, 2026-11-18: "유니티 에셋 스토어에서 구매한 건슬링어 이펙트 에셋은 스킬 전용으로만 사용.
/// 일반 총기 사격 시 건슬링어 이펙트가 출력되거나 총구 위치와 어긋나는 문제 수정"): from 0.8.286 every other gun wore
/// it too, by a class guessed from its Weapons.csv numbers, and 21 of the 28 guns fired the pack's muzzle, round and hit
/// in place of their own flame and tracer. That guess and its rows are gone: an ordinary gun fires with its own flame
/// (MuzzleFlash, by its row of Tables/MuzzleFlash) and tracer (MuzzleEffects). A gun wears the pack only by a row of
/// Tables/PackFx.csv named after itself (RowOf). At each of its shots:
/// - its muzzle use at the muzzle, once a shot, riding the muzzle while it lasts: rings and sparks on top of the gun's
///   own flame (or in place of it when the row's `flame` is 0), so not with a suppressor or the muzzle effects off;
/// - its shot use at the muzzle, once a shot, along the aim: a spray;
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
        RuntimeSceneHierarchy.Keep(host);
        instance = host.AddComponent<PackShots>();
    }

    /// <summary>
    /// Whether <paramref name="gun"/>'s shots wear the pack's look (RowOf): the pack's round then flies in place of the
    /// gun's own tracer (MuzzleEffects), which steps aside.
    /// </summary>
    public static bool Covers(WeaponData gun) => RowOf(gun) != null;

    /// <summary>
    /// Whether <paramref name="gun"/>'s own flame still shows at its muzzle (MuzzleFlash): always for a gun the pack doesn't
    /// cover, and for a covered one unless its row's `flame` cell is 0 (the pack's muzzle burst then stands in for it).
    /// </summary>
    public static bool OwnFlame(WeaponData gun)
    {
        string row = RowOf(gun);
        return row == null || PackFx.Number(row, "flame", 1f) >= .5f;
    }

    /// <summary>
    /// The Tables/PackFx.csv row <paramref name="gun"/>'s shots read: the one named after the gun, when it has a round or
    /// a shot there and its `on` isn't 0; null for every other gun. Only a gun that is itself a skill has one (a
    /// Schießhund's Desperado): ordinary guns fire with their own flame and tracer.
    /// </summary>
    public static string RowOf(WeaponData gun)
    {
        if (gun == null) return null;
        if (rows.TryGetValue(gun, out string known)) return known;
        string row = PackFx.Number(gun.name, "on", 1f) >= .5f && Looks(gun.name) ? gun.name : null;
        rows[gun] = row;
        return row;
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
            // What goes off at the muzzle rides the muzzle while it lasts, as the gun's own flame does: left at the point
            // in the world the shot was fired from, a burst of a second stood behind a gun that had moved on.
            Transform muzzle = controller.MuzzlePoint != null && controller.MuzzlePoint.gameObject.activeInHierarchy ? controller.MuzzlePoint : null;
            // The rings and sparks, once a shot however many pellets: not with a suppressor on or the muzzle effects off.
            if (!controller.Suppressed && GunFeelSettings.Instance.muzzleEffects)
                PackFx.Play(PackFx.Text(row, "muzzle"), shot.Origin, facing, self, muzzle);
            // A spray, once a shot, along where it was aimed.
            Vector3 aim = shot.Direction.sqrMagnitude > 1e-6f ? shot.Direction.normalized : way;
            PackFx.Play(PackFx.Text(row, "shot"), shot.Origin, Quaternion.LookRotation(aim), self, muzzle);
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
