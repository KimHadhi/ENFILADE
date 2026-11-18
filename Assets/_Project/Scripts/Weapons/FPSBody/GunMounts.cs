using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Where parts go on a gun, as data, one model for every gun (ours, KINEMATION's two packs', a mod's):
/// a gun has sockets, places on the gun itself; a part sits on a socket by one rule for its kind.
/// - Tables/Weapons/GunMounts.csv: a row per gun and socket, id "&lt;gun&gt;.&lt;socket&gt;" (the gun's id is its WeaponData's
///   name). A socket is an origin and a turn in the gun prefab's own space and a span along its own forward: a rail is a
///   stretch a part can sit anywhere along, a point (span 0) is where KINEMATION put its own mount bone. The sockets:
///   eye (where the eye is when the gun is aimed with its own sights), sight, muzzle, grip, under. The row also says
///   which parts the socket offers (fits), which one the gun starts with (starts), and for the sight socket which
///   pieces of the gun's own mesh are its iron sights (folds: put away under an optic) or glass the pack draws as solid
///   metal (hides: never drawn).
/// - Tables/Weapons/GunParts.csv: a row per part (its prefab's name; "own" for a gun's own sights): the socket it goes on,
///   what it is, its name in the menu, its foot on a rail, and for a sight how the aimed view is drawn through it.
/// The rule (Seat...): a sight slides along its rail until its own eye point is level with the gun's eye place, as far
/// as its foot stays on the rail; a light sits at its rail's front end; a grip sits where the hand that holds it is
/// (FPSGun.SeatGrips), kept on its rail; a muzzle part sits at the socket.
/// Until 2026-11-18 the FPS Animation Pack guns' mounts were guessed in the editor from one number for all of them: three
/// centimetres ahead of the gun's aim point, which is the eye's place behind the gun and no point of the gun at all. An
/// optic sat over the stock, a pistol's dot hung in the air 20 cm behind the pistol, the KXG12's sight sat inside its
/// body, and only the MX16A4 was put right by hand (user, 2026-11-18: "MP5를 비롯한 기존 무기 및 신규 추가 무기에서 부착물
/// 위치가 어긋나는 문제 … 개별 무기별 임시 수정이 아닌, 부착물 소켓/마운트 … 자체를 유동적이고 창작마당 모딩 친화적으로
/// 근본 재건축"). The rows were measured from each gun's own mesh (Tools/Guns/README.md) and are a table's: a mod
/// lays its own rows over them (Docs/Modding.md).
/// </summary>
public static class GunMounts
{
    public const string Table = "GunMounts", PartsTable = "GunParts";
    public const string Eye = "eye", Sight = "sight", Muzzle = "muzzle", Grip = "grip", Under = "under";
    /// <summary>The id of a gun's own sights (the sights entry with no part) in fits, starts and GunParts.csv.</summary>
    public const string Own = "own";

    /// <summary>How the aimed view is drawn through a sight (GunParts.csv view).</summary>
    public enum View
    {
        /// <summary>Looked through as it is: iron sights, a dot, a holographic window.</summary>
        Glass,
        /// <summary>The whole view is magnified and the lens shows the same magnification: one picture.</summary>
        Zoom,
        /// <summary>The view stays as the gun has it and the lens alone shows a magnified picture.</summary>
        Picture
    }

    /// <summary>One socket of one gun, in the gun prefab's own space.</summary>
    public struct Socket
    {
        public bool Found;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>How far ahead of its origin, along its own forward, a part may sit (m); 0 for a point.</summary>
        public float Span;
        /// <summary>The parts it offers; none listed = whatever the prefab carries.</summary>
        public string[] Fits;
        public string Starts;
        /// <summary>Boxes of the gun's own iron sights, and of glass never drawn, in the gun's space.</summary>
        public Bounds[] Folds, Hides;
        /// <summary>The same two cells as written: what a cut of the gun's mesh is remembered by.</summary>
        public string FoldsText, HidesText;

        public bool Offers(string part)
        {
            if (Fits == null || Fits.Length == 0) return true;
            foreach (string fit in Fits)
                if (string.Equals(fit, part, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A point of the socket's own frame (z along it) in the gun's space.</summary>
        public Vector3 At(float along) => Position + Rotation * new Vector3(0f, 0f, along);
    }

    /// <summary>One part, whatever gun wears it.</summary>
    public struct Part
    {
        public bool Found;
        public string Slot, Kind, NameId;
        /// <summary>How many times the aimed view is magnified; 1 leaves the gun's own aimed field.</summary>
        public float Magnification;
        public View View;
        /// <summary>For Zoom: the lens picture's diameter as a share of the screen's height.</summary>
        public float Window;
        /// <summary>How far its foot on a rail reaches behind and ahead of its origin (m).</summary>
        public float FootBack, FootFront;

        /// <summary>The whole view is this sight's: it is magnified and drawn as one picture.</summary>
        public bool Zooms => Found && View == View.Zoom && Magnification > 1.001f;
    }

    /// <summary>Counts up each time the tables are read anew, so what was seated by the old rows is seated again.</summary>
    public static int Version { get; private set; }

    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        listening = false;
        Version = 0;
    }

    private static void Listen()
    {
        if (listening) return;
        listening = true;
        GameTables.Reloaded += () => Version++;
    }

    /// <summary>A gun's id as the tables have it: its object's name without Unity's "(Clone)".</summary>
    public static string IdOf(GameObject gun)
    {
        string name = gun != null ? gun.name : "";
        int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return (clone >= 0 ? name.Substring(0, clone) : name).Trim();
    }

    /// <summary>The socket <paramref name="socket"/> of <paramref name="gun"/>; Found is false without a row.</summary>
    public static Socket Of(string gun, string socket)
    {
        Listen();
        var found = new Socket { Rotation = Quaternion.identity, Fits = Array.Empty<string>(), Folds = Array.Empty<Bounds>(), Hides = Array.Empty<Bounds>() };
        if (string.IsNullOrEmpty(gun) || !GameTables.TryGetRow(Table, gun + "." + socket, out var row)) return found;
        found.Found = true;
        found.Position = new Vector3(row.Number("x", 0f), row.Number("y", 0f), row.Number("z", 0f));
        found.Rotation = Quaternion.Euler(row.Number("rx", 0f), row.Number("ry", 0f), row.Number("rz", 0f));
        found.Span = Mathf.Max(0f, row.Number("span", 0f));
        var fits = new List<string>(TableCells.Parts(row.Text("fits")));
        found.Fits = fits.ToArray();
        found.Starts = row.Text("starts");
        found.FoldsText = row.Text("folds");
        found.HidesText = row.Text("hides");
        found.Folds = Boxes(found.FoldsText);
        found.Hides = Boxes(found.HidesText);
        return found;
    }

    /// <summary>
    /// The gun has rows of its own (its eye place is the row every such gun has). Its sockets are then the ones its rows
    /// say: a part its prefab carries on a socket without a row is not offered (the Striker-V's foregrip, which stood where
    /// its own folding grip is). A gun with no rows at all, one not measured yet or a mod's that brings no table, wears
    /// whatever its prefab carries, where the prefab has it.
    /// </summary>
    public static bool Known(string gun) => Of(gun, Eye).Found;

    /// <summary>The part <paramref name="id"/> (a part prefab's name, or Own); Found is false without a row.</summary>
    public static Part PartOf(string id)
    {
        Listen();
        var part = new Part { Magnification = 1f, View = View.Glass, Window = .9f };
        if (string.IsNullOrEmpty(id) || !GameTables.TryGetRow(PartsTable, id, out var row)) return part;
        part.Found = true;
        part.Slot = row.Text("slot");
        part.Kind = row.Text("kind");
        part.NameId = row.Text("name");
        part.Magnification = Mathf.Max(1f, row.Number("magnification", 1f));
        string view = row.Text("view");
        part.View = string.Equals(view, "zoom", StringComparison.OrdinalIgnoreCase) ? View.Zoom
            : string.Equals(view, "pip", StringComparison.OrdinalIgnoreCase) ? View.Picture : View.Glass;
        part.Window = Mathf.Clamp(row.Number("window", .9f), .2f, 2f);
        part.FootBack = Mathf.Max(0f, row.Number("footBack", 0f));
        part.FootFront = Mathf.Max(0f, row.Number("footFront", 0f));
        return part;
    }

    // ---- The rule: where on its socket a part sits (metres ahead of the socket's origin) ----

    /// <summary>
    /// A sight: where its own eye point (<paramref name="aimInPart"/>, in the part's own frame) is level with the gun's eye
    /// place (<paramref name="eye"/>, in the gun's space) along the rail, as far as its foot stays on the rail. So the
    /// gun is held the same whatever sight it wears, wherever its rail lets the sight go there; on a rail too far ahead
    /// (a pistol's slide) the sight sits at the rail's rear end and the gun comes that much nearer the eye.
    /// </summary>
    public static float SeatSight(in Socket socket, Vector3 eye, Vector3 aimInPart, float footBack, float footFront)
    {
        float eyeAlong = (Quaternion.Inverse(socket.Rotation) * (eye - socket.Position)).z;
        return Kept(socket, eyeAlong - aimInPart.z, footBack, footFront);
    }

    /// <summary>A light: at its rail's front end, where it looks past whatever else hangs under the gun.</summary>
    public static float SeatFront(in Socket socket, float footFront) => Mathf.Max(0f, socket.Span - footFront);

    /// <summary>A place <paramref name="along"/> the rail kept where the part's foot stays on it; a rail shorter than the
    /// foot (a point) holds the part at its middle.</summary>
    public static float Kept(in Socket socket, float along, float footBack, float footFront)
    {
        float low = footBack, high = socket.Span - footFront;
        return high < low ? socket.Span * .5f : Mathf.Clamp(along, low, high);
    }

    /// <summary>Puts <paramref name="mount"/> (any transform under the gun) where the socket is on <paramref name="gun"/>.</summary>
    public static void Place(Transform gun, Transform mount, in Socket socket)
    {
        if (gun == null || mount == null || !socket.Found) return;
        mount.SetPositionAndRotation(gun.TransformPoint(socket.Position), gun.rotation * socket.Rotation);
    }

    // ---- The aimed view (the sight's) ----

    /// <summary>
    /// The player's vertical field of view (degrees) fully aimed through <paramref name="sight"/>: a sight that magnifies
    /// the whole view narrows it to <paramref name="hipFov"/> seen that many times nearer; any other leaves the gun's own
    /// aimed field (<paramref name="ownFov"/>, Weapons.csv adsFov).
    /// </summary>
    public static float AimedFov(in Part sight, float hipFov, float ownFov)
    {
        if (!sight.Zooms) return ownFov;
        float zoomed = 2f * Mathf.Atan(Mathf.Tan(hipFov * .5f * Mathf.Deg2Rad) / sight.Magnification) * Mathf.Rad2Deg;
        return Mathf.Min(ownFov, zoomed);
    }

    // ---- Boxes ----

    // A box holds a piece whose every corner is within this of it (m): a row's numbers are written to a tenth of a millimetre.
    private const float BoxSlack = .0002f;

    /// <summary>The boxes of a folds or hides cell: "x0 y0 z0 x1 y1 z1" each, '|' between them. Malformed ones are left out.</summary>
    public static Bounds[] Boxes(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return Array.Empty<Bounds>();
        var boxes = new List<Bounds>();
        foreach (string one in TableCells.Parts(cell))
        {
            var numbers = one.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length != 6) continue;
            var value = new float[6];
            bool ok = true;
            for (int i = 0; i < 6 && ok; i++)
                ok = float.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value[i]);
            if (!ok) continue;
            var box = new Bounds();
            box.SetMinMax(new Vector3(Mathf.Min(value[0], value[3]), Mathf.Min(value[1], value[4]), Mathf.Min(value[2], value[5])) - Vector3.one * BoxSlack,
                new Vector3(Mathf.Max(value[0], value[3]), Mathf.Max(value[1], value[4]), Mathf.Max(value[2], value[5])) + Vector3.one * BoxSlack);
            boxes.Add(box);
        }
        return boxes.ToArray();
    }
}
