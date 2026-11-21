#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The bench's heels (check=heels): every character as it comes, with its shoes off, with its legs bare as well, and in
/// each pair of shoes the wardrobe offers it, sized and dressed as a player's body is (PlayerBody.Build), then its feet
/// read by the game's own rule (DrawnHeels) and told as the game would sound them (Player.csv Body.heelsFrom). Each
/// foot's line says how far its back and its front reach down and how high its middle stands, so a look read wrong shows
/// why. Logs/WardrobeBench/heels.txt; numbers in cm at the size the game draws the character. Unless told pictures=no, each
/// look's right foot is photographed from its outer side and from the front-right, low (heels/&lt;character&gt;.png, a row to a
/// look), so what was measured can be looked at.
/// </summary>
internal static partial class WardrobeBench
{
    private const string HeelsPath = Folder + "/heels.txt", HeelPictures = Folder + "/heels";

    private static void RunHeels(Ask ask, PlayerBodyProfile profile)
    {
        var report = new Report();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float camera = PlayerBody.StandingCamera();
        float from = GameTables.Number(PlayerMovement.TableName, "Body", "heelsFrom", .03f);
        int dressed = 0;
        Stage stage = null;
        try
        {
            if (ask.Pictures)
            {
                stage = new Stage();
                Directory.CreateDirectory(HeelPictures);
            }
            int done = 0;
            foreach (var skin in profile.Skins)
            {
                done++;
                if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                var model = profile.ModelOf(skin);
                if (model == null) continue;
                EditorUtility.DisplayProgressBar("ENFILADE: 옷장 검사대", skin.Id + " 굽", done / (float)Mathf.Max(1, profile.Skins.Count));
                report.Line("");
                report.Line("== " + skin.Id);
                var tiles = new List<Texture2D[]>();
                var rows = new List<string>();
                foreach (var (what, look) in ShoeLooks(skin.Id, ask))
                {
                    int before = tiles.Count;
                    dressed += Heels(report, stage, skin, model, what, look, camera, from, tiles, ask.Width);
                    if (tiles.Count > before) rows.Add(what);
                }
                if (tiles.Count > 0)
                {
                    string sheet = HeelPictures + "/" + skin.Id + ".png";
                    SaveSheet(sheet, tiles, ask.Width);
                    report.Line($"   pictures: {sheet}, columns outer side · front-right low, rows from the top: {string.Join(" | ", rows)}");
                }
            }
        }
        catch (Exception problem)
        {
            report.Failed++;
            report.Line("  FAIL the bench stopped: " + problem);
            Debug.LogException(problem);
        }
        finally
        {
            stage?.Dispose();
            EditorUtility.ClearProgressBar();
        }
        Write(HeelsPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} wardrobe bench, heels: {dressed} looks read"
            + (report.Failed == 0 ? "" : $", {report.Failed} could not be") + $"  ({watch.Elapsed.TotalSeconds:0.0} s)\n"
            + $"Heels from {from * 100f:0.0} cm sound like heels (Player.csv Body.heelsFrom). A heel is a foot whose back (the heel's tip)\n"
            + $"and front (the ball) both reach the floor while its middle stands clear of it (DrawnHeels): both ends within\n"
            + "35% of the middle's height. Per foot: back, front = how far over the foot's lowest point they reach down;\n"
            + "middle = how high the sole stands between them; length along the foot; drawn points read.\n"
            + report.Text);
        Debug.Log($"WardrobeBench: {dressed} looks' heels read; {HeelsPath}.");
    }

    // The looks to read for one character: as it comes, its shoes off, its legs bare too, then each pair of shoes alone.
    private static IEnumerable<(string What, OutfitLook Look)> ShoeLooks(string skinId, Ask ask)
    {
        if (ask.Models == null)
        {
            yield return ("as it comes", new OutfitLook());
            var shoesOff = new OutfitLook();
            shoesOff.Set("shoes", OutfitLook.Off);
            yield return ("shoes off", shoesOff);
            var bare = new OutfitLook();
            bare.Set("shoes", OutfitLook.Off);
            bare.Set("legs", OutfitLook.Off);
            yield return ("shoes and legs off", bare);
        }
        foreach (var item in OutfitCatalog.ItemsFor(skinId, "shoes"))
        {
            if (ask.Models != null && !ask.Models.Contains(item.ModelId)) continue;
            var look = new OutfitLook();
            look.Set("shoes", item.id);
            yield return ("in " + item.id, look);
        }
    }

    // One look dressed as a player's body is, at its size, its feet read and, on a stage, its right foot photographed.
    // Returns 1 when it was read.
    private static int Heels(Report report, Stage stage, PlayerBodyProfile.Skin skin, GameObject model, string what, OutfitLook look, float camera,
        float from, List<Texture2D[]> tiles, int width)
    {
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                report.Line($"  {what}: no humanoid Animator, nothing to read");
                return 0;
            }
            animator.applyRootMotion = false;
            // Sized on the soles it comes in, then dressed, as PlayerBody.Build does.
            float scale = PlayerBody.DrawnScaleOf(animator, instance.transform, camera);
            instance.transform.localScale = Vector3.one * scale;
            OutfitDresser.Dress(instance, model, skin, look);
            var (left, right) = DrawnHeels.Measure(instance.transform, animator);
            float heel = DrawnHeels.Of(left, right) * scale;
            report.Looked++;
            report.Line($"  {what,-26} heel {heel * 100f,5:0.0} cm -> {DrawnHeels.FootwearOf(heel, from) ?? "ordinary steps"}");
            report.Line("      left  " + FootLine(left, scale));
            report.Line("      right " + FootLine(right, scale));
            if (stage != null) tiles.Add(FootViews(stage, instance, animator, scale, width));
            return 1;
        }
        catch (Exception problem)
        {
            report.Expect(false, skin.Id + " " + what, "dressing or reading stopped: " + problem.Message);
            return 0;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // The right foot from its outer side and from the front-right, low, at the size the game draws it: 40 cm across, the
    // floor near the bottom of the picture.
    private static Texture2D[] FootViews(Stage stage, GameObject instance, Animator animator, float scale, int width)
    {
        stage.Take(instance);
        var foot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var toes = animator.GetBoneTransform(HumanBodyBones.RightToes);
        Vector3 ahead = toes != null ? Vector3.ProjectOnPlane(toes.position - foot.position, Vector3.up) : instance.transform.forward;
        ahead = ahead.sqrMagnitude > 1e-8f ? ahead.normalized : Vector3.forward;
        Vector3 outward = Vector3.Cross(Vector3.up, ahead);
        float floor = instance.transform.position.y + DrawnSole.Of(instance.transform) * scale;
        Vector3 middle = new Vector3(foot.position.x, floor + .15f, foot.position.z) + ahead * .04f;
        Vector3 aside = (outward + ahead * .6f).normalized;
        return new[]
        {
            stage.Look(middle + outward * 2f, middle, .2f, width),
            stage.Look(middle + aside * 2f + Vector3.up * .25f, middle, .2f, width),
        };
    }

    private static string FootLine(DrawnHeels.Foot foot, float scale)
    {
        if (!foot.Read) return "not read (too little drawn on the foot)";
        float cm = scale * 100f;
        return $"heel {foot.Heel * cm:0.0} | back {foot.Back * cm:0.0} front {foot.Front * cm:0.0} middle {foot.Middle * cm:0.0} | length {foot.Length * cm:0.0} cm, {foot.Points} points";
    }
}
#endif
