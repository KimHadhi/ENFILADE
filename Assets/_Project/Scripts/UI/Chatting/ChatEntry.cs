using System;
using System.Text.RegularExpressions;
using UnityEngine;

public enum ChatChannel { [InspectorName("Chat")] Note, Console, Dialogue, Broadcast, Team, All }

/// <summary>Message data is independent of its prefab and presentation.</summary>
[Serializable]
public sealed class ChatEntry
{
    public ChatChannel Channel { get; }
    public string Speaker { get; }
    public string Body { get; }
    public DateTime CreatedAt { get; }
    public Color SpeakerColor { get; }

    public ChatEntry(ChatChannel channel, string speaker, string body, Color? speakerColor = null)
    {
        Channel = channel;
        Speaker = (speaker ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
        Body = body ?? string.Empty;
        SpeakerColor = speakerColor ?? ChatStyle.ColorFor(channel);
        CreatedAt = DateTime.Now;
    }
}

public static class ChatStyle
{
    public static string Label(ChatChannel channel)
    {
        switch (channel)
        {
            case ChatChannel.Note: return Texts.Get("chat.channel.chat");
            case ChatChannel.Console: return Texts.Get("chat.channel.console");
            case ChatChannel.Dialogue: return Texts.Get("chat.channel.dialogue");
            case ChatChannel.Broadcast: return Texts.Get("chat.channel.broadcast");
            case ChatChannel.Team: return Texts.Get("chat.channel.team");
            default: return Texts.Get("chat.channel.all");
        }
    }

    public static Color ColorFor(ChatChannel channel)
    {
        switch (channel)
        {
            case ChatChannel.Note: return new Color32(255, 139, 174, 255);
            case ChatChannel.Console: return new Color32(173, 190, 218, 255);
            case ChatChannel.Dialogue: return new Color32(115, 225, 204, 255);
            case ChatChannel.Broadcast: return new Color32(245, 205, 119, 255);
            case ChatChannel.Team: return new Color32(125, 200, 255, 255);
            default: return new Color32(220, 227, 237, 255);
        }
    }

    // User text cannot close the noparse span and inject TMP formatting.
    public static string Literal(string text) => "<noparse>" + Regex.Replace(text ?? string.Empty,
        "</noparse", "</nopar\u200Bse", RegexOptions.IgnoreCase) + "</noparse>";

    public static string Format(ChatEntry entry)
    {
        string channel = ColorUtility.ToHtmlStringRGB(ColorFor(entry.Channel));
        string speaker = ColorUtility.ToHtmlStringRGB(entry.SpeakerColor);
        string who = string.IsNullOrWhiteSpace(entry.Speaker) ? "" : $" <color=#{speaker}>{Literal(entry.Speaker)}:</color>";
        return $"<color=#{channel}>[{Label(entry.Channel)}]</color>{who} <color=#EBEEF4>{Literal(entry.Body)}</color>";
    }
}
