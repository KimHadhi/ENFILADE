#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of the ammunition rules, without playing: every gun of the game's list is put through the rules as a
/// player would put it (rounds fired, a reload begun and stopped at any moment of it, a top-up), on the very classes the
/// game runs (WeaponRuntime, PlayerWeaponSlots), and what its rounds then are is held against what the rule says they must
/// be. Logs/ammo-check.txt has a line for every rule with how many guns kept it, and a FAIL line with the numbers for each
/// one that did not. Whoever changes a rule here can't play the game, so the rules are made to say for themselves whether
/// they hold (the user, 2026-11-18, heading the whole list: "문제를 근본적으로 생각하고 뿌리를 고치며").
/// It runs by itself once after every script reload and after a play (a few milliseconds: no scene, nothing drawn), from
/// ENFILADE → 총 → 탄약 규칙 점검, and when the file Temp/enfilade-ammocheck.request is made (a tool outside Unity asking,
/// as RefreshOnRequest is asked). It lives beside WeaponRuntime because it checks with the game's own classes.
/// </summary>
[InitializeOnLoad]
internal static class AmmoCheck
{
    private const string Request = "Temp/enfilade-ammocheck.request", ReportPath = "Logs/ammo-check.txt";
    // The clock the rules are stepped by (s): finer than any frame, so a moment is never stepped over by more than this.
    private const float Step = 1f / 240f;

    // It keeps itself alive on the editor's update.
    static AmmoCheck() => new CheckOnRequest(Request, Run);

    // What the checks found: a line per rule, the failures spelt out.
    private sealed class Report
    {
        public readonly StringBuilder Text = new StringBuilder();
        public int Failed, Rules;
        private readonly List<string> failures = new List<string>();
        private string rule;
        private int kept;

        public void Rule(string name)
        {
            Close();
            rule = name;
            Rules++;
        }

        /// <summary>One gun under the rule now open: kept, or not, with the numbers that show it.</summary>
        public void Expect(bool holds, WeaponData gun, string numbers)
        {
            if (holds) { kept++; return; }
            Failed++;
            failures.Add("  FAIL " + (gun != null ? gun.name : "?") + ": " + numbers);
        }

        public void Close()
        {
            if (rule == null) return;
            Text.AppendLine((failures.Count == 0 ? "ok   " : "FAIL ") + rule + "  (" + kept + " kept" + (failures.Count > 0 ? ", " + failures.Count + " broken" : "") + ")");
            foreach (string failure in failures) Text.AppendLine(failure);
            failures.Clear();
            kept = 0;
            rule = null;
        }
    }

    [MenuItem("ENFILADE/총/탄약 규칙 점검")]
    private static void Run()
    {
        var report = new Report();
        var guns = new List<WeaponData>();
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && entry.weapon.Category == WeaponCategory.Ranged && !guns.Contains(entry.weapon)) guns.Add(entry.weapon);
        try
        {
            if (guns.Count == 0) report.Text.AppendLine("FAIL no guns in Resources/Combat/FPSBody: nothing was checked.");
            else
            {
                Reloads(guns, report);
                TopUps(guns, report);
                SlotTopUps(guns, report);
                GunsAsThings(guns, report);
            }
        }
        catch (Exception problem)
        {
            report.Close();
            report.Failed++;
            report.Text.AppendLine("FAIL the check itself stopped: " + problem);
        }
        report.Close();
        int measured = 0;
        foreach (var gun in guns) if (ReloadMoments.TryGet(gun, false, out _)) measured++;
        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ammunition rules check, {guns.Count} guns, {report.Rules} rules: "
            + (report.Failed == 0 && guns.Count > 0 ? "ALL KEPT" : report.Failed + " BROKEN"));
        text.AppendLine("A gun's rounds are written magazine/size +reserve. Reload steps come from the clips of " + measured + " of the guns "
            + "(measured in a play of this editor session); the rest go by the shares the rules fall back on.");
        text.AppendLine();
        text.Append(report.Text);
        text.AppendLine();
        Steps(guns, text);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text.ToString(), new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("AmmoCheck: " + ReportPath + " could not be written: " + error.Message);
        }
        if (report.Failed > 0 || guns.Count == 0) Debug.LogWarning($"AmmoCheck: {report.Failed} ammunition rules broken, see {ReportPath}.");
    }

    // ---- The rules ----

    // A reload (WeaponRuntime.Advance, CutShort): its rounds move as its hands do it, the gun holds what the HUD says at
    // every moment, a cut moves nothing and the next reload goes on from it.
    private static void Reloads(List<WeaponData> guns, Report report)
    {
        report.Rule("reload: no round is made or lost at any moment of it (magazine + reserve stays the same), and it ends full");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
            {
                var gun = Reloading(data, empty, out int reserve);
                if (gun == null) continue;
                int total = gun.Ammo + gun.ReserveAmmo, least = total, most = total;
                for (int i = 0; i < 100000 && gun.IsReloading; i++)
                {
                    gun.Tick(Step, default);
                    least = Mathf.Min(least, gun.Ammo + gun.ReserveAmmo);
                    most = Mathf.Max(most, gun.Ammo + gun.ReserveAmmo);
                }
                report.Expect(least == total && most == total && Ready(gun, data.MagazineSize, total - data.MagazineSize), data,
                    $"{Kind(empty)} reload of {total} rounds held between {least} and {most}, ended {Rounds(gun)}{Doing(gun)}");
            }

        report.Rule("reload: the gun holds none while its magazine is out, and the new magazine's rounds from the moment it is seated, through the rest of the clip");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
            {
                if (data.ShellReload) continue;
                var gun = Reloading(data, empty, out int reserve);
                if (gun == null) continue;
                int size = data.MagazineSize;
                var steps = ReloadSteps.Of(data, empty);
                float whole = gun.ReloadDuration;
                // Up to just before the seating, then just past it.
                Pass(gun, whole * steps.MagIn - 2f * Step);
                bool outLong = whole * (steps.MagIn - steps.MagOut) > 6f * Step;
                bool before = !outLong || (gun.Ammo == 0 && gun.MagazineOut && gun.IsReloading);
                string was = Rounds(gun) + Doing(gun);
                Pass(gun, 4f * Step);
                bool tail = whole * (1f - steps.MagIn) > 6f * Step;
                bool boltLeft = whole * (steps.Loaded - steps.MagIn) > 6f * Step;
                bool seated = gun.Ammo == size && !gun.MagazineOut && (!tail || gun.IsReloading) && (!boltLeft || gun.ChamberDue);
                string then = Rounds(gun) + Doing(gun);
                bool stayed = true;
                for (int i = 0; i < 100000 && gun.IsReloading; i++)
                {
                    gun.Tick(Step, default);
                    stayed &= gun.Ammo == size;
                }
                report.Expect(before && seated && stayed && Ready(gun, size, reserve + (empty ? 0 : Mathf.Max(1, size / 3)) - size), data,
                    $"{Kind(empty)} reload, seated at {steps.MagIn:0.00} of {whole:0.00} s: just before {was}, just after {then}, ended {Rounds(gun)}{Doing(gun)}"
                    + (stayed ? "" : ", the count changed after the seating"));
            }

        report.Rule("reload cut short at any moment: the cut moves no round, and the next reload goes on from it (no step twice) to a full gun");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
                for (int moment = 1; moment <= 9; moment++)
                {
                    var gun = Reloading(data, empty, out int reserve);
                    if (gun == null) continue;
                    int size = data.MagazineSize, total = gun.Ammo + gun.ReserveAmmo;
                    var steps = ReloadSteps.Of(data, empty);
                    Pass(gun, WholeReload(gun) * moment / 10f);
                    int ammoThen = gun.Ammo, reserveThen = gun.ReserveAmmo;
                    gun.Interrupt();
                    bool still = gun.Ammo == ammoThen && gun.ReserveAmmo == reserveThen && !gun.IsReloading;
                    string cut = Rounds(gun) + Doing(gun);
                    // From where must the next one go on: with the new magazine at the pouch, from the seating, or afresh.
                    float from = data.ShellReload ? 0f : gun.MagazineOut ? steps.Pouch : gun.ChamberDue ? steps.MagIn : 0f;
                    bool wanted = gun.ResumeDue || gun.Ammo < size;
                    gun.Tick(Step, new WeaponInputFrame(false, false, true));
                    bool began = gun.IsReloading == wanted && (!wanted || Mathf.Abs(gun.ReloadStartShare - from) < .0005f);
                    float started = gun.ReloadStartShare;
                    for (int i = 0; i < 100000 && gun.IsReloading; i++) gun.Tick(Step, default);
                    report.Expect(still && began && Ready(gun, size, total - size), data,
                        $"{Kind(empty)} reload cut {moment}/10 through left {cut}{(still ? "" : " (the cut moved rounds)")}; the next "
                        + (wanted ? $"began at {started:0.00}, should at {from:0.00}" : "was not needed") + $"; ended {Rounds(gun)}{Doing(gun)}");
                }
    }

    // A top-up (WeaponRuntime.TopUp: a Point's Gun Kata, the room's slow motion): the magazine full, the reserve exactly
    // as it was the moment before, no reload left under way or to go on with, whatever the gun was doing.
    private static void TopUps(List<WeaponData> guns, Report report)
    {
        report.Rule("top-up: a part-empty magazine comes up full and the reserve is not touched");
        foreach (var data in guns)
        {
            int size = data.MagazineSize, reserve = 2 * size + 3;
            var gun = new WeaponRuntime(data, size / 3, reserve);
            bool changed = gun.TopUp();
            report.Expect(changed && Ready(gun, size, reserve), data, $"from {size / 3}/{size} +{reserve} came {Rounds(gun)}{Doing(gun)}");
        }

        report.Rule("top-up: a full gun is left alone");
        foreach (var data in guns)
        {
            int size = data.MagazineSize, reserve = size + 1;
            var gun = new WeaponRuntime(data, size, reserve);
            bool changed = gun.TopUp();
            report.Expect(!changed && Ready(gun, size, reserve), data, $"from {size}/{size} +{reserve} came {Rounds(gun)}{Doing(gun)}, changed {changed}");
        }

        report.Rule("top-up at any moment of a reload: full, the reserve as it was that moment, the reload over");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
                for (int moment = 1; moment <= 9; moment++)
                {
                    var gun = Reloading(data, empty, out int reserve);
                    if (gun == null) continue;
                    Pass(gun, WholeReload(gun) * moment / 10f);
                    int ammoThen = gun.Ammo, reserveThen = gun.ReserveAmmo;
                    gun.TopUp();
                    report.Expect(Ready(gun, data.MagazineSize, reserveThen), data,
                        $"{(empty ? "empty" : "tactical")} reload {moment}/10 through: {ammoThen}/{data.MagazineSize} +{reserveThen} came {Rounds(gun)}{Doing(gun)}");
                }

        report.Rule("top-up after a reload cut short at any moment: full, the reserve as the cut left it, nothing left to go on with");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
                for (int moment = 1; moment <= 9; moment++)
                {
                    var gun = Reloading(data, empty, out int reserve);
                    if (gun == null) continue;
                    Pass(gun, WholeReload(gun) * moment / 10f);
                    gun.Interrupt();
                    int ammoThen = gun.Ammo, reserveThen = gun.ReserveAmmo;
                    gun.TopUp();
                    report.Expect(Ready(gun, data.MagazineSize, reserveThen), data,
                        $"{(empty ? "empty" : "tactical")} reload cut {moment}/10 through: {ammoThen}/{data.MagazineSize} +{reserveThen} came {Rounds(gun)}{Doing(gun)}");
                }
    }

    // The room's slow motion on a player's slots (PlayerWeaponSlots.TopUpMagazines): both guns full, in hand or put away,
    // their reserves untouched, and a gun that keeps its own rounds (a Schießhund's Desperado) left as it was.
    private static void SlotTopUps(List<WeaponData> guns, Report report)
    {
        report.Rule("slow motion's fill: both gun slots full, reserves untouched, a gun loaded only by bounties left alone");
        WeaponData own = guns.Find(gun => gun.name == "Desperado") ?? guns[guns.Count - 1];
        var others = guns.FindAll(gun => gun != own);
        if (others.Count < 2) { report.Expect(false, own, "fewer than three guns in the list: not checked"); return; }
        var host = new GameObject("Ammo check") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        try
        {
            var slots = host.AddComponent<PlayerWeaponSlots>();
            for (int pair = 0; pair + 1 < others.Count; pair += 2)
            {
                WeaponData first = others[pair], second = others[pair + 1];
                slots.SpecialHoldsGun = true;
                bool set = slots.TrySetWeapon(0, first) & slots.TrySetWeapon(1, second) & slots.TrySetWeapon(3, own);
                if (!set) { report.Expect(false, first, $"the slots would not take {first.name}, {second.name} and {own.name}"); continue; }
                WeaponRuntime a = slots.GetRuntime(0), b = slots.GetRuntime(1), kept = slots.GetRuntime(3);
                Fire(a, 2);
                Fire(b, 1);
                kept.LoadOwnRounds(1);
                int reserveA = a.ReserveAmmo, reserveB = b.ReserveAmmo;
                string before = $"{first.name} {Rounds(a)}, {second.name} {Rounds(b)}, {own.name} {Rounds(kept)}";
                int filled = slots.TopUpMagazines();
                report.Expect(filled == 2 && Ready(a, first.MagazineSize, reserveA) && Ready(b, second.MagazineSize, reserveB)
                    && kept.Ammo == Mathf.Min(1, own.MagazineSize) && kept.ReserveAmmo == 0, first,
                    $"{before} came {Rounds(a)}, {Rounds(b)}, {Rounds(kept)}; filled {filled}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    // A gun is a thing with its rounds in it, and the reserve is its holder's (PlayerWeaponSlots.Put, Drop, TakeRounds,
    // ReserveOf): put down and taken up again nothing is made or lost, an empty gun is still empty, a swap leaves the
    // reserve of the gun put down with the player, the rounds lying with a laid-out gun are handed over once, and a gun
    // of a kind already held gives its rounds to that kind's reserve alone, no further than its cap.
    private static void GunsAsThings(List<WeaponData> guns, Report report)
    {
        var plain = guns.FindAll(gun => gun.name != "Desperado");
        if (plain.Count < 3) { report.Rule("a gun put down and taken up"); report.Expect(false, guns[0], "fewer than three guns in the list: not checked"); return; }
        var made = new List<GameObject>();
        try
        {
            var slots = NewPlayer(made);
            report.Rule("a gun put down and taken up again holds what it held, and its reserve is as it was (rounds earned past the cap too)");
            foreach (var data in plain)
            {
                Fresh(slots);
                slots.TrySetWeapon(0, data);
                var gun = slots.GetRuntime(0);
                Fire(gun, 2);
                gun.AddReserve(7);
                int rounds = gun.Ammo, reserve = gun.ReserveAmmo;
                bool dropped = slots.Drop(0, out GunState lying);
                bool gone = slots.GetWeapon(0) == null && slots.ReserveOf(data).Rounds == reserve;
                bool taken = slots.Put(0, lying, out GunState? before);
                var back = slots.GetRuntime(0);
                report.Expect(dropped && gone && taken && before == null && lying.Rounds == rounds && lying.Spare == 0 && back != null
                    && back.Ammo == rounds && back.ReserveAmmo == reserve, data,
                    $"held {rounds}/{data.MagazineSize} +{reserve}; lay with {lying.Rounds} in it and {lying.Spare} with it; taken up again {(back != null ? Rounds(back) : "nothing")}");
            }

            slots = NewPlayer(made);
            report.Rule("an empty gun put down and taken up again is still empty, with no reserve made for it");
            foreach (var data in plain)
            {
                Fresh(slots);
                slots.TrySetWeapon(0, data);
                var gun = slots.GetRuntime(0);
                slots.ReserveOf(data).Rounds = 0;
                for (int i = 0; i < 400 && gun.Ammo > 0; i++) Fire(gun, 1);
                bool dropped = slots.Drop(0, out GunState lying);
                bool taken = slots.Put(0, lying, out _);
                var back = slots.GetRuntime(0);
                report.Expect(dropped && taken && back != null && back.Ammo == 0 && back.ReserveAmmo == 0, data,
                    $"emptied, put down with {lying.Rounds} in it, taken up again {(back != null ? Rounds(back) : "nothing")}");
            }

            slots = NewPlayer(made);
            report.Rule("a swap with a gun on the ground: the gun put down keeps its rounds, its reserve stays with the player, the gun taken brings only what lay with it");
            for (int i = 0; i + 1 < plain.Count; i++)
            {
                WeaponData mine = plain[i], found = plain[i + 1];
                Fresh(slots);
                slots.TrySetWeapon(0, mine);
                var gun = slots.GetRuntime(0);
                Fire(gun, 3);
                gun.AddReserve(11);
                int rounds = gun.Ammo, reserve = gun.ReserveAmmo;
                // A gun the map laid out: 3 rounds short of full, 9 lying with it.
                found.BindTable();
                int inIt = Mathf.Max(0, found.MagazineSize - 3);
                var laid = new GunState(found, inIt, 9);
                bool swapped = slots.Put(0, laid, out GunState? before);
                var now = slots.GetRuntime(0);
                bool put = before is GunState old && old.Weapon == mine && old.Rounds == rounds && old.Spare == 0;
                bool kept = slots.ReserveOf(mine).Rounds == reserve;
                bool brought = now != null && now.Data == found && now.Ammo == inIt && now.ReserveAmmo == 9;
                // And back again: the first gun as it was put down, the second leaving with nothing lying with it.
                bool again = before is GunState mineBack && slots.Put(0, mineBack, out GunState? second) && second is GunState foundBack
                    && foundBack.Rounds == inIt && foundBack.Spare == 0 && slots.GetRuntime(0).Ammo == rounds && slots.GetRuntime(0).ReserveAmmo == reserve
                    && slots.ReserveOf(found).Rounds == 9;
                report.Expect(swapped && put && kept && brought && again, mine,
                    $"{mine.name} {rounds}/{mine.MagazineSize} +{reserve} for {found.name} with {inIt} in it and 9 with it: put down right {put}, "
                    + $"its reserve kept {kept}, the new gun {(now != null ? Rounds(now) : "nothing")}, swapped back right {again}");
            }

            slots = NewPlayer(made);
            report.Rule("a gun of a kind already held gives its rounds to that kind's reserve only, up to its cap, and stays with the rest; no other gun is filled");
            for (int i = 0; i + 1 < plain.Count; i++)
            {
                WeaponData mine = plain[i], other = plain[i + 1];
                Fresh(slots);
                slots.TrySetWeapon(0, mine);
                slots.TrySetWeapon(1, other);
                WeaponRuntime gun = slots.GetRuntime(0), second = slots.GetRuntime(1);
                Fire(gun, 1);
                Fire(second, 1);
                int cap = slots.ReserveCap(0), size = mine.MagazineSize;
                // Room for 5 rounds; the gun on the ground has its magazine and 4 lying with it.
                slots.ReserveOf(mine).Rounds = Mathf.Max(0, cap - 5);
                int reserve = gun.ReserveAmmo, room = cap - reserve, rounds = gun.Ammo, otherRounds = second.Ammo, otherReserve = second.ReserveAmmo;
                var lying = new GunState(mine, size, 4);
                int would = slots.RoomFor(0, lying);
                int taken = slots.TakeRounds(0, lying, out GunState left);
                int should = Mathf.Min(room, size + 4);
                report.Expect(would == should && taken == should && gun.ReserveAmmo == reserve + should && gun.Ammo == rounds
                    && left.Rounds + left.Spare == size + 4 - should && left.Spare == Mathf.Max(0, 4 - should)
                    && second.Ammo == otherRounds && second.ReserveAmmo == otherReserve
                    // The reserve is at its cap now: a second gun of the kind gives nothing more.
                    && slots.TakeRounds(0, new GunState(mine, size, 4), out _) == 0 && gun.ReserveAmmo == cap, mine,
                    $"room for {room}: took {taken} (should {should}), reserve {reserve} -> {gun.ReserveAmmo} of {cap}, left {left.Rounds} in it and {left.Spare} with it; "
                    + $"{other.name} {otherRounds} +{otherReserve} -> {second.Ammo} +{second.ReserveAmmo}");
            }

            slots = NewPlayer(made);
            report.Rule("a new gun (the shop's, a kit's) comes full with its reserve at the cap, and a second one of its kind adds nothing to it");
            foreach (var data in plain)
            {
                Fresh(slots);
                slots.TrySetWeapon(0, data);
                var gun = slots.GetRuntime(0);
                int cap = slots.ReserveCap(0);
                bool full = gun.Ammo == data.MagazineSize && gun.ReserveAmmo == cap;
                gun.AddReserve(5);
                slots.TrySetWeapon(0, data);
                var again = slots.GetRuntime(0);
                report.Expect(full && again.Ammo == data.MagazineSize && again.ReserveAmmo == cap + 5, data,
                    $"first {data.MagazineSize}/{data.MagazineSize} +{cap} was {full}; with 5 more carried a second came {Rounds(again)} (should be +{cap + 5})");
            }

            slots = NewPlayer(made);
            report.Rule("a gun put down with its magazine out lies so, and whoever takes it goes on with the reload from the pouch");
            foreach (var data in plain)
            {
                if (data.ShellReload || data.MagazineSize < 2) continue;
                Fresh(slots);
                slots.TrySetWeapon(0, data);
                var gun = slots.GetRuntime(0);
                Fire(gun, 1);
                gun.Tick(Step, new WeaponInputFrame(false, false, true));
                var steps = ReloadSteps.Of(data, gun.IsEmptyReload);
                Pass(gun, gun.ReloadDuration * (steps.MagOut + steps.MagIn) * .5f);
                bool outNow = gun.MagazineOut || steps.MagIn - steps.MagOut < .02f;
                int total = gun.Ammo + gun.ReserveAmmo;
                slots.Drop(0, out GunState lying);
                slots.Put(0, lying, out _);
                var back = slots.GetRuntime(0);
                bool same = back.MagazineOut == lying.MagazineOut && back.Ammo + back.ReserveAmmo == total;
                back.Tick(Step, new WeaponInputFrame(false, false, true));
                bool from = !lying.MagazineOut || Mathf.Abs(back.ReloadStartShare - steps.Pouch) < .0005f;
                for (int i = 0; i < 100000 && back.IsReloading; i++) back.Tick(Step, default);
                report.Expect(outNow && same && from && Ready(back, data.MagazineSize, total - data.MagazineSize), data,
                    $"put down mid-reload (magazine out {lying.MagazineOut}) with {lying.Rounds} in it; taken up {(same ? "as it lay" : "CHANGED")}, "
                    + $"reload went on from {back.ReloadStartShare:0.00} (pouch {steps.Pouch:0.00}), ended {Rounds(back)}{Doing(back)}");
            }
        }
        finally
        {
            foreach (var host in made) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    // A player's slots that hold nothing and carry nothing, on an object that never wakes (no scene, no play).
    private static PlayerWeaponSlots NewPlayer(List<GameObject> made)
    {
        var host = new GameObject("Ammo check") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        made.Add(host);
        return host.AddComponent<PlayerWeaponSlots>();
    }

    // The slots emptied of their guns; what the player carries for each kind stays, as it would.
    private static void Fresh(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) slots.TrySetWeapon(i, null);
    }

    // ---- What the rules are put through ----

    // A gun whose reload has just begun, from a part-empty magazine or an empty one; null when that gun can't begin one
    // (a magazine of one round is never part-empty).
    private static WeaponRuntime Reloading(WeaponData data, bool empty, out int reserve)
    {
        int size = data.MagazineSize;
        reserve = 2 * size + 3;
        int rounds = empty ? 0 : Mathf.Max(1, size / 3);
        if (rounds >= size) return null;
        var gun = new WeaponRuntime(data, rounds, reserve);
        gun.Tick(Step, new WeaponInputFrame(false, false, true));
        return gun.IsReloading ? gun : null;
    }

    // How long the reload just begun takes from here to its end (s): a magazine's one clip, or a shell reload's start,
    // one shell after another and its end.
    private static float WholeReload(WeaponRuntime gun)
    {
        var data = gun.Data;
        if (!data.ShellReload) return gun.ReloadRemaining;
        int shells = Mathf.Min(data.MagazineSize - gun.Ammo, gun.ReserveAmmo);
        return gun.ReloadRemaining + shells * data.ShellInsertDuration + data.ShellEndDuration;
    }

    // The rules run for this long with no hand on the gun.
    private static void Pass(WeaponRuntime gun, float seconds)
    {
        for (float left = seconds; left > 0f; left -= Step) gun.Tick(Mathf.Min(Step, left), default);
    }

    // The trigger pulled this many times, a second apart (a burst gun fires its burst out over them).
    private static void Fire(WeaponRuntime gun, int shots)
    {
        for (int i = 0; i < shots; i++)
        {
            gun.Tick(1f, new WeaponInputFrame(true, false));
            gun.Tick(1f, default);
        }
    }

    // Full, with this reserve, nothing under way and nothing left to do.
    private static bool Ready(WeaponRuntime gun, int size, int reserve)
        => gun.Ammo == size && gun.ReserveAmmo == reserve && !gun.IsReloading && !gun.ResumeDue;

    private static string Rounds(WeaponRuntime gun) => $"{gun.Ammo}/{gun.Data.MagazineSize} +{gun.ReserveAmmo}";

    private static string Kind(bool empty) => empty ? "empty" : "tactical";

    // Every magazine gun's reload steps as the rules read them now, for whoever reads the report: when the rounds are in,
    // and how long the clip goes on after the gun is loaded (the time the count used to wait).
    private static void Steps(List<WeaponData> guns, StringBuilder text)
    {
        text.AppendLine("== reload steps as the rules read them (ReloadSteps.Of): seconds into the reload; 'after' is how long its clip goes on once the gun is loaded");
        foreach (var data in guns)
        {
            if (data.ShellReload) { text.AppendLine($"{data.name,-16} shell reload: each shell counts as its own step ends"); continue; }
            foreach (bool empty in new[] { false, true })
            {
                var steps = ReloadSteps.Of(data, empty);
                float whole = empty ? data.EmptyReloadDuration : data.ReloadDuration;
                text.AppendLine($"{data.name,-16} {Kind(empty),-8} {whole,5:0.00} s: magazine out {whole * steps.MagOut,5:0.00}, at the pouch {whole * steps.Pouch,5:0.00}, "
                    + $"in {whole * steps.MagIn,5:0.00}, loaded {whole * steps.Loaded,5:0.00}, after {whole * (1f - steps.Loaded),5:0.00}"
                    + (steps.Measured ? "" : "  (not measured: stand-in shares)"));
            }
        }
    }

    private static string Doing(WeaponRuntime gun)
        => (gun.IsReloading ? " still reloading" : "") + (gun.MagazineOut ? " magazine out" : "") + (gun.ChamberDue ? " bolt due" : "");
}
#endif
