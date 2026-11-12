using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// What a Détente's healing looks like (the user, 2026-11-12: "데탕트 주사기 모델링이 없는데 휠 클릭하면 주사기 들게 할 수
/// 있나?" and the Guts &amp; Blackpowder surgeon's offer they asked for). There are no models for these, so they are made
/// here of simple parts, once:
/// - Syringe: the class key's dose. Seen from inside, the syringe comes up from below into view, its plunger goes home
///   and the red dose in its barrel empties, and it drops away again; anyone else sees it in the Détente's right hand.
/// - Bandage: the treatment by hand, as long as it lasts. Seen from inside, a roll of bandage winds round and round low in
///   view; anyone else sees it turning in the Détente's hand.
/// - Asked: a friend who called for healing (Callouts request.heal or request.medic), seen by a Détente: a red glow
///   behind them and a red cross over their head while the call holds, so the Détente finds them, and an offer to them
///   needs no answer (DetenteKit).
/// - The answer: the line at the foot of the screen of whoever is offered a treatment, "[F] … 치료 받기" (Prompt).
/// Every PC draws its own: DetenteKit on the Détente's PC, PlayerNetworkCombat for what the others see.
/// </summary>
public sealed class DetenteLooks : MonoBehaviour
{
    private enum Kind : byte { Syringe, Bandage, Asked }

    // The syringe's whole run (s) and when its plunger starts and ends; how long the bandage takes to drop away (s).
    private const float SyringeSeconds = .75f, PlungeFrom = .17f, PlungeTo = .45f, BandageOut = .25f;
    // The Asked look: the halo's size (m), the cross's arm and thickness (m), its height over the feet (m).
    private const float HaloWidth = 1.4f, HaloHeight = 2.2f, CrossArm = .2f, CrossThick = .065f, CrossHeight = 2.25f;
    private static readonly Color Dose = new Color(.95f, .16f, .2f, 1f), Plastic = new Color(.9f, .92f, .95f, 1f),
        Steel = new Color(.75f, .77f, .8f, 1f), Gauze = new Color(.96f, .95f, .9f, 1f);
    private static readonly Dictionary<PlayerMovement, DetenteLooks> asked = new Dictionary<PlayerMovement, DetenteLooks>();
    private static Material plastic, steel, dose, gauze;
    private static Canvas promptCanvas;
    private static TextMeshProUGUI promptLabel;

    private Kind kind;
    private PlayerMovement who;
    private bool inside;
    private float age, life;
    private Transform prop, plunger, fluid;
    private Mesh mesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        asked.Clear();
        plastic = steel = dose = gauze = null;
        promptCanvas = null;
        promptLabel = null;
    }

    /// <summary>A dose goes in: the syringe in this Détente's hand (from inside on their own PC).</summary>
    public static void Syringe(PlayerMovement detente) => Begin(Kind.Syringe, detente, SyringeSeconds);

    /// <summary>A treatment by hand under way for <paramref name="seconds"/>: the bandage in this Détente's hands.</summary>
    public static void Bandage(PlayerMovement detente, float seconds) => Begin(Kind.Bandage, detente, Mathf.Max(.3f, seconds) + BandageOut);

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

    /// <summary>Whether this PC shows <paramref name="friend"/>'s call for healing now.</summary>
    public static bool IsAsking(PlayerMovement friend) => friend != null && asked.TryGetValue(friend, out var look) && look != null;

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

    private static DetenteLooks Begin(Kind kind, PlayerMovement who, float seconds)
    {
        if (!Application.isPlaying || who == null) return null;
        var host = FeelAssets.CreateRuntimeRoot("Detente " + kind);
        var look = host.AddComponent<DetenteLooks>();
        look.kind = kind;
        look.who = who;
        look.inside = who.IsLocal && kind != Kind.Asked;
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

    // The syringe or the bandage, built of plain parts: from inside, held below the view in the camera's frame; from
    // outside, at the Détente's right hand.
    private void Prop()
    {
        prop = new GameObject(kind.ToString()).transform;
        prop.SetParent(transform, false);
        if (kind == Kind.Syringe)
        {
            // Along +z, the needle forward: barrel, the red dose in it, the plunger behind, the finger grips, the needle.
            Part("Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(.024f, .045f, .024f), Plastic, prop);
            fluid = Part("Dose", PrimitiveType.Cylinder, new Vector3(0f, 0f, .006f), new Vector3(.0256f, .03f, .0256f), Dose, prop);
            plunger = new GameObject("Plunger").transform;
            plunger.SetParent(prop, false);
            Part("Rod", PrimitiveType.Cylinder, new Vector3(0f, 0f, -.07f), new Vector3(.008f, .028f, .008f), Plastic, plunger);
            Part("Thumb", PrimitiveType.Cylinder, new Vector3(0f, 0f, -.099f), new Vector3(.026f, .0025f, .026f), Plastic, plunger);
            Part("Grips", PrimitiveType.Cube, new Vector3(0f, 0f, -.044f), new Vector3(.05f, .012f, .004f), Plastic, prop, false);
            Part("Hub", PrimitiveType.Cylinder, new Vector3(0f, 0f, .05f), new Vector3(.01f, .006f, .01f), Steel, prop);
            Part("Needle", PrimitiveType.Cylinder, new Vector3(0f, 0f, .074f), new Vector3(.0025f, .02f, .0025f), Steel, prop);
        }
        else
        {
            Part("Roll", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.06f, .035f, .06f), Gauze, prop, false);
            Part("Tail", PrimitiveType.Cube, new Vector3(.035f, -.05f, 0f), new Vector3(.004f, .1f, .05f), Gauze, prop, false);
        }
        if (!inside) prop.localScale = Vector3.one * 1.6f;
        foreach (var renderer in prop.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    // One part: a built-in shape, its own colour, laid along z unless told not to (a cylinder stands along y).
    private static Transform Part(string name, PrimitiveType shape, Vector3 at, Vector3 size, Color color, Transform parent, bool alongZ = true)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localRotation = alongZ && shape == PrimitiveType.Cylinder ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
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

    // Where it is this frame.
    private void Place()
    {
        if (kind == Kind.Asked) { Draw(); return; }
        if (prop == null) return;
        if (inside)
        {
            var eye = Camera.main;
            if (eye == null) return;
            prop.SetParent(eye.transform, false);
            if (kind == Kind.Syringe) SyringeInside();
            else BandageInside();
            return;
        }
        // From outside: at the right hand of the Détente's body, the needle or roll toward what they face.
        var hand = Hand(who);
        Vector3 at = hand != null ? hand.position : who.transform.position + Vector3.up * 1.1f + who.transform.right * .25f;
        prop.SetParent(transform, false);
        prop.position = at + who.transform.forward * .06f;
        prop.rotation = kind == Kind.Syringe
            ? Quaternion.LookRotation(who.transform.forward + Vector3.down * .35f)
            : Quaternion.LookRotation(who.transform.forward) * Quaternion.Euler(0f, 0f, age * 540f);
    }

    // Up from below the view, the plunger home and the dose gone, down again.
    private void SyringeInside()
    {
        float up = Mathf.Clamp01(age / PlungeFrom), down = Mathf.Clamp01((age - PlungeTo) / (SyringeSeconds - PlungeTo));
        float raise = KitReveal.EaseOut(up) * (1f - down * down);
        Vector3 low = new Vector3(.2f, -.34f, .3f), aim = new Vector3(.09f, -.13f, .4f);
        prop.localPosition = Vector3.Lerp(low, aim, raise);
        prop.localRotation = Quaternion.Slerp(Quaternion.Euler(-25f, -30f, 10f), Quaternion.Euler(12f, -10f, -6f), raise);
        float push = Mathf.Clamp01((age - PlungeFrom) / (PlungeTo - PlungeFrom));
        if (plunger != null) plunger.localPosition = new Vector3(0f, 0f, .034f * push);
        if (fluid != null)
        {
            fluid.localScale = new Vector3(.0256f, Mathf.Max(.002f, .03f * (1f - push)), .0256f);
            fluid.localPosition = new Vector3(0f, 0f, .006f + .03f * push);
        }
    }

    // The roll winding round low in view, the hands at work, dropping away at the end.
    private void BandageInside()
    {
        float into = KitReveal.EaseOut(Mathf.Clamp01(age / .25f)), outOf = Mathf.Clamp01((age - (life - BandageOut)) / BandageOut);
        float turn = age * Mathf.PI * 2f / .65f;
        Vector3 middle = new Vector3(.02f, -.2f, .42f) + new Vector3(Mathf.Cos(turn) * .07f, Mathf.Sin(turn) * .045f, 0f);
        Vector3 low = new Vector3(.15f, -.4f, .35f);
        prop.localPosition = Vector3.Lerp(low, middle, into * (1f - outOf));
        prop.localRotation = Quaternion.Euler(70f, 0f, -turn * Mathf.Rad2Deg * .5f);
    }

    // The Détente's body's right hand (a humanoid's), or null.
    private static Transform Hand(PlayerMovement who)
    {
        foreach (var animator in who.GetComponentsInChildren<Animator>())
            if (animator != null && animator.isHuman && animator.avatar != null)
            {
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null) return hand;
            }
        return null;
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
