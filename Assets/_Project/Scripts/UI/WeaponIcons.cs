using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The HUD's weapon and killer pictures (user, 2026-11-12: "무기 아이콘도 이제 좀 바꿀 수 있나? 그래야지 이제 킬로그나 무기
/// 슬롯에 쓸 수 있지"). Tools/Blender/weapon_icons.py draws each from the gun's own model: side on, muzzle to the right as
/// kill feeds show them, white on transparent so the HUD tints it for a dark or a light plate, and all on one canvas
/// (<see cref="Aspect"/>) with the guns' lengths kept in their order (a pistol a bit under half a rifle), so any box of that
/// shape shows them all at their right sizes.
/// <para>An icon's id is the weapon's asset name (its Weapons.csv row), or one of the killers that are not a weapon
/// (<see cref="Fist"/>, <see cref="Shove"/>, <see cref="Execution"/>, <see cref="Counter"/>, <see cref="Explosion"/>,
/// <see cref="Fall"/>). Where a picture comes from, the first found:</para>
/// <para>1. the weapon's own HudIcon, when one was set in its asset;
/// 2. a PNG of the id's name in Documents\ENFILADE\WeaponIcons, so a modder's new gun brings its own (and a player can
/// swap any) without touching the game;
/// 3. WeaponIcons.csv `&lt;id&gt;.icon`: another id whose picture this one shows;
/// 4. the game's: Resources/UI/WeaponIcons/&lt;look&gt;/&lt;id&gt;.png, the look being WeaponIcons.csv Look.style.</para>
/// Found nowhere: null, and the caller shows its own stand-in (the kill feed the weapon's name, the slots their vector
/// outline). Each picture is read once; saving the table reads them again.
/// </summary>
public static class WeaponIcons
{
    public const string TableName = "WeaponIcons", ResourcesFolder = "UI/WeaponIcons", DefaultStyle = "Solid";
    /// <summary>Killers that are not a weapon: bare hands (punches, kicks, card blows), a shove (and a dash's or a
    /// kick's push), an execution, a counter, a blast, a fall.</summary>
    public const string Fist = "fist", Shove = "shove", Execution = "execution", Counter = "counter", Explosion = "explosion", Fall = "fall";
    /// <summary>The headshot's mark after the weapon in the kill feed: a skull, Counter-Strike's way, in the canvas's middle
    /// square (the user, 2026-11-12: "헤드샷은 Counter Strike처럼 두개골 모양이었으면 좋겠어 직관적이게").</summary>
    public const string Headshot = "headshot";
    /// <summary>Where each game picture's drawing lies on its canvas, written by the icon tool (WeaponIconSpans.csv).</summary>
    public const string SpanTable = "WeaponIconSpans";
    /// <summary>Every icon's canvas, width over height (512 x 160).</summary>
    public const float Aspect = 512f / 160f;

    private static readonly Dictionary<string, Sprite> found = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    // Pictures read from the player's folder: their drawing can lie anywhere, so the game's spans don't hold for them.
    private static readonly HashSet<Sprite> theirs = new HashSet<Sprite>();
    // Pictures and sprites made here at run time (from files, or round a Resources picture), let go when the tables reload.
    private static readonly List<Object> made = new List<Object>();
    private static string style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Clear();
        GameTables.Reloaded -= Clear;
        GameTables.Reloaded += Clear;
    }

    /// <summary>Forget every picture read, so the next ask reads them again (a saved table, a new file in the folder).</summary>
    public static void Clear()
    {
        foreach (var thing in made)
        {
            if (thing == null) continue;
            // A table saved in the editor outside Play reloads them too, where Destroy isn't allowed.
            if (Application.isPlaying) Object.Destroy(thing);
            else Object.DestroyImmediate(thing);
        }
        made.Clear();
        found.Clear();
        theirs.Clear();
        style = null;
    }

    /// <summary>The look the game's own pictures are drawn in: Solid, Outline or Line (WeaponIcons.csv Look.style).</summary>
    public static string Style => style ??= Text("Look", "style", DefaultStyle);

    /// <summary>Documents\ENFILADE\WeaponIcons (the save folder's WeaponIcons where there is no Documents folder).</summary>
    public static string Folder
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents)
                ? Path.Combine(Application.persistentDataPath, "WeaponIcons")
                : Path.Combine(documents, "ENFILADE", "WeaponIcons");
        }
    }

    /// <summary>A weapon's icon id: its asset's name, as Weapons.csv names its row.</summary>
    public static string IdOf(WeaponData data)
    {
        if (data == null) return null;
        string id = data.name;
        int clone = id.IndexOf("(Clone)", StringComparison.Ordinal);
        return clone >= 0 ? id.Substring(0, clone).Trim() : id;
    }

    /// <summary>The weapon's picture, or null when it has none anywhere.</summary>
    public static Sprite For(WeaponData data)
    {
        if (data == null) return null;
        return data.HudIcon != null ? data.HudIcon : For(IdOf(data));
    }

    /// <summary>The picture of an id (a weapon's asset name or a killer), or null when it has none anywhere.</summary>
    public static Sprite For(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (found.TryGetValue(id, out var known)) return known;
        var sprite = Find(id, 0);
        // A miss is kept too, so a weapon without a picture isn't looked for on disk every frame.
        found[id] = sprite;
        return sprite;
    }

    /// <summary>
    /// Where an id's game picture is drawn on its canvas, in uv (x left, y right, z bottom, w top), and how much of its length
    /// from the muzzle back is the barrel end (<paramref name="front"/>), for flairs that burn only the barrel (KillFlairFx).
    /// A picture of the player's own, or one with no row: the whole canvas and half.
    /// </summary>
    public static Vector4 Span(string id, out float front)
    {
        front = .5f;
        if (string.IsNullOrWhiteSpace(id) || (found.TryGetValue(id.Trim(), out var sprite) && sprite != null && theirs.Contains(sprite))
            || !GameTables.TryGetRow(SpanTable, id.Trim(), out var cells))
            return new Vector4(0f, 1f, 0f, 1f);
        front = Cell(cells, "front", .5f);
        return new Vector4(Cell(cells, "left", 0f), Cell(cells, "right", 1f), Cell(cells, "bottom", 0f), Cell(cells, "top", 1f));
    }

    private static float Cell(IReadOnlyDictionary<string, string> cells, string field, float fallback)
        => cells.TryGetValue(field, out string raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    /// <summary>A number of WeaponIcons.csv (`Row.field`), for the HUD parts that show the icons.</summary>
    public static float Number(string row, string field, float fallback)
    {
        string raw = Text(row, field, null);
        return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
    }

    private static string Text(string row, string field, string fallback)
        => GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(field, out string value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim() : fallback;

    private static Sprite Find(string id, int depth)
    {
        var own = FromFolder(id);
        if (own != null) return own;
        // Another id's picture (a variant of a gun, a killer that looks like another), at most a few steps deep.
        string other = Text(id, "icon", null);
        if (other != null && depth < 4 && !string.Equals(other, id, StringComparison.OrdinalIgnoreCase)) return Find(other, depth + 1);
        var picture = Resources.Load<Texture2D>(ResourcesFolder + "/" + Style + "/" + id);
        if (picture == null && !string.Equals(Style, DefaultStyle, StringComparison.OrdinalIgnoreCase))
            picture = Resources.Load<Texture2D>(ResourcesFolder + "/" + DefaultStyle + "/" + id);
        return picture != null ? Wrap(picture) : null;
    }

    private static Sprite FromFolder(string id)
    {
        string path;
        try
        {
            path = Path.Combine(Folder, id + ".png");
            if (!File.Exists(path)) return null;
        }
        catch (Exception)
        {
            return null;
        }
        try
        {
            var picture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "Weapon icon " + id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            if (!picture.LoadImage(File.ReadAllBytes(path), true))
            {
                Object.Destroy(picture);
                Debug.LogWarning($"[WeaponIcons] {path} is not a picture Unity can read (PNG).");
                return null;
            }
            made.Add(picture);
            var sprite = Wrap(picture);
            theirs.Add(sprite);
            return sprite;
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[WeaponIcons] {path} could not be read: {problem.Message}");
            return null;
        }
    }

    private static Sprite Wrap(Texture2D picture)
    {
        var sprite = Sprite.Create(picture, new Rect(0f, 0f, picture.width, picture.height), new Vector2(.5f, .5f), 100f, 0,
            SpriteMeshType.FullRect);
        sprite.name = picture.name;
        sprite.hideFlags = HideFlags.DontSave;
        made.Add(sprite);
        return sprite;
    }
}
