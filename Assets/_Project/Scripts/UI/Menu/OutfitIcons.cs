using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Pictures of what can be worn, for the wardrobe's tiles and choices (MainMenuWardrobe): each item in a colour set, or
/// a character's own piece of a slot, the whole of it seen from its front, leaning a little clockwise, against nothing,
/// so the wardrobe can lay it see-through over its plates, big and cut by them (OutfitPictureGraphic). The user found the
/// wardrobe all words (2026-11-12: "텍스트만 보고 입히려면 하나하나 다 봐야 하는데 무슨 의상인지 보이면 걍 원하는 거 바로
/// 클릭할 수 있잖아").
/// Then (2026-11-12: "상의 미리보기 저거 패널에 있는거 작게 전체가 아니라 좀 전체적으로 보이고 살짝 투명하게 ㅇㅇ 그리고 미리
/// 구워 놓으면 로딩 속도 더 빨라 지려나"): a picture comes from a file when there is one (OutfitPictures: baked into the game
/// by Tools → Characters → 옷장 사진 모두 굽기, or taken again in the wardrobe's 사진 다시 찍기), else it is taken here in the
/// photo studio (OutfitStudio), one a frame, at once, framed whole from where the piece's vertices really are. Taking
/// one stands the piece's whole model file (an outfit's file with every piece of it, or the whole character for its own
/// pieces), which is what made a page full of new pictures stutter; in the editor and development builds the console
/// says what a batch of them cost. A picture taken while the editor compiled shaders (flat cyan) is thrown away and
/// taken again, as the portrait cards' are (SkinPortraits).
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitIcons : MonoBehaviour
{
    public const int Size = OutfitPictures.Size;

    private readonly struct Ask
    {
        public readonly string Key;
        public readonly PlayerBodyProfile.Skin Skin;
        public readonly OutfitItem Item;
        public readonly string Colour, Slot;

        public Ask(string key, PlayerBodyProfile.Skin skin, OutfitItem item, string colour, string slot)
        {
            Key = key;
            Skin = skin;
            Item = item;
            Colour = colour ?? "";
            Slot = slot;
        }
    }

    private static readonly Dictionary<string, Texture> taken = new Dictionary<string, Texture>();
    // The pictures made here (taken, or read from the player's own files), destroyed when replaced; a baked one is an
    // asset of the game and is left alone.
    private static readonly HashSet<Texture> owned = new HashSet<Texture>();
    private static readonly HashSet<string> asked = new HashSet<string>();
    private static OutfitIcons current;
    private readonly Queue<Ask> waiting = new Queue<Ask>();
    private OutfitStudio studio;
    // What taking pictures has cost since the queue last ran dry, for the console.
    private int batchCount;
    private double batchTotal, batchWorst;
    private string batchWorstKey;

    /// <summary>Raised whenever a picture is ready, so the wardrobe can put it in.</summary>
    public static event Action Taken;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        foreach (var picture in owned) Discard(picture);
        owned.Clear();
        taken.Clear();
        asked.Clear();
        Taken = null;
        current = null;
    }

    /// <summary>The picture of an item in a colour set for this character, or null until it has been taken (asked for then).</summary>
    public static Texture OfItem(PlayerBodyProfile.Skin skin, OutfitItem item, string colour)
    {
        if (skin == null || item == null) return null;
        colour = FirstIsBlank(skin, item, colour);
        string key = OutfitPictures.ItemKey(skin.Id, item.id, colour);
        return Get(key, () => new Ask(key, skin, item, colour, null));
    }

    /// <summary>The picture of the character's own piece in a slot, in its colour set, or null until taken (asked for then).</summary>
    public static Texture OfOwn(PlayerBodyProfile.Skin skin, string slot, SkinVariant variant)
    {
        if (skin == null || string.IsNullOrEmpty(slot) || !OutfitCatalog.HasOwnPiece(skin.Id, slot)) return null;
        string colour = variant != null ? variant.Id : "";
        string key = OutfitPictures.OwnKey(skin.Id, slot, colour);
        return Get(key, () => new Ask(key, skin, null, colour, slot));
    }

    // An item's first colour set is its picture with no colour written: the one baked into the game.
    private static string FirstIsBlank(PlayerBodyProfile.Skin skin, OutfitItem item, string colour)
    {
        if (string.IsNullOrEmpty(colour)) return "";
        var entry = OutfitCatalog.Model(item.id, skin.Id);
        return entry == null || entry.Variants.Count == 0 || entry.VariantOf(colour) == entry.Variants[0] ? "" : colour;
    }

    private static Texture Get(string key, Func<Ask> ask)
    {
        if (taken.TryGetValue(key, out var picture) && Usable(picture)) return picture;
        // A picture baked into the game or the player's own first; one to take is asked for once.
        if (!asked.Contains(key))
        {
            var file = OutfitPictures.Load(key, out bool made);
            if (file != null)
            {
                Keep(key, file, made);
                return file;
            }
        }
        if (!Application.isPlaying || !asked.Add(key)) return null;
        if (current == null) current = new GameObject("Outfit Icons").AddComponent<OutfitIcons>();
        current.waiting.Enqueue(ask());
        return null;
    }

    private static bool Usable(Texture picture) => picture != null && (!(picture is RenderTexture film) || film.IsCreated());

    private static void Keep(string key, Texture picture, bool made)
    {
        if (taken.TryGetValue(key, out var old) && old != null && old != picture && owned.Remove(old)) Discard(old);
        taken[key] = picture;
        if (made) owned.Add(picture);
    }

    private static void Discard(Texture picture)
    {
        if (picture == null) return;
        if (picture is RenderTexture film) film.Release();
        Destroy(picture);
    }

    // ---- Taking pictures, one a frame ----

    private void LateUpdate()
    {
        if (waiting.Count == 0)
        {
            Report();
            return;
        }
        if (ShadersCompiling) return;
        var ask = waiting.Dequeue();
        var watch = Stopwatch.StartNew();
        var picture = Take(ask, out bool spoiled);
        watch.Stop();
        if (spoiled)
        {
            waiting.Enqueue(ask);
            return;
        }
        batchCount++;
        batchTotal += watch.Elapsed.TotalMilliseconds;
        if (watch.Elapsed.TotalMilliseconds > batchWorst)
        {
            batchWorst = watch.Elapsed.TotalMilliseconds;
            batchWorstKey = ask.Key;
        }
        // Nothing of it to photograph: it stays without a picture and isn't asked for again.
        if (picture == null) return;
        Keep(ask.Key, picture, true);
        Taken?.Invoke();
    }

    // Stood, framed as its piece is (OutfitPictures.FramingOf) and photographed in the studio; spoiled when the editor
    // was compiling shaders meanwhile.
    private Texture Take(Ask ask, out bool spoiled)
    {
        spoiled = false;
        if (studio == null) studio = new OutfitStudio();
        if (!studio.Stand(ask.Skin, ask.Item, ask.Colour, ask.Slot)) return null;
        studio.Frame(OutfitPictures.FramingOf(OutfitPictures.PieceOf(ask.Key)));
        var picture = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { name = "Outfit Icon " + ask.Key };
        picture.Create();
        bool shot = studio.Shoot(picture);
        studio.Clear();
        if (shot && !ShadersCompiling) return picture;
        spoiled = shot;
        Discard(picture);
        return null;
    }

    // Once the queue runs dry: what the batch cost, in the editor and development builds (the user asked whether baked
    // pictures would load faster; this is the measure of what they save).
    private void Report()
    {
        if (batchCount == 0) return;
        if (Debug.isDebugBuild)
            Debug.Log($"[OutfitIcons] 옷 사진 {batchCount}장을 그 자리에서 찍었어요: 모두 {batchTotal:0} ms, 한 장 평균 {batchTotal / batchCount:0.0} ms, "
                + $"가장 오래 {batchWorst:0.0} ms ({batchWorstKey}). 구워 둔 사진이 있는 옷은 찍지 않고 바로 나와요(Tools → Characters → 옷장 사진 모두 굽기).");
        batchCount = 0;
        batchTotal = batchWorst = 0d;
        batchWorstKey = null;
    }

    private static bool ShadersCompiling
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.ShaderUtil.anythingCompiling;
#else
            return false;
#endif
        }
    }

    // ---- Taking a piece's pictures again ----

    /// <summary>
    /// Takes a piece's pictures again framed as <paramref name="framing"/> says, and keeps them and the framing where they
    /// are found first (OutfitPictures: the editor's are written into the project and ship, a build's stay on this PC):
    /// an item in its first colour set, a character's own piece (no item, its <paramref name="slot"/>) in each of its
    /// colour sets. The piece's pictures in other colours are taken again, framed the same, when next asked for. Returns
    /// how many were kept.
    /// </summary>
    public static int Reshoot(PlayerBodyProfile.Skin skin, OutfitItem item, string slot, OutfitFraming framing)
    {
        if (skin == null || (item == null && string.IsNullOrEmpty(slot))) return 0;
        string piece = item != null ? OutfitPictures.ItemPiece(skin.Id, item.id) : OutfitPictures.OwnPiece(skin.Id, slot);
        OutfitPictures.SetFraming(piece, framing);
        Forget(piece);
        var colours = new List<string>();
        if (item != null || skin.Variants.Count == 0) colours.Add("");
        else
            foreach (var variant in skin.Variants)
                if (variant != null) colours.Add(variant.Id);
        int kept = 0;
        var studio = new OutfitStudio();
        try
        {
            foreach (string colour in colours)
            {
                if (!studio.Stand(skin, item, colour, slot)) continue;
                studio.Frame(framing);
                var picture = studio.Capture(Size);
                studio.Clear();
                if (picture == null) continue;
                string key = item != null ? OutfitPictures.ItemKey(skin.Id, item.id, colour) : OutfitPictures.OwnKey(skin.Id, slot, colour);
                if (OutfitPictures.Save(key, picture)) kept++;
                Keep(key, picture, true);
            }
        }
        finally
        {
            studio.Dispose();
        }
        Taken?.Invoke();
        return kept;
    }

    // Every picture of a piece, in any colour, is let go: found or taken again when next asked for.
    private static void Forget(string piece)
    {
        var gone = new List<string>();
        foreach (string key in taken.Keys) if (OutfitPictures.PieceOf(key) == piece) gone.Add(key);
        foreach (string key in asked) if (OutfitPictures.PieceOf(key) == piece && !gone.Contains(key)) gone.Add(key);
        foreach (string key in gone)
        {
            if (taken.TryGetValue(key, out var picture) && picture != null && owned.Remove(picture)) Discard(picture);
            taken.Remove(key);
            asked.Remove(key);
        }
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        studio?.Dispose();
        studio = null;
        // Asked-for pictures this copy never took are asked for again by whoever comes next.
        foreach (var ask in waiting) asked.Remove(ask.Key);
        waiting.Clear();
    }
}
