using UnityEngine;

/// <summary>
/// A Schießhund's bounty claimed, seen in the world by everyone, on every PC: the hunter's at once, the others as
/// PlayerNetworkCombat tells them. The user, 2026-11-11: "다른 사람들이 볼 때도 와 개쩐다 싶게 세계에서".
/// The looks are the user's picks of 2026-11-12, drawn by MomentFx.
/// The Desperado's claim is pick C, made flashier as asked ("좀 더 멋있고 화려한 느낌으로. 그리고 죽었을 때 뭔가 터지는 듯이 주변에
/// 이펙트 좀"):
/// - the bounty's nested diamond draws itself round the head and turns to lock, then is stamped;
/// - a seal of ticks runs out over the ground;
/// - teal light bursts out round the body as it falls;
/// - a hairline stands up into the sky, seen across the map, and a few gold glints fall.
/// A plain bounty is pick B: a small CLAIMED plate slammed beside the head.
/// The hunter sees exactly what the others see. Nothing covers the screen any more: the old pillar, coin shower and
/// BOUNTY word went with the banner. The money shows in the kill feed (KillFlairs bounty).
/// </summary>
public sealed class BountyBurst : MonoBehaviour
{
    /// <summary>
    /// A bounty claimed: <paramref name="head"/> where the round went in (or the head), <paramref name="from"/> where the
    /// shot came from, <paramref name="ground"/> the feet. grand: the Desperado's own claim; else a plain bounty.
    /// </summary>
    public static void Play(Vector3 head, Vector3 from, Vector3 ground, bool grand)
    {
        if (!Application.isPlaying) return;
        Color teal = PingMarks.BountyColor;
        if (grand)
        {
            MomentFx.Play(MomentFx.Kind.BountyGrand, head, head - from, 1f, teal, MomentLook.Sigil.Hound, ground);
            MomentSound.Play("bounty", head);
        }
        else MomentFx.Play(MomentFx.Kind.BountyPlain, head, head - from, 1f, teal, MomentLook.Sigil.Hound, ground);
    }
}
