// WeaponController: The hands on the gun. One part of the class; the rest is in WeaponController.cs and the files beside it.
public sealed partial class WeaponController
{
    private HandOwner hands;

    // Whose each of this player's hands is (HandOwner): the gun asks it, and never works out for itself what else has a hand.
    private HandOwner Hands => hands != null ? hands : hands = HandOwner.Of(movement);

    /// <summary>
    /// The gun's bash can be swung now: no hand it needs is busy elsewhere (Tables/Player/HandUses.csv, gun.bash: both).
    /// With the left hand on something else that asked for it and keeps it (a grenade held ready, a syringe) V is the
    /// kick instead, as it is while both hands are on a reload (PlayerCombatActions; the user, 2026-11-18, asked what V
    /// should be with a grenade held: "발차기로 하죠"). A fact of the body having the hands is not that (HandOwner.Kept):
    /// in a zombie's grip V is the shove that fights it off, as it always was.
    /// </summary>
    public bool CanBash => Hands == null || !Hands.Kept(HandUses.GunBash, this);

    // A feeling shown from the callout wheel has both hands (PlayerBody.Emote): the gun is put away meanwhile.
    private bool EmoteHasHands => Hands != null && Hands.Up(HandUses.Emote);

    // The trigger or the sights pressed while a feeling has the gun put away: the player wants the gun, so it asks for
    // the hands as an aim does (30, over the feeling's 20). The feeling, having lost them, ends and the gun comes out
    // (PlayerBody.Emote tells the other PCs). This PC's own player only.
    private void WantGunBack(WeaponInputFrame frame)
    {
        if (Mine && (frame.FirePressed || frame.AimHeld) && EmoteHasHands) Hands.Take(HandUses.GunAim, this);
    }

    // Whether the gun would get the hands of this row if it asked now. noted: the player pressed a key for it, so a no
    // goes to the record (Logs/hands.txt) with the hand it could not have and whose that is.
    private bool HandsFor(string row, bool noted) => Hands == null || Hands.Could(row, this, noted);

    // What became of the hands the gun had: when it has lost one it needs since it last said its row (the left hand to a
    // grenade taken out, both to a climb), what it was doing with both is over: a reload stops where it stands (its
    // rounds are where its hands had put them: WeaponRuntime), a look over the gun ends. Only for this PC's own player:
    // a friend's copy shows what their PC did.
    private void MindHands()
    {
        if (!Mine || Hands == null || Hands.Has(this)) return;
        if (Runtime != null && Runtime.IsReloading) Runtime.Interrupt();
        StopInspecting();
    }

    // What the gun is doing with its hands this frame, said to their owner every frame it is out (the rows gun.* of
    // HandUses.csv): a reload and a look over the gun need both hands, a draw and an aim the right one and the left
    // while nobody stronger wants it, the plain hold the same at the lowest rank, so whatever wants the left hand gets
    // it and the gun goes on in the right (the user, 2026-11-18: "오른손은 조준한 채로 왼손은 수류탄 대기하고 있으면
    // 되잖아요"). A row refused leaves the gun its plain hold. Said again every frame, so a no here is no news for the
    // record: what the player asked for and did not get is written where the key is read (HandsFor).
    private void SayHands(bool aiming)
    {
        if (Hands == null || Runtime == null) return;
        string row = Runtime.IsReloading ? HandUses.GunReload : inspecting ? HandUses.GunInspect : switchLeft > 0f ? HandUses.GunSwap
            : shoveRecovery > 0f ? HandUses.GunBash : aiming ? HandUses.GunAim : HandUses.GunHold;
        if (!Hands.Take(row, this, false) && row != HandUses.GunHold) Hands.Take(HandUses.GunHold, this, false);
    }
}
