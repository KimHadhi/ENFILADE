using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// What the player sees through every sight of every gun, answered from the aiming eye with the game's own shaders and
/// without Play: is the world seen through the sight's window, and is its mark (the dot, the reticle) there and on the
/// line of sight.
/// Each gun of FPSBodyAssets is stood alone in a scene of its own, each sight its socket offers is put up as the game
/// puts it up (GunSights.Select: seated from the tables, the gun's own iron sights folded), and a camera is put at that
/// sight's eye point with the field of view the player has aimed through it. Then, with nothing guessed:
/// - through: the picture is taken twice before two walls of different colours; a pixel that changes with the wall is
///   one the world is seen through, one that stays is covered. Asked over the sight's window (the renderer that draws
///   its mark; for iron sights a band over the middle of the view).
/// - mark: the picture before a black wall with the mark drawn and with it put out; what differs is the mark as drawn:
///   how many pixels, how bright, how far from the middle of the view.
/// - when a window is covered: each renderer of the gun, then each material of the one that mattered, is left out in
///   turn, and the one whose absence opens the window is named with its shader.
/// The report is Logs/gun-sights.txt, the pictures Logs/GunSights/&lt;gun&gt;.&lt;sight&gt;.png (the eye's view before a
/// chequered wall 30 m off, squares of 25 cm). Run from ENFILADE → 총 → 조준경 점검, or by making the file
/// Temp/enfilade-gunsights.request (a line "guns=R08,M1911" asks for those alone); never while the game plays.
/// Why it exists: the R08's dot sight could not be seen through, the user said so three times (2026-11-18: "안 보이던
/// 조준기는 R08이며 지금 세 번째 말합니다. 이 조준경은 보이지 않습니다"), and the check of where parts sit (Tools/Guns)
/// passed it every time: that check places meshes and knows nothing of what a material draws. The model of the dot
/// sight has a glass lens across its window whose material the pack never made for Unity, so Unity drew it with the
/// model's own default, a solid grey.
/// </summary>
[InitializeOnLoad]
internal static class GunSightBench
{
    private const string Request = "Temp/enfilade-gunsights.request", ReportFile = "Logs/gun-sights.txt", Folder = "Logs/GunSights";
    // Drawn at a common play size: a holograph's ring is a pixel or two wide there, and at a third of it only its
    // brightest ticks were left, which read as a mark half a degree off the middle. The middle half is kept as the picture.
    private const int Width = 1920, Height = 1080, KeptWidth = 960, KeptHeight = 540;
    // The player's view: the default field across a 16:9 screen, and the player's camera's near plane (Player.prefab).
    private const float ScreenFieldAcross = 90f, ScreenAspect = 16f / 9f, NearClip = .03f;
    // What the report marks for a person to look at: a window the world shows through less than this, iron sights with
    // less than this of the band over them open, a mark this far (degrees) off the middle of the view.
    private const float ClearEnough = .6f, OffCentre = .5f;
    private static readonly Color Green = new Color(0f, 1f, 0f), Magenta = new Color(1f, 0f, 1f);
    private static readonly int ReticleId = Shader.PropertyToID("_Reticle"), ReticleEmissionId = Shader.PropertyToID("_ReticleEmission"),
        EmissiveColourId = Shader.PropertyToID("_EmissiveColor");

    private static double next;

    static GunSightBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        HashSet<string> guns = null;
        try
        {
            foreach (string line in File.ReadAllLines(Request))
            {
                int cut = line.IndexOf('=');
                if (cut <= 0 || !line.Substring(0, cut).Trim().Equals("guns", StringComparison.OrdinalIgnoreCase)) continue;
                guns = new HashSet<string>(line.Substring(cut + 1).Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            }
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run(guns);
    }

    [MenuItem("ENFILADE/총/조준경 점검 (눈으로 본 화면)")]
    private static void RunAll() => Run(null);

    private static void Run(HashSet<string> only)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} gun sights as the aiming eye sees them, drawn by the game's own shaders without Play (GunSightBench).");
        text.AppendLine("Each sight is put up as the game puts it up and looked through from its own eye point, with the field of view the player has aimed through it.");
        text.AppendLine("  through   how much of the world shows through the sight's window (0-100%): the view is taken before a green wall and a magenta one, and a");
        text.AppendLine("            pixel that changes with the wall is seen through. For iron sights: of a band 4 degrees wide just over the middle of the view.");
        text.AppendLine($"  mark      the dot or reticle as drawn: its pixels (of {Width} x {Height}), and how far its middle is from the middle of the view, degrees right and up.");
        text.AppendLine("            A scope's reticle is dark lines with a lit middle: only the lit part is counted (the lines show in the picture).");
        text.AppendLine("  A sight that magnifies the whole view (zoom) is asked over its picture, the round about the middle of the view; the lens round it is the scope's shadow.");
        text.AppendLine("  blocked   for a window that is covered: the renderer, and the material of it, whose absence opens the window.");
        text.AppendLine($"  '!' marks a sight to look at: a window under {ClearEnough:P0} through, no mark, a mark over {OffCentre.ToString("0.#", CultureInfo.InvariantCulture)} degrees off the middle.");
        text.AppendLine("  Iron sights are told and not marked: a peep sight covers most of its band by its make (look at the picture).");
        text.AppendLine($"Pictures: {Folder}/<gun>.<sight>.png, the middle half of the eye's view ({KeptWidth} x {KeptHeight} of {Width} x {Height}) before a chequered wall 30 m off (squares of 25 cm).");
        text.AppendLine();
        int marks = 0, looked = 0, guns = 0;
        var assets = FPSBodyAssets.Instance;
        if (assets == null || assets.guns == null)
        {
            text.AppendLine("FPSBodyAssets could not be read: nothing was looked at.");
            Write(text);
            return;
        }
        Directory.CreateDirectory(Folder);
        var done = new HashSet<GameObject>();
        using (var stage = new Stage())
        {
            foreach (var entry in assets.guns)
            {
                if (entry.prefab == null || !done.Add(entry.prefab)) continue;
                if (only != null && !only.Contains(entry.prefab.name)) continue;
                guns++;
                try
                {
                    LookAt(stage, entry.prefab, entry.weapon, text, ref marks, ref looked);
                }
                catch (Exception error)
                {
                    marks++;
                    text.AppendLine($"!  {entry.prefab.name}: not looked at ({error.GetType().Name}: {error.Message})");
                    Debug.LogException(error);
                }
            }
        }
        text.AppendLine();
        text.AppendLine($"({guns} guns, {looked} sights looked through, {marks} marks)");
        Write(text);
        Debug.Log($"GunSightBench: {guns} guns, {looked} sights, {marks} marks. {ReportFile}, {Folder}");
    }

    private static void Write(StringBuilder text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportFile));
        File.WriteAllText(ReportFile, text.ToString(), new UTF8Encoding(false));
    }

    // ---- One gun ----

    private static void LookAt(Stage stage, GameObject prefab, WeaponData weapon, StringBuilder text, ref int marks, ref int looked)
    {
        var gun = stage.Stand(prefab);
        try
        {
            text.AppendLine($"== {gun.name}");
            if (gun.TryGetComponent(out GunAttachments parts)) parts.Begin();
            if (!gun.TryGetComponent(out GunSights sights) || sights.Count == 0)
            {
                text.AppendLine("   no sights of its own to choose from (GunSights): not looked through");
                return;
            }
            float hip = Camera.HorizontalToVerticalFieldOfView(ScreenFieldAcross, ScreenAspect);
            float own = weapon != null ? Mathf.Min(hip, weapon.AdsFov) : hip;
            for (int i = 0; i < sights.Count; i++)
            {
                if (!sights.Offered(i)) continue;
                looked++;
                Look(stage, gun, sights, i, hip, own, text, ref marks);
            }
        }
        finally
        {
            Object.DestroyImmediate(gun);
        }
    }

    private static readonly List<Vector2Int> Pixels = new List<Vector2Int>();

    private static void Look(Stage stage, GameObject gun, GunSights sights, int index, float hip, float own, StringBuilder text, ref int marks)
    {
        sights.Select(index);
        string id = sights.IdOf(index);
        var data = sights.PartOf(index);
        var aim = sights.AimPoint;
        if (aim == null)
        {
            marks++;
            text.AppendLine($"!  {id,-26} has no eye point (GunSights.Sight.aimPoint): the game cannot aim through it");
            return;
        }
        float field = GunMounts.AimedFov(data, hip, own);
        stage.Aim(aim.position, aim.rotation, field);
        // A magnified sight's lens is a picture its own camera takes: asked for before every shot here (GunScope.RenderNow).
        var scope = sights.Scope;
        Action picture = null;
        if (scope != null)
        {
            scope.Show(1f, stage.Eye, field);
            picture = scope.RenderNow;
        }
        var window = WindowOf(sights.sights[index]);
        bool optic = window != null;
        // A sight that magnifies the whole view shows its picture in a round of its row's size about the middle of the
        // view (GunParts.csv window), with the scope's own shadow round it out to the lens's rim: the picture is the window.
        if (optic && data.Zooms) PointsOverThePicture(data.Window);
        else if (optic) PointsOver(window, stage.Eye, scope != null);
        else PointsOverTheSights(field);
        // One shot thrown away: the first after a gun's meshes were cut and its parts switched is of the frame before.
        picture?.Invoke();
        stage.Shoot();
        var bad = new List<string>();
        var line = new StringBuilder();
        string kind = data.Found && !string.IsNullOrEmpty(data.Kind) ? data.Kind : optic ? "optic" : "iron";
        line.Append($"{id,-26} {kind,-6} field {field.ToString("0.0", CultureInfo.InvariantCulture)} deg; ");
        if (Pixels.Count == 0)
        {
            bad.Add("its window is not in the view");
            line.Append("its window is nowhere in the view from its own eye point");
        }
        else
        {
            float through = Through(stage, picture, out int clear);
            line.Append($"through {through:P0} ({clear} of {Pixels.Count} points clear)");
            if (optic && through < ClearEnough)
            {
                bad.Add("covered");
                line.Append("; blocked by " + Blocker(stage, gun, picture, through));
            }
        }
        if (optic)
        {
            Mark(stage, window, picture, field, out int count, out int brightest, out Vector2 off);
            if (count == 0)
            {
                bad.Add("no mark");
                line.Append("; mark NOT DRAWN");
            }
            else
            {
                line.Append($"; mark{(scope != null ? " (its lit part)" : "")} {count} px, brightest {brightest}, {off.x.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} right {off.y.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} up deg");
                if (off.magnitude > OffCentre) bad.Add("mark off the middle");
            }
        }
        // The eye's view, to look at.
        string file = $"{Folder}/{gun.name}.{id}.png";
        stage.Wall(Color.white, true);
        picture?.Invoke();
        File.WriteAllBytes(file, stage.Middle(stage.Shoot()));
        // Outside play Unity never tells a scope it is going: its camera and picture are let go of here.
        if (scope != null) scope.Close();
        marks += bad.Count;
        text.AppendLine((bad.Count > 0 ? "!  " : "   ") + line + (bad.Count > 0 ? "   <- " + string.Join(", ", bad) : ""));
    }

    // ---- The window ----

    // The renderer a sight's mark is drawn by: a scope's lens, else the part's renderer whose material has a reticle.
    private static Renderer WindowOf(in GunSights.Sight sight)
    {
        if (sight.scope != null && sight.scope.lens != null) return sight.scope.lens;
        if (sight.part == null) return null;
        foreach (var renderer in sight.part.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(ReticleId)) return renderer;
        }
        return null;
    }

    // Where in the picture the window is: points over the middle four fifths of it (it lies across its own z, a quad or a
    // flattened ball), as pixels.
    private static void PointsOver(Renderer window, Camera eye, bool round)
    {
        Pixels.Clear();
        var local = window.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        const int across = 13;
        for (int y = 0; y < across; y++)
            for (int x = 0; x < across; x++)
            {
                float u = Mathf.Lerp(-.8f, .8f, x / (across - 1f)), v = Mathf.Lerp(-.8f, .8f, y / (across - 1f));
                if (round && u * u + v * v > .64f) continue;
                Vector3 world = window.transform.TransformPoint(local.center + new Vector3(u * local.extents.x, v * local.extents.y, 0f));
                Vector3 at = eye.WorldToViewportPoint(world);
                if (at.z <= eye.nearClipPlane || at.x < 0f || at.x >= 1f || at.y < 0f || at.y >= 1f) continue;
                Pixels.Add(new Vector2Int((int)(at.x * Width), (int)(at.y * Height)));
            }
    }

    // The middle four fifths of a zoomed sight's picture: a round about the middle of the view, its row's share of the
    // view's height across.
    private static void PointsOverThePicture(float share)
    {
        Pixels.Clear();
        float radius = Mathf.Clamp(share, .1f, 1f) * Height * .5f * .8f;
        // An even count: no point lies on the picture's middle lines, where a reticle's cross is drawn dark.
        const int across = 12;
        for (int y = 0; y < across; y++)
            for (int x = 0; x < across; x++)
            {
                float u = Mathf.Lerp(-1f, 1f, x / (across - 1f)), v = Mathf.Lerp(-1f, 1f, y / (across - 1f));
                if (u * u + v * v > 1f) continue;
                int px = Mathf.RoundToInt(Width * .5f + u * radius), py = Mathf.RoundToInt(Height * .5f + v * radius);
                if (px >= 0 && px < Width && py >= 0 && py < Height) Pixels.Add(new Vector2Int(px, py));
            }
    }

    // Iron sights have no window: the band the target stands in, 4 degrees wide from just over the front post up.
    private static void PointsOverTheSights(float field)
    {
        Pixels.Clear();
        float perDegree = Height / field;
        for (float up = .2f; up <= 1.5f; up += .1f)
            for (float right = -2f; right <= 2f; right += .2f)
            {
                int x = Mathf.RoundToInt(Width * .5f + right * perDegree), y = Mathf.RoundToInt(Height * .5f + up * perDegree);
                if (x >= 0 && x < Width && y >= 0 && y < Height) Pixels.Add(new Vector2Int(x, y));
            }
    }

    // How much of the world shows at the window's points: 1 where a point's colour goes all the way with the wall's.
    private static float Through(Stage stage, Action picture, out int clear)
    {
        stage.Wall(Green, false);
        picture?.Invoke();
        var a = stage.Shoot().GetPixels32();
        stage.Wall(Magenta, false);
        picture?.Invoke();
        var b = stage.Shoot().GetPixels32();
        float sum = 0f;
        clear = 0;
        foreach (var at in Pixels)
        {
            Color32 one = a[at.y * Width + at.x], other = b[at.y * Width + at.x];
            float changed = (Mathf.Abs(one.r - other.r) + Mathf.Abs(one.g - other.g) + Mathf.Abs(one.b - other.b)) / (3f * 255f);
            sum += changed;
            if (changed > .5f) clear++;
        }
        return Pixels.Count > 0 ? sum / Pixels.Count : 0f;
    }

    // What covers a window: the renderer whose absence opens it most, and which of its materials does it.
    private static string Blocker(Stage stage, GameObject gun, Action picture, float through)
    {
        Renderer worst = null;
        float opened = through;
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled || renderer.forceRenderingOff) continue;
            renderer.forceRenderingOff = true;
            float without = Through(stage, picture, out _);
            renderer.forceRenderingOff = false;
            if (without > opened + .05f)
            {
                opened = without;
                worst = renderer;
            }
        }
        if (worst == null) return "no one renderer of the gun (left out one at a time, none opens it)";
        var materials = worst.sharedMaterials;
        string named = null;
        float best = through;
        for (int i = 0; i < materials.Length; i++)
        {
            var swapped = (Material[])materials.Clone();
            swapped[i] = stage.Nothing;
            worst.sharedMaterials = swapped;
            float without = Through(stage, picture, out _);
            if (without > best + .05f)
            {
                best = without;
                named = $"material {i} {Describe(materials[i])}";
            }
        }
        worst.sharedMaterials = materials;
        return $"{worst.name}{(named != null ? "'s " + named : "")} (through {opened:P0} without it)";
    }

    private static string Describe(Material material)
    {
        if (material == null) return "none (an empty slot)";
        string where = AssetDatabase.GetAssetPath(material);
        string how = material.renderQueue < 2450 ? "solid" : material.renderQueue < 2500 ? "cut-out" : "see-through";
        return $"{material.name} [{material.shader.name}, {how}, queue {material.renderQueue}{(where.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ? "" : ", made by the importer from " + Path.GetFileName(where))}]";
    }

    // ---- The mark ----

    // The mark as drawn: the view before a black wall with it and with it put out, by its shader's own number where it
    // has one (a scope's reticle emission, a dot's or a holograph's emissive colour: the glass a holograph's plate also
    // draws then stays in both pictures), else by leaving its renderer out.
    private static void Mark(Stage stage, Renderer window, Action picture, float field, out int count, out int brightest, out Vector2 off)
    {
        stage.Wall(Color.black, false);
        picture?.Invoke();
        var with = stage.Shoot().GetPixels32();
        var block = new MaterialPropertyBlock();
        var material = window.sharedMaterial;
        bool byEmission = material != null && material.HasProperty(ReticleEmissionId);
        bool byColour = !byEmission && material != null && material.HasProperty(EmissiveColourId);
        window.GetPropertyBlock(block);
        if (byEmission) block.SetFloat(ReticleEmissionId, 0f);
        else if (byColour) block.SetColor(EmissiveColourId, Color.black);
        if (byEmission || byColour) window.SetPropertyBlock(block);
        else window.forceRenderingOff = true;
        picture?.Invoke();
        var without = stage.Shoot().GetPixels32();
        window.GetPropertyBlock(block);
        if (byEmission) block.SetFloat(ReticleEmissionId, material.GetFloat(ReticleEmissionId));
        else if (byColour) block.SetColor(EmissiveColourId, material.GetColor(EmissiveColourId));
        if (byEmission || byColour) window.SetPropertyBlock(block);
        else window.forceRenderingOff = false;
        count = brightest = 0;
        double sumX = 0, sumY = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Color32 one = with[y * Width + x], other = without[y * Width + x];
                int more = Mathf.Max(one.r - other.r, Mathf.Max(one.g - other.g, one.b - other.b));
                if (more < 24) continue;
                count++;
                brightest = Mathf.Max(brightest, more);
                sumX += x + .5;
                sumY += y + .5;
            }
        float perPixel = field / Height;
        off = count > 0 ? new Vector2((float)(sumX / count - Width * .5) * perPixel, (float)(sumY / count - Height * .5) * perPixel) : Vector2.zero;
    }

    // ---- Where the pictures are taken ----

    /// <summary>A scene of its own (the editor's preview scene, as GunBench's): two lights, a wall 30 m ahead of the
    /// eye, and the eye.</summary>
    private sealed class Stage : IDisposable
    {
        public readonly Camera Eye;
        /// <summary>A material that draws nothing, to leave one material of a renderer out.</summary>
        public readonly Material Nothing;

        private readonly Scene scene;
        private readonly GameObject root;
        private readonly Transform wall;
        private readonly Material wallMaterial;
        private readonly Texture2D chequers, shot;
        private readonly RenderTexture film;
        private readonly bool wasAsync;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public Stage()
        {
            // Shaders compile as they are needed, so no picture comes out in the placeholder's flat cyan.
            wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Gun Sight Bench") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);

            var eyeObject = new GameObject("Eye") { hideFlags = HideFlags.HideAndDontSave };
            eyeObject.transform.SetParent(root.transform, false);
            Eye = eyeObject.AddComponent<Camera>();
            Eye.enabled = false;
            Eye.clearFlags = CameraClearFlags.SolidColor;
            Eye.backgroundColor = Color.black;
            Eye.allowHDR = false;
            Eye.allowMSAA = false;
            Eye.nearClipPlane = NearClip;
            Eye.farClipPlane = 100f;
            Eye.useOcclusionCulling = false;
            Eye.scene = scene;
            Eye.aspect = Width / (float)Height;
            var data = Eye.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            film = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Gun Sight Bench Film", hideFlags = HideFlags.HideAndDontSave };
            film.Create();
            // Its picture's size is what a scope sizes its own picture from (GunScope.Size reads the view's pixel height).
            Eye.targetTexture = film;
            shot = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };

            AddLight("Over the shoulder", Quaternion.Euler(38f, 18f, 0f), 1.1f);
            AddLight("Front", Quaternion.Euler(22f, 205f, 0f), .9f);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            chequers = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            chequers.SetPixels32(new[] { new Color32(205, 215, 225, 255), new Color32(95, 115, 140, 255), new Color32(95, 115, 140, 255), new Color32(205, 215, 225, 255) });
            chequers.Apply();
            wallMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            var wallObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wallObject.name = "Wall";
            wallObject.hideFlags = HideFlags.HideAndDontSave;
            if (wallObject.TryGetComponent(out Collider solid)) Object.DestroyImmediate(solid);
            wall = wallObject.transform;
            wall.SetParent(root.transform, false);
            // 200 m across, 30 m ahead: squares of 25 cm when chequered.
            wall.localScale = new Vector3(200f, 200f, 1f);
            var wallRenderer = wallObject.GetComponent<Renderer>();
            wallRenderer.sharedMaterial = wallMaterial;
            wallRenderer.shadowCastingMode = ShadowCastingMode.Off;

            Nothing = new Material(unlit) { name = "Nothing", hideFlags = HideFlags.HideAndDontSave };
            Nothing.SetFloat("_Surface", 1f);
            Nothing.SetOverrideTag("RenderType", "Transparent");
            Nothing.SetFloat("_SrcBlend", (float)BlendMode.Zero);
            Nothing.SetFloat("_DstBlend", (float)BlendMode.One);
            Nothing.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            Nothing.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            Nothing.SetFloat("_ZWrite", 0f);
            Nothing.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            Nothing.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            Nothing.SetShaderPassEnabled("DepthOnly", false);
            Nothing.SetShaderPassEnabled("DepthNormalsOnly", false);
            Nothing.SetShaderPassEnabled("ShadowCaster", false);
            Nothing.renderQueue = (int)RenderQueue.Transparent;
        }

        private void AddLight(string name, Quaternion turn, float intensity)
        {
            var holder = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(root.transform, false);
            holder.transform.rotation = turn;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        /// <summary>Makes a gun in the stage's scene (never in the scene the editor has open), named as its prefab is: its
        /// id in the tables is its object's name (GunMounts.IdOf). Lit by the stage alone.</summary>
        public GameObject Stand(GameObject prefab)
        {
            var thing = Object.Instantiate(prefab, root.transform);
            thing.name = prefab.name;
            thing.hideFlags = HideFlags.HideAndDontSave;
            thing.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var flat = new SphericalHarmonicsL2();
            flat.AddAmbientLight(new Color(.45f, .47f, .52f));
            var probes = new[] { flat };
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                    // Skinned again at every picture: outside play nothing else re-skins a mesh between two renders.
                    skinned.forceMatrixRecalculationPerRender = true;
                }
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(probes);
                renderer.SetPropertyBlock(block);
            }
            return thing;
        }

        /// <summary>Puts the eye at a sight's eye point, and the wall 30 m ahead of it, square to it.</summary>
        public void Aim(Vector3 spot, Quaternion turn, float fieldOfView)
        {
            Eye.transform.SetPositionAndRotation(spot, turn);
            Eye.fieldOfView = fieldOfView;
            // A quad shows its face to whoever looks along its own forward.
            wall.SetPositionAndRotation(spot + turn * Vector3.forward * 30f, turn);
        }

        /// <summary>The wall in one colour, or chequered.</summary>
        public void Wall(Color colour, bool chequered)
        {
            wallMaterial.SetTexture("_BaseMap", chequered ? chequers : Texture2D.whiteTexture);
            wallMaterial.SetTextureScale("_BaseMap", chequered ? new Vector2(400f, 400f) : Vector2.one);
            wallMaterial.SetColor("_BaseColor", colour);
        }

        /// <summary>The eye's view now. The texture is the stage's own, written anew at every shot.</summary>
        public Texture2D Shoot()
        {
            var previous = RenderTexture.active;
            try
            {
                Eye.Render();
                RenderTexture.active = film;
                shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                shot.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return shot;
        }

        /// <summary>The middle of a shot as a PNG: where the sight is, at the size it was drawn.</summary>
        public byte[] Middle(Texture2D whole)
        {
            var kept = new Texture2D(KeptWidth, KeptHeight, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                kept.SetPixels(whole.GetPixels((Width - KeptWidth) / 2, (Height - KeptHeight) / 2, KeptWidth, KeptHeight));
                kept.Apply();
                return kept.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(kept);
            }
        }

        public void Dispose()
        {
            Eye.targetTexture = null;
            film.Release();
            Object.DestroyImmediate(film);
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(chequers);
            Object.DestroyImmediate(wallMaterial);
            Object.DestroyImmediate(Nothing);
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = wasAsync;
        }
    }
}
