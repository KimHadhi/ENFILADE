using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// TowerRun: the day's stealth lane and floor rules (the map gimmick cards "잠입 레인" and "오늘의 층 규칙", the latter built
// only from parts the game had: the user's note "발상은 좋은데 만들기 귀찮겠다"). One part of the class; the rest is in
// TowerRun.cs and the files beside it.
// - The stealth lane (TowerPlan.StealthLane, floors 1-9): its rooms hold their swarm calm and spread through the room
//   from the start, not let out of the pens. Reach its far end unseen and the way up opens (the calm ones are left
//   behind), paid the quiet bonus; seen (any of them woken: ZombieMind.Woke), its door stays shut and the swarm comes again
//   out of the pens, all of the room awake.
// - Blackout (TowerRule.Blackout): every PC turns that floor's room lights off until its lane is through it.
// - Sleeping floor (TowerRule.Sleeping): its rooms' swarms lie asleep where they are; cleared without one waking, the
//   quiet bonus.
// - Alarm wire (TowerRule.Wire): red wires across that floor's rooms at ankle height; walked through (not jumped) it
//   doubles the swarm of that lane's next room. The host watches every player's feet.
public sealed partial class TowerRun
{
    // A wire is crossed with the feet this low over its floor or lower (m): a jump clears it.
    private const float WireFeet = .35f;
    // How near a stealth room's far end someone must come to be through it (m).
    private const float ExitReach = 3f;

    // The host's word to every PC, a bit for each lane: got through its last room unseen (paid as it advances), seen in
    // its room now, walked through a wire of its room.
    private byte quietPass, seen, wired;
    private byte shownSeen, shownWired;
    // Every PC: the stretch's restarts the wires were laid for, and the room whose rule this PC's player was last told.
    private int wiresResets = -1, toldLevel = -1;
    // Every PC: the lights of each room (level, lane), for a blackout, and the wires it drew.
    private Dictionary<(int, int), List<Light>> roomLights;
    private readonly Dictionary<(int, int), List<GameObject>> wireLooks = new Dictionary<(int, int), List<GameObject>>();
    private int wiresFor = -1;
    // The host: where each player's feet were last frame, for a wire crossed between two frames.
    private readonly Dictionary<PlayerMovement, Vector3> lastFeet = new Dictionary<PlayerMovement, Vector3>();

    private void ListenRules() => ZombieMind.Woke += OnWoke;

    private void StopRules() => ZombieMind.Woke -= OnWoke;

    // ---- The host ----

    // A room whose swarm is laid out calm now (the stealth lane, a sleeping floor): every one placed at once over the room,
    // asleep or standing about.
    private bool BeginQuietRoom(int k, int level, int floor, Lane lane)
    {
        if (plan == null || !plan.Quiet(floor, k) || enemyPrefab == null) return false;
        lane.Quiet = true;
        lane.Unseen = true;
        lane.Stealth = k == plan.StealthLane;
        lane.Champion = false;
        lane.Toughness = plan.Of(floor).toughness;
        bool asleep = plan.RuleOf(floor) == TowerRule.Sleeping;
        var kinds = plan.SwarmOf(floor, k, lane.Players, crowdPerPlayer);
        if (lane.DoubleNext) { kinds.AddRange(plan.SwarmOf(floor, k, lane.Players, crowdPerPlayer)); lane.DoubleNext = false; }
        lane.Again.Clear();
        lane.Again.AddRange(kinds);
        bool fromEast = TowerLayout.EntersFromEast(level);
        for (int i = 0; i < kinds.Count; i++)
        {
            // Spread from a third of the way in to the far end, across the lane.
            float along = Random.Range(TowerLayout.RoomLength * .3f, TowerLayout.RoomLength - 3f);
            float x = fromEast ? TowerLayout.RoomLength - along : along;
            float z = Random.Range(TowerLayout.LaneMin(k) + 1.2f, TowerLayout.LaneMax(k) - 1.2f);
            if (!NavMesh.SamplePosition(new Vector3(x, TowerLayout.FloorY(level) + .3f, z), out NavMeshHit ground, 2f, NavMesh.AllAreas)) continue;
            var enemy = Make(kinds[i], ground.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), lane, k);
            if (enemy == null) continue;
            // Asleep on a sleeping floor; in the stealth lane most stand about, a few asleep.
            bool sleeps = asleep || Random.value < .3f;
            if (enemy.TryGetComponent(out ZombieMind mind))
            {
                if (sleeps) mind.Sleep();
                else { mind.Calm(); mind.FaceWhenCalm(Random.Range(0f, 360f)); }
            }
            if (sleeps && enemy.TryGetComponent(out ZombieBody body)) body.ForcedPose = ZombieBody.SleepPose;
            EnemyNetwork.SpawnInRoom(enemy);
            lane.Alive.Add(enemy);
        }
        quietPass = (byte)(quietPass & ~(1 << k));
        seen = (byte)(seen & ~(1 << k));
        return true;
    }

    // One of a room's zombies, as Release makes them (its kind, its toughness, its lane), not yet out to the room.
    private EnemyHealth Make(ZombieKind kind, Vector3 at, Quaternion facing, Lane lane, int k)
    {
        var container = RuntimeSceneHierarchy.GetGroup(gameObject.scene, RuntimeSceneHierarchy.Enemies, "Tower");
        var enemy = Instantiate(enemyPrefab, at, facing, container);
        if (enemy.TryGetComponent(out ZombieType type))
        {
            type.Set(kind);
            type.Toughen(lane.Toughness);
        }
        int stretch = segment;
        if (enemy.TryGetComponent(out EnemyTargeting targeting))
            targeting.Pen = someone => someone != null && TowerLayout.LaneAt(someone.transform.position, stretch) == k;
        return enemy;
    }

    // One of a quiet room's woke: the host's word that the room was seen. In the stealth lane its door stays shut, the
    // whole room wakes and the swarm comes again out of the pens.
    private void OnWoke(ZombieMind mind, PlayerMovement who)
    {
        if (!IsHost || mind == null || !mind.TryGetComponent(out EnemyHealth health)) return;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            var lane = lanes[k];
            if (!lane.Quiet || !lane.Unseen || !lane.Alive.Contains(health)) continue;
            lane.Unseen = false;
            seen = (byte)(seen | 1 << k);
            if (!lane.Stealth) return;
            int level = TowerLayout.FirstLevel(segment) + floors[k];
            float y = TowerLayout.FloorY(level);
            var room = new Bounds(new Vector3(TowerLayout.RoomLength * .5f, y + 2f, TowerLayout.LaneMid(k)),
                new Vector3(TowerLayout.RoomLength + 1f, 5f, TowerLayout.LaneWidth));
            ZombieMind.WakeWithin(room, who);
            foreach (var kind in lane.Again) lane.Due.Enqueue(kind);
            lane.NextSpawn = Time.time + .5f;
            return;
        }
    }

    // While a lane climbs or fights: a stealth room's far end reached unseen, and the wires of a wire floor. True when the
    // room is through (the caller moves the lane on).
    private bool TickRules(int k, int level)
    {
        var lane = lanes[k];
        int floor = TowerLayout.FloorOf(level);
        if (plan != null && plan.RuleOf(floor) == TowerRule.Wire && (wired & 1 << k) == 0) WatchWires(k, level, lane);
        if (states[k] != LaneState.Fighting || !lane.Quiet || !lane.Unseen || !lane.Stealth) return false;
        float far = TowerLayout.FarX(level), y = TowerLayout.FloorY(level);
        foreach (var someone in players)
        {
            Vector3 at = someone.transform.position;
            if (TowerLayout.LaneAt(at, segment) != k || at.y < y - .5f || at.y > y + 3f || Mathf.Abs(at.x - far) > ExitReach) continue;
            // Through unseen: the calm ones stay behind (taken away), and the lane is paid the quiet bonus as it advances.
            lane.Reset();
            quietPass = (byte)(quietPass | 1 << k);
            return true;
        }
        return false;
    }

    // A quiet room emptied with nobody seen (a sleeping floor's, every one taken down quietly): paid as quiet too.
    private void ClearedQuietly(int k)
    {
        var lane = lanes[k];
        if (lane.Quiet && lane.Unseen) quietPass = (byte)(quietPass | 1 << k);
        lane.Quiet = false;
    }

    private void WatchWires(int k, int level, Lane lane)
    {
        float y = TowerLayout.FloorY(level);
        foreach (var someone in players)
        {
            Vector3 feet = someone.transform.position;
            bool had = lastFeet.TryGetValue(someone, out Vector3 before);
            lastFeet[someone] = feet;
            if (!had || TowerLayout.LaneAt(feet, segment) != k || feet.y - y > WireFeet || feet.y < y - .5f) continue;
            foreach (float share in TowerLayout.WireShares)
            {
                float wire = TowerLayout.RoomLength * share;
                if ((before.x - wire) * (feet.x - wire) > 0f) continue;
                lane.DoubleNext = true;
                wired = (byte)(wired | 1 << k);
                return;
            }
        }
    }

    // A lane begins its next room: the wires and the seen of the last one are behind it.
    private void ForgetRoom(int k)
    {
        wired = (byte)(wired & ~(1 << k));
        seen = (byte)(seen & ~(1 << k));
    }

    // ---- Every PC ----

    // A floor's rule and the lane's quiet passes, shown: the blackout's lights, the wires, the alerts.
    private void ShowRules(PlayerMovement mine, int myLane)
    {
        if (plan == null) return;
        // The wires again for a new day, and after the stretch starts over (one walked through is back).
        if (wiresFor != plan.Seed || wiresResets != resets) MakeWires();
        Blackout();
        Tell(myLane);
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            int bit = 1 << k;
            int level = TowerLayout.FirstLevel(segment) + floors[k];
            if ((seen & bit) != 0 && (shownSeen & bit) == 0 && started)
            {
                if (k == myLane) AlertHUD.Danger(Texts.Get(plan.StealthLane == k ? "tower.alert.seen" : "tower.alert.woke"));
                else AlertHUD.Team(Texts.Format("tower.alert.seenlane", TowerHUD.Letter(k)), null, KitStyle.Accent);
            }
            if ((wired & bit) != 0 && (shownWired & bit) == 0 && started)
            {
                Wires(level, k, false);
                if (k == myLane) AlertHUD.Danger(Texts.Get("tower.alert.wire"));
            }
            if ((wired & bit) == 0 && (shownWired & bit) != 0) Wires(level, k, true);
        }
        shownSeen = seen;
        shownWired = wired;
    }

    // This PC's player's room begins: its floor's rule, and the stealth lane's way, said once.
    private void Tell(int myLane)
    {
        if (myLane < 0 || (phase != Phase.Lanes && phase != Phase.Boarding)) return;
        int level = TowerLayout.FirstLevel(segment) + floors[myLane];
        if (level == toldLevel || states[myLane] != LaneState.Fighting) return;
        toldLevel = level;
        int floor = TowerLayout.FloorOf(level);
        var rule = plan.RuleOf(floor);
        if (rule != TowerRule.None) AlertHUD.Self(Texts.Format("tower.alert.rule", floor, TowerPlan.RuleName(rule), Texts.Get("tower.rule." + RuleKey(rule) + ".line")));
        if (myLane == plan.StealthLane && plan.Quiet(floor, myLane)) AlertHUD.Self(Texts.Get("tower.alert.stealth"));
    }

    private static string RuleKey(TowerRule rule) => rule == TowerRule.Blackout ? "blackout" : rule == TowerRule.Sleeping ? "sleeping" : "wire";

    // Lane k got through a room of the stretch unseen: its own players get the quiet bonus (with the floor's pay).
    private void PayQuiet(int k)
    {
        if ((quietPass & 1 << k) == 0) return;
        CombatFeedHUD.Award(Texts.Get("tower.feed.quiet"), Mathf.Max(0, quietBonus));
    }

    // The rooms of a blackout floor are dark while the stretch they are in runs and their lane is not through them yet.
    private void Blackout()
    {
        if (roomLights == null) GatherRoomLights();
        foreach (var pair in roomLights)
        {
            var (level, k) = pair.Key;
            int floor = TowerLayout.FloorOf(level);
            bool dark = plan.RuleOf(floor) == TowerRule.Blackout && level >= TowerLayout.FirstLevel(segment) && level <= TowerLayout.LastLevel(segment)
                && phase != Phase.Gather && phase != Phase.Finale && states[k] != LaneState.Done
                && TowerLayout.FirstLevel(segment) + floors[k] <= level;
            foreach (var light in pair.Value) if (light != null && light.enabled == dark) light.enabled = !dark;
        }
    }

    // Every light of the tower's rooms, by the room it is in.
    private void GatherRoomLights()
    {
        roomLights = new Dictionary<(int, int), List<Light>>();
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional || !light.gameObject.scene.Equals(gameObject.scene)) continue;
            Vector3 at = light.transform.position;
            int level = Mathf.FloorToInt((at.y + .01f) / TowerLayout.Height);
            if (level < 1 || level >= TowerLayout.Levels || TowerLayout.IsHall(level)) continue;
            if (at.x < -.5f || at.x > TowerLayout.RoomLength + .5f) continue;
            int k = TowerLayout.LaneOfZ(at.z);
            if (k < 0) continue;
            if (!roomLights.TryGetValue((level, k), out var list)) roomLights[(level, k)] = list = new List<Light>();
            list.Add(light);
        }
    }

    // The day's wire floor: its rooms' wires shown (the builder laid them hidden in every room: TowerWire), the rest hidden.
    private void MakeWires()
    {
        wireLooks.Clear();
        wiresFor = plan.Seed;
        wiresResets = resets;
        foreach (var wire in FindObjectsByType<TowerWire>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool today = !TowerLayout.IsHall(wire.Level) && plan.RuleOf(TowerLayout.FloorOf(wire.Level)) == TowerRule.Wire;
            wire.gameObject.SetActive(today);
            if (!today) continue;
            if (!wireLooks.TryGetValue((wire.Level, wire.Lane), out var looks)) wireLooks[(wire.Level, wire.Lane)] = looks = new List<GameObject>();
            looks.Add(wire.gameObject);
        }
    }

    private void Wires(int level, int k, bool shown)
    {
        if (!wireLooks.TryGetValue((level, k), out var looks)) return;
        foreach (var look in looks) if (look != null) look.SetActive(shown);
    }
}
