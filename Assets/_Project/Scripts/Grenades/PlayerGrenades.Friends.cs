using UnityEngine;

// PlayerGrenades: Friends' throws, here. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    /// <summary>
    /// A friend's off hand went for a throwable, threw it or put it back: this PC's copy of them plays the same clips (a
    /// tap its tap clip, or the draw at the tap's speed). The throwable itself comes with ShowThrow.
    /// </summary>
    internal void ShowHand(Throwable handKind, HandPhase phase)
    {
        switch (phase)
        {
            case HandPhase.Draw:
                StartArm(handKind);
                letGo = false;
                if (handKind == Throwable.Knife) KnifeOut(transform.position + Vector3.up * 1.3f);
                break;
            case HandPhase.Throw:
            case HandPhase.TapThrow:
                // A claymore is set down, not thrown: the copy's hand lowers when it is told where it went (ShowMine).
                if (Throwables.Of(handKind) is ThrowableInfo setDown && setDown.Places) { letGo = true; break; }
                bool fresh = arm == Arm.None || arm == Arm.Lower;
                // Its draw never came: a tap from the start of its clip, a throw straight from the hold.
                if (fresh) StartArm(handKind);
                letGo = true;
                if (phase == HandPhase.TapThrow && StartTap()) break;
                if (phase == HandPhase.TapThrow) speed = GrenadeArmPath.For(handKind).TapSpeed;
                if (fresh) arm = Arm.Hold;
                if (arm == Arm.Hold) StartThrow();
                break;
            case HandPhase.Lower:
                if (arm == Arm.Draw || arm == Arm.Hold) Lower();
                break;
        }
    }

    /// <summary>A friend threw one: this PC throws its own copy (their PC's decides where it bursts).</summary>
    internal void ShowThrow(Throwable thrownKind, ushort id, Vector3 from, Vector3 velocity, float fuse)
    {
        Throwables.NoteThrower(movement, thrownKind);
        // It has left their hand: the copy's hand lets go of it too, wherever its arm has got to.
        released = true;
        if (thrownKind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(from, velocity, movement, false);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .8f, 1.5f, 20f);
            Throwables.Play("KnifeThrowCloth", from, .7f, 1.5f, 15f);
            return;
        }
        var grenade = ThrownGrenade.Throw(thrownKind, id, from, velocity, fuse, movement, false, null);
        if (grenade != null) flying[id] = grenade;
        Throwables.Play("Throw", from, .7f, 1.5f, 20f);
    }

    /// <summary>
    /// A friend set a claymore down there: this PC sets its own copy down (their PC fires it: ShowBurst, ShowChain), after
    /// taking off the copies of the ones their PC says came back off for it (<paramref name="retired"/>: their numbers).
    /// Their PC counted, with their cards; this one never counts a friend's (memory actor-decides).
    /// </summary>
    internal void ShowMine(ushort id, Vector3 at, Quaternion facing, bool wall, ushort[] retired)
    {
        if (retired != null)
            foreach (ushort gone in retired)
            {
                var old = mines.Find(m => m != null && m.Id == gone);
                if (old != null) old.Retire();
            }
        Hold(ClaymoreMine.Place(id, at, facing, wall, movement, false, null));
        Throwables.Play("Bounce", at, .6f, .7f, 15f);
        if (arm == Arm.Draw || arm == Arm.Hold) Lower();
    }

    /// <summary>A friend's throwable burst there: this PC's copy bursts in that spot (or, held too long, in their hand).</summary>
    internal void ShowBurst(Throwable thrownKind, ushort id, Vector3 at)
    {
        Throwables.NoteThrower(movement, thrownKind);
        // Their claymore fired: its copy here fires the same way from where it lies.
        if (thrownKind == Throwable.Claymore)
        {
            var claymore = mines.Find(m => m != null && m.Id == id);
            if (claymore != null)
            {
                mines.Remove(claymore);
                claymore.BurstAt(at);
                return;
            }
        }
        if (flying.TryGetValue(id, out var grenade) && grenade != null)
        {
            flying.Remove(id);
            grenade.BurstAt(at);
            return;
        }
        // Gone off in their hand: the copy's hand has nothing left to hold.
        if (arm == Arm.Draw || arm == Arm.Hold)
        {
            released = true;
            Lower();
        }
        GrenadeBursts.Play(thrownKind, at, movement, false);
    }

    /// <summary>
    /// A friend's claymore went off with others set down near it (their PC gathered them, ClaymoreChain): this PC's
    /// copies of those, anyone's, go off together as one big blast, and this PC's player's own among them land their hits
    /// here.
    /// </summary>
    internal void ShowChain(ulong[] owners, ushort[] ids, Vector3 middle)
    {
        if (owners == null || ids == null) return;
        var chain = ClaymoreChain.Find(owners, ids);
        // None left here: this PC set the same ones off a moment ago (one of its own saw a zombie too).
        if (chain.Count > 0) ClaymoreChain.GoOff(chain, ids.Length, middle);
    }

    /// <summary>A friend's grenade bounced or came to rest on their PC, flying so: this PC's copy follows it.</summary>
    internal void ShowFlight(ushort id, GrenadeFlight theirs)
    {
        if (flying.TryGetValue(id, out var grenade) && grenade != null) grenade.Follow(theirs);
    }

    /// <summary>A friend's thrown knife went in or came to lie there on their PC: this PC's copy goes there.</summary>
    internal void ShowKnifeLanded(ushort id, in BodyPoint at, bool lying)
    {
        if (knives.TryGetValue(id, out var knife) && knife != null) knife.Land(at, lying);
    }

    /// <summary>A friend took a thrown knife back: this PC's copy of it goes.</summary>
    internal void ShowKnifeTaken(ushort id)
    {
        if (knives.TryGetValue(id, out var knife) && knife != null) knife.Take();
        knives.Remove(id);
    }
}
