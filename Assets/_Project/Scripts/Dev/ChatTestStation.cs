using UnityEngine;

public sealed class ChatTestStation : MonoBehaviour
{
    [SerializeField] private ChatChannel channel=ChatChannel.Dialogue;
    public string Prompt => "[F] " + ChatStyle.Label(channel) + " 테스트";
    public void Activate(SinglePlayerChatManager chat) => CombatTestCommands.Emit(chat,channel);
}
