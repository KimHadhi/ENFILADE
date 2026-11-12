using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The wardrobe's photo studio, where every picture of a piece is taken (OutfitIcons' pictures, the shooting window
/// OutfitShootView, the editor's bake): the piece stood alone far to the side of everything, framed whole and
/// photographed against nothing, at once (Camera.Render, no waiting a frame), lit the same wherever it is taken.
/// - Framed from where its vertices really are, posed (a skinned piece baked: BakeMesh), so nothing of it is cut off
///   (the user, 2026-11-12: "작게 전체가 아니라 좀 전체적으로 보이고"); from its front (a character's front from its arms:
///   some avatars face -Z as made), a little from above, leaning a little clockwise: the lens turns about its own axis,
///   so the lean is in the picture and the wardrobe lays the square straight (the user drew it so: "살짝 기울어져
///   있으면서"). How far from above, how far it leans and how much of the picture it fills are the Auto numbers of
///   WardrobePictures.csv (OutfitPictureLook). A framing (OutfitFraming) turns round it, zooms (the lens narrows: the
///   camera never goes into the piece), moves it in the picture and leans it more or less.
/// - Lit by its own lights only, the title screen's character's (a white key, an ice rim and a pink rim) turned with the
///   view, and a flat ambient given to the piece's own renderers (light probes they are handed), so a picture taken on
///   the title screen, in the hub or in the editor looks the same. The scene's lights are switched off for the studio's
///   own render alone, and the piece and the studio's lights are switched on only for it: no other camera ever sees
///   them. In the editor's bake it stands in a preview scene of its own instead, where nothing else is.
/// </summary>
public sealed class OutfitStudio : IDisposable
{
    /// <summary>Far to the side of everything a menu or a level holds.</summary>
    public static readonly Vector3 Home = new Vector3(-600f, 0f, 0f);
    // The lens's field (°) and as far as it reaches (m). How much of the picture the piece fills, how far from above it
    // is seen and how far it leans when nothing says otherwise are the table's (OutfitPictureLook.AutoFill, AutoPitch,
    // AutoTilt).
    private const float FieldOfView = 24f, Reach = 60f;
    // At most this many vertices are kept for the framing: a dense piece is thinned out evenly.
    private const int MostPoints = 60000;
    // Looking for the parts of a piece: the steps of the widest-gap search along a side, and how many times a part is
    // split again at most.
    private const int GapBins = 1024, MostSplits = 5;
    private static readonly Color Ambient = new Color(.42f, .45f, .55f);
    // The title screen's character is lit for a camera looking back along -Z at a front facing +Z (MainMenuPreview); the
    // rig keeps those angles to whatever way the studio's lens looks.
    private static readonly Quaternion MenuView = Quaternion.LookRotation(Vector3.back);

    private readonly Transform root;
    private readonly Camera lens;
    private readonly bool isolated;
    private readonly List<(Light light, Quaternion turn)> rig = new List<(Light, Quaternion)>();
    private readonly List<Renderer> shown = new List<Renderer>();
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<Vector3> scratch = new List<Vector3>();
    private readonly List<Light> switchedOff = new List<Light>();
    // The piece's parts well apart (gloves on outstretched hands, a pair of earrings), left to right across its automatic
    // picture, each a run of `order` (indices into points); the points the lens frames now and which part they are.
    private readonly struct Part
    {
        public readonly int Start, Count;
        public readonly float Across;

        public Part(int start, int count, float across)
        {
            Start = start;
            Count = count;
            Across = across;
        }
    }
    private readonly List<Part> parts = new List<Part>();
    private readonly List<Vector3> framed = new List<Vector3>();
    private readonly bool[] filled = new bool[GapBins];
    private int[] order = Array.Empty<int>();
    private float[] along = Array.Empty<float>();
    private int autoPart, framedPart = int.MinValue;
    private MaterialPropertyBlock block;
    private GameObject sitter;
    private Mesh baked;
    private RenderTexture film;
    private Vector3 front = Vector3.forward, middle;

    /// <summary>A piece stands, ready to be photographed.</summary>
    public bool HasPiece => sitter != null && shown.Count > 0;
    /// <summary>How many parts well apart the standing piece is in (1: all of a piece).</summary>
    public int Parts => Mathf.Max(1, parts.Count);
    /// <summary>The part, 1 from the picture's left, the automatic framing holds; 0 when it holds the whole piece.</summary>
    public int AutoPart => autoPart;
    /// <summary>What a framing holds of the standing piece: 0 all of it, else the part so many from the picture's left.</summary>
    public int PartOf(OutfitFraming framing) => Resolve(framing.part);

    /// <param name="scene">A scene of its own to stand in (the editor's preview scene), where nothing else is; none: the
    /// active scene, far to its side.</param>
    public OutfitStudio(Scene? scene = null)
    {
        isolated = scene.HasValue;
        // In the editor, outside play, nothing of it may be saved into a scene; in play it goes with the scene.
        var flags = Application.isPlaying ? HideFlags.None : HideFlags.HideAndDontSave;
        var holder = new GameObject("Outfit Studio") { hideFlags = flags };
        if (scene.HasValue) SceneManager.MoveGameObjectToScene(holder, scene.Value);
        root = holder.transform;
        root.position = Home;
        var lensObject = new GameObject("Lens") { hideFlags = flags };
        lensObject.transform.SetParent(root, false);
        lens = lensObject.AddComponent<Camera>();
        lens.enabled = false;
        lens.clearFlags = CameraClearFlags.SolidColor;
        // Nothing behind the piece: the wardrobe lays its picture over its plates.
        lens.backgroundColor = new Color(0f, 0f, 0f, 0f);
        lens.allowHDR = false;
        lens.allowMSAA = true;
        lens.fieldOfView = FieldOfView;
        lens.nearClipPlane = .02f;
        lens.farClipPlane = Reach;
        lens.useOcclusionCulling = false;
        if (scene.HasValue) lens.scene = scene.Value;
        var data = lens.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        AddLight("Key", Quaternion.Euler(30f, 200f, 0f), Color.white, 1.2f, flags);
        AddLight("Ice Rim", Quaternion.Euler(15f, 35f, 0f), KitStyle.Ice, 1.1f, flags);
        AddLight("Pink Rim", Quaternion.Euler(-10f, -40f, 0f), KitStyle.Accent, .6f, flags);
    }

    private void AddLight(string name, Quaternion turn, Color color, float intensity, HideFlags flags)
    {
        var holder = new GameObject(name) { hideFlags = flags };
        holder.transform.SetParent(root, false);
        holder.transform.rotation = turn;
        var light = holder.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.enabled = false;
        rig.Add((light, turn));
    }

    // ---- Standing a piece ----

    /// <summary>
    /// Stands a piece alone: an item in a colour set for this character, or (no item) the character's own piece of
    /// <paramref name="slot"/> in one of its colour sets. False when there is nothing of it to photograph.
    /// </summary>
    public bool Stand(PlayerBodyProfile.Skin skin, OutfitItem item, string colour, string slot)
    {
        Clear();
        if (skin == null) return false;
        if (item != null)
        {
            var entry = OutfitCatalog.Model(item.id, skin.Id);
            if (entry == null || entry.Model == null) return false;
            sitter = Object.Instantiate(entry.Model, root.position, Quaternion.identity, root);
            front = FrontOf(sitter);
            Still(sitter);
            var wanted = new HashSet<string>(item.PartNames, StringComparer.OrdinalIgnoreCase);
            var drop = new HashSet<string>(OutfitCatalog.Split(OutfitCatalog.FitOf(item.id, skin.Id)?.drop), StringComparer.OrdinalIgnoreCase);
            var character = skin.Model;
            var body = character != null ? character.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                // Its own meshes only: an outfit's file often carries the avatar's body it was fitted on, told from the
                // piece by dressing's own rule (a mirrored copy of the character's mesh is a piece, not her body).
                bool mine = wanted.Count > 0 ? wanted.Contains(renderer.name)
                    : !drop.Contains(renderer.name) && (character == null || !OutfitDresser.CopiesOwn(renderer, sitter.transform, body, character.transform));
                if (mine) shown.Add(renderer);
            }
            SkinVariant.WearOn(shown, entry.VariantOf(colour));
        }
        else
        {
            if (skin.Model == null || string.IsNullOrEmpty(slot)) return false;
            sitter = Object.Instantiate(skin.Model, root.position, Quaternion.identity, root);
            front = FrontOf(sitter);
            Still(sitter);
            SkinVariant.Wear(sitter, null, skin.VariantOf(colour));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(skin.Id))
                if (string.Equals(part.slot, slot, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                if (!names.Contains(renderer.name)) continue;
                renderer.gameObject.SetActive(true);
                shown.Add(renderer);
            }
        }
        // Every renderer of it stays off but for the studio's own picture.
        foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        if (shown.Count == 0)
        {
            Clear();
            return false;
        }
        if (block == null) block = new MaterialPropertyBlock();
        var flat = new SphericalHarmonicsL2();
        flat.AddAmbientLight(Ambient);
        var probes = new[] { flat };
        foreach (var renderer in shown)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            // The studio's own ambient, whatever the scene's is.
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            renderer.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(probes);
            renderer.SetPropertyBlock(block);
        }
        Measure();
        return true;
    }

    // The way a character faces: across its upper arms (as the player's body measures it), else +Z.
    private static Vector3 FrontOf(GameObject model)
    {
        var animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.avatar == null || !animator.isHuman) return Vector3.forward;
        var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (left == null || right == null) return Vector3.forward;
        Vector3 facing = Vector3.Cross(right.position - left.position, Vector3.up);
        facing.y = 0f;
        return facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
    }

    // Stands as its file was made: no clips play on it.
    private static void Still(GameObject model)
    {
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
    }

    // Every vertex of the piece where it stands posed, a skinned one baked, for a framing that holds all of it; a mesh
    // that can't be read (a build's) gives the corners of its box instead. Then the piece's parts well apart, if it has.
    private void Measure()
    {
        points.Clear();
        foreach (var renderer in shown)
        {
            int before = points.Count;
            if (renderer is SkinnedMeshRenderer skinned) AddSkinned(skinned);
            else if (renderer.TryGetComponent(out MeshFilter filter)) AddPlain(renderer, filter.sharedMesh);
            if (points.Count == before) AddCorners(renderer);
        }
        if (points.Count > MostPoints)
        {
            int step = Mathf.CeilToInt(points.Count / (float)MostPoints), kept = 0;
            for (int i = 0; i < points.Count; i += step) points[kept++] = points[i];
            points.RemoveRange(kept, points.Count - kept);
        }
        SplitParts();
        framedPart = int.MinValue;
    }

    private static bool Readable(Mesh mesh) => mesh != null && (mesh.isReadable || Application.isEditor);

    private void AddSkinned(SkinnedMeshRenderer skinned)
    {
        if (!Readable(skinned.sharedMesh)) return;
        if (baked == null) baked = new Mesh { name = "Outfit Studio Bake", hideFlags = HideFlags.HideAndDontSave };
        try { skinned.BakeMesh(baked, true); }
        catch (Exception) { return; }
        baked.GetVertices(scratch);
        // Baked with the renderer's scale taken out (useScale): the mesh in the renderer's own space, which its whole matrix
        // takes to the world (as SkinSample and the zombie hitboxes read it). Put down with its place and turn alone, a
        // piece whose renderer is scaled small was measured as many times too big as its scale is small: the Ene earrings'
        // renderers are scaled 0.0067-0.07, so they were framed as if 14-149 times their size and came out a speck in
        // their pictures (the user, 2026-11-12: "귀걸이는 너무 작아서 아예 안 보여").
        var place = skinned.transform.localToWorldMatrix;
        foreach (var vertex in scratch) points.Add(place.MultiplyPoint3x4(vertex));
    }

    private void AddPlain(Renderer renderer, Mesh mesh)
    {
        if (!Readable(mesh)) return;
        mesh.GetVertices(scratch);
        var place = renderer.transform.localToWorldMatrix;
        foreach (var vertex in scratch) points.Add(place.MultiplyPoint3x4(vertex));
    }

    private void AddCorners(Renderer renderer)
    {
        var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        var local = mesh != null ? mesh.bounds : new Bounds(renderer.transform.InverseTransformPoint(renderer.bounds.center), renderer.bounds.size);
        for (int i = 0; i < 8; i++)
            points.Add(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
    }

    // ---- Parts well apart ----

    // A piece made as parts far apart, a pair of gloves on outstretched hands (83 cm between them), fitted whole left each
    // glove a sliver of its picture. The points are cut at the widest empty gap along the picture's across, up or depth
    // wherever the gap is wide (WardrobePictures.csv Auto.splitGap, m) or wide next to the parts' own widths that way
    // (Auto.splitRatio), again inside each side, and the sides are the parts; a side with almost none of the points
    // (Auto.noise) is a stray, framed never. Shoes, socks and stockings (2-5 cm apart) stay a pair (the user's
    // outfits measured 2026-11-12).
    private void SplitParts()
    {
        parts.Clear();
        autoPart = 0;
        int count = points.Count;
        if (count == 0) return;
        if (order.Length < count) order = new int[count];
        if (along.Length < count) along = new float[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Vector3 across = Vector3.Cross(front, Vector3.up);
        across = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.right;
        Vector3[] sides = { across, Vector3.up, front };
        float wide = OutfitPictureLook.AutoSplitGap, ratio = OutfitPictureLook.AutoSplitRatio;
        var leaves = new List<(int start, int size)>();
        var pending = new Stack<(int start, int size, int depth)>();
        pending.Push((0, count, 0));
        while (pending.Count > 0)
        {
            var (start, size, depth) = pending.Pop();
            if (depth < MostSplits && size > 1 && WidestGap(start, size, sides, wide, ratio, out Vector3 side, out float cut))
            {
                int split = Partition(start, size, side, cut);
                if (split > start && split < start + size)
                {
                    pending.Push((split, start + size - split, depth + 1));
                    pending.Push((start, split - start, depth + 1));
                    continue;
                }
            }
            leaves.Add((start, size));
        }
        int least = Mathf.Max(1, Mathf.CeilToInt(count * OutfitPictureLook.AutoNoise));
        int most = 0;
        foreach (var (_, size) in leaves) most = Mathf.Max(most, size);
        foreach (var (start, size) in leaves)
        {
            if (size < least && size < most) continue;
            float sum = 0f;
            for (int i = start; i < start + size; i++) sum += Vector3.Dot(points[order[i]], across);
            parts.Add(new Part(start, size, sum / size));
        }
        parts.Sort((a, b) => a.Across.CompareTo(b.Across));
        if (parts.Count < 2) return;
        // Alone by default: the biggest part, or of parts alike (a pair) the one on the picture's right, where the plate
        // shows it clear of its edge.
        int biggest = 0;
        foreach (var part in parts) biggest = Mathf.Max(biggest, part.Count);
        for (int i = parts.Count - 1; i >= 0; i--)
            if (parts[i].Count >= biggest * .85f)
            {
                autoPart = i + 1;
                break;
            }
    }

    // The widest empty gap of the points order[start..start+size) along one of the sides, where it is wide enough to part
    // them: the side and where to cut. Of more than one, the widest next to the parts' own widths.
    private bool WidestGap(int start, int size, Vector3[] sides, float wide, float ratio, out Vector3 side, out float cut)
    {
        side = Vector3.zero;
        cut = 0f;
        float best = 0f;
        foreach (var direction in sides)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < size; i++)
            {
                float value = Vector3.Dot(points[order[start + i]], direction);
                along[i] = value;
                low = Mathf.Min(low, value);
                high = Mathf.Max(high, value);
            }
            float span = high - low;
            if (span < 1e-5f) continue;
            Array.Clear(filled, 0, GapBins);
            for (int i = 0; i < size; i++) filled[Mathf.Min(GapBins - 1, (int)((along[i] - low) / span * (GapBins - 1)))] = true;
            int run = 0, runStart = 0, longest = 0, longestStart = 0;
            for (int bin = 0; bin < GapBins; bin++)
            {
                if (!filled[bin])
                {
                    if (run == 0) runStart = bin;
                    run++;
                    continue;
                }
                if (run > longest)
                {
                    longest = run;
                    longestStart = runStart;
                }
                run = 0;
            }
            if (longest == 0) continue;
            float at = low + (longestStart + longest * .5f) / (GapBins - 1) * span;
            float leftHigh = float.MinValue, rightLow = float.MaxValue;
            for (int i = 0; i < size; i++)
            {
                if (along[i] < at) leftHigh = Mathf.Max(leftHigh, along[i]);
                else rightLow = Mathf.Min(rightLow, along[i]);
            }
            if (leftHigh == float.MinValue || rightLow == float.MaxValue) continue;
            float gap = rightLow - leftHigh, width = Mathf.Max(leftHigh - low, high - rightLow);
            if (gap < wide && gap < ratio * width) continue;
            float score = gap / Mathf.Max(1e-4f, width);
            if (score <= best) continue;
            best = score;
            side = direction;
            cut = at;
        }
        return best > 0f;
    }

    // Puts the points of order[start..start+size) below the cut along the side first; where the rest begins.
    private int Partition(int start, int size, Vector3 side, float cut)
    {
        int low = start, high = start + size - 1;
        while (low <= high)
        {
            if (Vector3.Dot(points[order[low]], side) < cut) low++;
            else
            {
                (order[low], order[high]) = (order[high], order[low]);
                high--;
            }
        }
        return low;
    }

    // A framing's part as it stands: 0 the whole piece, else 1 from the picture's left.
    private int Resolve(int part)
    {
        if (parts.Count < 2 || part < 0) return 0;
        return part == 0 ? autoPart : Mathf.Min(part, parts.Count);
    }

    // The points the lens frames: one part, or all of them (strays left out), and their middle.
    private void Select(int part)
    {
        framed.Clear();
        if (part >= 1 && part <= parts.Count) Gather(parts[part - 1]);
        else foreach (var each in parts) Gather(each);
        if (framed.Count == 0) framed.AddRange(points);
        var box = new Bounds(framed.Count > 0 ? framed[0] : root.position, Vector3.zero);
        foreach (var point in framed) box.Encapsulate(point);
        middle = box.center;
        framedPart = part;
    }

    private void Gather(Part part)
    {
        for (int i = part.Start; i < part.Start + part.Count; i++) framed.Add(points[order[i]]);
    }

    // ---- Framing ----

    /// <summary>
    /// Points the lens at the standing piece as <paramref name="framing"/> says: round it from its front, leaning in the
    /// picture, the whole of it (or of the part it holds, PartOf) fitted in a picture of this <paramref name="aspect"/>,
    /// then the lens narrowed by the zoom and the piece moved (along the picture's own sides).
    /// </summary>
    public void Frame(OutfitFraming framing, float aspect = 1f)
    {
        int part = Resolve(framing.part);
        if (part != framedPart) Select(part);
        if (framed.Count == 0) return;
        aspect = Mathf.Max(.1f, aspect);
        float baseYaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
        var upright = Quaternion.Euler(Mathf.Clamp(OutfitPictureLook.AutoPitch + framing.pitch, -85f, 85f), baseYaw + framing.yaw, 0f);
        // The lens turned about its own axis: turned so (+ about its forward axis, its top towards its left) the piece
        // leans clockwise in the picture. The fit below is in the turned view, so the leaning piece still fits whole.
        var view = upright * Quaternion.Euler(0f, 0f, OutfitPictureLook.AutoTilt + framing.roll);
        var into = Quaternion.Inverse(view);
        float fill = OutfitPictureLook.AutoFill;
        float tanV = Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad), tanH = tanV * aspect;
        // Fitted round the box's middle, then once more round the middle of what that showed, so it sits centred.
        Vector2 shift = Vector2.zero;
        float distance = .1f, nearest = 0f, farthest = 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            distance = .05f;
            foreach (var point in framed)
            {
                Vector3 seen = into * (point - middle);
                float need = Mathf.Max(Mathf.Abs(seen.x - shift.x) / (tanH * fill), Mathf.Abs(seen.y - shift.y) / (tanV * fill)) - seen.z;
                if (need > distance) distance = need;
            }
            float left = float.MaxValue, right = float.MinValue, low = float.MaxValue, high = float.MinValue;
            nearest = float.MaxValue;
            farthest = float.MinValue;
            foreach (var point in framed)
            {
                Vector3 seen = into * (point - middle);
                float depth = distance + seen.z;
                nearest = Mathf.Min(nearest, depth);
                farthest = Mathf.Max(farthest, depth);
                if (depth <= .001f) continue;
                float across = (seen.x - shift.x) / depth, up = (seen.y - shift.y) / depth;
                left = Mathf.Min(left, across);
                right = Mathf.Max(right, across);
                low = Mathf.Min(low, up);
                high = Mathf.Max(high, up);
            }
            if (left <= right) shift += new Vector2((left + right) * .5f * distance, (low + high) * .5f * distance);
        }
        // The zoom narrows the lens rather than moving in, so the camera never stands inside a skirt or a coat.
        float zoom = Mathf.Clamp(framing.zoom, .05f, 20f);
        float zoomedV = tanV / zoom, zoomedH = tanH / zoom;
        lens.fieldOfView = 2f * Mathf.Atan(zoomedV) * Mathf.Rad2Deg;
        lens.aspect = aspect;
        Vector3 aim = middle + view * new Vector3(shift.x - framing.x * distance * zoomedH, shift.y - framing.y * distance * zoomedV, 0f);
        lens.transform.SetPositionAndRotation(aim - view * Vector3.forward * distance, view);
        lens.nearClipPlane = Mathf.Max(.005f, nearest * .5f);
        lens.farClipPlane = Mathf.Max(lens.nearClipPlane + .1f, farthest * 1.5f + .5f);
        // The lights keep to the upright view: the lean turns the photograph, not the light on the piece.
        foreach (var (light, turn) in rig) light.transform.rotation = upright * Quaternion.Inverse(MenuView) * turn;
    }

    // ---- Taking the picture ----

    /// <summary>Takes the picture into <paramref name="target"/> now, multisampled and resolved into it; false when nothing stands.</summary>
    public bool Shoot(RenderTexture target)
    {
        if (target == null || !HasPiece) return false;
        if (film == null || film.width != target.width || film.height != target.height)
        {
            if (film != null) { film.Release(); Gone(film); }
            film = new RenderTexture(target.width, target.height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Outfit Studio Film", antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave
            };
            film.Create();
        }
        if (!Render(film)) return false;
        Graphics.Blit(film, target);
        return true;
    }

    /// <summary>The picture as a readable texture of <paramref name="size"/> pixels a side (to be saved); null when nothing stands.</summary>
    public Texture2D Capture(int size)
    {
        var plain = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            if (!Shoot(plain)) return null;
            RenderTexture.active = plain;
            var picture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Outfit Picture", wrapMode = TextureWrapMode.Clamp };
            picture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            picture.Apply();
            return picture;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(plain);
        }
    }

    // The scene's lights off and the studio's on, the piece shown, for this one render; everything back after.
    private bool Render(RenderTexture target)
    {
        switchedOff.Clear();
        if (!isolated)
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != null && light.enabled && !Ours(light))
                {
                    light.enabled = false;
                    switchedOff.Add(light);
                }
        var was = lens.targetTexture;
        try
        {
            foreach (var (light, _) in rig) light.enabled = true;
            foreach (var renderer in shown) if (renderer != null) renderer.enabled = true;
            lens.targetTexture = target;
            lens.Render();
            return true;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitStudio] " + problem.Message);
            return false;
        }
        finally
        {
            lens.targetTexture = was;
            foreach (var renderer in shown) if (renderer != null) renderer.enabled = false;
            foreach (var (light, _) in rig) if (light != null) light.enabled = false;
            foreach (var light in switchedOff) if (light != null) light.enabled = true;
            switchedOff.Clear();
        }
    }

    private bool Ours(Light light)
    {
        foreach (var (mine, _) in rig) if (mine == light) return true;
        return false;
    }

    // ---- Clearing up ----

    /// <summary>Takes the standing piece away.</summary>
    public void Clear()
    {
        if (sitter != null) Gone(sitter);
        sitter = null;
        shown.Clear();
        points.Clear();
        parts.Clear();
        framed.Clear();
        autoPart = 0;
        framedPart = int.MinValue;
    }

    public void Dispose()
    {
        Clear();
        if (film != null) { film.Release(); Gone(film); }
        film = null;
        if (baked != null) Gone(baked);
        baked = null;
        if (root != null) Gone(root.gameObject);
    }

    private static void Gone(Object thing)
    {
        if (Application.isPlaying) Object.Destroy(thing);
        else Object.DestroyImmediate(thing);
    }
}
