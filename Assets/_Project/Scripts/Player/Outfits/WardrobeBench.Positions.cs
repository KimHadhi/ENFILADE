#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// WardrobeBench: where pieces are worn (WearPosition). One part of the class; the rest is in WardrobeBench.cs.
internal static partial class WardrobeBench
{
    private const string PositionsPath = Folder + "/positions.txt", PicturesFolder = Folder + "/positions";
    private static readonly Color Backdrop = new Color(.32f, .34f, .38f), Ambient = new Color(.5f, .52f, .56f);

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

    // ---- What a look says of where a piece is worn, as words (no model is made: runs after every script reload) ----

    private static void LookRules(Report report)
    {
        // A nudge as text.
        var old = ItemAdjust.Read("0.012_-0.004_1.1");
        report.Expect(Near(old.Up, .012f) && Near(old.Ahead, -.004f) && Near(old.Size, 1.1f) && !old.Moves && ItemAdjust.Write(old) == "0.012_-0.004_1.1",
            "a nudge saved before pieces had positions (up_ahead_size) reads as it did and is written the same",
            $"read up {old.Up} ahead {old.Ahead} size {old.Size}, written '{ItemAdjust.Write(old)}'");
        var whole = new ItemAdjust.Values(.01f, -.02f, .03f, 1.2f, 15f, -30f, 45f);
        string text = ItemAdjust.Write(whole);
        var back = ItemAdjust.Read(text);
        bool same = true;
        for (int axis = 0; axis < 7; axis++) same &= Mathf.Abs(back[axis] - whole[axis]) < 1e-3f;
        report.Expect(same, "a nudge with every handle moved reads back as written", $"'{text}' read back as '{ItemAdjust.Write(back)}'");
        report.Expect(ItemAdjust.Read("garbage").IsNone && ItemAdjust.Read("1_2").IsNone && ItemAdjust.Read("").IsNone
            && ItemAdjust.Write(ItemAdjust.Values.None).Length == 0, "text that is no nudge is none, and none is written as nothing", "it read as a nudge");
        var far = ItemAdjust.Read("99_99_99_99_999_999_999");
        report.Expect(far.Right <= ItemAdjust.MoveRange + 1e-4f && far.Size <= ItemAdjust.MostSize + 1e-4f && far.Yaw <= ItemAdjust.TurnRange + 1e-3f,
            "a nudge is kept inside the handles' reach (ItemAdjust.csv)", $"99 read as right {far.Right}, size {far.Size}, yaw {far.Yaw}");

        // The table's rows.
        int rows = 0;
        foreach (string id in GameTables.RowIds(WearPosition.TableName))
        {
            var position = OutfitCatalog.Position(id);
            if (position == null) continue;
            rows++;
            report.Expect(position.FitsFace || position.AtEyes || position.anchor.Length == 0 || position.TryBone(out _),
                $"position '{id}' hangs from something a body has", $"its anchor '{position.anchor}' is neither eyes nor a humanoid bone's name");
            report.Expect(position.scale > 0f, $"position '{id}' has a size", "scale " + position.scale);
            report.Expect(position.name.Length == 0 || Texts.Has(position.name) || position.name.IndexOf('.') < 0,
                $"position '{id}' has a name to show", $"Texts.csv has no '{position.name}'");
        }
        report.Expect(rows > 0, "WearPositions.csv has rows", "the table is missing or empty: no piece can be worn another way");

        // Every position an item or a slot names is a row; an item that took another item in as a position takes its looks in.
        OutfitItem twoWay = null;
        foreach (string id in GameTables.RowIds(OutfitItem.TableName))
        {
            var item = OutfitCatalog.Item(id);
            if (item == null) continue;
            foreach (string named in item.PositionIds)
                report.Expect(OutfitCatalog.Position(named) != null, $"{id} can be worn '{named}'", "WearPositions.csv has no such row");
            if (twoWay == null && OutfitCatalog.PositionsOf(item).Count > 1) twoWay = item;
            foreach (string was in item.WasIds)
                if (item.PositionWas(was) is string there) Migrates(report, item, was, there);
        }
        foreach (var slot in OutfitCatalog.Slots)
            foreach (string named in OutfitCatalog.Split(slot.positions))
                report.Expect(OutfitCatalog.Position(named) != null, $"slot {slot.id}'s pieces can be worn '{named}'", "WearPositions.csv has no such row");

        if (twoWay == null)
        {
            report.Line("  (no item offers two positions: the choice itself was not checked)");
            return;
        }
        // Choosing a position.
        var offered = OutfitCatalog.PositionsOf(twoWay);
        var look = new OutfitLook();
        bool unsaid = WearPosition.Worn(look, twoWay) == offered[0];
        WearPosition.Wear(look, twoWay, offered[1]);
        bool chosen = WearPosition.Worn(look, twoWay) == offered[1] && look.TryGet(WearPosition.Prefix + twoWay.id, out _);
        string second = ItemAdjust.Key(twoWay, offered[1]), first = ItemAdjust.Key(twoWay, offered[0]);
        WearPosition.Wear(look, twoWay, offered[0]);
        bool backToFirst = WearPosition.Worn(look, twoWay) == offered[0] && !look.TryGet(WearPosition.Prefix + twoWay.id, out _);
        look.Set(WearPosition.Prefix + twoWay.id, "no-such-position");
        bool unknown = WearPosition.Worn(look, twoWay) == offered[0];
        report.Expect(unsaid && chosen && backToFirst && unknown, $"{twoWay.id}: worn its first position unsaid, another when the look says so, the first again for one the table lost",
            $"unsaid {unsaid}, chosen {chosen}, back {backToFirst}, unknown {unknown}");
        report.Expect(first == ItemAdjust.Prefix + twoWay.id && second == ItemAdjust.Prefix + twoWay.id + ItemAdjust.At + offered[1].id,
            $"{twoWay.id}: each position keeps a nudge of its own", $"keys '{first}' and '{second}'");

        // A preset keeps all of it.
        var profile = PlayerBodyProfile.Load();
        PlayerBodyProfile.Skin wearer = null;
        if (profile != null)
            foreach (var skin in profile.Skins)
                if (skin != null && OutfitCatalog.Model(twoWay.id, skin.Id)?.HasModel == true) { wearer = skin; break; }
        if (wearer == null)
        {
            report.Line($"  (no character can wear {twoWay.id}: the preset was not checked)");
            return;
        }
        string saved = $"{twoWay.slot}={twoWay.id};{WearPosition.Prefix}{twoWay.id}={offered[1].id};{second}=0.01_0_1.1;{BodyShapes.Prefix}bench=1";
        var wearable = OutfitPresets.Wearable(wearer, saved, out int emptied);
        report.Expect(emptied == 0 && wearable.ToString() == OutfitLook.Parse(saved).ToString(),
            "a preset keeps where a piece is worn and how it was moved there, and counts none of it as an item lost",
            $"'{OutfitLook.Parse(saved)}' became '{wearable}', {emptied} counted as lost");
    }

    // A look saved with a retired item that was this item worn another way: it wears this item there, in the same colour,
    // and the nudge kept for the old item goes with it; the item's own nudge stays; settling twice changes nothing more.
    private static void Migrates(Report report, OutfitItem item, string was, string there)
    {
        var position = OutfitCatalog.Position(there);
        var offered = OutfitCatalog.PositionsOf(item);
        bool offers = false;
        foreach (var each in offered) offers |= each == position;
        report.Expect(position != null && offers, $"{was} was {item.id} worn '{there}'", "the item does not offer that position");
        if (position == null || !offers) return;
        bool isFirst = offered[0] == position;
        var saved = OutfitLook.Parse($"{item.slot}={was}#some;{ItemAdjust.Prefix}{was}=0.012_0.004_1.1" + (isFirst ? "" : $";{ItemAdjust.Prefix}{item.id}=-0.01_0_0.9"));
        var now = saved.Settled();
        bool wears = now.TryGet(item.slot, out var pick) && pick.Item == item.id && pick.Colour == "some";
        bool worn = WearPosition.Worn(now, item) == position;
        var nudge = ItemAdjust.Of(now, item, position);
        bool moved = Near(nudge.Up, .012f) && Near(nudge.Ahead, .004f) && Near(nudge.Size, 1.1f) && !now.TryGet(ItemAdjust.Prefix + was, out _);
        var own = ItemAdjust.Of(now, item, offered[0]);
        bool kept = isFirst || (Near(own.Up, -.01f) && Near(own.Size, .9f));
        report.Expect(wears && worn && moved && kept, $"a look saved wearing {was} wears {item.id} '{there}', same colour, both nudges kept",
            $"'{saved}' became '{now}' (wears {wears}, there {worn}, its nudge moved {moved}, the item's own kept {kept})");
        report.Expect(now.Settled().ToString() == now.ToString(), $"the look that wore {was} settles once", $"'{now}' settled again into '{now.Settled()}'");
        var idle = OutfitLook.Parse($"{ItemAdjust.Prefix}{was}=0.02_0_1").Settled();
        report.Expect(Near(ItemAdjust.Of(idle, item, position).Up, .02f) && !idle.TryGet(ItemAdjust.Prefix + was, out _),
            $"a nudge kept for {was} while it was not worn goes to {item.id} '{there}' too", $"became '{idle}'");
    }

    // ---- Every way of wearing every hung piece on every character ----

    private static void RunPositions(Ask ask, PlayerBodyProfile profile)
    {
        var report = new Report();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int dressed = 0, sheets = 0;
        Stage stage = null;
        try
        {
            report.Line("== what a look says of where a piece is worn, as words");
            LookRules(report);
            report.Line("");
            report.Line("== each hung piece (a model made for anyone) worn each way on each character, no nudge. A piece's spot (a mask's glass, an");
            report.Line("   earring's piercing point) is given from the middle of the eyes in eye spacings along the body's right, up and ahead.");
            if (ask.Pictures)
            {
                stage = new Stage();
                Directory.CreateDirectory(PicturesFolder);
            }
            int done = 0;
            foreach (var skin in profile.Skins)
            {
                done++;
                if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                var model = profile.ModelOf(skin);
                if (model == null) continue;
                EditorUtility.DisplayProgressBar("ENFILADE: 옷장 검사대", skin.Id + " 착용 위치", done / (float)Mathf.Max(1, profile.Skins.Count));
                var tiles = new List<Texture2D[]>();
                var rowsOf = new List<string>();
                report.Line("");
                report.Line("== " + skin.Id);
                foreach (var slot in OutfitCatalog.Slots)
                    foreach (var item in OutfitCatalog.ItemsFor(skin.Id, slot.id))
                    {
                        if (!ItemAdjust.For(item, skin.Id) || (ask.Models != null && !ask.Models.Contains(item.ModelId))) continue;
                        var offered = OutfitCatalog.PositionsOf(item);
                        var ways = offered.Count > 0 ? new List<WearPosition>(offered) : new List<WearPosition> { WearPosition.Plain };
                        foreach (var position in ways)
                        {
                            int before = tiles.Count;
                            dressed += Worn(report, stage, ask, skin, model, slot, item, position, tiles);
                            if (tiles.Count > before) rowsOf.Add(item.id + " " + (position == WearPosition.Plain ? "(no position)" : position.id));
                        }
                    }
                if (tiles.Count > 0)
                {
                    string sheet = PicturesFolder + "/" + skin.Id + ".png";
                    SaveSheet(sheet, tiles, ask.Width);
                    sheets++;
                    report.Line($"   pictures: {sheet}, columns front · right side · back, rows from the top: {string.Join(" | ", rowsOf)}");
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
            EditorUtility.ClearProgressBar();
            stage?.Dispose();
        }
        Write(PositionsPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} wardrobe bench, wear positions: {dressed} dressings, {sheets} picture sheets, {report.Looked} things checked: "
            + (report.Failed == 0 ? "ALL KEPT" : report.Failed + " BROKEN") + $"  ({watch.Elapsed.TotalSeconds:0.0} s)\n\n" + report.Text);
        if (report.Failed > 0) Debug.LogWarning($"WardrobeBench: {report.Failed} position checks broken, see {PositionsPath}.");
        else Debug.Log($"WardrobeBench: {dressed} ways of wearing stood, {sheets} picture sheets, all kept; {PositionsPath}.");
    }

    // One character wearing one piece one way, as the game dresses it: where the piece's spot went, what it follows,
    // and whether that is what the position says; then its three pictures. Returns 1 when it was dressed.
    private static int Worn(Report report, Stage stage, Ask ask, PlayerBodyProfile.Skin skin, GameObject model, OutfitSlot slot, OutfitItem item,
        WearPosition position, List<Texture2D[]> tiles)
    {
        string way = position == WearPosition.Plain ? "(no position)" : position.id;
        string what = $"{skin.Id} · {item.id} · {way}";
        var look = new OutfitLook();
        look.Set(slot.id, item.id);
        WearPosition.Wear(look, item, position);
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // What the character shows as it comes, to tell afterwards what wearing the piece took out of sight.
            var shown = new List<Renderer>();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy) shown.Add(renderer);
            var dressing = OutfitDresser.Dress(instance, model, skin, look);
            var animator = instance.GetComponentInChildren<Animator>();
            var record = ItemAdjust.On(instance, item.id);
            if (!dressing.Pieces.ContainsKey(item.id) || record == null || record.PieceCount == 0)
            {
                report.Expect(false, what, "the piece was not hung (see the Console's [Outfits] lines)");
                return 1;
            }
            var toModel = instance.transform.worldToLocalMatrix;
            if (!FaceFit.EyesOf(animator, toModel, out var eyes, out float spacing))
            {
                report.Expect(false, what, "the character has no head bone to measure from");
                return 1;
            }
            float unit = FaceFit.Unit(animator, toModel, spacing);
            var frame = FaceFit.FrameOf(animator, toModel);
            var into = Quaternion.Inverse(frame);
            var spots = new List<Vector3>();
            var names = new List<string>();
            var line = new StringBuilder();
            for (int i = 0; i < record.PieceCount; i++)
            {
                if (!record.TryPiece(i, out var part, out var pivot)) continue;
                var at = into * (toModel.MultiplyPoint3x4(pivot) - eyes) / unit;
                spots.Add(at);
                names.Add(part.name);
                line.Append($"  [{part.name}: right {at.x:+0.00;-0.00;0.00} up {at.y:+0.00;-0.00;0.00} ahead {at.z:+0.00;-0.00;0.00}, x{Mathf.Abs(part.lossyScale.x):0.00}, follows {Follows(part, animator)}]");
            }
            report.Line($"{what}:{line}" + (spacing > 0f ? "" : "  (no eye bones: the spacing is a guess from the head's height)"));
            if (spots.Count == 0)
            {
                report.Expect(false, what, "its record holds no piece");
                return 1;
            }
            // What the position says, held against where the piece went (a row that shifts the piece itself says where).
            if (position.FitsFace && !position.Shifts)
            {
                var at = spots[0];
                float seen = Mathf.Atan2(at.x, at.z) * Mathf.Rad2Deg;
                report.Expect(Mathf.Abs(Mathf.DeltaAngle(seen, position.yaw)) < 35f, what,
                    $"its spot is {seen:0}° round the head from the face; the position looks from {position.yaw:0}°");
                report.Expect(at.magnitude > .3f && at.magnitude < 6f, what, $"its spot is {at.magnitude:0.00} eye spacings from the eyes (a head is one to five across)");
            }
            if (!position.FitsFace && spots.Count == 2)
            {
                Vector3 a = spots[0], b = spots[1];
                report.Expect(Mathf.Abs(a.x + b.x) < .15f && Mathf.Abs(a.y - b.y) < .15f && Mathf.Abs(a.z - b.z) < .15f, what,
                    $"the pair is not a mirror across the face: {names[0]} at {a.x:0.00} {a.y:0.00} {a.z:0.00}, {names[1]} at {b.x:0.00} {b.y:0.00} {b.z:0.00}");
                for (int i = 0; i < 2; i++)
                {
                    int side = OutfitSpot.SideOf(names[i]);
                    report.Expect(side == 0 || spots[i].x * side > 0f, what, $"{names[i]} hangs on the character's {(spots[i].x > 0f ? "right" : "left")}");
                }
            }
            foreach (var at in spots)
                report.Expect(!float.IsNaN(at.x) && at.magnitude < 12f, what, $"a piece is {at.magnitude:0.0} eye spacings from the eyes: off the head");
            // Whether it covers what its row says, there.
            if (position.covers)
                foreach (string hidden in item.HiddenSlots)
                    foreach (var part in OutfitCatalog.PartsOf(skin.Id))
                    {
                        if (!string.Equals(part.slot, hidden, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (string name in OutfitCatalog.Split(part.parts))
                            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                                if (string.Equals(renderer.name, name, StringComparison.OrdinalIgnoreCase))
                                    report.Expect(!renderer.enabled, what, $"it covers the {hidden} slot here, yet the character's own '{name}' still shows");
                    }
            // And nothing it does not cover there.
            HidesWhatItCovers(report, what, skin.Id, item, position, animator, toModel, shown, dressing.Pieces[item.id]);
            if (stage != null)
            {
                stage.Take(instance);
                tiles.Add(stage.Views(eyes, frame, unit, ask.Width));
            }
            return 1;
        }
        catch (Exception problem)
        {
            report.Expect(false, what, "dressing stopped: " + problem.Message);
            return 0;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // What wearing the piece took out of sight among what hangs from the character's head (hair, its ornaments, ears):
    // the parts of the slots the item fills or covers there, and what lies under the piece where it stands now, as near
    // as SkinCover's reach. Anything else it hid, it hid for where it stood some other time: a measure kept past its
    // place. The first pictures this bench took showed it (2026-11-18): a mask worn round the back of the head went on
    // hiding the hairpins it had covered on the face a moment before, the cover being kept by the mesh alone.
    private static void HidesWhatItCovers(Report report, string what, string skinId, OutfitItem item, WearPosition position, Animator animator,
        Matrix4x4 toModel, List<Renderer> shown, List<Renderer> piece)
    {
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.slot };
        slots.UnionWith(item.AlsoSlots);
        if (position.covers) slots.UnionWith(item.HiddenSlots);
        var bySlot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in OutfitCatalog.PartsOf(skinId))
            if (slots.Contains(part.slot)) bySlot.UnionWith(OutfitCatalog.Split(part.parts));
        float reach = GameTables.TryGetRow(SkinCover.TableName, "Default", out var cells) ? cells.Number("reach", .03f) : .03f;
        var boxes = new List<Bounds>();
        foreach (var renderer in piece)
        {
            var mesh = renderer != null ? MeshTools.MeshOf(renderer) : null;
            if (mesh != null) boxes.Add(MeshTools.BoxOf(mesh.bounds, toModel * renderer.transform.localToWorldMatrix));
        }
        if (boxes.Count == 0) return;
        foreach (var renderer in shown)
        {
            if (renderer == null || renderer.enabled || renderer is not SkinnedMeshRenderer skinned || bySlot.Contains(renderer.name)
                || !HeadMeshes.OnHead(skinned, head)) continue;
            var mesh = MeshTools.MeshOf(renderer);
            if (mesh == null) continue;
            var box = MeshTools.BoxOf(mesh.bounds, toModel * renderer.transform.localToWorldMatrix);
            float gap = float.MaxValue;
            foreach (var other in boxes) gap = Mathf.Min(gap, Vector3.Max(Vector3.zero, Vector3.Max(box.min - other.max, other.min - box.max)).magnitude);
            // Boxes, not surfaces: twice the reach leaves room for a corner, and a hairpin on the far side of the head is
            // five times that away.
            report.Expect(gap <= reach * 2f, what,
                $"it hides the character's own '{renderer.name}', {gap * 100f:0.0} cm from where the piece stands: a piece hides only what lies under it (within {reach * 100f:0.#} cm)");
        }
    }

    // The humanoid bone a hung part rides on, by Unity's name for it; "nothing" when none is above it.
    private static string Follows(Transform part, Animator animator)
    {
        if (animator == null || !animator.isHuman) return "nothing";
        for (var at = part.parent; at != null; at = at.parent)
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                if (animator.GetBoneTransform(bone) == at) return bone.ToString();
        return "nothing";
    }

    // A character's sheet: a row to each way of wearing, three views across.
    private static void SaveSheet(string path, List<Texture2D[]> tiles, int size)
    {
        var sheet = new Texture2D(size * 3, size * tiles.Count, TextureFormat.RGB24, false);
        try
        {
            for (int row = 0; row < tiles.Count; row++)
                for (int column = 0; column < 3; column++)
                {
                    var tile = tiles[row][column];
                    if (tile == null) continue;
                    // A texture's rows run from the bottom: the first way of wearing goes on the top row.
                    sheet.SetPixels32(column * size, (tiles.Count - 1 - row) * size, size, size, tile.GetPixels32());
                    Object.DestroyImmediate(tile);
                }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("WardrobeBench: " + path + " could not be written: " + error.Message);
        }
        finally
        {
            Object.DestroyImmediate(sheet);
        }
    }

    // ---- Where the pictures are taken: a scene of its own, lit by itself, seen by one lens ----

    private sealed class Stage : IDisposable
    {
        private readonly Scene scene;
        private readonly GameObject root;
        private readonly Camera lens;
        private readonly bool wasAsync;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private RenderTexture film;

        public Stage()
        {
            // Shaders compile as they are needed, so no picture comes out in the placeholder's flat cyan.
            wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Wardrobe Bench") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            var lensObject = new GameObject("Lens") { hideFlags = HideFlags.HideAndDontSave };
            lensObject.transform.SetParent(root.transform, false);
            lens = lensObject.AddComponent<Camera>();
            lens.enabled = false;
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = Backdrop;
            lens.allowHDR = false;
            lens.allowMSAA = true;
            lens.orthographic = true;
            lens.nearClipPlane = .01f;
            lens.farClipPlane = 20f;
            lens.useOcclusionCulling = false;
            lens.scene = scene;
            var data = lens.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            // A light riding with the lens, so every view is lit from where it is seen.
            var lamp = lensObject.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.color = Color.white;
            lamp.intensity = 1.1f;
            lamp.shadows = LightShadows.None;
        }

        /// <summary>Stands a model in the scene: lit by the stage alone, drawn where its bones are.</summary>
        public void Take(GameObject thing)
        {
            SceneManager.MoveGameObjectToScene(thing, scene);
            var flat = new SphericalHarmonicsL2();
            flat.AddAmbientLight(Ambient);
            var probes = new[] { flat };
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                    // Outside play nothing else re-skins a mesh between two renders.
                    skinned.forceMatrixRecalculationPerRender = true;
                }
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(probes);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// The head from the front, the right side and the back: square pictures <paramref name="size"/> pixels wide,
        /// each seven eye spacings across with the eyes a little above the middle, the body's own front, right and back
        /// by its skeleton (<paramref name="frame"/>). The caller destroys them.
        /// </summary>
        public Texture2D[] Views(Vector3 eyes, Quaternion frame, float unit, int size)
        {
            Vector3 ahead = frame * Vector3.forward, right = frame * Vector3.right;
            var middle = eyes + Vector3.down * unit * .5f;
            float away = 3f;
            lens.orthographicSize = unit * 3.5f;
            return new[]
            {
                Shoot(middle + ahead * away, Quaternion.LookRotation(-ahead, Vector3.up), size),
                Shoot(middle + right * away, Quaternion.LookRotation(-right, Vector3.up), size),
                Shoot(middle - ahead * away, Quaternion.LookRotation(ahead, Vector3.up), size),
            };
        }

        private Texture2D Shoot(Vector3 spot, Quaternion turn, int size)
        {
            if (film == null || film.width != size)
            {
                if (film != null)
                {
                    film.Release();
                    Object.DestroyImmediate(film);
                }
                film = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = "Wardrobe Bench Film", antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
                film.Create();
            }
            lens.transform.SetPositionAndRotation(spot, turn);
            lens.aspect = 1f;
            var previous = RenderTexture.active;
            var picture = new Texture2D(size, size, TextureFormat.RGB24, false);
            try
            {
                lens.targetTexture = film;
                lens.Render();
                RenderTexture.active = film;
                picture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                picture.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
                lens.targetTexture = null;
            }
            return picture;
        }

        public void Dispose()
        {
            if (film != null)
            {
                film.Release();
                Object.DestroyImmediate(film);
            }
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = wasAsync;
        }
    }
}
#endif
