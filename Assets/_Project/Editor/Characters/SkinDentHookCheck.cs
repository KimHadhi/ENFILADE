using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The friends' poke dent (SkinDent.hlsl, SkinPoke) rides lines our project adds to two of lilToon's files in
/// Shader/Includes (marked ENFILADE: lil_common_vert.hlsl for every pass, lil_pass_forward_fakeshadow.hlsl for the hair's
/// fake shadow on a face). Updating lilToon puts its own files back and drops them, and then nothing says so: the
/// fingertips press and the sounds play, but no skin gives. So each time the scripts load, the editor looks for them once
/// and warns when they are gone (how to put them back: Docs/Features/Poke.md).
/// </summary>
[InitializeOnLoad]
public static class SkinDentHookCheck
{
    private const string Folder = "Packages/jp.lilxyzw.liltoon/Shader/Includes/";
    private static readonly string[] Hooked = { "lil_common_vert.hlsl", "lil_pass_forward_fakeshadow.hlsl" };

    static SkinDentHookCheck() => EditorApplication.delayCall += Check;

    private static void Check()
    {
        foreach (string file in Hooked)
        {
            string path = Path.GetFullPath(Folder + file);
            if (!File.Exists(path)) continue;
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }
            if (text.Contains("Art/Shaders/SkinDent.hlsl") && text.Contains("EnfiladeSkinDent(")) continue;
            Debug.LogWarning($"[SkinDent] lilToon's {file} has lost the ENFILADE lines (lilToon updated?): friends' pokes " +
                "won't dent the skin. Put the marked blocks back (Docs/Features/Poke.md).");
        }
    }
}
