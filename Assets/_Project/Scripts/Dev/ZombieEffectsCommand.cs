// dev-text: file (the console's /zfx and its answers, for trying the zombie hooks before a card uses them; nothing here is shown to players)
using UnityEngine;

/// <summary>
/// The console's /zfx: tries what class cards and ultimates do to zombies (ZombieEffects) by hand, from this PC's own
/// player, so each can be seen working alone and in a room (as a host and as a guest) before any card calls it. The
/// gas here has no cloud to look at: its look is the card's to make.
/// </summary>
public static class ZombieEffectsCommand
{
    private const float Near = 15f;
    private const string Usage = "/zfx suppress [초] · unseen [초] · gas [초] [반경m] · move — 좀비 훅 시험: 15m 안 좀비 제압 / 내가 좀비 눈에 안 보임 / 내 자리에 눈 가리는 가스 / 가장 가까운 좀비를 내 앞 2m로 옮기기";

    public static string Command(string args)
    {
        var mine = PlayerRoster.Local;
        if (mine == null) return "플레이어가 없습니다.";
        string[] words = (args ?? "").Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Usage;
        Vector3 at = mine.transform.position;
        switch (words[0].ToLowerInvariant())
        {
            case "suppress":
            {
                float seconds = Number(words, 1, 3f);
                int count = 0;
                foreach (var zombie in EnemyHealth.Living)
                {
                    if (zombie == null || !zombie.IsAlive || (zombie.transform.position - at).sqrMagnitude > Near * Near) continue;
                    ZombieEffects.Suppress(zombie, seconds, .45f, .3f);
                    count++;
                }
                return $"{Near:F0}m 안 좀비 {count}마리를 {seconds:F1}초 제압했습니다(걷기 45% 느리게, 공격 준비 30% 길게, 움츠림).";
            }
            case "unseen":
            {
                float seconds = Number(words, 1, 10f);
                ZombieEffects.Unseen(mine, seconds);
                return $"{seconds:F0}초 동안 좀비 눈에 안 보입니다(소리와 닿을 거리는 여전히 들킴).";
            }
            case "gas":
            {
                float seconds = Number(words, 1, 3f), radius = Number(words, 2, 4f);
                ZombieEffects.Blind(at, radius, seconds, .5f);
                return $"내 자리 반경 {radius:F0}m에 {seconds:F0}초 동안 눈 가리는 가스(모습은 없음): 안의 좀비가 나를 놓치고 기침하며 50% 느려집니다.";
            }
            case "move":
            {
                EnemyHealth nearest = null;
                float best = float.PositiveInfinity;
                foreach (var zombie in EnemyHealth.Living)
                {
                    if (zombie == null || !zombie.IsAlive) continue;
                    float distance = (zombie.transform.position - at).sqrMagnitude;
                    if (distance < best) { best = distance; nearest = zombie; }
                }
                if (nearest == null) return "좀비가 없습니다.";
                Vector3 ahead = Vector3.ProjectOnPlane(mine.transform.forward, Vector3.up).normalized;
                ZombieEffects.Move(nearest, at + ahead * 2f, Quaternion.LookRotation(-ahead, Vector3.up));
                return "가장 가까운 좀비를 내 앞 2m로 옮겼습니다.";
            }
            default: return Usage;
        }
    }

    private static float Number(string[] words, int index, float fallback)
        => words.Length > index && float.TryParse(words[index], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) && value > 0f ? value : fallback;
}
