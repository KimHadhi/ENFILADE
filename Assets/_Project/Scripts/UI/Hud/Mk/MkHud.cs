using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD Mk II, the player's HUD since the user's pick of the HUD drafts (2026-11-22): our HUD was "messy and too much, like
/// a mobile game", they wanted the look of the Garry's Mod addon "HUD Mk II" in their video, and on the draft page they
/// marked its card "좋아" with one note, "내 프로필 아이콘 넣을 공간 넣을 것" (room for their profile picture). One owner,
/// on this PC's player (PlayerMovement adds it), drawing on one flat overlay canvas, every number from
/// Tables/Hud/MkHud.csv: health and armour with the profile picture, rank and stance (MkStatus), teammates (MkTeam), timed
/// effects (MkEffects), the objective (MkMission) and the line that says a new one (MkNotice), the gun (MkAmmo), the
/// tiles over it for the throwable, the class and the ultimate (MkGear), what was picked up (MkPickups) and the
/// screen-edge glow (MkFlare). The weapon slots are drawn over the gun's block by their own owner, WeaponSlotsHUD, in Mk
/// II's look on a frame lent here (Lend): the user turned down the addon's weapon selector across the screen's middle for
/// the old slots in this look (2026-11-22: "무기 슬롯 1,2,3,4번 그거는 기존 방식이 더 맘에 들어. 기존 방식으로 디자인만
/// 바꿔서").
/// <para>Every piece sways as the addon's does: its place moves by how far the look has run ahead of a lagged copy of
/// itself (Hud.sway px a degree, scaled by Settings → HUD 흔들림), and it turns with a Q/E lean as the rest of the HUD.
/// The old pieces it replaces stand aside while MkHud.csv Hud.on is 1 (HudRig, MissionHUD, ClassHUD, PlayerMovement and
/// the feeds' static calls ask On); 0 brings the old HUD back on the next scene.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class MkHud : MonoBehaviour
{
    public const string TableName = "MkHud";
    private const string IconFolder = "UI/MkHud/";

    private static MkHud local;
    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    internal readonly MkLook Look = new MkLook();
    private readonly List<MkPiece> pieces = new List<MkPiece>();
    private GameObject host;
    private MkFlare flare;
    private MkAmmo ammo;
    private MkPickups pickups;
    private PlayerDamageReceiver watched;
    private float lastHealth = float.NaN;
    private Vector3 lag;
    private bool lagged;
    private Vector2 sway;

    /// <summary>Whether HUD Mk II stands in for the old HUD (MkHud.csv Hud.on). Asked where the old pieces are made.</summary>
    public static bool On => GameTables.Flag(TableName, "Hud", "on", true);

    /// <summary>The player whose HUD this is.</summary>
    public PlayerMovement Player { get; private set; }

    /// <summary>This PC's HUD Mk II once it is up: null before its Start, after it is gone, or while Hud.on is 0.</summary>
    internal static MkHud Local => local != null && local.host != null ? local : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        local = null;
        icons.Clear();
    }

    /// <summary>A picture of the addon's (Resources/UI/MkHud/&lt;name&gt;), or null.</summary>
    public static Texture2D Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (icons.TryGetValue(name, out var icon) && icon != null) return icon;
        return icons[name] = Resources.Load<Texture2D>(IconFolder + name);
    }

    /// <summary>The round's picture for a gun's ammunition (MkHud.csv AmmoIcon.&lt;ammunition&gt;, else AmmoIcon.other).</summary>
    public Texture AmmoIcon(WeaponData gun)
    {
        string fallback = GameTables.Text(TableName, "AmmoIcon", "other", "weapon");
        return Icon(gun != null ? GameTables.Text(TableName, "AmmoIcon", gun.Ammunition, fallback) : fallback);
    }

    /// <summary>Something taken (FieldPickups through PickupFeedHUD.Show): a row in the pickup list.</summary>
    public static void Picked(Texture icon, string name, string gives, bool gun)
    {
        if (local != null && local.pickups != null) local.pickups.Add(icon, name, gives, gun);
    }

    /// <summary>The gun's boxes light once: what they show changed by something taken (AmmoHUD.Blink's callers).</summary>
    public static void BlinkAmmo(Color colour)
    {
        if (local != null && local.ammo != null) local.ammo.Blink(colour);
    }

    /// <summary>The screen's edge glows once for a shield given (AdaptiveHealthHUD.Blink's callers); a heal glows by itself.</summary>
    public static void BlinkShield()
    {
        if (local != null && local.flare != null) local.flare.Flash(local.Look.Glow.armor);
    }

    private void Awake()
    {
        Player = GetComponent<PlayerMovement>();
        Look.Bind(this);
    }

    private void Start()
    {
        if (Player == null || !Player.IsLocal || !On) { enabled = false; return; }
        local = this;
        host = new GameObject("Mk HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupOverlay(host, Look.Hud.order);
        var canvas = (RectTransform)host.transform;
        // Back to front: the edge glow under everything, then the pieces.
        flare = Add(new MkFlare(this, canvas));
        Add(new MkStatus(this, canvas));
        Add(new MkTeam(this, canvas));
        Add(new MkEffects(this, canvas));
        var notice = Add(new MkNotice(this, canvas));
        Add(new MkMission(this, canvas, notice));
        ammo = Add(new MkAmmo(this, canvas));
        Add(new MkGear(this, canvas));
        pickups = Add(new MkPickups(this, canvas));
    }

    private T Add<T>(T piece) where T : MkPiece
    {
        pieces.Add(piece);
        return piece;
    }

    /// <summary>
    /// A frame on the Mk II canvas for something another owner draws there in Mk II's look, made once by its name and
    /// pinned to <paramref name="corner"/> as a piece's frame is: it sways and turns with the lean as the pieces do, and
    /// goes with the HUD. The weapon slots borrow one: the user wanted the old slots back in the new look (2026-11-22:
    /// "기존 방식으로 디자인만 바꿔서"), so WeaponSlotsHUD stays their one owner rather than Mk II drawing a copy of them.
    /// </summary>
    internal RectTransform Lend(string name, Vector2 corner)
    {
        foreach (var piece in pieces)
            if (piece is Lent && piece.Frame != null && piece.Frame.name == name) return piece.Frame;
        return Add(new Lent(this, (RectTransform)host.transform, name, corner)).Frame;
    }

    // A lent frame: its borrower draws in it, the HUD only moves it.
    private sealed class Lent : MkPiece
    {
        public Lent(MkHud hud, RectTransform canvas, string name, Vector2 corner) : base(hud, canvas, name, corner) { }

        public override void Tick(float dt) { }
    }

    private void LateUpdate()
    {
        if (host == null) return;
        float dt = Time.unscaledDeltaTime;
        Sway(dt);
        Watch();
        var turn = Look.Hud.lean ? Quaternion.Euler(0f, 0f, HudCamera.LeanRoll) : Quaternion.identity;
        foreach (var piece in pieces)
        {
            piece.Tick(dt);
            var at = piece.Sways ? sway : Vector2.zero;
            if (piece.Frame.anchoredPosition != at) piece.Frame.anchoredPosition = at;
            var lean = piece.Sways ? turn : Quaternion.identity;
            if (piece.Frame.localRotation != lean) piece.Frame.localRotation = lean;
        }
        // The gun's block keeps the right edge's passing pieces (the callout windows) above it, as the old ammo display did.
        if (Player != null && !Player.IsDead && HudRoom.Measure(ammo.Room, out float near, out float far, out float top, out float bottom))
            HudRoom.Take(this, near, far, top, bottom, HudRoom.Way.Over);
        else HudRoom.Free(this);
    }

    // The addon's sway: a copy of the look follows it a step each frame (LerpAngle), and the HUD stands off by how far the
    // look has run ahead, trailing a turn (left on a turn right, up on a look down), Hud.sway px a degree.
    private void Sway(float dt)
    {
        var view = Camera.main;
        if (view == null) { sway = Vector2.zero; lagged = false; return; }
        Vector3 eye = view.transform.eulerAngles;
        if (!lagged) { lag = eye; lagged = true; }
        float step = Mathf.Clamp01(dt * Look.Hud.swayFollow);
        lag.x = Mathf.LerpAngle(lag.x, eye.x, step);
        lag.y = Mathf.LerpAngle(lag.y, eye.y, step);
        // Settings → HUD 흔들림 at its default (0.5) gives the addon's own strength.
        float strength = Look.Hud.sway * Mathf.Clamp01(GameSettings.Current.hudSway) * 2f, most = Look.Hud.swayMost;
        sway = new Vector2(Mathf.Clamp(-Mathf.DeltaAngle(lag.y, eye.y) * strength, -most, most),
            Mathf.Clamp(Mathf.DeltaAngle(lag.x, eye.x) * strength, -most, most));
    }

    // The edge glow's causes: a hit taken (on the shield or on health) and health coming back.
    private void Watch()
    {
        var receiver = Player != null ? Player.DamageReceiver : null;
        if (receiver != watched)
        {
            if (watched != null) watched.HitResolved -= OnHit;
            watched = receiver;
            if (watched != null) watched.HitResolved += OnHit;
            lastHealth = float.NaN;
        }
        if (receiver == null) return;
        float health = receiver.Health;
        // Coming back from nothing (a respawn) is not a heal.
        if (!float.IsNaN(lastHealth) && lastHealth > 0f && health > lastHealth + .01f) flare.Flash(Look.Glow.heal);
        lastHealth = health;
    }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hit.TakesHold || watched == null) return;
        flare.Flash(watched.LastShieldTaken > 0f ? Look.Glow.armor : Look.Glow.damage);
    }

    private void OnDestroy()
    {
        if (watched != null) watched.HitResolved -= OnHit;
        foreach (var piece in pieces) piece.Dispose();
        pieces.Clear();
        HudRoom.Free(this);
        if (host != null) Destroy(host);
        if (local == this) local = null;
    }
}
