using UnityEngine;

/// <summary>
/// A Support's mending tool in hand while F is held on a worn barricade (SupportKit; the user, 2026-11-12: "처음에는
/// 망치로 고쳐. 망치로 뚝딱뚝딱 고치다가 나중에 카드 업그레이드하면 토치로 더 빠르게 고치는 거지"): a hammer going up and down,
/// or with the welding torch card, a torch held to the planks with a hissing blue flame and a flickering light. Seen from
/// inside, low in view in the camera's frame; anyone else sees it in the Support's right hand (PlayerNetworkCombat tells
/// them when it changes). There are no models for these in the project, so they are made of simple parts; a model put
/// at Resources/ClassProps/Hammer or Resources/ClassProps/Torch (a prefab, its handle along +y and its business end
/// toward +z) is used instead. The knock of the hammer is SupportKit's, in time with its mending.
/// </summary>
[DisallowMultipleComponent]
public sealed class RepairLooks : MonoBehaviour
{
    /// <summary>What is in hand.</summary>
    public enum Tool : byte { None, Hammer, Torch }

    // The hammer's beat (strokes a second), the flame's length (m), the light's reach and strength.
    private const float Strokes = 2.2f, FlameLength = .13f, LightRange = 2.2f, LightIntensity = 1.6f, HissVolume = .3f;
    private static readonly Color Wood = new Color(.45f, .3f, .18f), Steel = new Color(.62f, .64f, .68f), Can = new Color(.8f, .14f, .1f),
        Brass = new Color(.78f, .62f, .25f), Flame = new Color(.45f, .7f, 1f);
    private static Material wood, steel, can, brass;
    private static AudioClip hissClip;

    private PlayerMovement who;
    private Tool tool, built;
    private Transform prop, tip;
    private LineRenderer flame;
    private Light glow;
    private AudioSource hiss;
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
        Place();
    }

    private void OnDisable()
    {
        if (built != Tool.None) { tool = Tool.None; Build(); }
    }

    private void OnDestroy()
    {
        if (prop != null) Destroy(prop.gameObject);
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
        if (who == null || !who.IsLocal) prop.localScale = Vector3.one * 1.4f;
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

    // From inside: low in view, the hammer rising and falling, the torch held to the work. From outside: at the right hand.
    private void Place()
    {
        if (prop == null || who == null) return;
        float stroke = tool == Tool.Hammer ? Mathf.Abs(Mathf.Sin(age * Mathf.PI * Strokes)) : 0f;
        if (who.IsLocal)
        {
            var eye = Camera.main;
            if (eye == null) return;
            if (prop.parent != eye.transform) prop.SetParent(eye.transform, false);
            prop.localPosition = tool == Tool.Hammer ? new Vector3(.17f, -.25f, .42f) : new Vector3(.13f, -.24f, .36f);
            prop.localRotation = tool == Tool.Hammer ? Quaternion.Euler(-20f + 75f * stroke, -15f, 8f) : Quaternion.Euler(-35f, -10f, 0f);
        }
        else
        {
            if (prop.parent != transform) prop.SetParent(transform, false);
            var hand = Hand(who);
            prop.position = (hand != null ? hand.position : who.transform.position + Vector3.up * 1.1f + who.transform.right * .25f)
                + who.transform.forward * .05f;
            prop.rotation = Quaternion.LookRotation(who.transform.forward) * Quaternion.Euler(tool == Tool.Hammer ? -20f + 75f * stroke : -35f, 0f, 0f);
        }
        if (flame != null)
        {
            float flicker = .85f + .15f * Mathf.PerlinNoise(age * 23f, 0f);
            flame.SetPosition(0, tip.position);
            flame.SetPosition(1, tip.position + tip.forward * FlameLength * flicker);
            flame.startColor = KitStyle.With(Color.white, .95f);
            flame.endColor = KitStyle.With(Flame, .5f);
            glow.intensity = LightIntensity * flicker;
        }
    }

    // The Support's body's right hand (a humanoid's), or null.
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
}
