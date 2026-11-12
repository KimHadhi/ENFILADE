using TMPro;
using UnityEngine;

/// <summary>
/// The special moments' looks in the world, as the user picked them on 2026-11-12 (MomentLook has the language). Every PC
/// draws the same thing in the same place: the one who did it sees what their friends see, from where they stand (the
/// user: "내가 보는 화면이 진실이라서 남이 보는 세상과 … 똑같았으면"). Each is a few lit lines, flat plates or a sigil, over in about
/// a second, nothing left lying:
/// <list type="bullet">
/// <item>An ultimate fired (Ultimate): the notice on every HUD (MomentHUD), the class's stinger, and the class's sigil
/// drawn on the ground round them, turning into place and stamped. The Détente's white cross runs out over the ground
/// to its reach. A white tiger's head stands in outline before a Ghost. A Point's guns heat (GunHeat). A Knight's
/// shadow double steps out (BodyDouble, through CardFx's Shadow).</item>
/// <item>A Desperado bounty (BountyGrand). The bounty's nested diamond draws itself round the head and locks. A seal
/// of ticks runs over the ground. Teal light bursts out round the body (the user: "죽었을 때 뭔가 터지는 듯이 주변에
/// 이펙트"). A hairline stands up into the sky, seen across the map, and a few gold glints fall. A plain bounty: a small
/// CLAIMED plate slammed by the head (BountyPlain).</item>
/// <item>A reversal: a pink swoosh where the zombie is thrown over, a ring on the ground, chips. A cover's parry: the
/// blow turned aside in white and violet, a shield's flash, a ring. A Gun Kata draw: an orange arc round the gun as it
/// comes out.</item>
/// <item>A CHARGE's reach: a gold ring of ticks running out over the ground. The phantom cavalry: a wedge of gold plates
/// sliding along the ground at the riders' pace.</item>
/// <item>A Broken Arrow shell: its target drawn on the ground first, a bright streak falling, the target bursting.</item>
/// <item>The gauntlet's flash: a star and the cone it stuns, on the ground.</item>
/// <item>The White Tiger's claws: three curved wounds raked across the head, one after another.</item>
/// </list>
/// Real time throughout. Numbers: Tables/MomentFx.csv.
/// </summary>
public sealed class MomentFx : MonoBehaviour
{
    public enum Kind : byte { GroundSigil, Expand, Tiger, BountyGrand, BountyPlain, Reversal, CoverDeflect, KataDraw, ChargeRing, Wedge, Shell, Flash, Claws }

    private static readonly Color Teal = new Color(.122f, .922f, .82f), Gold = new Color(1f, .8f, .22f), PaleBlue = new Color(.62f, .86f, 1f),
        Pink = new Color(1f, .31f, .47f), Violet = new Color(.663f, .545f, 1f);

    private Kind kind;
    private Vector3 at, direction, extra;
    private float value, age, life;
    private Color colour;
    private MomentLook.Sigil sigil;
    private int seed;
    private bool landed;
    private FxShapes shapes, matter;
    private TextMeshPro word;

    // ---- The ultimate ----

    /// <summary>
    /// <paramref name="who"/> fired their ultimate (ClassCards on their PC; the others hear of it through CardNet). On this
    /// PC: the notice, the stinger, and the class's own look in the world. <paramref name="mine"/>: this PC's own player.
    /// </summary>
    public static void Ultimate(PlayerMovement who, string classId, bool mine)
    {
        if (!Application.isPlaying || who == null) return;
        MomentHUD.Notice(who, classId);
        Vector3 feet = who.transform.position;
        MomentSound.Play(classId, mine ? (Vector3?)null : feet);
        if (!MomentLook.On("Ult")) return;
        Color tint = MomentLook.ColorOf(classId);
        Vector3 facing = Vector3.ProjectOnPlane(who.transform.forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
        facing.Normalize();
        float radius = MomentLook.Number("Ult", "radius", 1.7f);
        switch ((classId ?? "").ToLowerInvariant())
        {
            case PlayerClass.Detente:
                Play(Kind.Expand, feet, facing, MomentLook.Number("Ult", "detenteReach", 6f), tint, MomentLook.Sigil.Detente);
                break;
            case PlayerClass.Assault:
                // The band leads; the wedge rides with the cavalry (CardFx) and everyone it took glows (ChargeGlow).
                break;
            case PlayerClass.Ghost:
                Play(Kind.GroundSigil, feet, facing, radius, tint, MomentLook.Sigil.Ghost);
                Play(Kind.Tiger, feet + facing * MomentLook.Number("Ult", "tigerAhead", 2.6f) + Vector3.up * MomentLook.Number("Ult", "tigerHigh", 2.2f),
                    facing, MomentLook.Number("Ult", "tigerSize", 2.2f), Color.white, MomentLook.Sigil.Tiger);
                break;
            case PlayerClass.Point:
                Play(Kind.GroundSigil, feet, facing, radius, tint, MomentLook.Sigil.Point);
                GunHeat.Kindle(who, MomentLook.Number("Heat", "seconds", 8f));
                break;
            default:
                Play(Kind.GroundSigil, feet, facing, radius, tint, MomentLook.SigilOf(classId));
                break;
        }
    }

    // ---- Playing one ----

    /// <summary>
    /// Draws <paramref name="kind"/> at <paramref name="where"/> facing or going <paramref name="way"/>. <paramref name="size"/>
    /// is its size, reach or radius (m); <paramref name="other"/> is a second point some need (a bounty's feet).
    /// </summary>
    public static MomentFx Play(Kind kind, Vector3 where, Vector3 way, float size, Color tint, MomentLook.Sigil mark = MomentLook.Sigil.Star, Vector3 other = default)
    {
        if (!Application.isPlaying) return null;
        var host = FeelAssets.CreateRuntimeRoot("Moment " + kind);
        host.transform.position = where;
        var fx = host.AddComponent<MomentFx>();
        fx.kind = kind;
        fx.at = where;
        fx.direction = way.sqrMagnitude > 1e-6f ? way.normalized : Vector3.forward;
        fx.value = size;
        fx.colour = tint;
        fx.sigil = mark;
        fx.extra = other;
        fx.seed = Mathf.Abs(Mathf.RoundToInt(where.x * 73f + where.y * 151f + where.z * 37f)) % 9973;
        fx.life = kind switch
        {
            Kind.GroundSigil => 1.15f,
            Kind.Expand => .75f,
            Kind.Tiger => 1.05f,
            Kind.BountyGrand => 1.1f,
            Kind.BountyPlain => .55f,
            Kind.Reversal => .55f,
            Kind.CoverDeflect => .4f,
            Kind.KataDraw => .32f,
            Kind.ChargeRing => .5f,
            Kind.Wedge => Mathf.Max(.2f, size) / CardFx.CavalrySpeed + CardFx.CavalryIn + CardFx.CavalryOut,
            Kind.Shell => CardFx.ShellFall + .45f,
            Kind.Flash => .45f,
            Kind.Claws => .5f,
            _ => 1f,
        };
        fx.shapes = new FxShapes(host.transform, "Light", true);
        if (kind == Kind.BountyPlain || kind == Kind.CoverDeflect) fx.matter = new FxShapes(host.transform, "Matter", false);
        if (kind == Kind.BountyPlain) fx.Word(MomentLook.Text("Bounty", "plainWord", "CLAIMED"), Teal);
        fx.Draw();
        return fx;
    }

    private void Update()
    {
        age += Time.unscaledDeltaTime;
        if (kind == Kind.Shell && !landed && age >= CardFx.ShellFall)
        {
            landed = true;
            CanBlastEffect.Play(at, Mathf.Max(1f, value), 0f);
            GameAudio.PlayAt(CardFx.Boom(), at, AudioChannel.Combat, 1f, 5f, 90f);
        }
        if (age >= life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        shapes?.Destroy();
        matter?.Destroy();
    }

    private void Draw()
    {
        var eye = Camera.main;
        if (shapes == null || !shapes.Begin(eye)) return;
        matter?.Begin(eye);
        if (word != null) word.alpha = 0f;
        float t = age;
        switch (kind)
        {
            case Kind.GroundSigil: GroundSigil(t); break;
            case Kind.Expand: Expand(t); break;
            case Kind.Tiger: Tiger(t); break;
            case Kind.BountyGrand: BountyGrand(t); break;
            case Kind.BountyPlain: BountyPlain(t, eye); break;
            case Kind.Reversal: Reversal(t); break;
            case Kind.CoverDeflect: CoverDeflect(t); break;
            case Kind.KataDraw: KataDraw(t); break;
            case Kind.ChargeRing: ChargeRing(t); break;
            case Kind.Wedge: Wedge(t); break;
            case Kind.Shell: Shell(t); break;
            case Kind.Flash: Flash(t); break;
            case Kind.Claws: Claws(t); break;
        }
        shapes.End();
        matter?.End();
    }

    // ---- Timing ----

    private static float Out(float k) { k = Mathf.Clamp01(k); return 1f - (1f - k) * (1f - k) * (1f - k); }
    // 0 before t0, up over a, held h, down over b (falling fast first: a crisp exit).
    private static float Window(float t, float t0, float a, float h, float b)
    {
        float k = t - t0;
        if (k <= 0f) return 0f;
        if (k < a) return Out(k / a);
        if (k < a + h) return 1f;
        float o = (k - a - h) / Mathf.Max(.001f, b);
        return o < 1f ? (1f - o) * (1f - o) : 0f;
    }
    private float Hash(int index, int salt)
    {
        uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }
    private Vector3 Aside => Vector3.Cross(Vector3.up, direction).normalized;

    // ---- The looks ----

    // A class sigil on the ground round the player: drawn turning into place, stamped, held a moment, gone.
    private void GroundSigil(float t)
    {
        float draw = Out(t / .25f), a = Window(t, 0f, .05f, .62f, .3f);
        Vector3 middle = at + Vector3.up * .03f, right = Aside, up = direction;
        float radius = value;
        shapes.Sigil(sigil, middle, right, up, radius * 2f, draw, FxShapes.With(colour, a), .035f, .12f, (1f - draw) * -.6f);
        shapes.Ring(middle, right, up, radius * 1.08f, .03f, FxShapes.With(colour, .8f * a), 72);
        shapes.Ticks(middle, right, up, radius * 1.12f, 12, .14f, .025f, FxShapes.With(colour, .8f * a), -t * .4f, 3);
        float stamp = t - .25f;
        if (stamp > 0f && stamp < .2f) shapes.Ring(middle, right, up, radius * 1.08f * Mathf.Lerp(1f, 1.45f, Out(stamp / .2f)), .05f, FxShapes.With(Color.white, 1f - stamp / .2f), 72);
    }

    // The Détente's cross running out over the ground to its reach, a ring at its edge, fading as it goes.
    private void Expand(float t)
    {
        float k = Mathf.Clamp01(t / .6f), radius = Mathf.Lerp(1f, value, Out(k)), a = (1f - k) * Mathf.Clamp01(t / .04f);
        Vector3 middle = at + Vector3.up * .03f;
        shapes.Sigil(MomentLook.Sigil.Detente, middle, Aside, direction, radius * 2f, 1f, FxShapes.With(colour, a * .9f), .05f, .16f);
        shapes.Ring(middle, Aside, direction, radius * 1.04f, .06f, FxShapes.With(Color.white, a * .7f), 96);
    }

    // A white tiger's head in outline before the Ghost, drawn as by one pen, held, and gone growing faint.
    private void Tiger(float t)
    {
        float draw = Out(t / .32f), a = Window(t, 0f, .06f, .62f, .3f), grow = 1f + .06f * Mathf.Clamp01((t - .7f) / .3f);
        shapes.Sigil(MomentLook.Sigil.Tiger, at, shapes.Right, shapes.Up, value * grow, draw, FxShapes.With(PaleBlue, a), .022f, .09f);
    }

    // The Desperado's claim: the bounty's diamond round the head, a seal over the ground, light bursting out, a sky line.
    private void BountyGrand(float t)
    {
        Vector3 head = at, feet = extra == default ? at - Vector3.up * 1.8f : extra, chest = Vector3.Lerp(feet, head, .62f);
        float size = MomentLook.Number("Bounty", "diamond", 1.1f);
        float draw = Out(t / .18f), a = Window(t, 0f, .03f, .52f, .22f), shrink = 1f - .25f * Mathf.Clamp01((t - .55f) / .25f);
        shapes.Sigil(MomentLook.Sigil.Hound, head, shapes.Right, shapes.Up, size * shrink, draw, FxShapes.With(Teal, a), .03f, .1f, (1f - draw) * Mathf.PI * .25f);
        float stamp = t - .18f;
        if (stamp > 0f && stamp < .16f) shapes.Ring(head, shapes.Right, shapes.Up, size * .5f * Mathf.Lerp(1f, 1.55f, Out(stamp / .16f)), .03f, FxShapes.With(Color.white, 1f - stamp / .16f));
        // The seal of ticks over the ground.
        float seal = Mathf.Clamp01((t - .08f) / .42f);
        if (seal > 0f && seal < 1f)
        {
            float radius = Mathf.Lerp(.5f, MomentLook.Number("Bounty", "seal", 3f), Out(seal));
            Vector3 floor = feet + Vector3.up * .04f;
            shapes.Ring(floor, Vector3.right, Vector3.forward, radius, .045f, FxShapes.With(Teal, 1f - seal), 72);
            shapes.Ticks(floor, Vector3.right, Vector3.forward, radius, 12, .3f, .04f, FxShapes.With(Teal, 1f - seal), t * .4f, 3);
        }
        // Light bursting out round the body as it falls: streaks thrown out from the chest, hollow at the middle.
        float burst = Mathf.Clamp01((t - .04f) / .32f);
        if (burst > 0f && burst < 1f)
        {
            for (int i = 0; i < 16; i++)
            {
                Vector3 way = new Vector3(Hash(i, 1) * 2f - 1f, Hash(i, 2) * 1.4f - .4f, Hash(i, 3) * 2f - 1f).normalized;
                float reach = .35f + 2.1f * Out(burst) * (.75f + .5f * Hash(i, 4));
                Vector3 tip = chest + way * reach, tail = chest + way * Mathf.Max(.35f, reach - .6f);
                shapes.Glow(tail, tip, FxShapes.With(i % 3 == 0 ? Color.white : Teal, 1f - burst), .022f, .07f);
            }
        }
        // A hairline stands up out of the body into the sky, seen across the map.
        float up = Out((t - .05f) / .09f), sky = 1f - Mathf.Clamp01((t - .15f) / .5f);
        if (up > 0f && sky > 0f) shapes.Glow(feet + Vector3.up * .05f, feet + Vector3.up * (MomentLook.Number("Bounty", "sky", 22f) * up), FxShapes.With(Teal, sky), .03f, .14f);
        // The bounty paid: a few gold glints falling.
        float fall = t - .06f;
        if (fall > 0f && fall < .8f)
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = new Vector3((Hash(i, 5) - .5f) * 2.4f, 1.5f + Hash(i, 6) * 2.5f, (Hash(i, 7) - .5f) * 2.4f);
                Vector3 p = head + new Vector3(v.x * fall, v.y * fall - 4.9f * fall * fall, v.z * fall);
                Spark(p, .07f, FxShapes.With(Gold, 1f - fall / .8f));
            }
    }

    // A plain bounty: a small CLAIMED plate slammed beside the head, held a beat, rising away.
    private void BountyPlain(float t, Camera eye)
    {
        float a = Window(t, 0f, .03f, .24f, .18f), slam = Mathf.Lerp(1.6f, 1f, Out(t / .06f)), rise = .35f * Mathf.Clamp01((t - .27f) / .2f);
        float w = .62f * slam, h = .19f * slam;
        Vector3 middle = at + shapes.Right * .45f + Vector3.up * (.12f + rise);
        Vector3 r = shapes.Right * (w * .5f), u = shapes.Up * (h * .5f);
        // Its slant: the top edge runs a little right of the bottom one.
        Vector3 lean = shapes.Right * (h * .3f);
        matter.Fill(new[] { middle - r - u - lean, middle + r - u - lean, middle + r + u + lean, middle - r + u + lean }, FxShapes.With(new Color(.05f, .05f, .06f), .92f * a));
        Color rim = Color.Lerp(Teal, Color.white, 1f - Mathf.Clamp01(t / .07f));
        shapes.Glow(middle - r - u - lean, middle + r - u - lean, FxShapes.With(rim, a), .008f, .02f);
        shapes.Glow(middle + r - u - lean, middle + r + u + lean, FxShapes.With(rim, a), .008f, .02f);
        shapes.Glow(middle + r + u + lean, middle - r + u + lean, FxShapes.With(rim, a), .008f, .02f);
        shapes.Glow(middle - r + u + lean, middle - r - u - lean, FxShapes.With(rim, a), .008f, .02f);
        shapes.Triangle(middle + r + u + lean + shapes.Right * .03f, middle + r + u + lean + shapes.Right * .11f, middle + r + lean * .5f + shapes.Right * .05f, FxShapes.With(Pink, a));
        if (word != null && eye != null)
        {
            word.alpha = a;
            word.transform.position = middle;
            word.transform.rotation = Quaternion.LookRotation(middle - eye.transform.position, eye.transform.up);
            word.transform.localScale = Vector3.one * slam;
        }
    }

    // A reversal: a pink swoosh over the top where the zombie goes, a ring on the ground under it, chips at its end.
    private void Reversal(float t)
    {
        Vector3 from = at - direction * .2f - Vector3.up * .3f, to = at + direction * 1.8f - Vector3.up * .9f;
        float head = Out(t / .13f), a = 1f - Mathf.Clamp01((t - .12f) / .34f);
        const int Steps = 14;
        for (int i = 0; i < Steps; i++)
        {
            float s0 = i / (float)Steps * head, s1 = (i + 1) / (float)Steps * head;
            Vector3 p0 = Vector3.Lerp(from, to, s0) + Vector3.up * (Mathf.Sin(Mathf.PI * s0) * 1.1f);
            Vector3 p1 = Vector3.Lerp(from, to, s1) + Vector3.up * (Mathf.Sin(Mathf.PI * s1) * 1.1f);
            float width = Mathf.Lerp(.24f, .03f, (i + .5f) / Steps);
            shapes.Blade(p0, p1, width, FxShapes.With(Pink, .55f * a));
            shapes.Blade(p0, p1, width * .32f, FxShapes.With(Color.Lerp(Pink, Color.white, .8f), a));
        }
        float ring = Mathf.Clamp01((t - .1f) / .35f);
        if (ring > 0f && ring < 1f)
        {
            Vector3 floor = at + direction * .6f - Vector3.up * 1.2f;
            shapes.Ring(floor, Vector3.right, Vector3.forward, Mathf.Lerp(.3f, 2.6f, Out(ring)), .06f, FxShapes.With(Pink, 1f - ring), 64);
        }
        float chips = t - .12f;
        if (chips > 0f && chips < .35f)
            for (int i = 0; i < 6; i++)
            {
                Vector3 way = (direction * (.4f + Hash(i, 1)) + Vector3.up * (Hash(i, 2) * 1.4f) + Aside * (Hash(i, 3) * 2f - 1f)).normalized;
                Vector3 p = to + way * (3.5f * chips) + Vector3.down * (2f * chips * chips);
                Shard(p, .07f * (1f - chips * 1.5f), Hash(i, 4) * 6.3f + chips * 12f, FxShapes.With(i % 3 == 0 ? Color.white : Pink, 1f - chips / .35f));
            }
    }

    // A Knight's cover turning the blow: a white crescent with a violet edge across it, sparks, a ring, the shield flashing.
    private void CoverDeflect(float t)
    {
        Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
        if (side.sqrMagnitude < .5f) side = shapes.Right;
        float cut = Out(t / .06f), a = 1f - Mathf.Clamp01((t - .06f) / .26f);
        Vector3 from = at - side * .55f + Vector3.up * .25f, to = at + side * .55f - Vector3.up * .25f;
        Vector3 bow = -direction * .22f;
        shapes.Blade(from, Vector3.Lerp(from, to, cut), .17f, FxShapes.With(Violet, .6f * a), bow, 12, 1f);
        shapes.Blade(from, Vector3.Lerp(from, to, cut), .055f, FxShapes.With(Color.white, a), bow, 12, 1f);
        float spark = Mathf.Clamp01(t / .25f);
        if (spark < 1f)
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2f / 10f + Hash(i, 1) * .5f;
                Vector3 way = shapes.Right * Mathf.Cos(angle) + shapes.Up * Mathf.Sin(angle);
                float reach = .15f + .9f * Out(spark) * (.6f + .4f * Hash(i, 2));
                shapes.Blade(at + way * Mathf.Max(.1f, reach - .25f), at + way * reach, .025f, FxShapes.With(i % 2 == 0 ? Color.white : Violet, 1f - spark));
            }
        float ring = Mathf.Clamp01(t / .3f);
        if (ring < 1f) shapes.Ring(at, shapes.Right, shapes.Up, Mathf.Lerp(.2f, 1.2f, Out(ring)), .035f, FxShapes.With(Color.white, .9f * (1f - ring)));
        // The shield's flash: a kite shield standing where the blow was turned.
        float shield = Window(t, 0f, .04f, .12f, .15f), s = .38f * Mathf.Lerp(1.35f, 1f, Out(t / .06f));
        if (shield > 0f)
        {
            Vector3 r = shapes.Right * s, u = shapes.Up * s;
            var points = new[] { at - r * .62f + u * .7f, at + r * .62f + u * .7f, at + r * .62f, at - u * .86f, at - r * .62f };
            matter.Fill(points, FxShapes.With(new Color(.16f, .08f, .3f), .7f * shield));
            for (int i = 0; i < points.Length; i++) shapes.Glow(points[i], points[(i + 1) % points.Length], FxShapes.With(Violet, shield), .012f, .04f);
        }
    }

    // A gun drawn in the Gun Kata: two orange arcs whirl round it the way a cylinder spins, a glint at their meeting.
    private void KataDraw(float t)
    {
        float spin = Out(t / .25f) * Mathf.PI * 1.7f, a = 1f - Mathf.Clamp01((t - .1f) / .22f), radius = .2f + .06f * Out(t / .25f);
        shapes.Ring(at, shapes.Right, shapes.Up, radius, .018f, FxShapes.With(colour, a), 16, spin, spin + Mathf.PI * .65f);
        shapes.Ring(at, shapes.Right, shapes.Up, radius, .018f, FxShapes.With(colour, a), 16, spin + Mathf.PI, spin + Mathf.PI * 1.65f);
        Spark(at + (shapes.Right * Mathf.Cos(spin) + shapes.Up * Mathf.Sin(spin)) * radius, .08f, FxShapes.With(Color.white, a));
    }

    // A CHARGE's reach: a gold ring with ticks running out over the ground and fading.
    private void ChargeRing(float t)
    {
        float k = Mathf.Clamp01(t / .45f);
        if (k >= 1f) return;
        float radius = Mathf.Lerp(.6f, value, Out(k));
        Vector3 floor = at + Vector3.up * .04f;
        shapes.Ring(floor, Vector3.right, Vector3.forward, radius, .05f, FxShapes.With(colour, 1f - k), 120);
        shapes.Ticks(floor, Vector3.right, Vector3.forward, radius, 40, .22f, .035f, FxShapes.With(colour, 1f - k), 0f, 5);
    }

    // The phantom cavalry as a wedge of gold chevron plates sliding along the ground at the riders' pace.
    private void Wedge(float t)
    {
        Vector3 travel = Vector3.ProjectOnPlane(direction, Vector3.up);
        travel = travel.sqrMagnitude > 1e-4f ? travel.normalized : Vector3.forward;
        Vector3 aside = Vector3.Cross(Vector3.up, travel);
        float reach = Mathf.Max(1f, value), ride = Mathf.Max(0f, t - CardFx.CavalryIn) * CardFx.CavalrySpeed;
        float shown = Mathf.Clamp01(t / CardFx.CavalryIn) * (1f - Mathf.Clamp01((t - (life - CardFx.CavalryOut)) / CardFx.CavalryOut));
        float[,] places = { { 0f, 0f }, { -1.3f, -1.2f }, { 1.3f, -1.2f }, { -2.6f, -2.4f }, { 2.6f, -2.4f } };
        for (int i = 0; i < 5; i++)
        {
            float along = Mathf.Min(reach, ride + places[i, 1]);
            if (along < 0f) continue;
            Vector3 tip = at + travel * (along + 1.5f) + aside * places[i, 0] + Vector3.up * .04f;
            Vector3 back = -travel * .75f, wing = aside * .85f;
            Color plate = FxShapes.With(colour, .85f * shown);
            shapes.Fill(new[] { tip, tip + back + wing, tip + back + wing * .62f, tip + back * .42f }, plate);
            shapes.Fill(new[] { tip, tip + back * .42f, tip + back - wing * .62f, tip + back - wing }, plate);
        }
    }

    // A Broken Arrow shell: its target on the ground first, a bright streak falling onto it, the target bursting out.
    private void Shell(float t)
    {
        Vector3 floor = at + Vector3.up * .05f;
        float radius = Mathf.Max(1f, value) * .5f;
        float draw = Out(t / .12f), hold = t < CardFx.ShellFall ? 1f : 0f;
        if (hold > 0f)
        {
            shapes.Sigil(MomentLook.Sigil.Target, floor, Vector3.right, Vector3.forward, radius * 2f, draw, FxShapes.With(Color.white, .9f), .04f, .12f, t * 2f);
            shapes.Ticks(floor, Vector3.right, Vector3.forward, radius * 1.05f, 8, .18f, .04f, FxShapes.With(Pink, .9f), -t * 3f, 2);
        }
        else
        {
            float burst = Mathf.Clamp01((t - CardFx.ShellFall) / .3f);
            if (burst < 1f)
            {
                shapes.Ring(floor, Vector3.right, Vector3.forward, radius * Mathf.Lerp(1f, 2.4f, Out(burst)), .08f, FxShapes.With(Color.white, 1f - burst), 72);
                shapes.Ring(floor, Vector3.right, Vector3.forward, radius * Mathf.Lerp(.6f, 1.6f, Out(burst)), .05f, FxShapes.With(Pink, 1f - burst), 72);
            }
        }
        float fall = Mathf.Clamp01(t / CardFx.ShellFall);
        if (t < CardFx.ShellFall)
        {
            Vector3 sky = at + Vector3.up * 45f + direction * 6f;
            Vector3 head = Vector3.Lerp(sky, at, fall * fall), tail = Vector3.Lerp(sky, at, Mathf.Max(0f, fall * fall - .22f));
            shapes.Glow(tail, head, FxShapes.With(Color.white, .95f), .06f, .22f);
            Spark(head, .5f, FxShapes.With(Gold, .9f));
        }
    }

    // The gauntlet's flash: a star where it struck, and on the ground the cone it stunned (seen from the Knight's own eyes too).
    private void Flash(float t)
    {
        float draw = Out(t / .08f), a = Window(t, 0f, .03f, .16f, .14f);
        shapes.Sigil(MomentLook.Sigil.Star, at, shapes.Right, shapes.Up, MomentLook.Number("Flash", "star", .5f), draw, FxShapes.With(Color.white, a), .02f, .07f);
        // The cone from the Knight, along the ground: two edges and the arc at its reach.
        Vector3 way = Vector3.ProjectOnPlane(direction, Vector3.up);
        way = way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward;
        Vector3 origin = at - way * MomentLook.Number("Flash", "back", 1f);
        if (Physics.Raycast(origin + Vector3.up * .5f, Vector3.down, out RaycastHit ground, 3f, ~(1 << 31), QueryTriggerInteraction.Ignore)) origin = ground.point;
        else origin.y -= 1.25f;
        origin += Vector3.up * .04f;
        float reach = Mathf.Max(1f, PlayerClass.Ability("Knight", "gauntletReach", 5f)) * Out(t / .1f);
        float half = Mathf.Clamp(PlayerClass.Ability("Knight", "gauntletAngle", 70f), 1f, 180f) * .5f * Mathf.Deg2Rad;
        float cone = Window(t, 0f, .04f, .2f, .18f);
        Vector3 left = Quaternion.AngleAxis(-half * Mathf.Rad2Deg, Vector3.up) * way, right = Quaternion.AngleAxis(half * Mathf.Rad2Deg, Vector3.up) * way;
        Color edge = FxShapes.With(Color.white, .85f * cone);
        shapes.Strip(origin, origin + left * reach, Vector3.Cross(Vector3.up, left), .035f, edge);
        shapes.Strip(origin, origin + right * reach, Vector3.Cross(Vector3.up, right), .035f, edge);
        Vector3 across = Vector3.Cross(Vector3.up, way);
        shapes.Ring(origin, across, way, reach, .035f, edge, 24, Mathf.PI * .5f - half, Mathf.PI * .5f + half);
    }

    // The White Tiger's claws: three curved wounds raked across the head, top right to bottom left, one after another.
    private void Claws(float t)
    {
        float size = MomentLook.Number("Claws", "size", 1f);
        Vector3 across = (shapes.Right * -.55f - shapes.Up).normalized, side = Vector3.Cross(shapes.Forward, across).normalized;
        for (int i = 0; i < 3; i++)
        {
            float start = i * .045f, k = Out((t - start) / .07f);
            if (k <= 0f) continue;
            float fade = 1f - Mathf.Clamp01((t - .16f - start) / .22f);
            if (fade <= 0f) continue;
            float length = size * (i == 1 ? 1.05f : .88f);
            Vector3 middle = at + side * (i - 1) * size * .26f + across * (i - 1) * size * .06f;
            Vector3 from = middle - across * length * .5f, to = Vector3.Lerp(from, middle + across * length * .5f, k);
            Vector3 bow = side * size * .07f;
            shapes.Blade(from, to, size * .1f, FxShapes.With(PaleBlue, .45f * fade), bow, 12, 1f);
            shapes.Blade(from, to, size * .045f, FxShapes.With(Color.white, fade), bow, 12, 1f);
        }
    }

    // ---- Small parts ----

    // A four-pointed glint.
    private void Spark(Vector3 point, float size, Color color)
    {
        shapes.Blade(point - shapes.Up * size, point + shapes.Up * size, size * .22f, color, default, 6, 1f);
        shapes.Blade(point - shapes.Right * size * .7f, point + shapes.Right * size * .7f, size * .16f, color, default, 6, 1f);
    }

    // A flat triangular chip turning as it flies.
    private void Shard(Vector3 point, float size, float turn, Color color)
    {
        if (size <= 0f) return;
        Vector3 a = shapes.Right * Mathf.Cos(turn) + shapes.Up * Mathf.Sin(turn), b = shapes.Right * Mathf.Cos(turn + 2.2f) + shapes.Up * Mathf.Sin(turn + 2.2f);
        shapes.Triangle(point + a * size, point + b * size * .6f, point - a * size * .4f, color);
    }

    // The plain bounty's word on its plate, turned to each eye.
    private void Word(string text, Color tint)
    {
        word = new GameObject("Word", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        word.transform.SetParent(transform, false);
        var font = GunFeelSettings.Instance.scoreFont != null ? GunFeelSettings.Instance.scoreFont : CombatUIAssets.ResolveFont(null);
        if (font != null) word.font = font;
        word.text = text;
        word.fontSize = 1.6f;
        word.fontStyle = FontStyles.Bold | FontStyles.Italic;
        word.characterSpacing = 6f;
        word.alignment = TextAlignmentOptions.Center;
        word.textWrappingMode = TextWrappingModes.NoWrap;
        word.rectTransform.sizeDelta = new Vector2(1.2f, .3f);
        word.color = tint;
        word.alpha = 0f;
    }
}
