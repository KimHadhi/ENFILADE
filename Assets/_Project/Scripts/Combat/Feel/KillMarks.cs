using System;

/// <summary>
/// The conditions a kill happened under, for the kill feed's flairs (KillFlairs; the user, 2026-11-12: "킬로그 무기 아이콘 쪽에
/// 이펙트도 있었음 좋겠어. 예를 들어 어썰트의 돌격 버프를 받고 처치한 거면 총구 쪽에 노란 화염이 불타며 … 시순트가 표적을
/// 처치한 거는 킬로그가 계약 완료 느낌으로"). The PC that calls a kill decides them (the host for a shared enemy, a lone PC its
/// own) and sends them with it (PlayerNetworkScore.KillRpc), so every PC plays the same flair. What only the killer's own PC
/// knows (their CHARGE, their Gun Kata) rides each of their hits to the host first (DamageHit.Marks, EnemyHitMessage).
/// </summary>
[Flags]
public enum KillMarks : uint
{
    None = 0,
    // How the blow landed.
    Headshot = 1 << 0,
    Pierced = 1 << 1,
    LongRange = 1 << 2,
    Explosion = 1 << 3,
    Execution = 1 << 4,
    Takedown = 1 << 5,
    Counter = 1 << 6,
    Shove = 1 << 7,
    Melee = 1 << 8,
    Thrown = 1 << 11,
    // Who fell.
    Standout = 1 << 9,
    Elite = 1 << 10,
    // The killer's own state, known on their PC.
    Charged = 1 << 16,
    Kata = 1 << 17,
    // A Schießhund's quarry: their own bounty mark, their duel foe.
    Bounty = 1 << 20,
    Duel = 1 << 21,
}
