using UnityEngine;

// ClassCards: Assault: its ultimate, an A-10's gun run down the line it aims at. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // The run under way on this PC's call: its rounds as every PC lays them (AssaultStrikes.LayRounds), when the first one
    // lands and how long they all take, which is next (below 0: none under way), and the lane's way.
    private Vector3[] runRounds;
    private float[] runShares;
    private bool[] runLanded;
    private float runStart, runTime;
    private int runNext = -1;
    private Vector3 runWay = Vector3.forward;

    /// <summary>
    /// The lane an Assault's gun run takes from where this player aims now (the user, 2026-11-14: "A10 공중 지원 느낌으로";
    /// the draft they took keeps the gauge, with the reason said, where the sky over the aim is closed). It ends a little past
    /// the aim point (Assault.gunRunPast) and runs back toward this player along the ground for its length
    /// (Assault.gunRunLength), so the rounds walk toward what is aimed at, as a strafe walks onto its target. Null when it
    /// can go; else the Texts id saying why not.
    /// </summary>
    private string PlanGunRun(out Vector3 from, out Vector3 to)
    {
        from = to = transform.position;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return "class.assault.gunrun.ground";
        float reach = Mathf.Max(5f, PlayerClass.Ability(AssaultRow, "gunRunReach", 150f));
        Vector3 look = eyes.forward;
        if (!AssaultStrikes.Ray(eyes.position, look, reach, out RaycastHit seen)) return "class.assault.gunrun.ground";
        Vector3 aim = seen.point;
        // A zombie or a wall looked at: the ground at its foot.
        if ((seen.normal.y < .55f || seen.collider.GetComponentInParent<EnemyHealth>() != null)
            && !AssaultStrikes.Ground(seen.point - look * .4f + Vector3.up * .5f, 30f, out aim))
            return "class.assault.gunrun.ground";
        if (AssaultStrikes.Number("gunRunSky", 1f) > .5f && !AssaultStrikes.OpenSky(aim, Mathf.Max(10f, AssaultStrikes.Number("gunRunHigh", 70f))))
            return "class.assault.gunrun.sky";
        Vector3 way = Vector3.ProjectOnPlane(aim - transform.position, Vector3.up);
        if (way.sqrMagnitude < 1f) way = Vector3.ProjectOnPlane(look, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        way = way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward;
        to = aim + way * Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "gunRunPast", 3f));
        from = to - way * Mathf.Max(5f, PlayerClass.Ability(AssaultRow, "gunRunLength", 40f));
        return null;
    }

    // Asked before the gauge is spent (UltimateHeldBack): a gun run wants ground in reach, under open sky, where it is
    // aimed. When not, it says why and the gauge stays full (a wrong aim costs nothing: the user's rule).
    private bool GunRunHeldBack()
    {
        string refusal = PlanGunRun(out _, out _);
        if (refusal == null) return false;
        KitSound.Play(KitSound.Cue.Refuse, .5f);
        AlertHUD.Self(Texts.Get(refusal));
        return true;
    }

    // The ultimate's run, called (Ultimate): the lane goes to every PC, which draw it, its rounds and the plane (CardFx
    // GunRun, GunRunFx), and its rounds land here as they come due (Strafe), after its moment (Assault.gunRunDelay).
    private void CallGunRun()
    {
        if (PlanGunRun(out Vector3 from, out Vector3 to) != null) return;
        int count = AssaultStrikes.RoundCount;
        runRounds = new Vector3[count];
        runShares = new float[count];
        runLanded = new bool[count];
        AssaultStrikes.LayRounds(from, to, runRounds, runShares, runLanded);
        runWay = Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
        float delay = Mathf.Max(0f, AssaultStrikes.Number("gunRunDelay", 2f));
        runStart = Time.time + delay;
        runTime = Mathf.Max(.1f, AssaultStrikes.Number("gunRunTime", 1.1f));
        runNext = 0;
        CardNet.Show(CardFx.Kind.GunRun, to, to - from, delay);
        KitSound.Play(KitSound.Cue.Alert, .7f);
    }

    // The run's rounds landing as they come due, at the pace every PC draws them (game time, as GunRunFx counts it).
    private void Strafe()
    {
        if (runNext < 0 || runRounds == null) return;
        while (runNext < runRounds.Length && Time.time >= runStart + runShares[runNext] * runTime)
        {
            int i = runNext++;
            if (runLanded[i]) Hammer(runRounds[i]);
        }
        if (runNext >= runRounds.Length) runNext = -1;
    }

    // One 30 mm round going off: every enemy within its blast takes the whole of it and is thrown down (a brute only
    // reels); no player is touched. A roof the round struck shelters what is under it.
    private void Hammer(Vector3 at)
    {
        float blast = Mathf.Max(.2f, PlayerClass.Ability(AssaultRow, "gunRunBlast", 1.5f));
        float damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "gunRunDamage", 120f));
        float down = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "gunRunDown", 1.5f));
        float reel = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "gunRunReel", .6f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 off = enemy.transform.position - at;
            // Feet on the ground the round struck, or a little below its edge (beside a car it hit), not under a roof.
            if (off.x * off.x + off.z * off.z <= blast * blast && off.y > -2.2f && off.y < blast) near.Add(enemy);
        }
        foreach (var enemy in near)
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : runWay;
            Blast(enemy, damage, enemy.transform.position + Vector3.up, away, at);
            Topple(enemy, away, down, reel);
        }
    }
}
