#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

public class DynamicFontSaveProcessor : UnityEditor.AssetModificationProcessor
{
    private static bool _allowSave = false;

    // Ctrl + S를 누를 때 유니티가 디스크에 쓸 파일 목록을 가로채서 필터링합니다.
    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (_allowSave) return paths;

        List<string> pathsToSave = new List<string>(paths.Length);

        foreach (string path in paths)
        {
            if (path.EndsWith(".asset"))
            {
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (assetType != null && typeof(TMP_FontAsset).IsAssignableFrom(assetType))
                {
                    var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (fontAsset != null && fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                    {
                        // Dynamic 폰트는 디스크 저장 목록에서 제외합니다.
                        continue;
                    }
                }
            }
            pathsToSave.Add(path);
        }

        return pathsToSave.ToArray();
    }

    // 나중에 폰트 설정(Fallback 추가 등)을 실제로 변경해서 저장해야 할 때만 이 메뉴를 누르세요.
    [MenuItem("Tools/TextMeshPro/Clean & Save Dynamic Fonts")]
    public static void CleanAndSaveDynamicFonts()
    {
        _allowSave = true;
        try
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            int count = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

                if (fontAsset != null && fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                {
                    fontAsset.ClearFontAssetData(setAtlasSizeToZero: true);
                    EditorUtility.SetDirty(fontAsset);
                    count++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TMP] {count}개의 Dynamic 폰트 아틀라스를 비우고 설정값을 저장했습니다.");
        }
        finally
        {
            _allowSave = false;
        }
    }
}
#endif