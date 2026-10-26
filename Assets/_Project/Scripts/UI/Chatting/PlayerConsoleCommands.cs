using System;
using System.Globalization;
using UnityEngine;

/// <summary>Explicit commands targeting the same player's gameplay resources as the HUD.</summary>
public static class PlayerConsoleCommands
{
    public static void Register(ChatCommandRouter router, Func<PlayerMovement> resolvePlayer)
    {
        router.Register("spawn", "[수량] 앞쪽 빈 공간에 적 소환. 생략하면 1마리 (웨이브 집계 제외)", args => WithPlayer(resolvePlayer, p =>
        {
            int count = 1;
            if (!string.IsNullOrWhiteSpace(args) && (!int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1))
                return "사용법: /spawn [수량] — 1 이상의 정수를 입력하세요. 생략하면 1마리 소환합니다.";
            if (!p.DamageReceiver.IsAlive) return "사망 상태입니다. /revive를 먼저 사용하세요.";
            return ConsoleEnemySpawner.Spawn(p, count);
        }));
        router.Register("ammo", "보유한 모든 총기의 탄창과 기본 예비탄 보충", args => WithPlayer(resolvePlayer, p =>
        {
            if (!string.IsNullOrWhiteSpace(args)) return "사용법: /ammo — 보유한 모든 총기의 탄약을 보충합니다.";
            var slots = p.GetComponentInChildren<PlayerWeaponSlots>(true);
            int count = slots != null ? slots.RefillAmmo() : 0;
            if (slots == null)
            {
                var runtime = p.GetComponentInChildren<WeaponController>(true)?.Runtime;
                if (runtime != null && runtime.Data.Category == WeaponCategory.Ranged) { runtime.RefillAmmo(); count = 1; }
            }
            return count > 0 ? $"총기 {count}정의 탄창과 예비탄을 보충했습니다." : "탄약을 보충할 총기를 보유하고 있지 않습니다.";
        }));
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
        router.Register("hit", "양 [front/right/back/left] — 방향성 피격 연출 시험 (기본 front)", args => WithPlayer(resolvePlayer, p =>
        {
            string[] parts = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2 || !Amount(parts[0], out float amount))
                return Usage("hit 양 [front/right/back/left]");
            var camera = p.GetComponentInChildren<Camera>();
            if (camera == null) return "플레이어 카메라를 찾지 못했습니다.";
            Vector3 axis;
            switch (parts.Length > 1 ? parts[1].ToLowerInvariant() : "front")
            {
                case "front": axis = camera.transform.forward; break;
                case "right": axis = camera.transform.right; break;
                case "back": axis = -camera.transform.forward; break;
                case "left": axis = -camera.transform.right; break;
                default: return "사용법: /hit 양 [front/right/back/left]";
            }
            Vector3 origin = camera.transform.position + axis * 15f;
            var result = p.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack,
                camera.transform.position, -axis, origin: origin));
            return $"{result} · " + Status(p);
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
            p.Ultimate.SetCurrentCharge(amount); return Status(p);
        }));
        router.Register("ultmax", "값 — 궁극기 최대 보유량 스탯 지정 (%)", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ultmax 값");
            p.Stats.SetUltimateCapacity(amount); return Status(p);
        }));
        router.Register("ultcost", "값 — 궁극기 1회 소비량 스탯 지정 (%)", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args,out float amount)) return Usage("ultcost 값");
            p.Stats.SetUltimateCost(amount); return Status(p);
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
        if (player == null || player.DamageReceiver == null || player.Stats == null || player.Ultimate == null)
            return "대상 플레이어를 찾지 못했습니다. ChatManager의 Player Input 연결을 확인하세요.";
        return command(player);
    }

    internal static bool Amount(string args, out float amount)
    {
        return float.TryParse(args,NumberStyles.Float,CultureInfo.InvariantCulture,out amount)
            && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount >= 0f;
    }
    private static string Usage(string syntax) => "사용법: /" + syntax + " · 0 이상의 숫자를 입력하세요 (소수점: .).";
    private static string Number(float value) => value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string Status(PlayerMovement p) => $"HP {Number(p.DamageReceiver.Health)}/{Number(p.DamageReceiver.MaximumHealth)} · 쉴드 {Number(p.DamageReceiver.Shield)} · 궁극기 {Number(p.Ultimate.CurrentCharge)}%/{Number(p.Ultimate.MaximumCharge)}% (1회 소비 {Number(p.Ultimate.RequiredCharge)}%)";
}
