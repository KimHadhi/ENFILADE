using UnityEngine;

/// <summary>
/// When the crouch key takes a player on their feet down and lets them up again, as in CS2 (the user, 2026-11-23: "앉기는 최신
/// 카스처럼", of its "너무 빨리 다시 누른 입력은 받지 않습니다": "이거면 연타해도 캐릭터 위아래로 급하게 미세하게라도 안 흔들리고
/// 잘 되려나?"). A way down or up that has begun goes to its end before the key turns it (PlayerBody.PostureSeconds), and a key
/// pressed or let go again within Ability.crouchAgain of the last time is not taken until it is left alone that long; then what it
/// says goes. So a tapped key never turns the body back on its way, and one tapped over and over leaves it where it went: at
/// seven taps a second it turned every tap, 11-31 cm up and down; refusing the next crouch alone still left a 3-17 cm dip each
/// time. A single crouch or stand is taken at once, however long it is held.
/// </summary>
internal sealed class CrouchKey
{
    private readonly PlayerMovement ctx;
    private bool wants, hasty;
    private float changedAt, freeAt;

    internal CrouchKey(PlayerMovement context)
    {
        ctx = context;
        Reset();
    }

    internal void Reset()
    {
        wants = hasty = false;
        changedAt = freeAt = float.NegativeInfinity;
    }

    /// <summary>What the key said when it was last asked, taken or not yet.</summary>
    internal bool Wants => wants;

    /// <summary>Whether to be crouched, asked each frame on the feet: what the key says, or as they are while it is not taken.</summary>
    internal bool Crouches(bool held, bool crouched)
    {
        float now = Time.time, again = ctx.AbilitySettings.crouchAgain;
        if (held != wants)
        {
            hasty = now - changedAt < again;
            changedAt = now;
            wants = held;
        }
        return now >= freeAt && (!hasty || now - changedAt >= again) ? wants : crouched;
    }

    /// <summary>The player goes down, or gets up, by the key.</summary>
    internal void Goes(bool down) => freeAt = Time.time + ctx.PostureSeconds(down);
}
