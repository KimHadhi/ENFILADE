using UnityEngine;

// ClassCards: Assault: Broken Arrow. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // Watched every frame: health falling through its line, or a grab taking hold, calls the barrage (when it may be
    // called); then its shells go out one by one, and each one landing hurts what is round it.
    private void BrokenArrow()
    {
        var body = movement.DamageReceiver;
        bool able = body != null && body.IsAlive && !movement.IsDead;
        bool high = able && body.MaximumHealth > 0f
            && body.Health / body.MaximumHealth * 100f > PlayerClass.Ability(AssaultRow, "brokenHealth", 30f);
        bool held = able && movement.IsHeld;
        if (able && PlayerClass.Is(PlayerClass.Assault) && PlayerPerks.Has(PlayerPerks.AssaultBroken) && Time.time >= brokenAgain
            && ((wasHigh && !high) || (held && !wasHeld)))
            CallBrokenArrow();
        wasHigh = high || !able;
        wasHeld = held;
        if (shellsLeft > 0 && Time.time >= shellAt)
        {
            shellsLeft--;
            shellAt = Time.time + Mathf.Max(.02f, PlayerClass.Ability(AssaultRow, "brokenEvery", .22f));
            Shell();
        }
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            if (Time.time < falling[i].due) continue;
            Land(falling[i].spot);
            falling.RemoveAt(i);
        }
    }

    private void CallBrokenArrow()
    {
        brokenAgain = Time.time + Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "brokenCooldown", 75f));
        shellsLeft = Mathf.Max(1, Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultBroken)));
        shellAt = Time.time + Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "brokenDelay", 1.5f));
        AlertHUD.Self(Texts.Get("class.assault.broken.self"));
        KitSound.Play(KitSound.Cue.Alert, .8f);
    }

    // One shell called down: on the next of the nearest enemies round this player (in turn, the nearest first), or on
    // open ground round them when nobody is there.
    private void Shell()
    {
        Vector3 me = transform.position;
        float reach = Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "brokenRadius", 9f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - me).sqrMagnitude <= reach * reach) near.Add(enemy);
        near.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));
        int total = Mathf.Max(1, Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultBroken))), index = Mathf.Max(0, total - 1 - shellsLeft);
        Vector3 at;
        if (near.Count > 0)
        {
            var target = near[index % near.Count];
            Vector2 jitter = Random.insideUnitCircle * .5f;
            at = target.transform.position + new Vector3(jitter.x, 0f, jitter.y);
        }
        else
        {
            Vector2 spot = Random.insideUnitCircle.normalized * Random.Range(3f, reach);
            at = me + new Vector3(spot.x, 0f, spot.y);
        }
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, ~(1 << 31), QueryTriggerInteraction.Ignore))
            at = ground.point;
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "brokenBlast", 3f));
        Vector2 lean = Random.insideUnitCircle;
        CardNet.Show(CardFx.Kind.Shell, at, new Vector3(lean.x, 0f, lean.y), blast);
        falling.Add((Time.time + CardFx.ShellFall, at));
    }

    // A shell landing: every enemy in its blast is hurt (less toward its edge) and thrown down; no player is.
    private void Land(Vector3 at)
    {
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "brokenBlast", 3f));
        float damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "brokenDamage", 150f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= blast * blast) near.Add(enemy);
        foreach (var enemy in near)
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float share = 1f - .5f * Mathf.Clamp01(away.magnitude / blast);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Strike(enemy, damage * share, enemy.transform.position + Vector3.up, away, 40f);
            if (enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable)
            {
                if (target.Steadfast) target.Interrupt(.8f);
                else target.KnockDown(away, 1.5f);
            }
        }
    }
}
