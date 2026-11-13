using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

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
/// - Exposed for the piece (2026-11-12, dark earrings lost on the plates: "검어서 그런가? 진짜 안 보이는데"): its first
///   picture after it stands is read back small and the mean brightness of what it covers measured; a dark piece's
///   pictures are then lifted toward the table's Exposure.target (at most Exposure.most times, and no further than lets
///   its brightest twentieth pass Exposure.ceiling), whatever passes Exposure.knee bent softly under white (the
///   Hidden/ENFILADE/OutfitExposure shader). Lighting can't do it: the Ene earrings' shine is lilToon's matcap, which no
///   light touches, over a base of .2 (silver) or .03 (black). A piece light enough is left as rendered.
/// </summary>
public sealed partial class OutfitStudio : IDisposable
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
    // The exposure's shader (Art/Resources/UI/OutfitExposure.shader, so it ships), the side of the small copy it is measured
    // on (px), and how much of a pixel must be covered to count.
    private const string ExposureShader = "Hidden/ENFILADE/OutfitExposure";
    private const int MeterSide = 64;
    private const float MeterCover = .1f;
    private static readonly int GainId = Shader.PropertyToID("_Gain"), KneeId = Shader.PropertyToID("_Knee");
    private static Material lift;
    private static bool liftTried;

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
    private Texture2D meter;
    // A dressed character's idle pose (StandDressed), held until it goes.
    private PlayableGraph pose;
    private Vector3 front = Vector3.forward, middle;
    // How much the standing piece's pictures are lifted, measured on its first one (1: as rendered).
    private float gain = 1f;
    private bool metered;

    /// <summary>A piece stands, ready to be photographed.</summary>
    public bool HasPiece => sitter != null && shown.Count > 0;
    /// <summary>How many times brighter the standing piece's pictures come out (1: as rendered), once one was taken.</summary>
    public float Gain => gain;
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
        if (!metered) Meter();
        var lifting = gain > 1.001f ? Lift : null;
        if (lifting != null)
        {
            lifting.SetFloat(GainId, gain);
            lifting.SetFloat(KneeId, OutfitPictureLook.ExposureKnee);
            Graphics.Blit(film, target, lifting);
        }
        else Graphics.Blit(film, target);
        return true;
    }
}
