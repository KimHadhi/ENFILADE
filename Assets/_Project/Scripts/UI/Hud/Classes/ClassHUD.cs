using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The class's own resource (stage 7, ClassKit). What it says is worked out here every frame into a ClassReadout
/// (ClassReadout.Local): for an Assault its adrenaline as ticks that fill pink, "[휠] CHARGE" once they are full, and with
/// 데인저 클로즈 its barrage's wait or aim; for a Point its two guns, each with a line that runs out while the gun put
/// away reloads itself and a flash when it has; for a Ghost its Head Hunter stacks, the newest flashing white as it comes
/// and all of them as they are lost, and "DEADEYE" while the gun in hand holds its first round since loading; for a
/// Knight "[휠] 원호" while its cover is ready and the wait in its place while not; for a Schießhund its Desperado's
/// rounds as teal ticks, the newest flashing as a bounty loads it, and "DESPERADO" while it is in hand with a round in
/// it; for a Détente its syringe as a tick a dose, red, "[휠] 주사" while a dose is in it, and how many Resurrections are
/// left; for a Support its bag. Classes whose abilities come later show their name only.
/// <para>Who draws it: HUD Mk II's class tile and the words beside it (MkGear) while HUD Mk II stands in (MkHud.On, the
/// user's pick of the HUD drafts, 2026-11-22); otherwise the old dark slanted plate at the foot of the screen with its
/// leaning pips, and under it, in a flow, what the player has standing in the world (DeployStrip), then every timed
/// effect on them, each in a slot of its own with its own time (EffectStack over TimedEffects). The plate used to carry
/// ONE draining bar there, and this class chose what it showed by a chain of "this if it runs, else that" (the user,
/// 2026-11-18: a gun swapped during the Gun Kata started Gunslinger and the kata's time was gone). Nothing chooses any
/// more: the features say what runs and every one of them is shown.</para>
/// Local player only (PlayerMovement). HUD look: black, white and pink.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassHUD : MonoBehaviour
{
    private const int MostPips = ClassReadout.MostTicks;
    private const float Width = 300f, Height = 44f, Above = 112f, Slant = 12f, PipWidth = 14f, PipHeight = 14f, PipGap = 5f;

    private PlayerMovement movement;
    private ClassKit kit;
    private readonly ClassReadout read = new ClassReadout();
    private RectTransform plate, pipRow, slotRow;
    private readonly Image[] pips = new Image[MostPips];
    private readonly Image[] slotLines = new Image[2], slotBacks = new Image[2];
    private TextMeshProUGUI title, note;
    private RawImage icon;
    private RectTransform keyRect;
    private KeyCapGraphic keyCap;
    private TextMeshProUGUI keyLabel, keyWord, deadeye;
    private InputAction ability;
    private string shownClass, paintedClass;
    private int shownPips = -1, paintedPips = -1;
    // Where the note stands on the old plate (past the pips, past the Point's guns, or at its place).
    private float noteX = 128f;
    // What this player has standing in the world, under the plate (how many of each kind, how many may: DeployStrip),
    // and under that every timed effect on them, a slot each (EffectStack). The old plate's only.
    private DeployStrip deploy;
    private EffectStack effects;

    // The old plate draws the readout; HUD Mk II draws it instead while it stands in (MkGear).
    private static bool Plate => !MkHud.On;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void LateUpdate()
    {
        if (movement == null || !movement.IsLocal) return;
        if (kit == null) kit = GetComponent<ClassKit>();
        ClassReadout.Local = read;
        bool drawn = Plate;
        if (drawn) Build();
        string id = PlayerClass.CurrentId;
        if (id != shownClass) Show(id);
        bool dead = movement.IsDead;
        read.Hidden = dead;
        if (drawn)
        {
            plate.gameObject.SetActive(!dead);
            // The count of what this player has standing, whatever their class and whether or not they are placing now
            // (the user, 2026-11-18: "현재 설치 개수/최대 제한 현황 표시"); what comes next in the flow stands under it.
            deploy.Show(movement, Placing(), !dead);
            // Every timed effect on this player, a slot each, under the count line (or right under the plate when that
            // shows nothing): the features say what runs (TimedEffects) and none of it is chosen between here.
            effects.Show(Above - deploy.Reach, !dead);
        }
        if (dead || kit == null) return;
        read.Key = KeyName();
        if (PlayerClass.Is(PlayerClass.Assault)) Adrenaline();
        else if (PlayerClass.Is(PlayerClass.Point)) Guns();
        else if (PlayerClass.Is(PlayerClass.Ghost)) Hunter();
        else if (PlayerClass.Is(PlayerClass.Knight)) Taunt();
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) Hound();
        else if (PlayerClass.Is(PlayerClass.Detente)) Detente();
        else if (PlayerClass.Is(PlayerClass.Support)) Support();
        if (drawn) Paint();
    }

    // The kind of thing being placed now, as Tables/Deployables names it ("" for none): a Support's structure while
    // its key is held, or a claymore held out. HUD Mk II's deploy lines ask it too.
    internal static string Placing()
    {
        var support = SupportKit.Local;
        if (support != null && support.Planning) return Barricade.KindName(support.PlanKind);
        var grenades = PlayerGrenades.Local;
        return grenades != null && grenades.Placing ? ClaymoreMine.KindName : "";
    }

    private void Show(string id)
    {
        shownClass = id;
        var info = PlayerClass.Find(id);
        read.ClassId = id;
        read.Title = info != null ? info.Title : id;
        read.Icon = KitButtons.Icon(MainMenuClasses.Icon(id));
        bool hound = PlayerClass.Is(PlayerClass.Schiesshund), knight = PlayerClass.Is(PlayerClass.Knight);
        bool detente = PlayerClass.Is(PlayerClass.Detente), support = PlayerClass.Is(PlayerClass.Support), point = PlayerClass.Is(PlayerClass.Point);
        read.Mark = Texts.Get(hound ? "class.hud.desperado" : "class.hud.deadeye");
        read.MarkShown = false;
        read.KeyReady = false;
        read.Guns = false;
        Word(Texts.Get(knight ? "class.hud.cover" : detente ? "class.hud.inject" : support ? "class.hud.barricade" : hound ? "class.hud.duel"
            : point ? "class.hud.kata" : "class.hud.charge"), KitStyle.Accent);
        read.Note = info != null && !info.Ready ? Texts.Get("class.hud.later") : "";
        // A Détente moves the note past its pips; every other class has it back in its place.
        noteX = 128f;
        shownPips = -1;
        read.Count = 0;
        if (PlayerClass.Is(PlayerClass.Assault)) Pips(kit != null ? kit.StackMax : 8);
    }

    // As many ticks as the resource holds (an Assault's adrenaline; a Ghost's Head Hunter, whose most the cards may raise).
    private void Pips(int most)
    {
        int count = Mathf.Clamp(most, 1, MostPips);
        if (count == shownPips) return;
        shownPips = count;
        read.Count = count;
    }

    // Tick i: how full and in what colour.
    private void Tick(int i, float fill, Color colour)
    {
        read.Fill[i] = fill;
        read.Colour[i] = colour;
    }

    // Adrenaline: a tick a stack (as many as its most, which the 깊은 숨 card raises), all beating with the key's prompt
    // once full. With 데인저 클로즈 (ClassCards) its barrage goes past the ticks: that the key held aims it, or its wait;
    // and while the key is held to aim, "[휠] 놓으면 포격" ("포격 불가" in red where it can't fall) and how to call it off.
    private void Adrenaline()
    {
        Pips(kit.StackMax);
        int count = shownPips, stacks = kit.Stacks;
        bool ready = kit.ChargeReady;
        float beat = ready ? .75f + .25f * Mathf.Sin(Time.unscaledTime * 9f) : 1f;
        for (int i = 0; i < count; i++)
            Tick(i, i < stacks ? 1f : 0f, i < stacks ? KitStyle.With(KitStyle.Accent, beat) : KitStyle.With(KitStyle.Ice, .14f));
        var cards = ClassCards.Local;
        string side = "";
        if (cards != null && cards.DangerAiming)
        {
            bool fits = cards.DangerFits;
            read.KeyReady = true;
            Word(Texts.Get(fits ? "class.hud.danger.aim" : "class.hud.danger.no"), fits ? KitStyle.Accent : KitStyle.Red);
            side = Texts.Get(fits ? "class.hud.danger.cancel" : "class.assault.danger.ground");
        }
        else
        {
            read.KeyReady = ready;
            Word(Texts.Get("class.hud.charge"), KitStyle.Accent);
            if (cards != null && cards.HasDanger)
                side = cards.DangerReady ? Texts.Format("class.hud.danger.hold", KeyName())
                    : Texts.Format("class.hud.danger_wait", Mathf.CeilToInt(cards.DangerWait));
        }
        read.Note = side;
        noteX = 128f + count * (PipWidth + PipGap) + 6f;
    }

    // A Ghost's Head Hunter: a tick a stack. The one that came flashes white, the one lost too, and all of them when a
    // hurt takes every stack. "DEADEYE" while the gun in hand holds its first round since loading.
    private void Hunter()
    {
        Pips(kit.HunterMax);
        int count = shownPips, stacks = Mathf.Min(kit.Hunter, count), change = kit.HunterChange;
        float flash = Mathf.Clamp01(1f - kit.SinceHunterChange / .3f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < stacks ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .14f);
            bool changed = change == -2 || (change == 1 && i == stacks - 1) || (change == -1 && i == stacks);
            Tick(i, i < stacks ? 1f : 0f, flash > 0f && changed ? Color.Lerp(color, KitStyle.Ice, flash) : color);
        }
        bool ready = kit.DeadeyeReady;
        read.MarkShown = ready;
        if (ready) read.MarkColour = KitStyle.With(KitStyle.Accent, .78f + .22f * Mathf.Sin(Time.unscaledTime * 6f));
    }

    // A Schießhund's Desperado: a teal tick a round in its cylinder, the newest flashing white as a bounty loads it, and
    // "DESPERADO" while it is in hand with a round in it (its next shot spends one).
    private void Hound()
    {
        Pips(kit.RoundMax);
        int count = shownPips, rounds = Mathf.Min(kit.Rounds, count);
        float flash = Mathf.Clamp01(1f - kit.SinceBounty / .4f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < rounds ? PingMarks.BountyColor : KitStyle.With(KitStyle.Ice, .14f);
            Tick(i, i < rounds ? 1f : 0f, flash > 0f && i == rounds - 1 ? Color.Lerp(color, KitStyle.Ice, flash) : color);
        }
        bool loaded = kit.DesperadoLoaded;
        read.MarkShown = loaded;
        if (loaded) read.MarkColour = KitStyle.With(PingMarks.BountyColor, .78f + .22f * Mathf.Sin(Time.unscaledTime * 6f));
        // Its duel on the class key: the prompt while it may be called, the seconds left to wait past the ticks while not.
        bool ready = kit.DuelReady;
        read.KeyReady = ready;
        read.Note = ready ? "" : Texts.Format("class.hud.duel_wait", Mathf.CeilToInt(kit.DuelWait));
        noteX = 128f + count * (PipWidth + PipGap) + 6f;
    }

    // A Détente: a red tick a dose left in the syringe (the newest flashing white as one goes in), "[휠] 주사" while there
    // is one, and how many Resurrections are left. A treatment by hand or a Resurrection under way and Golden Hour are
    // timed effects like any other (DetenteKit says them).
    private void Detente()
    {
        var detente = DetenteKit.Local;
        if (detente == null) return;
        Pips(detente.MostDoses);
        int count = shownPips, doses = Mathf.Min(detente.Doses, count);
        float flash = Mathf.Clamp01(1f - detente.SinceInject / .35f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < doses ? KitStyle.Red : KitStyle.With(KitStyle.Ice, .14f);
            Tick(i, i < doses ? 1f : 0f, flash > 0f && i == doses ? Color.Lerp(color, KitStyle.Ice, flash) : color);
        }
        read.KeyReady = !detente.Empty;
        // By hand (the syringe empty): the offer to make, the answer awaited, else the syringe's state.
        var friend = detente.Candidate;
        read.Note = detente.Treating != null ? "" : detente.Offering != null ? Texts.Format("class.hud.treat.waiting", PlayerTally.NameOf(detente.Offering))
            : friend != null ? Texts.Format(DetenteKit.Asked(friend) ? "class.hud.treat.asked" : "class.hud.treat.offer", PlayerTally.NameOf(friend))
            : detente.Empty ? Texts.Get("class.hud.empty") : detente.RaisesLeft > 0 ? Texts.Format("class.hud.raises", detente.RaisesLeft) : "";
        noteX = 128f + count * (PipWidth + PipGap) + 6f;
    }

    // A Support's bag: a tick a resupply's worth (white when full, the next one filling), "[휠] 바리케이드" while one can go
    // up, and beside it "[F] 보급 · name" while a friend is in reach, else the bag in numbers (the user, 2026-11-12:
    // "배낭이 얼마나 찼는지 보이나? 나는 바리케이드 설치 가능 횟수만 보여서": the pips alone didn't read as the bag). While
    // the class key is held to place one (SupportKit's preview): "[휠] 놓으면 설치" and "[우클릭] 취소" where it may go
    // up, "[휠] 설치 불가" in red and why where it can't; and with the throw card, "[F] 던지기 · name" while a friend is
    // under the crosshair in reach, F throwing them a supply pack then instead of choosing the next kind.
    private void Support()
    {
        var support = SupportKit.Local;
        if (support == null) return;
        Pips(Mathf.FloorToInt(support.SupplyMax / support.GiveCost + .001f));
        int count = shownPips;
        float bags = support.Supply / support.GiveCost, flash = Mathf.Clamp01(1f - Mathf.Min(support.SinceGive, support.SinceBuild) / .35f);
        for (int i = 0; i < count; i++)
        {
            float fill = Mathf.Clamp01(bags - i);
            Color color = fill >= 1f ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .14f + .3f * fill);
            Tick(i, fill, flash > 0f && i == Mathf.FloorToInt(bags) ? Color.Lerp(color, KitStyle.Accent, flash) : color);
        }
        string side;
        if (support.Planning)
        {
            bool fits = support.PlanFit == Barricade.Fit.Ok;
            read.KeyReady = true;
            // What it puts up and for how much; F goes on to the next kind held.
            string what = Texts.Get("class.hud.build." + support.PlanKind.ToString().ToLowerInvariant());
            // With as many standing as may, putting it up takes the oldest down: the word says so ("놓으면 교체"), as the
            // amber line of what stands and the amber ghost over that one in the world do.
            bool swaps = fits && support.NextDown != null;
            Word(Texts.Format(!fits ? "class.hud.place.kind.no" : swaps ? "class.hud.place.kind.swap" : "class.hud.place.kind", what,
                Mathf.RoundToInt(support.BuildCost)), fits ? KitStyle.Accent : KitStyle.Red);
            // With the throw card and a friend under the crosshair, F throws them a pack instead (SupportKit.Tossable).
            var catcher = support.Tossable;
            side = (fits ? Texts.Get("class.hud.place.cancel") : support.PlanWhy) + " · "
                + (catcher != null ? Texts.Format("class.hud.throw", PlayerTally.NameOf(catcher), Mathf.RoundToInt(support.ThrowCost))
                    : Texts.Get("class.hud.build.switch"));
        }
        else
        {
            read.KeyReady = support.CanBuild;
            Word(Texts.Get("class.hud.barricade"), KitStyle.Accent);
            var friend = support.Offered;
            // A barricade under the crosshair has its own line at the foot of the screen, where the eyes are (SupportKit).
            // How many of each structure stand is the deploy line's (DeployStrip, MkGear): every kind at once.
            side = friend != null ? Texts.Format("class.hud.resupply", friend.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : friend.name)
                : Texts.Format("class.hud.bag", Mathf.FloorToInt(support.Supply), Mathf.RoundToInt(support.SupplyMax));
        }
        read.Note = side;
        noteX = 128f + count * (PipWidth + PipGap) + 6f;
    }

    // A Knight's cover (KnightCover): the key's prompt while it is ready; while held, the friend under the crosshair (or
    // "look at a friend"); the friend it watches over while a cover is open; else the seconds left to wait.
    private void Taunt()
    {
        var cover = KnightCover.Local;
        bool ready = cover != null && cover.Ready;
        read.KeyReady = ready;
        read.Note = cover == null ? ""
            : cover.Planning ? (cover.Aimed != null ? Texts.Format("class.hud.cover.aim", PlayerTally.NameOf(cover.Aimed)) : Texts.Get("class.hud.cover.look"))
            : cover.Watched != null ? Texts.Format("class.hud.cover.watch", PlayerTally.NameOf(cover.Watched))
            : ready ? "" : Texts.Format("class.hud.cover_wait", Mathf.CeilToInt(cover.Wait));
    }

    // The word for the class key, and its colour (a Support's turns red where its barricade can't go).
    private void Word(string text, Color color)
    {
        read.Word = text;
        read.WordColour = color;
    }

    // A Point's two guns: the line under each runs out while it reloads put away, and flashes when it has.
    private void Guns()
    {
        read.Guns = true;
        read.Count = 2;
        for (int slot = 0; slot < 2; slot++)
        {
            float share = kit.AsideShare(slot), since = kit.SinceRefill(slot);
            float flash = Mathf.Clamp01(1f - since / .5f);
            Tick(slot, share, Color.Lerp(KitStyle.With(KitStyle.Ice, .12f), KitStyle.With(KitStyle.Accent, .85f), flash));
        }
        // The Gun Kata on the class key: the prompt while it may be used, the seconds left to wait past the guns while not.
        bool ready = kit.KataReady;
        read.KeyReady = ready;
        read.Note = ready ? "" : Texts.Format("class.hud.kata_wait", Mathf.CeilToInt(kit.KataWait));
        noteX = 128f + 160f + 6f;
    }

    private string KeyName()
    {
        if (ability == null && InputSystem.actions != null) ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        return KeyBindings.Name(ability);
    }

    // ---- The old plate: the readout drawn at the foot of the screen ----

    private void Paint()
    {
        if (paintedClass != shownClass)
        {
            paintedClass = shownClass;
            title.text = read.Title;
            icon.texture = read.Icon;
            icon.enabled = icon.texture != null;
            bool assault = PlayerClass.Is(PlayerClass.Assault), ghost = PlayerClass.Is(PlayerClass.Ghost), hound = PlayerClass.Is(PlayerClass.Schiesshund);
            bool detente = PlayerClass.Is(PlayerClass.Detente), support = PlayerClass.Is(PlayerClass.Support);
            pipRow.gameObject.SetActive(assault || ghost || hound || detente || support);
            slotRow.gameObject.SetActive(read.Guns || PlayerClass.Is(PlayerClass.Point));
            deadeye.text = read.Mark;
            paintedPips = -1;
        }
        if (!read.Guns && read.Count != paintedPips)
        {
            paintedPips = read.Count;
            for (int i = 0; i < MostPips; i++) pips[i].gameObject.SetActive(i < read.Count);
        }
        if (read.Guns)
            for (int slot = 0; slot < 2; slot++)
            {
                slotLines[slot].rectTransform.anchorMax = new Vector2(read.Fill[slot], 1f);
                slotLines[slot].color = KitStyle.Accent;
                slotBacks[slot].color = read.Colour[slot];
            }
        else
            for (int i = 0; i < read.Count && i < MostPips; i++) pips[i].color = read.Colour[i];
        KeyPrompt(read.KeyReady);
        if (keyWord.text != read.Word) keyWord.text = read.Word;
        if (keyWord.color != read.WordColour) keyWord.color = read.WordColour;
        if (note.text != read.Note) note.text = read.Note;
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(noteX, 0f);
        deadeye.gameObject.SetActive(read.MarkShown);
        if (read.MarkShown) deadeye.color = read.MarkColour;
    }

    // "[휠] CHARGE" or "[휠] 도발" over the plate's right end, beating, while the class key has something to do.
    private void KeyPrompt(bool ready)
    {
        keyRect.gameObject.SetActive(ready);
        if (!ready) return;
        string key = read.Key;
        if (keyLabel.text != key)
        {
            keyLabel.text = key;
            float width = Mathf.Max(30f, keyLabel.GetPreferredValues(key).x + 16f);
            keyRect.sizeDelta = new Vector2(width, 24f);
            keyCap.SetVerticesDirty();
        }
        keyRect.localScale = Vector3.one * (1f + .06f * Mathf.Sin(Time.unscaledTime * 9f));
    }

    private void Build()
    {
        if (plate != null) return;
        var host = new GameObject("Class HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.hideFlags = HideFlags.DontSave;
        host.transform.SetParent(transform, false);
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 76;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        plate = new GameObject("Class", typeof(RectTransform)).GetComponent<RectTransform>();
        plate.SetParent(host.transform, false);
        plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(.5f, 0f);
        plate.anchoredPosition = new Vector2(0f, Above);
        plate.sizeDelta = new Vector2(Width, Height);
        plate.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
        var back = KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.With(KitStyle.Ink, .78f));
        KitSkew.On(back, Slant);
        var edge = KitStyle.Bar("Edge", plate, Vector2.zero, new Vector2(4f, Height), KitStyle.Accent);
        KitSkew.On(edge, Slant);

        icon = KitButtons.IconImage(plate, "", 28f, KitStyle.Ice);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(28f, 0f);
        title = KitStyle.Label(KitStyle.TopLeft("Title", plate, new Vector2(46f, 0f), new Vector2(80f, Height)), "", 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        title.characterSpacing = 2f;
        note = KitStyle.Label(KitStyle.TopLeft("Note", plate, new Vector2(128f, 0f), new Vector2(160f, Height)), "", 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;

        pipRow = KitStyle.TopLeft("Adrenaline", plate, new Vector2(128f, (Height - PipHeight) * .5f), new Vector2(160f, PipHeight));
        for (int i = 0; i < MostPips; i++)
        {
            var pip = KitStyle.Fill(KitStyle.TopLeft("Pip", pipRow, new Vector2(i * (PipWidth + PipGap), 0f), new Vector2(PipWidth, PipHeight)),
                KitStyle.With(KitStyle.Ice, .14f));
            KitSkew.On(pip, 5f);
            pips[i] = pip;
        }

        slotRow = KitStyle.TopLeft("Guns", plate, new Vector2(128f, 7f), new Vector2(160f, Height - 14f));
        for (int slot = 0; slot < 2; slot++)
        {
            var box = KitStyle.TopLeft("Gun " + (slot + 1), slotRow, new Vector2(slot * 62f, 0f), new Vector2(52f, Height - 14f));
            slotBacks[slot] = KitStyle.Fill(box, KitStyle.With(KitStyle.Ice, .12f));
            KitSkew.On(slotBacks[slot], 6f);
            KitStyle.Label(box, (slot + 1).ToString(), 15f, TextAlignmentOptions.Center, KitStyle.Ice);
            var line = KitStyle.Stretch("Reload", box);
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(0f, 0f);
            line.pivot = new Vector2(0f, 0f);
            line.sizeDelta = new Vector2(0f, 3f);
            line.anchoredPosition = Vector2.zero;
            slotLines[slot] = line.gameObject.AddComponent<Image>();
            slotLines[slot].raycastTarget = false;
        }

        // "[휠] CHARGE" over the right end once the adrenaline is full.
        keyRect = new GameObject("Charge key", typeof(RectTransform), typeof(CanvasRenderer), typeof(KeyCapGraphic)).GetComponent<RectTransform>();
        keyRect.SetParent(plate, false);
        keyRect.anchorMin = keyRect.anchorMax = keyRect.pivot = new Vector2(1f, .5f);
        keyRect.anchoredPosition = new Vector2(-84f, Height * .5f + 16f);
        keyCap = keyRect.GetComponent<KeyCapGraphic>();
        keyCap.raycastTarget = false;
        keyCap.color = KitStyle.Accent;
        keyLabel = KitStyle.Label(keyRect, "", 13f, TextAlignmentOptions.Center, KitStyle.Ice);
        var word = KitStyle.TopLeft("Charge word", keyRect, new Vector2(0f, 0f), new Vector2(80f, 24f));
        word.anchorMin = word.anchorMax = new Vector2(1f, .5f);
        word.pivot = new Vector2(0f, .5f);
        word.anchoredPosition = new Vector2(8f, 0f);
        keyWord = KitStyle.Label(word, Texts.Get("class.hud.charge"), 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        keyRect.gameObject.SetActive(false);

        // A Ghost's "DEADEYE", over the plate's right end where a key's prompt would be.
        var mark = KitStyle.TopLeft("Deadeye", plate, Vector2.zero, new Vector2(120f, 24f));
        mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(1f, .5f);
        mark.anchoredPosition = new Vector2(-14f, Height * .5f + 16f);
        deadeye = KitStyle.Label(mark, Texts.Get("class.hud.deadeye"), 15f, TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        deadeye.characterSpacing = 3f;
        deadeye.gameObject.SetActive(false);

        // What this player has standing in the world: a piece of its own on this canvas, right under the plate.
        deploy = new DeployStrip(host.transform, Width, Above);
        // Every timed effect on them, a slot each: another piece of its own, under that (where the one bar used to hang).
        effects = new EffectStack(host.transform, Width);
    }

    private void OnDestroy()
    {
        if (ClassReadout.Local == read) ClassReadout.Local = null;
        deploy?.Dispose();
        effects?.Dispose();
        if (plate == null) return;
        var host = plate.parent != null ? plate.parent.gameObject : plate.gameObject;
        if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
    }
}
