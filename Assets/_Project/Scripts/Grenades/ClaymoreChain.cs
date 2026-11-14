using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Claymores set down close together go off together (the user, 2026-11-14, passed on by the manager: "가까우면 같이
/// 터져"). When one fires, every claymore within GrenadeRules claymoreChain of it, and of each one so found, fires in
/// the same moment, whoever set it down; one further off still waits for its own zombie. Each still blasts its own way
/// (its cone, its grit: FragBlast.PlayChained), its reach and its hurt grown by the chain: claymoreChainRadius and
/// claymoreChainDamage for each claymore past the first, counted up to claymoreChainMost. What is seen and heard is one
/// big blast at the chain's middle, not one each (Tables/PackFx row claymoreChain: the pack's frag look grown with the
/// chain's reach and drawn smaller near the camera, so it never fills the view; one sound; one shake; one bang). Held to
/// set one down, a line shows to each claymore it would go off with (ClaymorePreview), so it is known before it goes down.
/// On every PC: the PC whose player's claymore saw a zombie gathers the chain and sets it off, then names its claymores
/// to the others, each by its owner and number (PlayerGrenades.Chained, PlayerNetworkCombat.MineChainRpc); their copies
/// go off with it, grown the same, and each claymore's hits land on its own owner's PC as that player's (kills, money),
/// whoever's claymore set the chain off.
/// </summary>
public static class ClaymoreChain
{
    // The row of Tables/PackFx for the chain's look and sound.
    private const string Use = "claymoreChain";
    private static readonly List<ClaymoreMine> chain = new List<ClaymoreMine>();
    private static readonly List<ClaymoreMine> named = new List<ClaymoreMine>();

    // How near (m) two claymores' hearts are to go off together; 0 turns chains off.
    private static float Near => Mathf.Max(0f, Throwables.Rule("claymoreChain", 2f));

    /// <summary>
    /// The chain <paramref name="first"/> sets off: it, then every claymore down on this PC, anyone's, within
    /// claymoreChain of one already in it (none fired or being taken back). The list is reused: use it before the next
    /// call.
    /// </summary>
    public static List<ClaymoreMine> Gather(ClaymoreMine first)
    {
        chain.Clear();
        if (first == null) return chain;
        chain.Add(first);
        Spread(first.Heart, null, chain, null);
        return chain;
    }

    /// <summary>
    /// The claymores one set down at <paramref name="heart"/> would go off with, into <paramref name="found"/>, and into
    /// <paramref name="reachedFrom"/> where each is reached from (the heart, or the one found before it that is near it);
    /// <paramref name="leaving"/>, the one setting it down takes back, is left out.
    /// </summary>
    public static void Reach(Vector3 heart, ClaymoreMine leaving, List<ClaymoreMine> found, List<Vector3> reachedFrom)
    {
        found.Clear();
        reachedFrom?.Clear();
        Spread(heart, leaving, found, reachedFrom);
    }

    // Breadth first: from the start, then from each one found in turn, every live claymore within claymoreChain that is
    // not found yet and is not `leaving`.
    private static void Spread(Vector3 start, ClaymoreMine leaving, List<ClaymoreMine> found, List<Vector3> reachedFrom)
    {
        float near = Near;
        if (near <= 0f) return;
        float most = near * near;
        var placed = ClaymoreMine.Placed;
        Vector3 at = start;
        for (int next = 0; ; next++)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var other = placed[i];
                if (other == null || other == leaving || !other.Live || found.Contains(other)) continue;
                if ((other.Heart - at).sqrMagnitude > most) continue;
                found.Add(other);
                reachedFrom?.Add(at);
            }
            if (next >= found.Count) return;
            at = found[next].Heart;
        }
    }

    /// <summary>How much further each claymore of a chain of <paramref name="count"/> reaches (1 alone).</summary>
    public static float Grow(int count) => 1f + Mathf.Max(0f, Throwables.Rule("claymoreChainRadius", .3f)) * Extra(count);

    /// <summary>How much harder each claymore of a chain of <paramref name="count"/> hits (1 alone).</summary>
    public static float Harder(int count) => 1f + Mathf.Max(0f, Throwables.Rule("claymoreChainDamage", .4f)) * Extra(count);

    // The claymores past the first that add to the bonus: up to claymoreChainMost in all.
    private static int Extra(int count)
        => Mathf.Clamp(count, 1, Mathf.Max(1, Mathf.RoundToInt(Throwables.Rule("claymoreChainMost", 5f)))) - 1;

    /// <summary>The middle of the chain's claymores, where its one blast is.</summary>
    public static Vector3 Middle(List<ClaymoreMine> mines)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var claymore in mines)
        {
            if (claymore == null) continue;
            sum += claymore.Heart;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    /// <summary>The chain's claymores named for the other PCs: each one's owner (PlayerRoster.IdOf) and number.</summary>
    public static void Name(List<ClaymoreMine> mines, out ulong[] owners, out ushort[] ids)
    {
        owners = new ulong[mines.Count];
        ids = new ushort[mines.Count];
        for (int i = 0; i < mines.Count; i++)
        {
            var claymore = mines[i];
            if (claymore == null) continue;
            owners[i] = claymore.Owner != null ? PlayerRoster.IdOf(claymore.Owner) : 0UL;
            ids[i] = claymore.Id;
        }
    }

    /// <summary>
    /// The claymores another PC named, found on this PC by owner and number; the ones gone here already (set off from
    /// here a moment ago, or taken back) are left out. The list is reused: use it before the next call.
    /// </summary>
    public static List<ClaymoreMine> Find(ulong[] owners, ushort[] ids)
    {
        named.Clear();
        int count = Mathf.Min(owners.Length, ids.Length);
        var placed = ClaymoreMine.Placed;
        for (int n = 0; n < count; n++)
            for (int i = 0; i < placed.Count; i++)
            {
                var claymore = placed[i];
                if (claymore == null || !claymore.Live || claymore.Id != ids[n] || claymore.Owner == null) continue;
                if (PlayerRoster.IdOf(claymore.Owner) != owners[n] || named.Contains(claymore)) continue;
                named.Add(claymore);
                break;
            }
        return named;
    }

    /// <summary>
    /// Sets a chain off on this PC: <paramref name="mines"/> (each landing its hits here if this PC's player set it down),
    /// grown for <paramref name="count"/> claymores in all (the deciding PC's count, so every PC grows it the same), and
    /// the one big blast at <paramref name="middle"/>.
    /// </summary>
    public static void GoOff(List<ClaymoreMine> mines, int count, Vector3 middle)
    {
        float grow = Grow(count), harder = Harder(count);
        PlayerMovement first = null;
        for (int i = 0; i < mines.Count; i++)
        {
            var claymore = mines[i];
            if (claymore == null || !claymore.Live) continue;
            if (first == null) first = claymore.Owner;
            claymore.GoOff(grow, harder);
        }
        var info = Throwables.Of(Throwable.Claymore);
        if (info != null) Show(middle, info, grow, first);
    }

    // The chain's one big blast at its middle: the pack's look grown with the chain's reach, one sound, the view's shake
    // and the bang that brings calm zombies to look.
    private static void Show(Vector3 middle, ThrowableInfo info, float grow, PlayerMovement owner)
    {
        float reach = Mathf.Max(.5f, info.Radius) * grow;
        if (!GrenadePackFx.Play(Use, middle, Quaternion.identity, reach)) CanBlastEffect.Play(middle, reach * .7f, 0f);
        string sound = PackFx.Text(Use, "sound");
        Throwables.Play(sound.Length > 0 ? sound : "Frag", middle + Vector3.up * .3f, PackFx.Number(Use, "loud", 1f),
            PackFx.Number(Use, "hearFull", 10f), PackFx.Number(Use, "hearFar", 200f), PackFx.Number(Use, "pitch", .8f));
        FragBlast.Shake(middle, reach);
        FragBlast.Bang(middle, info, owner);
    }
}
