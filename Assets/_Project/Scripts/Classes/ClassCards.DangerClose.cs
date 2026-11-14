using UnityEngine;

// ClassCards: Assault: 데인저 클로즈, the barrage called on a mark with the class key. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // The class key handed over by ClassKit while the card is ready: when it went down (below 0: not held for it), whether
    // it has been held past a tap to aim, where the mark stands now and whether the barrage may fall there.
    private float dangerPressed = -1f;
    private bool dangerAiming, dangerFits;
    private Vector3 dangerMark;
    // When the barrage may be called again.
    private float dangerReadyAt;
    // The mark on this screen while aiming (made on first use).
    private StrikeAim dangerAim;

    /// <summary>An Assault holding 데인저 클로즈 (the class HUD shows its wait and its aim).</summary>
    public bool HasDanger => PlayerClass.Is(PlayerClass.Assault) && PlayerPerks.Has(PlayerPerks.AssaultDanger);
    /// <summary>Its barrage may be called now; and seconds until it may (0: now).</summary>
    public bool DangerReady => HasDanger && Time.time >= dangerReadyAt;
    public float DangerWait => HasDanger ? Mathf.Max(0f, dangerReadyAt - Time.time) : 0f;
    /// <summary>The class key is held to aim it; and whether the mark stands where the barrage may fall.</summary>
    public bool DangerAiming => dangerAiming;
    public bool DangerFits => dangerFits;

    /// <summary>
    /// ClassKit hands an Assault's class key press here first (the user, 2026-11-14: "데인저 클로즈는 진짜 폭격 스킬처럼").
    /// With 데인저 클로즈 ready this keeps it: held, it aims the barrage at a mark on the ground and let go it calls it;
    /// tapped, it is the CHARGE after all (ClassKit.TryCharge), as it was before the card. False: the press is ClassKit's.
    /// </summary>
    internal bool TakesKey()
    {
        if (!DangerReady || movement == null || !movement.IsLocal || movement.IsDowned) return false;
        dangerPressed = Time.time;
        dangerAiming = false;
        return true;
    }

    // Every frame while the key is handed over: held past a tap, it aims (the mark on this screen alone); the guard key
    // calls it off; let go, the barrage is called on the mark, or for a tap the CHARGE.
    private void DangerClose()
    {
        if (dangerPressed < 0f) return;
        var key = KeyBindings.Find("Player/ClassAbility");
        bool able = key != null && HasDanger && movement.InputAvailable && !movement.IsDead && !movement.IsDowned && !movement.IsHeld
            && !movement.CombatInputLocked;
        if (!able)
        {
            StopAim();
            return;
        }
        if (dangerAiming && KeyBindings.Pressed("Combat/Guard"))
        {
            StopAim();
            KitSound.Play(KitSound.Cue.Refuse, .25f);
            return;
        }
        bool held = KeyBindings.Held(key);
        if (held && !dangerAiming && Time.time - dangerPressed >= Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "dangerHold", .2f)))
            dangerAiming = true;
        if (dangerAiming)
        {
            dangerFits = Mark(out dangerMark);
            Aim().Show(dangerMark, Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "barrageRadius", 6f)), dangerFits);
        }
        if (held) return;
        bool aimed = dangerAiming, fits = dangerFits;
        Vector3 mark = dangerMark;
        StopAim();
        if (!aimed)
        {
            kit.OrNull()?.TryCharge();
            return;
        }
        if (!fits)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.assault.danger.ground"));
            return;
        }
        CallDanger(mark);
    }

    // Called: its wait starts (the card's second number, 45/40/35 s), the radio call is heard, and the barrage comes down
    // round the mark after its moment (Assault.dangerDelay), as many shells as the card's level gives (6/9/12).
    private void CallDanger(Vector3 mark)
    {
        dangerReadyAt = Time.time + Mathf.Max(1f, PlayerPerks.Total2(PlayerPerks.AssaultDanger));
        CallBarrage(mark, PlayerClass.Ability(AssaultRow, "dangerDelay", 2f), Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultDanger)), false);
        KitSound.Play(KitSound.Cue.Alert, .7f);
        AlertHUD.Self(Texts.Get("class.assault.danger.self"));
    }

    // Where the view comes down to the ground within reach (Assault.dangerAim): the floor it looks at; under a zombie it
    // looks at, its feet; before a wall, the floor at its foot; looking at nothing, the ground under the end of its reach.
    // False where there is no ground there at all.
    private bool Mark(out Vector3 mark)
    {
        mark = transform.position;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return false;
        float reach = Mathf.Max(2f, PlayerClass.Ability(AssaultRow, "dangerAim", 45f));
        Vector3 look = eyes.forward, end = eyes.position + look * reach;
        if (AssaultStrikes.Ray(eyes.position, look, reach, out RaycastHit seen))
        {
            bool zombie = seen.collider.GetComponentInParent<EnemyHealth>() != null;
            if (!zombie && seen.normal.y >= .55f)
            {
                mark = seen.point;
                return true;
            }
            end = zombie ? seen.point : seen.point - look * .4f;
        }
        return AssaultStrikes.Ground(end + Vector3.up * .5f, reach + 2f, out mark);
    }

    private void StopAim()
    {
        dangerPressed = -1f;
        dangerAiming = false;
        if (dangerAim != null) dangerAim.Hide();
    }

    private StrikeAim Aim() => dangerAim != null ? dangerAim : dangerAim = StrikeAim.Make();
}
