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
    /// <summary>
    /// The player who said or typed it, so the line can show their profile tile and colour; null for system notices,
    /// story dialogue and test lines. Kept as it was when the line came: they may have left since.
    /// </summary>
    public PlayerMovement Who { get; }
    /// <summary>This PC's own player said or typed it.</summary>
    public bool Mine { get; }

    public ChatEntry(ChatChannel channel, string speaker, string body, Color? speakerColor = null, PlayerMovement who = null)
    {
        Channel = channel;
        Speaker = (speaker ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
        Body = body ?? string.Empty;
        Who = who;
        Mine = who != null && who.IsLocal;
        SpeakerColor = speakerColor ?? ChatStyle.SpeakerColor(who, channel);
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

    // The HUD's colours since the 2026-11-12 redesign: the chat channel is the one pink (its input tab), the console a
    // cool grey, a system notice pink on its light card. Dialogue, team and all keep their own hues for their small tags.
    public static Color ColorFor(ChatChannel channel)
    {
        switch (channel)
        {
            case ChatChannel.Note: return KitStyle.Accent;
            case ChatChannel.Console: return new Color32(184, 193, 206, 255);
            case ChatChannel.Dialogue: return new Color32(115, 225, 204, 255);
            case ChatChannel.Broadcast: return KitStyle.Accent;
            case ChatChannel.Team: return new Color32(125, 200, 255, 255);
            default: return new Color32(220, 227, 237, 255);
        }
    }

    /// <summary>
    /// A player's colour in the chat: the HUD's pink for this PC's own, else their seat colour (PlayerTally), the one the
    /// kill feed and the scoreboard use, so a friend's name reads the same everywhere. Without a player, the channel's.
    /// </summary>
    public static Color SpeakerColor(PlayerMovement who, ChatChannel channel)
    {
        if (who == null) return ColorFor(channel);
        if (who.IsLocal) return KitStyle.Accent;
        return who.TryGetComponent(out PlayerTally tally) ? tally.Color : ColorFor(channel);
    }

    /// <summary>
    /// A player's profile picture: this PC's own as chosen in the menu, a friend's once their PC has sent it
    /// (PlayerNetworkPose), else null (their initial on their colour stands in).
    /// </summary>
    public static Texture PictureOf(PlayerMovement who)
    {
        if (who == null) return null;
        if (who.IsLocal) return ProfilePicture.Current;
        return who.TryGetComponent(out PlayerNetworkPose pose) ? pose.Portrait : null;
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
