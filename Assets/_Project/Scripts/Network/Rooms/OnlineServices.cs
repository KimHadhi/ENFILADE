using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Qos;
using UnityEngine;

/// <summary>
/// Connects to Unity's services without any account, so players never see a login. Only internet rooms use this;
/// solo and LAN play never touch it.
/// </summary>
public static class OnlineServices
{
    private static Task signingIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => signingIn = null;

    public static Task SignIn()
    {
        if (signingIn == null || signingIn.IsFaulted || signingIn.IsCanceled) signingIn = RunSignIn();
        return signingIn;
    }

    /// <summary>Round trip in ms from this PC to each relay region, nearest first. Empty when it can't be measured.</summary>
    public static async Task<IList<IQosAnnotatedResult>> MeasureRelayRegions()
    {
        await SignIn();
        try { return await QosService.Instance.GetSortedRelayQosResultsAsync(null); }
        catch (Exception) { return Array.Empty<IQosAnnotatedResult>(); } // only the ping estimate is lost, and rooms still work
    }

    /// <summary>True for what the network or Unity's services can throw, as opposed to a bug.</summary>
    public static bool IsServiceError(Exception e) =>
        e is SessionException || e is RequestFailedException || e is ServicesInitializationException;

    private static async Task RunSignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync(Options());
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private static InitializationOptions Options()
    {
        var options = new InitializationOptions();
#if UNITY_EDITOR
        // Play Mode windows on one PC would otherwise be the same player, and a player can't join their own room.
        string project = new DirectoryInfo(Application.dataPath).Parent.Name;
        options.SetProfile(new string(Regex.Replace(project, "[^A-Za-z0-9_-]", "_").Take(30).ToArray()));
#endif
        return options;
    }
}
