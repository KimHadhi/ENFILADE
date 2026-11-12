using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

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
internal static class OutfitDresser
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

    // What hangs from the head under the face pieces worn (OutfitSlot.FitsFace): the character's own head meshes (hair,
    // ears, the face) and items worn in the head's slots. A piece the face piece covers whole (FaceFit.csv hide, as
    // SkinCover measures it: a hairpin under the mask) goes out of sight; the rest is pressed in under it where it stands
    // out through it (UnderwearFit.Press), on copies of its meshes. Measured on the FBX files (2026-11-13), a few hair
    // vertices stood out through the welding mask on each character, nothing of a face.
    private static void UnderFace(Transform root, string skinId, Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered)
    {
        var onFace = new List<Renderer>();
        foreach (var piece in worn)
            if (piece.Slot.FitsFace && !covered.Contains(piece.Slot.id)) onFace.AddRange(piece.Renderers);
        if (onFace.Count == 0) return;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        var under = new List<Renderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned && renderer.enabled && renderer.gameObject.activeInHierarchy && HeadMeshes.OnHead(skinned, head))
                under.Add(renderer);
        foreach (var piece in worn)
        {
            if (piece.Slot.FitsFace || covered.Contains(piece.Slot.id) || !string.Equals(piece.Slot.group, "head", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var renderer in piece.Renderers)
                if (renderer is SkinnedMeshRenderer && renderer.enabled) under.Add(renderer);
        }
        var showing = new List<Renderer>();
        float hide = FaceFit.HideShare;
        foreach (var renderer in under)
        {
            if (SkinCover.Covers(root, skinId, renderer, onFace) >= hide) renderer.enabled = false;
            else showing.Add(renderer);
        }
        UnderwearFit.Press(root, showing, onFace);
    }

    // The character's own underwear in the underwear slots nothing else took (CharacterParts.csv rows of those slots):
    // on the skin whatever else is worn. Its maker kept it switched off under the clothes it came in (Mayo's bra and
    // panties, Ichigo's), so it is switched on here, and the rest of the outfit decides how much of it shows
    // (UnderwearFit). Before Replace: an item may name a piece of it that it takes the place of.
    private static void WearUnderwear(Transform root, Renderer[] own, string skinId, HashSet<string> emptied)
    {
        foreach (var row in OutfitCatalog.PartsOf(skinId))
        {
            if (emptied.Contains(row.slot) || OutfitCatalog.Slot(row.slot) is not { under: true }) continue;
            var names = new HashSet<string>(OutfitCatalog.Split(row.parts), StringComparer.OrdinalIgnoreCase);
            var wearing = new List<Renderer>();
            foreach (var renderer in own)
                if (renderer != null && names.Contains(renderer.name)) wearing.Add(renderer);
            SwitchOn(wearing, root);
        }
    }

    // Renderers switched on with their objects, and those objects' parents up to (not including) <paramref name="top"/>.
    private static void SwitchOn(List<Renderer> renderers, Transform top)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            for (var part = renderer.transform; part != null && part != top; part = part.parent)
            {
                if (part.gameObject.activeSelf) continue;
                part.gameObject.SetActive(true);
            }
            renderer.enabled = true;
        }
    }

    // The underwear on the body, each piece's renderers together (an item, or the character's own in its slot), and what
    // is worn over it: every other piece drawn solid but the bare ones (hair, a tail) and those on their way off. A
    // character with no rows has only its items told apart; its own meshes are its body.
    private static (List<List<Renderer>> Under, List<Renderer> Over) Layers(Renderer[] own, string skinId, List<Worn> worn,
        HashSet<string> covered, List<Renderer> leaving)
    {
        var under = new List<List<Renderer>>();
        var over = new List<Renderer>();
        var seen = new HashSet<Renderer>();
        var gone = new HashSet<Renderer>(leaving);
        foreach (var row in OutfitCatalog.PartsOf(skinId))
        {
            var slot = OutfitCatalog.Slot(row.slot);
            if (slot == null || SkinCover.IsBare(slot.id)) continue;
            var names = new HashSet<string>(OutfitCatalog.Split(row.parts), StringComparer.OrdinalIgnoreCase);
            var piece = new List<Renderer>();
            foreach (var renderer in own)
                if (renderer != null && names.Contains(renderer.name) && Drawn(renderer, gone) && seen.Add(renderer)) piece.Add(renderer);
            if (piece.Count == 0) continue;
            if (slot.under) under.Add(piece);
            else over.AddRange(piece);
        }
        foreach (var picked in worn)
        {
            if (covered.Contains(picked.Slot.id) || SkinCover.IsBare(picked.Slot.id)) continue;
            var piece = new List<Renderer>();
            foreach (var renderer in picked.Renderers)
                if (Drawn(renderer, gone) && seen.Add(renderer)) piece.Add(renderer);
            if (piece.Count == 0) continue;
            if (picked.Slot.under) under.Add(piece);
            else over.AddRange(piece);
        }
        return (under, over);
    }

    private static bool Drawn(Renderer renderer, HashSet<Renderer> gone) =>
        renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !gone.Contains(renderer);

    // The look's items that exist, sit in their own slot and were made for this character, in the slots' order. Anything
    // else (an item this PC doesn't have, or one for another character) leaves its slot as the character has it.
    private static List<(OutfitSlot Slot, OutfitItem Item, string Colour)> Picks(OutfitLook look, string skinId)
    {
        var found = new List<(OutfitSlot, OutfitItem, string)>();
        foreach (var slot in OutfitCatalog.Slots)
        {
            if (!look.TryGet(slot.id, out var pick) || pick.IsOff) continue;
            var item = OutfitCatalog.Item(pick.Item);
            if (item == null || !string.Equals(item.slot, slot.id, StringComparison.OrdinalIgnoreCase)) continue;
            if (OutfitCatalog.Model(item.id, skinId)?.Model == null) continue;
            found.Add((slot, item, pick.Colour));
        }
        return found;
    }

    // Makes the model on the character and joins it to the body.
    private static Placed Place(OutfitItem item, OutfitSlot slot, Transform root, GameObject source, OutfitSkeleton skeleton,
        Animator animator, Renderer[] own, string skinId)
    {
        var entry = OutfitCatalog.Model(item.id, skinId);
        var instance = Object.Instantiate(entry.Model, root, false);
        instance.name = "Outfit (" + item.ModelId + ")";
        Align(instance.transform, entry.Model.transform, source != null ? source.transform : null);
        // An outfit's file may come with an Animator (imported as a humanoid of its own): only the character's may play.
        foreach (var extra in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(extra);
        int layer = root.gameObject.layer;
        foreach (var part in instance.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;

        var copy = new Placed { Entry = entry, Instance = instance };
        bool skinned = false;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            // Outfit files often carry the avatar's own body to fit the clothes on; the character has that already.
            if (CopiesOwn(renderer, root, own, root))
            {
                renderer.enabled = false;
                continue;
            }
            if (renderer is SkinnedMeshRenderer mesh)
            {
                mesh.updateWhenOffscreen = true;
                skinned = true;
            }
            copy.Renderers.Add(renderer);
        }
        if (copy.Renderers.Count == 0)
        {
            Warn($"{item.ModelId}: {entry.Model.name}에 입힐 메시가 없습니다.");
            Object.Destroy(instance);
            return null;
        }
        // A piece made for anyone (an accessory sold for any avatar) brings bones of its own, matched to none of the
        // character's by name, and hangs on the slot's spots measured for this character: the Ene earrings on the lobes.
        if (entry.ForAnyone)
        {
            // A rigid piece for the face (the welding mask) goes onto this face as measured from its own meshes, then
            // follows the head: no rows per character (FaceFit).
            if (slot.FitsFace)
            {
                var face = FaceFit.Measure(root, animator, own);
                if (face == null || !FaceFit.Put(instance.transform, root, face, slot.Anchor(animator)))
                    Warn($"{item.ModelId}: {skinId}의 얼굴(머리 뼈와 읽을 수 있는 머리 메시)을 재지 못해 {slot.id} 칸에 맞추지 못했습니다.");
                return copy;
            }
            var spot = OutfitSpot.Of(slot.id, skinId);
            if (spot == null || !spot.Hang(instance.transform, root, slot.Anchor(animator)))
                Warn($"{item.ModelId}: {skinId}의 {slot.id} 칸 자리(OutfitSpots.csv)나 뼈 '{slot.bone}'가 없어 몸을 따라가지 않습니다.");
            return copy;
        }
        if (Merge(instance.transform, skeleton, item.ModelId) > 0) return copy;
        // Nothing to follow bone by bone: a rigid piece, made standing on the character, follows its slot's bone.
        if (skinned) Warn($"{item.ModelId}: {entry.Model.name}의 뼈 이름이 캐릭터 뼈와 하나도 맞지 않아 {slot.bone}에 통째로 붙입니다.");
        var anchor = slot.Anchor(animator);
        if (anchor != null) instance.transform.SetParent(anchor, true);
        else Warn($"{item.ModelId}: {slot.id} 칸의 뼈 '{slot.bone}'를 찾지 못해 몸을 따라가지 않습니다.");
        return copy;
    }

    // Both files stood their roots at one origin, so the outfit's root goes where it stood against the character's.
    private static void Align(Transform instance, Transform itemSource, Transform characterSource)
    {
        var character = characterSource != null
            ? Matrix4x4.TRS(characterSource.localPosition, characterSource.localRotation, characterSource.localScale)
            : Matrix4x4.identity;
        var item = Matrix4x4.TRS(itemSource.localPosition, itemSource.localRotation, itemSource.localScale);
        var local = character.inverse * item;
        instance.localPosition = local.GetColumn(3);
        instance.localRotation = local.rotation;
        instance.localScale = local.lossyScale;
    }

    // How far a twin may stand from the character's bone with no correction to the bind poses (metres, degrees, share of
    // its size): the two files' skeletons, made from one avatar, agree to rounding. Further apart, Bind corrects them;
    // whether it is her bone at all is OutfitSkeleton's call.
    private const float SameSpot = .0005f, SameTurn = .1f, SameSize = .001f;

    // Meshes whose bind poses took in where an outfit's bones stood apart from the character's, kept for every later
    // dressing (the wardrobe's preview dresses the model again on every try).
    private static readonly Dictionary<(Mesh, int), Mesh> rebound = new Dictionary<(Mesh, int), Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        rebound.Clear();
        movedBy.Clear();
    }

    // An outfit's file carries a copy of the character's skeleton (every BOOTH outfit does). Each bone of it the character
    // has too (its twin: her name, and her place unless it is a humanoid bone, OutfitSkeleton) is the character's own: the
    // outfit's meshes are bound straight to the character's bone, as Modular Avatar binds them, and whatever of the
    // outfit's own hangs from a twin (a coat's tails, a belt, a ribbon's chain) goes under the character's bone, staying
    // where it is.
    // The twins used to go under the character's bones themselves. Magica takes every transform under a cloth chain's
    // root for a particle of that chain, so a twin under Mayo's breast swung on its own inside her breast's spring and
    // the chest belt bound to it flapped apart from her (the user, 2026-11-12: "조끼랑 가슴이랑 서로 맞물리는 게 아니라
    // 걍 서로 안 맞아서 흐물거리고 휘적거려"). Bound to her bones, the belt moves exactly as they do, hair and skirt chains
    // likewise. The twins stay behind in the outfit, unused, renamed after it ("Hips [white-board]"): a second "Hips"
    // anywhere under the model makes the humanoid ambiguous ("Ambiguous Transform 'Armature/Hips' and 'Armature/Hips/Hips'
    // found in hierarchy for human bone 'Hips'") and Unity lets go of the whole avatar (0.8.115). Nothing finds them by
    // name: the sway rows name only an outfit's own bones.
    // A bone that only shares a name with one of hers is the outfit's own, like any she lacks. Chocolate rice's mirrored
    // side locks hang from "Side_root" and "R_Side" (or "L_Side"), the names of Mayo's own lock bones, 1.3 to 6 cm from
    // them: taken for hers by name alone, the mirrored lock rode the chain of the lock already on that side, shaped for
    // another lock (2026-11-12). Such a namesake keeps its chain and sways on the outfit's own row, and is renamed after
    // the model too (OutfitSkeleton.Mark; BoothImport names it so in that row): the cloth rows find bones by name on the
    // dressed body and would find hers first. Returns how many twins there were; none is a rigid piece.
    private static int Merge(Transform instance, OutfitSkeleton skeleton, string mark)
    {
        var twins = new Dictionary<Transform, Transform>();
        var namesakes = new List<Transform>();
        skeleton.Sort(instance, twins, namesakes);
        foreach (var namesake in namesakes) namesake.name = OutfitSkeleton.Mark(namesake.name, mark);
        if (twins.Count == 0) return 0;
        // Gathered before anything moves: a mesh's object may hang from a twin too.
        var meshes = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var hanging = new List<(Transform Part, Transform Under)>();
        foreach (var pair in twins)
            foreach (Transform child in pair.Key)
                if (!twins.ContainsKey(child)) hanging.Add((child, pair.Value));
        foreach (var (part, under) in hanging)
        {
            part.SetParent(under, true);
            // The character's cloth rows leave it to the outfit's: under a breast or a skirt bone it would join that
            // row's particles too (ClothBones.Collect).
            if (!part.TryGetComponent<OutfitBone>(out _)) part.gameObject.AddComponent<OutfitBone>();
        }
        foreach (var mesh in meshes) Bind(mesh, twins);
        foreach (var twin in twins.Keys) twin.name = OutfitSkeleton.Mark(twin.name, mark);
        return twins.Count;
    }

    // Points a mesh's bones at the character's. Where the outfit's bone stood apart from the character's (two skeletons
    // that don't quite agree), the mesh's bind poses take the difference in, so nothing moves, on a copy of the mesh.
    private static void Bind(SkinnedMeshRenderer renderer, Dictionary<Transform, Transform> twins)
    {
        var bones = renderer.bones;
        Matrix4x4[] apart = null;
        bool changed = false;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null || !twins.TryGetValue(bones[i], out var own)) continue;
            if (!SameBone(bones[i], own))
            {
                apart ??= Identities(bones.Length);
                apart[i] = own.worldToLocalMatrix * bones[i].localToWorldMatrix;
            }
            bones[i] = own;
            changed = true;
        }
        if (renderer.rootBone != null && twins.TryGetValue(renderer.rootBone, out var root)) renderer.rootBone = root;
        if (!changed) return;
        if (apart != null && renderer.sharedMesh != null) renderer.sharedMesh = Rebound(renderer.sharedMesh, apart);
        renderer.bones = bones;
    }

    private static bool SameBone(Transform outfit, Transform own) =>
        (outfit.position - own.position).sqrMagnitude < SameSpot * SameSpot
        && Quaternion.Angle(outfit.rotation, own.rotation) < SameTurn
        && Mathf.Abs(Mathf.Abs(outfit.lossyScale.x) / Mathf.Max(Mathf.Abs(own.lossyScale.x), 1e-6f) - 1f) < SameSize;

    private static Matrix4x4[] Identities(int count)
    {
        var all = new Matrix4x4[count];
        for (int i = 0; i < count; i++) all[i] = Matrix4x4.identity;
        return all;
    }

    // The mesh with each bind pose moved by where its outfit bone stood on the character's: skinned to the character's
    // bone, a vertex lands where the outfit's bone put it.
    private static Mesh Rebound(Mesh mesh, Matrix4x4[] apart)
    {
        if (!mesh.isReadable) return mesh;
        int key = 17;
        foreach (var matrix in apart)
            for (int k = 0; k < 16; k++) key = key * 31 + Mathf.RoundToInt(matrix[k] * 10000f);
        if (rebound.TryGetValue((mesh, key), out var copy) && copy != null) return copy;
        var poses = mesh.bindposes;
        if (poses.Length != apart.Length) return mesh;
        for (int i = 0; i < poses.Length; i++) poses[i] = apart[i] * poses[i];
        copy = Object.Instantiate(mesh);
        copy.name = mesh.name;
        copy.bindposes = poses;
        return rebound[(mesh, key)] = copy;
    }

    /// <summary>
    /// Whether a renderer of an outfit's file is the character's own mesh shipped in it to fit the clothes on: one of
    /// <paramref name="own"/> with its name and vertex count, as large and standing in the same place, each measured in
    /// its model's frame (<paramref name="frame"/>, <paramref name="ownFrame"/>). Name and count alone took a mirrored
    /// copy for the body: Chocolate rice's mirrored side locks are Mayo's own two side locks turned to her other side,
    /// named and counted as hers (MAYO_Hair_Side_3, 737 vertices), so the wardrobe hid the only mesh the piece had and
    /// dropped it ("mayo-side-hair-1에 입힐 메시가 없습니다", the user: "마요 거울 머리 1은 선택하면 뭐 안 바뀌고",
    /// 2026-11-12). A copy's middle agrees to rounding; the mirrored locks' stand 9.5 and 14 cm from their originals. The
    /// bounds are the mesh's own, kept with it, so this costs nothing per try, and a mesh stands where its object puts its
    /// own space whether skinned (the bind poses start there) or not. The wardrobe's pictures ask it too (OutfitIcons).
    /// </summary>
    internal static bool CopiesOwn(Renderer renderer, Transform frame, Renderer[] own, Transform ownFrame)
    {
        var mesh = MeshOf(renderer);
        if (mesh == null) return false;
        Vector3 middle = Middle(renderer, mesh, frame);
        const float near = OutfitSkeleton.SamePlace * OutfitSkeleton.SamePlace;
        foreach (var mine in own)
        {
            if (mine == null || !string.Equals(mine.name, renderer.name, StringComparison.OrdinalIgnoreCase)
                || MeshOf(mine) is not Mesh theirs || theirs.vertexCount != mesh.vertexCount) continue;
            if ((theirs.bounds.size - mesh.bounds.size).sqrMagnitude <= near && (Middle(mine, theirs, ownFrame) - middle).sqrMagnitude <= near)
                return true;
        }
        return false;
    }

    // Where the middle of a mesh stands as its file made it, in its model's frame.
    private static Vector3 Middle(Renderer renderer, Mesh mesh, Transform frame) =>
        frame.InverseTransformPoint(renderer.transform.TransformPoint(mesh.bounds.center));

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    // What of the character the item takes the place of on this character (OutfitFits.csv replaces), or null when it
    // takes its whole slot as pieces do.
    private static string Replaces(OutfitItem item, string skinId)
    {
        string names = OutfitCatalog.FitOf(item.id, skinId)?.replaces;
        return string.IsNullOrWhiteSpace(names) ? null : names;
    }

    // The character's own renderers the worn pieces take the place of go off. Chocolate rice's mirrored side locks put
    // one of Mayo's locks on her other side, right onto the lock already there: a short lock lies along the top fifth of
    // the long one (93% of its vertices within 1 cm of it, measured from the FBX files), so worn beside it the two show
    // through each other. The maker's piece is meant instead of that lock, as its bones say too: its chain carries the
    // names of that lock's. A covered piece is out of sight and takes nothing's place.
    private static void Replace(Renderer[] own, string skinId, List<Worn> worn, HashSet<string> covered)
    {
        HashSet<string> names = null;
        foreach (var piece in worn)
        {
            if (covered.Contains(piece.Slot.id) || Replaces(piece.Item, skinId) is not string replaces) continue;
            names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.UnionWith(OutfitCatalog.Split(replaces));
        }
        if (names == null) return;
        foreach (var renderer in own)
            if (names.Contains(renderer.name)) renderer.enabled = false;
    }

    // The character's own pieces in these slots go (CharacterParts.csv): their renderers switch off and their hiding
    // keys go on, and the body's keys only those pieces held go back to 0. Underwear no longer comes on here once a dress
    // is off: it has slots of its own and is always on (WearUnderwear). Returns the keys let go.
    private static HashSet<string> TakeOff(Renderer[] own, string skinId, HashSet<string> slots)
    {
        var freed = new HashSet<string>(StringComparer.Ordinal);
        if (slots.Count == 0) return freed;
        var rows = OutfitCatalog.PartsOf(skinId);
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skins = new List<SkinnedMeshRenderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned) skins.Add(skinned);
        foreach (var row in rows)
        {
            if (!slots.Contains(row.slot)) continue;
            off.UnionWith(OutfitCatalog.Split(row.parts));
            Shape(skins, row.shapes);
        }
        Release(skins, rows, slots, off, freed);
        if (off.Count == 0) return freed;
        foreach (var renderer in own)
            if (off.Contains(renderer.name)) renderer.enabled = false;
        return freed;
    }

    // BOOTH bodies come with the skin under their stockings and shoes drawn in (Shrink_Knee_L, Shrink_Foot_R) and the
    // feet shaped for the heels, all set on in the prefab, because they never meant those pieces to come off. Taking the
    // stockings off left Mayo's shins hollow (the user: "마요 스타킹 벗는 거 고장남", 2026-11-11), so each piece names
    // the keys it holds (CharacterParts.csv needs) and a key nothing showing holds any more goes to 0. Keys two pieces
    // share stay while either shows: the feet stay drawn in while the shoes are on, bare shins or not.
    private static void Release(List<SkinnedMeshRenderer> skins, List<OutfitCatalog.Part> rows, HashSet<string> slots,
        HashSet<string> off, HashSet<string> freed)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.needs)) continue;
            (Shows(row, slots, off) ? held : freed).UnionWith(OutfitCatalog.Split(row.needs));
        }
        freed.ExceptWith(held);
        foreach (string name in freed) SetShape(skins, name, 0f);
    }

    // A piece shows while any of its renderers is still on; one made only of keys shows while its slot is kept.
    private static bool Shows(OutfitCatalog.Part row, HashSet<string> slots, HashSet<string> off)
    {
        bool any = false;
        foreach (string piece in OutfitCatalog.Split(row.parts))
        {
            any = true;
            if (!off.Contains(piece)) return true;
        }
        return !any && !slots.Contains(row.slot);
    }

    // Shape keys "name=weight|name" on every mesh that has them: the body's shrink keys under clothes, a heel key on the
    // feet, a shirt's own key to tuck under a coat. An item's fit (<paramref name="fit"/>) doesn't stack a draw-in key on
    // skin another draws in already (Stacks).
    private static void Shape(IEnumerable<SkinnedMeshRenderer> meshes, string shapes, bool fit = false)
    {
        if (string.IsNullOrEmpty(shapes)) return;
        foreach (string pair in OutfitCatalog.Split(shapes))
        {
            int equals = pair.IndexOf('=');
            string name = (equals < 0 ? pair : pair.Substring(0, equals)).Trim();
            float weight = 100f;
            if (equals >= 0 && !float.TryParse(pair.Substring(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
            {
                Warn($"셰이프키 '{pair}'의 값을 읽지 못했습니다.");
                continue;
            }
            SetShape(meshes, name, weight, fit && weight > 0f && SkinCover.Hides(name));
        }
    }

    private static void SetShape(IEnumerable<SkinnedMeshRenderer> meshes, string name, float weight, bool unstacked = false)
    {
        foreach (var mesh in meshes)
        {
            int index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(name) : -1;
            if (index < 0 || (unstacked && Stacks(mesh, index))) continue;
            mesh.SetBlendShapeWeight(index, Mathf.Clamp(weight, 0f, 100f));
        }
    }

    // A mesh's draw-in keys (SkinCover.Hides) are ways to hide one stretch of skin, not layers to pile up: the "both
    // sides" key and the one-side keys, or a tights fit drawing the whole leg in (Ichigo's Shrink_Costume_Tights_Fit) and
    // the foot keys under shoes. On together they move that skin twice as far, out through the other side: with her
    // tights on, the Sera Yura shoes' fit put Shrink_Foot and Shrink_Toe on her body over the tights fit, and her heel
    // stood out of the shoes by up to 19 mm at 314 of their vertices; left to the tights fit, 2 vertices, 10 mm (measured
    // from the FBX files, 2026-11-12). So a fit's draw-in key doesn't go on where nearly all the skin it moves is moved by
    // a draw-in key that is on already. Mayo's six foot keys share no skin, so hers all go on as before.
    private const float StackShare = .9f;
    private static readonly Dictionary<(Mesh, int), bool[]> movedBy = new Dictionary<(Mesh, int), bool[]>();
    private static Vector3[] keyDeltas = Array.Empty<Vector3>();

    private static bool Stacks(SkinnedMeshRenderer renderer, int key)
    {
        var mesh = renderer.sharedMesh;
        if (renderer.GetBlendShapeWeight(key) > 0f || !(Moved(mesh, key) is bool[] mine)) return false;
        List<bool[]> others = null;
        for (int k = 0; k < mesh.blendShapeCount; k++)
            if (k != key && renderer.GetBlendShapeWeight(k) > 0f && SkinCover.Hides(mesh.GetBlendShapeName(k)) && Moved(mesh, k) is bool[] other)
                (others ??= new List<bool[]>()).Add(other);
        if (others == null) return false;
        int skin = 0, drawn = 0;
        for (int i = 0; i < mine.Length; i++)
        {
            if (!mine[i]) continue;
            skin++;
            foreach (var other in others)
                if (other[i])
                {
                    drawn++;
                    break;
                }
        }
        return skin > 0 && drawn >= skin * StackShare;
    }

    // Which of a mesh's vertices a key moves (over a millimetre), read once per mesh and key; null where it can't be read.
    private static bool[] Moved(Mesh mesh, int key)
    {
        if (movedBy.TryGetValue((mesh, key), out var known)) return known;
        int frames = mesh.isReadable ? mesh.GetBlendShapeFrameCount(key) : 0;
        if (frames == 0) return movedBy[(mesh, key)] = null;
        if (keyDeltas.Length != mesh.vertexCount) keyDeltas = new Vector3[mesh.vertexCount];
        mesh.GetBlendShapeFrameVertices(key, frames - 1, keyDeltas, null, null);
        var moved = new bool[keyDeltas.Length];
        for (int i = 0; i < moved.Length; i++) moved[i] = keyDeltas[i].sqrMagnitude > 1e-6f;
        return movedBy[(mesh, key)] = moved;
    }

    // The body's keys as dressing left them, for these names: the first of its meshes with the key set, else 0. The same
    // first-set rule as ShapesOf, so keys nothing touched follow exactly as before.
    private static Dictionary<string, float> ShapesNow(Renderer[] own, HashSet<string> names)
    {
        var shapes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
            var mesh = skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!names.Contains(name)) continue;
                float weight = skinned.GetBlendShapeWeight(i);
                if (!shapes.TryGetValue(name, out float found) || (found == 0f && weight != 0f)) shapes[name] = weight;
            }
        }
        return shapes;
    }

    // The body's shape keys as its maker set them (a fuller chest, wider hips), by name.
    private static Dictionary<string, float> ShapesOf(Renderer[] own)
    {
        var shapes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
            var mesh = skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = skinned.GetBlendShapeWeight(i);
                if (weight != 0f) shapes.TryAdd(mesh.GetBlendShapeName(i), weight);
            }
        }
        return shapes;
    }

    // Clothes made for a body carry its body-shape keys (Breast_Big, Hip_Big, Legs_Slim) and take the body's values, as
    // Modular Avatar's Blendshape Sync makes them in VRChat: a jacket made for the flat chest fits the fuller one too.
    private static void Follow(Dictionary<string, float> body, List<Worn> worn)
    {
        if (body.Count == 0) return;
        foreach (var piece in worn)
            foreach (var renderer in piece.Renderers)
            {
                if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
                var mesh = skinned.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    if (body.TryGetValue(mesh.GetBlendShapeName(i), out float weight)) skinned.SetBlendShapeWeight(i, weight);
            }
    }

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
