using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Q/E peeks as far as the movement says (MovementLean.Offset: Ability.leanOffset at a full lean, less near a wall): the
/// spine tips sideways from the waist just far enough that the eyes, and the camera on them, go that far on this body.
/// CAS's Look tips it unarmed, FPS Animation Framework's Look with a gun (FPSBody); each shares the tip down the spine its
/// own way, and the angle is found for that share from where the body's bones are (LeanRollFor). Then the eyes' actual
/// sideways move is measured and the leaning hitboxes follow it (PlayerLeanHitboxes), so a shot meets the head where it
/// is seen. Before KINEMATION the camera slid the rest of the peek by itself; on the eyes it can't, and the spine's fixed
/// 20-25° carried them only about 0.25 m while the hitboxes sat 0.35 m out (2026-11-08).
/// </summary>
public sealed partial class PlayerBody
{
    // The furthest the spine tips for a peek (degrees), whatever the body.
    private const float MostLeanRoll = 60f;
    // Under this share of a full lean the body counts as upright, and where the eyes sit sideways then is learnt.
    private const float LeanAtRest = .02f;

    private readonly List<(Transform bone, float share)> casLeanChain = new();
    private readonly float[] leanHeights = new float[8], leanShares = new float[8], leanX = new float[8], leanY = new float[8];
    private float eyesSideAtRest;
    private bool hasEyesSide;
    private PlayerLeanHitboxes leanHitboxes;

    private void SetUpLean()
    {
        casLeanChain.Clear();
        hasEyesSide = false;
        leanHitboxes = player != null ? player.GetComponent<PlayerLeanHitboxes>() : null;
    }

    // CAS's Look tips the spine bones it bends evenly (BodyCas: each its equal share of the roll).
    private IReadOnlyList<(Transform bone, float share)> CasLeanChain
    {
        get
        {
            if (casLeanChain.Count == 0 && bodyCas != null)
            {
                var bends = bodyCas.SpineBend;
                foreach (var bend in bends) casLeanChain.Add((bend.bone, 1f / Mathf.Max(1, bends.Count)));
            }
            return casLeanChain;
        }
    }

    /// <summary>
    /// The roll (degrees, positive) that carries the eyes <paramref name="offset"/> (m) sideways when the spine tips as
    /// <paramref name="chain"/> shares it, from the lowest bone up: each bone turns its share about itself, carrying the
    /// ones above it. Heights are the bones' as the body stands now.
    /// </summary>
    internal float LeanRollFor(float offset, IReadOnlyList<(Transform bone, float share)> chain)
    {
        if (offset <= .001f || chain == null || chain.Count == 0 || head == null) return 0f;
        int count = Mathf.Min(chain.Count, leanHeights.Length - 1);
        Vector3 up = transform.up;
        for (int i = 0; i < count; i++)
        {
            leanHeights[i] = chain[i].bone != null ? Vector3.Dot(chain[i].bone.position, up) : 0f;
            leanShares[i] = chain[i].bone != null ? chain[i].share : 0f;
        }
        leanHeights[count] = Vector3.Dot(head.TransformPoint(eyeLocal), up);
        if (Reach(MostLeanRoll) <= offset) return MostLeanRoll;
        float low = 0f, high = MostLeanRoll;
        for (int step = 0; step < 14; step++)
        {
            float middle = (low + high) * .5f;
            if (Reach(middle) < offset) low = middle; else high = middle;
        }
        return (low + high) * .5f;

        // How far sideways the eyes go for a roll, all the points starting on one upright line.
        float Reach(float roll)
        {
            for (int i = 0; i <= count; i++) { leanX[i] = 0f; leanY[i] = leanHeights[i]; }
            for (int i = 0; i < count; i++)
            {
                float angle = roll * leanShares[i] * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                for (int j = i + 1; j <= count; j++)
                {
                    float dx = leanX[j] - leanX[i], dy = leanY[j] - leanY[i];
                    leanX[j] = leanX[i] + dx * cos + dy * sin;
                    leanY[j] = leanY[i] - dx * sin + dy * cos;
                }
            }
            return leanX[count];
        }
    }

    // The tip for CAS's Look this frame (positive tips the top to the left, as the camera's own roll does).
    private float CasLeanRoll()
    {
        float share = player.Abilities != null ? player.Abilities.Lean.Share : 0f;
        if (Mathf.Abs(share) < .001f) return 0f;
        return -Mathf.Sign(share) * LeanRollFor(Mathf.Abs(player.LeanOffset), CasLeanChain);
    }

    // After the pose: how far the eyes have actually gone sideways from where they sit upright; the hitboxes go with them.
    private void FollowLeanWithHitboxes(Vector3 eye, float dt)
    {
        float side = Vector3.Dot(eye - transform.position, transform.right);
        float share = player.Abilities != null ? player.Abilities.Lean.Share : 0f;
        bool leaning = !dead && Mathf.Abs(share) >= LeanAtRest;
        if (!hasEyesSide) { eyesSideAtRest = side; hasEyesSide = true; }
        else if (!leaning) eyesSideAtRest = Mathf.Lerp(eyesSideAtRest, side, 1f - Mathf.Exp(-dt / .2f));
        if (leanHitboxes != null) leanHitboxes.FollowEyes(leaning ? transform.right * (side - eyesSideAtRest) : Vector3.zero);
    }
}
