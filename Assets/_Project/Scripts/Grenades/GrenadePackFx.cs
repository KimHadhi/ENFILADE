using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A throwable's burst in the Realistic Gunslinger pack's look (the user's yes to moment-vfx's map of the pack, passed on
/// by the manager, 2026-11-13: "ㅇㅇ 제안대로 써"): the frag's Frag Granade, the flashbang's Flashbang granade, the smoke's
/// Smoke Granade and the claymore's Bullet Granade (its shrapnel). PackFx plays and tones each by its row of
/// Tables/PackFx.csv (fragBurst, flashBurst, smokeBurst, claymoreBurst: size, speed, the parts left out, when it is gone),
/// so it stays as restrained as the rest of the pack's uses: short, never over the screen, the big fuzz and decals out.
/// The pack's grenades are showcases: a round is thrown, waits, and its blast comes out of it a few metres off the
/// prefab's root; the smoke waits 1.5 s behind a rock. So after PackFx, the same row's own keys take only the burst,
/// here and now:
/// - hide: parts that neither draw nor emit themselves while their children still run (the thrown round, whose death
///   would set its blast off again three seconds later);
/// - center: the part the burst comes out of: the copy moves so that part sits on the point;
/// - now: parts that start at once instead of after the pack's own wait, emitting as they were made;
/// - fire: parts emitted once, now (path:count), once the copy is in place (the blast's own burst, whose birth sets off
///   every piece of it where it is).
/// Every PC plays its own, as every PC has the burst (GrenadeBursts, ClaymoreMine). False when the pack's look is off or
/// missing: the game's own look draws instead.
/// </summary>
public static class GrenadePackFx
{
    /// <summary>Plays <paramref name="use"/> (a row of Tables/PackFx.csv) bursting at <paramref name="at"/>, its forward along <paramref name="facing"/>.</summary>
    public static bool Play(string use, Vector3 at, Quaternion facing)
    {
        var copy = PackFx.Play(use, at, facing);
        if (copy == null) return false;
        var root = copy.transform;
        foreach (var part in Named(root, PackFx.Text(use, "hide"))) Hide(part);
        var middle = First(root, PackFx.Text(use, "center"));
        // Nothing has come out yet (a particle system emits on its next step): moved now, the whole burst is on the point.
        if (middle != null) root.position += at - middle.position;
        foreach (var part in Named(root, PackFx.Text(use, "now")))
            if (part.gameObject.activeInHierarchy && part.TryGetComponent(out ParticleSystem system))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startDelay = 0f;
                system.Play(false);
            }
        foreach (string entry in TableCells.Parts(PackFx.Text(use, "fire")))
        {
            int colon = entry.LastIndexOf(':');
            string path = colon > 0 ? entry.Substring(0, colon) : entry;
            int count = colon > 0 && int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 1;
            foreach (var part in Named(root, path))
            {
                if (!part.gameObject.activeInHierarchy || !part.TryGetComponent(out ParticleSystem system)) continue;
                // Once, now: no wait, no loop, nothing more of it after.
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startDelay = 0f;
                main.loop = false;
                var emission = system.emission;
                emission.enabled = false;
                system.Play(false);
                system.Emit(Mathf.Clamp(count, 1, 20));
            }
        }
        return true;
    }

    // Its own particles off and never emitted; what hangs under it runs as before.
    private static void Hide(Transform part)
    {
        if (part.TryGetComponent(out ParticleSystem system))
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            var emission = system.emission;
            emission.enabled = false;
        }
        if (part.TryGetComponent(out ParticleSystemRenderer renderer)) renderer.enabled = false;
    }

    // Every part a list names, as PackFx names them: by its path under the copy ("Burst/FuzzAdd": every one there) or by
    // its name anywhere in it.
    private static IEnumerable<Transform> Named(Transform root, string list)
    {
        foreach (string part in TableCells.Parts(list))
            foreach (var found in Matching(root, part)) yield return found;
    }

    private static Transform First(Transform root, string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;
        foreach (var found in Matching(root, part.Trim())) return found;
        return null;
    }

    private static IEnumerable<Transform> Matching(Transform root, string part)
    {
        bool path = part.IndexOf('/') >= 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            if (path ? Hierarchy.PathOf(t, root) == part : t.name == part) yield return t;
        }
    }
}
