using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The wardrobe's pictures baked into the game (the user, 2026-11-12: "미리 구워 놓으면 로딩 속도 더 빨라 지려나").
/// Tools → Characters → 옷장 사진 모두 굽기 photographs every piece the wardrobe shows, for every character, framed as
/// Tables/OutfitPictures.csv says (else whole, from its front), into Resources/UI/OutfitPictures, where OutfitPictures
/// finds them before OutfitIcons would have to take them in play. Baked: each item in its first colour set (its other
/// colours are still taken in play when worn, framed the same) and each character's own pieces in every colour set of
/// the character. A full bake also removes the pictures of pieces that are gone. Its dialog says what one picture cost,
/// which is what each picture not baked costs in play.
/// Besides, it lets the wardrobe's 사진 다시 찍기 write into the project while the editor plays (OutfitPictures.ProjectWriter
/// and FramingWriter), so a picture framed by hand ships, and it imports the pictures as see-through UI textures.
/// </summary>
internal sealed class OutfitPictureBake : AssetPostprocessor
{
    private const string TablePath = "Assets/_Project/Resources/Tables/" + OutfitPictures.TableName + ".csv";
    private const string Header = "id,yaw,pitch,zoom,x,y";
    private const string Description = "\"#설명 (옷장 사진의 구도. 옷마다 한 줄, 그 옷의 모든 색이 같이 쓴다. 줄이 없는 옷은 자동 구도: 옷 전체가 "
        + "사진의 90%를 채우게 앞에서 조금 위(8°)에서 찍는다. id는 item@캐릭터@옷 또는 own@캐릭터@슬롯(캐릭터 원래 옷). "
        + "yaw는 옆으로 돌린 각도(°, +는 옷 앞이 사진 오른쪽을 보게), pitch는 위아래 각도(°, +는 더 위에서 내려다봄), zoom은 "
        + "자동 구도보다 가까운 배율(2는 두 배 크게), x·y는 사진 안에서 옮긴 거리(사진 반 폭이 1, +는 오른쪽·위). 옷장에서 "
        + "'사진 다시 찍기'로 저장하면 이 줄이 써지고(에디터 Play 중), 자동으로 저장하면 지워진다. 바꾼 뒤 Tools → Characters → "
        + "옷장 사진 모두 굽기로 사진을 다시 굽는다)\",,,,,";

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        OutfitPictures.ProjectWriter = WritePicture;
        OutfitPictures.FramingWriter = WriteFraming;
    }

    // ---- The bake ----

    private readonly struct Job
    {
        public readonly PlayerBodyProfile.Skin Skin;
        public readonly OutfitItem Item;
        public readonly string Slot, Colour, Key, Name;

        public Job(PlayerBodyProfile.Skin skin, OutfitItem item, string slot, string colour, string key, string name)
        {
            Skin = skin;
            Item = item;
            Slot = slot;
            Colour = colour;
            Key = key;
            Name = name;
        }
    }

    [MenuItem("Tools/Characters/옷장 사진 모두 굽기")]
    private static void BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("옷장 사진 굽기", "Play를 멈춘 뒤 다시 눌러 주세요. (Play 중에는 옷장의 '사진 다시 찍기'가 옷 하나씩 프로젝트에 저장합니다.)", "확인");
            return;
        }
        var jobs = Jobs();
        if (jobs.Count == 0)
        {
            EditorUtility.DisplayDialog("옷장 사진 굽기", "찍을 옷이 없습니다. 캐릭터 목록(PlayerBody)과 옷 목록(Tools → Characters → 옷 목록 다시 만들기)을 확인해 주세요.", "확인");
            return;
        }

        // Shaders compile as they are needed, so no picture comes out in the placeholder's flat cyan.
        bool wasAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        var scene = EditorSceneManager.NewPreviewScene();
        var studio = new OutfitStudio(scene);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int empty = 0, failed = 0;
        bool cancelled = false;
        // What taking the pictures cost (standing, framing, rendering, reading back: what play does for a picture not
        // baked), apart from writing the files.
        double photos = 0d, worst = 0d;
        string worstKey = null;
        var watch = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (EditorUtility.DisplayCancelableProgressBar("옷장 사진 굽기", $"{job.Skin.DisplayName} · {job.Name}  ({i + 1}/{jobs.Count})", i / (float)jobs.Count))
                {
                    cancelled = true;
                    break;
                }
                var one = Stopwatch.StartNew();
                Texture2D picture = null;
                try
                {
                    if (!studio.Stand(job.Skin, job.Item, job.Colour, job.Slot))
                    {
                        empty++;
                        continue;
                    }
                    studio.Frame(OutfitPictures.FramingOf(OutfitPictures.PieceOf(job.Key)));
                    picture = studio.Capture(OutfitPictures.Size);
                    studio.Clear();
                    one.Stop();
                    photos += one.Elapsed.TotalMilliseconds;
                    if (one.Elapsed.TotalMilliseconds > worst)
                    {
                        worst = one.Elapsed.TotalMilliseconds;
                        worstKey = job.Key;
                    }
                    string asset = AssetOf(OutfitPictures.FileOf(job.Key));
                    if (picture != null && Write(asset, picture.EncodeToPNG())) written.Add(asset);
                    else failed++;
                }
                finally
                {
                    if (picture != null) UnityEngine.Object.DestroyImmediate(picture);
                }
            }
        }
        finally
        {
            studio.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = wasAsync;
            EditorUtility.ClearProgressBar();
        }
        watch.Stop();

        // A whole bake leaves only the pictures of pieces there are: an outfit taken out of the game takes its pictures.
        int removed = 0;
        if (!cancelled && failed == 0)
            foreach (string stale in Existing().Where(path => !written.Contains(path)))
                if (AssetDatabase.DeleteAsset(stale)) removed++;
        AssetDatabase.Refresh();

        int taken = written.Count;
        double each = taken > 0 ? photos / taken : 0d;
        string report = $"옷장 사진 {taken}장을 구웠습니다 ({watch.Elapsed.TotalSeconds:0.0}초. 한 장 찍는 데 평균 {each:0} ms, 가장 오래 {worst:0} ms: {worstKey})."
            + (empty > 0 ? $" 찍을 것이 없는 옷 {empty}개는 사진 없이 둡니다." : "")
            + (failed > 0 ? $" {failed}장은 파일을 쓰지 못했습니다(콘솔 참고)." : "")
            + (removed > 0 ? $" 없어진 옷의 사진 {removed}장을 지웠습니다." : "")
            + (cancelled ? " 중간에 멈췄습니다: 남은 옷은 Play 중에 그 자리에서 찍힙니다." : "")
            + " 구운 사진은 옷장에서 바로 나오고, 굽지 않은 사진은 Play 중에 한 장마다 위의 평균만큼 걸려 찍힙니다.";
        Debug.Log("[OutfitPictures] " + report);
        EditorUtility.DisplayDialog("옷장 사진 굽기", report, "확인");
    }

    // Every picture the wardrobe can show without a colour picked: each character's own pieces in each of its colour
    // sets (OutfitIcons.OfOwn), each item in its first (OutfitIcons.OfItem).
    private static List<Job> Jobs()
    {
        var jobs = new List<Job>();
        var profile = PlayerBodyProfile.Load();
        if (profile == null) return jobs;
        foreach (var skin in profile.Skins)
        {
            if (skin == null || skin.Model == null) continue;
            var variants = skin.Variants.Where(variant => variant != null).Select(variant => variant.Id).ToList();
            if (variants.Count == 0) variants.Add("");
            foreach (var slot in OutfitCatalog.Slots)
            {
                if (OutfitCatalog.HasOwnPiece(skin.Id, slot.id))
                    foreach (string variant in variants)
                        jobs.Add(new Job(skin, null, slot.id, variant, OutfitPictures.OwnKey(skin.Id, slot.id, variant), slot.Name));
                foreach (var item in OutfitCatalog.ItemsFor(skin.Id, slot.id))
                    jobs.Add(new Job(skin, item, slot.id, "", OutfitPictures.ItemKey(skin.Id, item.id, ""), item.DisplayName));
            }
        }
        // An item that goes in more than one slot is one picture.
        return jobs.GroupBy(job => job.Key).Select(group => group.First()).ToList();
    }

    private static IEnumerable<string> Existing()
    {
        if (!Directory.Exists(OutfitPictures.ProjectFolder)) return Enumerable.Empty<string>();
        return Directory.GetFiles(OutfitPictures.ProjectFolder, "*.png", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/'));
    }

    private static string AssetOf(string file) => OutfitPictures.ProjectFolder + "/" + file + ".png";

    // ---- Writing into the project ----

    // A picture taken again in the wardrobe while the editor plays: written and imported at once, so it ships.
    private static bool WritePicture(string resource, byte[] png)
    {
        string asset = "Assets/_Project/Resources/" + resource + ".png";
        if (!Write(asset, png)) return false;
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
        return true;
    }

    // The PNG's bytes in place of the file's. A file another program has mapped can't be cut short (TableFile's 1224):
    // then the new picture is written over the old one's start and the rest left as zeros past its end, which every
    // PNG reader stops before.
    private static bool Write(string asset, byte[] png)
    {
        if (png == null || png.Length == 0) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            AssetDatabase.ReleaseCachedFileHandles();
            try
            {
                File.WriteAllBytes(asset, png);
            }
            catch (IOException)
            {
                using (var file = new FileStream(asset, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    var bytes = new byte[Math.Max(png.Length, (int)file.Length)];
                    Buffer.BlockCopy(png, 0, bytes, 0, png.Length);
                    file.Write(bytes, 0, bytes.Length);
                    try
                    {
                        file.SetLength(png.Length);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            return true;
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[OutfitPictures] {asset}: {problem.Message}");
            return false;
        }
    }

    // A piece's framing row in Tables/OutfitPictures.csv: replaced, added, or (automatic) taken out.
    private static bool WriteFraming(string piece, OutfitFraming? framing)
    {
        string text;
        try
        {
            text = TableFile.Read(TablePath);
        }
        catch (IOException problem)
        {
            Debug.LogWarning($"[OutfitPictures] {TablePath}: {problem.Message}");
            return false;
        }
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        if (lines.Count > 0 && lines[0].Length > 0 && lines[0][0] == '﻿') lines[0] = lines[0].Substring(1);
        if (lines.Count == 0 || !lines[0].StartsWith("id,", StringComparison.Ordinal)) lines.Insert(0, Header);
        if (lines.Count < 2 || !IdOf(lines[1]).StartsWith("#", StringComparison.Ordinal)) lines.Insert(1, Description);
        int found = lines.FindIndex(2, line => IdOf(line) == piece);
        string row = framing.HasValue ? OutfitPictures.Row(piece, framing.Value) : null;
        if (found >= 0)
        {
            if (row == null) lines.RemoveAt(found);
            else lines[found] = row;
        }
        else if (row != null) lines.Add(row);
        else return true;
        var output = new StringBuilder();
        foreach (string line in lines) output.Append(line).Append('\n');
        // TableFile imports the table and reloads GameTables once it lands; a held file is written as soon as it is let go.
        if (!TableFile.Write(TablePath, output.ToString(), piece + " 사진 구도", "옷장의 '사진 다시 찍기'에서 다시 저장해 주세요."))
            Debug.Log($"[OutfitPictures] 다른 프로그램이 {TablePath}를 잡고 있어서, {piece} 구도는 파일이 풀리는 대로 저절로 저장됩니다.");
        return true;
    }

    // A row's id: its first cell, quotes taken off.
    private static string IdOf(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        if (line[0] != '"')
        {
            int comma = line.IndexOf(',');
            return comma < 0 ? line : line.Substring(0, comma);
        }
        var id = new StringBuilder();
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] != '"') id.Append(line[i]);
            else if (i + 1 < line.Length && line[i + 1] == '"') id.Append(line[++i]);
            else break;
        }
        return id.ToString();
    }

    // ---- Importing the pictures ----

    // See-through UI pictures: the colour bled into the clear background (no dark rims where they are scaled down),
    // mipmapped for the plates' smaller size, clamped at their edges.
    private void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(OutfitPictures.ProjectFolder + "/", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
