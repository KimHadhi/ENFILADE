using System;
using UnityEngine;

/// <summary>
/// F on an enemy (stage 3 of the classes; the user's rule, 2026-11-11: "F가 상호작용이든 … 처형이나 암살로 이어질 거면
/// 거기서 바로 넘어가는데 그런 거 아니면 걍 적 찍게"). When F has nothing else to do here (no execution or takedown, no
/// pickup, no friend to get up, no ladder top, no button, door, can or shop counter: whatever offers itself to the
/// interact key's one owner, InteractKey, where the ping is the last of all), it pings the enemy the crosshair is
/// on, Back 4 Blood's way: the whole team sees it outlined, through walls too, with a diamond and its distance over it
/// (PingMarks). A Schießhund's ping is its teal bounty mark, which its class hunts (ClassKit), unless the foe is a lesser
/// quarry than the one it already hunts: that one is a plain ping (PlayerClass.Grade). The free hand points at it for a
/// moment as it goes (PlayerPoint.Flash, the held point's drawn hand), a quick tap of the left hand alone when aimed down
/// the sights; with the hand busy (a reload, a draw, a throw) the mark and its sound say it alone. The hand stays on the
/// foe as the view turns, until it is further round than the shoulder carries the arm: then the point ends early and the
/// hand drops back (PointArm, PlayerPoint.TurnAway). The other PCs hear of it from PlayerNetworkCombat, with the spot the
/// hand points at, so their copy of this player points there too. This PC's own player only. Numbers:
/// Tables/ClassAbilities, Ping.*.
/// Any foe can be pinged (ICombatFoe): a zombie, or a target on the hub's range, where a Schießhund tries its mark out
/// (the user, 2026-11-12: "시순트 표적 찍기도 못 하네"). A Ghost with its 정찰 card pings a zombie it sees through a wall
/// too, where the look met the wall first (GhostSight.AimedThroughWall).
/// Held, F points (GTA V's way; the user, 2026-11-12: "F 꾹 누르고 있으면 GTA5에서 무슨 키 누르고 있으면 계속
/// 손가락질할 수 있잖아?"): a press that found F with nothing else to do, still held after Poke.csv Point.holdAfter, has the
/// free hand point where the player looks for as long as it is held (PlayerPoint, PointArm), and a fingertip on a friend
/// pokes them (SkinPoke). The tap's ping comes first as always. Anything F would do keeps it: an interaction coming
/// under the crosshair ends the point, as do aiming, a reload, a draw, a shot or a punch, until F is pressed again.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPing : MonoBehaviour
{
    private const string Row = "Ping";
    /// <summary>
    /// The ping, and the point when held: what F does with nothing else to do, as the interact key's one owner knows it
    /// (InteractKey). It is the last row of Tables/Player/InteractKey.csv: everything else F does comes before it.
    /// </summary>
    public const string OfferId = "ping";
    // How fat the look's cast is (m): a ping needn't be dead on, as a shot must.
    private const float Radius = .3f;
    // The most the cast's buffer grows to: far more colliders than any look crosses, only so a broken scene cannot grow it
    // without end.
    private const int MostHits = 4096;

    /// <summary>
    /// This PC's player pinged this foe (a zombie or a range target); true for a Schießhund's bounty mark; the spot on it
    /// (world) the hand points at. PlayerNetworkCombat tells the others, the spot too, so their copy of this player points
    /// at it as this PC's player does.
    /// </summary>
    public event Action<Component, bool, Vector3> Pinged;

    private PlayerMovement movement;
    private WeaponController weapon;
    private PlayerMeleeCombat melee;
    private PlayerPoint point;
    private float nextPing, pressedAt;
    // The press held now was this class's own (F was free: the key's owner gave it here) and the key is still its own;
    // and nothing has ended its point since.
    private bool pressFree, pointEnded;
    // What the look's cast met. It grows when a cast fills it (Met).
    private RaycastHit[] hits = new RaycastHit[32];

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        weapon = GetComponent<WeaponController>();
        point = GetComponent<PlayerPoint>();
        if (point == null) point = gameObject.AddComponent<PlayerPoint>();
    }

    // A shot or a punch takes the hand back from a point (the other components are all on by now; the fists may come
    // later, Update listens to them then).
    private void Start()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ShotFired += OnShot;
    }

    private void OnDestroy()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (melee != null) melee.Punched -= OnPunched;
    }

    private void OnShot(WeaponShot shot) => EndPoint();
    private void OnPunched(float charge) => EndPoint();

    // A held point ends until F is pressed again, and a ping's moment ends at once.
    private void EndPoint()
    {
        if (point == null) return;
        if (point.Held || point.Pointing) pointEnded = true;
        point.StopFlash();
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal || !movement.InputAvailable || movement.IsDead || movement.IsDowned)
        {
            if (point != null) point.Hold(false);
            return;
        }
        if (melee == null && movement.MeleeCombat != null)
        {
            melee = movement.MeleeCombat;
            melee.Punched += OnPunched;
        }
        // F with nothing else to do: the last of everything F does (InteractKey, row ping), offered whenever the hands are
        // this player's own (not in a takedown, a counter or a treatment: PlayerMovement.CombatInputLocked). Whatever else
        // would do something with F stands before it in the key's order, so a press reaches here only when F was free.
        // This class kept a list of those others until 2026-11-18 (Busy(): eleven of them by name, each asked for
        // itself, and whoever was not on it shared the press with the ping).
        if (!movement.CombatInputLocked) InteractKey.Offer(OfferId, held: true);
        bool pressed = InteractKey.Pressed(OfferId);
        if (pressed)
        {
            pressedAt = Time.time;
            pressFree = true;
            pointEnded = false;
        }
        // The key let go, or something F does came on screen and has the key now: the point ends until F is pressed again.
        else if (pressFree && !InteractKey.Held(OfferId)) pressFree = false;
        Hold();
        if (!pressed || Time.time < nextPing) return;
        var foe = Aimed(out Vector3 pinged);
        // A Ghost's 정찰 card pings a zombie it sees through a wall too, the wall in front having taken the look (GhostSight).
        if (foe == null) foe = GhostSight.AimedThroughWall(movement, out pinged);
        if (foe == null) return;
        nextPing = Time.time + Mathf.Max(0f, PlayerClass.Ability(Row, "gap", .4f));
        bool bounty = PlayerClass.Is(PlayerClass.Schiesshund);
        // A Schießhund's mark moves only onto a quarry as big or bigger than the one it hunts: F on a lesser foe meanwhile
        // is a plain ping for the team and the hunt goes on (the user, 2026-11-12: "엘리트를 조준했는데 일반 좀비를 조준한다면
        // 자기보다 급 낮은 애로 다시 조준하는 건 표식이 옮겨가진 않고 그냥 하이라이트 하는 걸로 바꾸자"). Before, every F
        // moved it, so calling out a runner for the team cost the elite being hunted.
        if (bounty)
        {
            var hunted = PingMarks.BountyOf(movement);
            if (hunted != null && hunted != foe && PlayerClass.Grade(foe) < PlayerClass.Grade(hunted)) bounty = false;
            // In a duel the mark stays on the foe called out (HoundDuel): F elsewhere is the team's plain ping.
            if (HoundDuel.Of(movement) is HoundDuel duel && duel.Foe != null && duel.Foe.gameObject != foe.gameObject) bounty = false;
        }
        PingMarks.Ping(foe, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        Gesture(foe, pinged);
        Pinged?.Invoke(foe, bounty, pinged);
    }

    /// <summary>The living foe under the crosshair now, as F would find it, or null: a Schießhund's duel is called on it (ClassKit).</summary>
    public Component AimedFoe() => Aimed(out _);

    /// <summary>
    /// Marks this foe as F would, its sound and the hand's point (or the gun's nod) with it, the other PCs told: a
    /// Schießhund's duel puts its bounty mark on the foe it calls out (ClassKit), whatever the mark was on.
    /// </summary>
    public void Mark(Component foe, bool bounty)
    {
        if (foe == null || movement == null) return;
        PingMarks.Ping(foe, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        Vector3 middle = Middle(foe);
        Gesture(foe, middle);
        Pinged?.Invoke(foe, bounty, middle);
    }

    // The free hand points at what was pinged for a moment, the hand drawn for the held point (PlayerPoint.Flash; the
    // user, 2026-11-12: "적 핑 찍을 때 가리키는 애니메이션이 있었으면"), aimed down the sights too, as a quick tap of the
    // left hand while the gun stays on the aim. The gun's nod (FPSBody.Point) no longer stands in for a busy hand: it
    // threw the aim off (the user, 2026-11-12: "줌 한 상태로 핑 찍으면 그 총 잠깐 애니메이션 나오는 게 너무 조준을
    // 방해함 … 걍 왼손만 움직이게 해"), and through a reload or a throw the mark and its sound say enough.
    private void Gesture(Component foe, Vector3 at)
    {
        if (point != null) point.Flash(foe != null ? foe.transform : null, at);
    }

    // A foe's middle to point at: its first collider's centre, else a body's height over its feet.
    private static Vector3 Middle(Component foe)
    {
        var body = foe.GetComponentInChildren<Collider>();
        return body != null ? body.bounds.center : foe.transform.position + Vector3.up;
    }

    // Held past Point.holdAfter, a free press points for as long as F stays down and the hand is free (PlayerPoint.CanHold:
    // the hands' owner says); whatever F would do coming on screen, or the hand being wanted (aiming, a reload, a throw,
    // a syringe, a shot, a punch), ends the point until F is pressed again.
    private void Hold()
    {
        if (point == null) return;
        // pressFree: the press was this class's own and the held key still is (Update lets it go when the key is let go or
        // something else F does has taken it).
        bool wants = pressFree && !pointEnded && Time.time - pressedAt >= PokeTables.HoldAfter;
        if (wants && !point.CanHold)
        {
            if (point.Held || point.Pointing) pointEnded = true;
            wants = false;
        }
        point.Hold(wants);
    }

    // The living foe the crosshair is on (a zombie or a range target): the nearest thing along the look that isn't this
    // player, if it is one, and where on the look it was met. A wall or the floor in front of it takes the ping instead
    // (nothing happens).
    private Component Aimed(out Vector3 at)
    {
        at = default;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        int count = Met(eyes.position, eyes.forward, Reach());
        float nearest = float.PositiveInfinity;
        Collider first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            // A cast that starts inside something reports it at no distance: only a foe's body counts there.
            if (hit.distance <= 0f && CombatFoe.Of(hit.collider) == null) continue;
            if (hit.distance < nearest) { nearest = hit.distance; first = hit.collider; }
        }
        var foe = CombatFoe.Of(first);
        if (!CombatFoe.IsAlive(foe)) return null;
        at = nearest > 0f && !float.IsInfinity(nearest) ? eyes.position + eyes.forward * nearest : Middle(foe);
        return foe;
    }

    /// <summary>
    /// How far a ping reaches (m): as far as the look does. A ping marks what the player sees, so it has no range of its
    /// own: the limit is the far plane of the camera they look through (a scope's picture shares it), and a wall or the
    /// floor in the way takes the ping as before (the user, 2026-11-18: "벽에 막히지 않는 한 최대 핑 사거리를 확장"). It
    /// was a number of its own, 120 m: nearer than the 160 m the screen draws an enemy to (Tables/Maps/Culling Enemy),
    /// far nearer than what a magnified scope shows. ClassAbilities Ping.reach stays as a design cap for whoever wants
    /// one: 0 is none.
    /// </summary>
    public static float Reach()
    {
        var view = Camera.main;
        float far = view != null ? view.farClipPlane : float.PositiveInfinity;
        float cap = PlayerClass.Ability(Row, "reach", 0f);
        return cap > 0f ? Mathf.Min(cap, far) : far;
    }

    // Everything the look's cast meets within reach, into `hits`; returns how many. A cast that fills its buffer has
    // dropped the rest in no order, and the nearest thing (the wall that should take the ping, or the foe itself) may be
    // among the dropped: a long look across a crowd meets more than a short one did. So a full buffer is doubled and the
    // cast made again, until everything fits.
    private int Met(Vector3 from, Vector3 way, float reach)
    {
        int count = Physics.SphereCastNonAlloc(from, Radius, way, hits, reach, ~0, QueryTriggerInteraction.Ignore);
        while (count >= hits.Length && hits.Length < MostHits)
        {
            hits = new RaycastHit[hits.Length * 2];
            count = Physics.SphereCastNonAlloc(from, Radius, way, hits, reach, ~0, QueryTriggerInteraction.Ignore);
        }
        return count;
    }
}
