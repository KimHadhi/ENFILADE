using System;
using System.Collections.Generic;
using HudAddons;
using TMPro;
using UnityEngine;
using static HudPageRows;
using W = SettingsWidgets;

/// <summary>
/// What the HUD settings offer for each part of the HUD (HudSettingsView shows one page at a time): its rows, where it can
/// be moved, whether it is on, and its defaults. Every field is the addon modules' own options
/// (PlayerHudOptions, HitFeedbackOptions, EnemyBarsOptions, GrenadesOptions, DeathScreenOptions) or a HUD setting of ours
/// (GameSettings); the words are Texts' settings.hudedit.*.
/// </summary>
public static class HudSettingsPages
{
    private static PlayerHudOptions P => GameSettings.Current.addonPlayerHud;
    private static HitFeedbackOptions H => GameSettings.Current.addonHitFeedback;
    private static EnemyBarsOptions E => GameSettings.Current.addonEnemyBars;
    private static GrenadesOptions G => GameSettings.Current.addonGrenades;
    private static DeathScreenOptions D => GameSettings.Current.addonDeathScreen;

    /// <summary>The groups along the top, and the parts of each.</summary>
    public static readonly (string id, HudPart[] parts)[] Groups =
    {
        ("whole", new[] { HudPart.Whole, HudPart.Fx, HudPart.Colour, HudPart.Motion }),
        ("me", new[] { HudPart.Health, HudPart.Armor, HudPart.Portrait, HudPart.ClassUi, HudPart.Stims, HudPart.Hazards }),
        ("gun", new[] { HudPart.Ammo, HudPart.Grenade, HudPart.Slots, HudPart.History }),
        ("fight", new[] { HudPart.Marker, HudPart.Score, HudPart.Skulls, HudPart.EnemyBar, HudPart.Numbers, HudPart.Incoming }),
        ("hurt", new[] { HudPart.Sense, HudPart.Flare, HudPart.Death }),
        ("mission", new[] { HudPart.Alert }),
    };

    /// <summary>The parts that can be pressed on the screen, the ones under others first.</summary>
    public static readonly HudPart[] Pressable =
    {
        HudPart.Score, HudPart.Skulls, HudPart.EnemyBar, HudPart.Slots, HudPart.Portrait, HudPart.ClassUi, HudPart.History, HudPart.Ammo,
        HudPart.Armor, HudPart.Health, HudPart.Hazards, HudPart.Stims, HudPart.Grenade, HudPart.Marker,
    };

    public static string Id(HudPart part) => part.ToString().ToLowerInvariant();

    public static string Name(HudPart part) => T("part." + Id(part));

    public static string About(HudPart part) => T("part." + Id(part) + ".about");

    public static int GroupOf(HudPart part) => Array.FindIndex(Groups, g => Array.IndexOf(g.parts, part) >= 0);

    /// <summary>The part stands at the right of the screen, so its page opens at the left.</summary>
    public static bool AtRight(HudPart part) => part == HudPart.Ammo || part == HudPart.Grenade || part == HudPart.Slots || part == HudPart.History
        || part == HudPart.Score;

    /// <summary>The part is shown at all (its tag in the list is faint while it isn't).</summary>
    public static bool IsOn(HudPart part) => part switch
    {
        HudPart.Whole => P.enabled,
        HudPart.Fx => P.sfx,
        HudPart.Health => P.enabled && P.health,
        HudPart.Armor => P.enabled && P.armor,
        HudPart.Portrait => P.enabled,
        HudPart.ClassUi => P.enabled && P.classUi,
        HudPart.Stims => P.enabled && P.stims,
        HudPart.Hazards => P.enabled && P.hazards,
        HudPart.Ammo => P.enabled && P.weapon,
        HudPart.Grenade => P.enabled && P.eqp,
        HudPart.History => P.enabled && P.history,
        HudPart.Marker => H.enable && (H.hitmark || H.killmark),
        HudPart.Score => H.enable && (H.score || H.achievements),
        HudPart.Skulls => H.skulls,
        HudPart.EnemyBar => E.enabled,
        HudPart.Numbers => GameSettings.Current.damageNumbers != DamageNumberMode.Off,
        HudPart.Incoming => G.indicator || G.fuseIndicator,
        HudPart.Sense => P.enabled && P.sense,
        HudPart.Flare => P.enabled && (P.flickArmor || P.flickDamage || P.flickHeal || P.shake),
        HudPart.Death => D.enabled,
        _ => true,
    };

    /// <summary>Where the part can be moved, or null.</summary>
    public static HudPlace PlaceOf(HudPart part)
    {
        var d = new PlayerHudOptions();
        switch (part)
        {
            case HudPart.Health: return At(() => new Vector2(P.healthX, P.healthY), v => { P.healthX = v.x; P.healthY = v.y; }, new Vector2(d.healthX, d.healthY));
            case HudPart.Armor: return At(() => new Vector2(P.armorX, P.armorY), v => { P.armorX = v.x; P.armorY = v.y; }, new Vector2(d.armorX, d.armorY));
            case HudPart.Stims: return At(() => new Vector2(P.stimsX, P.stimsY), v => { P.stimsX = v.x; P.stimsY = v.y; }, new Vector2(d.stimsX, d.stimsY));
            case HudPart.ClassUi: return At(() => new Vector2(P.classX, P.classY), v => { P.classX = v.x; P.classY = v.y; }, new Vector2(d.classX, d.classY));
            case HudPart.Hazards: return At(() => new Vector2(P.hazardsX, P.hazardsY), v => { P.hazardsX = v.x; P.hazardsY = v.y; }, new Vector2(d.hazardsX, d.hazardsY));
            case HudPart.Ammo: return At(() => new Vector2(P.weaponX, P.weaponY), v => { P.weaponX = v.x; P.weaponY = v.y; }, new Vector2(d.weaponX, d.weaponY));
            case HudPart.Grenade: return At(() => new Vector2(P.eqpX, P.eqpY), v => { P.eqpX = v.x; P.eqpY = v.y; }, new Vector2(d.eqpX, d.eqpY));
            case HudPart.History: return At(() => new Vector2(P.historyX, P.historyY), v => { P.historyX = v.x; P.historyY = v.y; }, new Vector2(d.historyX, d.historyY));
            case HudPart.Score: return At(() => new Vector2(H.horizontal, H.vertical), v => { H.horizontal = v.x; H.vertical = v.y; }, Vector2.zero);
            // The portrait stands on the health bar: the whole corner goes with it, each piece keeping its place in it.
            case HudPart.Portrait:
                return At(() => new Vector2(P.healthX, P.healthY), v =>
                {
                    var by = v - new Vector2(P.healthX, P.healthY);
                    P.healthX += by.x; P.healthY += by.y;
                    P.armorX += by.x; P.armorY += by.y;
                    P.stimsX += by.x; P.stimsY += by.y;
                }, new Vector2(d.healthX, d.healthY));
            // Only up and down, as a share of the screen's height.
            case HudPart.Skulls:
                return At(() => new Vector2(0f, (H.skullOffset - new HitFeedbackOptions().skullOffset) * 1080f),
                    v => H.skullOffset = Mathf.Clamp(new HitFeedbackOptions().skullOffset + v.y / 1080f, HitFeedbackOptions.MinSkullOffset, HitFeedbackOptions.MaxSkullOffset),
                    Vector2.zero);
            default: return null;
        }
    }

    private static HudPlace At(Func<Vector2> get, Action<Vector2> set, Vector2 home) => new HudPlace { Get = get, Set = set, Home = home };

    // ---- the pages ----

    public static void Build(HudPart part, HudPageRows r, HudSettingsView view)
    {
        switch (part)
        {
            case HudPart.Whole: Whole(r); break;
            case HudPart.Fx: Fx(r); break;
            case HudPart.Colour: Colour(r); break;
            case HudPart.Motion: Motion(r); break;
            case HudPart.Health:
                r.Toggle("show", P.health, on => P.health = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.healthColor, c => P.healthColor = c);
                break;
            case HudPart.Armor:
                r.Toggle("show", P.armor, on => P.armor = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.armorColor, c => P.armorColor = c);
                break;
            case HudPart.Portrait:
                r.Pictures("spacing", (int)P.spacing, i => P.spacing = (HudSpacing)i,
                    (T("spacing.default"), HudPictures.SpacingDefault), (T("spacing.wide"), HudPictures.SpacingWide));
                r.Place(PlaceOf(part));
                r.Note("portrait.note");
                break;
            case HudPart.ClassUi: ClassUi(r); break;
            case HudPart.Stims:
                r.Toggle("show", P.stims, on => P.stims = on);
                r.Place(PlaceOf(part));
                break;
            case HudPart.Hazards:
                r.Toggle("show", P.hazards, on => P.hazards = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.hazardsColor, c => P.hazardsColor = c);
                break;
            case HudPart.Ammo:
                r.Toggle("show", P.weapon, on => P.weapon = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.weaponColor, c => P.weaponColor = c);
                break;
            case HudPart.Grenade:
                r.Toggle("show", P.eqp, on => P.eqp = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.eqpColor, c => P.eqpColor = c);
                break;
            case HudPart.Slots:
                r.Pictures("slots.look", (int)GameSettings.Current.weaponSlots, i => GameSettings.Current.weaponSlots = (WeaponSlotLook)i,
                    (T("slots.plates"), HudPictures.SlotPlates), (T("slots.cards"), HudPictures.SlotCards));
                if (PlayerRoster.Local == null) r.Note("ingame.note");
                break;
            case HudPart.History:
                r.Toggle("show", P.history, on => P.history = on);
                r.Place(PlaceOf(part));
                r.Colour(() => P.historyColor, c => P.historyColor = c);
                break;
            case HudPart.Marker:
                r.Toggle("marker.enable", H.enable, on => H.enable = on);
                r.Toggle("marker.hit", H.hitmark, on => H.hitmark = on);
                r.Toggle("marker.kill", H.killmark, on => H.killmark = on);
                r.Share("size", 0f, 3f, H.markSize, v => H.markSize = v);
                r.Toggle("marker.hit_sound", H.hitSound, on => H.hitSound = on);
                r.Toggle("marker.kill_sound", H.killSound, on => H.killSound = on);
                break;
            case HudPart.Score: Score(r); break;
            case HudPart.Skulls:
                r.Toggle("show", H.skulls, on => H.skulls = on);
                r.Share("size", HitFeedbackOptions.MinSkullScale, HitFeedbackOptions.MaxSkullScale, H.skullScale, v => H.skullScale = v);
                r.Slider("skulls.height", HitFeedbackOptions.MinSkullOffset, HitFeedbackOptions.MaxSkullOffset, H.skullOffset, v => H.skullOffset = v, Percent, shown: 100f);
                r.Slider("skulls.most", 0f, HitFeedbackOptions.MaxSkulls, H.skullMax, v => H.skullMax = Mathf.RoundToInt(v),
                    v => v < .5f ? T("auto") : HudPageRows.Whole(v), whole: true);
                break;
            case HudPart.EnemyBar: EnemyBar(r); break;
            case HudPart.Numbers:
                r.Pictures("numbers.mode", (int)GameSettings.Current.damageNumbers, i => GameSettings.Current.damageNumbers = (DamageNumberMode)i,
                    (Texts.Get("settings.off"), HudPictures.NumbersOff), (T("numbers.total"), HudPictures.NumbersTotal), (T("numbers.each"), HudPictures.NumbersEach));
                break;
            case HudPart.Incoming:
                r.Toggle("incoming.indicator", G.indicator, on => G.indicator = on);
                r.Toggle("incoming.fuse", G.fuseIndicator, on => G.fuseIndicator = on);
                r.Toggle("incoming.claymore", G.listClaymores, on => G.listClaymores = on);
                break;
            case HudPart.Sense:
                r.Toggle("show", P.sense, on => P.sense = on);
                break;
            case HudPart.Flare:
                r.Header("flare.flash");
                r.Toggle("flare.damage", P.flickDamage, on => P.flickDamage = on);
                r.Toggle("flare.armor", P.flickArmor, on => P.flickArmor = on);
                r.Toggle("flare.heal", P.flickHeal, on => P.flickHeal = on);
                r.Slider("flare.fade", 0f, 3f, P.flickFade, v => P.flickFade = v, Seconds);
                r.Header("flare.shake");
                r.Toggle("show.shake", P.shake, on => P.shake = on);
                r.Slider("flare.strength", 0f, 5f, P.shakeIntensity, v => P.shakeIntensity = v);
                r.Slider("flare.fade", 0f, 3f, P.shakeFade, v => P.shakeFade = v, Seconds);
                break;
            case HudPart.Death:
                r.Toggle("show", D.enabled, on => D.enabled = on);
                r.Slider("death.volume", 0f, DeathScreenOptions.MaxVolume, D.volume, v => D.volume = v, Percent, shown: 100f);
                r.Slider("death.blur", 0f, DeathScreenOptions.MaxBlur, D.blur, v => D.blur = Mathf.Round(v), HudPageRows.Whole, whole: true);
                r.Slider("death.blood", 0f, 1f, D.blood, v => D.blood = v, Percent, shown: 100f);
                r.Slider("death.blood_delay", 0f, DeathScreenOptions.MaxBloodDelay, D.bloodDelay, v => D.bloodDelay = v, Seconds);
                if (PlayerRoster.Local != null) r.Button("preview", () => { global::DeathScreen.Command(""); view.Peek(5f); });
                else r.Note("ingame.note");
                break;
            case HudPart.Alert:
                r.Pictures("alert.look", (int)GameSettings.Current.objectiveAlert, i =>
                    {
                        GameSettings.Current.objectiveAlert = (ObjectiveAlertLook)i;
                        ShowAlert(view);
                    },
                    (T("alert.card"), HudPictures.AlertCard), (T("alert.ribbon"), HudPictures.AlertRibbon));
                if (SceneOne<MissionHUD>.Get() != null) r.Button("preview", () => ShowAlert(view));
                else r.Note("ingame.note");
                break;
        }
    }

    private static string Seconds(float v) => Texts.Format("settings.hudedit.seconds", One(v));

    private static void ShowAlert(HudSettingsView view)
    {
        var mission = SceneOne<MissionHUD>.Get();
        if (mission == null) return;
        mission.ShowAlert(T("sample.objective"));
        view.Peek(3.5f);
    }

    private static void Whole(HudPageRows r)
    {
        r.Toggle("show", P.enabled, on => P.enabled = on);
        r.Share("size", .5f, 2f, P.scale, v => P.scale = v);
        var faces = Faces();
        int face = Mathf.Max(0, faces.FindIndex(f => string.Equals(f, P.font, StringComparison.OrdinalIgnoreCase)));
        r.Rows.Stepper(T("font"), faces.ToArray(), face, i => P.font = faces[i]);
        r.Toggle("bold", P.weight > 1.1f, on => P.weight = on ? 2f : 1f);
        r.Toggle("sway", P.sway, on => { P.sway = on; HudStage.SwayUntil = Smooth.Now + 4f; });
        r.Slider("sway.amount", 1f, 5f, P.swayIntensity, v => { P.swayIntensity = v; HudStage.SwayUntil = Smooth.Now + 4f; }, One);
        r.Toggle("hide", P.hide, on => { P.hide = on; HudStage.HideShownAt = Smooth.Now; });
    }

    // The faces the HUD can be written in: each file once (a face goes by several names).
    private static List<string> Faces()
    {
        var faces = new List<string>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(HudFont.TableName))
        {
            string file = GameTables.Text(HudFont.TableName, id, "file", "");
            // The default by the name the options hold it under, whichever of its names comes first.
            string name = file == GameTables.Text(HudFont.TableName, PlayerHudOptions.VideoFont, "file", "") ? PlayerHudOptions.VideoFont : id;
            if (file.Length > 0 && !id.StartsWith("#") && files.Add(file)) faces.Add(name);
        }
        if (!faces.Exists(f => string.Equals(f, P.font, StringComparison.OrdinalIgnoreCase))) faces.Insert(0, P.font);
        return faces;
    }

    private static void Fx(HudPageRows r)
    {
        r.Toggle("fx.show", P.sfx, on => P.sfx = on);
        r.Toggle("fx.scanline", P.scanline, on => P.scanline = on);
        r.Toggle("fx.aberration", P.aberration, on => P.aberration = on);
        r.Toggle("fx.glitch", P.glitch, on => P.glitch = on);
    }

    private static void Colour(HudPageRows r)
    {
        var row = W.Rect("Own Colour", r.Rows.Parent);
        W.Size(row, height: 56f, flexibleHeight: 0f);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 0, 0);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        PlayerColourRow.Controls(row, false);
        r.Rows.Note(Texts.Get("profile.colour.hint"));
    }

    private static void Motion(HudPageRows r)
    {
        var s = GameSettings.Current;
        r.Share("motion.sway", 0f, 1f, s.hudSway, v => s.hudSway = v);
        r.Slider("motion.tilt", 0f, GameSettings.MaxHudTilt, s.hudTilt, v => s.hudTilt = Mathf.Round(v * 20f) / 20f, v => Percent(Mathf.Round(v * 20f) / 20f), shown: 100f);
        r.Slider("motion.angle", 0f, GameSettings.MaxHudAngle, s.hudAngle, v => s.hudAngle = Mathf.Round(v), v => Mathf.RoundToInt(v) + "°");
        r.Toggle("motion.lean", s.hudFollowsLean, on => s.hudFollowsLean = on);
        r.Toggle("motion.boards", s.hudBoards, on => s.hudBoards = on);
        if (PlayerRoster.Local == null) r.Note("ingame.note");
    }

    private static void ClassUi(HudPageRows r)
    {
        r.Toggle("show", P.classUi, on => P.classUi = on);
        r.Pictures("class.place", (int)P.classPlace, i => P.classPlace = (ClassUiPlace)i,
            (T("class.place.centre"), HudPictures.ClassCentre), (T("class.place.portrait"), HudPictures.ClassPortrait));
        r.Share("size", .5f, 2f, P.classScale, v => P.classScale = v);
        r.Place(PlaceOf(HudPart.ClassUi));
        r.Toggle("class.own_colour", P.classOwnColour, on => P.classOwnColour = on);
        r.Colour(() => P.classColor, c => P.classColor = c);
    }

    private static void Score(HudPageRows r)
    {
        r.Toggle("score.points", H.score, on => H.score = on);
        r.Toggle("score.names", H.achievements, on => H.achievements = on);
        r.Share("text_size", .5f, 3f, H.textSize, v => H.textSize = v);
        r.Place(PlaceOf(HudPart.Score));
        r.Header("score.rename");
        Word(r, "eliminate", () => H.nameEliminate, v => H.nameEliminate = v);
        Word(r, "headshot", () => H.nameHeadshot, v => H.nameHeadshot = v);
        Word(r, "jump", () => H.nameJump, v => H.nameJump = v);
        Word(r, "melee", () => H.nameMelee, v => H.nameMelee = v);
        Word(r, "burn", () => H.nameBurn, v => H.nameBurn = v);
        Word(r, "explosive", () => H.nameExplosive, v => H.nameExplosive = v);
        Word(r, "triple", () => H.nameTriple, v => H.nameTriple = v);
        Word(r, "quadra", () => H.nameQuadra, v => H.nameQuadra = v);
        Word(r, "penta", () => H.namePenta, v => H.namePenta = v);
    }

    // A kill's name as the popup shows it (the addon's own while none is typed); typing the addon's own, or nothing, goes
    // back to it.
    private static void Word(HudPageRows r, string id, Func<string> get, Action<string> set)
    {
        string textId = "hitfeedback.name." + id;
        string Shown() => string.IsNullOrEmpty(get()) ? Texts.InEnglish(textId) : get();
        var row = W.Row(r.Rows.Parent, T("score.rename." + id));
        var field = W.TextInput(row, Shown(), _ => { });
        TypingLook.On(field, row.childCount > 0 ? row.GetChild(0).GetComponent<TMP_Text>() : null);
        field.onEndEdit.AddListener(text =>
        {
            set(string.IsNullOrEmpty(text) || text == Texts.InEnglish(textId) ? "" : text);
            GameSettings.Apply();
            field.SetTextWithoutNotify(Shown());
        });
    }

    private static void EnemyBar(HudPageRows r)
    {
        r.Toggle("show", E.enabled, on => E.enabled = on);
        r.Toggle("enemybar.name", E.name, on => E.name = on);
        r.Toggle("enemybar.shadow", E.nameShadow, on => E.nameShadow = on);
        r.Toggle("enemybar.numbers", E.damageNumbers, on => E.damageNumbers = on);
        r.Toggle("enemybar.pattern", E.altBackground, on => E.altBackground = on);
        // The slider runs to the crosshair ray's own reach: past it no enemy can be aimed at.
        float reach = GameTables.Number(EnemyBarsLook.Table, EnemyBarsLook.AimRow, "traceLength", 32768f * EnemyBarsOptions.MetresPerUnit);
        r.Slider("enemybar.distance", 0f, Mathf.Max(reach, E.maxDistance), E.maxDistance, v => E.maxDistance = v, v => Texts.Format("settings.hudedit.metres", v.ToString("0")));
    }

    // ---- the defaults ----

    /// <summary>The part's own settings back to their defaults.</summary>
    public static void Reset(HudPart part)
    {
        var s = GameSettings.Current;
        var d = GameSettingsPreset.Copy();
        PlayerHudOptions p = P, dp = d.addonPlayerHud;
        HitFeedbackOptions h = H, dh = d.addonHitFeedback;
        switch (part)
        {
            case HudPart.Whole:
                p.enabled = dp.enabled; p.scale = dp.scale; p.font = dp.font; p.weight = dp.weight;
                p.sway = dp.sway; p.swayIntensity = dp.swayIntensity; p.hide = dp.hide;
                break;
            case HudPart.Fx: p.sfx = dp.sfx; p.scanline = dp.scanline; p.aberration = dp.aberration; p.glitch = dp.glitch; break;
            case HudPart.Colour: s.playerColour = d.playerColour; s.playerColourSlots = d.playerColourSlots; break;
            case HudPart.Motion:
                s.hudSway = d.hudSway; s.hudTilt = d.hudTilt; s.hudAngle = d.hudAngle; s.hudFollowsLean = d.hudFollowsLean; s.hudBoards = d.hudBoards;
                break;
            case HudPart.Health: p.health = dp.health; p.healthX = dp.healthX; p.healthY = dp.healthY; p.healthColor = dp.healthColor; break;
            case HudPart.Armor: p.armor = dp.armor; p.armorX = dp.armorX; p.armorY = dp.armorY; p.armorColor = dp.armorColor; break;
            case HudPart.Portrait:
                p.spacing = dp.spacing;
                p.healthX = dp.healthX; p.healthY = dp.healthY; p.armorX = dp.armorX; p.armorY = dp.armorY; p.stimsX = dp.stimsX; p.stimsY = dp.stimsY;
                break;
            case HudPart.ClassUi:
                p.classUi = dp.classUi; p.classPlace = dp.classPlace; p.classScale = dp.classScale; p.classX = dp.classX; p.classY = dp.classY;
                p.classOwnColour = dp.classOwnColour; p.classColor = dp.classColor;
                break;
            case HudPart.Stims: p.stims = dp.stims; p.stimsX = dp.stimsX; p.stimsY = dp.stimsY; break;
            case HudPart.Hazards: p.hazards = dp.hazards; p.hazardsX = dp.hazardsX; p.hazardsY = dp.hazardsY; p.hazardsColor = dp.hazardsColor; break;
            case HudPart.Ammo: p.weapon = dp.weapon; p.weaponX = dp.weaponX; p.weaponY = dp.weaponY; p.weaponColor = dp.weaponColor; break;
            case HudPart.Grenade: p.eqp = dp.eqp; p.eqpX = dp.eqpX; p.eqpY = dp.eqpY; p.eqpColor = dp.eqpColor; break;
            case HudPart.Slots: s.weaponSlots = d.weaponSlots; break;
            case HudPart.History: p.history = dp.history; p.historyX = dp.historyX; p.historyY = dp.historyY; p.historyColor = dp.historyColor; break;
            case HudPart.Marker:
                h.enable = dh.enable; h.hitmark = dh.hitmark; h.killmark = dh.killmark; h.markSize = dh.markSize; h.hitSound = dh.hitSound; h.killSound = dh.killSound;
                break;
            case HudPart.Score:
                h.score = dh.score; h.achievements = dh.achievements; h.textSize = dh.textSize; h.horizontal = dh.horizontal; h.vertical = dh.vertical;
                h.nameEliminate = dh.nameEliminate; h.nameHeadshot = dh.nameHeadshot; h.nameJump = dh.nameJump; h.nameMelee = dh.nameMelee;
                h.nameBurn = dh.nameBurn; h.nameExplosive = dh.nameExplosive; h.nameTriple = dh.nameTriple; h.nameQuadra = dh.nameQuadra; h.namePenta = dh.namePenta;
                break;
            case HudPart.Skulls: h.skulls = dh.skulls; h.skullScale = dh.skullScale; h.skullOffset = dh.skullOffset; h.skullMax = dh.skullMax; break;
            case HudPart.EnemyBar:
                E.enabled = d.addonEnemyBars.enabled; E.name = d.addonEnemyBars.name; E.nameShadow = d.addonEnemyBars.nameShadow;
                E.damageNumbers = d.addonEnemyBars.damageNumbers; E.altBackground = d.addonEnemyBars.altBackground; E.maxDistance = d.addonEnemyBars.maxDistance;
                break;
            case HudPart.Numbers: s.damageNumbers = d.damageNumbers; break;
            case HudPart.Incoming: s.addonGrenades = d.addonGrenades; break;
            case HudPart.Sense: p.sense = dp.sense; break;
            case HudPart.Flare:
                p.flickDamage = dp.flickDamage; p.flickArmor = dp.flickArmor; p.flickHeal = dp.flickHeal; p.flickFade = dp.flickFade;
                p.shake = dp.shake; p.shakeIntensity = dp.shakeIntensity; p.shakeFade = dp.shakeFade;
                break;
            case HudPart.Death: s.addonDeathScreen = d.addonDeathScreen; break;
            case HudPart.Alert: s.objectiveAlert = d.objectiveAlert; break;
        }
        GameSettings.Apply();
    }
}
