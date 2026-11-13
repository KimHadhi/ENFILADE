using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses a freshly made model of a character in an outfit (<see cref="OutfitLook"/>), the way VRChat outfits go on with
/// Modular Avatar's Merge Armature: the outfit's meshes are bound to the character's bones wherever the outfit's skeleton
/// has the same bone (its name, and its place unless it is a humanoid bone: <see cref="OutfitSkeleton"/>), and the
/// outfit's own extra bones (a skirt's, a coat's tails, a mirrored side lock's) go under the character's bone they hung
/// from, so from then on they follow it (Merge). Nothing is baked per character in the editor, because the user means to let players
/// bring their own characters: a model goes on any body whose bones carry the names it was made for. A model with no
/// bones of its own (a rigid mask) follows its slot's bone. A model made for anyone (an accessory sold for any avatar, a
/// pair of earrings) hangs on its slot's spots measured for each character (OutfitSpot) and follows the slot's bone.
/// Pieces cut from one model (a BOOTH set's beret, jacket and skirt, Outfits.csv) share one copy of it, so its bones are
/// there once, each piece showing its own meshes in its own colour. Then the character's own pieces in the filled slots
/// switch off, or only the pieces an item names that it takes the place of (a mirrored side lock swaps one lock, not all
/// the hair), the items' shape keys go on (OutfitFits.csv), and the clothes take the body's shape keys of the same names,
/// as Modular Avatar's Blendshape Sync does. Underwear has slots of its own (OutfitSlot.under) and is always on, the
/// character's own unless an item is picked: the rest of the outfit decides only how much of it shows (UnderwearFit).
/// The model must stand as it was made, before any clip plays, because the bones are matched in that pose.
/// </summary>
internal static partial class OutfitDresser
{
    /// <summary>What dressing put on, for the body to keep.</summary>
    internal sealed class Dressing
    {
        /// <summary>Renderers seen in first person only with the player's own head shown (a helmet, earrings).</summary>
        public readonly List<Renderer> HeadParts = new List<Renderer>();
        /// <summary>Renderers never seen in first person, only their shadows (a mask, glasses).</summary>
        public readonly List<Renderer> FaceParts = new List<Renderer>();
        /// <summary>The ids of the items on and of their models: their rows in Cloth.csv swing their loose parts.</summary>
        public readonly HashSet<string> Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Each item put on and the renderers it shows: the wardrobe's try-on and slot marks find it here.</summary>
        public readonly Dictionary<string, List<Renderer>> Pieces = new Dictionary<string, List<Renderer>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The leaving slot's piece (Dress leaving), drawn over the body as it will be without it.</summary>
        public readonly List<Renderer> Leaving = new List<Renderer>();
    }

    private readonly struct Worn
    {
        public readonly OutfitSlot Slot;
        public readonly OutfitItem Item;
        public readonly List<Renderer> Renderers;

        public Worn(OutfitSlot slot, OutfitItem item, List<Renderer> renderers)
        {
            Slot = slot;
            Item = item;
            Renderers = renderers;
        }
    }

    // One copy of an outfit model on the body, shared by the pieces cut from it.
    private sealed class Placed
    {
        public OutfitLibrary.Entry Entry;
        public GameObject Instance;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        private readonly HashSet<Renderer> taken = new HashSet<Renderer>();

        // A piece's renderers: those it names, or everything no other piece of the model names or took (minus what it
        // drops). Named by another piece, a renderer stays out even while that piece isn't worn: the Banshaku shirt
        // brings its bikini no more, the bikini is an underwear item of its own (2026-11-12).
        public List<Renderer> Take(OutfitItem item, ICollection<string> drop)
        {
            var wanted = new HashSet<string>(item.PartNames, StringComparer.OrdinalIgnoreCase);
            var others = wanted.Count == 0 ? OutfitCatalog.NamedByOthers(item) : null;
            var found = new List<Renderer>();
            foreach (var renderer in Renderers)
            {
                bool mine = wanted.Count > 0 ? wanted.Contains(renderer.name) : !drop.Contains(renderer.name) && !others.Contains(renderer.name);
                if (mine && taken.Add(renderer)) found.Add(renderer);
            }
            return found;
        }

        public void HideRest()
        {
            foreach (var renderer in Renderers)
                if (!taken.Contains(renderer)) renderer.enabled = false;
        }
    }

    private static readonly HashSet<string> warned = new HashSet<string>();

    /// <param name="model">A new, unposed instance of <paramref name="skin"/>'s model.</param>
    /// <param name="source">The model asset it was made from: the outfits' roots go where they stood against its root.</param>
    /// <param name="leaving">
    /// A slot whose piece is on its way off (the wardrobe's 벗기 preview): it is put on and drawn as the look has it, but
    /// the body is dressed as it will be once the piece is gone. Its skin comes back out, what it covered shows, and it
    /// holds nothing (Dressing.Leaving lists it, to draw see-through over that body). The preview once drew the piece
    /// see-through over the body as it was, its skin still drawn in, and the legs under Mayo's stockings showed hollow
    /// (the user, 2026-11-12: "이런 문제가 발생한 게 벌써 두 번 째잖아"): now a preview and the real thing are dressed by
    /// the same rule, here.
    /// </param>
    internal static Dressing Dress(GameObject model, GameObject source, PlayerBodyProfile.Skin skin, OutfitLook look, string leaving = null)
    {
        var dressing = new Dressing();
        var going = string.IsNullOrEmpty(leaving) ? null : OutfitCatalog.Slot(leaving);
        if (model == null || skin == null || look == null || (look.IsEmpty && going == null)) return dressing;
        // A look from before an item moved slot (the bikini from the vest to the underwear) finds it in its new one.
        look = look.Settled();
        // The character's own, before anything is added: outfits merge only onto its bones and switch off only its pieces,
        // and its shape keys as it was made are what the clothes follow.
        var own = model.GetComponentsInChildren<Renderer>(true);
        // As it came, before anything changes: how covered its skin was meant to be (SkinCover).
        var made = SkinCover.Read(own);
        // The player's body options (BodyShapes.csv) go on first: they are the body as its player shaped it, which the
        // clothes then follow like any key the body has, a key turned down to 0 too.
        var shaped = BodyShapes.Apply(own, skin.Id, look);
        var bodyShapes = ShapesOf(own);
        var animator = model.GetComponentInChildren<Animator>();
        OutfitSkeleton skeleton = null;
        var picks = Picks(look, skin.Id);
        // A leaving one-piece takes no other slot: once it is gone, they are as the look has them.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, item, _) in picks)
            if (slot != going) claimed.UnionWith(item.AlsoSlots);
        var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in OutfitCatalog.Slots)
            if (look.TryGet(slot.id, out var pick) && pick.IsOff && slot.removable) emptied.Add(slot.id);
        var placed = new Dictionary<string, Placed>(StringComparer.OrdinalIgnoreCase);
        var worn = new List<Worn>();
        foreach (var (slot, item, colour) in picks)
        {
            // A one-piece took this slot: what was picked here waits until it comes off.
            if (claimed.Contains(slot.id)) continue;
            if (!placed.TryGetValue(item.ModelId, out var copy))
            {
                skeleton ??= SkeletonOf(animator, model.transform, own);
                copy = Place(item, slot, model.transform, source, skeleton, animator, own, skin.Id);
                if (copy == null) continue;
                placed[item.ModelId] = copy;
                dressing.Models.Add(item.ModelId);
                // The sway BoothImport read from a BOOTH outfit is the version made for this character's (Cloth.csv "model@skin").
                dressing.Models.Add(item.ModelId + OutfitLook.Separator + skin.Id);
            }
            var drop = new HashSet<string>(OutfitCatalog.Split(OutfitCatalog.FitOf(item.id, skin.Id)?.drop), StringComparer.OrdinalIgnoreCase);
            var renderers = copy.Take(item, drop);
            if (renderers.Count == 0)
            {
                Warn($"{item.id}: {copy.Entry.Model.name}에 '{item.parts}' 렌더러가 없습니다.");
                continue;
            }
            SkinVariant.WearOn(renderers, copy.Entry.VariantOf(colour));
            // Underwear sold inside an outfit is often switched off in its file, a VRChat menu toggle (the Sera Yura
            // bikini): picked for an underwear slot, it is meant to be on.
            if (slot.under) SwitchOn(renderers, copy.Instance.transform);
            dressing.Pieces[item.id] = renderers;
            // A piece that names what of the character it takes the place of (OutfitFits.csv replaces) leaves the rest of
            // its slot's own pieces on (Replace, below): Chocolate rice's mirrored side lock swaps one of Mayo's two side
            // locks and leaves the rest of her hair. Leaving, it empties its slot as any piece does: 벗기 takes the slot's
            // own pieces off too.
            if (slot == going || Replaces(item, skin.Id) == null) emptied.Add(slot.id);
            // The leaving item is only drawn: no fit keys, nothing it covers or takes, no sway, no first-person part.
            if (slot == going)
            {
                dressing.Leaving.AddRange(renderers);
                continue;
            }
            worn.Add(new Worn(slot, item, renderers));
            dressing.Items.Add(item.id);
            emptied.UnionWith(item.AlsoSlots);
        }
        foreach (var copy in placed.Values) copy.HideRest();
        // Slots an item covers (a full mask covers the glasses): whatever is worn there goes out of sight too.
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in worn) covered.UnionWith(piece.Item.HiddenSlots);
        emptied.UnionWith(covered);
        // A leaving item under another (glasses under a full mask) was out of sight already: nothing to draw going.
        if (going != null && covered.Contains(going.id))
        {
            foreach (var renderer in dressing.Leaving) renderer.enabled = false;
            dressing.Leaving.Clear();
        }
        // The character's own piece in the leaving slot, if it shows until now: it comes off with its slot, so its skin
        // and what it covered are as they will be, and is drawn again afterwards over that body.
        var leavingOwn = new List<Renderer>();
        if (going != null && !emptied.Contains(going.id))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(skin.Id))
                if (string.Equals(part.slot, going.id, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in own)
                if (names.Contains(renderer.name) && renderer.enabled && renderer.gameObject.activeInHierarchy) leavingOwn.Add(renderer);
            emptied.Add(going.id);
        }
        foreach (var piece in worn)
        {
            if (covered.Contains(piece.Slot.id))
            {
                foreach (var renderer in piece.Renderers) renderer.enabled = false;
                continue;
            }
            var view = piece.Slot.FirstPerson;
            if (view == OutfitSlot.View.Head) dressing.HeadParts.AddRange(piece.Renderers);
            else if (view == OutfitSlot.View.Hide) dressing.FaceParts.AddRange(piece.Renderers);
        }
        var freed = TakeOff(own, skin.Id, emptied);
        WearUnderwear(model.transform, own, skin.Id, emptied);
        Replace(own, skin.Id, worn, covered);
        foreach (var renderer in leavingOwn) renderer.enabled = true;
        dressing.Leaving.AddRange(leavingOwn);
        var meshes = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var piece in worn) Shape(meshes, OutfitCatalog.FitOf(piece.Item.id, skin.Id)?.shapes, true);
        // The clothes follow the body as dressing left it: a key the stockings or shoes held and let go of (a heel) goes
        // down on the socks worn instead too.
        freed.UnionWith(bodyShapes.Keys);
        freed.UnionWith(shaped);
        Follow(ShapesNow(own, freed), worn);
        // Underwear on the skin, under everything else: out of sight where the rest covers it, its own fit keys on where
        // they tuck it under (UnderwearFit). What of it shows covers skin too, so it goes before SkinCover.
        var (under, over) = Layers(own, skin.Id, worn, covered, dressing.Leaving);
        var showing = UnderwearFit.Fit(model.transform, skin.Id, own, under, over);
        // The last word on skin drawn in under clothes: wherever nothing solid covers it any more it comes back out, so no
        // body is left hollow whatever CharacterParts.csv says or leaves out (SkinCover). The clothes take the change.
        var items = new List<Renderer>();
        foreach (var piece in worn)
            if (!covered.Contains(piece.Slot.id)) items.AddRange(piece.Renderers);
        var released = SkinCover.Release(model.transform, skin.Id, own, items, dressing.Leaving, made);
        if (released.Count > 0) Follow(ShapesNow(own, released), worn);
        // Last, with every key where dressing leaves it: underwear still standing out through what is over it goes in under.
        UnderwearFit.Press(model.transform, showing, over);
        // Hair, ears and head ornaments under a piece fitted to the face (the welding mask) likewise go in under it where
        // they stand out through it; one it covers whole goes out of sight.
        UnderFace(model.transform, skin.Id, animator, own, worn, covered);
        return dressing;
    }

    // Meshes whose bind poses took in where an outfit's bones stood apart from the character's, kept for every later
    // dressing (the wardrobe's preview dresses the model again on every try).
    private static readonly Dictionary<(Mesh, int), Mesh> rebound = new Dictionary<(Mesh, int), Mesh>();

    private static readonly Dictionary<(Mesh, int), bool[]> movedBy = new Dictionary<(Mesh, int), bool[]>();
    private static Vector3[] keyDeltas = Array.Empty<Vector3>();

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[Outfits] " + message);
    }

    /// <summary>
    /// The character's bones as outfits join them (OutfitSkeleton): the hips and everything under them, not the objects of
    /// its meshes, and which of them are humanoid bones.
    /// </summary>
    private static OutfitSkeleton SkeletonOf(Animator animator, Transform root, Renderer[] own)
    {
        bool human = animator != null && animator.isHuman;
        var hips = human ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var drawn = new HashSet<Transform>();
        foreach (var renderer in own) drawn.Add(renderer.transform);
        var humanoid = new HashSet<Transform>();
        if (human)
            for (var id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
            {
                var bone = animator.GetBoneTransform(id);
                if (bone != null) humanoid.Add(bone);
            }
        // Above the hips sit the root and its "Armature": an outfit's copies of those stay on the outfit.
        return new OutfitSkeleton(hips != null ? hips : root, root, drawn, humanoid);
    }
}
