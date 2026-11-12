using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A small skull over every foe a Schießhund's bounty mark is on: the Realistic Gunslinger pack's Mark Debuff, the skull
/// with the bullet hole, in the mark's teal, looping just over its head for as long as the mark lasts (PackFx
/// bountyMark; the user approved the pack map, 2026-11-13). It sits under the mark's diamond, which stays PingMarks'.
/// The marks are PingMarks' (class logic) and every PC keeps them alike, so every PC shows the same skulls; only the
/// look is here. It goes with the mark: claimed, run out or moved to another foe.
/// </summary>
public sealed class BountySkulls : MonoBehaviour
{
    private const string Use = "bountyMark";
    private static BountySkulls instance;

    private readonly Dictionary<Component, PackFx> skulls = new Dictionary<Component, PackFx>();
    private readonly HashSet<Component> marked = new HashSet<Component>();
    private readonly List<Component> gone = new List<Component>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Bounty Skulls");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<BountySkulls>();
    }

    private void LateUpdate()
    {
        marked.Clear();
        if (PackFx.Number(Use, "on", 1f) > .5f)
            foreach (var hunter in PlayerRoster.All)
            {
                if (hunter == null) continue;
                var foe = PingMarks.BountyOf(hunter);
                if (foe != null) marked.Add(foe);
            }
        // Each marked foe keeps its skull just over its head.
        Vector3 over = Vector3.up * PackFx.Number(Use, "over", -.2f);
        foreach (var foe in marked)
        {
            Vector3 at = CombatFoe.Top(foe) + over;
            if (!skulls.TryGetValue(foe, out var skull) || skull == null)
            {
                var copy = PackFx.Play(Use, at, Quaternion.identity);
                skull = copy != null ? copy.GetComponent<PackFx>() : null;
                if (skull == null) continue;
                skulls[foe] = skull;
            }
            skull.transform.position = at;
        }
        // A mark gone, or its foe: its skull fades out.
        gone.Clear();
        foreach (var pair in skulls)
            if (pair.Key == null || pair.Value == null || !marked.Contains(pair.Key)) gone.Add(pair.Key);
        float fade = PackFx.Number(Use, "fade", .4f);
        foreach (var foe in gone)
        {
            if (skulls.TryGetValue(foe, out var skull) && skull != null) skull.End(fade);
            skulls.Remove(foe);
        }
    }

    private void OnDestroy()
    {
        foreach (var skull in skulls.Values)
            if (skull != null) Destroy(skull.gameObject);
        skulls.Clear();
    }
}
