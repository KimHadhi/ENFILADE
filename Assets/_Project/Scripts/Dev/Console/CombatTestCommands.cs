using System;
using UnityEngine;

public static class CombatTestCommands
{
    /// <summary>The chat is looked up when a command runs: the developer console exists in scenes without one.</summary>
    public static void Register(ChatCommandRouter commands, Func<SinglePlayerChatManager> findChat)
    {
        commands.Register("chattest", "[dialogue / broadcast / team / all / mixed] — 채팅 유형 테스트", args =>
        {
            if(!Application.isPlaying) return "Play 중에 사용할 수 있습니다.";
            var chat=findChat();
            if(chat==null) return "이 씬에는 채팅 창이 없습니다.";
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
