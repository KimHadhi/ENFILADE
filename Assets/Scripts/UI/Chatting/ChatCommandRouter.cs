using System;
using System.Collections.Generic;

/// <summary>Only explicitly registered game commands execute. No OS or code evaluation.</summary>
public sealed class ChatCommandRouter
{
    private sealed class Command { public string Help; public Func<string, string> Run; }
    private readonly SortedDictionary<string, Command> commands = new SortedDictionary<string, Command>(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, string help, Func<string, string> handler)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { ' ', '/', '\t', '\n', '\r' }) >= 0 || handler == null)
            throw new ArgumentException("Command names must be a single word, without slash.");
        commands[name] = new Command { Help = help, Run = handler };
    }

    public bool Unregister(string name) => commands.Remove(name);
    public string Help()
    {
        var lines = new List<string>();
        foreach (var pair in commands) lines.Add("/" + pair.Key + " — " + pair.Value.Help);
        return string.Join("\n", lines);
    }

    public string Execute(string input)
    {
        string text = (input ?? "").Trim();
        if (text.StartsWith("/")) text = text.Substring(1);
        int split = text.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
        string name = split < 0 ? text : text.Substring(0, split);
        string args = split < 0 ? "" : text.Substring(split + 1).Trim();
        if (!commands.TryGetValue(name, out var command)) return "알 수 없는 명령어입니다. /help로 목록을 확인하세요.";
        try { return command.Run(args); }
        catch (Exception) { return "명령어 실행에 실패했습니다: /" + name; }
    }
}
