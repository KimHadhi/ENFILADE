using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Dresses a freshly made model of a character in an outfit (<see cref="OutfitLook"/>), the way VRChat outfits go on with
/// Modular Avatar's Merge Armature: every bone of an outfit model that shares its name with one of the character's moves
/// under that bone, staying where it is, so from then on it follows it, and the outfit's own extra bones (a skirt's, a
/// coat's tails) come along beneath. Nothing is baked per character in the editor, because the user means to let players
/// bring their own characters: a model goes on any body whose bones carry the names it was made for. A model with no
/// bones of its own (a rigid mask) follows its slot's bone.
/// Pieces cut from one model (a BOOTH set's beret, jacket and skirt, Outfits.csv) share one copy of it, so its bones are
/// there once, each piece showing its own meshes in its own colour. Then the character's own pieces in the filled slots
/// switch off (and the underwear they covered on), the items' shape keys go on (OutfitFits.csv), and the clothes take the
/// body's shape keys of the same names, as Modular Avatar's Blendshape Sync does.
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

        // A piece's renderers: those it names, or everything no other piece took (minus what it drops).
        public List<Renderer> Take(OutfitItem item, ICollection<string> drop)
        {
            var wanted = new HashSet<string>(item.PartNames, StringComparer.OrdinalIgnoreCase);
            var found = new List<Renderer>();
            foreach (var renderer in Renderers)
            {
                bool mine = wanted.Count > 0 ? wanted.Contains(renderer.name) : !drop.Contains(renderer.name);
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
    internal static Dressing Dress(GameObject model, GameObject source, PlayerBodyProfile.Skin skin, OutfitLook look)
    {
        var dressing = new Dressing();
        if (model == null || skin == null || look == null || look.IsEmpty) return dressing;
        // The character's own, before anything is added: outfits merge only onto its bones and switch off only its pieces,
        // and its shape keys as it was made are what the clothes follow.
        var own = model.GetComponentsInChildren<Renderer>(true);
        var bodyShapes = ShapesOf(own);
        var animator = model.GetComponentInChildren<Animator>();
        Skeleton skeleton = null;
        var picks = Picks(look, skin.Id);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, item, _) in picks) claimed.UnionWith(item.AlsoSlots);
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
                skeleton ??= new Skeleton(animator, model.transform, own);
                copy = Place(item, slot, model.transform, source, skeleton, animator, own, skin.Id);
                if (copy == null) continue;
                placed[item.ModelId] = copy;
                dressing.Models.Add(item.ModelId);
            }
            var drop = new HashSet<string>(OutfitCatalog.Split(OutfitCatalog.FitOf(item.id, skin.Id)?.drop), StringComparer.OrdinalIgnoreCase);
            var renderers = copy.Take(item, drop);
            if (renderers.Count == 0)
            {
                Warn($"{item.id}: {copy.Entry.Model.name}에 '{item.parts}' 렌더러가 없습니다.");
                continue;
            }
            SkinVariant.WearOn(renderers, copy.Entry.VariantOf(colour));
            worn.Add(new Worn(slot, item, renderers));
            dressing.Items.Add(item.id);
            emptied.Add(slot.id);
            emptied.UnionWith(item.AlsoSlots);
        }
        foreach (var copy in placed.Values) copy.HideRest();
        // Slots an item covers (a full mask covers the glasses): whatever is worn there goes out of sight too.
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in worn) covered.UnionWith(piece.Item.HiddenSlots);
        emptied.UnionWith(covered);
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
        TakeOff(own, skin.Id, emptied);
        foreach (var piece in worn) Shape(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), OutfitCatalog.FitOf(piece.Item.id, skin.Id)?.shapes);
        Follow(bodyShapes, worn);
        return dressing;
    }

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
    private static Placed Place(OutfitItem item, OutfitSlot slot, Transform root, GameObject source, Skeleton skeleton,
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
            if (CopiesOwn(renderer, own))
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
        if (Merge(instance.transform, skeleton) > 0) return copy;
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

    // Each transform of the outfit that the character's skeleton has too goes under the character's, staying where it
    // is. Parents come before their children, so a bone under one already moved is still where it was made.
    private static int Merge(Transform instance, Skeleton skeleton)
    {
        int merged = 0;
        foreach (var part in instance.GetComponentsInChildren<Transform>(true))
        {
            if (part == instance || part.GetComponent<Renderer>() != null) continue;
            var twin = skeleton.Find(part.name);
            if (twin == null) continue;
            part.SetParent(twin, true);
            merged++;
        }
        return merged;
    }

    // The character's own mesh shipped inside an outfit's file: the same name and the same vertex count.
    private static bool CopiesOwn(Renderer renderer, Renderer[] own)
    {
        var mesh = MeshOf(renderer);
        if (mesh == null) return false;
        foreach (var mine in own)
            if (string.Equals(mine.name, renderer.name, StringComparison.OrdinalIgnoreCase) && MeshOf(mine) is Mesh theirs
                && theirs.vertexCount == mesh.vertexCount) return true;
        return false;
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    // The character's own pieces in these slots go (CharacterParts.csv): their renderers switch off and their hiding
    // keys go on. A slot left with none of its pieces showing, whichever slot took them, shows what they covered.
    private static void TakeOff(Renderer[] own, string skinId, HashSet<string> slots)
    {
        if (slots.Count == 0) return;
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
        if (off.Count == 0) return;
        foreach (var renderer in own)
            if (off.Contains(renderer.name)) renderer.enabled = false;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.under)) continue;
            bool bare = false;
            foreach (string piece in OutfitCatalog.Split(row.parts))
            {
                bare = off.Contains(piece);
                if (!bare) break;
            }
            if (!bare) continue;
            var under = new HashSet<string>(OutfitCatalog.Split(row.under), StringComparer.OrdinalIgnoreCase);
            foreach (var renderer in own)
            {
                if (!under.Contains(renderer.name)) continue;
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
            }
        }
    }

    // Shape keys "name=weight|name" on every mesh that has them: the body's shrink keys under clothes, a heel key on the
    // feet, a shirt's own key to tuck under a coat.
    private static void Shape(IEnumerable<SkinnedMeshRenderer> meshes, string shapes)
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
            foreach (var mesh in meshes)
            {
                int index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(name) : -1;
                if (index >= 0) mesh.SetBlendShapeWeight(index, Mathf.Clamp(weight, 0f, 100f));
            }
        }
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
    /// The character's bones by name: the hips and everything under them, not the meshes. Names match exactly first, then
    /// loosely, since "Upper_Leg.L" and "UpperLeg_L" are one bone to the people who name them by hand.
    /// </summary>
    private sealed class Skeleton
    {
        private readonly Dictionary<string, Transform> exact = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> loose = new Dictionary<string, Transform>(StringComparer.Ordinal);

        internal Skeleton(Animator animator, Transform root, Renderer[] own)
        {
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var drawn = new HashSet<Transform>();
            foreach (var renderer in own) drawn.Add(renderer.transform);
            // Above the hips sit the root and its "Armature": an outfit's copies of those stay on the outfit.
            foreach (var bone in (hips != null ? hips : root).GetComponentsInChildren<Transform>(true))
            {
                if (bone == root || drawn.Contains(bone)) continue;
                exact.TryAdd(bone.name, bone);
                loose.TryAdd(Loose(bone.name), bone);
            }
        }

        internal Transform Find(string name) =>
            exact.TryGetValue(name, out var bone) || loose.TryGetValue(Loose(name), out bone) ? bone : null;

        private static string Loose(string name)
        {
            var key = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
            return key.ToString();
        }
    }
}
