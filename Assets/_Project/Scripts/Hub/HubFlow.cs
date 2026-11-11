using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The way through a run (memo 46): the title screen's 혼자 하기 or 방 만들기 opens on the hub, the walkable hideout
/// (Scenes/Hub, built by Tools > World > 허브 로비 짓기); from its map table and ready button the party sets off to a
/// chapter; when the chapter is escaped its results are kept (MatchResults) and everyone comes back to the hub, where
/// the results play over the players themselves (ResultsShow) and the map table already offers the next chapter.
/// In a room the host takes everyone along (GameSession.ChangeMap), so the party stays together. Until the hub's scene
/// is built and in the build, everything runs as it did: the title screen starts the chosen chapter and a chapter goes
/// on to the next.
/// </summary>
public static class HubFlow
{
    public const string SceneName = "Hub";

    /// <summary>The hub's scene is in the build.</summary>
    public static bool IsBuilt => Application.CanStreamedLevelBeLoaded(SceneName);

    /// <summary>This scene is the hub.</summary>
    public static bool InHub => SceneManager.GetActiveScene().name == SceneName;

    /// <summary>Where a room opens and where 혼자 하기 goes: the hub, or the chosen chapter while there is no hub.</summary>
    public static string StartScene => IsBuilt ? SceneName : GameSettings.Current.lastMap;

    /// <summary>The chapter the hub's map table offers: the title screen's pick, moved on as chapters are escaped.</summary>
    public static string Chosen
    {
        get => GameSettings.Current.lastMap;
        set
        {
            if (string.IsNullOrEmpty(value) || GameSettings.Current.lastMap == value) return;
            GameSettings.Current.lastMap = value;
            GameSettings.Save();
        }
    }

    /// <summary>혼자 하기: alone into the hub (or the chosen chapter while there is none).</summary>
    public static void EnterSolo()
    {
        if (GameSession.IsOnline) return;
        GameSettings.Save();
        string scene = StartScene;
        if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
    }

    /// <summary>
    /// A chapter is over and its card has had its time (CampaignRun): the map table will offer the next one (or the first
    /// again once the campaign is done), and the host, or the PC alone, takes everyone back to the hub. False when there is
    /// no hub, so the chapter goes on the old way.
    /// </summary>
    public static bool Return(string next)
    {
        if (!IsBuilt) return false;
        Chosen = !string.IsNullOrEmpty(next) && Application.CanStreamedLevelBeLoaded(next) ? next : FirstChapter();
        if (GameSession.IsOnline) return GameSession.IsGuest || GameSession.ChangeMap(SceneName);
        SceneManager.LoadScene(SceneName);
        return true;
    }

    /// <summary>Sets off from the hub to <paramref name="chapter"/>: the host takes the room, alone it just loads.</summary>
    public static bool Depart(string chapter)
    {
        if (string.IsNullOrEmpty(chapter) || !Application.CanStreamedLevelBeLoaded(chapter)) return false;
        Chosen = chapter;
        if (GameSession.IsOnline) return !GameSession.IsGuest && GameSession.ChangeMap(chapter);
        SceneManager.LoadScene(chapter);
        return true;
    }

    /// <summary>The console's /hub: back to the hub now, the room with the host.</summary>
    public static string Command(string args)
    {
        if (!IsBuilt) return "허브 씬이 아직 없어요. Tools > World > 허브 로비 짓기를 먼저 눌러 주세요.";
        if (InHub) return "이미 허브예요.";
        if (GameSession.IsGuest) return "방장만 방 전체를 허브로 데려갈 수 있어요.";
        if (GameSession.IsOnline) return GameSession.ChangeMap(SceneName) ? "방 전체가 허브로 가요." : "지금은 옮길 수 없어요.";
        SceneManager.LoadScene(SceneName);
        return "허브로 가요.";
    }

    /// <summary>The campaign's first chapter that is built (Tables/CampaignChapters order), or the first map there is.</summary>
    public static string FirstChapter()
    {
        foreach (string id in GameTables.RowIds("CampaignChapters"))
            if (Application.CanStreamedLevelBeLoaded(id)) return id;
        var maps = MapTable.Playable();
        return maps.Length > 0 ? maps[0] : "";
    }
}
