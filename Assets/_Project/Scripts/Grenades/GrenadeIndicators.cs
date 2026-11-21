using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Where the live grenades round this PC's player are, shown as two Garry's Mod addons show it and kept as they are (the
/// user, 2026-11-22: "원본대로 하라고"). Their numbers are read off the addons' Lua (Tables/Grenades/GrenadeIndicators.csv,
/// 1920x1080 canvas px, Hammer units turned to metres) and their pictures are the addons' own textures, decoded
/// (Resources/UI/GrenadeIndicators):
/// - Direction rows: the indicator of [MW] Quick Grenade and Grenade Indicator (Indicator:DrawFrag). Every grenade gets the
///   frag picture on a circle round the screen's middle, on the side it lies (up = ahead, left = to the left), with an
///   arrow round it turned its way, while it is within reach of the player's feet; white, red when close. A new one slides
///   out from the middle, and every change (place, strength, colour) follows over a tenth of a second.
/// - Arctic rows: Arctic's Grenade Indicators. Every grenade within reach of the eye gets a red triangle on a smaller
///   circle, turned its way, and its own model drawn small in front of the eye over everything, orange and added like
///   light, turning, pushed to the grenade's side the further it is.
/// Both look at every thrown grenade on this PC (ThrownGrenade.Live): this player's own and the copies of friends', as
/// the addons look at every grenade entity. The one departure is the user's rule against flicker (a panic risk): Arctic's
/// triangle blinked on and off 2.4 times a second and its hologram's static jumped 20 times a second; here the triangle
/// breathes in a smooth pulse (pulseHz, never below pulseLow) and the static's frames fade into each other (holoFrames).
/// The cook ring of the same MW addon (Indicator:DrawTimer) is GrenadeHUD's, the one ring round the crosshair.
/// Made with the first throw (Prepare), on a screen overlay of its own.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers + 1)]
public sealed class GrenadeIndicators : MonoBehaviour
{
    public const string TableName = "GrenadeIndicators", RingRow = "Ring", DirectionRow = "Direction", ArcticRow = "Arctic";
    // The decoded addon textures: MW's vgui/frag and vgui/frag_arrow, Arctic's ugi/triangle.png and ugi/holo's frames.
    private const string Pictures = "UI/GrenadeIndicators/";
    private const string HoloShader = "Combat/Shaders/GrenadeHolo";
    private const int StaticFrames = 5, Order = 25;

    /// <summary>MW's indicator (Indicator:DrawFrag): GrenadeIndicators.csv rows Direction.*.</summary>
    [Serializable]
    public sealed class DirectionLook
    {
        public bool on = true;
        /// <summary>Which kinds show (Grenades.csv row ids, between spaces). The addon shows its frag only.</summary>
        public string kinds = "frag smoke flash mist dye";
        /// <summary>The frag picture's side (px), and the arrow's as a multiple of it.</summary>
        public float icon = 45f, arrow = 1.5f;
        /// <summary>From the screen's middle to a picture's (px).</summary>
        public float radius = 216f;
        /// <summary>Shown within reach of the feet (m), red within near.</summary>
        public float reach = 5.715f, near = 4.7625f;
        /// <summary>The time constant place, strength and colour follow by (s), and the strength under which a burst one is gone.</summary>
        public float tau = .1f, gone = .1f;
        public Color color = Color.white, nearColor = Color.red;
    }

    /// <summary>Arctic's Grenade Indicators: GrenadeIndicators.csv rows Arctic.*.</summary>
    [Serializable]
    public sealed class ArcticLook
    {
        public bool on = true;
        public string kinds = "frag smoke flash mist dye";
        /// <summary>Shown within reach of the eye (m).</summary>
        public float reach = 9.7536f;
        /// <summary>The triangle's side and the circle it sits on (px).</summary>
        public float size = 30f, radius = 192f;
        public Color color = Color.red;
        /// <summary>The triangle's pulse (Hz) and its faintest (0 to 1).</summary>
        public float pulseHz = 1.6f, pulseLow = .5f;
        /// <summary>The field of view the addon draws the hologram through (°, Source's: the width of a 4:3 screen).</summary>
        public float holoFov = 100f;
        /// <summary>The hologram's place before the eye (m): ahead, and up and aside at most, times the distance share.</summary>
        public float holoAhead = 1.7145f, holoUp = .9144f, holoSide = 1.2192f;
        /// <summary>Distance share = (distance / reach + holoNear) × holoScale.</summary>
        public float holoNear = .25f, holoScale = .75f;
        /// <summary>Its pitch (°) and its turn about the world's up (°/s).</summary>
        public float holoPitch = 30f, holoSpin = 40f;
        public Color holoColor = new Color(1f, .65f, 0f);
        /// <summary>Its strength (render.SetBlend) and how many static frames a second it moves through.</summary>
        public float holoBlend = .5f, holoFrames = 1.6f;
    }

    // One MW indicator (Indicator.Frags[index]): the grenade it follows, kept after the grenade burst until it faded.
    private sealed class Mark
    {
        public ThrownGrenade grenade;
        public float alpha, distance, turn;
        public Vector2 at;
        public Color color;
        public RawImage icon, arrow;
    }

    // Which kinds a look's cell lists, read again only when the cell changes (a reloaded table).
    private sealed class KindList
    {
        private static readonly char[] Gaps = { ' ', ',', ';', '|', '/' };
        private readonly HashSet<Throwable> kinds = new HashSet<Throwable>();
        private string read;

        public bool Has(string cell, Throwable kind)
        {
            if (!string.Equals(cell, read, StringComparison.Ordinal))
            {
                read = cell;
                kinds.Clear();
                if (!string.IsNullOrEmpty(cell))
                    foreach (string word in cell.Split(Gaps, StringSplitOptions.RemoveEmptyEntries))
                        if (Enum.TryParse(word.Trim(), true, out Throwable found)) kinds.Add(found);
            }
            return kinds.Contains(kind);
        }
    }

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), NextTexId = Shader.PropertyToID("_NextTex"),
        MixId = Shader.PropertyToID("_Mix"), ColorId = Shader.PropertyToID("_Color"), BlendId = Shader.PropertyToID("_Blend");
    private static GrenadeIndicators instance;

    private readonly DirectionLook direction = new DirectionLook();
    private readonly ArcticLook arctic = new ArcticLook();
    private readonly KindList directionKinds = new KindList(), arcticKinds = new KindList();
    private readonly List<Mark> marks = new List<Mark>();
    private readonly List<Mark> spare = new List<Mark>();
    private readonly List<RawImage> triangles = new List<RawImage>();
    private readonly List<MeshFilter> filters = new List<MeshFilter>();
    private readonly Texture2D[] statics = new Texture2D[StaticFrames];
    private Texture2D frag, fragArrow, triangle;
    private RectTransform arcticRoot, directionRoot;
    private Canvas canvas;
    private Material holo;
    private bool holoLooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Made once, during Play, the first time a grenade is thrown on this PC.</summary>
    internal static void Prepare()
    {
        if (!Application.isPlaying || instance != null) return;
        var host = new GameObject("Grenade Indicators", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        instance = host.AddComponent<GrenadeIndicators>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, Order);
        canvas = GetComponent<Canvas>();
        GameTables.BindNested(TableName, DirectionRow, this, direction);
        GameTables.BindNested(TableName, ArcticRow, this, arctic);
        frag = Resources.Load<Texture2D>(Pictures + "frag");
        fragArrow = Resources.Load<Texture2D>(Pictures + "frag_arrow");
        triangle = Resources.Load<Texture2D>(Pictures + "triangle");
        for (int i = 0; i < StaticFrames; i++) statics[i] = Resources.Load<Texture2D>(Pictures + "holo_" + i);
        // Arctic's triangles under MW's pictures: two addons, each drawing in its own hook; MW's are the outer circle.
        arcticRoot = FeelUI.Rect("Arctic", transform);
        directionRoot = FeelUI.Rect("Direction", transform);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (holo != null) Destroy(holo);
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        var view = Camera.main;
        bool seen = view != null;
        if (canvas.enabled != seen) canvas.enabled = seen;
        if (!seen) return;
        var eye = view.transform;
        // The view's heading (Source's EyeAngles().y); its roll and pitch play no part in the bearing.
        float viewYaw = eye.eulerAngles.y;
        var player = PlayerRoster.Local;
        Vector3 feet = player != null ? player.transform.position : eye.position;
        Direction(eye.position, feet, viewYaw);
        Arctic(view, viewYaw);
    }

    // ---- [MW] Quick Grenade: Indicator:DrawFrag ----

    private void Direction(Vector3 eye, Vector3 feet, float viewYaw)
    {
        var look = direction;
        if (!look.on)
        {
            for (int i = marks.Count - 1; i >= 0; i--) Drop(i);
            return;
        }
        // A grenade is added as it first appears (Indicator:Add on OnEntityCreated), near or not, at the screen's middle.
        foreach (var grenade in ThrownGrenade.Live)
            if (grenade != null && !grenade.Done && !Follows(grenade) && directionKinds.Has(look.kinds, grenade.Kind))
                marks.Add(Take(grenade, look));
        // Lerp(FrameTime()/0.1, from, to): the original's frame-time step, as it is.
        float step = Mathf.Clamp01(Time.unscaledDeltaTime / Mathf.Max(1e-4f, look.tau));
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            bool live = mark.grenade != null && !mark.grenade.Done;
            if (!live && mark.alpha < look.gone) { Drop(i); continue; }
            // A burst one keeps its last bearing and place while it fades.
            if (live)
            {
                Vector3 at = mark.grenade.transform.position;
                mark.turn = Turn(eye, at, viewYaw);
                // How far from the feet (ply:GetPos()), though the bearing is from the eye.
                mark.distance = Vector3.Distance(at, feet);
                mark.at = Vector2.Lerp(mark.at, Around(mark.turn, look.radius), step);
            }
            if (!live || mark.distance > look.reach) mark.alpha = Mathf.Lerp(mark.alpha, 0f, step);
            else
            {
                mark.alpha = Mathf.Lerp(mark.alpha, 1f, step);
                mark.color = Color.Lerp(mark.color, mark.distance > look.near ? look.color : look.nearColor, step);
            }
            // surface.SetAlphaMultiplier(alpha) over the colour's own full alpha; the picture upright, the arrow turned.
            var tint = mark.color;
            tint.a = mark.alpha;
            mark.icon.color = mark.arrow.color = tint;
            mark.icon.rectTransform.anchoredPosition = mark.arrow.rectTransform.anchoredPosition = mark.at;
            mark.icon.rectTransform.sizeDelta = Vector2.one * look.icon;
            mark.arrow.rectTransform.sizeDelta = Vector2.one * (look.icon * look.arrow);
            mark.arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, mark.turn);
        }
    }

    private bool Follows(ThrownGrenade grenade)
    {
        foreach (var mark in marks)
            if (mark.grenade == grenade) return true;
        return false;
    }

    private Mark Take(ThrownGrenade grenade, DirectionLook look)
    {
        Mark mark;
        if (spare.Count > 0)
        {
            mark = spare[spare.Count - 1];
            spare.RemoveAt(spare.Count - 1);
        }
        else
        {
            // The arrow made right after the picture, so it lies over it, as the addon draws them.
            mark = new Mark { icon = Picture("Frag", directionRoot, frag), arrow = Picture("Frag arrow", directionRoot, fragArrow) };
        }
        mark.grenade = grenade;
        mark.alpha = mark.distance = mark.turn = 0f;
        mark.at = Vector2.zero;
        mark.color = look.color;
        mark.icon.color = mark.arrow.color = Color.clear;
        mark.icon.gameObject.SetActive(true);
        mark.arrow.gameObject.SetActive(true);
        return mark;
    }

    private void Drop(int index)
    {
        var mark = marks[index];
        marks.RemoveAt(index);
        mark.grenade = null;
        mark.icon.gameObject.SetActive(false);
        mark.arrow.gameObject.SetActive(false);
        spare.Add(mark);
    }

    // ---- Arctic's Grenade Indicators ----

    private void Arctic(Camera view, float viewYaw)
    {
        var look = arctic;
        int used = 0;
        if (look.on)
        {
            var eye = view.transform;
            // The original's clamp(sin(15t), 0, 1) + .1 blinked; a smooth breath at most pulseHz, never fainter than pulseLow.
            float breath = .5f - .5f * Mathf.Cos(Time.time * Mathf.Max(0f, look.pulseHz) * Mathf.PI * 2f);
            var tint = look.color;
            tint.a *= Mathf.Lerp(1f, Mathf.Clamp01(look.pulseLow), breath);
            var material = HoloMaterial(look);
            var squeeze = material != null ? Squeeze(view, look) : Matrix4x4.identity;
            foreach (var grenade in ThrownGrenade.Live)
            {
                if (grenade == null || grenade.Done || !arcticKinds.Has(look.kinds, grenade.Kind)) continue;
                Vector3 at = grenade.transform.position;
                // ents.FindInSphere(LocalPlayer():EyePos(), 512).
                float distance = Vector3.Distance(at, eye.position);
                if (distance > look.reach) continue;
                float turn = Turn(eye.position, at, viewYaw);
                if (material != null) Hologram(grenade, view, look, material, squeeze, turn, distance);
                if (used == triangles.Count) triangles.Add(Picture("Triangle", arcticRoot, triangle));
                var mark = triangles[used++];
                if (!mark.gameObject.activeSelf) mark.gameObject.SetActive(true);
                mark.color = tint;
                mark.rectTransform.anchoredPosition = Around(turn, look.radius);
                mark.rectTransform.sizeDelta = Vector2.one * look.size;
                mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn);
            }
        }
        for (int i = used; i < triangles.Count; i++)
            if (triangles[i].gameObject.activeSelf) triangles[i].gameObject.SetActive(false);
    }

    // The grenade's model, each mesh of it as it sits under the grenade, drawn at the addon's spot before the eye: ahead,
    // then up by the bearing's cosine and aside by its sine (EyeAngles():Up() and Right()), both times the distance share,
    // turned in the world (Angle(30, CurTime() * 40, 0); Source's yaw goes the other way round).
    private void Hologram(ThrownGrenade grenade, Camera view, ArcticLook look, Material material, Matrix4x4 squeeze, float turn,
        float distance)
    {
        var eye = view.transform;
        float share = (Mathf.Clamp(distance, 0f, look.reach) / Mathf.Max(1e-4f, look.reach) + look.holoNear) * look.holoScale;
        float bearing = turn * Mathf.Deg2Rad;
        Vector3 at = eye.position + eye.forward * look.holoAhead + eye.up * (Mathf.Cos(bearing) * look.holoUp * share)
            - eye.right * (Mathf.Sin(bearing) * look.holoSide * share);
        var place = squeeze * Matrix4x4.TRS(at, Quaternion.Euler(look.holoPitch, -look.holoSpin * Time.time, 0f), Vector3.one);
        var root = grenade.transform.worldToLocalMatrix;
        int layer = grenade.gameObject.layer;
        grenade.GetComponentsInChildren(false, filters);
        foreach (var filter in filters)
        {
            var mesh = filter.sharedMesh;
            if (mesh == null || !filter.TryGetComponent(out MeshRenderer body) || !body.enabled) continue;
            var matrix = place * (root * filter.transform.localToWorldMatrix);
            for (int part = 0; part < mesh.subMeshCount; part++)
                Graphics.DrawMesh(mesh, matrix, material, layer, view, part, null, ShadowCastingMode.Off, false);
        }
        filters.Clear();
    }

    // The addon draws the hologram through a camera of its own (cam.Start3D, fov 100), so it keeps its place and size
    // whatever the view's zoom: the view's x and y are scaled about the eye so this camera's projection lands where that
    // one's would. Source's fov is the width of a 4:3 screen; the height's half-tangent is three quarters of it.
    private static Matrix4x4 Squeeze(Camera view, ArcticLook look)
    {
        if (view.orthographic) return Matrix4x4.identity;
        float own = Mathf.Tan(Mathf.Clamp(look.holoFov, 1f, 179f) * .5f * Mathf.Deg2Rad) * .75f;
        float scale = Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) / own;
        var frame = Matrix4x4.TRS(view.transform.position, view.transform.rotation, Vector3.one);
        return frame * Matrix4x4.Scale(new Vector3(scale, scale, 1f)) * frame.inverse;
    }

    // The shared hologram material, set for this frame: the static's two neighbouring frames and how far between them.
    private Material HoloMaterial(ArcticLook look)
    {
        if (holo == null)
        {
            if (holoLooked) return null;
            holoLooked = true;
            var shader = Resources.Load<Shader>(HoloShader);
            if (shader == null || statics[0] == null) return null;
            holo = new Material(shader) { name = "Grenade Holo (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        }
        float frames = Time.time * Mathf.Max(0f, look.holoFrames);
        int now = Mathf.FloorToInt(frames) % StaticFrames, next = (now + 1) % StaticFrames;
        float between = frames - Mathf.Floor(frames);
        holo.SetTexture(MainTexId, statics[now] != null ? statics[now] : statics[0]);
        holo.SetTexture(NextTexId, statics[next] != null ? statics[next] : statics[0]);
        holo.SetFloat(MixId, between * between * (3f - 2f * between));
        holo.SetColor(ColorId, look.holoColor);
        holo.SetFloat(BlendId, look.holoBlend);
        return holo;
    }

    // ---- Shared ----

    // The grenade's bearing from the eye against the view's heading, in degrees, positive to the left: Source's
    // ((pos - eye):Angle() - EyeAngles()).y.
    private static float Turn(Vector3 eye, Vector3 at, float viewYaw)
    {
        Vector3 way = at - eye;
        if (way.x * way.x + way.z * way.z < 1e-8f) return 0f;
        return Mathf.DeltaAngle(Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg, viewYaw);
    }

    // A point on a circle round the screen's middle on the bearing's side: ahead is up, left is left (the original's
    // (cos(θ + 90°), -sin(θ + 90°)) on a screen whose y goes down).
    private static Vector2 Around(float turn, float radius)
    {
        float bearing = turn * Mathf.Deg2Rad;
        return new Vector2(-Mathf.Sin(bearing), Mathf.Cos(bearing)) * radius;
    }

    private static RawImage Picture(string name, Transform parent, Texture texture)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }
}
