using UnityEngine;

public static class CombatTestCommands
{
    public static void Register(ChatCommandRouter commands, SinglePlayerChatManager chat)
    {
        commands.Register("wave", "start / stop(pause) / resume / abort / status — 웨이브 제어", args =>
        {
            string action=args.Trim().ToLowerInvariant();
            if(action!="start"&&action!="stop"&&action!="pause"&&action!="resume"&&action!="abort"&&action!="status")
                return "사용법: /wave start | stop (일시정지) | resume (재개) | abort (적 정리 후 초기화) | status";
            if(!Application.isPlaying) return "Play 중에 사용할 수 있습니다.";
            WaveManager wave=null; int count=0;
            foreach(var candidate in Object.FindObjectsByType<WaveManager>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                if(candidate.gameObject.scene==chat.gameObject.scene && candidate.isActiveAndEnabled) {wave=candidate;count++;}
            if(count!=1) return "이 씬에 활성 WaveManager가 하나 있어야 합니다.";
            switch(action)
            {
                case "start": return wave.StartWaves() ? "웨이브를 시작합니다." : wave.IsRunning ? "이미 실행 중입니다. 일시정지 상태라면 /wave resume을 사용하세요." : wave.LastError ?? "웨이브를 시작할 수 없습니다.";
                case "stop": case "pause": return wave.PauseWaves() ? "적의 이동·공격과 웨이브 타이머를 일시정지했습니다." : "실행 중인 웨이브가 없거나 이미 일시정지 상태입니다.";
                case "resume": return wave.ResumeWaves() ? "웨이브를 재개했습니다." : "일시정지된 웨이브가 없습니다.";
                case "abort": wave.AbortWaves(); return "웨이브를 완전히 중단했습니다. 생성된 적을 정리했고 다음 start는 1웨이브부터 시작합니다.";
                default: return $"웨이브 {wave.CurrentWave} · {(wave.IsPaused ? "일시정지" : wave.Phase.ToString())} · 남은 적 {wave.AliveCount} · 대기 {wave.RemainingDelay:0.0}초";
            }
        });
        commands.Register("chattest", "[dialogue / broadcast / team / all / mixed] — 채팅 유형 테스트", args =>
        {
            if(!Application.isPlaying) return "Play 중에 사용할 수 있습니다.";
            switch(args.Trim().ToLowerInvariant())
            {
                case "dialogue": Emit(chat,ChatChannel.Dialogue); break;
                case "broadcast": Emit(chat,ChatChannel.Broadcast); break;
                case "team": Emit(chat,ChatChannel.Team); break;
                case "all": Emit(chat,ChatChannel.All); break;
                case "": case "mixed":
                    foreach(var channel in new[]{ChatChannel.Dialogue,ChatChannel.Broadcast,ChatChannel.Team,ChatChannel.All}) Emit(chat,channel);
                    break;
                default: return "사용법: /chattest dialogue | broadcast | team | all | mixed";
            }
            return null;
        });
    }

    public static void Emit(SinglePlayerChatManager chat,ChatChannel channel)
    {
        if(chat==null) return;
        switch(channel)
        {
            case ChatChannel.Dialogue:
                chat.AddDialogue("Alex","전방을 확인해 줘. 방금 놓친 대사도 채팅 기록에서 다시 볼 수 있어.",new Color(.95f,.65f,.4f));
                chat.AddDialogue("Mira","확인했어. 준비되면 웨이브를 시작하자.",new Color(.45f,.9f,.8f)); break;
            case ChatChannel.Broadcast: chat.AddBroadcast("[테스트] 새로운 목표가 등록되었습니다: 주변을 정찰하세요."); break;
            case ChatChannel.Team: chat.AddMessage(ChatChannel.Team,"지원","[테스트] 오른쪽 통로를 확인할게."); break;
            case ChatChannel.All: chat.AddMessage(ChatChannel.All,"관측자","[테스트] 전체 채널 메시지가 정상적으로 표시됩니다."); break;
        }
    }
}
