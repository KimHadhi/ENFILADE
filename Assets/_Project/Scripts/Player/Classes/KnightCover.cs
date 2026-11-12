using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Knight's cover (원호 방어), its class key (the user, 2026-11-12: "아군을 대상으로 능력 키를 눌렀을 때 아군 대신해 패링 판정
/// 발생시켜서 아군 쪽으로 순간이동 되면서 공격 튕겨내 주면서 아군 지키는 컨셉 … 나이트의 장점은 그런 츠바이 협회 같은 아군 보호의
/// 멋있음과 동시에 걍 순수 체급이 졸라 쎄다는 데에 있지"; it took the taunt's place, which felt far from a knight's pride):
/// - held, a ring on the ground shows how far it reaches, and the friend under the crosshair within it and in sight
///   glows on this PC alone, which says they can be covered now ("능력키 꾹 누르고 있으면 자기 원호 방어 사거리만큼 범위 뜨고
///   누른 채로 아군 바라보고 있으면 … 그 아군이 나이트한테만 빛났으면"); the guard key calls it off;
/// - let go on a friend, a parry opens on their behalf for a moment: an enemy's blow on them then is turned as their own
///   parry would turn it (their PC judges it: Covers, PlayerDamageReceiver; the enemy staggers as from any parry,
///   ParryFeedback), and the Knight is at their side at once, between them and the blow, facing it, with a white streak
///   (Covered, CoverStreak). No blow in that moment, nothing happens, and the key is soon ready again; a blow turned
///   sets the whole wait;
/// - with the shadow step card the Knight steps to them on letting go, blow or none ("나중에 스킬 카드 올리면 굳이 패링 안
///   해도 아군한테 원호 방어 하듯이 순간이동"), the parry still open on them after.
/// Every PC keeps the open covers (PlayerNetworkCombat.CoverRpc), since the friend's PC judges their blows; it tells the
/// Knight's PC when one was turned (CoveredRpc). This PC's own Knight only. Numbers: Tables/ClassAbilities, Knight.cover*.
/// </summary>
[DisallowMultipleComponent]
public sealed class KnightCover : MonoBehaviour
{
    private const string Row = "Knight";
    private const int RingPoints = 72;
    // The friend's glow: the halo's size (m) and the light's reach.
    private const float HaloWidth = 1.3f, HaloHeight = 2.2f, GlowRange = 3f;

    private struct Window
    {
        public PlayerMovement Knight, Ally;
        public float Until;
    }

    private static readonly List<Window> windows = new List<Window>();

    /// <summary>This PC's own player's, while they play a Knight.</summary>
    public static KnightCover Local { get; private set; }
    /// <summary>The id of the last blow a cover turned on this PC (ClassCards leaves its gauge to the Knight).</summary>
    public static long CoveredHit { get; private set; }

    /// <summary>A cover opened on this friend for so long: the other PCs are told (PlayerNetworkCombat).</summary>
    public event Action<PlayerMovement, float> Opened;
    /// <summary>The Knight stepped from here to there: the other PCs draw the streak.</summary>
    public event Action<Vector3, Vector3> Stepped;

    private PlayerMovement movement;
    private bool planning;
    private float readyAt, wait = 1f, openUntil;
    private PlayerMovement aimed, opened;
    private LineRenderer ring;
    private GameObject glowHost;
    private Mesh glow;
    private Light glowLight;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        windows.Clear();
        Local = null;
        CoveredHit = 0;
    }

    /// <summary>A Knight whose cover may be used now; seconds until it may (0: now), and how much of that wait is left.</summary>
    public bool Ready => PlayerClass.Is(PlayerClass.Knight) && Time.time >= readyAt;
    public float Wait => Mathf.Max(0f, readyAt - Time.time);
    public float WaitShare => Mathf.Clamp01(Wait / wait);
    /// <summary>The key held to choose a friend, and the friend under the crosshair who can be covered now (or null).</summary>
    public bool Planning => planning;
    public PlayerMovement Aimed => aimed;
    /// <summary>The friend a cover is open on now, waiting for a blow (or null).</summary>
    public PlayerMovement Watched => opened != null && Time.time < openUntil ? opened : null;

    private void Awake() => movement = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        if (Local == this) Local = null;
        Stop();
    }

    private void OnDestroy()
    {
        if (glowHost != null) Destroy(glowHost);
        if (glow != null) Destroy(glow);
        if (ring != null) Destroy(ring.gameObject);
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        if (!PlayerClass.Is(PlayerClass.Knight))
        {
            if (Local == this) Local = null;
            Stop();
            return;
        }
        Local = this;
        Plan();
        if (opened != null && Time.time >= openUntil) opened = null;
        Draw();
    }

    /// <summary>A class was picked (PlayerClass.Picked, by ClassKit): a fresh start, the cover ready.</summary>
    internal void Fresh()
    {
        readyAt = 0f;
        wait = 1f;
        opened = null;
        Stop();
    }

    private void Stop()
    {
        planning = false;
        aimed = null;
        if (ring != null) ring.enabled = false;
        if (glowHost != null) glowHost.SetActive(false);
    }

    // The class key (Player/ClassAbility; ClassKit leaves a Knight's to this): held, the ring and the friend's glow; let
    // go on a friend, the cover. The guard key calls it off, and so does anything that takes the hands.
    private void Plan()
    {
        var key = KeyBindings.Find("Player/ClassAbility");
        bool able = key != null && movement.InputAvailable && !movement.IsDead && !movement.IsDowned && !movement.IsHeld
            && !movement.CombatInputLocked;
        if (!able) { planning = false; aimed = null; return; }
        if (KeyBindings.Pressed(key))
        {
            if (Time.time < readyAt) KitSound.Play(KitSound.Cue.Refuse, .5f);
            else planning = true;
        }
        if (!planning) { aimed = null; return; }
        if (KeyBindings.Pressed("Combat/Guard"))
        {
            planning = false;
            aimed = null;
            KitSound.Play(KitSound.Cue.Refuse, .25f);
            return;
        }
        aimed = Aim();
        if (KeyBindings.Held(key)) return;
        planning = false;
        var ally = aimed;
        aimed = null;
        if (ally == null) { KitSound.Play(KitSound.Cue.Refuse, .35f); return; }
        Cover(ally);
    }

    // Let go on a friend: the parry opens on their behalf. The short wait now; a blow turned sets the whole one (Covered).
    private void Cover(PlayerMovement ally)
    {
        float seconds = Mathf.Max(.1f, Number("coverWindow", 1f));
        wait = Mathf.Max(seconds, Number("coverWhiff", 2f));
        readyAt = Time.time + wait;
        opened = ally;
        openUntil = Time.time + seconds;
        Open(movement, ally, seconds);
        Opened?.Invoke(ally, seconds);
        KitSound.Play(KitSound.Cue.Taunt, .45f);
        // The shadow step card: at their side at once, blow or none.
        if (PlayerPerks.Has(PlayerPerks.KnightShadowstep)) Step(ally, Threat(ally));
    }

    /// <summary>A cover opens on <paramref name="ally"/> by <paramref name="knight"/> for <paramref name="seconds"/> (every PC).</summary>
    public static void Open(PlayerMovement knight, PlayerMovement ally, float seconds)
    {
        if (knight == null || ally == null) return;
        windows.RemoveAll(w => w.Knight == knight);
        windows.Add(new Window { Knight = knight, Ally = ally, Until = Time.time + Mathf.Max(.05f, seconds) });
    }

    /// <summary>
    /// The friend's PC: an enemy's blow on <paramref name="victim"/> under an open cover is turned (the cover spent), and
    /// <paramref name="knight"/> is the one who turned it. A grab or anything that can't be guarded comes through as ever.
    /// </summary>
    public static bool Covers(PlayerMovement victim, DamageHit hit, out PlayerMovement knight)
    {
        knight = null;
        if (windows.Count == 0 || victim == null || hit.Kind != DamageKind.Attack || !hit.CanBeGuarded || hit.Source == null) return false;
        if (hit.Source.GetComponentInParent<EnemyHealth>() == null) return false;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            var window = windows[i];
            if (window.Knight == null || window.Ally == null || Time.time >= window.Until) { windows.RemoveAt(i); continue; }
            if (window.Ally != victim || window.Knight.IsDead || window.Knight.IsDowned) continue;
            windows.RemoveAt(i);
            knight = window.Knight;
            CoveredHit = hit.Id;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The Knight's PC: its cover turned a blow from <paramref name="attacker"/> on <paramref name="ally"/> (their PC said
    /// so): the whole wait, the parry's charge on the gauge, and the Knight at their side facing the blow.
    /// </summary>
    internal void Covered(PlayerMovement ally, Vector3 attacker)
    {
        if (ally == null || movement == null || movement.IsDead) return;
        opened = null;
        wait = Mathf.Max(.1f, Number("coverCooldown", 12f));
        readyAt = Time.time + wait;
        UltimateCharge.AwardHelp(UltimateCharge.Help.Parried);
        // With the shadow step the Knight is already beside them; it turns to the blow where it stands.
        if (!PlayerPerks.Has(PlayerPerks.KnightShadowstep) || (ally.transform.position - transform.position).sqrMagnitude > 9f) Step(ally, attacker);
        else Face(attacker);
        // The blow turned aside before the friend, for everyone to see (the user: "공격을 튕겨내는 화려한 이펙트").
        Vector3 toward = Vector3.ProjectOnPlane(attacker - transform.position, Vector3.up);
        toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : transform.forward;
        CardNet.Show(CardFx.Kind.CoverDeflect, transform.position + toward * .6f + Vector3.up * 1.3f, -toward, 0f);
        GuardPose.Strike(movement, attacker); // the parry stance with it, on every PC (the user: "특유의 패링 자세와 함께")
        if (!TryGetComponent(out CounterBanner banner)) banner = gameObject.AddComponent<CounterBanner>();
        banner.Play(.85f, CounterBanner.Grades - 1, "COVER", Color.white);
        AlertHUD.Self(Texts.Format("class.knight.cover.done", NameOf(ally)));
    }

    // To the friend's side, between them and the threat, facing it, with the streak for everyone.
    private void Step(PlayerMovement ally, Vector3 threat)
    {
        Vector3 from = transform.position, to = Spot(ally, threat);
        movement.MoveTo(to);
        Face(threat);
        CoverStreak.Show(from, to, gameObject);
        Stepped?.Invoke(from, to);
        KitSound.Play(KitSound.Cue.Charge, .6f);
    }

    private void Face(Vector3 threat)
    {
        Vector3 face = threat - transform.position;
        face.y = 0f;
        if (face.sqrMagnitude > .01f && TryGetComponent(out PlayerLook look)) look.FaceYaw(Quaternion.LookRotation(face).eulerAngles.y);
    }

    // The nearest living enemy within a few metres of the friend, or the way they face.
    private static Vector3 Threat(PlayerMovement ally)
    {
        Vector3 at = ally.transform.position, best = at + ally.transform.forward * 2f;
        float nearest = 36f;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            float distance = (enemy.transform.position - at).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            best = enemy.transform.position;
        }
        return best;
    }

    // Where to stand: between the friend and the threat at arm's length, else beside them, else just short of them.
    private Vector3 Spot(PlayerMovement ally, Vector3 threat)
    {
        Vector3 at = ally.transform.position, toward = threat - at;
        toward.y = 0f;
        toward = toward.sqrMagnitude > .01f ? toward.normalized : ally.transform.forward;
        float stand = Mathf.Max(.5f, Number("coverStand", 1f));
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * toward;
        Vector3[] tries = { at + toward * stand, at + toward * stand * .7f, at + side * stand, at - side * stand, at - toward * stand };
        foreach (var spot in tries)
            if (Free(spot)) return spot;
        return at + toward * .5f;
    }

    private bool Free(Vector3 feet)
        => !Physics.CheckCapsule(feet + Vector3.up * .4f, feet + Vector3.up * 1.4f, .3f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);

    // The friend under the crosshair within reach and in sight, alive.
    private PlayerMovement Aim()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Mathf.Max(1f, Number("coverReach", 15f)), widest = Mathf.Max(1f, Number("coverAngle", 8f));
        PlayerMovement found = null;
        float best = widest;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == movement || !other.IsInitialized || other.DamageReceiver == null || !other.DamageReceiver.IsAlive) continue;
            Vector3 chest = other.transform.position + Vector3.up * 1.1f, to = chest - eyes.position;
            if (to.sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eyes.forward, to);
            if (angle > best) continue;
            if (Physics.Linecast(eyes.position, chest, out RaycastHit hit, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(other.transform)) continue;
            best = angle;
            found = other;
        }
        return found;
    }

    // ---- The look: the ring while the key is held, the friend's glow (this PC only) ----

    private void Draw()
    {
        bool show = planning;
        if (show && ring == null) MakeRing();
        if (ring != null)
        {
            ring.enabled = show;
            if (show)
            {
                float reach = Mathf.Max(1f, Number("coverReach", 15f));
                Vector3 middle = transform.position + Vector3.up * .08f;
                for (int i = 0; i < RingPoints; i++)
                {
                    float angle = 2f * Mathf.PI * i / RingPoints;
                    ring.SetPosition(i, middle + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach));
                }
                Color color = KitStyle.With(Color.white, .45f + .15f * Mathf.Sin(Time.unscaledTime * 5f));
                ring.startColor = ring.endColor = color;
            }
        }
        var lit = planning ? aimed : Watched;
        if (lit == null) { if (glowHost != null) glowHost.SetActive(false); return; }
        if (glowHost == null) MakeGlow();
        glowHost.SetActive(true);
        Glow(lit, planning ? 1f : .55f);
    }

    private void MakeRing()
    {
        var host = new GameObject("Cover Ring") { hideFlags = HideFlags.DontSave };
        ring = host.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = .07f;
        ring.alignment = LineAlignment.TransformZ;
        host.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
    }

    private void MakeGlow()
    {
        glowHost = new GameObject("Cover Glow") { hideFlags = HideFlags.DontSave };
        glow = new Mesh { name = "Cover Glow", hideFlags = HideFlags.HideAndDontSave };
        glow.MarkDynamic();
        glowHost.AddComponent<MeshFilter>().sharedMesh = glow;
        var renderer = glowHost.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        glowLight = new GameObject("Light").AddComponent<Light>();
        glowLight.transform.SetParent(glowHost.transform, false);
        glowLight.type = LightType.Point;
        glowLight.shadows = LightShadows.None;
        glowLight.color = Color.white;
        glowLight.range = GlowRange;
    }

    // A white halo behind the friend and a soft light on them, beating.
    private void Glow(PlayerMovement friend, float strength)
    {
        var eye = Camera.main;
        if (eye == null || glow == null) return;
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        Vector3 feet = friend.transform.position, right = eye.transform.right;
        Vector3 toEye = eye.transform.position - feet;
        toEye.y = 0f;
        Vector3 back = toEye.sqrMagnitude > 1e-4f ? -toEye.normalized * .25f : Vector3.zero;
        float beat = .8f + .2f * Mathf.Sin(Time.unscaledTime * 8f);
        Vector3 middle = feet + back + Vector3.up * (HaloHeight * .5f), across = right * (HaloWidth * .5f), up = Vector3.up * (HaloHeight * .5f);
        vertices.Add(middle - across - up);
        vertices.Add(middle - across + up);
        vertices.Add(middle + across + up);
        vertices.Add(middle + across - up);
        Color32 shade = KitStyle.With(Color.white, .42f * beat * strength);
        for (int i = 0; i < 4; i++) colors.Add(shade);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        triangles.Add(0); triangles.Add(1); triangles.Add(2);
        triangles.Add(0); triangles.Add(2); triangles.Add(3);
        glow.Clear();
        glow.SetVertices(vertices);
        glow.SetColors(colors);
        glow.SetUVs(0, uvs);
        glow.SetTriangles(triangles, 0);
        glow.RecalculateBounds();
        glowLight.transform.position = feet + Vector3.up * 1.2f;
        glowLight.intensity = 1.8f * beat * strength;
    }

    private static string NameOf(PlayerMovement player)
        => player != null && player.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName
            : player != null ? player.name : "";

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
