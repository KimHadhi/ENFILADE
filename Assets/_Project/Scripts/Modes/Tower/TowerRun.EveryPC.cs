using System.Collections.Generic;
using UnityEngine;

// TowerRun: Every PC. One part of the class; the rest is in TowerRun.cs and the files beside it.
public sealed partial class TowerRun
{
    private void Present()
    {
        if (plan == null) { offering = false; return; }
        if (shownSeed != seed) { shownSeed = seed; WriteSigns(); }
        var mine = PlayerRoster.Local;
        int myLane = mine != null ? TowerLayout.LaneAt(mine.transform.position, segment) : -1;
        ShowDoors();
        ShowScanners();
        Announce(mine, myLane);
        ShowHud(myLane);
        ShowObjective(mine, myLane);
        Prompt(mine);
        Fetch(mine);
        PlayMusic();
        ShowCardWhenDue();
    }

    private void ShowDoors()
    {
        foreach (var door in doors) if (door != null) door.Open = WantsOpen(door);
    }

    private bool WantsOpen(TowerDoor door)
    {
        int k = door.Lane;
        if (k < 0 || k >= TowerLayout.Lanes) return false;
        bool chosen = (opened & (1 << k)) != 0;
        switch (door.Kind)
        {
            case TowerDoorKind.Departure:
                return phase == Phase.Boarding && chosen && door.Level == TowerLayout.HallLevel(segment);
            case TowerDoorKind.Entry:
            {
                int index = door.Level - TowerLayout.FirstLevel(segment);
                if (index < 0 || index >= TowerLayout.FloorsIn(segment)) return false;
                // Open on the way up, and through the fight until everyone of the lane still alive is in the room: the
                // first one in doesn't shut a slower lane-mate out on the stairs.
                return (phase == Phase.Boarding && chosen && index == 0)
                       || (phase == Phase.Lanes && floors[k] == index
                           && (states[k] == LaneState.Climbing || (states[k] == LaneState.Fighting && SomeoneOutside(k, door.Level))));
            }
            case TowerDoorKind.Exit:
            {
                int index = door.Level - TowerLayout.FirstLevel(segment);
                if (index < 0 || index >= TowerLayout.FloorsIn(segment) || (phase != Phase.Lanes && phase != Phase.Gather)) return false;
                return floors[k] > index || (states[k] == LaneState.Done && floors[k] == index);
            }
            case TowerDoorKind.Arrival:
                return (phase == Phase.Lanes || phase == Phase.Gather) && states[k] == LaneState.Done
                       && !TowerLayout.IsLastSegment(segment) && door.Level == TowerLayout.HallLevel(segment + 1);
        }
        return false;
    }

    // Someone of the lane, alive, is not yet in its room on this level (on the stairs, or in the doorway).
    private bool SomeoneOutside(int k, int level)
    {
        foreach (var someone in players)
            if (TowerLayout.LaneAt(someone.transform.position, segment) == k && !TowerLayout.InRoom(someone.transform.position, level, k, .6f))
                return true;
        return false;
    }

    private void ShowScanners()
    {
        ulong me = MyId;
        foreach (var scanner in scanners)
        {
            if (scanner == null) continue;
            int k = scanner.Lane;
            bool here = scanner.Segment == segment && k >= 0 && k < TowerLayout.Lanes;
            bool chosen = here && (opened & (1 << k)) != 0;
            scanner.Raised = here && (phase == Phase.Gate || (phase == Phase.Boarding && chosen));
            if (!here) { scanner.Show(TowerScanner.Look.Down); continue; }
            scanner.Show(phase != Phase.Gate ? (chosen ? TowerScanner.Look.Chosen : TowerScanner.Look.Free)
                : hands[k] == 0 ? TowerScanner.Look.Free : hands[k] == me ? TowerScanner.Look.Mine : TowerScanner.Look.Held);
        }
    }

    // Each lane's letter over its door, and what its rooms hold in each hall's stretch.
    private void WriteSigns()
    {
        foreach (var sign in signs)
        {
            if (sign == null) continue;
            int stretch = Mathf.Clamp(sign.Segment, 0, TowerLayout.Segments - 1);
            var trials = new List<string>();
            for (int i = 0; i < TowerLayout.FloorsIn(stretch); i++) trials.Add(plan.Label(TowerLayout.FloorNumber(stretch, i), sign.Lane));
            int first = TowerLayout.FloorNumber(stretch, 0), last = TowerLayout.FloorNumber(stretch, TowerLayout.FloorsIn(stretch) - 1);
            string floorsText = first == last ? Texts.Format("tower.sign.floor", first) : Texts.Format("tower.sign.floors", first, last);
            sign.Write("<size=280%><b>" + TowerHUD.Letter(sign.Lane) + "</b></size>\n<color=#FF4F78>" + floorsText + "</color>\n"
                       + string.Join(" · ", trials));
        }
    }

    // What changed since the last frame: sounds, alerts, and this player's pay for a floor got through.
    private void Announce(PlayerMovement mine, int myLane)
    {
        if (phase != shownPhase)
        {
            if (phase == Phase.Gate && started) { AlertHUD.Self(Texts.Get("tower.alert.scanners")); KitSound.Play(KitSound.Cue.TowerRise, .8f); }
            if (phase == Phase.Gate && !started) KitSound.Play(KitSound.Cue.TowerRise, .6f);
            if (phase == Phase.Boarding) AlertHUD.Self(Texts.Get("tower.alert.open"));
            if (phase == Phase.Gather) AlertHUD.Self(Texts.Get("tower.alert.gather"));
            if (phase == Phase.Finale) Finish();
        }
        if (resets != shownResets) { shownResets = resets; if (started) AlertHUD.Danger(Texts.Get("tower.alert.reset")); }
        if (phase == Phase.Gate && counting && !shownCounting)
        {
            ulong first = 0UL;
            foreach (ulong hand in hands) if (hand != 0) { first = hand; break; }
            AlertHUD.Self(Texts.Format("tower.alert.count", NameOf(first), Mathf.CeilToInt(countSeconds)));
        }
        shownCounting = counting && (phase == Phase.Gate || phase == Phase.Boarding);
        // A tick for each second of a count, louder for the last three.
        int second = shownCounting ? Mathf.CeilToInt(count) : -1;
        if (second != shownSecond && second > 0) KitSound.Play(KitSound.Cue.TowerTick, second <= 3 ? 1f : .55f);
        shownSecond = second;
        // Lanes getting through floors and going out, in the stretch that was already showing (not on this PC's first look,
        // so joining late pays for nothing; a stretch's last room is paid even when the stretch ends on that same frame).
        if (started && shownSegment == segment && (phase == Phase.Lanes || phase == Phase.Gather || phase == Phase.Finale))
            for (int k = 0; k < TowerLayout.Lanes; k++)
            {
                for (int index = shownFloors[k]; index < floors[k]; index++) Through(k, index, mine, myLane);
                if (states[k] == LaneState.Done && shownStates[k] != LaneState.Done) Through(k, floors[k], mine, myLane);
                if (states[k] == LaneState.Failed && shownStates[k] != LaneState.Failed)
                    AlertHUD.Danger(Texts.Format("tower.alert.failed", TowerHUD.Letter(k)));
            }
        for (int k = 0; k < TowerLayout.Lanes; k++) { shownStates[k] = states[k]; shownFloors[k] = floors[k]; }
        shownPhase = phase;
        shownSegment = segment;
        started = true;
    }

    // Lane k got through floor index <index> of the stretch: its own players are paid (each PC its own), the rest told.
    private void Through(int k, int index, PlayerMovement mine, int myLane)
    {
        int floor = TowerLayout.FloorNumber(segment, index);
        bool mineToo = mine != null && !mine.IsDead && myLane == k;
        if (!mineToo)
        {
            AlertHUD.Team(Texts.Format("tower.alert.cleared", TowerHUD.Letter(k), floor), null, KitStyle.Accent);
            return;
        }
        KitSound.Play(KitSound.Cue.TowerClear, .9f);
        CombatFeedHUD.Award(Texts.Format("tower.feed.floor", floor), Mathf.Max(0, plan.Of(floor).reward));
    }

    private void ShowHud(int myLane)
    {
        if (hud == null) return;
        bool countShown = counting && (phase == Phase.Gate || phase == Phase.Boarding);
        hud.Count(countShown, count, phase == Phase.Boarding ? Texts.Get("tower.hud.board") : Texts.Get("tower.hud.count"));
        bool lanesShown = phase != Phase.Finale;
        hud.ShowLanes(lanesShown);
        if (!lanesShown) return;
        ulong me = MyId;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            string letter = TowerHUD.Letter(k);
            bool chosen = (opened & (1 << k)) != 0;
            string words;
            bool faded = false, own;
            if (phase == Phase.Gate)
            {
                words = hands[k] != 0 ? Texts.Format("tower.hud.lane.held", letter, NameOf(hands[k])) : Texts.Format("tower.hud.lane.free", letter);
                faded = hands[k] == 0;
                own = hands[k] == me;
            }
            else
            {
                int floor = TowerLayout.FloorNumber(segment, Mathf.Clamp(floors[k], 0, TowerLayout.FloorsIn(segment) - 1));
                switch (states[k])
                {
                    case LaneState.Boarding: words = Texts.Format("tower.hud.lane.in", letter); break;
                    case LaneState.Climbing: words = Texts.Format("tower.hud.lane.climb", letter, floor); break;
                    case LaneState.Fighting: words = Texts.Format("tower.hud.lane.fight", letter, floor, lefts[k]); break;
                    case LaneState.Done: words = Texts.Format("tower.hud.lane.done", letter); break;
                    case LaneState.Failed: words = Texts.Format("tower.hud.lane.failed", letter); faded = true; break;
                    default: words = Texts.Format("tower.hud.lane.none", letter); faded = true; break;
                }
                own = chosen && myLane == k;
            }
            hud.Lane(k, words, own, faded);
        }
    }

    // The objective on the mission HUD, for where this player is.
    private void ShowObjective(PlayerMovement mine, int myLane)
    {
        if (tracker == null || phase == Phase.Finale) return;
        string id, title, about;
        string done = null;
        bool fight = false;
        int floor = 0;
        if (phase == Phase.Gate)
        {
            id = "tower.gate." + segment;
            title = Texts.Get(counting ? "tower.objective.gate_count" : "tower.objective.gate");
            about = Texts.Get("tower.objective.gate_desc");
        }
        else if (phase == Phase.Boarding)
        {
            bool inside = myLane >= 0 && (opened & (1 << myLane)) != 0;
            id = inside ? "tower.board.in." + segment : "tower.board." + segment;
            title = Texts.Get(inside ? "tower.objective.climb_first" : "tower.objective.board");
            about = Texts.Get(inside ? "tower.objective.climb_desc" : "tower.objective.board_desc");
        }
        else if (phase == Phase.Gather)
        {
            id = "tower.gather." + segment;
            title = Texts.Get("tower.objective.gather");
            about = Texts.Get("tower.objective.up_desc");
        }
        else if (mine != null && mine.IsDead)
        {
            id = "tower.dead." + segment;
            title = Texts.Get("tower.objective.dead");
            about = Texts.Get("tower.objective.dead_desc");
        }
        else if (myLane >= 0 && (states[myLane] == LaneState.Climbing || states[myLane] == LaneState.Fighting))
        {
            floor = TowerLayout.FloorNumber(segment, floors[myLane]);
            fight = states[myLane] == LaneState.Fighting;
            id = (fight ? "tower.fight." : "tower.climb.") + floor;
            title = fight ? Texts.Format("tower.objective.fight", floor, plan.Label(floor, myLane)) : Texts.Format("tower.objective.climb", floor);
            about = Texts.Get(fight ? "tower.objective.fight_desc" : "tower.objective.climb_desc");
            if (fight) done = Texts.Format("tower.done.floor", floor);
        }
        else if (myLane >= 0 && states[myLane] == LaneState.Done)
        {
            bool last = TowerLayout.IsLastSegment(segment);
            id = "tower.up." + segment;
            title = Texts.Get(last ? "tower.objective.last" : "tower.objective.up");
            about = Texts.Get(last ? "tower.objective.last_desc" : "tower.objective.up_desc");
        }
        else
        {
            id = "tower.wait." + segment;
            title = Texts.Get("tower.objective.wait_lanes");
            about = Texts.Get("tower.objective.wait_desc");
        }
        int left = fight ? lefts[myLane] : 0;
        if (id + title != shownObjective)
        {
            shownObjective = id + title;
            fightTotal = left;
            shownLeft = left;
            // The way up between rooms is said quietly; a room's fight, the scanners and the halls sound the objective's alarm.
            bool announce = !id.StartsWith("tower.climb.", System.StringComparison.Ordinal);
            tracker.SetObjective(fight
                ? new MissionObjective(id, title, about, MissionProgressDisplay.Count, 0, Mathf.Max(1, fightTotal), done)
                : new MissionObjective(id, title, about, doneTitle: done), announce);
            return;
        }
        if (!fight || left == shownLeft) return;
        shownLeft = left;
        fightTotal = Mathf.Max(fightTotal, left);
        tracker.SetProgress(id, Mathf.Max(0, fightTotal - left), Mathf.Max(1, fightTotal));
    }

    // [F]: a scanner in the hall that is choosing, else a shop counter.
    private void Prompt(PlayerMovement mine)
    {
        if (screen == null) return;
        string text = null;
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && !mine.IsDowned && !FieldPickups.Targeting && phase != Phase.Finale;
        if (able && phase == Phase.Gate)
        {
            int near = NearestScanner(mine);
            if (near >= 0)
                text = hands[near] == MyId ? Texts.Get("tower.prompt.holding")
                    : hands[near] != 0 && palm != near ? Texts.Format("tower.prompt.taken", NameOf(hands[near]))
                    : palm == near ? Texts.Get("tower.prompt.holding") : Texts.Get("tower.prompt.palm");
        }
        if (text == null && able && NearShop(mine, out Vector3 shop))
        {
            text = Texts.Get("campaign.prompt.shop");
            if (InputFocus.TakesPlayInput() && interact != null && interact.WasPressedThisFrame()) ShopMenu.Open(mine, shop);
        }
        offering = text != null;
        screen.Prompt(ShopMenu.IsOpen ? null : text);
    }

    private static bool NearShop(PlayerMovement mine, out Vector3 shop)
    {
        for (int stretch = 0; stretch < TowerLayout.Segments; stretch++)
        {
            shop = TowerLayout.ShopAt(stretch);
            Vector3 apart = mine.transform.position - shop;
            if (Mathf.Abs(apart.y) < 1.5f && Flat(apart) <= ShopReach) return true;
        }
        shop = default;
        return false;
    }

    // Once the lanes are through, this PC's player, if alive and in no lane (left in the hall below, or outside), is
    // brought to the next hall; and anyone still not there when the gathering's time is up.
    private void Fetch(PlayerMovement mine)
    {
        if (phase != Phase.Gather) { fetched = false; return; }
        if (mine == null || !mine.IsInitialized || mine.IsDead || fetched || TowerLayout.IsLastSegment(segment)) return;
        int next = segment + 1;
        if (TowerLayout.InHall(mine.transform.position, next)) return;
        bool astray = TowerLayout.LaneAt(mine.transform.position, segment) < 0;
        if (!astray && (!counting || count > 0f)) return;
        fetched = true;
        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        mine.MoveTo(TowerLayout.HallSpot(next) + new Vector3(scatter.x, 0f, scatter.y));
    }

    // The last floor plays its song while its lanes are climbed and under the finale.
    private void PlayMusic()
    {
        bool last = TowerLayout.IsLastSegment(segment) && (phase == Phase.Lanes || phase == Phase.Finale);
        music.Want(last ? finaleSong : "", 0f);
        music.Tick();
    }
}
