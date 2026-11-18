using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// What a Détente's healing looks like (the user, 2026-11-12: "데탕트 주사기 모델링이 없는데 휠 클릭하면 주사기 들게 할 수
/// 있나?" and the Guts &amp; Blackpowder surgeon's offer they asked for). There are no models for these, so they are made
/// here of simple parts, once:
/// - Syringe: the class key's dose, a thick adrenaline syringe rather than a slim one (the user, 2026-11-12: "일반
///   주사기가 아니라 좀 두꺼운 주사기였으면 좋겠는데 아드레날린 주사기처럼"), an orange band at its neck. It is in the Détente's
///   free left hand on their body, the gun staying in the right (InjectArm plays the motion on the left arm and the
///   syringe rides in the fist): a dose into itself goes into the left thigh, a dose into a friend is thrust at their
///   arm, and the plunger goes home and the red dose empties as the needle is in. So their own first-person view, the F5
///   view and everyone else's show the same thing. It once rode the camera on a fixed path with no arm moving, and from
///   outside hung by the right hand beside the gun (the user, 2026-11-18: "카메라 기준이 아닌 True FPS 캐릭터 신체/손
///   기준으로 주사기가 사용되도록").
/// - Bandage: the treatment by hand, as long as it lasts: a roll of bandage in the same left hand, wound round and round
///   toward the friend (InjectArm's loop).
/// - Asked: a friend who called for healing (Callouts request.heal or request.medic), seen by a Détente: a red glow
///   behind them and a red cross over their head while the call holds, so the Détente finds them, and an offer to them
///   needs no answer (DetenteKit).
/// - The answer: the line at the foot of the screen of whoever is offered a treatment, "[F] … 치료 받기" (Prompt).
/// Every PC draws its own: DetenteKit on the Détente's PC, PlayerNetworkCombat for what the others see (it says who was
/// dosed, so a friend's dose is aimed at that friend on every PC). A model put at Resources/ClassProps/Syringe or
/// Resources/ClassProps/Bandage (a prefab in the tool's axes: the syringe from its needle, -y, to its plunger, +y; the
/// roll's axis along y) is used in place of the made one, without the plunger's play.
/// </summary>
public sealed class DetenteLooks : MonoBehaviour
{
    private enum Kind : byte { Syringe, Bandage, Asked }

    // How long a dose's look may last at most (s: its motion is the arm's, which ends by itself), and how long the bandage
    // takes to be put away once the treatment ends (s).
    private const float SyringeMost = 2.5f, BandageOut = .25f;
    // The Asked look: the halo's size (m), the cross's arm and thickness (m), its height over the feet (m).
    private const float HaloWidth = 1.4f, HaloHeight = 2.2f, CrossArm = .2f, CrossThick = .065f, CrossHeight = 2.25f;
    private static readonly Color Dose = new Color(.95f, .16f, .2f, 1f), Plastic = new Color(.9f, .92f, .95f, 1f),
        Steel = new Color(.75f, .77f, .8f, 1f), Gauze = new Color(.96f, .95f, .9f, 1f), Band = new Color(1f, .55f, .1f, 1f);
    private static readonly Dictionary<PlayerMovement, DetenteLooks> asked = new Dictionary<PlayerMovement, DetenteLooks>();
    private static Material plastic, steel, dose, gauze, band;
    private static Canvas promptCanvas;
    private static TextMeshProUGUI promptLabel;

    private Kind kind;
    private PlayerMovement who, patient;
    private float age, life;
    private Transform prop, plunger, fluid;
    private InjectArm arm;
    // The dose's motion was begun: once the arm is done with it the syringe goes.
    private bool began;
    private Mesh mesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        asked.Clear();
        plastic = steel = dose = gauze = band = null;
        promptCanvas = null;
        promptLabel = null;
    }

    /// <summary>
    /// A dose goes in: the syringe in this Détente's left hand, into <paramref name="patient"/> (null or the Détente:
    /// into its own thigh).
    /// </summary>
    public static void Syringe(PlayerMovement detente, PlayerMovement patient) => Begin(Kind.Syringe, detente, SyringeMost, patient);

    /// <summary>A treatment by hand under way for <paramref name="seconds"/>: the bandage in this Détente's left hand, wound toward <paramref name="patient"/>.</summary>
    public static void Bandage(PlayerMovement detente, float seconds, PlayerMovement patient = null)
        => Begin(Kind.Bandage, detente, Mathf.Max(.3f, seconds) + BandageOut, patient);

    /// <summary>A treatment called off early: the bandage drops away now.</summary>
    public static void StopBandage(PlayerMovement detente)
    {
        foreach (var look in FindObjectsByType<DetenteLooks>(FindObjectsSortMode.None))
            if (look.kind == Kind.Bandage && look.who == detente) look.life = Mathf.Min(look.life, look.age + BandageOut);
    }

    /// <summary>
    /// <paramref name="friend"/> called for healing: on this PC (a Détente's), a red glow and cross on them for
    /// <paramref name="seconds"/>, or until Unask.
    /// </summary>
    public static void Ask(PlayerMovement friend, float seconds)
    {
        if (friend == null) return;
        if (asked.TryGetValue(friend, out var look) && look != null) { look.life = look.age + seconds; return; }
        asked[friend] = Begin(Kind.Asked, friend, seconds);
    }

    /// <summary>The call is answered (healed, or not a Détente here any more): the glow and cross go.</summary>
    public static void Unask(PlayerMovement friend)
    {
        if (friend != null && asked.TryGetValue(friend, out var look) && look != null) Destroy(look.gameObject);
        if (friend != null) asked.Remove(friend);
    }

    /// <summary>The line at the foot of this PC's screen (an offered treatment's answer); null or empty hides it.</summary>
    public static void Prompt(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (promptCanvas != null) promptCanvas.enabled = false;
            return;
        }
        if (promptCanvas == null)
        {
            var host = new GameObject("Detente Prompt", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler)) { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(host);
            promptCanvas = host.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            promptCanvas.sortingOrder = 70;
            var scaler = host.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var rect = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(host.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 250f);
            rect.sizeDelta = new Vector2(900f, 40f);
            promptLabel = KitStyle.Label(rect, text, 28f, TextAlignmentOptions.Center, KitStyle.Ice);
        }
        promptCanvas.enabled = true;
        if (promptLabel.text != text) promptLabel.text = text;
        promptLabel.color = KitStyle.With(KitStyle.Ice, .8f + .2f * Mathf.Sin(Time.unscaledTime * 7f));
    }

    private static DetenteLooks Begin(Kind kind, PlayerMovement who, float seconds, PlayerMovement patient = null)
    {
        if (!Application.isPlaying || who == null) return null;
        // One syringe, one roll in a hand: a dose given before the last one's arm was back at the gun takes its place.
        if (kind != Kind.Asked)
            foreach (var earlier in FindObjectsByType<DetenteLooks>(FindObjectsSortMode.None))
                if (earlier.kind == kind && earlier.who == who) Destroy(earlier.gameObject);
        var host = FeelAssets.CreateRuntimeRoot("Detente " + kind);
        var look = host.AddComponent<DetenteLooks>();
        look.kind = kind;
        look.who = who;
        look.patient = patient;
        look.life = seconds;
        if (kind == Kind.Asked) look.Shapes();
        else look.Prop();
        look.Place();
        return look;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life || who == null)
        {
            if (kind == Kind.Asked && who != null && asked.TryGetValue(who, out var look) && look == this) asked.Remove(who);
            Destroy(gameObject);
            return;
        }
        Place();
    }

    private void OnDestroy()
    {
        if (prop != null) Destroy(prop.gameObject);
        if (mesh != null) Destroy(mesh);
    }

    // ---- The props ----

    // The syringe or the bandage, built of plain parts in the tool's own axes (y its handle: the syringe from its needle
    // at -y to its plunger at +y, the roll's axis), to ride in the Détente's left fist (InjectArm.Wear).
    private void Prop()
    {
        prop = new GameObject(kind.ToString()).transform;
        prop.SetParent(transform, false);
        var model = Resources.Load<GameObject>("ClassProps/" + kind);
        if (model != null)
            // Held on a body, it brings nothing that would make it part of the world (WornParts: a pack's prefab comes with colliders).
            WornParts.Strip(Instantiate(model, prop, false));
        else if (kind == Kind.Syringe)
        {
            // A thick barrel (3.4 cm across, 12 cm long), the red dose in it, an orange band at its neck, the plunger behind,
            // the finger grips, the needle: its tip 12.5 cm under the middle of the barrel (Tables/InjectArms tip).
            Part("Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(.034f, .06f, .034f), Plastic, prop);
            fluid = Part("Dose", PrimitiveType.Cylinder, new Vector3(0f, -.008f, 0f), new Vector3(.0356f, .045f, .0356f), Dose, prop);
            Part("Band", PrimitiveType.Cylinder, new Vector3(0f, -.058f, 0f), new Vector3(.037f, .006f, .037f), Band, prop);
            plunger = new GameObject("Plunger").transform;
            plunger.SetParent(prop, false);
            Part("Rod", PrimitiveType.Cylinder, new Vector3(0f, .095f, 0f), new Vector3(.012f, .035f, .012f), Plastic, plunger);
            Part("Thumb", PrimitiveType.Cylinder, new Vector3(0f, .132f, 0f), new Vector3(.042f, .004f, .042f), Plastic, plunger);
            Part("Grips", PrimitiveType.Cube, new Vector3(0f, .062f, 0f), new Vector3(.085f, .006f, .016f), Plastic, prop);
            Part("Hub", PrimitiveType.Cylinder, new Vector3(0f, -.07f, 0f), new Vector3(.016f, .008f, .016f), Steel, prop);
            Part("Needle", PrimitiveType.Cylinder, new Vector3(0f, -.1f, 0f), new Vector3(.0035f, .025f, .0035f), Steel, prop);
        }
        else
        {
            Part("Roll", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.06f, .035f, .06f), Gauze, prop);
            Part("Tail", PrimitiveType.Cube, new Vector3(.035f, 0f, .05f), new Vector3(.004f, .05f, .1f), Gauze, prop);
        }
        foreach (var renderer in prop.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        // In the fist on the next Place (InjectArm.Wear); a body without a humanoid arm shows none rather than one in the air.
        prop.gameObject.SetActive(false);
    }

    // One part: a built-in shape (a cylinder stands along y, the tool's handle), its own colour. Its collider goes at once:
    // left for the frame's end, it stood in the hand as part of the world (WornParts).
    private static Transform Part(string name, PrimitiveType shape, Vector3 at, Vector3 size, Color color, Transform parent)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = Paint(color);
        return part.transform;
    }

    private static Material Paint(Color color)
    {
        ref Material made = ref plastic;
        if (color == Dose) made = ref dose;
        else if (color == Steel) made = ref steel;
        else if (color == Gauze) made = ref gauze;
        else if (color == Band) made = ref band;
        if (made != null) return made;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        made = new Material(shader) { name = "Detente Prop (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Color")) made.SetColor("_Color", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", color == Gauze ? .1f : .75f);
        if (color == Dose && made.HasProperty("_EmissionColor"))
        {
            made.EnableKeyword("_EMISSION");
            made.SetColor("_EmissionColor", color * .6f);
        }
        return made;
    }

    // This frame: the Détente's left arm does it (InjectArm), the thing in its fist; the plunger and the dose as far as the
    // arm's needle has been in.
    private void Place()
    {
        if (kind == Kind.Asked) { Draw(); return; }
        if (prop == null) return;
        if (arm == null) arm = InjectArm.Of(who);
        if (arm == null) return;
        if (arm.Holder == null || prop.parent != arm.Holder)
        {
            arm.Wear(prop);
            prop.gameObject.SetActive(true);
        }
        if (kind == Kind.Bandage)
        {
            // While the treatment lasts; then the arm goes back to the gun by itself and the roll is put away with it.
            if (age < life - BandageOut) arm.Wind(patient);
            return;
        }
        if (!began)
        {
            began = true;
            arm.Dose(patient);
        }
        else if (arm.Playing != InjectArm.Kind.Self && arm.Playing != InjectArm.Kind.Friend) life = age;
        float push = arm.Plunge;
        if (plunger != null) plunger.localPosition = new Vector3(0f, -.045f * push, 0f);
        if (fluid != null)
        {
            // Its front end stays at the neck while the plunger drives the back of it forward.
            fluid.localScale = new Vector3(.0356f, Mathf.Max(.002f, .045f * (1f - push)), .0356f);
            fluid.localPosition = new Vector3(0f, -.008f - .045f * push, 0f);
        }
    }

    // ---- A friend's call for healing ----

    private void Shapes()
    {
        var host = new GameObject("Shapes") { hideFlags = HideFlags.DontSave };
        host.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Detente Asked", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        // Two submeshes: the soft glow behind them, and the solid cross over their head.
        renderer.sharedMaterials = new[] { FeelAssets.Particle(FeelAssets.SoftDot, true), FeelAssets.Particle(Texture2D.whiteTexture, true) };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // A red glow behind them and a red cross over their head, turned to the eye, beating.
    private void Draw()
    {
        var eye = Camera.main;
        if (eye == null || mesh == null || who == null) return;
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        Vector3 feet = who.transform.position, right = eye.transform.right, up = eye.transform.up;
        Vector3 toEye = eye.transform.position - feet;
        toEye.y = 0f;
        Vector3 back = toEye.sqrMagnitude > 1e-4f ? -toEye.normalized * .25f : Vector3.zero;
        float beat = .75f + .25f * Mathf.Sin(age * 6f), fade = Mathf.Clamp01((life - age) / .4f) * Mathf.Clamp01(age / .2f);
        Quad(feet + back + Vector3.up * (HaloHeight * .5f), right * (HaloWidth * .5f), Vector3.up * (HaloHeight * .5f),
            KitStyle.With(KitStyle.Red, .3f * beat * fade));
        int glowEnd = triangles.Count;
        Vector3 top = feet + Vector3.up * CrossHeight;
        Color red = KitStyle.With(KitStyle.Red, .95f * fade);
        Quad(top, right * CrossArm, up * (CrossThick * .5f), red);
        Quad(top, right * (CrossThick * .5f), up * CrossArm, red);
        mesh.Clear();
        mesh.subMeshCount = 2;
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles.GetRange(0, glowEnd), 0);
        mesh.SetTriangles(triangles.GetRange(glowEnd, triangles.Count - glowEnd), 1);
        mesh.RecalculateBounds();
    }

    private void Quad(Vector3 middle, Vector3 across, Vector3 upward, Color color)
    {
        int start = vertices.Count;
        vertices.Add(middle - across - upward);
        vertices.Add(middle - across + upward);
        vertices.Add(middle + across + upward);
        vertices.Add(middle + across - upward);
        Color32 shade = color;
        for (int i = 0; i < 4; i++) colors.Add(shade);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }
}
