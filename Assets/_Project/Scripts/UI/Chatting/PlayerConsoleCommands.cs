using System;
using System.Globalization;
using UnityEngine;

/// <summary>Explicit commands targeting the same player's gameplay resources as the HUD.</summary>
public static class PlayerConsoleCommands
{
    public static void Register(ChatCommandRouter router, Func<PlayerMovement> resolvePlayer)
    {
        router.Register("spawn", "[수량] [elite/named] [calm] [runner/shambler/brute/exploder/charger] 앞쪽 빈 공간에 적 소환. 생략하면 1마리 (웨이브 집계 제외). elite·named는 잡으면 킬 피드가 자홍 불꽃으로 탄다. calm은 등을 돌리고 조용히 서서 플레이어를 알아채야 한다. runner·shambler·brute(거구)·exploder(자폭)·charger(돌진)는 좀비 종류(생략하면 표의 비율대로)", args => WithPlayer(resolvePlayer, p =>
        {
            int count = 1;
            EnemyRank? rank = null;
            bool calm = false;
            ZombieKind? kind = null;
            const string usage = "사용법: /spawn [수량] [elite/named] [calm] [runner/shambler] — 수량은 1 이상의 정수. 생략하면 보통 적 1마리를 소환합니다.";
            foreach (string part in args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Equals("elite", StringComparison.OrdinalIgnoreCase) || part == "엘리트") rank = EnemyRank.Elite;
                else if (part.Equals("named", StringComparison.OrdinalIgnoreCase) || part == "네임드") rank = EnemyRank.Named;
                else if (part.Equals("calm", StringComparison.OrdinalIgnoreCase) || part == "조용") calm = true;
                else if (part.Equals("runner", StringComparison.OrdinalIgnoreCase) || part == "러너") kind = ZombieKind.Runner;
                else if (part.Equals("shambler", StringComparison.OrdinalIgnoreCase) || part == "셤블러") kind = ZombieKind.Shambler;
                else if (part.Equals("brute", StringComparison.OrdinalIgnoreCase) || part == "거구") { kind = ZombieKind.Brute; rank ??= EnemyRank.Elite; }
                else if (part.Equals("exploder", StringComparison.OrdinalIgnoreCase) || part == "자폭") { kind = ZombieKind.Exploder; rank ??= EnemyRank.Elite; }
                else if (part.Equals("charger", StringComparison.OrdinalIgnoreCase) || part == "돌진") { kind = ZombieKind.Charger; rank ??= EnemyRank.Elite; }
                else if (char.IsLetter(part[0]) && Enum.TryParse(part, true, out ZombieKind named)) { kind = named; rank ??= EnemyRank.Elite; } // ninja, candyman, breadbag: any kind by its name
                else if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1) return usage;
            }
            if (!p.DamageReceiver.IsAlive) return "사망 상태입니다. /revive를 먼저 사용하세요.";
            return ConsoleEnemySpawner.Spawn(p, count, rank, calm, kind);
        }));
        router.Register("poses", "[sight/noise/shot [번호]/stone/stay] [runner 등 종류] 조용한 자세(앉기·먹기·웅크리기·엎드리기)마다 좀비를 하나씩 앞에 반원으로 소환하고, 자세가 잡히면(2.5초) 깨운다. sight: 플레이어를 보고 있다가 눈으로 알아챔 · noise: 큰 소리 두 번 · shot: 1 피해 총알(번호를 주면 그 하나만 쏘고 나머지는 그 부름에 깸) · stone: 뒤에 돌을 떨어뜨려 가장 가까운 하나가 수색하러 걸어감 · stay(기본): 깨우지 않음. 모두 제자리에서 일어선 다음에 움직여야 한다(자세는 호스트 화면만 정해짐)", args => WithPlayer(resolvePlayer, p =>
        {
            var wake = ConsolePoseTest.Wake.Stay;
            int only = -1;
            ZombieKind? kind = null;
            const string usage = "사용법: /poses [sight/noise/shot [번호]/stone/stay] [runner 등 종류]";
            foreach (string part in args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // A number first: Enum.TryParse would take "3" as a wake too.
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) { if (index < 0) return usage; only = index; }
                else if (Enum.TryParse(part, true, out ConsolePoseTest.Wake way)) wake = way;
                else if (char.IsLetter(part[0]) && Enum.TryParse(part, true, out ZombieKind named)) kind = named;
                else return usage;
            }
            if (!p.DamageReceiver.IsAlive) return "사망 상태입니다. /revive를 먼저 사용하세요.";
            return ConsolePoseTest.Spawn(p, wake, only, kind);
        }));
        router.Register("dummy", "[수량] 또는 clear — 멀티 구조 시험용 더미 플레이어 소환·제거", args => WithPlayer(resolvePlayer, p =>
        {
            string text = args.Trim();
            if (text.Equals("clear", StringComparison.OrdinalIgnoreCase)) return ConsoleDummyPlayers.Clear();
            int count = 1;
            if (text.Length > 0 && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1))
                return "사용법: /dummy [수량] 또는 /dummy clear";
            return ConsoleDummyPlayers.Spawn(p, count);
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
        router.Register("gun", "이름 — 들고 있는 총 칸(총이 아니면 1번 칸)을 그 총으로 바꾸고 꺼낸다. 비우면 목록", args => WithPlayer(resolvePlayer, p =>
        {
            var guns = new System.Collections.Generic.List<WeaponData>();
            var assets = FPSBodyAssets.Instance;
            if (assets != null)
                foreach (var entry in assets.guns)
                    if (entry.weapon != null && entry.weapon.Category == WeaponCategory.Ranged && !guns.Contains(entry.weapon)) guns.Add(entry.weapon);
            string list = string.Join(", ", guns.ConvertAll(gun => gun.name));
            string wanted = Plain(args);
            if (wanted.Length == 0) return $"사용법: /gun 이름 — 있는 총: {list}";
            WeaponData found = guns.Find(gun => Plain(gun.name) == wanted || Plain(gun.DisplayName) == wanted)
                ?? guns.Find(gun => Plain(gun.name).Contains(wanted) || Plain(gun.DisplayName).Contains(wanted));
            if (found == null) return $"'{args.Trim()}' 총이 없습니다. 있는 총: {list}";
            var slots = p.GetComponentInChildren<PlayerWeaponSlots>(true);
            if (slots == null) return "무기 칸이 없습니다.";
            int slot = slots.SelectedSlot >= 0 && PlayerWeaponSlots.GetSlotCategory(slots.SelectedSlot) == WeaponCategory.Ranged ? slots.SelectedSlot : 0;
            if (!slots.TrySetWeapon(slot, found)) return "지금은 총을 바꿀 수 없습니다.";
            slots.SelectSlot(slot);
            return $"{slot + 1}번 칸: {found.DisplayName}";

            // Lower case, letters and digits only: "Herrington 11-87" and "herrington1187" are the same gun.
            static string Plain(string text)
            {
                var plain = new System.Text.StringBuilder();
                foreach (char c in text ?? string.Empty)
                    if (char.IsLetterOrDigit(c)) plain.Append(char.ToLowerInvariant(c));
                return plain.ToString();
            }
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
        router.Register("fatal", "[front/right/back/left] — 쓰러짐 없이 즉사. 방향을 주면 그쪽에서 맞아 시체가 밀려난다", args => WithPlayer(resolvePlayer, p =>
        {
            string side = args.Trim();
            var camera = p.GetComponentInChildren<Camera>();
            Vector3 axis = Vector3.zero;
            if (side.Length > 0 && (camera == null || !TryHitSide(side, camera.transform, out axis))) return Usage("fatal [front/right/back/left]");
            var result = p.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), p.DamageReceiver.MaximumHealth,
                side.Length > 0 ? DamageKind.Attack : DamageKind.Fall, p.transform.position + Vector3.up, -axis, isFatal: true));
            return (result == DamageResult.Ignored ? "피해가 적용되지 않았습니다. " : "") + Status(p);
        }));
        router.Register("streak", "값 — 연속 처치 수 지정 (다음 처치부터 킬 피드에 반영)", args => WithPlayer(resolvePlayer, p =>
        {
            if (!Amount(args, out float amount) || !p.TryGetComponent(out PlayerTally tally)) return Usage("streak 값");
            tally.SetStreak(Mathf.RoundToInt(amount));
            return "연속 처치 " + tally.Streak;
        }));
        router.Register("counter", "[0~3] — COUNTER 연출만 그 단계로 미리 보기 (비우면 0). 실제로는 연속 카운터마다 한 단계씩 오른다", args => WithPlayer(resolvePlayer, p =>
        {
            int grade = 0;
            if (!string.IsNullOrWhiteSpace(args)
                && (!int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out grade) || grade < 0 || grade >= CounterBanner.Grades))
                return "사용법: /counter [0~" + (CounterBanner.Grades - 1) + "]";
            if (!p.TryGetComponent(out ShoveCounter counter)) return "카운터를 쓸 수 없는 상태입니다.";
            counter.PreviewBanner(grade);
            return "COUNTER 연출 " + grade + "단계";
        }));
        // To judge the fitted hitboxes by eye (ZombieBody.Hitboxes.cs; 2026-11-11 playtest).
        router.Register("hitbox", "좀비 히트박스 보이기/숨기기 — 머리(목 포함) 빨강, 몸 노랑", _ => ZombieBody.ToggleHitboxes());
        router.Register("alert", "[danger/team/self] — 화면 위 알림 띠 미리 보기 (비우면 danger). danger는 오른쪽, team은 왼쪽 25m에 가상의 자리를 둔다", args => WithPlayer(resolvePlayer, p =>
        {
            string kind = args.Trim().ToLowerInvariant();
            if (kind == "self") { AlertHUD.Self("실드 깨짐"); return "알림: 내 상태"; }
            if (kind.Length > 0 && kind != "danger" && kind != "team") return "사용법: /alert [danger/team/self]";
            // Somewhere to point at, off to one side, gone again once the alert is.
            var camera = p.GetComponentInChildren<Camera>();
            Transform eyes = camera != null ? camera.transform : p.transform;
            bool team = kind == "team";
            var spot = new GameObject("Alert Preview Spot").transform;
            spot.position = eyes.position + (team ? -eyes.right : eyes.right) * 25f + eyes.forward * 8f;
            UnityEngine.Object.Destroy(spot.gameObject, 10f);
            var mate = new Color(.4f, .85f, 1f);
            if (team) AlertHUD.Team("<color=#" + ColorUtility.ToHtmlStringRGB(mate) + ">철수</color> 쓰러짐", spot, mate);
            else AlertHUD.Danger("거구 등장", spot, 6f);
            return team ? "알림: 아군" : "알림: 위험";
        }));
        router.Register("hit", "양 [front/right/back/left] — 방향성 피격 연출 시험 (기본 front)", args => WithPlayer(resolvePlayer, p =>
        {
            string[] parts = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2 || !Amount(parts[0], out float amount))
                return Usage("hit 양 [front/right/back/left]");
            var camera = p.GetComponentInChildren<Camera>();
            if (camera == null) return "플레이어 카메라를 찾지 못했습니다.";
            if (!TryHitSide(parts.Length > 1 ? parts[1] : "front", camera.transform, out Vector3 axis))
                return "사용법: /hit 양 [front/right/back/left]";
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
            return "대상 플레이어를 찾지 못했습니다.";
        return command(player);
    }

    internal static bool Amount(string args, out float amount)
    {
        return float.TryParse(args,NumberStyles.Float,CultureInfo.InvariantCulture,out amount)
            && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount >= 0f;
    }
    private static string Usage(string syntax) => "사용법: /" + syntax + " · 0 이상의 숫자를 입력하세요 (소수점: .).";
    private static bool TryHitSide(string side, Transform view, out Vector3 axis)
    {
        switch (side.ToLowerInvariant())
        {
            case "front": axis = view.forward; return true;
            case "right": axis = view.right; return true;
            case "back": axis = -view.forward; return true;
            case "left": axis = -view.right; return true;
            default: axis = Vector3.zero; return false;
        }
    }
    private static string Number(float value) => value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string Status(PlayerMovement p) => $"HP {Number(p.DamageReceiver.Health)}/{Number(p.DamageReceiver.MaximumHealth)} · 쉴드 {Number(p.DamageReceiver.Shield)} · 궁극기 {Number(p.Ultimate.CurrentCharge)}%/{Number(p.Ultimate.MaximumCharge)}% (1회 소비 {Number(p.Ultimate.RequiredCharge)}%)";
}
