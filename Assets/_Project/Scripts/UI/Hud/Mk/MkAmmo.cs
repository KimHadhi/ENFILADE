using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The gun in hand at the bottom right, as the addon's DrawAmmo draws it: a dark rounded magazine box with the round's
/// icon at its left (Ammo.icon.&lt;ammunition&gt;: the addon's ammo-type pictures), the count right-aligned over a dim
/// "000", red at 30 % of the magazine and below; the reserve in a smaller box beside it, red once it holds no more than
/// one magazine; the gun's name spaced out over the box with its fire mode. A shot lights the magazine box a moment,
/// redder as the magazine empties (the addon's fire flicker, eased so a long burst glows instead of flickering).
/// What the old display also said keeps a place: a line under the box fills as the reload goes, a short line under it
/// says what to do (reload, out of ammo, no reserve) or, while the gun is looked over (Y), its rank and kills, and the
/// ammunition's name stands under the reserve (which guns share it). Hidden for fists, blades and the dead, as before.
/// </summary>
public sealed class MkAmmo : MkPiece
{
    private readonly MkShapes shapes;
    private readonly RawImage icon;
    private readonly TextMeshProUGUI ghost, count, name, reserve, status, kind;
    private readonly RectTransform room;
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    private WeaponRuntime shown;
    private int lastAmmo;
    private float clipShown, reserveShown, flick, flickShown, blink, rankUpLeft;
    private Color magsColour = Color.white, ammoColour = Color.white, blinkColour = Color.white;
    private string rankUpText;
    private WeaponData rankUpGun;

    /// <summary>The block the gun and the tiles over it take, for the right edge's passing pieces (HudRoom).</summary>
    public RectTransform Room => room;

    public MkAmmo(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Ammo", new Vector2(1f, 0f))
    {
        var a = Look.Gun;
        shapes = MkShapes.Make("Shapes", Frame);
        icon = Picture("Round", 0f, 0f, a.magHeight);
        ghost = MkText.Make("Ghost", Frame, Look, 1f, .5f);
        count = MkText.Make("Magazine", Frame, Look, 1f, .5f);
        name = MkText.Make("Name", Frame, Look, 0f, 0f);
        reserve = MkText.Make("Reserve", Frame, Look, .5f, .5f);
        status = MkText.Make("Status", Frame, Look, 1f, 0f);
        kind = MkText.Make("Ammunition", Frame, Look, .5f, 0f);
        room = new GameObject("Room", typeof(RectTransform)).GetComponent<RectTransform>();
        room.SetParent(Frame, false);
        room.anchorMin = room.anchorMax = new Vector2(0f, 1f);
        room.pivot = new Vector2(0f, 1f);
        GunKills.RankUpAnnounced += OnRankUp;
    }

    public override void Dispose() => GunKills.RankUpAnnounced -= OnRankUp;

    /// <summary>The boxes light once: what they show just changed by something taken (ammo, a gun, a resupply).</summary>
    public void Blink(Color colour)
    {
        blink = 1f;
        blinkColour = colour;
    }

    // A rank the gun reached, said as it is looked over: on the status line for a while, the boxes lit its orange.
    private void OnRankUp(WeaponData gun)
    {
        if (gun == null) return;
        rankUpText = Texts.Format("gunkills.rankup", GunKills.Title(gun));
        rankUpGun = gun;
        rankUpLeft = Look.Gun.rankUpSeconds;
        Blink(new Color(1f, .6f, .18f));
    }

    public override void Tick(float dt)
    {
        var a = Look.Gun;
        var c = Look.Colour;
        var player = Hud.Player;
        if (slots == null && player != null) slots = player.GetComponent<PlayerWeaponSlots>();
        if (weapon == null && player != null) weapon = player.GetComponent<WeaponController>();
        var runtime = slots != null && slots.isActiveAndEnabled ? slots.GetRuntime(slots.SelectedSlot) : null;
        bool visible = runtime != null && runtime.Data.Category != WeaponCategory.Melee && player != null && !player.IsDead;
        Group.alpha = visible ? 1f : 0f;
        if (!visible) { shown = null; flick = flickShown = 0f; return; }

        int magazine = runtime.Data.MagazineSize, rounds = runtime.Ammo, carried = runtime.ReserveAmmo;
        if (runtime != shown)
        {
            shown = runtime;
            lastAmmo = rounds;
            clipShown = rounds;
            reserveShown = carried;
            MkText.Label(kind, Look, AmmoKinds.NameOf(runtime.Data) ?? Texts.Get("hud.ammo.reserve"));
        }
        // A shot, not the magazine taken out in a reload, which empties the count too.
        else if (rounds < lastAmmo && !runtime.MagazineOut) flick = 1f;
        lastAmmo = rounds;
        flick = Mathf.Max(0f, flick - dt * a.flickFade);
        flickShown = MkText.Follow(flickShown, flick, a.flickFollow, dt);
        blink = Mathf.Max(0f, blink - dt * a.flickFade);
        clipShown = MkText.Follow(clipShown, rounds, a.follow, dt);
        reserveShown = MkText.Follow(reserveShown, carried, a.follow, dt);
        magsColour = MkText.Follow(magsColour, rounds <= magazine * a.low ? c.low : c.text, a.colourFollow, dt);
        ammoColour = MkText.Follow(ammoColour, carried <= magazine ? c.low : c.text, a.colourFollow, dt);

        float w = a.magWidth, h = a.magHeight, x = a.x, y = a.y, rw = a.reserveWidth;
        float rx = x + (w + rw) / a.reserveSpread, corner = Look.Health.corner;
        shapes.Clear();
        // The magazine box (BTL_Text), lit by a shot or a blink.
        shapes.Box(x - w * .5f, y - h * .5f, w, h, c.background, corner);
        float full = magazine > 0 ? Mathf.Clamp01(clipShown / magazine) : 1f;
        shapes.Box(x - w * .5f, y - h * .5f, w, h, new Color(magsColour.r, magsColour.g * full, magsColour.b * full, a.flickAlpha * flickShown), corner);
        Place(icon, x - w * .5f + h * .5f, y, h);
        Show(icon, Hud.AmmoIcon(runtime.Data), MkText.Fade(magsColour, a.iconAlpha));
        float textX = x + w / a.textAt;
        MkText.Size(ghost, Look, Look.Text.mags);
        MkText.Say(ghost, "000");
        MkText.At(ghost, textX, y);
        ghost.color = MkText.Fade(magsColour, a.ghost);
        MkText.Size(count, Look, Look.Text.mags);
        MkText.Say(count, Mathf.RoundToInt(clipShown).ToString());
        MkText.At(count, textX, y);
        count.color = magsColour;
        MkText.Label(name, Look, NameOf(runtime));
        MkText.At(name, x - w * a.nameX, y - w * a.nameY);
        name.color = magsColour;
        // The reserve's box (BL_Text).
        shapes.Box(rx - rw * .5f, y - h * .5f, rw, h, c.background, corner);
        MkText.Size(reserve, Look, Look.Text.ammo);
        MkText.Say(reserve, Mathf.RoundToInt(reserveShown).ToString());
        MkText.At(reserve, rx, y);
        reserve.color = MkText.Fade(ammoColour, a.reserveAlpha);
        if (blink > 0f)
        {
            Color lit = MkText.Fade(blinkColour, a.flickAlpha * blink);
            shapes.Box(x - w * .5f, y - h * .5f, w, h, lit, corner);
            shapes.Box(rx - rw * .5f, y - h * .5f, rw, h, lit, corner);
        }
        // The reload's line under the magazine box, the status line under that, the ammunition under the reserve.
        float under = y + h * .5f + a.reloadGap;
        if (runtime.IsReloading && runtime.ReloadDuration > 0f)
            shapes.Box(x - w * .5f, under, w * Mathf.Clamp01(1f - runtime.ReloadRemaining / runtime.ReloadDuration), a.reloadHeight,
                MkText.Fade(c.text, a.reloadAlpha));
        MkText.Label(status, Look, Status(runtime, rounds, magazine, carried, dt));
        MkText.At(status, x + w * .5f, y + h * .5f + a.statusGap);
        status.color = runtime.IsReloading ? c.text : rankUpLeft > 0f || Inspecting(runtime) ? new Color(1f, .6f, .18f) : c.tag;
        MkText.At(kind, rx, y + h * .5f + a.reloadGap);
        kind.color = c.tag;
        shapes.Show();
        // The room the block takes: from the name over the box (and the tiles over the reserve) down to the status line.
        float tilesTop = y - Look.Kit.tile * (Look.Kit.ultRise + .5f);
        float left = x - w * a.nameX, right = rx + rw * .5f, bottom = y + h * .5f + a.statusGap + Look.Text.label;
        room.anchoredPosition = new Vector2(left, -tilesTop);
        room.sizeDelta = new Vector2(right - left, bottom - tilesTop);
    }

    private bool Inspecting(WeaponRuntime runtime) => weapon != null && weapon.IsInspecting && !runtime.IsReloading;

    // The gun's name in capitals, spaced, and its fire mode after it (the draft's "M X 1 6 A 4   A U T O").
    private static string NameOf(WeaponRuntime runtime)
    {
        string mode = Texts.Get(runtime.FireMode == WeaponFireMode.Automatic ? "hud.mk.mode.auto"
            : runtime.FireMode == WeaponFireMode.Burst ? "hud.mk.mode.burst"
            : runtime.FireMode == WeaponFireMode.Fanning ? "hud.mk.mode.fan" : "hud.mk.mode.semi");
        return MkText.Spaced((runtime.Data.DisplayName ?? "").ToUpperInvariant()) + "   " + MkText.Spaced(mode);
    }

    // What the old status line said: reloading, what to do, or the gun's rank and kills while it is looked over.
    private string Status(WeaponRuntime runtime, int rounds, int magazine, int carried, float dt)
    {
        bool looked = Inspecting(runtime);
        if (looked) GunKills.AnnounceRankUp(runtime.Data);
        else rankUpLeft = 0f;
        rankUpLeft = Mathf.Max(0f, rankUpLeft - dt);
        if (looked)
            return rankUpLeft > 0f && rankUpGun == runtime.Data && !string.IsNullOrEmpty(rankUpText) ? rankUpText
                : Texts.Format("gunkills.inspect", GunKills.Title(runtime.Data), GunKills.Kills(runtime.Data), GunKills.Headshots(runtime.Data));
        if (runtime.IsReloading) return Texts.Format("hud.mk.ammo.reloading", Mathf.Max(0f, runtime.ReloadRemaining).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        bool empty = rounds == 0, low = rounds <= Mathf.CeilToInt(magazine * Look.Gun.low);
        string key = KeyBindings.Name("Combat/Reload");
        if (runtime.ChamberDue) return Texts.Format("hud.mk.ammo.reload", key);
        if (empty) return carried > 0 ? Texts.Format("hud.mk.ammo.reload", key) : Texts.Get("hud.mk.ammo.out");
        if (carried == 0) return Texts.Get("hud.mk.ammo.noreserve");
        return low ? Texts.Format("hud.mk.ammo.reload", key) : "";
    }
}
