using UnityEngine;

/// <summary>
/// The console commands of the movement and input side (2026-11-18). Each prints or writes what a change there is judged
/// by, so nothing of it has to be found by playing: who holds the player's input, and the probes the movement fixes of
/// that day brought (each added beside its feature).
/// </summary>
public static class MovementCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("input", "입력을 누가 쥐고 있는지 보기 — 화면마다 쥔 것(걷기 Move · 시야 Look · 총 Combat · 포인터 Cursor)과 지금 게임이 받는 입력 (InputHold, InputFocus)", _ => Input()); // dev-text
        router.Register("callouts", "콜아웃 표 점검 — 없는 사실·종류·단계 이름, 말할 줄이 없는 칸, 칸이 없는 줄을 찾고, 지금 내 상황에서 맞는 사실과 휠의 칸마다 나올 줄을 Logs/callouts.txt에 쓴다 (CalloutCheck, Callouts.csv·CalloutSlots.csv)", // dev-text
            _ => Application.isPlaying ? CalloutCheck.Write(PlayerCallouts.Local) : "이 명령어는 Play 중에 사용할 수 있습니다."); // dev-text
        router.Register("ledge", "앞의 턱 점검 — 선 자리에서 발 높이를 0~1.4m로 올려 가며(점프를 흉내) 앞에 잡을 렛지가 있는지, 어느 콜라이더의 윗면인지, 올라설 수 있는지(Stand·Crouch·None), 못 잡으면 왜인지(NoHandRoom = 이음매·덮인 턱)를 보여 주고 Logs/ledge-probe.txt에 쓴다. 플레이 중 잡은 렛지와 거절한 이음매도 그 파일에 적힌다 (LedgeFinder)", _ => Ledge()); // dev-text
        router.Register("lean", "기울이기 점검 — Q/E를 누르지 않아도 기울기를 오른쪽 끝·왼쪽 끝까지 저절로 줬다 풀면서, 두 발목이 디딘 자리에서 얼마나 밀렸는지(1cm 아래면 제자리)와 골반·눈이 옆으로 간 거리, 골반이 내려간 거리를 Logs/lean-probe.txt에 한 줄로 쓴다. 재는 4.5초 동안 움직이거나 시선을 돌리지 않는다. 맨손·주먹·총, 서서·앉아서, 캐릭터마다(마네킹 포함) 한 번씩 (LeanProbe, FootPlanting.Hold)", // dev-text
            _ => Application.isPlaying ? LeanProbe.Run(PlayerRoster.Local) : "이 명령어는 Play 중에 사용할 수 있습니다."); // dev-text
        router.Register("drop", "[높이 m] — 그 높이에서 그냥 떨어지면 어떤 착지인지: 발이 닿는 속도, 착지 단계(가볍게·세게·크게), 서서와 앉아서의 소리와 좀비가 듣는 거리. 비우면 0.5m부터 6m까지 표. 실제로 한 착지는 Logs/landings.txt에 적힌다 (MovementLanding, PlayerMovementAudio.LandingCue)", Drop); // dev-text
    }

    // What the ledge finder says of the wall ahead for a body at this spot with its feet at each height a jump takes them
    // through: nothing is jumped. The same question the game asks every airborne frame (ParkourDetector.TryLedgeAt).
    private static string Ledge()
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다."; // dev-text
        var mine = PlayerRoster.Local;
        if (mine == null || mine.Abilities == null) return "대상 플레이어를 찾지 못했습니다."; // dev-text
        var detector = mine.Abilities.Traversal.Detector;
        Vector3 feet = mine.transform.position, facing = mine.transform.forward;
        var text = new System.Text.StringBuilder("Logs/" + ParkourTraversal.LedgeLog + ".txt"); // dev-text
        string last = null;
        for (float up = 0f; up <= 1.41f; up += .1f)
        {
            bool found = detector.TryLedgeAt(feet + Vector3.up * up, facing, out LedgeTarget ledge, out LedgeFinder.Probe probe);
            string on = probe.On != null ? PlayRecord.PathOf(probe.On.transform) : "-";
            string line = found ? $"FOUND top {probe.Point.y - feet.y:0.00} m over the floor on {on}, climb {ledge.Climb}"
                : probe.Result == LedgeFinder.Why.NoFace ? "NoFace" : $"{probe.Result} (top {probe.Point.y - feet.y:0.00} m over the floor on {on})";
            PlayRecord.Line(ParkourTraversal.LedgeLog, ParkourTraversal.LedgeLogHeading, $"/ledge feet +{up:0.0} m: {line}");
            if (line != last) text.Append($"\n발 +{up:0.0}m: {line}"); // dev-text
            last = line;
        }
        return text.ToString();
    }

    // What a plain drop of a height comes to with the tables as they are now: nothing is dropped. The tuck column shows
    // the same drop with the legs drawn up in the air (the capsule falls further, the feet's speed is the same).
    private static string Drop(string args)
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다."; // dev-text
        var mine = PlayerRoster.Local;
        if (mine == null || mine.Abilities == null || !mine.TryGetComponent(out PlayerMovementAudio audio)) return "대상 플레이어를 찾지 못했습니다."; // dev-text
        var landing = mine.Abilities.Landing;
        float gravity = Mathf.Abs(mine.gravity), tuck = Mathf.Max(0f, mine.StandingColliderHeight - mine.CrouchColliderHeight);
        var text = new System.Text.StringBuilder();
        text.Append($"세게 착지는 {mine.AbilitySettings.hardLandingHeight:0.00}m({landing.HardSpeed:0.0}m/s)부터 · 다치는 낙하는 {mine.AbilitySettings.safeFallSpeed:0.0}m/s·{mine.AbilitySettings.safeFallDistance:0.0}m부터"); // dev-text
        string trimmed = (args ?? "").Trim();
        if (trimmed.Length > 0)
        {
            if (!TableCells.TryNumber(trimmed, out float height) || height < 0f) return "사용법: /drop [높이 m]"; // dev-text
            text.Append('\n').Append(DropLine(mine, audio, height, gravity, tuck));
            return text.ToString();
        }
        for (float height = .5f; height <= 6.01f; height += .5f) text.Append('\n').Append(DropLine(mine, audio, height, gravity, tuck));
        return text.ToString();
    }

    private static string DropLine(PlayerMovement mine, PlayerMovementAudio audio, float height, float gravity, float tuck)
    {
        var landing = mine.Abilities.Landing;
        float feet = MovementLanding.SpeedOfDrop(height, gravity);
        // Tucked, the capsule's bottom falls this much further; judged at the feet it is the same landing.
        float tucked = MovementLanding.FeetImpact(MovementLanding.SpeedOfDrop(height + tuck, gravity), tuck, gravity);
        var tier = landing.TierOf(feet, height);
        string upright = audio.LandingCueFor(tier, feet, false), crouched = audio.LandingCueFor(tier, feet, true);
        return $"{height:0.0}m: 발 {feet:0.0}m/s (다리 접고 {tucked:0.0}) {tier} · 서서 {Heard(upright)} · 앉아서 {Heard(crouched)}"; // dev-text
    }

    // A cue and how far zombies hear it (Tables/SoundCues noise, m); 0 is not heard at all.
    private static string Heard(string cue)
    {
        if (cue == null) return "소리 없음"; // dev-text
        var found = SoundCues.Find(cue, null);
        return found != null ? $"{cue}({found.Noise:0}m)" : cue + "(표에 없음)"; // dev-text
    }

    // Who holds what, and what the three questions answer for this PC's player right now. The console itself holds
    // everything while it is typed into, so its own line is always there: read the others.
    private static string Input()
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다."; // dev-text
        var mine = PlayerRoster.Local;
        return InputHold.Describe()
            + "\n받는 입력: 걷기 " + Word(InputFocus.TakesMove()) + " · 시야 " + Word(InputFocus.TakesLook()) + " · 총 " + Word(InputFocus.TakesCombat()) // dev-text
            + (mine != null ? " · 내 캐릭터 키 " + Word(mine.InputAvailable) : ""); // dev-text
    }

    private static string Word(bool yes) => yes ? "받음" : "막힘"; // dev-text
}
