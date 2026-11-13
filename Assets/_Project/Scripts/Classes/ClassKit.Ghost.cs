using UnityEngine;

// ClassKit: Ghost: Head Hunter, Deadeye, the stagger. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    // Each pellet of this PC's own shot, once traced (a lone bullet is pellet 0 of 1). What the shot met counts once, at
    // its last pellet: a head among its pellets is a headshot, else a body is a body shot.
    private void OnShot(WeaponShot shot)
    {
        if (movement == null || !movement.IsLocal || !PlayerClass.Is(PlayerClass.Ghost)) return;
        var data = HeldGun;
        var trace = shot.Trace;
        // Any foe counts toward the stacks, a range target as a zombie does; only a zombie elite can be staggered.
        var foe = trace.HasHit && trace.Damage == DamageResult.Damaged ? CombatFoe.Of(trace.Hit.collider) : null;
        if (foe != null)
        {
            if (trace.IsHeadshot) shotHead = true;
            else shotBody = true;
            if (foe is EnemyHealth enemy && PlayerClass.IsElite(enemy) && PlayerClass.OwnsWeapon(PlayerClass.Current, data))
                Stagger(enemy, trace.IsHeadshot);
        }
        if (shot.Pellet < (data != null ? Mathf.Max(1, data.Pellets) : 1) - 1) return;
        // The round loaded first is spent.
        int slot = slots != null ? slots.SelectedSlot : -1;
        if (slot >= 0 && slot < loaded.Length) loaded[slot] = false;
        if (shotHead) Hunt(1);
        else if (shotBody) Hunt(-Mathf.Max(0, Mathf.RoundToInt(PlayerClass.Ability(GhostRow, "hunterBodyLoss", 1f))));
        shotHead = shotBody = false;
    }

    // A reload done: the gun in hand is loaded again, its next round Deadeye's.
    private void OnReloaded()
    {
        if (movement == null || !movement.IsLocal || slots == null) return;
        int slot = slots.SelectedSlot;
        if (slot >= 0 && slot < loaded.Length) loaded[slot] = true;
    }

    private void Hunt(int change)
    {
        int before = hunter;
        hunter = Mathf.Clamp(hunter + change, 0, HunterMax);
        if (hunter == before) return;
        hunterChange = hunter > before ? 1 : -1;
        hunterChangedAt = Time.time;
        if (hunter > before) KitSound.Play(KitSound.Cue.Hunter, .45f);
    }

    // Hurt, shield or health: a Ghost loses every stack (the memo's rule: the Head Hunter is for whoever isn't touched).
    private void OnHurt(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hunter <= 0 || receiver == null || !PlayerClass.Is(PlayerClass.Ghost)) return;
        if (receiver.LastShieldTaken + receiver.LastHealthTaken <= 0f) return;
        // The White Tiger card holds: no hurt takes the stacks (ClassCards).
        if (cards != null && cards.TigerGuards) return;
        hunter = 0;
        hunterChange = -2;
        hunterChangedAt = Time.time;
        KitSound.Play(KitSound.Cue.HunterLost, .6f);
    }

    // A Ghost's own gun hit an elite: it staggers (a stun that cuts off its blow or its charge; the host is told by the
    // stun itself, MeleeCombatTarget), unless this Ghost staggered it lately.
    private void Stagger(EnemyHealth enemy, bool head)
    {
        if (!enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) return;
        if (staggerAgain.TryGetValue(target, out float again) && Time.time < again) return;
        float seconds = head ? PlayerClass.Ability(GhostRow, "stumbleHead", 1.6f) : PlayerClass.Ability(GhostRow, "stumbleBody", .8f);
        if (seconds <= 0f) return;
        target.Interrupt(seconds);
        if (staggerAgain.Count >= StaggerMemory) Forget();
        staggerAgain[target] = Time.time + seconds + Mathf.Max(0f, PlayerClass.Ability(GhostRow, "stumbleRest", 3f));
    }

    private void Forget()
    {
        forget.Clear();
        foreach (var pair in staggerAgain) if (pair.Key == null || Time.time >= pair.Value) forget.Add(pair.Key);
        foreach (var key in forget) staggerAgain.Remove(key);
    }
}
