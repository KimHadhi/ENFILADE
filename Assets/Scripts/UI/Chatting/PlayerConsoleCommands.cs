using System;
using System.Globalization;
using UnityEngine;

/// <summary>Explicit commands targeting the same player's gameplay resources as the HUD.</summary>
public static class PlayerConsoleCommands
{
    public static void Register(ChatCommandRouter router, Func<PlayerMovement> resolvePlayer)
    {
        router.Register("heal", "[양] HP 회복. 생략하면 최대까지 회복", args => WithPlayer(resolvePlayer, p =>
        {
            float amount = p.DamageReceiver.MaximumHealth;
            if (!string.IsNullOrWhiteSpace(args) && !Amount(args, out amount)) return Usage("heal [양]");
            if (!p.DamageReceiver.IsAlive) return "사망 상태입니다. /revive로 회복하세요.";
            p.DamageReceiver.Heal(amount); return Status(p);
        }));
        router.Register("shield", "양 — 쉴드 추가", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("shield 양");
            if (!p.DamageReceiver.IsAlive) return "사망 상태입니다. /revive를 먼저 사용하세요.";
            p.DamageReceiver.AddShield(amount); return Status(p);
        }));
        router.Register("hurt", "양 — 자신에게 쉴드부터 피해 적용 (회피 무시)", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("hurt 양");
            // Console damage is deterministic and still uses the normal shield/HP damage path.
            var result = p.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),amount,DamageKind.Fall,p.transform.position,Vector3.zero));
            return (result == DamageResult.Ignored ? "피해가 적용되지 않았습니다. " : "") + Status(p);
        }));
        router.Register("maxhp", "값 또는 +증가량/-감소량 — 최대 HP 설정/증감 (최소 1, 자동 회복 없음)", args => WithPlayer(resolvePlayer, p =>
        {
            string text = args.Trim();
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value))
                return "사용법: /maxhp 200 (지정), /maxhp +10 (증가), /maxhp -10 (감소). 최대 HP는 최소 1입니다.";
            bool relative = text[0] == '+' || text[0] == '-';
            double requested = relative ? (double)p.DamageReceiver.MaximumHealth + value : value;
            float maximum = (float)Math.Max(1d, Math.Min(float.MaxValue, requested));
            p.DamageReceiver.SetMaximumHealth(maximum);
            return (requested < 1d ? "최대 HP의 최솟값은 1이므로 1로 설정했습니다. " : "") + Status(p);
        }));
        router.Register("revive", "HP 완전 회복 / 임시 쉴드 초기화", args => WithPlayer(resolvePlayer, p =>
        {
            if (!string.IsNullOrWhiteSpace(args)) return Usage("revive");
            p.DamageReceiver.RestoreHealth(); return Status(p);
        }));
        router.Register("ult", "양 — 궁극기 충전량 추가", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ult 양");
            p.Ultimate.AddCharge(amount); return Status(p);
        }));
        router.Register("ultset", "값 — 궁극기 충전량 지정", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ultset 값");
            p.Ultimate.SetValues(amount,p.Ultimate.MaximumCharge,p.Ultimate.RequiredCharge); return Status(p);
        }));
        router.Register("ultmax", "증가량 — 궁극기 최대 보유량 증가", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ultmax 증가량");
            p.Ultimate.SetLimits(Sum(p.Ultimate.MaximumCharge,amount),p.Ultimate.RequiredCharge); return Status(p);
        }));
        router.Register("ultcost", "값 — 궁극기 사용 필요량 지정", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ultcost 값");
            p.Ultimate.SetLimits(p.Ultimate.MaximumCharge,amount); return Status(p);
        }));
        router.Register("ultuse", "궁극기 자원 소모 테스트 (스킬 발동 없음)", args => WithPlayer(resolvePlayer, p =>
        {
            if (!string.IsNullOrWhiteSpace(args)) return Usage("ultuse");
            return (p.Ultimate.TryConsume() ? "궁극기 자원을 소모했습니다. " : "궁극기 게이지가 부족합니다. ") + Status(p);
        }));
        router.Register("stats", "현재 HP / 쉴드 / 궁극기 확인", args => WithPlayer(resolvePlayer, p =>
            string.IsNullOrWhiteSpace(args) ? Status(p) : Usage("stats")));
    }

    private static string WithPlayer(Func<PlayerMovement> resolve, Func<PlayerMovement,string> command)
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다.";
        var player = resolve();
        if (player == null || player.DamageReceiver == null || player.Ultimate == null)
            return "대상 플레이어를 찾지 못했습니다. ChatManager의 Player Input 연결을 확인하세요.";
        return command(player);
    }

    internal static bool Amount(string args, out float amount)
    {
        return float.TryParse(args,NumberStyles.Float,CultureInfo.InvariantCulture,out amount)
            && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount >= 0f;
    }
    private static float Sum(float a,float b) => (float)Math.Min(float.MaxValue,(double)a+b);
    private static string Usage(string syntax) => "사용법: /" + syntax + " · 0 이상의 숫자를 입력하세요 (소수점: .).";
    private static string Number(float value) => value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string Status(PlayerMovement p) => $"HP {Number(p.DamageReceiver.Health)}/{Number(p.DamageReceiver.MaximumHealth)} · 쉴드 {Number(p.DamageReceiver.Shield)} · 궁극기 {Number(p.Ultimate.CurrentCharge)}/{Number(p.Ultimate.MaximumCharge)} (필요 {Number(p.Ultimate.RequiredCharge)})";
}
