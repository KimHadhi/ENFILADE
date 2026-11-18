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
                // What a gun takes and carries is the table's (Weapons.csv), read before any rule asks.
                foreach (var gun in guns) gun.BindTable();
                Reloads(guns, report);
                TopUps(guns, report);
                SlotTopUps(guns, report);
                GunsAsThings(guns, report);
                SharedReserves(guns, report);
                ReloadEnds(guns, report);
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
        Ammunitions(guns, text);
        text.AppendLine();
        ReloadEndRules(text);
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
            report.Rule("a swap with a gun on the ground that takes other ammunition: the gun put down keeps its rounds, its reserve stays with the player untouched, the gun taken brings only what lay with it");
            foreach (var mine in plain)
            {
                var found = OtherAmmunition(plain, mine);
                if (found == null) { report.Expect(false, mine, "no gun of another ammunition in the list: not checked"); continue; }
                Fresh(slots);
                slots.TrySetWeapon(0, mine);
                var gun = slots.GetRuntime(0);
                Fire(gun, 3);
                gun.AddReserve(11);
                int rounds = gun.Ammo, reserve = gun.ReserveAmmo;
                // A gun the map laid out: 3 rounds short of full, 9 lying with it, and its taker carries none of its
                // ammunition yet.
                slots.ReserveOf(found).Rounds = 0;
                int inIt = Mathf.Max(0, found.MagazineSize - 3), with = Mathf.Min(9, slots.CapWith(0, found));
                var laid = new GunState(found, inIt, 9);
                bool swapped = slots.Put(0, laid, out GunState? before);
                var now = slots.GetRuntime(0);
                bool put = before is GunState old && old.Weapon == mine && old.Rounds == rounds && old.Spare == 0;
                bool kept = slots.ReserveOf(mine).Rounds == reserve;
                bool brought = now != null && now.Data == found && now.Ammo == inIt && now.ReserveAmmo == with;
                // And back again: the first gun as it was put down, the second leaving with what its taker had no room for.
                bool again = before is GunState mineBack && slots.Put(0, mineBack, out GunState? second) && second is GunState foundBack
                    && foundBack.Rounds == inIt && foundBack.Spare == 9 - with && slots.GetRuntime(0).Ammo == rounds && slots.GetRuntime(0).ReserveAmmo == reserve
                    && slots.ReserveOf(found).Rounds == with;
                report.Expect(swapped && put && kept && brought && again, mine,
                    $"{mine.name} {rounds}/{mine.MagazineSize} +{reserve} for {found.name} with {inIt} in it and 9 with it: put down right {put}, "
                    + $"its reserve kept {kept}, the new gun {(now != null ? Rounds(now) : "nothing")}, swapped back right {again}");
            }

            slots = NewPlayer(made);
            report.Rule("rounds lying with a gun go to its taker's reserve only up to its cap; the rest stays with the gun and lies with it again when it is put down");
            foreach (var data in plain)
            {
                Fresh(slots);
                int cap = slots.CapWith(0, data), size = data.MagazineSize;
                // Room for 2 rounds; the gun on the ground has 6 lying with it.
                slots.ReserveOf(data).Rounds = Mathf.Max(0, cap - 2);
                int reserve = slots.ReserveOf(data).Rounds, should = Mathf.Min(cap - reserve, 6);
                bool taken = slots.Put(0, new GunState(data, size, 6), out _);
                var gun = slots.GetRuntime(0);
                bool held = gun != null && gun.Ammo == size && gun.ReserveAmmo == reserve + should && gun.Spare == 6 - should;
                bool dropped = slots.Drop(0, out GunState lying);
                report.Expect(taken && held && dropped && lying.Rounds == size && lying.Spare == 6 - should && slots.ReserveOf(data).Rounds == reserve + should, data,
                    $"reserve {reserve} of {cap}, 6 lying with the gun: took {(gun != null ? gun.ReserveAmmo - reserve : 0)} (should {should}), "
                    + $"{(gun != null ? gun.Spare : -1)} stayed with it, put down with {lying.Spare} lying with it");
            }

            slots = NewPlayer(made);
            report.Rule("a gun already held, found on the ground, gives its rounds to the reserve of its ammunition only, up to its cap, and stays with the rest; a gun of other ammunition is not filled");
            foreach (var mine in plain)
            {
                var other = OtherAmmunition(plain, mine);
                if (other == null) { report.Expect(false, mine, "no gun of another ammunition in the list: not checked"); continue; }
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
            report.Rule("a new gun (the shop's, a kit's) comes full and brings what it carries to a holder who had none: the reserve at its cap; the same gun new again adds nothing past it");
            foreach (var data in plain)
            {
                Fresh(slots);
                slots.ReserveOf(data).Rounds = 0;
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

    // The reserve is kept by the ammunition a gun takes (WeaponData.Ammunition, PlayerWeaponSlots.ReserveOf, ReserveCap):
    // guns of one ammunition reload from the one reserve, a gun swapped for another of its ammunition goes on with it as
    // it stood, two of them held are filled to what both carry, and a gun loaded only from outside shares with none.
    private static void SharedReserves(List<WeaponData> guns, Report report)
    {
        var plain = guns.FindAll(gun => gun.name != "Desperado");
        var made = new List<GameObject>();
        try
        {
            var slots = NewPlayer(made);
            report.Rule("a swap with a gun that takes the same ammunition: the gun taken goes on with the reserve as it stood, and reloads from it");
            foreach (var mine in plain)
            {
                // An ammunition only one gun takes has nothing to be swapped for.
                var found = SameAmmunition(plain, mine);
                if (found == null) continue;
                Fresh(slots);
                slots.ReserveOf(mine).Rounds = 0;
                slots.TrySetWeapon(0, mine);
                var gun = slots.GetRuntime(0);
                Fire(gun, 3);
                int rounds = gun.Ammo, reserve = gun.ReserveAmmo;
                // The gun on the ground: 3 rounds short of full, nothing lying with it.
                int inIt = Mathf.Max(0, found.MagazineSize - 3);
                bool swapped = slots.Put(0, new GunState(found, inIt), out GunState? before);
                var now = slots.GetRuntime(0);
                bool one = ReferenceEquals(slots.ReserveOf(mine), slots.ReserveOf(found));
                bool went = swapped && now != null && now.Data == found && now.Ammo == inIt && now.ReserveAmmo == reserve
                    && before is GunState old && old.Weapon == mine && old.Rounds == rounds;
                int total = now != null ? now.Ammo + now.ReserveAmmo : 0;
                if (now != null)
                {
                    now.Tick(Step, new WeaponInputFrame(false, false, true));
                    for (int i = 0; i < 100000 && now.IsReloading; i++) now.Tick(Step, default);
                }
                bool reloaded = now != null && now.Ammo == Mathf.Min(found.MagazineSize, total) && now.Ammo + now.ReserveAmmo == total
                    && slots.ReserveOf(mine).Rounds == now.ReserveAmmo;
                report.Expect(one && went && reloaded, mine,
                    $"{mine.name} {rounds}/{mine.MagazineSize} +{reserve} for {found.name} ({found.Ammunition}) with {inIt} in it: one reserve {one}, "
                    + $"went on with it {went}, after its reload {(now != null ? Rounds(now) : "nothing")} of {total} rounds, {mine.name}'s reserve now {slots.ReserveOf(mine).Rounds}");
            }

            slots = NewPlayer(made);
            report.Rule("two guns of one ammunition held: one reserve, filled to what the two carry added up; a second gun brings its own share and no more; with one gone the cap is the other's and what is carried stays");
            foreach (var first in plain)
            {
                var second = SameAmmunition(plain, first);
                if (second == null) continue;
                Fresh(slots);
                slots.ReserveOf(first).Rounds = 0;
                slots.TrySetWeapon(0, first);
                int ownFirst = slots.OwnCap(first), ownSecond = slots.OwnCap(second), both = ownFirst + ownSecond;
                bool alone = slots.ReserveCap(0) == ownFirst && slots.GetRuntime(0).ReserveAmmo == ownFirst;
                // The holder has spent the first's reserve down to 5 when the second gun comes, new.
                slots.ReserveOf(first).Rounds = 5;
                slots.TrySetWeapon(1, second);
                WeaponRuntime a = slots.GetRuntime(0), b = slots.GetRuntime(1);
                int carried = Mathf.Min(both, 5 + ownSecond);
                bool caps = slots.ReserveCap(0) == both && slots.ReserveCap(1) == both;
                bool shared = a.ReserveAmmo == carried && b.ReserveAmmo == carried;
                string held = $"{Rounds(a)} and {Rounds(b)} of a cap of {slots.ReserveCap(0)}";
                // One reloads: what it takes is gone for the other too.
                Fire(a, 2);
                int took = first.MagazineSize - a.Ammo;
                a.Tick(Step, new WeaponInputFrame(false, false, true));
                for (int i = 0; i < 100000 && a.IsReloading; i++) a.Tick(Step, default);
                bool drew = a.Ammo == first.MagazineSize && a.ReserveAmmo == carried - took && b.ReserveAmmo == carried - took;
                // Filled to the cap of both, then the second gun is put down: the cap is the first's alone, the rounds
                // carried past it stay, and a gun on the ground has nothing more to give.
                slots.ReserveOf(first).Rounds = both;
                bool gone = slots.Drop(1, out _) && slots.ReserveCap(0) == ownFirst && a.ReserveAmmo == both
                    && slots.RoomFor(0, new GunState(first, first.MagazineSize, 4)) == 0;
                report.Expect(alone && caps && shared && drew && gone, first,
                    $"{first.name} carries {ownFirst}, {second.name} {ownSecond} ({first.Ammunition}): alone right {alone}; with 5 left and {second.name} new they held {held} "
                    + $"(should be +{carried} of {both}); {first.name} reloading {took} left +{b.ReserveAmmo} for {second.name} (should {carried - took}); "
                    + $"with {second.name} put down the cap is {slots.ReserveCap(0)} (should {ownFirst}) and +{a.ReserveAmmo} stays (should {both})");
            }

            slots = NewPlayer(made);
            report.Rule("a gun loaded only from outside (a Schießhund's Desperado) shares its reserve with no gun, whatever ammunition it names");
            foreach (var data in plain)
            {
                Fresh(slots);
                slots.ReserveOf(data).Rounds = 0;
                slots.SpecialHoldsGun = true;
                // The very same gun twice: in a gun slot, and in the special slot as the gun loaded only from outside.
                bool set = slots.TrySetWeapon(0, data) & slots.TrySetWeapon(3, data);
                WeaponRuntime held = slots.GetRuntime(0), kept = slots.GetRuntime(3);
                int cap = slots.ReserveCap(0), own = slots.OwnCap(data);
                bool apart = set && held != null && kept != null && cap == own && held.ReserveAmmo == own && kept.ReserveAmmo == 0;
                bool still = false;
                if (apart)
                {
                    kept.LoadOwnRounds(1);
                    held.AddReserve(3);
                    still = kept.ReserveAmmo == 0 && kept.Ammo == Mathf.Min(1, data.MagazineSize) && held.ReserveAmmo == own + 3 && slots.ReserveCap(0) == own;
                }
                report.Expect(apart && still, data,
                    $"in slot 1 {(held != null ? Rounds(held) : "nothing")} of a cap of {cap} (it carries {own}); in the special slot {(kept != null ? Rounds(kept) : "nothing")}");
                slots.TrySetWeapon(3, null);
                slots.SpecialHoldsGun = false;
            }

            report.Rule("every ammunition a gun names has a name to show over the reserve count (Texts ammo.kind.<name>); a gun that names none has ammunition of its own");
            foreach (var data in guns)
            {
                bool own = data.Ammunition == data.name;
                report.Expect(own || AmmoKinds.NameOf(data) != null, data, $"takes '{data.Ammunition}', which has no text ammo.kind.{data.Ammunition}: the count would be headed RESERVE");
            }
        }
        finally
        {
            foreach (var host in made) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    // What is left of a magazine reload once the gun is loaded may be cut (WeaponRuntime.OnlyEndLeft, ReloadRules): it is
    // offered from the moment the gun is loaded to the clip's end and at no moment before, a cut there leaves a full gun
    // that fires at once and counts as a reload done, and V is the gun's bash only then (as the table stands).
    private static void ReloadEnds(List<WeaponData> guns, Report report)
    {
        report.Rule("reload: only its end is left (what the trigger, the sights, a sprint or a shove may cut) from the moment the gun is loaded to the clip's end, and at no moment before");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
            {
                if (data.ShellReload) continue;
                var gun = Reloading(data, empty, out int reserve);
                if (gun == null) continue;
                var steps = ReloadSteps.Of(data, empty);
                int full = Mathf.Min(data.MagazineSize, gun.Ammo + gun.ReserveAmmo);
                float whole = gun.ReloadDuration, loadedAt = whole * steps.Loaded, clock = 0f, firstAt = -1f;
                bool early = false, late = false, wrong = false;
                for (int i = 0; i < 100000; i++)
                {
                    gun.Tick(Step, default);
                    clock += Step;
                    if (!gun.IsReloading) break;
                    if (!gun.OnlyEndLeft) { late |= clock > loadedAt + 2f * Step; continue; }
                    if (firstAt < 0f) firstAt = clock;
                    early |= clock < loadedAt - 2f * Step;
                    // Offered, the gun is already as the finished reload leaves it.
                    wrong |= gun.MagazineOut || gun.ChamberDue || gun.Ammo != full;
                }
                bool hasEnd = whole - loadedAt > 6f * Step;
                report.Expect(!early && !late && !wrong && (!hasEnd || firstAt >= 0f) && !gun.OnlyEndLeft, data,
                    $"{Kind(empty)} reload of {whole:0.00} s, loaded at {loadedAt:0.00}: its end was first offered at {firstAt:0.00}"
                    + (early ? ", before the gun was loaded" : "") + (late ? ", not through to the clip's end" : "") + (wrong ? ", with the gun not yet as a finished reload leaves it" : ""));
            }

        report.Rule("reload cut where only its end is left: the gun is full and ready, the reload counts as done, nothing is left to go on with, and the trigger fires on the next tick");
        foreach (var data in guns)
            foreach (bool empty in new[] { false, true })
            {
                if (data.ShellReload) continue;
                var gun = Reloading(data, empty, out int reserve);
                if (gun == null) continue;
                var steps = ReloadSteps.Of(data, empty);
                float whole = gun.ReloadDuration;
                // A clip that is over as the gun is loaded has no end to cut.
                if (whole * (1f - steps.Loaded) <= 6f * Step) continue;
                int size = data.MagazineSize, total = gun.Ammo + gun.ReserveAmmo;
                // Half-way through what is left once the gun is loaded.
                Pass(gun, whole * (steps.Loaded + (1f - steps.Loaded) * .5f));
                bool offered = gun.OnlyEndLeft;
                int loads = gun.Loads;
                float left = gun.ReloadRemaining;
                gun.Interrupt();
                bool counted = gun.Loads == loads + 1;
                bool ready = Ready(gun, size, total - size) && !gun.OnlyEndLeft;
                string cut = Rounds(gun) + Doing(gun);
                int shots = gun.Tick(Step, new WeaponInputFrame(true, false));
                report.Expect(offered && counted && ready && shots >= 1, data,
                    $"{Kind(empty)} reload cut with {left:0.00} s of its {whole:0.00} s left: offered {offered}, left {cut} (should be {size}/{size} +{total - size}), "
                    + $"counted as done {counted}, the trigger then fired {shots}");
            }

        report.Rule("V during a reload (ReloadRules.ShoveEnds): until the gun is loaded as the table's shoveCancels says (0: the kick that leaves the reload alone), once only its end is left as cutByShove says (1: the gun's bash, which ends it)");
        bool beforeLoaded = ReloadRules.Allows(ReloadRules.ShoveCancels, false), onceLoaded = ReloadRules.Allows(ReloadRules.ByShove, true);
        foreach (var data in guns)
        {
            if (data.ShellReload) continue;
            var gun = Reloading(data, false, out int reserve);
            if (gun == null) continue;
            var steps = ReloadSteps.Of(data, false);
            float whole = gun.ReloadDuration;
            bool atStart = ReloadRules.ShoveEnds(gun) == beforeLoaded;
            bool hasEnd = whole * (1f - steps.Loaded) > 6f * Step;
            if (hasEnd) Pass(gun, whole * (steps.Loaded + (1f - steps.Loaded) * .5f));
            bool atEnd = !hasEnd || ReloadRules.ShoveEnds(gun) == onceLoaded;
            gun.Interrupt();
            // With no reload under way there is nothing for a shove to end: it is the plain shove.
            bool after = !ReloadRules.ShoveEnds(gun);
            report.Expect(atStart && atEnd && after, data, $"as the reload began right {atStart}, with only its end left right {atEnd}, with none under way right {after}");
        }

        report.Rule("the table of what ends a reload early (ReloadRules.csv) has its row Default, and each of its rules reads as a yes or a no");
        bool found = GameTables.TryGetRow(ReloadRules.TableName, ReloadRules.Row, out var cells);
        foreach (string rule in new[] { ReloadRules.ByFire, ReloadRules.ByAim, ReloadRules.BySprint, ReloadRules.ByShove, ReloadRules.ShoveCancels })
        {
            string text = null;
            bool reads = found && cells.TryGetValue(rule, out text) && TableCells.TryFlag(text, out _);
            report.Expect(reads, null, found ? $"{rule} is '{text}', neither a yes nor a no" : "the table has no row Default");
        }
    }

    // What ends a reload early as the table stands, for whoever reads the report.
    private static void ReloadEndRules(StringBuilder text)
    {
        string Says(string rule, bool fallback) => ReloadRules.Allows(rule, fallback) ? "yes" : "no";
        text.AppendLine("== what may cut a reload once the gun is loaded (ReloadRules.csv): the trigger " + Says(ReloadRules.ByFire, true) + ", the sights "
            + Says(ReloadRules.ByAim, true) + ", a sprint " + Says(ReloadRules.BySprint, true) + ", a shove " + Says(ReloadRules.ByShove, true)
            + "; V before the gun is loaded: " + (ReloadRules.Allows(ReloadRules.ShoveCancels, false) ? "the gun's bash, which cuts the reload where it stands"
                : "the kick that leaves the reload alone"));
    }

    // The next gun of the list after this one (round the list) that takes other ammunition, or the same; null when none does.
    private static WeaponData OtherAmmunition(List<WeaponData> guns, WeaponData gun) => Next(guns, gun, false);

    private static WeaponData SameAmmunition(List<WeaponData> guns, WeaponData gun) => Next(guns, gun, true);

    private static WeaponData Next(List<WeaponData> guns, WeaponData gun, bool same)
    {
        int at = guns.IndexOf(gun);
        for (int i = 1; i < guns.Count; i++)
        {
            var other = guns[(at + i) % guns.Count];
            if (other != gun && (other.Ammunition == gun.Ammunition) == same) return other;
        }
        return null;
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

    // Which guns share a reserve, for whoever reads the report: each ammunition with the guns that take it and what each
    // carries by itself (two of them held are filled to the two numbers added up).
    private static void Ammunitions(List<WeaponData> guns, StringBuilder text)
    {
        text.AppendLine("== ammunition (Weapons.csv ammo): the guns of one line reload from their holder's one reserve; each carries the number after it, "
            + "and two of them held are filled to the two added up");
        var seen = new List<string>();
        foreach (var gun in guns)
        {
            string kind = gun.Ammunition;
            if (seen.Contains(kind)) continue;
            seen.Add(kind);
            var takers = guns.FindAll(other => other.Ammunition == kind);
            string name = AmmoKinds.NameOf(gun);
            text.Append((kind == gun.name ? "(its own)" : kind).PadRight(10)).Append(' ').Append((name ?? "-").PadRight(8)).Append(' ');
            for (int i = 0; i < takers.Count; i++) text.Append(i > 0 ? ", " : "").Append(takers[i].name).Append(' ').Append(takers[i].InitialReserveAmmo);
            text.AppendLine();
        }
    }

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
