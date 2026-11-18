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
    private static double next;
    private static bool ran;

    static AmmoCheck() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool asked = File.Exists(Request);
        if (!asked && ran) return;
        if (asked)
        {
            try
            {
                File.Delete(Request);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return;
            }
        }
        ran = true;
        Run();
    }

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
                TopUps(guns, report);
                SlotTopUps(guns, report);
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

    private static string Doing(WeaponRuntime gun)
        => (gun.IsReloading ? " still reloading" : "") + (gun.MagazineOut ? " magazine out" : "") + (gun.ChamberDue ? " bolt due" : "");
}
#endif
