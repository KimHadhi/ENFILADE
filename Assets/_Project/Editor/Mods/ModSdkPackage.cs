// dev-text: file (the package's read-me and the menu's answers, for whoever makes an avatar mod; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The exporter as a folder a mod maker drops into their own Unity project (their VRChat project, as it is): the
/// exporter's scripts (Editor/ModSdk: the window, and the rules that sort meshes into the wardrobe's slots), the file
/// format they share with the game (ModAvatarFile.cs) and a read-me.
/// Written beside every build, in Mods/_SDK (a folder starting with _ is never read as a mod), so whoever has the game
/// has the exporter that matches it; and from the menu, to hand it out without a build.
/// </summary>
internal static class ModSdkPackage
{
    public const string FolderName = "_SDK", PackageName = "ENFILADE Mod SDK";
    private const string Format = "Assets/_Project/Scripts/Core/Mods/ModAvatarFile.cs", Scripts = "Assets/_Project/Editor/ModSdk";

    [MenuItem("ENFILADE/모드/모드 SDK 폴더 만들기 (제작자에게 줄 것)")]
    private static void WriteFromMenu()
    {
        string folder = Path.Combine(GameMods.Root, FolderName);
        try
        {
            Write(folder);
            EditorUtility.RevealInFinder(Path.Combine(folder, PackageName));
            Debug.Log("[Mods] 모드 SDK를 " + Path.Combine(folder, PackageName) + " 에 만들었습니다. 이 폴더를 통째로 제작자의 Unity 프로젝트 Assets 안에 넣으면 됩니다.");
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[Mods] 모드 SDK 폴더를 만들지 못했습니다: " + error.Message);
        }
    }

    /// <summary>Writes the package as &lt;folder&gt;/ENFILADE Mod SDK, over whatever was there.</summary>
    internal static void Write(string folder)
    {
        CheckSlots();
        string root = Path.Combine(folder, PackageName), editor = Path.Combine(root, "Editor");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(editor);
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        File.Copy(Path.Combine(project, Format), Path.Combine(root, Path.GetFileName(Format)), true);
        foreach (string script in Directory.GetFiles(Path.Combine(project, Scripts), "*.cs"))
            File.Copy(script, Path.Combine(editor, Path.GetFileName(script)), true);
        File.WriteAllText(Path.Combine(root, "읽어 주세요.txt"), ReadMe, new UTF8Encoding(true));
    }

    // The exporter runs where the game's tables aren't, so it carries the wardrobe's slots written down (ModPieces.Slots):
    // said here when that list and OutfitSlots.csv have come apart, each time the package is written out.
    private static void CheckSlots()
    {
        var game = new List<string>();
        foreach (var slot in OutfitCatalog.Slots) game.Add(slot.id);
        var sdk = new List<string>();
        foreach (var (id, _) in ModPieces.Slots) sdk.Add(id);
        if (game.Count == 0 || string.Join("|", game) == string.Join("|", sdk)) return;
        Debug.LogWarning("[Mods] 모드 SDK의 옷 칸 목록(ModPieces.Slots)이 OutfitSlots.csv와 다릅니다. 내보내기 창이 새 칸을 모릅니다: Editor/ModSdk/ModPieces.Words.cs의 Slots를 표와 같은 순서로 맞춰 주세요.\n"
            + "표: " + string.Join(", ", game) + "\nSDK: " + string.Join(", ", sdk));
    }

    private const string ReadMe =
@"ENFILADE 모드 SDK — 내 캐릭터(VRChat 아바타 등)와 옷을 ENFILADE에서 쓰기

캐릭터
1. 이 폴더(ENFILADE Mod SDK)를 통째로 내 Unity 프로젝트의 Assets 안에 넣습니다.
   VRChat 아바타를 올리던 그 프로젝트 그대로면 됩니다(Unity 2022, VRChat SDK, Modular Avatar가 있어도 됩니다).
2. Unity 위쪽 메뉴 ENFILADE → 모드 → 아바타를 모드로 내보내기.
3. 아바타(씬에 놓인 것이나 프리팹)를 넣고, id와 이름을 정하고, '내보낼 곳'에 게임의 Mods 폴더를 고른 뒤 [캐릭터 내보내기].
   - id는 영문 소문자·숫자·-·_ 만. 한번 정하면 바꾸지 마세요.
   - 원본 아바타는 바뀌지 않습니다(사본으로 작업합니다).
   - 창의 '옷 칸' 목록: 아바타가 입고 있는 옷을 이름으로 알아보고 옷장 칸(상의·하의·신발 …)에 나눕니다.
     그렇게 나뉜 옷은 게임에서 칸마다 벗기거나 갈아입힐 수 있습니다. 알아보지 못한 메시는 몸으로 보고 늘 입힌 채 둡니다.
     틀렸거나 빠진 것은 목록의 메뉴에서 칸을 골라 고치면 됩니다.
4. 게임을 켜면(켜져 있으면 ` 키 콘솔에서 /mods reload) 캐릭터 목록에 나옵니다.

옷 (캐릭터와 따로, 옷장에서 골라 입는 옷)
1. Unity 위쪽 메뉴 ENFILADE → 모드 → 옷을 모드로 내보내기 (같은 창의 '옷' 탭).
2. '옷'에 옷 오브젝트를 넣습니다: 아바타 밑에 넣어 둔 옷(Modular Avatar로 입혀 둔 그대로)이나 옷 프리팹.
   '입힐 몸'에는 그 옷이 맞는 아바타를 넣습니다(아바타 밑의 옷을 넣으면 알아서 채워집니다. 없으면 비워 둡니다).
3. '입을 캐릭터 id'에 게임에서 이 옷을 입을 캐릭터의 id를 적습니다.
   - 게임에 든 캐릭터의 id는 게임 콘솔의 /mods ids 가 보여 줍니다(mayo, ichigo …).
   - 내 모드 캐릭터에게 입히려면 그 캐릭터를 내보낼 때 정한 id를 적습니다.
   - 아바타용 옷은 그 아바타의 뼈 이름과 체형에 맞춰 만든 것이라 그 캐릭터에게만 맞습니다.
     같은 옷의 다른 아바타용 프리팹이 있으면, 옷 id는 같게 두고 캐릭터 id만 바꿔 한 번 더 내보내면 두 캐릭터가 다 입습니다.
   - 뼈 없이 통짜로 된, 누구나 쓰는 물건(마스크 등)은 * 를 적습니다.
4. '옷 칸' 목록에서 조각마다 들어갈 칸을 확인하고(재킷 → 겉옷, 치마 → 하의 …) 틀린 것은 메뉴에서 고칩니다.
   같은 칸의 조각은 한 벌로 묶이고, 종류가 다르면(스타킹과 양말) 따로도 고를 수 있게 나옵니다.
   꺼져 있던 조각(VRChat 토글의 다른 쪽)과 몸의 사본은 빠집니다. 넣고 싶으면 칸을 골라 주세요.
5. [옷 내보내기]. 게임을 켜면 그 캐릭터의 옷장 칸에 나와 다른 옷과 섞어 입을 수 있습니다.
   겹치는 곳(양말과 신발, 셔츠와 재킷)은 게임이 메시를 재서 맞춥니다.

무엇이 그대로 가나
- 모델, 본, 셰이프키, 텍스처: 그대로.
- PhysBone(머리카락·꼬리·치마·가슴 흔들림): 설정값을 그대로 적어 가고, 게임이 자기 물리로 바꿉니다.
- 표정: 깜박임과 입 모양은 아바타 디스크립터에서 읽습니다. 나머지 표정은 게임이 흔한 셰이프키 이름으로 찾습니다.
- Modular Avatar로 입힌 옷: 캐릭터는 업로드할 때처럼 합쳐서 내보냅니다(프로젝트에 NDMF가 있을 때).
  옷은 게임이 입힐 때 같은 방식(같은 이름의 뼈에 붙이기)으로 합칩니다. Bone Proxy(머리를 따라가는 모자 등)도 옮겨 갑니다.
- 재질:
  · lilToon → 게임에도 lilToon이 있어서 값이 전부 그대로 갑니다(가장 권장).
  · 그 밖의 쉐이더 → VRChat용 프로젝트에서 컴파일된 쉐이더는 게임(Unity 6, URP)에서 돌릴 수 없어,
    게임이 그림·색을 옮겨 lilToon으로 비슷하게 그립니다.
  · 내 쉐이더를 그대로 쓰고 싶다면: Unity 6 + URP 프로젝트에서 그 쉐이더의 URP판으로 아바타를 꾸미고
    '게임에 없는 쉐이더는 쉐이더째 담기'를 켠 채 내보내세요. 그러면 쉐이더까지 번들에 담겨 그대로 그려집니다.

떼고 가는 것 (게임에서 쓰지 않거나 방해되는 것)
- VRChat·애드온 스크립트, 콜라이더·리지드바디, 조명, 소리, 카메라, 파티클, EditorOnly 오브젝트.

같이 하는 친구도 같은 모드 폴더를 넣어야 내 캐릭터와 옷이 보입니다. 없는 친구 화면에는 기본 캐릭터, 원래 옷으로 보입니다.

고치고 싶을 때
- 만들어진 .avatar.json은 사람이 읽을 수 있는 글입니다. 재질의 숫자·색, 옷이 들어갈 칸(slot)과 이름(name)을 직접 고쳐도 됩니다.
- 흔들림·표정·옷 칸 등 게임의 표(Cloth, FaceKeys, CharacterParts …)에 내 캐릭터 id로 줄을 적은 Tables 폴더를
  같은 모드 폴더에 두면, 게임이 알아서 정한 것 위에 덮어씁니다.
";
}
