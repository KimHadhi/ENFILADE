using UnityEngine;

/// <summary>How sharp textures are drawn (settings 화면 > 그래픽 > 텍스처 품질): as imported, or one, two or three steps
/// down, each step half the width and height (a quarter of the graphics memory). Rows of Tables/TextureDetail.csv by name.</summary>
public enum TextureDetail { Full, High, Medium, Low }

/// <summary>
/// Applies the texture quality chosen in the settings before the first scene, on every change and when its table is
/// edited. QualitySettings.globalTextureMipmapLimit leaves out each mipmapped texture's largest levels on the graphics
/// card: maps, props, characters, zombies, guns and effects, 95% of the texture bytes in the project (2026-11-14 count:
/// 3829 mipmapped textures, 9.2 GB of 9.7 GB if all were loaded). Text, icons and anything else without mipmaps keep
/// their size, as do render textures. The source images and their import settings stay as they are, so any level goes
/// back to the full textures at once. The user asked for it after the 11-14 night's lag ("내가 보기엔 텍스쳐 문제 같은데
/// 최적화. 최대한 줄일 수 있는 대로 줄여 봐"); the same night's records had the graphics card idle most of each long frame
/// (4.4 ms of GPU work in 25-35 ms frames), so this saves memory and loading more than frame time.
/// A level's row may also switch mipmap streaming on with a memory budget. Only textures imported with Streaming
/// Mipmaps take part (592 that night, mostly the BOOTH characters), and pictures taken by a camera in one frame (outfit
/// icons, portraits) could be taken before their sharp levels stream in, so every level leaves it off for now.
/// In the editor the project's own quality values come back when Play stops, so a level chosen in the game never ends
/// up saved into ProjectSettings.
/// </summary>
public static class TextureDetailSettings
{
    public const string TableName = "TextureDetail";
    // A 4096 texture has 13 levels; past 4 steps down (1/16 of the width) nothing would be worth seeing.
    private const int MostSteps = 4;

#if UNITY_EDITOR
    private static bool remembered, startStreaming;
    private static int startLimit;
    private static float startBudget;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
#if UNITY_EDITOR
        if (!remembered)
        {
            remembered = true;
            startLimit = QualitySettings.globalTextureMipmapLimit;
            startStreaming = QualitySettings.streamingMipmapsActive;
            startBudget = QualitySettings.streamingMipmapsMemoryBudget;
            Application.quitting += PutBack;
        }
#endif
        GameSettings.Changed += Apply;
        GameTables.Reloaded -= Apply;
        GameTables.Reloaded += Apply;
        Apply();
    }

    /// <summary>The steps down a level takes (0 = as imported), from its table row.</summary>
    public static int Steps(TextureDetail level)
    {
        int fallback = Mathf.Clamp((int)level, 0, MostSteps);
        return GameTables.TryGetRow(TableName, level.ToString(), out var row) ? Mathf.Clamp(row.Whole("mipmapLimit", fallback), 0, MostSteps) : fallback;
    }

    // Each value is set only when it differs: a new mipmap limit sends every texture to the graphics card again.
    private static void Apply()
    {
        var level = GameSettings.Current.textureDetail;
        int steps = Steps(level);
        bool streaming = false;
        float budget = QualitySettings.streamingMipmapsMemoryBudget;
        if (GameTables.TryGetRow(TableName, level.ToString(), out var row))
        {
            streaming = row.Flag("streaming", false);
            budget = Mathf.Max(64f, row.Number("streamingBudget", budget));
        }
        if (QualitySettings.globalTextureMipmapLimit != steps) QualitySettings.globalTextureMipmapLimit = steps;
        if (QualitySettings.streamingMipmapsActive != streaming) QualitySettings.streamingMipmapsActive = streaming;
        if (streaming && !Mathf.Approximately(QualitySettings.streamingMipmapsMemoryBudget, budget)) QualitySettings.streamingMipmapsMemoryBudget = budget;
    }

#if UNITY_EDITOR
    private static void PutBack()
    {
        Application.quitting -= PutBack;
        GameSettings.Changed -= Apply;
        GameTables.Reloaded -= Apply;
        remembered = false;
        QualitySettings.globalTextureMipmapLimit = startLimit;
        QualitySettings.streamingMipmapsActive = startStreaming;
        QualitySettings.streamingMipmapsMemoryBudget = startBudget;
    }
#endif
}
