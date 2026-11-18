using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The class's own resource, at the foot of the screen (stage 7, ClassKit): a dark slanted plate with the class's name,
/// and for an Assault its adrenaline as a row of leaning pips that fill pink, "[휠] CHARGE" beating on it once they are
/// full, and with 데인저 클로즈 its barrage's wait or aim past the
/// pips; for a Point its two guns' numbers, each with a line
/// that runs out while the gun put away reloads itself and a flash when it has; for a Ghost its Head Hunter stacks as
/// the pips, the newest flashing white as it comes and all of them as they are lost, and "DEADEYE" over the plate while
/// the gun in hand holds its first round since loading; for a Knight "[휠] 원호" while its cover is ready and the wait in
/// its place while not; for a Schießhund its Desperado's rounds as teal pips, the newest flashing as a bounty loads it,
/// and "DESPERADO" over the plate while it is in hand with a round in it; for a Détente its syringe as a pip a dose,
/// red, "[휠] 주사" while a dose is in it, and how many Resurrections are left beside them. Classes whose abilities
/// come later show their name only.
/// Under the plate, in a flow: what the player has standing in the world (DeployStrip), then every timed effect on
/// them, each in a slot of its own with its own time (EffectStack over TimedEffects): a CHARGE, a Gun Kata, Gunslinger,
/// Return Back, Golden Hour, a friend's adrenaline shot or cover, Momentum and the rest. The plate used to carry ONE
/// draining bar there, and this class chose what it showed by a chain of "this if it runs, else that" (a card's state,
/// then a friend's shot, then the taunt, the kata, the duel, the cheer, Return Back, the CHARGE): whatever came later in
/// the chain was not on the screen while something earlier ran (the user, 2026-11-18: a gun swapped during the Gun
/// Kata started Gunslinger and the kata's time was gone). Nothing chooses any more: the features say what runs and
/// every one of them is shown.
/// Local player only (PlayerMovement). HUD look: black, white and pink.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassHUD : MonoBehaviour
{
    private const int MostPips = 12;
    private const float Width = 300f, Height = 44f, Above = 112f, Slant = 12f, PipWidth = 14f, PipHeight = 14f, PipGap = 5f;

    private PlayerMovement movement;
    private ClassKit kit;
    private RectTransform plate, pipRow, slotRow;
    private readonly Image[] pips = new Image[MostPips];
    private readonly Image[] slotLines = new Image[2], slotBacks = new Image[2];
    private TextMeshProUGUI title, note;
    private RawImage icon;
    private RectTransform keyRect;
    private KeyCapGraphic keyCap;
    private TextMeshProUGUI keyLabel, keyWord, deadeye;
    private InputAction ability;
    private string shownClass;
    private int shownPips;
    // What this player has standing in the world, under the plate (how many of each kind, how many may: DeployStrip),
    // and under that every timed effect on them, a slot each (EffectStack).
    private DeployStrip deploy;
    private EffectStack effects;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void LateUpdate()
    {
        if (movement == null || !movement.IsLocal) return;
        if (kit == null) kit = GetComponent<ClassKit>();
        Build();
        string id = PlayerClass.CurrentId;
        if (id != shownClass) Show(id);
        bool dead = movement.IsDead;
        plate.gameObject.SetActive(!dead);
        // The count of what this player has standing, whatever their class and whether or not they are placing now (the
        // user, 2026-11-18: "현재 설치 개수/최대 제한 현황 표시"); what hangs under the plate (the bar) stands under it.
        deploy.Show(movement, Placing(), !dead);
        // Every timed effect on this player, a slot each, under the count line (or right under the plate when that
        // shows nothing): the features say what runs (TimedEffects) and none of it is chosen between here.
        effects.Show(Above - deploy.Reach, !dead);
        if (dead || kit == null) return;
        if (PlayerClass.Is(PlayerClass.Assault)) Adrenaline();
        else if (PlayerClass.Is(PlayerClass.Point)) Guns();
        else if (PlayerClass.Is(PlayerClass.Ghost)) Hunter();
        else if (PlayerClass.Is(PlayerClass.Knight)) Taunt();
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) Hound();
        else if (PlayerClass.Is(PlayerClass.Detente)) Detente();
        else if (PlayerClass.Is(PlayerClass.Support)) Support();
    }

    // The kind of thing being placed now, as Tables/Deployables names it ("" for none): a Support's structure while
    // its key is held, or a claymore held out.
    private static string Placing()
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
        title.text = info != null ? info.Title : id;
        icon.texture = KitButtons.Icon(MainMenuClasses.Icon(id));
        icon.enabled = icon.texture != null;
        bool assault = PlayerClass.Is(PlayerClass.Assault), point = PlayerClass.Is(PlayerClass.Point);
        bool ghost = PlayerClass.Is(PlayerClass.Ghost), knight = PlayerClass.Is(PlayerClass.Knight), hound = PlayerClass.Is(PlayerClass.Schiesshund);
        bool detente = PlayerClass.Is(PlayerClass.Detente), support = PlayerClass.Is(PlayerClass.Support);
        pipRow.gameObject.SetActive(assault || ghost || hound || detente || support);
        deadeye.text = Texts.Get(hound ? "class.hud.desperado" : "class.hud.deadeye");
        slotRow.gameObject.SetActive(point);
        keyRect.gameObject.SetActive(false);
        deadeye.gameObject.SetActive(false);
        Word(Texts.Get(knight ? "class.hud.cover" : detente ? "class.hud.inject" : support ? "class.hud.barricade" : hound ? "class.hud.duel"
            : point ? "class.hud.kata" : "class.hud.charge"), KitStyle.Accent);
        note.text = info != null && !info.Ready ? Texts.Get("class.hud.later") : "";
        // A Détente moves the note past its pips; every other class has it back in its place.
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(128f, 0f);
        shownPips = 0;
        if (assault) Pips(kit != null ? kit.StackMax : 8);
    }

    // As many pips as the resource holds (an Assault's adrenaline; a Ghost's Head Hunter, whose most the cards may raise).
    private void Pips(int most)
    {
        int count = Mathf.Clamp(most, 1, MostPips);
        if (count == shownPips) return;
        shownPips = count;
        for (int i = 0; i < MostPips; i++) pips[i].gameObject.SetActive(i < count);
    }

    // Adrenaline: a pip a stack (as many as its most, which the 깊은 숨 card raises), all beating with the key's prompt once
    // full. With 데인저 클로즈 (ClassCards) its barrage goes past the pips: that the key held aims it, or its wait; and while
    // the key is held to aim, "[휠] 놓으면 포격" over the plate ("포격 불가" in red where it can't fall) and how to call it
    // off past the pips.
    private void Adrenaline()
    {
        Pips(kit.StackMax);
        int count = shownPips, stacks = kit.Stacks;
        bool ready = kit.ChargeReady;
        float beat = ready ? .75f + .25f * Mathf.Sin(Time.unscaledTime * 9f) : 1f;
        for (int i = 0; i < count; i++)
            pips[i].color = i < stacks ? KitStyle.With(KitStyle.Accent, beat) : KitStyle.With(KitStyle.Ice, .14f);
        var cards = ClassCards.Local;
        string side = "";
        if (cards != null && cards.DangerAiming)
        {
            bool fits = cards.DangerFits;
            KeyPrompt(true);
            Word(Texts.Get(fits ? "class.hud.danger.aim" : "class.hud.danger.no"), fits ? KitStyle.Accent : KitStyle.Red);
            side = Texts.Get(fits ? "class.hud.danger.cancel" : "class.assault.danger.ground");
        }
        else
        {
            KeyPrompt(ready);
            Word(Texts.Get("class.hud.charge"), KitStyle.Accent);
            if (cards != null && cards.HasDanger)
                side = cards.DangerReady ? Texts.Format("class.hud.danger.hold", KeyName())
                    : Texts.Format("class.hud.danger_wait", Mathf.CeilToInt(cards.DangerWait));
        }
        if (note.text != side) note.text = side;
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(128f + count * (PipWidth + PipGap) + 6f, 0f);
    }

    // A Ghost's Head Hunter: a pip a stack. The one that came flashes white, the one lost too, and all of them when a
    // hurt takes every stack. "DEADEYE" over the plate while the gun in hand holds its first round since loading.
    private void Hunter()
    {
        Pips(kit.HunterMax);
        int count = shownPips, stacks = Mathf.Min(kit.Hunter, count), change = kit.HunterChange;
        float flash = Mathf.Clamp01(1f - kit.SinceHunterChange / .3f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < stacks ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .14f);
            bool changed = change == -2 || (change == 1 && i == stacks - 1) || (change == -1 && i == stacks);
            pips[i].color = flash > 0f && changed ? Color.Lerp(color, KitStyle.Ice, flash) : color;
        }
        bool ready = kit.DeadeyeReady;
        deadeye.gameObject.SetActive(ready);
        if (ready) deadeye.color = KitStyle.With(KitStyle.Accent, .78f + .22f * Mathf.Sin(Time.unscaledTime * 6f));
    }

    // A Schießhund's Desperado: a teal pip a round in its cylinder, the newest flashing white as a bounty loads it, and
    // "DESPERADO" over the plate while it is in hand with a round in it (its next shot spends one).
    private void Hound()
    {
        Pips(kit.RoundMax);
        int count = shownPips, rounds = Mathf.Min(kit.Rounds, count);
        float flash = Mathf.Clamp01(1f - kit.SinceBounty / .4f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < rounds ? PingMarks.BountyColor : KitStyle.With(KitStyle.Ice, .14f);
            pips[i].color = flash > 0f && i == rounds - 1 ? Color.Lerp(color, KitStyle.Ice, flash) : color;
        }
        bool loaded = kit.DesperadoLoaded;
        deadeye.gameObject.SetActive(loaded);
        if (loaded) deadeye.color = KitStyle.With(PingMarks.BountyColor, .78f + .22f * Mathf.Sin(Time.unscaledTime * 6f));
        // Its duel on the class key: the prompt while it may be called, the seconds left to wait past the pips while not.
        bool ready = kit.DuelReady;
        KeyPrompt(ready);
        string wait = ready ? "" : Texts.Format("class.hud.duel_wait", Mathf.CeilToInt(kit.DuelWait));
        if (note.text != wait) note.text = wait;
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(128f + count * (PipWidth + PipGap) + 6f, 0f);
    }

    // A Détente: a red pip a dose left in the syringe (the newest flashing white as one goes in), "[휠] 주사" while there
    // is one, and how many Resurrections are left beside the pips. A treatment by hand or a Resurrection under way and
    // Golden Hour are timed effects like any other, each in its slot under the plate (DetenteKit says them).
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
            pips[i].color = flash > 0f && i == doses ? Color.Lerp(color, KitStyle.Ice, flash) : color;
        }
        KeyPrompt(!detente.Empty);
        // By hand (the syringe empty): the offer to make, the answer awaited, else the syringe's state.
        var friend = detente.Candidate;
        string side = detente.Treating != null ? "" : detente.Offering != null ? Texts.Format("class.hud.treat.waiting", PlayerTally.NameOf(detente.Offering))
            : friend != null ? Texts.Format(DetenteKit.Asked(friend) ? "class.hud.treat.asked" : "class.hud.treat.offer", PlayerTally.NameOf(friend))
            : detente.Empty ? Texts.Get("class.hud.empty") : detente.RaisesLeft > 0 ? Texts.Format("class.hud.raises", detente.RaisesLeft) : "";
        if (note.text != side) note.text = side;
        note.rectTransform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(128f + count * (PipWidth + PipGap) + 6f, 0f);
    }

    // A Support's bag: a pip a resupply's worth (white when full, the next one filling), "[휠] 바리케이드" while one can go
    // up, and beside the pips "[F] 보급 · name" while a friend is in reach, else the bag in numbers and how many of its
    // barricades stand (a barricade under the crosshair has its own line at the foot of the screen: SupportKit) (the user,
    // 2026-11-12: "배낭이 얼마나 찼는지 보이나? 나는 바리케이드 설치 가능 횟수만 보여서": the pips alone didn't read as the
    // bag). While the
    // class key is held to place one (SupportKit's preview): "[휠] 놓으면 설치" and "[우클릭] 취소" where it may go up,
    // "[휠] 설치 불가" in red and why where it can't; and with the throw card, "[F] 던지기 · name" while a friend is under
    // the crosshair in reach, F throwing them a supply pack then instead of choosing the next kind.
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
            pips[i].color = flash > 0f && i == Mathf.FloorToInt(bags) ? Color.Lerp(color, KitStyle.Accent, flash) : color;
        }
        string side;
        if (support.Planning)
        {
            bool fits = support.PlanFit == Barricade.Fit.Ok;
            KeyPrompt(true);
            // What it puts up and for how much; F goes on to the next kind held.
            string what = Texts.Get("class.hud.build." + support.PlanKind.ToString().ToLowerInvariant());
            // With as many standing as may, putting it up takes the oldest down: the word says so ("놓으면 교체"), as the
            // amber chip under the plate and the amber ghost over that one in the world do.
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
            KeyPrompt(support.CanBuild);
            Word(Texts.Get("class.hud.barricade"), KitStyle.Accent);
            var friend = support.Offered;
            // A barricade under the crosshair has its own line at the foot of the screen, where the eyes are (SupportKit).
            // How many of each structure stand is the line under the plate's (DeployStrip): every kind at once, and still
            // there while placing, where this note showed the one kind in hand and hid it then.
            side = friend != null ? Texts.Format("class.hud.resupply", friend.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : friend.name)
                : Texts.Format("class.hud.bag", Mathf.FloorToInt(support.Supply), Mathf.RoundToInt(support.SupplyMax));
        }
        if (note.text != side) note.text = side;
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(128f + count * (PipWidth + PipGap) + 6f, 0f);
    }

    // A Knight's cover (KnightCover): the key's prompt while it is ready; while held, the friend under the crosshair (or
    // "look at a friend"); the friend it watches over while a cover is open; else the seconds left to wait.
    private void Taunt()
    {
        var cover = KnightCover.Local;
        bool ready = cover != null && cover.Ready;
        KeyPrompt(ready);
        string side = cover == null ? ""
            : cover.Planning ? (cover.Aimed != null ? Texts.Format("class.hud.cover.aim", PlayerTally.NameOf(cover.Aimed)) : Texts.Get("class.hud.cover.look"))
            : cover.Watched != null ? Texts.Format("class.hud.cover.watch", PlayerTally.NameOf(cover.Watched))
            : ready ? "" : Texts.Format("class.hud.cover_wait", Mathf.CeilToInt(cover.Wait));
        if (note.text != side) note.text = side;
    }

    // The word beside the key's cap, and its colour (a Support's turns red where its barricade can't go).
    private void Word(string text, Color color)
    {
        if (keyWord.text != text) keyWord.text = text;
        if (keyWord.color != color) keyWord.color = color;
    }

    // "[휠] CHARGE" or "[휠] 도발" over the plate's right end, beating, while the class key has something to do.
    private void KeyPrompt(bool ready)
    {
        keyRect.gameObject.SetActive(ready);
        if (!ready) return;
        string key = KeyName();
        if (keyLabel.text != key)
        {
            keyLabel.text = key;
            float width = Mathf.Max(30f, keyLabel.GetPreferredValues(key).x + 16f);
            keyRect.sizeDelta = new Vector2(width, 24f);
            keyCap.SetVerticesDirty();
        }
        keyRect.localScale = Vector3.one * (1f + .06f * Mathf.Sin(Time.unscaledTime * 9f));
    }

    // A Point's two guns: the line under each runs out while it reloads put away, and flashes when it has.
    private void Guns()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            float share = kit.AsideShare(slot), since = kit.SinceRefill(slot);
            slotLines[slot].rectTransform.anchorMax = new Vector2(share, 1f);
            slotLines[slot].color = KitStyle.Accent;
            float flash = Mathf.Clamp01(1f - since / .5f);
            slotBacks[slot].color = Color.Lerp(KitStyle.With(KitStyle.Ice, .12f), KitStyle.With(KitStyle.Accent, .85f), flash);
        }
        // The Gun Kata on the class key: the prompt while it may be used, the seconds left to wait past the guns while not.
        bool ready = kit.KataReady;
        KeyPrompt(ready);
        string wait = ready ? "" : Texts.Format("class.hud.kata_wait", Mathf.CeilToInt(kit.KataWait));
        if (note.text != wait) note.text = wait;
        ((RectTransform)note.rectTransform.parent).anchoredPosition = new Vector2(128f + 160f + 6f, 0f);
    }

    private string KeyName()
    {
        if (ability == null && InputSystem.actions != null) ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        return KeyBindings.Name(ability);
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
        deploy?.Dispose();
        effects?.Dispose();
        if (plate == null) return;
        var host = plate.parent != null ? plate.parent.gameObject : plate.gameObject;
        if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
    }
}
