using System.Collections.Generic;
using UnityEngine;

// ClassCards: Assault: the barrage both its bombardment cards call (데인저 클로즈 on a mark, 브로큰 애로우 on its own spot). One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // One barrage under way: round where and how far, how many shells and how many still to come, when the next is due,
    // and whether it is an escape (브로큰 애로우: whatever holds this player is shelled before anything else).
    private sealed class Salvo
    {
        public Vector3 Centre;
        public float Radius, Next;
        public int Total, Left;
        public bool Escape;
    }

    private readonly List<Salvo> salvos = new List<Salvo>();
    // The order the enemies in a barrage's reach are shelled in (Volley): round which middle, and holders first or not.
    private Vector3 sortFrom;
    private bool sortEscape;
    private System.Comparison<EnemyHealth> closer;

    /// <summary>
    /// Calls a barrage on <paramref name="centre"/>: after <paramref name="delay"/> seconds its <paramref name="shells"/>
    /// come down one after another within its reach (Assault.barrageRadius), each on the next of the enemies there,
    /// nearest the middle first, and its blast hurts and throws down every enemy in it and never a player. Everyone sees
    /// where it will fall from the moment it is called (CardFx Barrage), then each shell as it comes (CardFx Shell); the
    /// hits are judged here and reach the host as hits do. An escape (<paramref name="escape"/>) shells whatever holds this
    /// player before anything else, to knock it off them.
    /// </summary>
    private void CallBarrage(Vector3 centre, float delay, int shells, bool escape)
    {
        float radius = Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "barrageRadius", 6f));
        delay = Mathf.Max(0f, delay);
        shells = Mathf.Max(1, shells);
        salvos.Add(new Salvo { Centre = centre, Radius = radius, Total = shells, Left = shells, Next = Time.time + delay, Escape = escape });
        Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
        CardNet.Show(CardFx.Kind.Barrage, centre, facing.normalized * radius, delay);
    }

    // Every barrage under way calls its next shell when it is due; every shell on its way down lands when it is due.
    private void Barrage()
    {
        float every = Mathf.Max(.02f, PlayerClass.Ability(AssaultRow, "barrageEvery", .22f));
        for (int i = salvos.Count - 1; i >= 0; i--)
        {
            var salvo = salvos[i];
            if (Time.time < salvo.Next) continue;
            salvo.Next = Time.time + every;
            Volley(salvo);
            if (--salvo.Left <= 0) salvos.RemoveAt(i);
        }
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            if (Time.time < falling[i].due) continue;
            Burst(falling[i].spot);
            falling.RemoveAt(i);
        }
    }

    // One shell called down: on the next of the enemies in its reach (in turn, nearest the middle first), or on open ground
    // in its reach when nobody is there. Where it will land shows at once on every PC (CardFx Shell), and it lands as its
    // streak comes down (Burst).
    private void Volley(Salvo salvo)
    {
        near.Clear();
        Vector3 middle = salvo.Centre;
        float reach = salvo.Radius;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 off = enemy.transform.position - middle;
            // Within its reach on this floor, not the one above or below; whatever holds this player wherever it is.
            if ((Mathf.Abs(off.y) > 3f || off.x * off.x + off.z * off.z > reach * reach) && !(salvo.Escape && Holds(enemy))) continue;
            near.Add(enemy);
        }
        sortFrom = middle;
        sortEscape = salvo.Escape;
        if (closer == null) closer = Closer;
        near.Sort(closer);
        int index = salvo.Total - salvo.Left;
        Vector3 at;
        if (near.Count > 0)
        {
            Vector2 jitter = Random.insideUnitCircle * .5f;
            at = near[index % near.Count].transform.position + new Vector3(jitter.x, 0f, jitter.y);
        }
        else
        {
            // Nobody there: anywhere in its reach; round an escape's caller, not on their head.
            Vector2 spot = salvo.Escape ? Random.insideUnitCircle.normalized * Random.Range(Mathf.Min(3f, reach * .5f), reach)
                : Random.insideUnitCircle * reach;
            at = middle + new Vector3(spot.x, 0f, spot.y);
        }
        // Down onto what is under it: the ground, not a zombie's head.
        if (AssaultStrikes.Ground(at + Vector3.up * 3f, 8f, out Vector3 ground)) at = ground;
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "barrageBlast", 3f));
        Vector2 lean = Random.insideUnitCircle;
        CardNet.Show(CardFx.Kind.Shell, at, new Vector3(lean.x, 0f, lean.y), blast);
        falling.Add((Time.time + CardFx.ShellFall, at));
    }

    // Whatever holds this player comes first in an escape, then the nearest the middle.
    private int Closer(EnemyHealth a, EnemyHealth b)
    {
        if (sortEscape)
        {
            bool first = Holds(a), second = Holds(b);
            if (first != second) return first ? -1 : 1;
        }
        return (a.transform.position - sortFrom).sqrMagnitude.CompareTo((b.transform.position - sortFrom).sqrMagnitude);
    }

    // It holds this player: a grab, a kneel to eat, a runner's pin or a hook (ZombieGrip, set on this player's own PC).
    private bool Holds(EnemyHealth enemy) => enemy.TryGetComponent(out ZombieGrip grip) && grip.Held == movement;

    // A shell landing: every enemy in its blast is hurt (less toward its edge) and thrown down, a brute only reeling; no
    // player is.
    private void Burst(Vector3 at)
    {
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "barrageBlast", 3f));
        float damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "barrageDamage", 150f));
        float down = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "barrageDown", 1.5f));
        float reel = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "barrageReel", .8f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= blast * blast) near.Add(enemy);
        foreach (var enemy in near)
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float share = 1f - .5f * Mathf.Clamp01(away.magnitude / blast);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Blast(enemy, damage * share, enemy.transform.position + Vector3.up, away, at);
            Topple(enemy, away, down, reel);
        }
    }

    // A card's own blast on an enemy, this player's as a punch is (its kill, its money). It comes from `origin`: a blow
    // with a place it came from is a blast to the kill feed (an explosion's picture, KillMarks.Explosion), where one with
    // none read as bare hands.
    private void Blast(EnemyHealth enemy, float amount, Vector3 point, Vector3 direction, Vector3 origin)
    {
        if (amount <= 0f || enemy == null || !enemy.IsAlive) return;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, direction, gameObject,
            false, 40f, true, origin: origin));
    }

    // Thrown down for `down` seconds by a blast (on a copy it falls here at once and the host follows, MeleeCombatTarget);
    // a brute stands and only reels.
    private static void Topple(EnemyHealth enemy, Vector3 away, float down, float reel)
    {
        if (!enemy.IsAlive || !enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) return;
        if (!target.Steadfast) target.KnockDown(away, down);
        else if (reel > 0f) target.Interrupt(reel);
    }
}
