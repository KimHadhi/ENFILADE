using UnityEngine;
using UnityEngine.AI;

// TowerRun: The host. One part of the class; the rest is in TowerRun.cs and the files beside it.
public sealed partial class TowerRun
{
    private void Run(float dt)
    {
        if (plan == null) return;
        switch (phase)
        {
            case Phase.Gate: RunGate(dt); break;
            case Phase.Boarding: RunBoarding(dt); break;
            case Phase.Lanes: RunLanes(); break;
            case Phase.Gather: RunGather(dt); break;
            case Phase.Finale:
                if (!movedOn && Time.time - finaleAt >= finaleSeconds + outroSeconds) MoveOn();
                break;
        }
    }

    private void BeginGate()
    {
        phase = Phase.Gate;
        counting = false;
        count = countSeconds;
        opened = 0;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            hands[k] = 0;
            states[k] = LaneState.Shut;
            floors[k] = lefts[k] = 0;
            lanes[k].Reset();
        }
    }

    // The scanners: the count starts with the first palm and stops if every palm comes off; at its end the held lanes open.
    private void RunGate(float dt)
    {
        ResolveHands();
        bool any = false;
        foreach (ulong hand in hands) any |= hand != 0;
        if (!any)
        {
            counting = false;
            count = countSeconds;
            return;
        }
        if (!counting) { counting = true; count = countSeconds; }
        count -= dt;
        if (EveryoneHolds() && count > everyoneSeconds) count = everyoneSeconds;
        if (count <= 0f) OpenLanes();
    }

    // Who holds which scanner: a palm that came off lets go, and a palm on a free one takes it (first come keeps it).
    private void ResolveHands()
    {
        for (int k = 0; k < TowerLayout.Lanes; k++)
            if (hands[k] != 0 && PalmOf(hands[k]) != k) hands[k] = 0;
        TakeHand(MyId, palm);
        foreach (var pair in palms) TakeHand(pair.Key + 1UL, PalmOf(pair.Key + 1UL));
    }

    private void TakeHand(ulong id, int lane)
    {
        if (lane < 0 || lane >= TowerLayout.Lanes || hands[lane] != 0) return;
        foreach (ulong hand in hands) if (hand == id) return;
        hands[lane] = id;
    }

    // The scanner a player's palm is on: this PC's own, or a guest's word if fresh and they really stand there.
    private int PalmOf(ulong id)
    {
        if (id == MyId) return palm;
        if (!palms.TryGetValue(id - 1UL, out var word) || Time.unscaledTime - word.at > PalmStale || word.lane < 0) return -1;
        var who = PlayerById(id);
        var scanner = ScannerOf(segment, word.lane);
        if (who == null || who.IsDead || who.IsDowned || scanner == null) return -1;
        return Flat(who.transform.position - scanner.Palm) <= palmReach + PalmSlack ? word.lane : -1;
    }

    private bool EveryoneHolds()
    {
        if (players.Count == 0) return false;
        foreach (var someone in players)
        {
            ulong id = IdOf(someone);
            bool holds = false;
            foreach (ulong hand in hands) holds |= hand != 0 && hand == id;
            if (!holds) return false;
        }
        return true;
    }

    private void OpenLanes()
    {
        opened = 0;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if (hands[k] == 0) continue;
            opened |= 1 << k;
            states[k] = LaneState.Boarding;
            floors[k] = lefts[k] = 0;
        }
        phase = Phase.Boarding;
        counting = true;
        count = boardingSeconds;
    }

    // The doors stand open until everyone is through one or the time is up; then they shut and the lanes begin.
    private void RunBoarding(float dt)
    {
        count -= dt;
        bool allIn = players.Count > 0;
        foreach (var someone in players)
        {
            int lane = TowerLayout.LaneAt(someone.transform.position, segment);
            if (lane < 0 || (opened & (1 << lane)) == 0) { allIn = false; break; }
        }
        if (count > 0f && !allIn) return;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if ((opened & (1 << k)) == 0) continue;
            int inside = PlayersIn(k);
            states[k] = inside > 0 ? LaneState.Climbing : LaneState.Vacant;
            lanes[k].Players = Mathf.Max(1, inside);
        }
        phase = Phase.Lanes;
        counting = false;
        count = 0f;
    }

    private int PlayersIn(int lane)
    {
        int inside = 0;
        foreach (var someone in players) if (TowerLayout.LaneAt(someone.transform.position, segment) == lane) inside++;
        return inside;
    }

    private void RunLanes()
    {
        bool open = false, through = false;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if (states[k] == LaneState.Climbing || states[k] == LaneState.Fighting)
            {
                TickLane(k);
                if (states[k] == LaneState.Climbing || states[k] == LaneState.Fighting) open = true;
            }
            if (states[k] == LaneState.Done) through = true;
        }
        if (open) return;
        // Every lane is through or out. Through, the next hall (or ENFILADED!! on the tower's last floor, on the frame of
        // its last kill); every one out with someone still standing in the hall, the stretch starts over.
        if (!through) StartOver();
        else if (TowerLayout.IsLastSegment(segment)) BeginFinale();
        else
        {
            phase = Phase.Gather;
            counting = true;
            count = gatherSeconds;
        }
    }

    private void TickLane(int k)
    {
        var lane = lanes[k];
        int level = TowerLayout.FirstLevel(segment) + floors[k];
        if (PlayersIn(k) == 0) { Fail(k); return; }
        if (states[k] == LaneState.Climbing)
        {
            foreach (var someone in players)
                if (TowerLayout.InRoom(someone.transform.position, level, k, enterPast)) { BeginRoom(k, level); break; }
            return;
        }
        lane.Alive.RemoveAll(enemy => enemy == null || !enemy.IsAlive);
        Release(k, level);
        int left = Mathf.Clamp(lane.Due.Count + lane.Alive.Count, 0, 255);
        if (left != lefts[k]) lane.Changed = Time.time;
        lefts[k] = left;
        // The last few out and none of them down for this long: one stuck out of reach can't hold the lane for ever.
        if (lane.Due.Count == 0 && lane.Alive.Count > 0 && Time.time - lane.Changed > StuckSeconds) { lane.Reset(); lefts[k] = 0; }
        if (lane.Due.Count > 0 || lane.Alive.Count > 0) return;
        // The room is through: on to the next floor, or the stretch is done for this lane.
        if (floors[k] + 1 < TowerLayout.FloorsIn(segment)) { floors[k]++; states[k] = LaneState.Climbing; }
        else states[k] = LaneState.Done;
    }

    // Someone came into the room: its door shuts behind them (TowerDoor waits while anyone is in the doorway) and the
    // room's trial is let out.
    private void BeginRoom(int k, int level)
    {
        var lane = lanes[k];
        lane.Reset();
        int floor = TowerLayout.FloorOf(level);
        var row = plan.Of(floor);
        lane.Champion = plan.TrialOf(floor, k) == TowerTrial.Champion;
        if (lane.Champion)
        {
            lane.Due.Enqueue(plan.ChampionOf(floor, k));
            lane.Toughness = row.championToughness * (1f + championPerPlayer * (lane.Players - 1));
        }
        else
        {
            foreach (var kind in plan.SwarmOf(floor, k, lane.Players, crowdPerPlayer)) lane.Due.Enqueue(kind);
            lane.Toughness = row.toughness;
        }
        lane.NextSpawn = Time.time + .5f;
        lane.Changed = Time.time;
        states[k] = LaneState.Fighting;
        lefts[k] = lane.Due.Count;
    }

    // One out of the pens at a time, as long as the room doesn't hold its most already.
    private void Release(int k, int level)
    {
        var lane = lanes[k];
        if (lane.Due.Count == 0 || lane.Alive.Count >= mostAlive || Time.time < lane.NextSpawn || enemyPrefab == null) return;
        lane.NextSpawn = Time.time + spawnEvery;
        Vector3 wanted = lane.Champion ? TowerLayout.ChampionAt(level, k) : TowerLayout.PenAt(level, k, lane.Spot++ % 6);
        if (!NavMesh.SamplePosition(wanted + Vector3.up * .3f, out NavMeshHit ground, 2.5f, NavMesh.AllAreas)) return;
        var kind = lane.Due.Dequeue();
        var container = RuntimeSceneHierarchy.GetGroup(gameObject.scene, RuntimeSceneHierarchy.Enemies, "Tower");
        var facing = Quaternion.LookRotation(TowerLayout.EntersFromEast(level) ? Vector3.right : Vector3.left, Vector3.up);
        var enemy = Instantiate(enemyPrefab, ground.position, facing, container);
        // Set up before it goes out to the room, so its copies are born what it is.
        if (enemy.TryGetComponent(out ZombieType type))
        {
            type.Set(kind);
            type.Toughen(lane.Toughness);
        }
        if (lane.Champion) enemy.SetRank(EnemyRank.Elite);
        int stretch = segment;
        if (enemy.TryGetComponent(out EnemyTargeting targeting))
            targeting.Pen = someone => someone != null && TowerLayout.LaneAt(someone.transform.position, stretch) == k;
        EnemyNetwork.SpawnInRoom(enemy);
        lane.Alive.Add(enemy);
    }

    // Nobody left alive in it: the lane is out, and what was left in its room goes.
    private void Fail(int k)
    {
        lanes[k].Reset();
        states[k] = LaneState.Failed;
        lefts[k] = 0;
    }

    private static void Remove(EnemyHealth enemy)
    {
        if (enemy == null) return;
        enemy.gameObject.SetActive(false);
        Destroy(enemy.gameObject);
    }

    // The lanes are through: everyone in the next hall (the dead come back there, the rest walk up or are brought) and the
    // scanners rise for the next stretch.
    private void RunGather(float dt)
    {
        count = Mathf.Max(0f, count - dt);
        int next = segment + 1;
        if (players.Count == 0) return;
        foreach (var someone in players) if (!TowerLayout.InHall(someone.transform.position, next)) return;
        segment = next;
        BeginGate();
    }

    // A wipe, or every lane out: back to this stretch's scanners, with nothing left in its rooms.
    private void StartOver()
    {
        resets = (resets + 1) & 255;
        music.Stop();
        BeginGate();
    }

    private void BeginFinale()
    {
        phase = Phase.Finale;
        finaleAt = Time.time;
        counting = false;
        foreach (var lane in lanes) lane.Reset();
    }

    // Back to the hub with the results, the map table keeping the tower chosen. With no hub, the tower again.
    private void MoveOn()
    {
        movedOn = true;
        string scene = gameObject.scene.name;
        if (HubFlow.Return(scene)) return;
        if (GameSession.IsOnline) GameSession.ChangeMap(scene);
        else SceneLoad.Go(scene);
    }
}
