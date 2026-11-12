/// <summary>
/// Where the project keeps what the game finds by name. Unity loads anything under a folder called Resources by its
/// path from there, so each kind of thing has one such drawer inside its own top folder: pictures, sounds, shaders and
/// clips in Art, tables and lists in Data, assembled objects in Prefabs. The path from the drawer is the name the game
/// asks for ("UI/Icons/Fist"), whichever drawer it lies in. Editor tools that write such files take the drawer from
/// here and never spell the folder themselves, so the layout can change in this one place.
/// </summary>
public static class ProjectFolders
{
    public const string Root = "Assets/_Project/";
    /// <summary>Pictures, sounds, shaders, materials and clips found by name.</summary>
    public const string ArtByName = Root + "Art/Resources/";
    /// <summary>Tables and lists (ScriptableObject libraries, sound banks, JSON) found by name.</summary>
    public const string DataByName = Root + "Data/Resources/";
    /// <summary>Prefabs found by name.</summary>
    public const string PrefabsByName = Root + "Prefabs/Resources/";
    /// <summary>Guns' own data, wired by reference: weapons, fitted prefabs, baked clips.</summary>
    public const string Guns = Root + "Data/Guns/";

    /// <summary>A drawer as a folder path without the closing slash, the way AssetDatabase wants a folder.</summary>
    public static string Folder(string drawer) => drawer.TrimEnd('/');
}
