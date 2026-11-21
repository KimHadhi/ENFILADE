using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

/// <summary>
/// The health bar over an enemy: Black Ops 6 Zombies' bar as the Garry's Mod addon "Black Ops 6 Zombies Healthbars"
/// (cl_bo6healthbar.lua, sv_bo6healthbar.lua) draws it, reproduced as it is (user, 2026-11-22: "애드온 원본대로 해 … 전혀
/// 원본 같지 않잖아"). One bar on the screen: the enemy under the crosshair within Aim.reach, which stays Aim.keep
/// seconds after the crosshair leaves it, or the one the local player just hit. A flat bar of fixed pixel size at every
/// distance over the middle of the top of the enemy's collision capsule: grey under a white trail under the red health,
/// a black line where the health ends, a black outline, the name in capitals under it with a soft black shadow, and a
/// zombie's or a boss's head left of it. A hit leaves the trail standing for Bar.hold seconds before it slides down to
/// the health; every drop in health raises a translucent column from the bar's foot where the health now ends (yellow
/// for a headshot) with a solid chunk inside the bar. A killed enemy's bar stays, empty, where it died for Bar.deadHold
/// seconds. No fading in or out, as the addon has none.
/// Numbers: Tables/Hud/EnemyBars.csv, in 1920x1080 pixels scaled by the screen's width or height as the addon scales
/// them by ScrW or ScrH; rectangles are cut to whole pixels and text placed on whole pixels as Source's surface functions
/// take them. Docs/Features/EnemyBars.md.
/// One owner: every enemy's bar lives here, kept from the moment it comes into play (Track, from MeleeCombatTarget) as
/// the addon keeps one for every NPC from its creation, so an enemy's trail and columns are already right when its bar
/// comes up. It took the place of EnemyVitalsHUD, a world-space bar on every enemy in view with brackets, a posture bar
/// and the stagger mark; whether an enemy can be executed is its outline's to show (EnemyOutlines).
/// </summary>
[DefaultExecutionOrder(FrameOrder.EnemyBars)]
public sealed class EnemyHealthBarHUD : MonoBehaviour
{
    public const string TableName = "EnemyBars", BarRow = "Bar", NameRow = "Name", IconRow = "Icon", HitRow = "Hit", AimRow = "Aim";
    // Under the hit markers, the kill feed and the blood on the screen (24 and up): the bar belongs to the world it hangs in.
    private const int Order = 22;
    private const string PictureFolder = "UI/EnemyBars/";

    /// <summary>The bar (EnemyBars.csv rows Bar.*). Across values scale with the screen's width, heights with its height.</summary>
    [System.Serializable]
    public sealed class BarLook
    {
        [Tooltip("Bar width (px at 1920 wide). The addon's ScrW * 0.07395833.")]
        public float width = 142f;
        [Tooltip("Bar height (px at 1080 high), cut to whole pixels when drawn (8 at 1080p). ScrH * 0.00833.")]
        public float height = 8.9964f;
        [Tooltip("From the enemy's top on the screen up to the bar's middle (px at 1080 high). ScrH * 0.02.")]
        public float above = 21.6f;
        public Color background = new Color32(69, 73, 76, 255);
        [Tooltip("The bar's ground drawn with bg_alt_1.png instead (the addon's froze_bo6hbar_alt_bg_texture, off by default).")]
        public bool altBackground;
        [Tooltip("The trail: what a hit took, standing and then sliding down to the health.")]
        public Color follower = Color.white;
        public Color health = new Color32(221, 71, 59, 255);
        [Tooltip("The line where the health ends.")]
        public Color edge = Color.black;
        public Color outline = Color.black;
        [Tooltip("The addon's one-pixel pieces (screen px at any size): the end line, the outline, and the steps a hit's column and chunk keep from it.")]
        public float line = 1f;
        [Tooltip("Seconds of game time the trail stands after a hit, counted from the first hit while it stands.")]
        public float hold = 1f;
        [Tooltip("The trail closes (frame time / this) of what is left to the health each frame (seconds).")]
        public float follow = .11f;
        [Tooltip("The trail lands on the health once the health times this is longer than it.")]
        public float snap = 1.03f;
        [Tooltip("Seconds a killed (or gone) enemy's bar stays, empty, where it was.")]
        public float deadHold = .4f;
        [Tooltip("Rectangles cut to whole pixels and text put on whole pixels, as Source's surface functions take them.")]
        public bool wholePixels = true;
    }

    /// <summary>The name under the bar (rows Name.*): Hitmarker Text Medium (Fonts.csv EnemyBar), capitals, shadowed.</summary>
    [System.Serializable]
    public sealed class NameLook
    {
        [Tooltip("The name is drawn (the addon's froze_bo6hbar_enabled_name_cl).")]
        public bool show = true;
        [Tooltip("The blurred black shadow behind it (froze_bo6hbar_enabled_name_shadow_cl).")]
        public bool shadow = true;
        [Tooltip("The name's character cell height (px at 1080 high), cut to whole pixels. ScrH * 0.014.")]
        public float cell = 15.12f;
        [Tooltip("The blurred shadow's cell height (px at 1080 high). ScrH * 0.015.")]
        public float blurCell = 16.2f;
        [Tooltip("Cell height over the font's size: Windows picks a font by its cell, Hitmarker's winAscent + winDescent = 1.25 em.")]
        public float cellPerEm = 1.25f;
        [Tooltip("From the cell's top down to the baseline, em: Hitmarker's winAscent over its unitsPerEm.")]
        public float ascent = 1f;
        [Tooltip("From the bar's foot down to the name's cell (screen px).")]
        public float gap = 2f;
        public Color color = Color.white;
        public Color shadowColor = Color.black;
        [Tooltip("The name's own drop shadow, right and down (screen px): its font's shadow flag.")]
        public float drop = 1f;
        [Tooltip("The shadow font's blursize (screen px): Source blurs with a (2 x this + 1) wide Gaussian of sigma 0.683 x this.")]
        public int blur = 2;
        [Tooltip("Times the blurred shadow is drawn over itself.")]
        public int passes = 6;
    }

    /// <summary>The head left of the bar (rows Icon.*).</summary>
    [System.Serializable]
    public sealed class IconLook
    {
        [Tooltip("icon_normal.png's width; height (px at 1920 wide, both: the addon makes both ScrW * 0.0125).")]
        public Vector2 normalSize = new Vector2(24f, 24f);
        [Tooltip("From the bar's left end to the normal icon's (px at 1920 wide). ScrW * (0.0125 + 0.00078125).")]
        public float normalLeft = 25.5f;
        [Tooltip("icon_boss.png's width (px at 1920 wide); height (px at 1080 high). ScrW * 0.0138, ScrH * 0.0352.")]
        public Vector2 bossSize = new Vector2(26.496f, 38.016f);
        [Tooltip("From the bar's left end to the boss icon's (px at 1920 wide). ScrW * (0.0138 + 0.00078125).")]
        public float bossLeft = 27.996f;
        [Tooltip("From the bar's top up to the icon's (px at 1080 high). ScrH * 0.00463.")]
        public float up = 5.0004f;
        [Tooltip("Zombie kinds with the boss icon (the addon's boss list); everything else has the normal one.")]
        public ZombieKind[] bossKinds =
            { ZombieKind.Brute, ZombieKind.Exploder, ZombieKind.Charger, ZombieKind.Ninja, ZombieKind.Candyman, ZombieKind.Breadbag, ZombieKind.Reaper };
    }

    /// <summary>The columns a drop in health raises (rows Hit.*). Alphas are 0 to 255, as the addon's colours.</summary>
    [System.Serializable]
    public sealed class HitLook
    {
        [Tooltip("A column's first height (px at 1080 high): half the bar.")]
        public float start = 4.4982f;
        [Tooltip("How far above the bar's height it rises (px at 1080 high). ScrH * 0.0111.")]
        public float rise = 11.988f;
        [Tooltip("The column rises (frame time / this) of the rest each frame (seconds).")]
        public float riseTime = .16f;
        [Tooltip("The same for the line at its left.")]
        public float lineTime = .08f;
        [Tooltip("That line's width (screen px).")]
        public float lineWidth = 2f;
        [Tooltip("Past this share of its full height the column starts to fade.")]
        public float fadeFrom = .95f;
        [Tooltip("The column fades (frame time / this) of its alpha each frame (seconds).")]
        public float fadeTime = .15f;
        [Tooltip("The same for the line.")]
        public float lineFadeTime = .1f;
        [Tooltip("A column whose alpha (0 to 255) is down to this is gone.")]
        public float gone = .01f;
        public Color color = Color.white;
        [Tooltip("A headshot's column and chunk.")]
        public Color headColor = new Color32(255, 255, 94, 255);
        [Tooltip("The column's alpha as the drop is seen, before the hit's word paints it.")]
        public float firstAlpha = 60f;
        public float firstLineAlpha = 100f;
        [Tooltip("The column's alpha once the hit's word has painted it.")]
        public float alpha = 100f;
        public float lineAlpha = 140f;
        [Tooltip("Seconds of game time from the hit's word to painting the newest column (yellow for a headshot).")]
        public float wordDelay = .05f;
    }

    /// <summary>Which enemy's bar shows (rows Aim.*).</summary>
    [System.Serializable]
    public sealed class AimLook
    {
        [Tooltip("Metres: only an enemy this close shows its bar under the crosshair (the addon's 3000 units).")]
        public float reach = 57.15f;
        [Tooltip("Seconds the bar stays after the crosshair leaves the enemy.")]
        public float keep = 3f;
        [Tooltip("Metres the crosshair's ray looks (Garry's Mod's eye trace, 32768 units).")]
        public float traceRange = 624.2304f;
        [Tooltip("Seconds of game time after the local player's kill in which neither the aim nor their hits move the bar.")]
        public float pause = .4f;
        [Tooltip("Seconds of game time after the local player's hit in which any body or loose object under the crosshair counts as aimed at.")]
        public float damagedWindow = .1f;
    }

    // One enemy's bar, shown or not (the addon's HealthBars entry).
    private sealed class Entry
    {
        public MeleeCombatTarget Target;
        public GameObject Body;
        public ICombatVitals Vitals;
        public ZombieType Kind;
        public Vector3 Top;
        public bool Placed, Boss;
        // Health as last read (bo6_lasthp); after the last hit's report, for a body gone before it was read; its most.
        public float LastHealth = float.NaN, Reported = float.NaN, Maximum;
        // The trail's width (px); held after a hit (currently_delaying), sliding down (currently_following).
        public float Trail;
        public bool Show, Dead, Holding = true, Following;
        // Real seconds since it died or went (lifetime_after_removed).
        public float Gone;
        public string RawName, Name = "";
        public readonly List<Column> Columns = new List<Column>();
    }

    // What one drop in health raised (the addon's DamageBlocks entry). Lengths in px, alphas 0 to 255.
    private sealed class Column
    {
        public float Height, Line, Reach, Left, Width, Alpha, LineAlpha;
        public Color Tint;
        public bool FrontLine = true;
    }

    // The addon's timer.Simple calls: they run on game time (Garry's Mod timers go by CurTime).
    private enum Due { Paint, Release, Calm, Unpause }

    private struct Timer
    {
        public float At;
        public Due What;
        public Entry Entry;
        public bool Head;
    }

    // The pieces one shown bar is drawn with, bottom to top as the addon draws them.
    private sealed class BarView
    {
        public RectTransform Root, NameRoot, Shadow;
        public RawImage Back, Icon;
        public PixelRects Under, Over;
        public TextMeshProUGUI Drop, Face;
        public TextMeshProUGUI[] Taps = new TextMeshProUGUI[0];
        public float[] TapAlpha = new float[0];
        public int Blur = -1, Passes = -1, Cell = -1, BlurCell = -1;
        public string Text;
        public Color Colour, ShadowColour;
        public float FaceBaseline, FaceWidth, TapBaseline, TapWidth;
    }

    private static EnemyHealthBarHUD instance;
    private static Texture2D normalPicture, bossPicture, pattern;
    private readonly BarLook bar = new BarLook();
    private readonly NameLook label = new NameLook();
    private readonly IconLook icon = new IconLook();
    private readonly HitLook columns = new HitLook();
    private readonly AimLook aim = new AimLook();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<GameObject, Entry> byBody = new Dictionary<GameObject, Entry>();
    private readonly List<Timer> timers = new List<Timer>();
    private readonly List<BarView> views = new List<BarView>();
    private readonly RaycastHit[] rays = new RaycastHit[16];
    // Real seconds since the crosshair was last on an enemy in reach (TimeWithoutTarget); a hit of the local player's a
    // moment ago (GET_DAMAGED); their kill a moment ago (bo6_pause_trace).
    private float sinceAim;
    private bool damagedRecently, paused;

    private static Texture2D NormalPicture => normalPicture != null ? normalPicture : normalPicture = Resources.Load<Texture2D>(PictureFolder + "icon_normal");
    private static Texture2D BossPicture => bossPicture != null ? bossPicture : bossPicture = Resources.Load<Texture2D>(PictureFolder + "icon_boss");
    private static Texture2D Pattern => pattern != null ? pattern : pattern = Resources.Load<Texture2D>(PictureFolder + "bg_alt_1");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        normalPicture = bossPicture = pattern = null;
    }

    /// <summary>
    /// An enemy or practice target with health has come into play (MeleeCombatTarget): its bar is kept from now on,
    /// shown or not. Again for one already kept changes nothing, unless it died: a new life is a new bar.
    /// </summary>
    public static void Track(MeleeCombatTarget target)
    {
        if (!Application.isPlaying || target == null) return;
        if (instance == null)
        {
            var host = new GameObject("Enemy Health Bar", typeof(RectTransform), typeof(Canvas));
            host.AddComponent<EnemyHealthBarHUD>();
        }
        if (instance != null) instance.Add(target);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelAssets.GroupRuntimeRoot(gameObject, "Screen");
        // One canvas unit per screen pixel and no scaler: the addon scales each length by the screen's width or height
        // itself (Tick), and its whole-pixel cuts land on real pixels only at that scale.
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Order;
        GameTables.BindNested(TableName, BarRow, this, bar);
        GameTables.BindNested(TableName, NameRow, this, label);
        GameTables.BindNested(TableName, IconRow, this, icon);
        GameTables.BindNested(TableName, HitRow, this, columns);
        GameTables.BindNested(TableName, AimRow, this, aim);
    }

    private void OnEnable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private void OnDisable() => CombatDamageEvents.Damaged -= OnDamaged;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Add(MeleeCombatTarget target)
    {
        var body = target.gameObject;
        if (byBody.TryGetValue(body, out var kept))
        {
            if (!kept.Dead) return;
            entries.Remove(kept);
        }
        var entry = new Entry { Target = target, Body = body, Vitals = target.GetComponent<ICombatVitals>() };
        entries.Add(entry);
        byBody[body] = entry;
    }

    /// <summary>
    /// A hit landed (the addon's SendDamageToClient, which the server sends for every hit a player or an NPC lands).
    /// Every attacker's hit holds the enemy's trail and paints its newest column, as there; only the local player's own
    /// hits bring its bar up and take it off the others (the addon would follow a teammate's hit anywhere), set the
    /// aim's short grace and, on a kill, the pause.
    /// </summary>
    private void OnDamaged(CombatDamageReport report)
    {
        var hit = report.Hit;
        if (report.Target == null || hit.Kind == DamageKind.Fall || hit.Source == null) return;
        if (!byBody.TryGetValue(report.Target.gameObject, out var entry)) return;
        entry.Reported = report.HealthAfter;
        entry.Maximum = report.MaximumHealth;
        if (entry.Target != null && !entry.Dead) { entry.Top = TopOf(entry.Target); entry.Placed = true; }
        bool mine = hit.Source.GetComponentInParent<PlayerMovement>() != null && !report.ByOtherPlayer;
        if (mine)
        {
            var local = PlayerRoster.Local;
            if (local != null && Vector3.Distance(report.Target.transform.position, local.transform.position) <= aim.reach) damagedRecently = true;
            After(aim.damagedWindow, Due.Calm, null);
            foreach (var other in entries)
                if (other != entry) other.Show = false;
            if (!paused) entry.Show = true;
            if (report.HealthAfter <= 0f)
            {
                paused = true;
                After(aim.pause, Due.Unpause, null);
            }
        }
        if (!entry.Holding && !entry.Following) entry.Holding = true;
        After(columns.wordDelay, Due.Paint, entry, report.IsCritical);
        After(bar.hold, Due.Release, entry);
    }

    private void After(float seconds, Due what, Entry entry, bool head = false)
        => timers.Add(new Timer { At = Time.time + seconds, What = what, Entry = entry, Head = head });

    private void RunTimers(float now)
    {
        for (int i = 0; i < timers.Count; i++)
        {
            var timer = timers[i];
            if (timer.At > now) continue;
            timers.RemoveAt(i--);
            switch (timer.What)
            {
                case Due.Paint:
                    // The newest column takes the hit's colour; with the trail held or sliding it loses its right line
                    // and reaches one pixel further instead.
                    var list = timer.Entry.Columns;
                    if (list.Count == 0) break;
                    var newest = list[list.Count - 1];
                    newest.Tint = timer.Head ? columns.headColor : columns.color;
                    newest.Alpha = columns.alpha;
                    newest.LineAlpha = columns.lineAlpha;
                    if (timer.Entry.Holding || timer.Entry.Following) newest.FrontLine = false;
                    break;
                case Due.Release:
                    timer.Entry.Holding = false;
                    timer.Entry.Following = true;
                    break;
                case Due.Calm:
                    damagedRecently = false;
                    break;
                case Due.Unpause:
                    paused = false;
                    break;
            }
        }
    }

    private void LateUpdate()
    {
        RunTimers(Time.time);
        int drawn = entries.Count > 0 ? Tick(Time.unscaledDeltaTime) : 0;
        for (int i = drawn; i < views.Count; i++)
            if (views[i].Root.gameObject.activeSelf) views[i].Root.gameObject.SetActive(false);
    }

    // The addon's HUDPaint, entry by entry in its order; returns how many bars it drew.
    private int Tick(float dt)
    {
        var view = Camera.main;
        var local = PlayerRoster.Local;
        float across = Screen.width / 1920f, down = Screen.height / 1080f;
        float barW = bar.width * across, barH = bar.height * down;
        Entry aimed = null;
        bool met = false;
        Vector3 metAt = default;
        if (view != null) Look(view, local, out aimed, out met, out metAt);
        float reach = aim.reach, distance = reach * 2f;
        // The addon tests "not IsNPC or not IsNextBot", which nothing fails, so this clock runs every frame and only an
        // enemy in reach under the crosshair sets it back: a bar stays Aim.keep seconds after the aim leaves it.
        sinceAim += dt;
        if (sinceAim >= aim.keep)
            foreach (var each in entries) each.Show = false;
        if (met && (aimed != null || damagedRecently))
        {
            distance = Vector3.Distance(metAt, local != null ? local.transform.position : view.transform.position);
            if (distance <= reach) sinceAim = 0f;
        }
        int drawn = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool valid = entry.Target != null && entry.Target.isActiveAndEnabled;
            if (valid && entry.Vitals == null) entry.Vitals = entry.Target.GetComponent<ICombatVitals>();
            // No health to read yet (its health part comes on after it): nothing to show of it until there is.
            if (valid && entry.Vitals == null) continue;
            float health = 0f;
            if (valid)
            {
                health = entry.Vitals.Health;
                entry.Maximum = entry.Vitals.MaximumHealth;
                Watch(entry, health, barW, barH, down);
                // Read here: a report of a hit older than this reading says nothing more.
                entry.Reported = float.NaN;
                if (!paused && aimed == entry && distance <= reach)
                {
                    foreach (var other in entries) other.Show = false;
                    entry.Show = true;
                }
                if (!entry.Dead && health <= 0f)
                {
                    // Dead: its bar stays where it died, as the addon's stays where its NPC was removed.
                    entry.Dead = true;
                    entry.Top = TopOf(entry.Target);
                    entry.Placed = true;
                }
            }
            // Gone before this frame read its last health (destroyed by the kill): the hit's own report of it.
            else if (!float.IsNaN(entry.Reported)) Watch(entry, entry.Reported, barW, barH, down);
            // Its last moment counts while it is dead or gone; one shown again alive (a copy hidden and back) starts afresh.
            if (!valid || entry.Dead) entry.Gone += dt;
            else entry.Gone = 0f;
            float healthWidth = valid && health > 0f && entry.Maximum > 0f ? Mathf.Clamp(health / entry.Maximum * barW, 0f, barW) : 0f;
            // The trail starts full, and comes back full whenever it lands on exactly nothing (the addon's follower_init,
            // never set).
            if (entry.Trail == 0f) entry.Trail = barW;
            if (!entry.Holding) entry.Trail = Mathf.Lerp(entry.Trail, healthWidth, dt / bar.follow);
            if (healthWidth * bar.snap > entry.Trail)
            {
                entry.Trail = healthWidth;
                entry.Following = false;
            }
            BarView shown = null;
            float x = 0f, y = 0f;
            if (entry.Show && view != null && OnScreen(entry, view, valid, out Vector2 at))
            {
                x = at.x - barW * .5f;
                y = at.y - barH * .5f - bar.above * down;
                if (drawn == views.Count) views.Add(Build());
                shown = views[drawn++];
                DrawBar(shown, entry, valid, x, y, barW, barH, healthWidth, across, down);
            }
            Rise(entry, shown, x, y, barH, dt);
            if (entry.Gone >= bar.deadHold)
            {
                entries.RemoveAt(i--);
                if (byBody.TryGetValue(entry.Body, out var held) && held == entry) byBody.Remove(entry.Body);
            }
        }
        return drawn;
    }

    // What the crosshair is on (the addon's GetEyeTrace: the first solid thing along the view, its own body left out).
    // An enemy is met by its collision capsule, as an NPC by its hull. Anything else counts only while damagedRecently,
    // as any valid entity there: a body or a loose object, not the world.
    private void Look(Camera view, PlayerMovement local, out Entry aimed, out bool met, out Vector3 metAt)
    {
        aimed = null;
        met = false;
        metAt = default;
        var eye = view.transform;
        int count = Physics.RaycastNonAlloc(eye.position, eye.forward, rays, aim.traceRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var results = rays;
        if (count == rays.Length)
        {
            // A full buffer may have left out the nearest; the rare crowded ray asks for all of them.
            results = Physics.RaycastAll(eye.position, eye.forward, aim.traceRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            count = results.Length;
        }
        var own = local != null ? local.transform : null;
        int found = -1;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var collider = results[i].collider;
            if (collider == null || results[i].distance >= nearest || (own != null && collider.transform.IsChildOf(own))) continue;
            nearest = results[i].distance;
            found = i;
        }
        if (found < 0) return;
        var struck = results[found].collider;
        var target = struck.GetComponentInParent<MeleeCombatTarget>();
        if (target != null)
        {
            if (byBody.TryGetValue(target.gameObject, out var entry)) aimed = entry;
            met = true;
            metAt = target.transform.position;
            return;
        }
        if (struck.attachedRigidbody != null)
        {
            met = true;
            metAt = struck.attachedRigidbody.position;
            return;
        }
        var player = struck.GetComponentInParent<PlayerMovement>();
        if (player != null)
        {
            met = true;
            metAt = player.transform.position;
        }
    }

    // A drop in health raises a column where the health now ends, as wide as the drop (the addon reads every NPC's
    // health each frame and compares it with the last).
    private void Watch(Entry entry, float health, float barW, float barH, float down)
    {
        if (!float.IsNaN(entry.LastHealth) && entry.LastHealth > health)
        {
            float before = entry.LastHealth, most = entry.Maximum > 0f ? entry.Maximum : 1f;
            float taken = Mathf.Min(before, Mathf.Abs(before - health));
            entry.Columns.Add(new Column
            {
                Height = columns.start * down,
                Reach = barH + columns.rise * down,
                Left = health / most * barW,
                Width = Mathf.Clamp(taken / most * barW, 0f, barW),
                Tint = columns.color,
                Alpha = columns.firstAlpha,
                LineAlpha = columns.firstLineAlpha,
            });
        }
        entry.LastHealth = health;
    }

    /// <summary>
    /// The middle of the top of an enemy's collision shape: the addon puts its bar over OBBCenter.x/y and OBBMaxs.z, the
    /// NPC's hull, which keeps its size whatever the body does. A zombie's hull is its capsule (fitted to its head, grown
    /// for a brute and shrunk to a crawl by ZombieBody), read whether it is on or off (off while it lies knocked down).
    /// </summary>
    private static Vector3 TopOf(MeleeCombatTarget target)
    {
        if (target.TryGetComponent(out CapsuleCollider capsule))
        {
            Vector3 centre = capsule.center;
            float up = capsule.direction == 1 ? Mathf.Max(capsule.height * .5f, capsule.radius) : capsule.radius;
            return capsule.transform.TransformPoint(new Vector3(centre.x, centre.y + up, centre.z));
        }
        return CombatFoe.Top(target);
    }

    // Where the bar's anchor falls on the screen, y down from the top as Source's ToScreen gives it. A point behind the
    // view has no place on the screen and its bar is not drawn (the addon draws it mirrored, a glitch).
    private static bool OnScreen(Entry entry, Camera view, bool valid, out Vector2 at)
    {
        at = default;
        if (valid && !entry.Dead)
        {
            entry.Top = TopOf(entry.Target);
            entry.Placed = true;
        }
        if (!entry.Placed) return false;
        Vector3 screen = view.WorldToScreenPoint(entry.Top);
        if (screen.z <= 0f) return false;
        at = new Vector2(screen.x, Screen.height - screen.y);
        return true;
    }

    private void DrawBar(BarView view, Entry entry, bool valid, float x, float y, float barW, float barH, float healthWidth, float across, float down)
    {
        if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
        var under = view.Under;
        under.Clear();
        var ground = bar.altBackground ? Pattern : null;
        view.Back.enabled = ground != null;
        if (ground != null)
        {
            view.Back.texture = ground;
            Picture(view.Back, x, y, barW, barH);
        }
        else Fill(under, x, y, barW, barH, Shade(bar.background));
        Fill(under, x, y, entry.Trail, barH, Shade(bar.follower));
        Fill(under, x, y, healthWidth, barH, Shade(bar.health));
        if (healthWidth < barW) Fill(under, x + healthWidth - bar.line, y, bar.line, barH, Shade(bar.edge));
        Outline(under, x, y, barW, barH, Shade(bar.outline));
        Label(view, entry, valid, x, y + barH, down);
        Mark(view, entry, valid, x, y, across, down);
        view.Over.Clear();
    }

    // The name in its cell under the bar: the blurred shadow drawn Name.passes times, then the name with its own drop
    // shadow (draw.DrawText puts each at the cell's top left, rounded up to a whole pixel).
    private void Label(BarView view, Entry entry, bool valid, float x, float barFoot, float down)
    {
        if (valid && label.show)
        {
            // Capitals, as the addon's string.upper; worked out again only when the name changes.
            string raw = CombatDamageEvents.DisplayNameOf(entry.Target) ?? "";
            if (raw != entry.RawName)
            {
                entry.RawName = raw;
                entry.Name = raw.ToUpperInvariant();
            }
        }
        string text = entry.Name ?? "";
        bool any = text.Length > 0;
        if (view.NameRoot.gameObject.activeSelf != any) view.NameRoot.gameObject.SetActive(any);
        if (!any) return;
        // Source's font sizes are whole pixels.
        int cell = Mathf.Max(1, (int)(label.cell * down)), blurCell = Mathf.Max(1, (int)(label.blurCell * down));
        Dress(view, text, cell, blurCell);
        float top = barFoot + label.gap;
        float faceTop = Up(top) + Ascent(Em(cell)) + view.FaceBaseline;
        float faceX = Up(x);
        view.Face.rectTransform.anchoredPosition = new Vector2(faceX, -faceTop);
        view.Drop.rectTransform.anchoredPosition = new Vector2(faceX + label.drop, -(faceTop + label.drop));
        if (view.Shadow.gameObject.activeSelf != label.shadow) view.Shadow.gameObject.SetActive(label.shadow);
        if (!label.shadow) return;
        // The blurred font is a size up; the addon centres it on the name by the two texts' widths and heights (the
        // name's height counts its drop shadow).
        float shadowX = Up(x - (view.TapWidth - view.FaceWidth) * .5f);
        float shadowY = Up(top - (blurCell - (cell + label.drop)) * .5f);
        view.Shadow.anchoredPosition = new Vector2(shadowX, -(shadowY + Ascent(Em(blurCell)) + view.TapBaseline));
    }

    // The head left of the bar: the boss's for the kinds listed, the zombie's for the rest.
    private void Mark(BarView view, Entry entry, bool valid, float x, float y, float across, float down)
    {
        if (valid) entry.Boss = IsBoss(entry);
        var picture = entry.Boss ? BossPicture : NormalPicture;
        view.Icon.enabled = picture != null;
        if (picture == null) return;
        view.Icon.texture = picture;
        if (entry.Boss) Picture(view.Icon, x - icon.bossLeft * across, y - icon.up * down, icon.bossSize.x * across, icon.bossSize.y * down);
        // Both by the screen's width: the addon's ScrW * 0.0125 square.
        else Picture(view.Icon, x - icon.normalLeft * across, y - icon.up * down, icon.normalSize.x * across, icon.normalSize.y * across);
    }

    private bool IsBoss(Entry entry)
    {
        if (entry.Kind == null && entry.Target.TryGetComponent(out ZombieType kind)) entry.Kind = kind;
        if (entry.Kind == null || icon.bossKinds == null) return false;
        foreach (var boss in icon.bossKinds)
            if (boss == entry.Kind.Kind) return true;
        return false;
    }

    // Each column rises from the bar's foot and fades once nearly up, shown or not; drawn over everything else of the
    // bar when it shows: its line, its body (one pixel wider without the right line), and the solid chunk inside the
    // outline, cut to the trail (render.SetScissorRect).
    private void Rise(Entry entry, BarView view, float x, float y, float barH, float dt)
    {
        var list = entry.Columns;
        for (int i = 0; i < list.Count; i++)
        {
            var column = list[i];
            // Never all the way in one frame: a column at its full height never fades (the addon's test wants it still
            // below), and a frame of a sixth of a second (a hitch) would have left it standing for good.
            column.Height = Mathf.Lerp(column.Height, column.Reach, Mathf.Min(dt / columns.riseTime, .999f));
            column.Line = Mathf.Lerp(column.Line, column.Reach, dt / columns.lineTime);
            if (column.Height > columns.fadeFrom * column.Reach && column.Height < column.Reach)
                column.Alpha = Mathf.Lerp(column.Alpha, 0f, dt / columns.fadeTime);
            if (column.Line > columns.fadeFrom * column.Reach && column.Height < column.Reach)
                column.LineAlpha = Mathf.Lerp(column.LineAlpha, 0f, dt / columns.lineFadeTime);
            column.Alpha = Mathf.Max(columns.gone, column.Alpha);
            column.LineAlpha = Mathf.Max(columns.gone, column.LineAlpha);
            if (view != null)
            {
                var over = view.Over;
                float left = Mathf.Max(x, x + column.Left), foot = y + barH - bar.line, width = columns.lineWidth;
                Color32 line = Shade(column.Tint, column.LineAlpha);
                Fill(over, left, foot - column.Line, width, column.Line, line);
                float wider = 0f;
                if (column.FrontLine) Fill(over, left + column.Width - width, foot - column.Line, width, column.Line, line);
                else wider = bar.line;
                Fill(over, left, foot - column.Height, column.Width + wider, column.Height, Shade(column.Tint, column.Alpha));
                Clipped(over, left, y + bar.line, column.Width, barH - 2f * bar.line, Shade(column.Tint, 255f),
                    Cut(x), Cut(y), Cut(x + entry.Trail), Cut(y + barH));
            }
            if (column.Alpha <= columns.gone) list.RemoveAt(i--);
        }
    }

    // ---- Drawing as Source's surface functions do ----

    // A length as surface.DrawRect takes it: the whole pixels of it (C's cast drops the fraction).
    private float Cut(float value) => bar.wholePixels ? (int)value : value;

    // A text position as draw.SimpleText puts it: math.ceil.
    private float Up(float value) => bar.wholePixels ? Mathf.Ceil(value) : value;

    // A font's size in whole pixels for its cell (GDI's MulDiv rounds to the nearest), and its ascent in pixels.
    private int Em(int cell) => Mathf.Max(1, (int)System.Math.Floor(cell / Mathf.Max(.01f, label.cellPerEm) + .5f));
    private int Ascent(int em) => (int)System.Math.Floor(label.ascent * em + .5f);

    private void Fill(PixelRects rects, float x, float y, float w, float h, Color32 colour)
    {
        float left = Cut(x), top = Cut(y);
        rects.Add(left, top, left + Cut(w), top + Cut(h), colour);
    }

    // surface.DrawOutlinedRect: four rectangles of Bar.line inside the box.
    private void Outline(PixelRects rects, float x, float y, float w, float h, Color32 colour)
    {
        float left = Cut(x), top = Cut(y), right = left + Cut(w), bottom = top + Cut(h), t = bar.line;
        rects.Add(left, top, right, top + t, colour);
        rects.Add(left, bottom - t, right, bottom, colour);
        rects.Add(left, top + t, left + t, bottom - t, colour);
        rects.Add(right - t, top + t, right, bottom - t, colour);
    }

    private void Clipped(PixelRects rects, float x, float y, float w, float h, Color32 colour, float clipLeft, float clipTop, float clipRight, float clipBottom)
    {
        float left = Cut(x), top = Cut(y), right = left + Cut(w), bottom = top + Cut(h);
        rects.Add(Mathf.Max(left, clipLeft), Mathf.Max(top, clipTop), Mathf.Min(right, clipRight), Mathf.Min(bottom, clipBottom), colour);
    }

    // surface.DrawTexturedRect: the picture over the whole pixels of the box.
    private void Picture(RawImage image, float x, float y, float w, float h)
    {
        var rect = image.rectTransform;
        rect.anchoredPosition = new Vector2(Cut(x), -Cut(y));
        rect.sizeDelta = new Vector2(Cut(w), Cut(h));
    }

    // A table colour as the addon's 0-255 colour; alpha 0 to 255 cut to a whole number, as surface.SetDrawColor takes it.
    private static Color32 Shade(Color colour) => Shade(colour, colour.a * 255f);

    private static Color32 Shade(Color colour, float alpha)
    {
        Color32 bytes = colour;
        bytes.a = (byte)Mathf.Clamp((int)alpha, 0, 255);
        return bytes;
    }

    // ---- The pieces of a shown bar ----

    private BarView Build()
    {
        var view = new BarView { Root = Corner("Bar", transform) };
        view.Back = Corner("Pattern", view.Root).gameObject.AddComponent<RawImage>();
        view.Back.raycastTarget = false;
        view.Under = Corner("Bar Rectangles", view.Root).gameObject.AddComponent<PixelRects>();
        view.Under.raycastTarget = false;
        view.NameRoot = Corner("Name", view.Root);
        view.Shadow = Corner("Blurred Shadow", view.NameRoot);
        view.Drop = NewText("Drop Shadow", view.NameRoot);
        view.Face = NewText("Name", view.NameRoot);
        view.Icon = Corner("Icon", view.Root).gameObject.AddComponent<RawImage>();
        view.Icon.raycastTarget = false;
        view.Over = Corner("Hit Columns", view.Root).gameObject.AddComponent<PixelRects>();
        view.Over.raycastTarget = false;
        return view;
    }

    // The name and its shadows, written again only when the text, the sizes or the colours change.
    private void Dress(BarView view, string text, int cell, int blurCell)
    {
        int blur = Mathf.Clamp(label.blur, 0, 8), passes = Mathf.Max(1, label.passes);
        bool rebuilt = view.Blur != blur || view.Passes != passes;
        if (rebuilt) BuildTaps(view, blur, passes);
        if (!rebuilt && view.Text == text && view.Cell == cell && view.BlurCell == blurCell && view.Colour == label.color
            && view.ShadowColour == label.shadowColor) return;
        view.Text = text;
        view.Cell = cell;
        view.BlurCell = blurCell;
        view.Colour = label.color;
        view.ShadowColour = label.shadowColor;
        int em = Em(cell), blurEm = Em(blurCell);
        Write(view.Face, text, em, label.color);
        Write(view.Drop, text, em, label.shadowColor);
        for (int i = 0; i < view.Taps.Length; i++)
        {
            var tint = label.shadowColor;
            tint.a *= view.TapAlpha[i];
            Write(view.Taps[i], text, blurEm, tint);
        }
        view.Face.ForceMeshUpdate(true);
        view.FaceBaseline = Baseline(view.Face);
        view.FaceWidth = view.Face.preferredWidth;
        if (view.Taps.Length > 0)
        {
            view.Taps[0].ForceMeshUpdate(true);
            view.TapBaseline = Baseline(view.Taps[0]);
            view.TapWidth = view.Taps[0].preferredWidth;
        }
        else
        {
            view.TapBaseline = view.FaceBaseline;
            view.TapWidth = view.FaceWidth;
        }
    }

    /// <summary>
    /// The blurred shadow as copies of the name, one per pixel of Source's blur kernel (vgui_surfacelib's
    /// ApplyGaussianBlurToTexture: (2r+1)² taps weighing g(i)·g(j), g(d) = 1/√(2·3.14·s²)·2.7^(−d²/2s²) with s = 0.683r,
    /// its own rounded π and e included). Drawn black over each other, a copy of weight w at alpha 1 − (1 − w)^passes
    /// adds up to the addon's blurred text drawn Name.passes times, but for the cross terms of overlapping taps.
    /// </summary>
    private static void BuildTaps(BarView view, int blur, int passes)
    {
        foreach (var tap in view.Taps)
            if (tap != null) Destroy(tap.gameObject);
        int side = blur * 2 + 1;
        view.Taps = new TextMeshProUGUI[side * side];
        view.TapAlpha = new float[side * side];
        double sigma = .683 * Mathf.Max(1, blur);
        int n = 0;
        for (int j = -blur; j <= blur; j++)
            for (int i = -blur; i <= blur; i++)
            {
                var tap = NewText("Tap " + i + "," + j, view.Shadow);
                tap.rectTransform.anchoredPosition = new Vector2(i, -j);
                double weight = blur > 0 ? Gauss(i, sigma) * Gauss(j, sigma) : 1.0;
                view.TapAlpha[n] = (float)(1.0 - System.Math.Pow(1.0 - System.Math.Min(1.0, weight), passes));
                view.Taps[n++] = tap;
            }
        view.Blur = blur;
        view.Passes = passes;
        view.Text = null;
    }

    private static double Gauss(int d, double sigma)
        => 1.0 / System.Math.Sqrt(2 * 3.14 * sigma * sigma) * System.Math.Pow(2.7, -(d * d) / (2 * sigma * sigma));

    private static void Write(TextMeshProUGUI text, string value, int em, Color colour)
    {
        if (text.text != value) text.text = value;
        if (!Mathf.Approximately(text.fontSize, em)) text.fontSize = em;
        if (text.color != colour) text.color = colour;
    }

    // Where TMP put the first baseline, in the text's own space (its top left at 0, y up).
    private static float Baseline(TMP_Text text)
    {
        var info = text.textInfo;
        if (info != null && info.lineCount > 0) return info.lineInfo[0].baseline;
        var face = text.font != null ? text.font.faceInfo : default;
        return face.pointSize > 0f ? -face.ascentLine / face.pointSize * text.fontSize : -text.fontSize;
    }

    private static TextMeshProUGUI NewText(string title, Transform parent)
    {
        var rect = Corner(title, parent);
        rect.sizeDelta = new Vector2(1024f, 128f);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        FontRoles.Apply(text, FontRoles.EnemyBar);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.raycastTarget = false;
        // Source's Windows fonts advance by each glyph's own widths: no kerning pairs, no ligatures.
        var features = new List<OTL_FeatureTag>(text.fontFeatures);
        features.Remove(OTL_FeatureTag.kern);
        features.Remove(OTL_FeatureTag.liga);
        text.fontFeatures = features;
        return text;
    }

    // A rectangle placed from the top left corner of its parent, y down as the addon counts.
    private static RectTransform Corner(string title, Transform parent)
    {
        var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }
}
