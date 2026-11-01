using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries what a player says to the other PCs, through the host, with the name they go by. Alone it never spawns, and
/// the chat stays on this screen.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerChat : NetworkBehaviour
{
    private const int LengthLimit = 500;

    /// <summary>Someone else said something: their name, and what they said.</summary>
    public static event Action<string, string> Heard;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Heard = null;

    public void Say(string text)
    {
        if (IsSpawned) SayRpc(GameSettings.Current.DisplayName, text.Length > LengthLimit ? text.Substring(0, LengthLimit) : text);
    }

    [Rpc(SendTo.NotMe)]
    private void SayRpc(string speaker, string text) => Heard?.Invoke(speaker, text);
}
