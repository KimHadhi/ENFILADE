using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How one shot's muzzle flame looks: a row of Tables/MuzzleFlash. Every number is a share of the flame MuzzleFlame's
/// prefab holds (CS2's first-person rifle flash, measured frame by frame), so 1 everywhere is that flame as measured.
/// </summary>
public readonly struct MuzzleFlashLook
{
    /// <summary>The row it was read from.</summary>
    public readonly string Row;
    /// <summary>The whole flame's size. 0: the shot throws no flame and no light.</summary>
    public readonly float Size;
    /// <summary>How far the puffs string out and drift along the barrel: a long tongue (a rifle) or a ball (a pistol).</summary>
    public readonly float Length;
    /// <summary>How fat the puffs are across the barrel: a shotgun's bore, a magnum's ball.</summary>
    public readonly float Width;
    /// <summary>The fewest and the most puffs a shot rolls.</summary>
    public readonly int FewestPuffs, MostPuffs;
    /// <summary>How long it stays bright and takes to go (CS2's rifle: three frames at 60).</summary>
    public readonly float Bright;
    /// <summary>The chance (0-1) a shot leaves the faint afterglow a frame longer.</summary>
    public readonly float Afterglow;
    /// <summary>The chance (0-1) a shot throws sparks.</summary>
    public readonly float Sparks;
    /// <summary>How hot it burns: over 1 whiter, under 1 a dull orange.</summary>
    public readonly float Heat;
    /// <summary>How solid it is: under 1 a thin puff.</summary>
    public readonly float Alpha;
    /// <summary>The light it throws on the room (MuzzleLight), as a share of GunFeel's muzzle light. 0: none.</summary>
    public readonly float Light;
    /// <summary>How thick its wisp of smoke is.</summary>
    public readonly float Smoke;
    /// <summary>The shooter sees the smoke too (it otherwise shows only from further off: F5, teammates).</summary>
    public readonly bool NearSmoke;

    public MuzzleFlashLook(string row, float size, float length, float width, int fewestPuffs, int mostPuffs, float bright, float afterglow,
        float sparks, float heat, float alpha, float light, float smoke, bool nearSmoke)
    {
        Row = row;
        Size = Mathf.Max(0f, size); Length = Mathf.Max(0f, length); Width = Mathf.Max(0f, width);
        FewestPuffs = Mathf.Max(1, fewestPuffs); MostPuffs = Mathf.Max(FewestPuffs, mostPuffs);
        Bright = Mathf.Max(.05f, bright); Afterglow = Mathf.Clamp01(afterglow); Sparks = Mathf.Clamp01(sparks);
        Heat = Mathf.Max(0f, heat); Alpha = Mathf.Clamp01(alpha); Light = Mathf.Max(0f, light); Smoke = Mathf.Max(0f, smoke);
        NearSmoke = nearSmoke;
    }

    /// <summary>The flame as the prefab has it: what a row's empty cells mean, and every row when the table is missing.</summary>
    public static MuzzleFlashLook Measured(string row) => new MuzzleFlashLook(row, 1f, 1f, 1f, 4, 6, 1f, .55f, .5f, 1f, 1f, 1f, 1f, false);
}

/// <summary>
/// Which flame a gun's shot throws. A muzzle flame is the powder gas burning as it leaves the barrel, so what it looks
/// like belongs to the cartridge, the barrel and what is on the muzzle, not to each gun as a number of its own: the looks
/// are the rows of Tables/MuzzleFlash (rifle, pistol, smg, magnum, lmg, dmr, sniper, shotgun, suppressed …; the user,
/// 2026-11-18: "기존 CS2 스타일 총구 화염 이펙트를 기반으로 저격총·샷건·권총·소총·소음기 등 총기 및 탄종별로 화염 강도를
/// 차등 제작하여 적용"). A gun names its row in Weapons.csv's `flash` cell; a gun that names none (a mod's) gets the row
/// its kind stands for: a gun of several pellets the shotgun's, else by the sound of its shot (WeaponData.SoundProfile:
/// a pistol's, a heavy rifle's, a rifle's). With a suppressor on, the shot reads the row its own row points at
/// (`suppressedAs`): a small dim puff, not nothing. One reader for the flame (MuzzleFlash) and its light (MuzzleLight);
/// nothing else decides it. A mod adds rows and names them the same way.
/// Until 2026-11-18 a gun's flame was one number on its asset, and 21 of the 28 guns showed a bought pack's muzzle effect
/// instead, by a class guessed from their table numbers (PackShots.ClassOf): that pack is for skills now.
/// </summary>
public static class MuzzleFlashLooks
{
    public const string TableName = "MuzzleFlash";
    /// <summary>The rows a gun that names none stands for by its kind, and the row a suppressor turns a look into when it names none.</summary>
    public const string Rifle = "rifle", Pistol = "pistol", Heavy = "dmr", Shotgun = "shotgun", Suppressed = "suppressed";

    private static readonly Dictionary<(WeaponData, bool), MuzzleFlashLook> looks = new Dictionary<(WeaponData, bool), MuzzleFlashLook>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        looks.Clear();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => looks.Clear();

    /// <summary>The flame <paramref name="gun"/>'s shot throws, with a suppressor on its muzzle or without.</summary>
    public static MuzzleFlashLook Of(WeaponData gun, bool suppressed)
    {
        if (gun == null) return MuzzleFlashLook.Measured(Rifle);
        if (looks.TryGetValue((gun, suppressed), out var known)) return known;
        var look = Read(RowOf(gun, suppressed));
        looks[(gun, suppressed)] = look;
        return look;
    }

    /// <summary>The row <paramref name="gun"/>'s shot reads: its open row, or with a suppressor on the row that one points at.</summary>
    public static string RowOf(WeaponData gun, bool suppressed)
    {
        string open = OpenRowOf(gun);
        return suppressed ? GameTables.Text(TableName, open, "suppressedAs", Suppressed) : open;
    }

    /// <summary>
    /// The row of <paramref name="gun"/>'s shot with nothing on its muzzle: the one its `flash` cell names when there is
    /// such a row, else the one its kind stands for.
    /// </summary>
    public static string OpenRowOf(WeaponData gun)
    {
        if (gun == null) return Rifle;
        string named = gun.Flash;
        if (!string.IsNullOrEmpty(named) && GameTables.TryGetRow(TableName, named, out _)) return named;
        if (gun.Pellets > 1) return Shotgun;
        return gun.SoundProfile == GunSoundProfile.Pistol ? Pistol : gun.SoundProfile == GunSoundProfile.Heavy ? Heavy : Rifle;
    }

    /// <summary>A row of the table as a look; a row that isn't there, or the table itself missing, is the measured flame.</summary>
    public static MuzzleFlashLook Read(string row)
    {
        var plain = MuzzleFlashLook.Measured(row);
        if (!GameTables.TryGetRow(TableName, row, out var cells)) return plain;
        return new MuzzleFlashLook(row, cells.Number("size", plain.Size), cells.Number("length", plain.Length), cells.Number("width", plain.Width),
            cells.Whole("puffsFew", plain.FewestPuffs), cells.Whole("puffsMost", plain.MostPuffs), cells.Number("bright", plain.Bright),
            cells.Number("afterglow", plain.Afterglow), cells.Number("sparks", plain.Sparks), cells.Number("heat", plain.Heat),
            cells.Number("alpha", plain.Alpha), cells.Number("light", plain.Light), cells.Number("smoke", plain.Smoke),
            cells.Flag("nearSmoke", plain.NearSmoke));
    }
}

#if UNITY_EDITOR
/// <summary>
/// The editor's list of the flame every gun's shot throws, without playing: Logs/muzzle-flash.txt has a line per gun
/// with its Weapons.csv `flash` cell, the row it reads open and suppressed with that row's numbers, and whether its shots
/// still wear a bought pack's look (PackShots: only a skill's gun should). A cell or a `suppressedAs` that names no row
/// is a FAIL line. So "which flame does this gun get" is read, not played for, a mod's gun too. Runs after every script
/// reload and a play, from ENFILADE → 총 → 총구 화염 점검, and when Temp/enfilade-muzzleflash.request is made.
/// </summary>
[UnityEditor.InitializeOnLoad]
internal static class MuzzleFlashCheck
{
    private const string Request = "Temp/enfilade-muzzleflash.request", ReportPath = "Logs/muzzle-flash.txt";

    // It keeps itself alive on the editor's update.
    static MuzzleFlashCheck() => new CheckOnRequest(Request, Run);

    [UnityEditor.MenuItem("ENFILADE/총/총구 화염 점검")]
    private static void Run()
    {
        var text = new System.Text.StringBuilder();
        int failed = 0;
        var assets = FPSBodyAssets.Instance;
        var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var packed = new List<string>();
        try
        {
            if (!GameTables.TryGetRow(MuzzleFlashLooks.TableName, MuzzleFlashLooks.Rifle, out _))
            {
                failed++;
                text.AppendLine("FAIL Tables/MuzzleFlash.csv has no '" + MuzzleFlashLooks.Rifle + "' row: every gun throws the measured flame.");
            }
            text.AppendLine("gun              flash cell       open row -> its numbers | suppressed row -> its numbers | pack look");
            if (assets != null)
                foreach (var entry in assets.guns)
                {
                    var gun = entry.weapon;
                    if (gun == null || gun.Category != WeaponCategory.Ranged) continue;
                    gun.BindTable();
                    string cell = gun.Flash, open = MuzzleFlashLooks.RowOf(gun, false), quiet = MuzzleFlashLooks.RowOf(gun, true);
                    used.Add(open);
                    used.Add(quiet);
                    if (!string.IsNullOrEmpty(cell) && !GameTables.TryGetRow(MuzzleFlashLooks.TableName, cell, out _))
                    {
                        failed++;
                        text.AppendLine($"FAIL {gun.name}: Weapons.csv flash '{cell}' is no row of MuzzleFlash.csv; it reads '{open}' by its kind.");
                    }
                    if (!GameTables.TryGetRow(MuzzleFlashLooks.TableName, quiet, out _))
                    {
                        failed++;
                        text.AppendLine($"FAIL {gun.name}: its suppressed row '{quiet}' (suppressedAs of '{open}') is no row of MuzzleFlash.csv.");
                    }
                    string pack = PackShots.RowOf(gun);
                    if (pack != null) packed.Add(gun.name);
                    text.AppendLine($"{gun.name,-16} {(string.IsNullOrEmpty(cell) ? "(by its kind)" : cell),-16} {Numbers(MuzzleFlashLooks.Read(open))} | "
                        + $"{Numbers(MuzzleFlashLooks.Read(quiet))} | "
                        + (pack == null ? "none" : "PackFx row " + pack + (PackShots.OwnFlame(gun) ? ", own flame too" : ", in place of its flame")));
                }
            text.AppendLine();
            text.AppendLine("Guns whose shots wear the bought pack's look (it is for skills: a class's own gun): " + (packed.Count == 0 ? "none" : string.Join(", ", packed)));
            var idle = new List<string>();
            foreach (string row in GameTables.RowIds(MuzzleFlashLooks.TableName))
                if (!used.Contains(row)) idle.Add(row);
            text.AppendLine("Rows no gun of the game reads now (a mod's gun may): " + (idle.Count == 0 ? "none" : string.Join(", ", idle)));
        }
        catch (System.Exception problem)
        {
            failed++;
            text.AppendLine("FAIL the check itself stopped: " + problem);
        }
        string head = $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} muzzle flame of every gun: " + (failed == 0 ? "ALL ROWS FOUND" : failed + " BROKEN")
            + "\nNumbers are shares of the measured flame (1 = CS2's rifle flash in MuzzleFlame.prefab): size, length x width, puffs, bright time, "
            + "afterglow chance, spark chance, heat, alpha, light, smoke (+near: the shooter sees the smoke too).\n\n";
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath));
            System.IO.File.WriteAllText(ReportPath, head + text, new System.Text.UTF8Encoding(false));
        }
        catch (System.Exception error) when (error is System.IO.IOException || error is System.UnauthorizedAccessException)
        {
            Debug.LogWarning("MuzzleFlashCheck: " + ReportPath + " could not be written: " + error.Message);
        }
        if (failed > 0) Debug.LogWarning($"MuzzleFlashCheck: {failed} broken, see {ReportPath}.");
    }

    private static string Numbers(MuzzleFlashLook look)
        => $"{look.Row,-15} size {look.Size:0.##} {look.Length:0.##}x{look.Width:0.##} puffs {look.FewestPuffs}-{look.MostPuffs} bright {look.Bright:0.##} "
            + $"glow {look.Afterglow:0.##} sparks {look.Sparks:0.##} heat {look.Heat:0.##} alpha {look.Alpha:0.##} light {look.Light:0.##} "
            + $"smoke {look.Smoke:0.##}{(look.NearSmoke ? "+near" : "")}";
}
#endif
