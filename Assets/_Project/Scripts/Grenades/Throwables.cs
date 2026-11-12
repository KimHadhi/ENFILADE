using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// The class throwables, one per class, thrown with G (PlayerGrenades; the user, 2026-11-12: "클래스별로 수류탄이
/// 달랐으면 좋겠고 gmod 호드 모드처럼 G키로 퀵 그레네이드 던질 수 있었음 좋겠어"). Which class carries which is the
/// table's class column (Tables/Grenades); a class without a row has none yet. Dye (the Schießhund's bounty dye) and
/// Claymore (the Support's improvised directional mine, placed rather than thrown) came from the user's picks of 11-12.
/// </summary>
public enum Throwable : byte { None, Knife, Frag, Smoke, Flash, Mist, Dye, Claymore }

/// <summary>One throwable's numbers: a row of Tables/Grenades. Unused cells of a kind stay at their defaults.</summary>
public sealed class ThrowableInfo
{
    public Throwable Kind;
    public string Id = "", Class = "", Callout = "";
    /// <summary>Its kill feed picture's id (WeaponIcons: Resources/UI/WeaponIcons/&lt;style&gt;/&lt;id&gt;.png), its model's name by default.</summary>
    public string Icon = "";
    /// <summary>Most carried, and how many a fresh kit starts with (a chapter, a class pick).</summary>
    public int Carry = 3;
    /// <summary>How many an ammo pile on the field gives (FieldPickups), and a Support's resupply.</summary>
    public int Refill = 1;
    /// <summary>Seconds from the throw to the burst (from the pin, for one that cooks). 0: none (the knife).</summary>
    public float Fuse = 1.5f;
    /// <summary>Holding G runs its fuse from the pin (the Assault's frag, the Point's flashbang): let go when it should burst.</summary>
    public bool Cooks;
    /// <summary>
    /// One that cooks, held to the end of its fuse: true, the hand throws it on reflex at the last moment (the Assault
    /// counts the fuse in their head and the hand lets go by itself, so a frag never goes off in it); false, it goes off in
    /// the hand (the Point's flashbang, which only staggers the zombies round its holder). Tables/Grenades cookOut.
    /// </summary>
    public bool ThrowsOut;
    /// <summary>
    /// Where it sits in the left hand (GrenadeArm), in hand lengths (wrist to middle knuckle): x across to the little
    /// finger's side, y out of the palm, z along to the fingers; and its turn there (degrees, in the same axes).
    /// </summary>
    public Vector3 HeldAt = new Vector3(.12f, .6f, .9f), HeldTurn = new Vector3(0f, 0f, 90f);
    /// <summary>
    /// The model's own point the hand holds at HeldAt (m, in the model's axes; zero: its origin), so a small hand and a
    /// big one close on the same place of it: the MW knife is held near its point, as the addon's viewmodel holds it, while
    /// its origin stays the middle it spins about in flight. Tables/Grenades gripX/Y/Z.
    /// </summary>
    public Vector3 Grip;
    /// <summary>
    /// Set down where the crosshair is rather than thrown (the Support's claymore; the user: "던지는 느낌보단 설치로 가도
    /// 되긴 하겠다"): held, G shows where it would go and what it would guard; let go, the hand puts it there.
    /// Tables/Grenades places.
    /// </summary>
    public bool Places;
    /// <summary>Its hand motion (GrenadeArmPath.Motion, a row of Tables/ThrowArms); empty is the grenade's.</summary>
    public string Arm = "";
    public float Speed = 16f, Lift = 2.5f, Gravity = 1f, Bounce = .3f, Size = .08f;
    public float Radius = 5f, Damage, Edge, Head, Seconds, Posture, Knock, Heal, Push, PlayerDamage, PlayerEdge, Noise, Price;
    public Color Tint = Color.white;

    /// <summary>Its name (Texts grenade.&lt;id&gt;.name): the HUD, the shop, the kill feed.</summary>
    public string Name => Texts.Get("grenade." + Id + ".name");
}

/// <summary>
/// What every PC knows of the class throwables: their numbers (Tables/Grenades and the rules row of
/// Tables/GrenadeRules), which a class carries, their models (Resources/Combat/GrenadeLibrary), sounds (the Grenades sound
/// bank, Art/Audio/Grenades: CS:GO's from the ArcCW GSO addon), HUD icons drawn here, what a player last threw (the kill
/// feed names it), and the colliders bullets pass (AttackTrace).
/// </summary>
public static class Throwables
{
    public const string TableName = "Grenades", RulesTable = "GrenadeRules", RulesRow = "Default", Bank = "Grenades";

    private static readonly Dictionary<Throwable, ThrowableInfo> kinds = new Dictionary<Throwable, ThrowableInfo>();
    private static readonly HashSet<Collider> thrown = new HashSet<Collider>();
    private static readonly Dictionary<PlayerMovement, Throwable> lastThrown = new Dictionary<PlayerMovement, Throwable>();
    private static readonly Dictionary<Throwable, Texture2D> icons = new Dictionary<Throwable, Texture2D>();
    private static readonly Dictionary<Color, Material> looks = new Dictionary<Color, Material>();
    private static bool loaded, libraryLooked, soundsLooked;
    private static GrenadeLibrary library;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
    private static SoundBank sounds;
    private static int coverMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        kinds.Clear();
        thrown.Clear();
        lastThrown.Clear();
        icons.Clear();
        looks.Clear();
        loaded = libraryLooked = soundsLooked = false;
        library = null;
        sounds = null;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A table saved while playing: read again at the next ask.
    private static void Forget()
    {
        kinds.Clear();
        loaded = false;
    }

    // ---- Numbers ----

    /// <summary>A kind's numbers, or null for None and for a kind the table lacks.</summary>
    public static ThrowableInfo Of(Throwable kind)
    {
        Load();
        return kinds.TryGetValue(kind, out var info) ? info : null;
    }

    /// <summary>What a class carries (Tables/Grenades' class column); None when it has nothing yet.</summary>
    public static Throwable ForClass(string classId)
    {
        Load();
        if (string.IsNullOrEmpty(classId)) return Throwable.None;
        foreach (var pair in kinds)
            if (string.Equals(pair.Value.Class, classId, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        return Throwable.None;
    }

    /// <summary>A number of the rules row (Tables/GrenadeRules, row Default).</summary>
    public static float Rule(string name, float fallback)
        => GameTables.TryGetRow(RulesTable, RulesRow, out var row) ? Number(row, name, fallback) : fallback;

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        kinds.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            if (!Enum.TryParse(id, true, out Throwable kind) || kind == Throwable.None) continue;
            var info = new ThrowableInfo
            {
                Kind = kind, Id = id.Trim().ToLowerInvariant(), Class = Text(row, "class"), Callout = Text(row, "callout"),
                Carry = Mathf.Max(0, Mathf.RoundToInt(Number(row, "carry", 3f))),
                Refill = Mathf.Max(0, Mathf.RoundToInt(Number(row, "refill", 1f))),
                Fuse = Mathf.Max(0f, Number(row, "fuse", 1.5f)), Cooks = Number(row, "cooks", 0f) > .5f,
                Speed = Mathf.Max(1f, Number(row, "speed", 16f)), Lift = Number(row, "lift", 2.5f),
                Gravity = Mathf.Max(0f, Number(row, "gravity", 1f)), Bounce = Mathf.Clamp01(Number(row, "bounce", .3f)),
                Size = Mathf.Max(.01f, Number(row, "size", .08f)), Radius = Mathf.Max(0f, Number(row, "radius", 5f)),
                Damage = Number(row, "damage", 0f), Edge = Number(row, "edge", 0f), Head = Number(row, "head", 0f),
                Seconds = Mathf.Max(0f, Number(row, "seconds", 0f)), Posture = Number(row, "posture", 0f),
                Knock = Number(row, "knock", 0f), Heal = Number(row, "heal", 0f), Push = Number(row, "push", 0f),
                PlayerDamage = Number(row, "playerDamage", 0f), PlayerEdge = Number(row, "playerEdge", 0f),
                Noise = Number(row, "noise", 0f), Price = Number(row, "price", 0f),
            };
            if (ColorUtility.TryParseHtmlString(Text(row, "tint"), out Color tint)) info.Tint = tint;
            info.ThrowsOut = string.Equals(Text(row, "cookOut"), "throw", StringComparison.OrdinalIgnoreCase);
            info.Places = Number(row, "places", 0f) > .5f;
            info.Arm = Text(row, "arm");
            info.HeldAt = new Vector3(Number(row, "heldAcross", info.HeldAt.x), Number(row, "heldPalm", info.HeldAt.y),
                Number(row, "heldAlong", info.HeldAt.z));
            info.HeldTurn = new Vector3(Number(row, "heldTurnX", info.HeldTurn.x), Number(row, "heldTurnY", info.HeldTurn.y),
                Number(row, "heldTurnZ", info.HeldTurn.z));
            info.Grip = new Vector3(Number(row, "gripX", 0f), Number(row, "gripY", 0f), Number(row, "gripZ", 0f));
            info.Icon = Text(row, "icon");
            if (info.Icon.Length == 0) info.Icon = DefaultIcon(kind);
            kinds[kind] = info;
        }
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string text) && text != null ? text.Trim() : "";

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;

    // ---- Who threw what ----

    /// <summary>Someone threw this (their own PC, or their copy here): the kill feed names it for their kills.</summary>
    public static void NoteThrower(PlayerMovement who, Throwable kind)
    {
        if (who != null && kind != Throwable.None) lastThrown[who] = kind;
    }

    /// <summary>
    /// The kill feed's weapon for a throwable's hit (GunFeelDirector): the thrower's throwable by name. A thrown hit
    /// that strikes in flight is a knife's and a burst a frag's, should the thrower be unknown here.
    /// </summary>
    public static string FeedName(in DamageHit hit)
    {
        var who = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        var kind = Throwable.None;
        if (who != null) lastThrown.TryGetValue(who, out kind);
        if (kind == Throwable.None || (hit.Method == DamageMethod.Thrown) != (kind == Throwable.Knife))
            kind = hit.Method == DamageMethod.Thrown ? Throwable.Knife : Throwable.Frag;
        var info = Of(kind);
        return info != null ? info.Name : Texts.Get("feed.weapon.fist");
    }

    /// <summary>
    /// The kill feed's picture for a throwable's hit (GunFeelDirector.WeaponIcon): the thrower's throwable's icon id. Until
    /// that picture exists a burst shows the blast's (WeaponIcons.Explosion) and a knife its name.
    /// </summary>
    public static string FeedIcon(in DamageHit hit)
    {
        var who = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        var kind = Throwable.None;
        if (who != null) lastThrown.TryGetValue(who, out kind);
        if (kind == Throwable.None || (hit.Method == DamageMethod.Thrown) != (kind == Throwable.Knife))
            kind = hit.Method == DamageMethod.Thrown ? Throwable.Knife : Throwable.Frag;
        var info = Of(kind);
        string id = info != null ? info.Icon : DefaultIcon(kind);
        if (WeaponIcons.For(id) != null) return id;
        return hit.Method == DamageMethod.Blast ? WeaponIcons.Explosion : id;
    }

    // A kind's picture is named after its model (Art/Models/Addons/Grenades/<Name>), which the icon tool draws from.
    private static string DefaultIcon(Throwable kind)
    {
        switch (kind)
        {
            case Throwable.Knife: return "ThrowingKnife";
            case Throwable.Frag: return "GrenadeFrag";
            case Throwable.Smoke: return "GrenadeSmoke";
            case Throwable.Flash: return "GrenadeFlash";
            case Throwable.Mist: return "GrenadeCanister";
            case Throwable.Dye: return "GrenadeDye";
            case Throwable.Claymore: return "Claymore";
        }
        return "";
    }

    // ---- Bodies in flight ----

    /// <summary>A thrown body's collider: bullets and aim go through it (AttackTrace).</summary>
    public static bool Passes(Collider collider) => collider != null && thrown.Contains(collider);

    internal static void Mark(Collider collider)
    {
        if (collider != null) thrown.Add(collider);
    }

    internal static void Unmark(Collider collider)
    {
        if (!ReferenceEquals(collider, null)) thrown.Remove(collider);
    }

    /// <summary>The world a throw stops against and a burst is shielded by: not players, corpses, pieces or zombies.</summary>
    public static int WorldMask => ThrownStone.WorldMask;

    /// <summary>Something solid between two points: a wall, a floor or a car takes a burst for whoever is behind it.</summary>
    public static bool Covered(Vector3 from, Vector3 to)
    {
        if (coverMask == 0) coverMask = ~(LayerMask.GetMask("Enemy") | 1 << 31);
        return Physics.Linecast(from, to, coverMask, QueryTriggerInteraction.Ignore);
    }

    // ---- Models ----

    /// <summary>A kind's model (GrenadeLibrary), or null to draw it from plain shapes.</summary>
    public static GameObject Model(Throwable kind)
    {
        if (!libraryLooked)
        {
            libraryLooked = true;
            library = Resources.Load<GrenadeLibrary>(GrenadeLibrary.ResourcePath);
        }
        if (library == null) return null;
        switch (kind)
        {
            case Throwable.Knife: return library.knife;
            case Throwable.Frag: return library.frag;
            case Throwable.Smoke: return library.smoke;
            case Throwable.Flash: return library.flash;
            case Throwable.Mist: return library.mist;
            // The bounty dye is the smoke canister, dyed in its own colour (Dress).
            case Throwable.Dye: return library.smoke;
            case Throwable.Claymore: return library.claymore;
        }
        return null;
    }

    /// <summary>
    /// A copy of a kind's look under parent, without colliders or scripts: its model, or plain shapes in its colour when
    /// the model is missing (a capsule for the canisters, a sphere for the frag, a long thin box for the knife).
    /// </summary>
    public static GameObject Dress(Throwable kind, Transform parent)
    {
        var model = Model(kind);
        GameObject look;
        if (model != null)
        {
            look = Object.Instantiate(model, parent, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.identity;
            foreach (var part in look.GetComponentsInChildren<Collider>(true)) Object.Destroy(part);
        }
        else
        {
            var info = Of(kind);
            float size = info != null ? info.Size : .08f;
            var shape = kind == Throwable.Frag ? PrimitiveType.Sphere : kind == Throwable.Knife ? PrimitiveType.Cube : PrimitiveType.Capsule;
            look = GameObject.CreatePrimitive(shape);
            Object.Destroy(look.GetComponent<Collider>());
            look.transform.SetParent(parent, false);
            look.transform.localScale = kind == Throwable.Knife ? new Vector3(.008f, .04f, .24f)
                : kind == Throwable.Frag ? Vector3.one * size : new Vector3(size * .7f, size * .75f, size * .7f);
            look.GetComponent<Renderer>().sharedMaterial = Look(info != null ? info.Tint : Color.grey);
        }
        look.name = "Look";
        foreach (var renderer in look.GetComponentsInChildren<Renderer>(true))
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        // The bounty dye wears the smoke canister in the dye's own teal (its row's tint), laid over the canister's paint.
        if (kind == Throwable.Dye && model != null)
        {
            var info = Of(kind);
            Color dye = info != null ? info.Tint : new Color(.12f, .92f, .82f);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in look.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, dye);
                block.SetColor(ColorId, dye);
                renderer.SetPropertyBlock(block);
            }
        }
        return look;
    }

    /// <summary>A plain lit material in this colour, made once.</summary>
    public static Material Look(Color color)
    {
        if (looks.TryGetValue(color, out var made) && made != null) return made;
        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        made = new Material(lit) { hideFlags = HideFlags.DontSave, name = "Throwable " + ColorUtility.ToHtmlStringRGB(color) };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", .35f);
        looks[color] = made;
        return made;
    }

    // ---- Sounds ----

    /// <summary>A clip of the Grenades bank's key (Art/Audio/Grenades/&lt;key&gt;) with its Sounds.csv volume and a pitch; null when missing.</summary>
    public static AudioClip Clip(string key, out float volume, out float pitch)
    {
        volume = pitch = 1f;
        if (!soundsLooked)
        {
            soundsLooked = true;
            sounds = SoundBank.Load(Bank);
        }
        if (sounds == null) return null;
        var clip = sounds.Pick(key);
        if (clip == null) return null;
        var settings = sounds.SettingsOf(key);
        volume = settings.Volume;
        pitch = settings.RandomPitch();
        return clip;
    }

    /// <summary>
    /// A sound out in the world at the combat channel's volume, heard to far (m), full within near. pitchScale bends it
    /// (the Détente's mist hisses higher than the Ghost's smoke). Returns the source, which removes itself when done.
    /// </summary>
    public static AudioSource Play(string key, Vector3 at, float level = 1f, float near = 2f, float far = 60f, float pitchScale = 1f,
        Transform follow = null)
    {
        var clip = Clip(key, out float volume, out float pitch);
        if (clip == null || level <= 0f) return null;
        var host = new GameObject("Throwable Sound " + key);
        if (follow != null) host.transform.SetParent(follow, false);
        host.transform.position = at;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = Mathf.Max(.1f, near);
        source.maxDistance = Mathf.Max(source.minDistance + 1f, far);
        source.dopplerLevel = 0f;
        source.pitch = Mathf.Max(.1f, pitch * pitchScale);
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume * level);
        GameAudio.Bind(source, AudioChannel.Combat);
        source.Play();
        Object.Destroy(host, clip.length / source.pitch + .1f);
        return source;
    }

    // ---- HUD icons ----

    /// <summary>A kind's white silhouette for the HUD (drawn here, 96 px): a knife's blade and ring, a frag's body and spoon,
    /// a canister, a flashbang's banded tube, a canister with a cross.</summary>
    public static Texture2D Icon(Throwable kind)
    {
        if (icons.TryGetValue(kind, out var made) && made != null) return made;
        const int size = 96;
        made = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Throwable Icon " + kind, hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Centred, y up, one unit across the icon.
                float u = (x + .5f) / size - .5f, v = (y + .5f) / size - .5f;
                float d = Shape(kind, u, v);
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(.5f - d * size));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        made.SetPixels32(pixels);
        made.Apply(false, true);
        icons[kind] = made;
        return made;
    }

    // Signed distance (icon units, negative inside) of a kind's silhouette.
    private static float Shape(Throwable kind, float u, float v)
    {
        switch (kind)
        {
            case Throwable.Knife:
            {
                // A blade leaning right, its point up, over a grip that ends in a ring.
                float c = Mathf.Cos(-.5f), s = Mathf.Sin(-.5f);
                float x = u * c - v * s, y = u * s + v * c;
                float blade = Mathf.Max(Mathf.Abs(x) - .075f * Mathf.Clamp01((.42f - y) / .5f) - .004f, Mathf.Max(-y - .02f, y - .42f));
                float grip = Box(x, y + .14f, .045f, .12f);
                float ring = Mathf.Abs(Circle(x, y + .33f, .075f)) - .025f;
                return Mathf.Min(blade, Mathf.Min(grip, ring));
            }
            case Throwable.Frag:
            {
                float body = Circle(u / .85f, (v + .08f) / 1f, .3f) * .85f;
                float fuse = Box(u, v - .27f, .07f, .06f);
                float spoon = Box(u + .14f, v - .1f, .035f, .2f);
                return Mathf.Min(body, Mathf.Min(fuse, spoon));
            }
            case Throwable.Flash:
            {
                float tube = Box(u, v + .02f, .14f, .32f) - .02f;
                // Three dark bands cut across the tube.
                float bands = Mathf.Abs(Mathf.Repeat(v + .06f, .18f) - .09f) - .022f;
                float cut = Mathf.Max(tube, -Mathf.Max(bands, Mathf.Abs(v + .02f) - .22f));
                float fuse = Box(u, v - .36f, .06f, .05f);
                return Mathf.Min(cut, fuse);
            }
            case Throwable.Dye:
            {
                // The canister with a drop of dye cut out of its side.
                float can = Box(u, v + .03f, .19f, .3f) - .035f;
                float cap = Box(u, v - .33f, .09f, .04f);
                float drop = Mathf.Min(Circle(u, v + .07f, .085f), Box(u, v + .01f - .06f, .045f * Mathf.Clamp01((.13f - v) / .12f), .06f));
                return Mathf.Max(Mathf.Min(can, cap), -drop);
            }
            case Throwable.Claymore:
            {
                // A wide charge bowed toward its front, standing on two splayed legs.
                float bow = v - .1f + .09f * (u / .38f) * (u / .38f);
                float body = Mathf.Max(Mathf.Abs(bow) - .12f, Mathf.Abs(u) - .38f);
                float legLeft = Box((u + .22f) * .97f + (v + .28f) * .25f, v + .28f, .025f, .11f);
                float legRight = Box((u - .22f) * .97f - (v + .28f) * .25f, v + .28f, .025f, .11f);
                return Mathf.Min(body, Mathf.Min(legLeft, legRight));
            }
            case Throwable.Smoke:
            case Throwable.Mist:
            {
                float can = Box(u, v + .03f, .19f, .3f) - .035f;
                float cap = Box(u, v - .33f, .09f, .04f);
                float shape = Mathf.Min(can, cap);
                if (kind == Throwable.Mist)
                {
                    // The healer's cross cut out of its side.
                    float cross = Mathf.Min(Box(u, v + .03f, .11f, .035f), Box(u, v + .03f, .035f, .11f));
                    shape = Mathf.Max(shape, -cross);
                }
                return shape;
            }
        }
        return Circle(u, v, .3f);
    }

    private static float Circle(float x, float y, float r) => Mathf.Sqrt(x * x + y * y) - r;

    private static float Box(float x, float y, float halfWidth, float halfHeight)
    {
        float dx = Mathf.Abs(x) - halfWidth, dy = Mathf.Abs(y) - halfHeight;
        float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(dx, dy), 0f);
    }

    /// <summary>A small random spin, so two of the same never tumble alike.</summary>
    public static Vector3 Spin(float most) => Random.insideUnitSphere * most;
}
