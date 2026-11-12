using UnityEngine;

/// <summary>
/// A Support's mending tool in hand while F is held on a worn barricade (SupportKit; the user, 2026-11-12: "처음에는
/// 망치로 고쳐. 망치로 뚝딱뚝딱 고치다가 나중에 카드 업그레이드하면 토치로 더 빠르게 고치는 거지"): a hammer knocking at the planks,
/// or with the welding torch card, a torch held to them with a hissing blue flame and a flickering light. It is in the
/// Support's free left hand on their body, the gun staying in the right (RepairArm plays the stroke or the hold on the left
/// arm and the tool rides in the palm), so their own first-person view, the F5 view and everyone else's show the same
/// thing (PlayerNetworkCombat tells the others when the tool changes). It once sat in the camera's frame, so from outside
/// it floated in the air (the user, 2026-11-12: "3인칭하면 카메라 기준이라 공중에 떠 잇잖아. 내가 원하는 건 남는 손으로 망치 손으로
/// 두들기는 거라고"). The hammer knocks where its head meets the planks, at the stroke's strike, on every PC. There are no
/// models for these in the project, so they are made of simple parts; a model put at Resources/ClassProps/Hammer or
/// Resources/ClassProps/Torch (a prefab, its handle along +y and its business end toward +z) is used instead.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 2)]
public sealed class RepairLooks : MonoBehaviour
{
    /// <summary>What is in hand.</summary>
    public enum Tool : byte { None, Hammer, Torch }

    // The flame's length (m), the light's reach and strength, the hiss's and the knock's loudness and how far it carries (m).
    private const float FlameLength = .13f, LightRange = 2.2f, LightIntensity = 1.6f, HissVolume = .3f, KnockVolume = .75f,
        KnockNear = 1.5f, KnockFar = 25f;
    // Where the hammer's head is on its model (its handle along +y, its face toward +z).
    private static readonly Vector3 HammerHead = new Vector3(0f, .15f, .015f);
    private static readonly Color Wood = new Color(.45f, .3f, .18f), Steel = new Color(.62f, .64f, .68f), Can = new Color(.8f, .14f, .1f),
        Brass = new Color(.78f, .62f, .25f), Flame = new Color(.45f, .7f, 1f);
    private static Material wood, steel, can, brass;
    private static AudioClip hissClip, knockClip;

    private PlayerMovement who;
    private Tool tool, built;
    private Transform prop, tip;
    private LineRenderer flame;
    private Light glow;
    private AudioSource hiss;
    private RepairArm arm;
    private float age;

    /// <summary>This Support holds <paramref name="tool"/> now (None: puts it away).</summary>
    public static void Hold(PlayerMovement who, Tool tool)
    {
        if (!Application.isPlaying || who == null) return;
        if (!who.TryGetComponent(out RepairLooks looks))
        {
            if (tool == Tool.None) return;
            looks = who.gameObject.AddComponent<RepairLooks>();
        }
        looks.who = who;
        if (looks.tool != tool) looks.age = 0f;
        looks.tool = tool;
        looks.enabled = true;
    }

    private void Update()
    {
        if (tool != built) Build();
        if (tool == Tool.None) { enabled = false; return; }
        age += Time.deltaTime;
        // The left arm takes the tool up and plays its stroke (RepairArm); it lets go once this stops asking.
        if (arm == null)
        {
            arm = RepairArm.Of(who);
            if (arm != null) arm.Struck += Knock;
        }
        if (arm == null) return;
        arm.Drive(tool);
        if (prop != null && prop.parent != arm.Holder) arm.Wear(prop);
    }

    // After the arm has put the hand where it is this frame: the flame from the nozzle as it now is.
    private void LateUpdate()
    {
        if (flame == null || tip == null) return;
        float flicker = .85f + .15f * Mathf.PerlinNoise(age * 23f, 0f);
        flame.SetPosition(0, tip.position);
        flame.SetPosition(1, tip.position + tip.forward * FlameLength * flicker);
        flame.startColor = KitStyle.With(Color.white, .95f);
        flame.endColor = KitStyle.With(Flame, .5f);
        glow.intensity = LightIntensity * flicker;
    }

    private void OnDisable()
    {
        if (built != Tool.None) { tool = Tool.None; Build(); }
    }

    private void OnDestroy()
    {
        if (arm != null) arm.Struck -= Knock;
        if (prop != null) Destroy(prop.gameObject);
    }

    // The hammer met the planks (the stroke's strike, RepairArm): a knock there, as the others hear it too.
    private void Knock()
    {
        if (tool != Tool.Hammer || prop == null) return;
        GameAudio.PlayAt(KnockClip(), prop.TransformPoint(HammerHead), AudioChannel.Combat, KnockVolume, KnockNear, KnockFar);
    }

    // The tool's parts (or the model at Resources/ClassProps/<tool>), made when it changes.
    private void Build()
    {
        if (prop != null) Destroy(prop.gameObject);
        prop = null;
        tip = null;
        flame = null;
        glow = null;
        hiss = null;
        built = tool;
        if (tool == Tool.None) return;
        prop = new GameObject(tool.ToString()).transform;
        var model = Resources.Load<GameObject>("ClassProps/" + tool);
        if (model != null) Instantiate(model, prop, false);
        else if (tool == Tool.Hammer)
        {
            Part("Handle", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(.028f, .15f, .028f), Paint(Wood, ref wood));
            Part("Head", PrimitiveType.Cube, new Vector3(0f, .15f, .015f), new Vector3(.035f, .035f, .11f), Paint(Steel, ref steel));
            Part("Claw", PrimitiveType.Cube, new Vector3(0f, .14f, -.06f), new Vector3(.03f, .02f, .05f), Paint(Steel, ref steel), Quaternion.Euler(-25f, 0f, 0f));
        }
        else
        {
            Part("Canister", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(.06f, .09f, .06f), Paint(Can, ref can));
            Part("Valve", PrimitiveType.Cylinder, new Vector3(0f, .1f, 0f), new Vector3(.03f, .015f, .03f), Paint(Brass, ref brass));
            Part("Nozzle", PrimitiveType.Cylinder, new Vector3(0f, .13f, .06f), new Vector3(.014f, .07f, .014f), Paint(Steel, ref steel), Quaternion.Euler(60f, 0f, 0f));
        }
        if (tool == Tool.Torch)
        {
            tip = new GameObject("Tip").transform;
            tip.SetParent(prop, false);
            tip.localPosition = new Vector3(0f, .165f, .125f);
            tip.localRotation = Quaternion.Euler(60f, 0f, 0f);
            flame = tip.gameObject.AddComponent<LineRenderer>();
            flame.useWorldSpace = true;
            flame.positionCount = 2;
            flame.numCapVertices = 3;
            flame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flame.receiveShadows = false;
            flame.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
            flame.widthCurve = new AnimationCurve(new Keyframe(0f, .035f), new Keyframe(1f, .005f));
            glow = new GameObject("Light").AddComponent<Light>();
            glow.transform.SetParent(tip, false);
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.color = Flame;
            glow.range = LightRange;
            hiss = tip.gameObject.AddComponent<AudioSource>();
            hiss.clip = Hiss();
            hiss.loop = true;
            hiss.volume = HissVolume;
            hiss.spatialBlend = who != null && who.IsLocal ? 0f : 1f;
            hiss.minDistance = 2f;
            hiss.maxDistance = 25f;
            GameAudio.Bind(hiss, AudioChannel.Combat);
            hiss.Play();
        }
        foreach (var renderer in prop.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        // Into the palm on the next Update (RepairArm.Wear); a body without a humanoid arm shows none rather than one in the air.
        if (arm != null) arm.Wear(prop);
        else prop.gameObject.SetActive(RepairArm.Of(who) != null);
    }

    private void Part(string name, PrimitiveType shape, Vector3 at, Vector3 size, Material material, Quaternion? turn = null)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(prop, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn ?? Quaternion.identity;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Paint(Color color, ref Material made)
    {
        if (made != null) return made;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        made = new Material(shader) { name = "Repair Tool (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Color")) made.SetColor("_Color", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", color == Wood ? .2f : .7f);
        return made;
    }

    // The torch's hiss, made once: soft noise with a slow waver, looped.
    private static AudioClip Hiss()
    {
        if (hissClip != null) return hissClip;
        const int Rate = 44100;
        var data = new float[Rate];
        var random = new System.Random(4127);
        float held = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            held = Mathf.Lerp(held, noise, .35f);
            float waver = .8f + .2f * Mathf.Sin(2f * Mathf.PI * 3f * i / Rate);
            // Ease in and out over the loop's ends so it joins without a click.
            float edge = Mathf.Clamp01(Mathf.Min(i, data.Length - 1 - i) / 400f);
            data[i] = held * .5f * waver * Mathf.Lerp(.85f, 1f, edge);
        }
        hissClip = AudioClip.Create("Torch Hiss", data.Length, 1, Rate, false);
        hissClip.SetData(data, 0);
        return hissClip;
    }

    // The hammer's knock, made once: the planks' dull thump, the head's short ring and the strike's click.
    private static AudioClip KnockClip()
    {
        if (knockClip != null) return knockClip;
        const int Rate = 44100;
        var data = new float[Rate * 18 / 100];
        var random = new System.Random(911);
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            float thump = Mathf.Sin(2f * Mathf.PI * 170f * t) * Mathf.Exp(-t * 38f);
            float ring = Mathf.Sin(2f * Mathf.PI * 2350f * t) * Mathf.Exp(-t * 60f) * .35f;
            float click = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 300f) * .6f;
            data[i] = (thump * .8f + ring + click) * .8f;
        }
        knockClip = AudioClip.Create("Hammer Knock", data.Length, 1, Rate, false);
        knockClip.SetData(data, 0);
        return knockClip;
    }
}
