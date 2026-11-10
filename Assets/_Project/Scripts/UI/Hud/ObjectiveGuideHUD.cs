using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Where to go. The user's pick of the first drafts (2026-11-10, "어디로 가야 하는지", draft B; their words: "레포데는
/// 외곽선으로 세이프하우스나 어디로 가야 하는지 알려 줘요"; until now only the mission bar's words said it). While the
/// step's place is out of view, the HUD's pink diamond stands at the screen's edge on its side, a small arrow past it
/// pointing the way and the place's kind and distance beside it. In view, a smaller diamond floats over the place with
/// the distance, and looking toward it lights the place itself through walls: a doorway of light standing on it and a
/// ring on the floor as wide as "arrived" is. Within the step's reach of the place all of it steps aside, as it does
/// while the player is down or dead. Nothing outside a campaign chapter (CampaignRun.TryObjective). On this PC's
/// player; what it draws lives in the map's scene, so it is made again after a change of map.
/// </summary>
[DisallowMultipleComponent]
public sealed class ObjectiveGuideHUD : MonoBehaviour
{
    // The mark (px at 1080p): how far in from the screen's edge it stops, the diamond at the edge and over the place.
    private const float EdgeMargin = 64f, EdgeDiamond = 40f, PlaceDiamond = 26f;
    // How far over the place the diamond floats (m), and how far past "arrived" the mark fades out (m).
    private const float Over = 2.6f, FadeBeyond = 3f;
    // Looking this near the place lights it fully, and this far not at all (degrees off the view's middle).
    private const float LitWithin = 4f, UnlitBeyond = 20f;
    // The doorway of light (m): its width and height, its lines' thickness; how fast the mark's look follows (1/s).
    private const float DoorWidth = 1.4f, DoorHeight = 2.4f, LineWidth = .09f, Follow = 10f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static Texture2D lineTexture;

    private RectTransform canvas, mark, pointer;
    private CanvasGroup group;
    private DiamondGraphic diamond;
    private TextMeshProUGUI label;
    private Camera view;
    private Transform beacon, door;
    private Renderer[] lines;
    private Renderer ring, fill;
    private Material lineMaterial, ringMaterial, fillMaterial;
    private bool noShader;
    private MaterialPropertyBlock block;
    // How much of it shows (0 there already or nothing to show, 1 on the way), the mark's own opacity where it stands,
    // and how lit the place is.
    private float shown, look = 1f, lit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => lineTexture = null;

    private void BuildMark()
    {
        var host = new GameObject("Objective Guide", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupOverlay(host, 25);
        canvas = host.GetComponent<RectTransform>();
        mark = FeelUI.Rect("Mark", canvas);
        group = mark.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        group.alpha = 0f;
        diamond = HudShapes.Make<DiamondGraphic>("Diamond", mark, KitStyle.Accent);
        pointer = FeelUI.Rect("Pointer", mark);
        pointer.sizeDelta = new Vector2(12f, 20f);
        var arrow = pointer.gameObject.AddComponent<UITriangle>();
        arrow.raycastTarget = false;
        arrow.color = KitStyle.Ice;
        // Pointing along its own right, so one angle turns it to the way out.
        arrow.SetCorners(new Vector2(0f, 1f), new Vector2(1f, .5f), new Vector2(0f, 0f));
        label = FeelUI.ScoreLabel("Distance", mark, 21f, TextAlignmentOptions.Center);
        label.rectTransform.sizeDelta = new Vector2(260f, 28f);
        label.color = KitStyle.Ice;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        if (beacon != null) Destroy(beacon.gameObject);
        if (lineMaterial != null) Destroy(lineMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);
        if (fillMaterial != null) Destroy(fillMaterial);
    }

    private void LateUpdate()
    {
        if (view == null) view = GetComponentInChildren<Camera>();
        float dt = Time.unscaledDeltaTime, wanted = 0f, looked = 0f;
        var player = PlayerRoster.Local;
        bool up = player != null && !player.IsDead && !player.IsDowned;
        if (view != null && up && CampaignRun.TryObjective(out var spot, out float radius, out var kind))
        {
            if (canvas == null) BuildMark();
            if (beacon == null) BuildBeacon();
            Vector3 place = spot.position, from = view.transform.position;
            float distance = Vector3.Distance(transform.position, place);
            // There already: nothing to point at.
            wanted = Mathf.InverseLerp(radius, radius + FadeBeyond, distance);
            if (wanted > 0f)
            {
                Vector3 over = place + Vector3.up * Over;
                looked = 1f - Mathf.InverseLerp(LitWithin, UnlitBeyond, Vector3.Angle(view.transform.forward, over - from));
                Place(over, distance, kind, looked);
                Light(place, from, radius);
            }
        }
        if (canvas == null) return;
        shown = Mathf.MoveTowards(shown, wanted, dt * Follow * .5f);
        lit = Mathf.Lerp(lit, wanted > 0f ? looked : 0f, 1f - Mathf.Exp(-Follow * dt));
        group.alpha = shown * look;
        Glow(shown * lit);
    }

    // The mark: over the place while it is in view, else at the screen's edge on its side, pointing out.
    private void Place(Vector3 over, float distance, CampaignStepKind kind, float looked)
    {
        Vector3 point = view.WorldToViewportPoint(over);
        Vector2 offset = new Vector2(point.x - .5f, point.y - .5f);
        // Behind the camera the projection mirrors, and the way to turn is the other side (as TeammateMarkerHUD has it).
        if (point.z < 0f) offset = -offset;
        Vector2 size = canvas.rect.size, half = size * .5f - new Vector2(EdgeMargin, EdgeMargin), position = Vector2.Scale(offset, size);
        float reach = Mathf.Max(Mathf.Abs(position.x) / half.x, Mathf.Abs(position.y) / half.y);
        bool away = point.z < 0f || reach > 1f;
        if (point.z < 0f && position.sqrMagnitude < 1f) position = Vector2.down * half.y;
        else if (away) position /= Mathf.Max(reach, .001f);
        mark.anchoredPosition = position;

        string metres = Mathf.RoundToInt(distance) + "m";
        diamond.rectTransform.sizeDelta = Vector2.one * (away ? EdgeDiamond : PlaceDiamond);
        pointer.gameObject.SetActive(away);
        if (away)
        {
            Vector2 outward = position.sqrMagnitude > 1f ? position.normalized : Vector2.down;
            pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg);
            pointer.anchoredPosition = outward * (EdgeDiamond * .5f + 14f);
            // The words stand on the inner side: beside the diamond at the left and right edges, under or over it at the others.
            bool side = Mathf.Abs(outward.x) > Mathf.Abs(outward.y);
            label.alignment = !side ? TextAlignmentOptions.Center : outward.x > 0f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            label.rectTransform.anchoredPosition = side ? new Vector2(-Mathf.Sign(outward.x) * (EdgeDiamond * .5f + 142f), 0f)
                : new Vector2(0f, -Mathf.Sign(outward.y) * (EdgeDiamond * .5f + 22f));
            SetText(NameOf(kind) + " " + metres);
            look = 1f;
        }
        else
        {
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchoredPosition = new Vector2(0f, PlaceDiamond * .5f + 18f);
            SetText(metres);
            // Quiet at the edge of the view, plain once it is looked at.
            look = Mathf.Lerp(.5f, 1f, looked);
        }
    }

    private void SetText(string text)
    {
        if (label.text != text) label.text = text;
    }

    private static string NameOf(CampaignStepKind kind) => kind switch
    {
        CampaignStepKind.Checkpoint => Texts.Get("objective.kind.checkpoint"),
        CampaignStepKind.Escape => Texts.Get("objective.kind.escape"),
        CampaignStepKind.Hold => Texts.Get("objective.kind.hold"),
        CampaignStepKind.Collect => Texts.Get("objective.kind.collect"),
        CampaignStepKind.Press => Texts.Get("objective.kind.press"),
        CampaignStepKind.Gather => Texts.Get("objective.kind.gather"),
        _ => Texts.Get("objective.kind.other")
    };

    // ---- The place itself, lit while it is looked at ----

    // The doorway stands on the place, turned to face the player; the ring lies round it as wide as the step's reach.
    private void Light(Vector3 place, Vector3 from, float radius)
    {
        if (beacon == null) return;
        beacon.position = place;
        Vector3 toward = from - place;
        toward.y = 0f;
        if (toward.sqrMagnitude > 1e-4f) door.rotation = Quaternion.LookRotation(toward, Vector3.up);
        // FeelAssets.Ring draws its circle at .82 of the quad's half-size.
        float across = Mathf.Max(1f, radius) * 2f / .82f;
        ring.transform.localScale = new Vector3(across, across, 1f);
    }

    private void Glow(float strength)
    {
        if (beacon == null) return;
        bool on = strength > .01f;
        if (beacon.gameObject.activeSelf != on) beacon.gameObject.SetActive(on);
        if (!on) return;
        float breathe = .85f + .15f * Mathf.Sin(Time.unscaledTime * 3f);
        foreach (var line in lines) Paint(line, KitStyle.With(KitStyle.Ice, .8f * strength * breathe));
        Paint(fill, KitStyle.With(KitStyle.Ice, .1f * strength));
        Paint(ring, KitStyle.With(KitStyle.Accent, .5f * strength));
    }

    private void Paint(Renderer target, Color color)
    {
        block.SetColor(ColorId, color);
        target.SetPropertyBlock(block);
    }

    private void BuildBeacon()
    {
        if (lineMaterial == null)
        {
            // Light drawn through whatever stands in front; without the shader there is only the HUD's mark.
            var shader = noShader ? null : Resources.Load<Shader>("Combat/Shaders/GuideBeacon");
            if (shader == null) { noShader = true; return; }
            block = new MaterialPropertyBlock();
            lineMaterial = Lamp(shader, LineTexture, "Guide Line");
            fillMaterial = Lamp(shader, Texture2D.whiteTexture, "Guide Fill");
            ringMaterial = Lamp(shader, FeelAssets.Ring, "Guide Ring");
        }
        beacon = new GameObject("Objective Beacon").transform;
        RuntimeSceneHierarchy.Group(beacon.gameObject, "EFFECTS", "World", gameObject);
        door = new GameObject("Doorway").transform;
        door.SetParent(beacon, false);
        // Two posts and a lintel: the line's texture is soft across its quad's width, so the lintel is a post on its side.
        lines = new[]
        {
            Quad("Post Left", door, lineMaterial, new Vector3(-DoorWidth * .5f, DoorHeight * .5f, 0f), Quaternion.identity, new Vector3(LineWidth, DoorHeight, 1f)),
            Quad("Post Right", door, lineMaterial, new Vector3(DoorWidth * .5f, DoorHeight * .5f, 0f), Quaternion.identity, new Vector3(LineWidth, DoorHeight, 1f)),
            Quad("Lintel", door, lineMaterial, new Vector3(0f, DoorHeight, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(LineWidth, DoorWidth + LineWidth, 1f))
        };
        fill = Quad("Fill", door, fillMaterial, new Vector3(0f, DoorHeight * .5f, 0f), Quaternion.identity, new Vector3(DoorWidth, DoorHeight, 1f));
        ring = Quad("Ring", beacon, ringMaterial, new Vector3(0f, .04f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
        beacon.gameObject.SetActive(false);
    }

    // Added light (SrcAlpha, One), its own material so the colours set here stay apart from the shared effect ones.
    private static Material Lamp(Shader shader, Texture texture, string name)
    {
        var material = new Material(shader) { name = name + " (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.SetTexture("_MainTex", texture);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        return material;
    }

    private static Renderer Quad(string name, Transform parent, Material material, Vector3 at, Quaternion turn, Vector3 scale)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn;
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    // A line of light: solid along its length, soft across its width.
    private static Texture2D LineTexture
    {
        get
        {
            if (lineTexture != null) return lineTexture;
            const int width = 32, height = 4;
            lineTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Guide Line", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float across = (x + .5f) / width * 2f - 1f;
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Exp(-across * across * 5f) - .007f) * 255f));
            }
            lineTexture.SetPixels32(pixels);
            lineTexture.Apply(false, true);
            return lineTexture;
        }
    }
}
