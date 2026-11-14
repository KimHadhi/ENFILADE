using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Resources/Characters/ModShaders (ModShaderLibrary) listing every lilToon shader a material can wear, so a build
/// has them all for the avatars players bring: a build takes only the shaders its own materials use, and nearly every
/// BOOTH avatar wears lilToon, each in the variants its maker picked (cut-out, see-through, outline, fur, gem …).
/// Left out: the shaders lilToon's own shaders borrow passes from and its bakers, which no material wears, and its
/// keyword variant (lilToonMulti), which can't be kept whole (ModMaterials sends such a material to the plain variant
/// its settings name). Runs when the editor loads the scripts and from the menu; the asset changes only when the list
/// does (lilToon updated).
/// </summary>
internal static class ModShaderLibraryBuilder
{
    private const string AssetPath = "Assets/_Project/Data/Resources/Characters/ModShaders.asset";
    private const string LilToon = "Packages/jp.lilxyzw.liltoon/Shader";

    [InitializeOnLoadMethod]
    private static void Queue() => EditorApplication.delayCall += () =>
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Refresh();
    };

    [MenuItem("ENFILADE/모드/모드용 쉐이더 목록 다시 만들기")]
    private static void Refresh()
    {
        if (!AssetDatabase.IsValidFolder(LilToon)) return;
        var shaders = new List<Shader>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { LilToon }))
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            if (shader != null && Kept(shader.name)) shaders.Add(shader);
        }
        shaders.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var library = AssetDatabase.LoadAssetAtPath<ModShaderLibrary>(AssetPath);
        if (library == null)
        {
            if (shaders.Count == 0) return;
            library = ScriptableObject.CreateInstance<ModShaderLibrary>();
            library.EditorSet(shaders.ToArray());
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Mods] 모드 아바타가 쓸 lilToon 쉐이더 {shaders.Count}개를 빌드에 넣도록 {AssetPath}를 만들었습니다. 다음 빌드는 이 쉐이더들을 처음 컴파일하느라 더 걸립니다.");
            return;
        }
        if (!library.EditorSet(shaders.ToArray())) return;
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    private static bool Kept(string name) =>
        name.IndexOf("lilToon", StringComparison.Ordinal) >= 0 && name.IndexOf("Multi", StringComparison.Ordinal) < 0;
}
