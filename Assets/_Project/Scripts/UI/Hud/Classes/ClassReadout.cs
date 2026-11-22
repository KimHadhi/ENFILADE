using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// What the class UI shows for this PC's player this frame, read from the class's kits and nothing drawn here: the HUD
/// draws it with the addon HUD's parts (HudAddons.PlayerHud.Class). Per class:
/// <list type="bullet">
/// <item>Assault: adrenaline as pips (as many as its most, which the 깊은 숨 card raises), "[wheel] CHARGE" beating once
/// full; with 데인저 클로즈 the barrage's wait or aim, and while the key is held to aim, "놓으면 포격" (or "포격 불가" in
/// red) and how to call it off.</item>
/// <item>Point: its two guns, each with the line that runs out while the gun put away reloads itself and a flash when it
/// has; the Gun Kata on the key, its wait while not ready.</item>
/// <item>Ghost: its Head Hunter stacks as pips, the one that came or went flashing (all of them when a hurt takes every
/// stack), "DEADEYE" while the gun in hand holds its first round since loading.</item>
/// <item>Knight: its cover on the key while ready; while held, the friend under the crosshair; the friend it watches
/// over; else its wait.</item>
/// <item>Schießhund: the Desperado's rounds as pips, the newest flashing as a bounty loads it, "DESPERADO" while it is in
/// hand with a round in it, the duel on the key and its wait.</item>
/// <item>Détente: a pip a dose in the syringe, the one that went flashing, "[wheel] 주사" while a dose is in it, the
/// offer of a treatment by hand and how many Resurrections are left.</item>
/// <item>Support: a pip a resupply's worth (the next one filling), the barricade on the key, and while placing what
/// goes up and for how much, or why it can't ("설치 불가" in red).</item>
/// </list>
/// A recharging ability (the Knight's cover, the Point's kata, the Schießhund's duel) also gives its share back, which
/// the class box fills with. The rules and texts are the old class plate's (ClassHUD, 0.9.329), kept as they were; the
/// plate itself went when the class UI moved into the addon HUD's parts (2026-11-22). This PC's own player only.
/// </summary>
public static class ClassReadout
{
    /// <summary>One frame's class UI. Reused: Read fills it.</summary>
    public sealed class View
    {
        public bool Shown;
        public string Id = "", Title = "";
        public Texture Icon;
        /// <summary>Pips: how many, how many are full (a fraction for the Support's bag); none when Most is 0.</summary>
        public int Most;
        public float Filled;
        /// <summary>The pip that just changed (-1: none) or all of them, and how much of its flash is left (0-1).</summary>
        public int FlashAt = -1;
        public bool FlashAll;
        public float Flash;
        /// <summary>A gained stack, so the flash is a swell on the new pip (not a loss).</summary>
        public bool Gained;
        /// <summary>The Point's two guns: how far each has reloaded put away, and each one's refill flash (0-1).</summary>
        public bool Guns;
        public readonly float[] Gun = new float[2], GunFlash = new float[2];
        /// <summary>The count at the class box's corner (-1: none), and the box's fill for a recharging ability (-1: none).</summary>
        public int Count = -1;
        public float Rise = -1f;
        /// <summary>The grey words under the pips.</summary>
        public string Side = "";
        /// <summary>The class key's prompt: shown, its word, red where the thing can't be done, beating while it may be acted on.</summary>
        public bool Key, Deny, Beat;
        public string Word = "";
        /// <summary>DEADEYE / DESPERADO, or null.</summary>
        public string Mark;
        /// <summary>The kind being placed now ("" for none): its deploy chip is marked.</summary>
        public string Placing = "";
    }

    private static readonly Dictionary<(string, string), string> formatted = new Dictionary<(string, string), string>();
    private static InputAction ability;
    // A recharge's longest wait seen since it last began, so its share can be shown (the kits give only what is left).
    private static float kataLongest, duelLongest, coverLongest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        formatted.Clear();
        ability = null;
        kataLongest = duelLongest = coverLongest = 0f;
    }

    /// <summary>Fills <paramref name="view"/> for <paramref name="player"/>; Shown is false while there is nothing to show.</summary>
    public static void Read(PlayerMovement player, View view)
    {
        Clear(view);
        if (player == null || player.IsDead) return;
        var kit = player.GetComponent<ClassKit>();
        string id = PlayerClass.CurrentId;
        var info = PlayerClass.Find(id);
        view.Shown = true;
        view.Id = id;
        view.Title = info != null ? info.Title : id;
        view.Icon = KitButtons.Icon(MainMenuClasses.Icon(id));
        view.Placing = Placing();
        if (info != null && !info.Ready) view.Side = Texts.Get("class.hud.later");
        if (kit == null) return;
        if (PlayerClass.Is(PlayerClass.Assault)) Assault(kit, view);
        else if (PlayerClass.Is(PlayerClass.Point)) Point(kit, view);
        else if (PlayerClass.Is(PlayerClass.Ghost)) Ghost(kit, view);
        else if (PlayerClass.Is(PlayerClass.Knight)) Knight(view);
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) Hound(kit, view);
        else if (PlayerClass.Is(PlayerClass.Detente)) Detente(view);
        else if (PlayerClass.Is(PlayerClass.Support)) Support(view);
    }

    /// <summary>The HUD settings' sample where no player has a class UI (HudStage on the title screen): the class to be
    /// played, some of its pips full, its key ready.</summary>
    public static void Sample(View view)
    {
        Clear(view);
        string id = ClassGrowth.CurrentId;
        var info = PlayerClass.Find(id);
        view.Shown = true;
        view.Id = id;
        view.Title = info != null ? info.Title : id;
        view.Icon = KitButtons.Icon(MainMenuClasses.Icon(id));
        view.Most = 8;
        view.Filled = 5f;
        view.Key = view.Beat = true;
        view.Word = Texts.Get("settings.hudedit.sample.key");
    }

    private static void Clear(View view)
    {
        view.Shown = false;
        view.Icon = null;
        view.Most = 0;
        view.Filled = 0f;
        view.FlashAt = -1;
        view.FlashAll = view.Gained = view.Guns = view.Key = view.Deny = view.Beat = false;
        view.Flash = 0f;
        view.Count = -1;
        view.Rise = -1f;
        view.Side = view.Word = view.Placing = "";
        view.Mark = null;
    }

    // The kind of thing being placed now, as Tables/Deployables names it ("" for none): a Support's structure while its
    // key is held, or a claymore held out.
    private static string Placing()
    {
        var support = SupportKit.Local;
        if (support != null && support.Planning) return Barricade.KindName(support.PlanKind);
        var grenades = PlayerGrenades.Local;
        return grenades != null && grenades.Placing ? ClaymoreMine.KindName : "";
    }

    private static void Assault(ClassKit kit, View view)
    {
        view.Most = kit.StackMax;
        view.Filled = Mathf.Min(kit.Stacks, view.Most);
        view.Count = kit.Stacks;
        bool ready = kit.ChargeReady;
        var cards = ClassCards.Local;
        if (cards != null && cards.DangerAiming)
        {
            bool fits = cards.DangerFits;
            Prompt(view, Texts.Get(fits ? "class.hud.danger.aim" : "class.hud.danger.no"), !fits);
            view.Side = Texts.Get(fits ? "class.hud.danger.cancel" : "class.assault.danger.ground");
            return;
        }
        if (ready) Prompt(view, Texts.Get("class.hud.charge"), false);
        if (cards != null && cards.HasDanger)
            view.Side = cards.DangerReady ? Format("class.hud.danger.hold", KeyName()) : Format("class.hud.danger_wait", Seconds(cards.DangerWait));
    }

    private static void Point(ClassKit kit, View view)
    {
        view.Guns = true;
        for (int slot = 0; slot < 2; slot++)
        {
            view.Gun[slot] = kit.AsideShare(slot);
            view.GunFlash[slot] = Mathf.Clamp01(1f - kit.SinceRefill(slot) / .5f);
        }
        bool ready = kit.KataReady;
        view.Rise = Recharge(ref kataLongest, kit.KataWait, ready);
        if (ready) Prompt(view, Texts.Get("class.hud.kata"), false);
        else view.Side = Format("class.hud.kata_wait", Seconds(kit.KataWait));
    }

    private static void Ghost(ClassKit kit, View view)
    {
        view.Most = kit.HunterMax;
        int stacks = Mathf.Min(kit.Hunter, view.Most), change = kit.HunterChange;
        view.Filled = stacks;
        view.Count = stacks;
        view.Flash = Mathf.Clamp01(1f - kit.SinceHunterChange / .3f);
        view.FlashAll = change == -2;
        view.Gained = change == 1;
        view.FlashAt = change == 1 ? stacks - 1 : change == -1 ? stacks : -1;
        if (kit.DeadeyeReady) view.Mark = Texts.Get("class.hud.deadeye");
    }

    private static void Knight(View view)
    {
        var cover = KnightCover.Local;
        if (cover == null) return;
        bool ready = cover.Ready;
        view.Rise = Recharge(ref coverLongest, cover.Wait, ready);
        if (ready) Prompt(view, Texts.Get("class.hud.cover"), false);
        view.Side = cover.Planning ? (cover.Aimed != null ? Format("class.hud.cover.aim", PlayerTally.NameOf(cover.Aimed)) : Texts.Get("class.hud.cover.look"))
            : cover.Watched != null ? Format("class.hud.cover.watch", PlayerTally.NameOf(cover.Watched))
            : ready ? "" : Format("class.hud.cover_wait", Seconds(cover.Wait));
    }

    private static void Hound(ClassKit kit, View view)
    {
        view.Most = kit.RoundMax;
        int rounds = Mathf.Min(kit.Rounds, view.Most);
        view.Filled = rounds;
        view.Count = rounds;
        view.Flash = Mathf.Clamp01(1f - kit.SinceBounty / .4f);
        view.FlashAt = rounds - 1;
        view.Gained = true;
        if (kit.DesperadoLoaded) view.Mark = Texts.Get("class.hud.desperado");
        bool ready = kit.DuelReady;
        view.Rise = Recharge(ref duelLongest, kit.DuelWait, ready);
        if (ready) Prompt(view, Texts.Get("class.hud.duel"), false);
        else view.Side = Format("class.hud.duel_wait", Seconds(kit.DuelWait));
    }

    private static void Detente(View view)
    {
        var detente = DetenteKit.Local;
        if (detente == null) return;
        view.Most = detente.MostDoses;
        int doses = Mathf.Min(detente.Doses, view.Most);
        view.Filled = doses;
        view.Count = doses;
        view.Flash = Mathf.Clamp01(1f - detente.SinceInject / .35f);
        view.FlashAt = doses;
        if (!detente.Empty) Prompt(view, Texts.Get("class.hud.inject"), false);
        // By hand (the syringe empty): the offer to make, the answer awaited, else the syringe's state.
        var friend = detente.Candidate;
        view.Side = detente.Treating != null ? "" : detente.Offering != null ? Format("class.hud.treat.waiting", PlayerTally.NameOf(detente.Offering))
            : friend != null ? Format(DetenteKit.Asked(friend) ? "class.hud.treat.asked" : "class.hud.treat.offer", PlayerTally.NameOf(friend))
            : detente.Empty ? Texts.Get("class.hud.empty") : detente.RaisesLeft > 0 ? Format("class.hud.raises", detente.RaisesLeft.ToString()) : "";
    }

    private static void Support(View view)
    {
        var support = SupportKit.Local;
        if (support == null) return;
        view.Most = Mathf.FloorToInt(support.SupplyMax / support.GiveCost + .001f);
        float bags = support.Supply / support.GiveCost;
        view.Filled = Mathf.Min(bags, view.Most);
        view.Count = Mathf.FloorToInt(bags + .001f);
        view.Flash = Mathf.Clamp01(1f - Mathf.Min(support.SinceGive, support.SinceBuild) / .35f);
        view.FlashAt = Mathf.FloorToInt(bags);
        if (support.Planning)
        {
            bool fits = support.PlanFit == Barricade.Fit.Ok;
            string what = Texts.Get("class.hud.build." + support.PlanKind.ToString().ToLowerInvariant());
            // With as many standing as may, putting it up takes the oldest down: the word says so ("놓으면 교체").
            bool swaps = fits && support.NextDown != null;
            Prompt(view, Format(!fits ? "class.hud.place.kind.no" : swaps ? "class.hud.place.kind.swap" : "class.hud.place.kind", what,
                Mathf.RoundToInt(support.BuildCost).ToString()), !fits);
            // With the throw card and a friend under the crosshair, F throws them a pack instead (SupportKit.Tossable).
            var catcher = support.Tossable;
            view.Side = (fits ? Texts.Get("class.hud.place.cancel") : support.PlanWhy) + " · "
                + (catcher != null ? Format("class.hud.throw", PlayerTally.NameOf(catcher), Mathf.RoundToInt(support.ThrowCost).ToString())
                    : Texts.Get("class.hud.build.switch"));
            return;
        }
        if (support.CanBuild) Prompt(view, Texts.Get("class.hud.barricade"), false);
        var friend = support.Offered;
        view.Side = friend != null ? Format("class.hud.resupply", friend.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : friend.name)
            : Format("class.hud.bag", Mathf.FloorToInt(support.Supply).ToString(), Mathf.RoundToInt(support.SupplyMax).ToString());
    }

    // The prompt beats while it may be acted on, as the old plate's key cap did; one that says no stays still.
    private static void Prompt(View view, string word, bool deny)
    {
        view.Key = true;
        view.Word = word;
        view.Deny = deny;
        view.Beat = !deny;
    }

    /// <summary>The class key's name as the player bound it.</summary>
    public static string KeyName()
    {
        if (ability == null && InputSystem.actions != null) ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        return KeyBindings.Name(ability);
    }

    // A recharge's share: the wait is measured against the longest seen since it began; ready is full.
    private static float Recharge(ref float longest, float wait, bool ready)
    {
        if (ready || wait <= 0f)
        {
            longest = 0f;
            return 1f;
        }
        if (wait > longest) longest = wait;
        return Mathf.Clamp01(1f - wait / Mathf.Max(.01f, longest));
    }

    private static string Seconds(float wait) => Mathf.CeilToInt(wait).ToString();

    // Texts.Format, kept while its words stay the same (the HUD reads every frame; the words change seldom).
    private static string Format(string key, string a, string b = null)
    {
        string arg = b == null ? a : a + "\u0001" + b;
        if (formatted.TryGetValue((key, arg), out string text)) return text;
        text = b == null ? Texts.Format(key, a) : Texts.Format(key, a, b);
        if (formatted.Count > 512) formatted.Clear();
        return formatted[(key, arg)] = text;
    }
}
